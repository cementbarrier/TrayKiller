using System.Linq;
using TrayKiller.Models;
using TrayKiller.Services;

namespace TrayKiller.UI;

/// <summary>
/// 主悬浮窗口：侧边面板 + 进程列表 + 垃圾桶拖拽区。
/// 核心交互：拖拽进程条目到垃圾桶区域 → 关闭进程。
/// </summary>
public partial class MainForm : Form
{
    private readonly TrayEnumerator _enumerator;
    private readonly ProcessTerminator _terminator;
    private readonly WhitelistManager _whitelist;
    private readonly SettingsManager _settings;

    private List<ProcessItem> _items = new();
    private System.Windows.Forms.Timer? _hideTimer;
    private System.Windows.Forms.Timer? _refreshTimer;
    private bool _isHidden = false;
    private bool _isDraggingOut = false;

    // 标题栏控件
    private Button? _refreshBtn;

    // 拖拽源
    private Point _dragStartPoint;
    private ListBox? _dragSource;

    // 进程图标缓存（PID → Image）
    private readonly Dictionary<int, Image> _iconCache = new();

    public MainForm(SettingsManager settings, WhitelistManager whitelist)
    {
        _settings = settings;
        _whitelist = whitelist;
        _enumerator = new TrayEnumerator();
        _terminator = new ProcessTerminator(settings);

        InitializeComponent();
        ApplySettings();
        SetupHideTimer();
        SetupRefreshTimer();
    }

    #region 初始化与设置

