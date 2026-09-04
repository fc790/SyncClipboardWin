using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace SyncClipboardWin
{
    public sealed class AppSettings
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public string WebDavUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string UploadHotkey { get; set; }
        public string DownloadHotkey { get; set; }
        public long UploadLimitBytes { get; set; }
        public long TempLimitBytes { get; set; }
        public bool UseImageType { get; set; }
        public bool StartWithWindows { get; set; }
        public bool NotifySuccess { get; set; }
        public bool NotifyFailure { get; set; }
        public bool MonitorClipboard { get; set; }

        // 自动网络匹配规则
        public bool AutoMatchEnabled { get; set; }
        public string AutoMatchMode { get; set; }   // Any / All
        public string NetworkType { get; set; }     // Any / WiFi / Ethernet / Other / 空=不参与
        public string IpRanges { get; set; }        // 每行/分号一个规则
        public string NetworkNames { get; set; }    // 每行/分号一个关键字

        public AppSettings()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "默认配置";
            WebDavUrl = "";
            Username = "";
            Password = "";
            UploadHotkey = "Ctrl+Shift+C";
            DownloadHotkey = "Ctrl+Shift+V";
            UploadLimitBytes = 900L * 1024L * 1024L;
            TempLimitBytes = 1024L * 1024L * 1024L;
            UseImageType = true;
            StartWithWindows = false;
            NotifySuccess = true;
            NotifyFailure = true;
            MonitorClipboard = false;
            AutoMatchEnabled = false;
            AutoMatchMode = "All";
            NetworkType = "";
            IpRanges = "";
            NetworkNames = "";
        }

        public static string ConfigDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, "config.json"); }
        }

        public static string DownloadsDirectory
        {
            get
            {
                try
                {
                    string path = NativeMethods.GetKnownFolderPath(NativeMethods.FOLDERID_Downloads);
                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
                catch { }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
            }
        }

        public static string DesktopDirectory
        {
            get
            {
                try
                {
                    string path = NativeMethods.GetKnownFolderPath(NativeMethods.FOLDERID_Desktop);
                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
                catch { }

                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
        }

        public static string TempDirectory
        {
            get { return Path.Combine(DownloadsDirectory, "synccliptmp"); }
        }
    }

    public sealed class AppConfig
    {
        public int ConfigVersion { get; set; }
        public string ActiveProfileId { get; set; }
        public bool AutoSwitchEnabled { get; set; }
        public List<AppSettings> Profiles { get; set; }

        public AppConfig()
        {
            ConfigVersion = 2;
            AutoSwitchEnabled = false;
            Profiles = new List<AppSettings>();
        }

        public AppSettings GetActiveProfile()
        {
            EnsureValid();
            for (int i = 0; i < Profiles.Count; i++)
            {
                if (string.Equals(Profiles[i].Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                    return Profiles[i];
            }

            ActiveProfileId = Profiles[0].Id;
            return Profiles[0];
        }

        public void EnsureValid()
        {
            if (Profiles == null)
                Profiles = new List<AppSettings>();

            if (Profiles.Count == 0)
                Profiles.Add(new AppSettings());

            for (int i = 0; i < Profiles.Count; i++)
            {
                AppSettings p = Profiles[i];
                if (string.IsNullOrWhiteSpace(p.Id))
                    p.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(p.Name))
                    p.Name = "配置 " + (i + 1).ToString();
                if (string.IsNullOrWhiteSpace(p.AutoMatchMode))
                    p.AutoMatchMode = "All";
            }

            bool activeFound = false;
            for (int i = 0; i < Profiles.Count; i++)
            {
                if (string.Equals(Profiles[i].Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    activeFound = true;
                    break;
                }
            }

            if (!activeFound)
                ActiveProfileId = Profiles[0].Id;
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(AppSettings.ConfigPath))
                {
                    AppConfig fresh = new AppConfig();
                    AppSettings first = new AppSettings();
                    fresh.Profiles.Add(first);
                    fresh.ActiveProfileId = first.Id;
                    return fresh;
                }

                string json = File.ReadAllText(AppSettings.ConfigPath);
                JavaScriptSerializer serializer = new JavaScriptSerializer();

                // 新版多配置格式
                try
                {
                    AppConfig cfg = serializer.Deserialize<AppConfig>(json);
                    if (cfg != null && cfg.Profiles != null && cfg.Profiles.Count > 0)
                    {
                        cfg.EnsureValid();
                        return cfg;
                    }
                }
                catch { }

                // 兼容 0.2.3 以及更早的单配置格式
                AppSettings legacy = serializer.Deserialize<AppSettings>(json);
                if (legacy == null)
                    legacy = new AppSettings();
                if (string.IsNullOrWhiteSpace(legacy.Id))
                    legacy.Id = Guid.NewGuid().ToString("N");
                legacy.Name = "默认配置";

                AppConfig migrated = new AppConfig();
                migrated.Profiles.Add(legacy);
                migrated.ActiveProfileId = legacy.Id;
                migrated.EnsureValid();
                return migrated;
            }
            catch
            {
                AppConfig fallback = new AppConfig();
                AppSettings first = new AppSettings();
                fallback.Profiles.Add(first);
                fallback.ActiveProfileId = first.Id;
                return fallback;
            }
        }

        public void Save()
        {
            EnsureValid();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(this);
            File.WriteAllText(AppSettings.ConfigPath, json);
        }

        public AppConfig Clone()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Deserialize<AppConfig>(serializer.Serialize(this));
        }
    }
}
