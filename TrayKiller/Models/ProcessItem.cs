using System.Drawing;

namespace TrayKiller.Models;

/// <summary>
/// 托盘进程条目数据模型
/// </summary>
public class ProcessItem
{
    /// <summary>进程名称（如 "WeChat.exe"）</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>进程 ID</summary>
    public int ProcessId { get; set; }

    /// <summary>托盘图标提示文字（ToolTip 内容）</summary>
    public string TrayTooltip { get; set; } = string.Empty;

    /// <summary>主窗口句柄（找到的第一个可见主窗口）</summary>
    public IntPtr MainWindowHandle { get; set; } = IntPtr.Zero;

    /// <summary>托盘图标图像（从 Toolbar 按钮提取）</summary>
    public Image? TrayIcon { get; set; }

    /// <summary>是否在白名单中</summary>
    public bool IsWhitelisted { get; set; }

    /// <summary>显示名称（优先 Tooltip，回退进程名）</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(TrayTooltip) ? TrayTooltip : ProcessName;

    public override string ToString() => $"[{ProcessId}] {DisplayName}";
}
