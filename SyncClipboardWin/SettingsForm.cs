using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SyncClipboardWin
{
    public sealed class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private AppSettings _editingProfile;
        private bool _loadingProfile;

        private readonly ComboBox profileCombo;
        private readonly TextBox profileName;
        private readonly Button addProfile;
        private readonly Button copyProfile;
        private readonly Button deleteProfile;

        private readonly TextBox url;
        private readonly TextBox user;
        private readonly TextBox pass;
        private readonly TextBox hkUp;
        private readonly TextBox hkDown;
        private readonly NumericUpDown uploadMb;
        private readonly NumericUpDown tempMb;
        private readonly CheckBox imageType;
        private readonly CheckBox startup;
        private readonly CheckBox notifyOk;
        private readonly CheckBox notifyFail;
        private readonly CheckBox monitorClipboard;
        private readonly TextBox configPath;

        private readonly CheckBox autoSwitch;
        private readonly CheckBox autoMatch;
        private readonly ComboBox matchMode;
        private readonly ComboBox networkType;
        private readonly TextBox ipRanges;
        private readonly TextBox networkNames;

        public bool SettingsChanged { get; private set; }
        public AppConfig ResultConfig { get; private set; }

        public SettingsForm(AppConfig config)
        {
            _config = config.Clone();
            _config.EnsureValid();

            Text = "SyncClipboardWin 设置";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(700, 650);
            Size = new Size(820, 820);

            Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted != null)
                Icon = extracted;

            profileCombo = new ComboBox();
            profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            profileCombo.Width = 220;

            profileName = new TextBox();
            profileName.Width = 180;

            addProfile = MakeButton("新建", 72);
            copyProfile = MakeButton("复制", 72);
            deleteProfile = MakeButton("删除", 72);

            url = new TextBox(); url.Width = 420;
            user = new TextBox(); user.Width = 240;
            pass = new TextBox(); pass.Width = 240; pass.UseSystemPasswordChar = true;
            hkUp = new TextBox(); hkUp.Width = 180; hkUp.ReadOnly = true;
            hkDown = new TextBox(); hkDown.Width = 180; hkDown.ReadOnly = true;

            uploadMb = new NumericUpDown();
            uploadMb.Minimum = 1; uploadMb.Maximum = 102400; uploadMb.Width = 120;
            tempMb = new NumericUpDown();
            tempMb.Minimum = 1; tempMb.Maximum = 102400; tempMb.Width = 120;

            imageType = MakeCheck("图片文件使用 Image 类型");
            startup = MakeCheck("开机启动");
            notifyOk = MakeCheck("成功时通知");
            notifyFail = MakeCheck("失败时通知");
            monitorClipboard = MakeCheck("监测剪贴板变化并自动上传");

            configPath = new TextBox();
            configPath.Width = 420;
            configPath.ReadOnly = true;
            configPath.Text = AppSettings.ConfigPath;

            autoSwitch = MakeCheck("启用按网络环境自动切换配置");
            autoSwitch.Checked = _config.AutoSwitchEnabled;
            autoMatch = MakeCheck("此配置参与自动匹配");

            matchMode = new ComboBox();
            matchMode.DropDownStyle = ComboBoxStyle.DropDownList;
            matchMode.Width = 220;
            matchMode.Items.Add("全部已填写条件同时满足");
            matchMode.Items.Add("任一已填写条件满足");

            networkType = new ComboBox();
            networkType.DropDownStyle = ComboBoxStyle.DropDownList;
            networkType.Width = 220;
            networkType.Items.Add("不限制");
            networkType.Items.Add("Wi-Fi");
            networkType.Items.Add("有线网络");
            networkType.Items.Add("其他网络");

            ipRanges = new TextBox();
            ipRanges.Width = 420;
            ipRanges.Height = 70;
            ipRanges.Multiline = true;
            ipRanges.ScrollBars = ScrollBars.Vertical;

            networkNames = new TextBox();
            networkNames.Width = 420;
            networkNames.Height = 70;
            networkNames.Multiline = true;
            networkNames.ScrollBars = ScrollBars.Vertical;

            hkUp.KeyDown += CaptureHotkey;
            hkDown.KeyDown += CaptureHotkey;

            profileCombo.SelectedIndexChanged += delegate
            {
                if (_loadingProfile) return;
                SaveControlsToEditingProfile();
                SelectProfileByIndex(profileCombo.SelectedIndex);
            };

            addProfile.Click += delegate { AddNewProfile(false); };
            copyProfile.Click += delegate { AddNewProfile(true); };
            deleteProfile.Click += delegate { DeleteCurrentProfile(); };

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;

            TabPage basicTab = new TabPage("配置与同步");
            basicTab.Controls.Add(BuildBasicPanel());
            tabs.TabPages.Add(basicTab);

            TabPage autoTab = new TabPage("网络自动切换");
            autoTab.Controls.Add(BuildAutoPanel());
            tabs.TabPages.Add(autoTab);

            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 58;
            bottomPanel.Padding = new Padding(10);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            Button save = MakeButton("保存", 96);
            save.Height = 32;
            Button cancel = MakeButton("取消", 96);
            cancel.Height = 32;
            cancel.DialogResult = DialogResult.Cancel;

            save.Click += delegate { SaveSettings(); };
            cancel.Click += delegate { Close(); };

            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            bottomPanel.Controls.Add(buttons);

            Controls.Add(tabs);
            Controls.Add(bottomPanel);

            AcceptButton = save;
            CancelButton = cancel;

            RefreshProfileCombo(_config.ActiveProfileId);
        }

        private Control BuildBasicPanel()
        {
            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.Padding = new Padding(16, 14, 16, 14);

            TableLayoutPanel table = MakeTable();
            int r = 0;

            FlowLayoutPanel selector = new FlowLayoutPanel();
            selector.AutoSize = true;
            selector.WrapContents = false;
            selector.Controls.Add(profileCombo);
            selector.Controls.Add(addProfile);
            selector.Controls.Add(copyProfile);
            selector.Controls.Add(deleteProfile);

            AddRow(table, r++, "当前配置", selector);
            AddRow(table, r++, "配置名称", profileName);
            AddRow(table, r++, "WebDAV 地址", url);
            AddRow(table, r++, "用户名", user);
            AddRow(table, r++, "密码", pass);
            AddRow(table, r++, "上传快捷键", hkUp);
            AddRow(table, r++, "下载快捷键", hkDown);
            AddRow(table, r++, "上传大小限制 (MB)", uploadMb);
            AddRow(table, r++, "临时目录限制 (MB)", tempMb);
            AddRow(table, r++, "", imageType);
            AddRow(table, r++, "", startup);
            AddRow(table, r++, "", notifyOk);
            AddRow(table, r++, "", notifyFail);
            AddRow(table, r++, "", monitorClipboard);
            AddRow(table, r++, "配置文件", configPath);

            scroll.Controls.Add(table);
            return scroll;
        }

        private Control BuildAutoPanel()
        {
            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.Padding = new Padding(16, 14, 16, 14);

            TableLayoutPanel table = MakeTable();
            int r = 0;

            Label help = new Label();
            help.AutoSize = true;
            help.MaximumSize = new Size(560, 0);
            help.Text =
                "自动切换会依次检查配置列表，遇到第一个匹配的配置就切换。\r\n" +
                "网络名称会匹配 Wi-Fi SSID、网卡名称和网卡描述；名称使用“包含”匹配。";

            Label ipHelp = new Label();
            ipHelp.AutoSize = true;
            ipHelp.MaximumSize = new Size(560, 0);
            ipHelp.Text =
                "每行或分号写一个 IPv4 规则。支持：192.168.1.0/24、192.168.1.*、" +
                "192.168.1.10-192.168.1.99、单个 IP。多个规则之间为“任一匹配”。";

            Label nameHelp = new Label();
            nameHelp.AutoSize = true;
            nameHelp.MaximumSize = new Size(560, 0);
            nameHelp.Text = "每行或分号写一个名称关键字。多个名称关键字之间为“任一匹配”。";

            AddRow(table, r++, "", autoSwitch);
            AddRow(table, r++, "说明", help);
            AddRow(table, r++, "", autoMatch);
            AddRow(table, r++, "条件组合方式", matchMode);
            AddRow(table, r++, "网络类型", networkType);
            AddRow(table, r++, "IPv4 范围", ipRanges);
            AddRow(table, r++, "IPv4 写法", ipHelp);
            AddRow(table, r++, "网络名称", networkNames);
            AddRow(table, r++, "名称写法", nameHelp);

            scroll.Controls.Add(table);
            return scroll;
        }

        private static TableLayoutPanel MakeTable()
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 2;
            table.Dock = DockStyle.Top;
            table.Margin = new Padding(0);
            table.Padding = new Padding(0);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return table;
        }

        private static Button MakeButton(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            return b;
        }

        private static CheckBox MakeCheck(string text)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            return c;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label lbl = new Label();
            lbl.Text = label;
            lbl.AutoSize = true;
            lbl.Anchor = AnchorStyles.Left;
            lbl.Margin = new Padding(0, 7, 12, 8);

            control.Anchor = AnchorStyles.Left;
            control.Margin = new Padding(0, 3, 0, 8);

            table.Controls.Add(lbl, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private void RefreshProfileCombo(string selectedId)
        {
            _loadingProfile = true;
            try
            {
                profileCombo.Items.Clear();
                int selectedIndex = 0;
                for (int i = 0; i < _config.Profiles.Count; i++)
                {
                    profileCombo.Items.Add(_config.Profiles[i].Name);
                    if (string.Equals(_config.Profiles[i].Id, selectedId, StringComparison.OrdinalIgnoreCase))
                        selectedIndex = i;
                }
                profileCombo.SelectedIndex = selectedIndex;
                SelectProfileByIndex(selectedIndex);
            }
            finally
            {
                _loadingProfile = false;
            }
        }

        private void SelectProfileByIndex(int index)
        {
            if (index < 0 || index >= _config.Profiles.Count)
                return;

            _editingProfile = _config.Profiles[index];
            LoadProfileToControls(_editingProfile);
        }

        private void LoadProfileToControls(AppSettings p)
        {
            _loadingProfile = true;
            try
            {
                profileName.Text = p.Name;
                url.Text = p.WebDavUrl;
                user.Text = p.Username;
                pass.Text = p.Password;
                hkUp.Text = p.UploadHotkey;
                hkDown.Text = p.DownloadHotkey;
                uploadMb.Value = ClampDecimal(p.UploadLimitBytes / 1024L / 1024L, 1, 102400);
                tempMb.Value = ClampDecimal(p.TempLimitBytes / 1024L / 1024L, 1, 102400);
                imageType.Checked = p.UseImageType;
                startup.Checked = p.StartWithWindows;
                notifyOk.Checked = p.NotifySuccess;
                notifyFail.Checked = p.NotifyFailure;
                monitorClipboard.Checked = p.MonitorClipboard;

                autoMatch.Checked = p.AutoMatchEnabled;
                matchMode.SelectedIndex = string.Equals(p.AutoMatchMode, "Any", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

                if (string.Equals(p.NetworkType, "WiFi", StringComparison.OrdinalIgnoreCase)) networkType.SelectedIndex = 1;
                else if (string.Equals(p.NetworkType, "Ethernet", StringComparison.OrdinalIgnoreCase)) networkType.SelectedIndex = 2;
                else if (string.Equals(p.NetworkType, "Other", StringComparison.OrdinalIgnoreCase)) networkType.SelectedIndex = 3;
                else networkType.SelectedIndex = 0;

                ipRanges.Text = p.IpRanges ?? "";
                networkNames.Text = p.NetworkNames ?? "";
            }
            finally
            {
                _loadingProfile = false;
            }
        }

        private void SaveControlsToEditingProfile()
        {
            if (_editingProfile == null || _loadingProfile)
                return;

            _editingProfile.Name = string.IsNullOrWhiteSpace(profileName.Text)
                ? "未命名配置"
                : profileName.Text.Trim();
            _editingProfile.WebDavUrl = url.Text.Trim().TrimEnd('/');
            _editingProfile.Username = user.Text;
            _editingProfile.Password = pass.Text;
            _editingProfile.UploadHotkey = hkUp.Text;
            _editingProfile.DownloadHotkey = hkDown.Text;
            _editingProfile.UploadLimitBytes = (long)uploadMb.Value * 1024L * 1024L;
            _editingProfile.TempLimitBytes = (long)tempMb.Value * 1024L * 1024L;
            _editingProfile.UseImageType = imageType.Checked;
            _editingProfile.StartWithWindows = startup.Checked;
            _editingProfile.NotifySuccess = notifyOk.Checked;
            _editingProfile.NotifyFailure = notifyFail.Checked;
            _editingProfile.MonitorClipboard = monitorClipboard.Checked;

            _editingProfile.AutoMatchEnabled = autoMatch.Checked;
            _editingProfile.AutoMatchMode = matchMode.SelectedIndex == 1 ? "Any" : "All";
            if (networkType.SelectedIndex == 1) _editingProfile.NetworkType = "WiFi";
            else if (networkType.SelectedIndex == 2) _editingProfile.NetworkType = "Ethernet";
            else if (networkType.SelectedIndex == 3) _editingProfile.NetworkType = "Other";
            else _editingProfile.NetworkType = "";
            _editingProfile.IpRanges = ipRanges.Text.Trim();
            _editingProfile.NetworkNames = networkNames.Text.Trim();
        }

        private void AddNewProfile(bool copyCurrent)
        {
            SaveControlsToEditingProfile();

            AppSettings p;
            if (copyCurrent && _editingProfile != null)
            {
                AppConfig temp = new AppConfig();
                temp.Profiles.Add(_editingProfile);
                p = temp.Clone().Profiles[0];
                p.Id = Guid.NewGuid().ToString("N");
                p.Name = _editingProfile.Name + " - 副本";
            }
            else
            {
                p = new AppSettings();
                p.Name = "新配置 " + (_config.Profiles.Count + 1).ToString();
            }

            _config.Profiles.Add(p);
            RefreshProfileCombo(p.Id);
        }

        private void DeleteCurrentProfile()
        {
            if (_editingProfile == null)
                return;

            if (_config.Profiles.Count <= 1)
            {
                MessageBox.Show("至少需要保留一个配置。", "SyncClipboardWin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show(
                "确定删除配置“" + _editingProfile.Name + "”吗？",
                "SyncClipboardWin",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
                return;

            int index = _config.Profiles.IndexOf(_editingProfile);
            string deletedId = _editingProfile.Id;
            _config.Profiles.Remove(_editingProfile);
            if (string.Equals(_config.ActiveProfileId, deletedId, StringComparison.OrdinalIgnoreCase))
                _config.ActiveProfileId = _config.Profiles[Math.Max(0, Math.Min(index, _config.Profiles.Count - 1))].Id;

            RefreshProfileCombo(_config.ActiveProfileId);
        }

        private static decimal ClampDecimal(long value, long min, long max)
        {
            if (value < min) value = min;
            if (value > max) value = max;
            return value;
        }

        private void CaptureHotkey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            TextBox tb = sender as TextBox;
            if (tb != null)
                tb.Text = Hotkey.FromKeyEvent(e);
        }

        private void SaveSettings()
        {
            SaveControlsToEditingProfile();

            for (int i = 0; i < _config.Profiles.Count; i++)
            {
                Hotkey parsedUp;
                Hotkey parsedDown;
                if (!Hotkey.TryParse(_config.Profiles[i].UploadHotkey, out parsedUp) ||
                    !Hotkey.TryParse(_config.Profiles[i].DownloadHotkey, out parsedDown))
                {
                    MessageBox.Show(
                        "配置“" + _config.Profiles[i].Name + "”的快捷键无效。",
                        "SyncClipboardWin",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            _config.AutoSwitchEnabled = autoSwitch.Checked;
            if (_editingProfile != null)
                _config.ActiveProfileId = _editingProfile.Id;
            _config.EnsureValid();

            try
            {
                _config.Save();
                ApplyStartup(_config.GetActiveProfile().StartWithWindows);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    "无法写入配置文件：\r\n" + AppSettings.ConfigPath +
                    "\r\n\r\n请把程序放到普通可写目录（例如 D:\\Tools\\SyncClipboardWin），不要放在 Program Files 等受保护目录。",
                    "SyncClipboardWin",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "保存配置失败：\r\n" + ex.Message + "\r\n\r\n目标：" + AppSettings.ConfigPath,
                    "SyncClipboardWin",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            SettingsChanged = true;
            ResultConfig = _config;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static void ApplyStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled)
                {
                    key.SetValue("SyncClipboardWin", "\"" + Application.ExecutablePath + "\"");
                }
                else
                {
                    key.DeleteValue("SyncClipboardWin", false);
                }
            }
        }
    }
}
