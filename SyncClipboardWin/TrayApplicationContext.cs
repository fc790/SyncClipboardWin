using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SyncClipboardWin
{
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private AppConfig _config;
        private AppSettings _settings;
        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkeys;
        private readonly SyncService _service;
        private readonly ToolStripMenuItem _profilesMenu;
        private readonly Timer _networkTimer;
        private bool _busy;
        private string _lastClipboardSignature;
        private uint _lastClipboardSequence;

        public TrayApplicationContext()
        {
            _config = AppConfig.Load();
            _settings = _config.GetActiveProfile();
            _service =
                new SyncService(
                    delegate
                    {
                        return _settings;
                    });

            _hotkeys = new HotkeyWindow();

            _hotkeys.HotkeyPressed +=
                async delegate(int id)
                {
                    if (id == 1)
                        await RunAsync(
                            "上传",
                            delegate(Action<TransferProgress> progress)
                            {
                                return _service.UploadAsync(progress);
                            });

                    if (id == 2)
                        await RunAsync(
                            "下载",
                            delegate(Action<TransferProgress> progress)
                            {
                                return _service.DownloadAsync(progress);
                            });
                };

            _hotkeys.ClipboardUpdated +=
                async delegate(object sender, EventArgs e)
                {
                    await OnClipboardUpdatedAsync();
                };

            ContextMenuStrip menu =
                new ContextMenuStrip();

            _profilesMenu = new ToolStripMenuItem("切换配置");

            menu.Items.Add(
                "上传到云剪切板",
                null,
                async delegate
                {
                    await RunAsync(
                        "上传",
                        delegate(Action<TransferProgress> progress)
                        {
                            return _service.UploadAsync(progress);
                        });
                });

            menu.Items.Add(
                "下载云剪切板",
                null,
                async delegate
                {
                    await RunAsync(
                        "下载",
                        delegate(Action<TransferProgress> progress)
                        {
                            return _service.DownloadAsync(progress);
                        });
                });

            menu.Items.Add(
                new ToolStripSeparator());

            menu.Items.Add(_profilesMenu);

            menu.Items.Add(
                "设置",
                null,
                delegate
                {
                    ShowSettings();
                });

            menu.Items.Add(
                "测试 WebDAV",
                null,
                async delegate
                {
                    await RunAsync(
                        "测试连接",
                        async delegate(Action<TransferProgress> progress)
                        {
                            await _service.TestConnectionAsync();
                            return "WebDAV 连接成功";
                        });
                });

            menu.Items.Add(
                "打开临时目录",
                null,
                delegate
                {
                    Directory.CreateDirectory(
                        AppSettings.TempDirectory);

                    ProcessStartInfo psi =
                        new ProcessStartInfo(
                            "explorer.exe",
                            AppSettings.TempDirectory);

                    psi.UseShellExecute = true;
                    Process.Start(psi);
                });

            menu.Items.Add(
                new ToolStripSeparator());

            menu.Items.Add(
                "退出",
                null,
                delegate
                {
                    Exit();
                });

            Icon icon =
                Icon.ExtractAssociatedIcon(
                    Application.ExecutablePath);

            _tray = new NotifyIcon();
            _tray.Icon =
                icon ?? SystemIcons.Application;
            _tray.Text = "SyncClipboardWin";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;

            _tray.DoubleClick +=
                delegate
                {
                    ShowSettings();
                };

            RebuildProfilesMenu();

            _networkTimer = new Timer();
            _networkTimer.Interval = 5000;
            _networkTimer.Tick += delegate { CheckAutoSwitch(); };
            _networkTimer.Start();

            RegisterHotkeys(false);

            _lastClipboardSequence =
                NativeMethods.GetClipboardSequenceNumber();

            _lastClipboardSignature =
                TryGetClipboardSignature();

            if (string.IsNullOrWhiteSpace(
                _settings.WebDavUrl))
            {
                ShowSettings();
            }
        }

        private async Task OnClipboardUpdatedAsync()
        {
            if (!_settings.MonitorClipboard)
                return;

            if (ClipboardHelper.IsInternalClipboardOperation)
                return;

            uint seq =
                NativeMethods.GetClipboardSequenceNumber();

            if (seq == _lastClipboardSequence)
                return;

            _lastClipboardSequence = seq;

            if (_busy)
                return;

            string signature =
                TryGetClipboardSignature();

            if (signature == null)
                return;

            if (signature == _lastClipboardSignature)
                return;

            _lastClipboardSignature = signature;

            await RunAsync(
                "自动上传",
                delegate(Action<TransferProgress> progress)
                {
                    return _service.UploadClipboardOnlyAsync(progress);
                });
        }

        private static string
            TryGetClipboardSignature()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    string[] items =
                        Clipboard.GetFileDropList()
                        .Cast<string>()
                        .Select(
                            delegate(string path)
                            {
                                try
                                {
                                    if (File.Exists(path))
                                    {
                                        FileInfo fi =
                                            new FileInfo(path);

                                        return string.Format(
                                            "F|{0}|{1}|{2}",
                                            path,
                                            fi.Length,
                                            fi.LastWriteTimeUtc.Ticks);
                                    }

                                    if (Directory.Exists(path))
                                    {
                                        return string.Format(
                                            "D|{0}|{1}",
                                            path,
                                            Directory.GetLastWriteTimeUtc(
                                                path).Ticks);
                                    }
                                }
                                catch { }

                                return "P|" + path;
                            })
                        .ToArray();

                    return "FILES:" +
                           string.Join("\n", items);
                }

                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();

                    if (string.IsNullOrEmpty(text))
                        return null;

                    byte[] bytes =
                        Encoding.UTF8.GetBytes(text);

                    byte[] hash;
                    using (SHA256 sha =
                        SHA256.Create())
                    {
                        hash = sha.ComputeHash(bytes);
                    }

                    return "TEXT:" +
                           BytesToHex(hash);
                }
            }
            catch { }

            return null;
        }

        private static string BytesToHex(
            byte[] data)
        {
            StringBuilder sb =
                new StringBuilder(
                    data.Length * 2);

            for (int i = 0; i < data.Length; i++)
                sb.Append(data[i].ToString("X2"));

            return sb.ToString();
        }

        private void RegisterHotkeys(
            bool showErrors)
        {
            _hotkeys.Unregister(1);
            _hotkeys.Unregister(2);

            Hotkey up;
            Hotkey down;

            bool upOk =
                Hotkey.TryParse(
                    _settings.UploadHotkey,
                    out up) &&
                _hotkeys.Register(1, up);

            bool downOk =
                Hotkey.TryParse(
                    _settings.DownloadHotkey,
                    out down) &&
                _hotkeys.Register(2, down);

            if (showErrors &&
                (!upOk || !downOk))
            {
                MessageBox.Show(
                    "部分全局快捷键注册失败，可能已被其他程序占用。",
                    "SyncClipboardWin",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async Task RunAsync(
            string action,
            Func<Action<TransferProgress>, Task<string>> operation)
        {
            if (_busy)
                return;

            _busy = true;
            ProgressForm progressForm = null;

            try
            {
                Action<TransferProgress> report =
                    delegate(TransferProgress value)
                    {
                        if (progressForm == null)
                        {
                            progressForm = new ProgressForm();
                            progressForm.Show();
                        }

                        if (!progressForm.IsDisposed)
                            progressForm.UpdateProgress(value);
                    };

                // 重要：这里不能提前显示任何窗口。
                // UploadAsync/DownloadAsync 必须先从当前前台窗口读取
                // Explorer/Desktop 选择项或选中文本。
                string result =
                    await operation(report);

                if (_settings.NotifySuccess)
                {
                    Notify(
                        action + "成功",
                        result,
                        ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                if (_settings.NotifyFailure)
                {
                    Notify(
                        action + "失败",
                        ex.Message,
                        ToolTipIcon.Error);
                }
            }
            finally
            {
                if (progressForm != null)
                {
                    try
                    {
                        progressForm.Close();
                        progressForm.Dispose();
                    }
                    catch { }
                }

                _busy = false;
            }
        }

        private void Notify(
            string title,
            string message,
            ToolTipIcon icon)
        {
            _tray.BalloonTipTitle = title;

            _tray.BalloonTipText =
                message.Length > 240
                    ? message.Substring(0, 240)
                    : message;

            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(3000);
        }

        private void ShowSettings()
        {
            using (SettingsForm form = new SettingsForm(_config))
            {
                if (form.ShowDialog() == DialogResult.OK && form.SettingsChanged)
                {
                    _config = form.ResultConfig ?? AppConfig.Load();
                    _settings = _config.GetActiveProfile();
                    RebuildProfilesMenu();
                    RegisterHotkeys(true);
                    _lastClipboardSequence = NativeMethods.GetClipboardSequenceNumber();
                    _lastClipboardSignature = TryGetClipboardSignature();
                }
            }
        }

        private void RebuildProfilesMenu()
        {
            _profilesMenu.DropDownItems.Clear();
            for (int i = 0; i < _config.Profiles.Count; i++)
            {
                AppSettings profile = _config.Profiles[i];
                ToolStripMenuItem item = new ToolStripMenuItem(profile.Name);
                item.Checked = string.Equals(profile.Id, _config.ActiveProfileId, StringComparison.OrdinalIgnoreCase);
                string id = profile.Id;
                item.Click += delegate { SwitchProfile(id, false); };
                _profilesMenu.DropDownItems.Add(item);
            }
        }

        private void SwitchProfile(string id, bool automatic)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            if (string.Equals(id, _config.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                return;

            AppSettings target = null;
            for (int i = 0; i < _config.Profiles.Count; i++)
            {
                if (string.Equals(_config.Profiles[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    target = _config.Profiles[i];
                    break;
                }
            }
            if (target == null)
                return;

            _config.ActiveProfileId = target.Id;
            _settings = target;
            try { _config.Save(); } catch { }
            RebuildProfilesMenu();
            RegisterHotkeys(false);

            _lastClipboardSequence = NativeMethods.GetClipboardSequenceNumber();
            _lastClipboardSignature = TryGetClipboardSignature();

            Notify(
                automatic ? "已自动切换配置" : "已切换配置",
                target.Name,
                ToolTipIcon.Info);
        }

        private void CheckAutoSwitch()
        {
            if (_busy || !_config.AutoSwitchEnabled)
                return;

            try
            {
                NetworkSnapshot snapshot = NetworkEnvironment.Capture();
                for (int i = 0; i < _config.Profiles.Count; i++)
                {
                    AppSettings p = _config.Profiles[i];
                    if (NetworkEnvironment.IsMatch(p, snapshot))
                    {
                        SwitchProfile(p.Id, true);
                        return;
                    }
                }
            }
            catch { }
        }

        private void Exit()
        {
            _tray.Visible = false;
            _networkTimer.Stop();
            _networkTimer.Dispose();
            _tray.Dispose();
            _hotkeys.Dispose();
            ExitThread();
        }
    }
}
