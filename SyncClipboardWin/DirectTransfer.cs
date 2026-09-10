using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboardWin
{
    public sealed class DirectEndpoint
    {
        public string Ip { get; set; }
        public string Mask { get; set; }
    }

    public sealed class DirectTransferInfo
    {
        public int Version { get; set; }
        public string TransferId { get; set; }
        public string Token { get; set; }
        public string Status { get; set; } // waiting / upload_requested / uploaded
        public string Type { get; set; }
        public string FileName { get; set; }
        public string DisplayText { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
        public int Port { get; set; }
        public string CreatedUtc { get; set; }
        public List<DirectEndpoint> Endpoints { get; set; }

        public DirectTransferInfo()
        {
            Version = 1;
            Endpoints = new List<DirectEndpoint>();
        }
    }

    internal sealed class PendingDirectTransfer
    {
        public DirectTransferInfo Info;
        public string LocalPath;
        public bool DeleteWhenDone;
        public AppSettings Settings;
        public SyncClipboardModel StandardModel;
        public volatile bool CancelRequested;
    }

    public sealed class DirectTransferManager : IDisposable
    {
        private readonly object _sync = new object();
        private TcpListener _listener;
        private Thread _listenThread;
        private volatile bool _stopping;
        private int _listenPort;
        private PendingDirectTransfer _pending;

        public int EnsureListener(int requestedPort)
        {
            if (requestedPort < 1 || requestedPort > 65535)
                requestedPort = 45678;

            lock (_sync)
            {
                if (_listener != null && _listenPort == requestedPort)
                    return _listenPort;

                StopListenerLocked();
                _stopping = false;
                _listener = new TcpListener(IPAddress.Any, requestedPort);
                _listener.Start();
                _listenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _listenThread = new Thread(ListenLoop);
                _listenThread.IsBackground = true;
                _listenThread.Name = "SyncClipboardDirectListener";
                _listenThread.Start();
                return _listenPort;
            }
        }

        public DirectTransferInfo RegisterOutgoing(
            AppSettings settings,
            string localPath,
            bool deleteWhenDone,
            SyncClipboardModel standardModel)
        {
            int port = EnsureListener(settings.DirectTransferPort);
            DirectTransferInfo info = new DirectTransferInfo();
            info.TransferId = Guid.NewGuid().ToString("N");
            info.Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            info.Status = "waiting";
            info.Type = standardModel.Type;
            info.FileName = standardModel.DataName;
            info.DisplayText = standardModel.Text;
            info.Size = standardModel.Size;
            info.Hash = standardModel.Hash;
            info.Port = port;
            info.CreatedUtc = DateTime.UtcNow.ToString("o");
            info.Endpoints = CaptureEndpoints();

            PendingDirectTransfer old = null;
            lock (_sync)
            {
                old = _pending;
                _pending = new PendingDirectTransfer
                {
                    Info = info,
                    LocalPath = localPath,
                    DeleteWhenDone = deleteWhenDone,
                    Settings = CloneSettings(settings),
                    StandardModel = standardModel
                };
            }
            CleanupPending(old);
            return info;
        }

        internal PendingDirectTransfer GetPending()
        {
            lock (_sync) { return _pending; }
        }

        public void ClearPending()
        {
            PendingDirectTransfer old;
            lock (_sync) { old = _pending; _pending = null; }
            CleanupPending(old);
        }

        public void CompletePending(string transferId)
        {
            PendingDirectTransfer done = null;
            lock (_sync)
            {
                if (_pending != null && string.Equals(
                    _pending.Info.TransferId, transferId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    done = _pending;
                    _pending = null;
                }
            }
            CleanupPending(done);
        }

        public static List<DirectEndpoint> CaptureEndpoints()
        {
            List<DirectEndpoint> result = new List<DirectEndpoint>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;
                        if (IPAddress.IsLoopback(ua.Address))
                            continue;
                        string mask = ua.IPv4Mask == null ? "" : ua.IPv4Mask.ToString();
                        result.Add(new DirectEndpoint { Ip = ua.Address.ToString(), Mask = mask });
                    }
                }
            }
            catch { }
            return result;
        }

        public static bool IsSameLan(DirectEndpoint remote)
        {
            if (remote == null) return false;
            IPAddress remoteIp, remoteMask;
            if (!IPAddress.TryParse(remote.Ip, out remoteIp) ||
                !IPAddress.TryParse(remote.Mask, out remoteMask))
                return false;
            byte[] rip = remoteIp.GetAddressBytes();
            byte[] rmask = remoteMask.GetAddressBytes();
            if (rip.Length != 4 || rmask.Length != 4) return false;

            foreach (DirectEndpoint local in CaptureEndpoints())
            {
                IPAddress lipAddr, lmaskAddr;
                if (!IPAddress.TryParse(local.Ip, out lipAddr) ||
                    !IPAddress.TryParse(local.Mask, out lmaskAddr)) continue;
                byte[] lip = lipAddr.GetAddressBytes();
                byte[] lmask = lmaskAddr.GetAddressBytes();
                if (lip.Length != 4 || lmask.Length != 4) continue;
                bool same = true;
                for (int i = 0; i < 4; i++)
                {
                    byte mask = (byte)(rmask[i] & lmask[i]);
                    if ((rip[i] & mask) != (lip[i] & mask)) { same = false; break; }
                }
                if (same) return true;
            }
            return false;
        }

        public async Task<bool> ReceiveAsync(
            DirectTransferInfo info,
            string localPath,
            Action<TransferProgress> progress,
            Func<bool> cancelled)
        {
            if (info == null || info.Endpoints == null) return false;
            foreach (DirectEndpoint ep in info.Endpoints)
            {
                if (!IsSameLan(ep)) continue;
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                TcpClient client = new TcpClient();
                try
                {
                    Task connect = client.ConnectAsync(ep.Ip, info.Port);
                    Task timeout = Task.Delay(1800);
                    if (await Task.WhenAny(connect, timeout) != connect)
                        continue;
                    await connect;
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] request = Encoding.UTF8.GetBytes(info.TransferId + "|" + info.Token + "\n");
                        await stream.WriteAsync(request, 0, request.Length);
                        string response = await ReadLineAsync(stream, cancelled);
                        if (!string.Equals(response, "OK", StringComparison.Ordinal))
                            continue;

                        string dir = Path.GetDirectoryName(localPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        using (FileStream output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[128 * 1024];
                            long received = 0;
                            if (progress != null) progress(new TransferProgress("局域网直传", info.FileName, 0, info.Size));
                            while (received < info.Size)
                            {
                                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                                int want = (int)Math.Min(buffer.Length, info.Size - received);
                                int read = await stream.ReadAsync(buffer, 0, want);
                                if (read <= 0) throw new IOException("局域网连接提前断开。");
                                await output.WriteAsync(buffer, 0, read);
                                received += read;
                                if (progress != null) progress(new TransferProgress("局域网直传", info.FileName, received, info.Size));
                            }
                        }
                    }
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch { try { if (File.Exists(localPath)) File.Delete(localPath); } catch { } }
                finally { try { client.Close(); } catch { } }
            }
            return false;
        }

        private void ListenLoop()
        {
            while (!_stopping)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { ServeClient(client); });
                }
                catch
                {
                    if (client != null) try { client.Close(); } catch { }
                    if (_stopping) break;
                    Thread.Sleep(200);
                }
            }
        }

        private void ServeClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    string line = ReadLineSync(stream);
                    string[] parts = line.Split('|');
                    if (parts.Length != 2) { WriteLine(stream, "ERR"); return; }
                    PendingDirectTransfer pending = GetPending();
                    if (pending == null || pending.CancelRequested ||
                        !string.Equals(parts[0], pending.Info.TransferId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(parts[1], pending.Info.Token, StringComparison.Ordinal))
                    { WriteLine(stream, "ERR"); return; }
                    if (!File.Exists(pending.LocalPath)) { WriteLine(stream, "MISS"); return; }
                    WriteLine(stream, "OK");
                    using (FileStream input = new FileStream(pending.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] buffer = new byte[128 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            stream.Write(buffer, 0, read);
                    }
                }
                catch { }
            }
        }

        private static async Task<string> ReadLineAsync(Stream stream, Func<bool> cancelled)
        {
            List<byte> data = new List<byte>();
            byte[] one = new byte[1];
            while (data.Count < 1024)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                int n = await stream.ReadAsync(one, 0, 1);
                if (n <= 0) break;
                if (one[0] == 10) break;
                if (one[0] != 13) data.Add(one[0]);
            }
            return Encoding.UTF8.GetString(data.ToArray());
        }

        private static string ReadLineSync(Stream stream)
        {
            List<byte> data = new List<byte>();
            while (data.Count < 1024)
            {
                int b = stream.ReadByte();
                if (b < 0 || b == 10) break;
                if (b != 13) data.Add((byte)b);
            }
            return Encoding.UTF8.GetString(data.ToArray());
        }

        private static void WriteLine(Stream stream, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text + "\n");
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }

        private static AppSettings CloneSettings(AppSettings source)
        {
            System.Web.Script.Serialization.JavaScriptSerializer s = new System.Web.Script.Serialization.JavaScriptSerializer();
            return s.Deserialize<AppSettings>(s.Serialize(source));
        }

        private static void CleanupPending(PendingDirectTransfer p)
        {
            if (p != null && p.DeleteWhenDone && !string.IsNullOrEmpty(p.LocalPath))
            {
                try { File.Delete(p.LocalPath); } catch { }
            }
        }

        private void StopListenerLocked()
        {
            _stopping = true;
            if (_listener != null) try { _listener.Stop(); } catch { }
            _listener = null;
            _listenPort = 0;
        }

        public void Dispose()
        {
            PendingDirectTransfer pending;
            lock (_sync)
            {
                StopListenerLocked();
                pending = _pending;
                _pending = null;
            }
            CleanupPending(pending);
        }
    }
}
