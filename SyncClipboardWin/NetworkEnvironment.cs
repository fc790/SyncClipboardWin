using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SyncClipboardWin
{
    public sealed class NetworkSnapshot
    {
        public List<string> Types { get; private set; }
        public List<IPAddress> IPv4Addresses { get; private set; }
        public List<string> Names { get; private set; }

        public NetworkSnapshot()
        {
            Types = new List<string>();
            IPv4Addresses = new List<IPAddress>();
            Names = new List<string>();
        }
    }

    public static class NetworkEnvironment
    {
        public static NetworkSnapshot Capture()
        {
            NetworkSnapshot result = new NetworkSnapshot();

            try
            {
                NetworkInterface[] all = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < all.Length; i++)
                {
                    NetworkInterface nic = all[i];
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    AddUnique(result.Names, nic.Name);
                    AddUnique(result.Names, nic.Description);
                    AddUnique(result.Types, Classify(nic.NetworkInterfaceType));

                    try
                    {
                        UnicastIPAddressInformationCollection addresses =
                            nic.GetIPProperties().UnicastAddresses;
                        foreach (UnicastIPAddressInformation item in addresses)
                        {
                            if (item.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(item.Address))
                            {
                                bool exists = result.IPv4Addresses.Any(
                                    delegate(IPAddress x) { return x.Equals(item.Address); });
                                if (!exists)
                                    result.IPv4Addresses.Add(item.Address);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            string ssid = TryGetWifiSsid();
            AddUnique(result.Names, ssid);
            return result;
        }

        public static bool IsMatch(AppSettings profile, NetworkSnapshot snapshot)
        {
            if (profile == null || !profile.AutoMatchEnabled)
                return false;

            List<bool> conditions = new List<bool>();

            if (!string.IsNullOrWhiteSpace(profile.NetworkType) &&
                !string.Equals(profile.NetworkType, "Any", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add(snapshot.Types.Any(
                    delegate(string x)
                    {
                        return string.Equals(x, profile.NetworkType, StringComparison.OrdinalIgnoreCase);
                    }));
            }

            string[] ipRules = SplitRules(profile.IpRanges);
            if (ipRules.Length > 0)
            {
                bool matched = false;
                for (int i = 0; i < snapshot.IPv4Addresses.Count && !matched; i++)
                {
                    for (int j = 0; j < ipRules.Length; j++)
                    {
                        if (IpMatches(snapshot.IPv4Addresses[i], ipRules[j]))
                        {
                            matched = true;
                            break;
                        }
                    }
                }
                conditions.Add(matched);
            }

            string[] nameRules = SplitRules(profile.NetworkNames);
            if (nameRules.Length > 0)
            {
                bool matched = false;
                for (int i = 0; i < snapshot.Names.Count && !matched; i++)
                {
                    for (int j = 0; j < nameRules.Length; j++)
                    {
                        if (snapshot.Names[i].IndexOf(nameRules[j], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matched = true;
                            break;
                        }
                    }
                }
                conditions.Add(matched);
            }

            if (conditions.Count == 0)
                return false;

            if (string.Equals(profile.AutoMatchMode, "Any", StringComparison.OrdinalIgnoreCase))
                return conditions.Any(delegate(bool x) { return x; });

            return conditions.All(delegate(bool x) { return x; });
        }

        private static string Classify(NetworkInterfaceType type)
        {
            if (type == NetworkInterfaceType.Wireless80211)
                return "WiFi";

            if (type == NetworkInterfaceType.Ethernet ||
                type == NetworkInterfaceType.Ethernet3Megabit ||
                type == NetworkInterfaceType.FastEthernetFx ||
                type == NetworkInterfaceType.FastEthernetT ||
                type == NetworkInterfaceType.GigabitEthernet)
                return "Ethernet";

            return "Other";
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (!list.Any(delegate(string x) { return string.Equals(x, value, StringComparison.OrdinalIgnoreCase); }))
                list.Add(value.Trim());
        }

        private static string[] SplitRules(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new string[0];

            return value.Replace("\r", "\n")
                .Split(new char[] { '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(delegate(string x) { return x.Trim(); })
                .Where(delegate(string x) { return x.Length > 0; })
                .ToArray();
        }

        private static bool IpMatches(IPAddress address, string rule)
        {
            string text = rule.Trim();
            if (text.Length == 0)
                return false;

            // 192.168.1.*
            if (text.IndexOf('*') >= 0)
            {
                string ip = address.ToString();
                string[] a = ip.Split('.');
                string[] r = text.Split('.');
                if (a.Length != 4 || r.Length != 4)
                    return false;
                for (int i = 0; i < 4; i++)
                {
                    if (r[i] == "*") continue;
                    if (!string.Equals(a[i], r[i], StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return true;
            }

            // 192.168.1.0/24
            int slash = text.IndexOf('/');
            if (slash > 0)
            {
                IPAddress network;
                int bits;
                if (!IPAddress.TryParse(text.Substring(0, slash).Trim(), out network) ||
                    !int.TryParse(text.Substring(slash + 1).Trim(), out bits) ||
                    bits < 0 || bits > 32)
                    return false;
                return IsInCidr(address, network, bits);
            }

            // 192.168.1.10-192.168.1.99
            int dash = text.IndexOf('-');
            if (dash > 0)
            {
                IPAddress start;
                IPAddress end;
                if (!IPAddress.TryParse(text.Substring(0, dash).Trim(), out start) ||
                    !IPAddress.TryParse(text.Substring(dash + 1).Trim(), out end))
                    return false;
                uint value = ToUInt(address);
                uint lo = ToUInt(start);
                uint hi = ToUInt(end);
                return value >= Math.Min(lo, hi) && value <= Math.Max(lo, hi);
            }

            IPAddress exact;
            return IPAddress.TryParse(text, out exact) && exact.Equals(address);
        }

        private static bool IsInCidr(IPAddress address, IPAddress network, int bits)
        {
            uint a = ToUInt(address);
            uint n = ToUInt(network);
            uint mask = bits == 0 ? 0U : uint.MaxValue << (32 - bits);
            return (a & mask) == (n & mask);
        }

        private static uint ToUInt(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            if (b.Length != 4)
                return 0;
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static string TryGetWifiSsid()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "netsh.exe";
                psi.Arguments = "wlan show interfaces";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1500);

                string[] lines = output.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = line.Substring(0, colon).Trim();
                    if (string.Equals(key, "SSID", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(colon + 1).Trim();
                }
            }
            catch { }
            return "";
        }
    }
}
