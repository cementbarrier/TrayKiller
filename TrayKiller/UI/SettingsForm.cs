using TrayKiller.Services;

namespace TrayKiller.UI;

/// <summary>
/// 设置面板：开机自启、自动隐藏、关闭模式、停靠位置、黑白名单管理。
/// </summary>
public partial class SettingsForm : Form
{
    private readonly SettingsManager _settings;
    private readonly WhitelistManager _whitelist;

    private CheckBox? _chkAutoStart;
    private CheckBox? _chkAutoHide;
    private CheckBox? _chkForceKill;
    private ComboBox? _cmbDockSide;
    private ComboBox? _cmbRefresh;
    private ListBox? _lstWhitelist;
    private ListBox? _lstBlacklist;

    public SettingsForm(SettingsManager settings, WhitelistManager whitelist)
    {
        _settings = settings;
        _whitelist = whitelist;
        InitializeComponent();
        this.Shown += (s, e) => LoadSettingsToUI();
    }

    private void InitializeComponent()
    {
        this.Text = "TrayKiller 设置";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(480, 600);
        this.BackColor = Color.FromArgb(42, 42, 50);
        this.ForeColor = Color.White;
        this.Font = new Font("Segoe UI", 9f);

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 9,
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        int row = 0;

        // -- 功能区标题 --
        var lblFunc = new Label
        {
            Text = "功能开关",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255),
            AutoSize = true,
        };
        mainPanel.Controls.Add(lblFunc, 0, row++);
        mainPanel.SetColumnSpan(lblFunc, 2);

        // 开机自启
        mainPanel.Controls.Add(new Label { Text = "开机自启", AutoSize = true }, 0, row);
        _chkAutoStart = new CheckBox { AutoSize = true, Checked = false };
        _chkAutoStart.CheckedChanged += (s, e) =>
        {
            _settings.Data.AutoStart = _chkAutoStart.Checked;
            SetAutoStart(_chkAutoStart.Checked);
            _settings.Save();
        };
        mainPanel.Controls.Add(_chkAutoStart, 1, row++);

        // 贴边自动隐藏
        mainPanel.Controls.Add(new Label { Text = "贴边自动隐藏", AutoSize = true }, 0, row);
        _chkAutoHide = new CheckBox { AutoSize = true, Checked = true };
        _chkAutoHide.CheckedChanged += (s, e) =>
        {
            _settings.Data.AutoHide = _chkAutoHide.Checked;
            _settings.Save();
        };
        mainPanel.Controls.Add(_chkAutoHide, 1, row++);

        // -- 关闭策略 --
        var lblKill = new Label
        {
            Text = "关闭策略",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
        };
        mainPanel.Controls.Add(lblKill, 0, row);
        mainPanel.SetColumnSpan(lblKill, 2);
        row++;

        mainPanel.Controls.Add(new Label { Text = "关闭模式", AutoSize = true }, 0, row);
        _chkForceKill = new CheckBox
        {
            Text = "超时后强制终止（推荐）",
            AutoSize = true,
            Checked = true,
        };
        _chkForceKill.CheckedChanged += (s, e) =>
        {
            _settings.Data.ForceKillAfterTimeout = _chkForceKill.Checked;
            _settings.Save();
        };
        mainPanel.Controls.Add(_chkForceKill, 1, row++);

        // -- 停靠位置 --
        var lblDock = new Label
        {
            Text = "界面布局",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
        };
        mainPanel.Controls.Add(lblDock, 0, row);
        mainPanel.SetColumnSpan(lblDock, 2);
        row++;