    private void InitializeComponent()
    {
        this.Text = "TrayKiller";
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.StartPosition = FormStartPosition.Manual;
        this.Size = new Size(280, 500);
        this.MinimumSize = new Size(220, 300);
        this.BackColor = Color.FromArgb(32, 32, 36);
        this.ForeColor = Color.White;
        this.Padding = new Padding(6);

        // -- 标题栏 --
        var titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(22, 22, 26),
        };
        var titleLabel = new Label
        {
            Text = "  TrayKiller",
            ForeColor = Color.FromArgb(200, 200, 210),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(6, 7),
        };
        _refreshBtn = new Button
        {
            Text = "↻ 刷新",
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = Color.FromArgb(60, 60, 70),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f),
            Size = new Size(56, 24),
            Location = new Point(200, 4),
            Cursor = Cursors.Hand,
        };
        _refreshBtn.Click += (s, e) => RefreshList();
        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(_refreshBtn);

        // -- 进程列表 --
        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(42, 42, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f),
            ItemHeight = 36,
            DrawMode = DrawMode.OwnerDrawFixed,
            AllowDrop = true,
        };
        listBox.DrawItem += ListBox_DrawItem;
        listBox.MouseDown += ListBox_MouseDown;
        listBox.MouseMove += ListBox_MouseMove;
        listBox.DragOver += ListBox_DragOver;
        _dragSource = listBox;

        // -- 垃圾桶拖拽目标区 --
        var trashPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            BackColor = Color.FromArgb(180, 50, 50),
            AllowDrop = true,
            Cursor = Cursors.Hand,
        };
        var trashLabel = new Label
        {
            Text = "🗑  拖拽到此处关闭进程",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
        };
        trashPanel.Controls.Add(trashLabel);
        trashPanel.DragEnter += TrashPanel_DragEnter;
        trashPanel.DragLeave += TrashPanel_DragLeave;
        trashPanel.DragDrop += TrashPanel_DragDrop;

        // -- 布局 --
        var mainPanel = new Panel { Dock = DockStyle.Fill };
        mainPanel.Controls.Add(listBox);
        mainPanel.Controls.Add(titleBar);

        this.Controls.Add(mainPanel);
        this.Controls.Add(trashPanel);

        // -- 窗口事件 --
        this.Load += MainForm_Load;
        this.FormClosing += MainForm_FormClosing;
        this.MouseEnter += (s, e) => ShowPanel();
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        RefreshList();
        PositionWindow();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
        }
    }

    private void ApplySettings()
    {
        if (_settings.Data.AutoHide)
            _hideTimer?.Start();
        else
        {
            _hideTimer?.Stop();
            ShowPanel();
        }
    }

    /// <summary>
    /// 设置自动隐藏定时器：鼠标离开窗口 800ms 后滑出屏幕
    /// </summary>
    private void SetupHideTimer()
    {
        _hideTimer = new System.Windows.Forms.Timer { Interval = 800 };
        _hideTimer.Tick += (s, e) =>
        {
            if (_settings.Data.AutoHide && !this.Bounds.Contains(Cursor.Position) && !_isDraggingOut)
                HidePanel();
        };
        this.MouseLeave += (s, e) =>
        {
            if (_settings.Data.AutoHide)
                _hideTimer?.Start();
        };
        this.MouseEnter += (s, e) =>
        {
            _hideTimer?.Stop();
            ShowPanel();
        };
    }

    #endregion

    #region 面板显示/隐藏

    private void PositionWindow()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;

        int x = 0, y = 0;
        bool useSaved = false;

        // 使用已保存的位置当且仅当：(1)坐标为有效屏幕坐标，(2)窗口在某一屏幕边缘（确保启动即是吸附状态）
        if (_settings.Data.PanelX >= 0 && _settings.Data.PanelY >= 0 &&
            Screen.AllScreens.Any(s => s.WorkingArea.Contains(_settings.Data.PanelX, _settings.Data.PanelY)))
        {
            // 先把窗口放到保存位置，再检测是否贴边
            this.Location = new Point(_settings.Data.PanelX, _settings.Data.PanelY);
            if (DetectDockedEdge() != null)
            {
                x = _settings.Data.PanelX;
                y = _settings.Data.PanelY;
                useSaved = true;
            }
        }

        if (!useSaved)
        {
            switch (_settings.Data.DockSide)
            {
                case "Left":
                    x = wa.Left;
                    y = wa.Top + (wa.Height - this.Height) / 2;
                    break;
                case "Right":
                    x = wa.Right - this.Width;
                    y = wa.Top + (wa.Height - this.Height) / 2;
                    break;
                case "Top":
                    x = wa.Left + (wa.Width - this.Width) / 2;
                    y = wa.Top;
                    break;
                case "Bottom":
                    x = wa.Left + (wa.Width - this.Width) / 2;
                    y = wa.Bottom - this.Height;
                    break;
                default:
                    x = wa.Right - this.Width;
                    y = wa.Top + (wa.Height - this.Height) / 2;
                    break;
            }
        }

        this.Location = new Point(x, y);
        _isHidden = false;
    }

    private void ShowPanel()
    {
        if (!_isHidden) return;
        _isHidden = false;
        this.Opacity = 1.0;

        // 分设 Left/Top 比直接设 Location 更可靠（某些 WinForms 版本 Location setter 不立即生效）
        this.Left = _origX;
        this.Top = _origY;
        this.BringToFront();

        RefreshList();
        StartRefreshTimer();
    }

    /// <summary>隐藏前的原始坐标，用于精确恢复</summary>
    private int _origX;
    private int _origY;

    private void HidePanel()
    {
        if (_isHidden) return;
        _isHidden = true;

        var edge = DetectDockedEdge();
        if (edge == null)
        {
            // 未贴边：仅降透明度，保持原位
            _origX = this.Left;
            _origY = this.Top;
            this.Opacity = 0.7;
            StopRefreshTimer();
            return;
        }

        // 先把窗口吸附到屏幕边缘（消除用户拖拽可能带来的越界偏移），再记录原始坐标
        SnapToEdge(edge);
        _origX = this.Left;
        _origY = this.Top;

        if (edge == "Bottom")
        {
            // 底部不下移，避免与任务栏重叠导致虚化。仅降透明度。
            this.Opacity = 0.3;
        }
        else
        {
            this.Opacity = 0.7;
            (int dx, int dy) = GetHideDisplacement(edge);
            this.Left = _origX + dx;
            this.Top = _origY + dy;
        }

        StopRefreshTimer();
    }

    /// <summary>将窗口吸附到指定的屏幕工作区边缘</summary>
    private void SnapToEdge(string edge)
    {
        var screen = Screen.FromPoint(this.Location);
        var wa = screen.WorkingArea;
        switch (edge)
        {
            case "Left":   this.Left = wa.Left; break;
            case "Right":  this.Left = wa.Right - this.Width; break;
            case "Top":    this.Top = wa.Top; break;
            case "Bottom": this.Top = wa.Bottom - this.Height; break;
        }
    }

    /// <summary>检测窗口当前实际贴在哪条边，不贴边返回 null</summary>
    private string? DetectDockedEdge()
    {
        var screen = Screen.FromPoint(this.Location);
        var wa = screen.WorkingArea;
        const int tolerance = 20;

        if (this.Left <= wa.Left + tolerance) return "Left";
        if (this.Right >= wa.Right - tolerance) return "Right";
        if (this.Top <= wa.Top + tolerance) return "Top";
        // 底部贴边已禁用
        return null;
    }

    /// <summary>计算贴边隐藏时的位移量。底部使用更大的可见条（30px），避免被任务栏遮挡导致无法触发弹出。</summary>
    private (int dx, int dy) GetHideDisplacement(string edge)
    {
        var screen = Screen.FromPoint(this.Location);
        var wa = screen.WorkingArea;
        const int sliver = 6;
        const int bottomSliver = 30;

        return edge switch
        {
            "Left"   => (-(this.Right - wa.Left - sliver), 0),
            "Right"  => (wa.Right - this.Left - sliver, 0),
            "Top"    => (0, -(this.Bottom - wa.Top - sliver)),
            "Bottom" => (0, wa.Bottom - this.Top - bottomSliver),
            _ => (0, 0),
        };
    }

    private bool IsAtDockEdge() => DetectDockedEdge() != null;

    private void SetupRefreshTimer()
    {
        _refreshTimer = new System.Windows.Forms.Timer();
        _refreshTimer.Tick += (s, e) => RefreshList();
    }

    private void StartRefreshTimer()
    {
        var interval = _settings.Data.AutoRefreshIntervalSeconds;
        if (interval <= 0) return;

        _refreshTimer!.Interval = interval * 1000;
        _refreshTimer.Start();
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Stop();
    }

    public void ShowPanelExplicit()
    {
        this.Show();
        ShowPanel();
        this.BringToFront();
    }

    /// <summary>
    /// 切换 DockSide 后重新定位到对应边缘
    /// </summary>
    public void RepositionToEdge()
    {
        _settings.Data.PanelX = -1;
        _settings.Data.PanelY = -1;
        _settings.Save();
        _isHidden = false;
        this.Opacity = 1.0;
        PositionWindow();
        RefreshList();
    }

    /// <summary>
    /// 退出前获取隐藏前的真实边缘坐标
    /// </summary>
    public Point GetRestoredPosition()
    {
        if (!_isHidden) return this.Location;
        return new Point(_origX, _origY);
    }

    #endregion

    #region 列表刷新

    /// <summary>
    /// 从进程可执行文件中提取图标，失败返回 null。
    /// 结果会用 PID 做内存缓存，避免重复提取；失败也缓存 null 防止反复重试。
    /// 优先走 MainModule，权限不足时降级使用 QueryFullProcessImageName。
    /// </summary>
    private Image? ExtractProcessIcon(int processId)
    {
        if (_iconCache.TryGetValue(processId, out var cached))
            return cached;

        // 标记已尝试，即使失败也缓存 null 防止反复重试
        _iconCache[processId] = null!;

        string? exePath = null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            exePath = process.MainModule?.FileName;
        }
        catch { /* 权限不足，降级到 P/Invoke */ }

        if (string.IsNullOrEmpty(exePath))
            exePath = GetProcessPathByApi(processId);

        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    var bitmap = icon.ToBitmap();
                    _iconCache[processId] = bitmap;
                    return bitmap;
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// 当 ProcessId=0 时，通过进程名回退查找图标。提取成功则缓存到该 PID，失败返回 null。
    /// </summary>
    private Image? ExtractProcessIconByName(string processName)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(processName);
            var procs = System.Diagnostics.Process.GetProcessesByName(name);
            if (procs.Length > 0)
                return ExtractProcessIcon(procs[0].Id);
        }
        catch { }
        return null;
    }

    private static string? GetProcessPathByApi(int processId)
    {
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                return sb.ToString();
        }
        catch { }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }

        return null;
    }

    public void RefreshList()
    {
        _items = _enumerator.Enumerate();
        foreach (var item in _items)
        {
            item.IsWhitelisted = _whitelist.IsProtected(item);
        }

        // 去重：同一进程名只保留一条（优先保留有 PID 的）
        var deduped = new List<ProcessItem>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
        {
            if (!seenNames.Add(item.ProcessName))
            {
                // 同名已存在，仅在当前条目有 PID 且已有条目无 PID 时替换
                var existing = deduped.Find(x => string.Equals(x.ProcessName, item.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing.ProcessId == 0 && item.ProcessId > 0)
                {
                    deduped.Remove(existing);
                    deduped.Add(item);
                }
                continue;
            }
            deduped.Add(item);
        }
        _items = deduped;

        // 提取进程图标
        foreach (var item in _items)
        {
            if (item.ProcessId > 0)
                item.TrayIcon = ExtractProcessIcon(item.ProcessId);
            else if (!string.IsNullOrEmpty(item.ProcessName) && item.ProcessName != "（未识别）")
                item.TrayIcon = ExtractProcessIconByName(item.ProcessName);
        }

        _dragSource!.BeginUpdate();
        _dragSource.Items.Clear();
        foreach (var item in _items)
        {
            _dragSource.Items.Add(item);
        }
        _dragSource.EndUpdate();
    }

    private void ListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _dragSource!.Items[e.Index] is not ProcessItem item) return;

        e.DrawBackground();
        var g = e.Graphics;
        var rect = e.Bounds;
        rect.Inflate(-4, -2);

        // 图标占位
        var iconRect = new Rectangle(rect.X + 2, rect.Y + 4, 24, 24);
        if (item.TrayIcon != null)
            g.DrawImage(item.TrayIcon, iconRect);
        else
            g.FillRectangle(Brushes.Gray, iconRect);

        // 显示名称
        var textColor = item.IsWhitelisted ? Color.FromArgb(255, 200, 100) : Color.White;
        using var textBrush = new SolidBrush(textColor);
        g.DrawString(item.DisplayName, e.Font!, textBrush,
            rect.X + 32, rect.Y + 4);

        // PID 小字
        using var pidBrush = new SolidBrush(Color.FromArgb(140, 140, 150));
        using var smallFont = new Font("Segoe UI", 7.5f);
        g.DrawString($"PID: {item.ProcessId}", smallFont, pidBrush,
            rect.X + 32, rect.Y + 20);

        if (item.IsWhitelisted)
        {
            using var lockFont = new Font("Segoe UI", 8f, FontStyle.Bold);
            var lockText = "🔒";
            var lockSize = g.MeasureString(lockText, lockFont);
            g.DrawString(lockText, lockFont, textBrush,
                rect.Right - lockSize.Width - 4, rect.Y + (rect.Height - lockSize.Height) / 2);
        }
    }

    #endregion

    #region 拖拽逻辑

    private void ListBox_MouseDown(object? sender, MouseEventArgs e)
    {
        _dragStartPoint = e.Location;
    }

    private void ListBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var dx = Math.Abs(e.X - _dragStartPoint.X);
        var dy = Math.Abs(e.Y - _dragStartPoint.Y);
        if (dx < SystemInformation.DragSize.Width && dy < SystemInformation.DragSize.Height)
            return;

        var index = _dragSource!.IndexFromPoint(_dragStartPoint);
        if (index < 0 || index >= _items.Count) return;

        var item = _items[index];
        _isDraggingOut = true;
        _dragSource.DoDragDrop(item, DragDropEffects.Move);
        _isDraggingOut = false;
    }

    private void ListBox_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
    }

    private void TrashPanel_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data!.GetDataPresent(typeof(ProcessItem)))
        {
            e.Effect = DragDropEffects.Move;
            if (sender is Panel p)
                p.BackColor = Color.FromArgb(220, 40, 40);
        }
    }

    private void TrashPanel_DragLeave(object? sender, EventArgs e)
    {
        if (sender is Panel p)
            p.BackColor = Color.FromArgb(180, 50, 50);
    }

    private async void TrashPanel_DragDrop(object? sender, DragEventArgs e)
    {
        if (sender is Panel p)
            p.BackColor = Color.FromArgb(180, 50, 50);

        if (e.Data!.GetData(typeof(ProcessItem)) is not ProcessItem item) return;

        // 检查白名单
        if (_whitelist.IsProtected(item))
        {
            ShowBubble($"已保护：{_whitelist.GetProtectionReason(item)}");
            return;
        }

        var result = await _terminator.TerminateAsync(item);
        ShowBubble(result);
        RefreshList();
    }

    private void ShowBubble(string message)
    {
        var bubble = new ToolTip
        {
            IsBalloon = false,
            InitialDelay = 0,
            ShowAlways = true,
        };
        bubble.Show(message, this, this.Width / 2 - 60, this.Height - 80, 2500);
    }

    #endregion

    #region 窗口拖动

    private const int WM_NCHITTEST = 0x84;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                var pt = PointToClient(new Point(m.LParam.ToInt32()));
                if (pt.Y <= 32 && (_refreshBtn == null || !_refreshBtn.Bounds.Contains(pt)))
                {
                    // 虚化状态下拖动会导致窗口行为异常，先恢复
                    if (_isHidden) ShowPanel();
                    m.Result = (IntPtr)HTCAPTION;
                }
            }
            return;
        }
        base.WndProc(ref m);
    }

    #endregion
}
