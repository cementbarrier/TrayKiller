using TrayKiller.Services;
using TrayKiller.UI;

namespace TrayKiller;

/// <summary>
/// 应用程序上下文：管理系统托盘图标、主窗口和设置窗口。
/// 程序最小化到托盘，通过托盘右键菜单控制。
/// </summary>
public class AppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsManager _settings;
    private readonly WhitelistManager _whitelist;

    private MainForm? _mainForm;
    private SettingsForm? _settingsForm;

    public AppContext()
    {
        _settings = new SettingsManager();
        _whitelist = new WhitelistManager(_settings);

        // 系统托盘图标（使用应用自身图标）
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // 可替换为自定义 .ico
            Text = "TrayKiller - 托盘进程管理",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };
        _notifyIcon.DoubleClick += (s, e) => ShowMainPanel();

        // 启动时显示主面板
        ShowMainPanel();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("打开面板");
        openItem.Click += (s, e) => ShowMainPanel();
        menu.Items.Add(openItem);

        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (s, e) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (s, e) => ExitApplication();
        menu.Items.Add(exitItem);

        return menu;
    }

    public void ShowMainPanel()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_settings, _whitelist);
            _mainForm.FormClosed += (s, e) =>
            {
                _mainForm = null;
            };
        }

        _mainForm.ShowPanelExplicit();
    }

    private void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings, _whitelist);
            _settingsForm.FormClosed += (s, e) =>
            {
                _settingsForm = null;
                // 设置变更后刷新主面板
                _mainForm?.RefreshList();
            };
        }

        _settingsForm.Show();
        _settingsForm.BringToFront();
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _mainForm?.Close();
        _settingsForm?.Close();

        // 保存窗口位置
        if (_mainForm != null && !_mainForm.IsDisposed)
        {
            _settings.Data.PanelX = _mainForm.Location.X;
            _settings.Data.PanelY = _mainForm.Location.Y;
        }
        _settings.Save();

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