        mainPanel.Controls.Add(new Label { Text = "停靠位置", AutoSize = true }, 0, row);
        _cmbDockSide = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Items = { "右侧", "左侧" },
            SelectedIndex = 1,
            Width = 120,
        };
        _cmbDockSide.SelectedIndexChanged += (s, e) =>
        {
            _settings.Data.DockSide = _cmbDockSide.SelectedIndex == 1 ? "Left" : "Right";
            _settings.Save();
        };
        mainPanel.Controls.Add(_cmbDockSide, 1, row++);

        // -- 自动刷新 --
        var lblRefresh = new Label
        {
            Text = "自动刷新",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
        };
        mainPanel.Controls.Add(lblRefresh, 0, row);
        mainPanel.SetColumnSpan(lblRefresh, 2);
        row++;

        mainPanel.Controls.Add(new Label { Text = "刷新间隔", AutoSize = true }, 0, row);
        _cmbRefresh = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Items = { "关闭", "10 秒", "30 秒", "1 分钟", "5 分钟" },
            Width = 120,
        };
        _cmbRefresh.SelectedIndexChanged += (s, e) =>
        {
            int[] values = { 0, 10, 30, 60, 300 };
            _settings.Data.AutoRefreshIntervalSeconds = values[_cmbRefresh.SelectedIndex];
            _settings.Save();
        };
        mainPanel.Controls.Add(_cmbRefresh, 1, row++);

        // -- 保存/取消按钮 --
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Padding = new Padding(12, 8, 12, 12),
            Height = 44,
        };
        var btnClose = new Button
        {
            Text = "关闭",
            Size = new Size(80, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 70),
            ForeColor = Color.White,
        };
        btnClose.Click += (s, e) => this.Close();
        btnPanel.Controls.Add(btnClose);

        // -- 名单管理区域（使用 TabControl）--
        var tabControl = new TabControl
        {
            Dock = DockStyle.Bottom,
            Height = 260,
            BackColor = Color.FromArgb(32, 32, 36),
        };

        // 白名单 Tab
        var whitelistTab = new TabPage("白名单（受保护进程）");
        _lstWhitelist = CreateListBoxWithAddRemove(true);
        whitelistTab.Controls.Add(_lstWhitelist);
        whitelistTab.Controls.Add(CreateAddPanel(true));
        tabControl.TabPages.Add(whitelistTab);

        // 黑名单 Tab
        var blacklistTab = new TabPage("黑名单（禁止保护）");
        _lstBlacklist = CreateListBoxWithAddRemove(false);
        blacklistTab.Controls.Add(_lstBlacklist);
        blacklistTab.Controls.Add(CreateAddPanel(false));
        tabControl.TabPages.Add(blacklistTab);

        this.Controls.Add(mainPanel);
        this.Controls.Add(tabControl);
        this.Controls.Add(btnPanel);
    }

    private ListBox CreateListBoxWithAddRemove(bool isWhitelist)
    {
        var lb = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(50, 50, 58),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f),
        };
        lb.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Delete && lb.SelectedItem != null)
            {
                var name = lb.SelectedItem.ToString()!;
                if (isWhitelist)
                    _whitelist.RemoveFromWhitelist(name);
                else
                    _whitelist.RemoveFromBlacklist(name);
                _settings.Save();
                RefreshLists();
            }
        };
        return lb;
    }

    private Panel CreateAddPanel(bool isWhitelist)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(4),
        };
        var txt = new TextBox
        {
            Width = 160,
            Location = new Point(4, 5),
            PlaceholderText = "输入进程名，如 WeChat.exe",
            BackColor = Color.FromArgb(60, 60, 68),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        var btn = new Button
        {
            Text = "添加",
            Size = new Size(52, 24),
            Location = new Point(170, 5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 100, 180),
            ForeColor = Color.White,
        };
        btn.Click += (s, e) =>
        {
            var name = txt.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (isWhitelist)
                _whitelist.AddToWhitelist(name);
            else
                _whitelist.AddToBlacklist(name);
            _settings.Save();
            RefreshLists();
            txt.Clear();
        };
        panel.Controls.Add(txt);
        panel.Controls.Add(btn);
        return panel;
    }

    private void LoadSettingsToUI()
    {
        _chkAutoStart!.Checked = _settings.Data.AutoStart;
        _chkAutoHide!.Checked = _settings.Data.AutoHide;
        _chkForceKill!.Checked = _settings.Data.ForceKillAfterTimeout;
        _cmbDockSide!.SelectedIndex = _settings.Data.DockSide == "Left" ? 1 : 0;

        var seconds = _settings.Data.AutoRefreshIntervalSeconds;
        int[] values = { 0, 10, 30, 60, 300 };
        int idx = Array.IndexOf(values, seconds);
        _cmbRefresh!.SelectedIndex = idx >= 0 ? idx : 0;

        RefreshLists();
    }

    private void RefreshLists()
    {
        _lstWhitelist!.Items.Clear();
        _lstWhitelist.Items.AddRange(_settings.Data.CustomWhitelist.ToArray());

        _lstBlacklist!.Items.Clear();
        _lstBlacklist.Items.AddRange(_settings.Data.CustomBlacklist.ToArray());
    }

    #region 开机自启
    private static void SetAutoStart(bool enable)
    {
        var appPath = Application.ExecutablePath;
        var keyName = "TrayKiller";

        try
        {
            if (enable)
            {
                IntPtr hKey;
                var result = NativeMethods.RegOpenKeyEx(
                    NativeMethods.HKEY_CURRENT_USER,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    0, NativeMethods.KEY_WRITE, out hKey);
                if (result == 0)
                {
                    var data = System.Text.Encoding.Unicode.GetBytes(appPath + "\0");
                    NativeMethods.RegSetValueEx(hKey, keyName, 0, NativeMethods.REG_SZ, data, data.Length);
                    NativeMethods.RegCloseKey(hKey);
                }
            }
            else
            {
                IntPtr hKey;
                var result = NativeMethods.RegOpenKeyEx(
                    NativeMethods.HKEY_CURRENT_USER,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    0, NativeMethods.KEY_WRITE, out hKey);
                if (result == 0)
                {
                    NativeMethods.RegDeleteValue(hKey, keyName);
                    NativeMethods.RegCloseKey(hKey);
                }
            }
        }
        catch
        {
            // 注册表操作失败不崩溃
        }
    }
    #endregion
}
