using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TrayKiller.Models;

namespace TrayKiller.Services;

/// <summary>
/// 托盘图标枚举器。
/// Win10 中可见托盘图标已不存储于 ToolbarWindow32 按钮，
/// 所有图标均位于 NotifyIconOverflowWindow 下的 ToolbarWindow32 中。
/// 通过跨进程内存读取 TBBUTTON.iString 获取 tooltip 文字。
/// </summary>
public class TrayEnumerator
{
    /// <summary>
    /// 枚举系统托盘中的托盘图标，返回进程条目列表。
    /// </summary>
    public List<ProcessItem> Enumerate()
    {
        var items = new List<ProcessItem>();
        var seenPids = new HashSet<int>();

        // 主路径：NotifyIconOverflowWindow → ToolbarWindow32
        var hOverflow = NativeMethods.FindWindow("NotifyIconOverflowWindow", null);
        if (hOverflow != IntPtr.Zero)
        {
            var hTb = NativeMethods.FindWindowEx(hOverflow, IntPtr.Zero, "ToolbarWindow32", null);
            if (hTb != IntPtr.Zero)
                EnumerateToolbar(hTb, items, seenPids);
        }

        // 备用路径：Shell_TrayWnd → TrayNotifyWnd → SysPager → ToolbarWindow32
        var hShell = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (hShell != IntPtr.Zero)
        {
            var hTrayNotify = NativeMethods.FindWindowEx(hShell, IntPtr.Zero, "TrayNotifyWnd", null);
            if (hTrayNotify != IntPtr.Zero)
            {
                var hSysPager = NativeMethods.FindWindowEx(hTrayNotify, IntPtr.Zero, "SysPager", null);
                if (hSysPager != IntPtr.Zero)
                {
                    var hTb = NativeMethods.FindWindowEx(hSysPager, IntPtr.Zero, "ToolbarWindow32", null);
                    if (hTb != IntPtr.Zero)
                        EnumerateToolbar(hTb, items, seenPids);
                }
            }
        }

        return items;
    }

    /// <summary>
    /// 枚举指定 Toolbar 中的所有按钮，通过跨进程内存读取 tooltip。
    /// </summary>
    private void EnumerateToolbar(IntPtr hToolbar, List<ProcessItem> items, HashSet<int> seenPids)
    {
        var count = NativeMethods.SendMessage(hToolbar, NativeMethods.TB_BUTTONCOUNT,
            IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (count <= 0 || count > 256)
            return;

        // 获取 Toolbar 所属进程 ID（explorer.exe）
        NativeMethods.GetWindowThreadProcessId(hToolbar, out uint targetPid);
        if (targetPid == 0) return;

        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_ALL_ACCESS_SAFE, false, (int)targetPid);
        if (hProcess == IntPtr.Zero) return;

        try
        {
            int tbbSize = NativeMethods.TbbuttonNativeSize;
            int iStringOffset = IntPtr.Size == 8 ? 24 : 16;

            // 在 explorer 进程中分配 TBBUTTON 缓冲区
            var remoteBuf = NativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero,
                (uint)tbbSize, NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_READWRITE);
            if (remoteBuf == IntPtr.Zero) return;

            try
            {
                var zeroBuf = new byte[tbbSize];

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        // 清零远程缓冲区
                        NativeMethods.WriteProcessMemory(hProcess, remoteBuf, zeroBuf,
                            (uint)tbbSize, out _);

                        // 让 explorer 在当前进程写入 TBBUTTON 结构
                        var result = NativeMethods.SendMessage(hToolbar, NativeMethods.TB_GETBUTTON,
                            new IntPtr(i), remoteBuf);
                        if (result.ToInt32() == 0)
                            continue;

                        // 读回 TBBUTTON 数据
                        var tbbBuf = new byte[tbbSize];
                        NativeMethods.ReadProcessMemory(hProcess, remoteBuf, tbbBuf,
                            (uint)tbbSize, out _);

                        // 提取 iString 指针
                        long iStringPtr = IntPtr.Size == 8
                            ? BitConverter.ToInt64(tbbBuf, iStringOffset)
                            : BitConverter.ToInt32(tbbBuf, iStringOffset);

                        if (iStringPtr == 0 || iStringPtr == -1 || iStringPtr == 0xFFFFFFFF)
                            continue;

                        // 从 explorer 进程中读取文字（最多 128 字符 = 256 bytes Unicode）
                        var strBuf = new byte[256];
                        NativeMethods.ReadProcessMemory(hProcess, new IntPtr(iStringPtr),
                            strBuf, 256, out IntPtr bytesRead);

                        var tooltip = ExtractNullTerminatedString(strBuf, (int)bytesRead);
                        if (string.IsNullOrWhiteSpace(tooltip))
                            continue;

                        var (processName, processId) = ResolveProcess(tooltip);

                        if (processId > 0 && !seenPids.Add(processId))
                            continue;

                        items.Add(new ProcessItem
                        {
                            ProcessName = processName ?? "（未识别）",
                            ProcessId = processId,
                            TrayTooltip = tooltip,
                            IsWhitelisted = false,
                        });
                    }
                    catch
                    {
                        // 单个按钮读取失败不影响整体
                    }
                }
            }
            finally
            {
                NativeMethods.VirtualFreeEx(hProcess, remoteBuf, 0, NativeMethods.MEM_RELEASE);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// 从字节数组中提取 null 结尾的 Unicode 字符串，过滤掉尾部乱码。
    /// </summary>
    private static string ExtractNullTerminatedString(byte[] buf, int bytesRead)
    {
        // 找到第一个 null 宽字符 (U+0000 = two zero bytes)
        int nullPos = -1;
        for (int i = 0; i < bytesRead - 1; i += 2)
        {
            if (buf[i] == 0 && buf[i + 1] == 0)
            {
                nullPos = i;
                break;
            }
        }

        int len = nullPos >= 0 ? nullPos : bytesRead;
        return len > 0 ? Encoding.Unicode.GetString(buf, 0, len) : string.Empty;
    }

    /// <summary>
    /// 根据 ToolTip 文字反向查找进程。多级匹配策略：
    /// 1. 原始包含匹配
    /// 2. 去空格/下划线/连字符后包含匹配（处理 "MSI Afterburner" vs "MSIAfterburner"）
    /// 3. tooltip 拆词，逐 token 匹配进程名（排除短 token 和通用词）
    /// 4. tooltip token 匹配进程 FileDescription（如 "NVIDIA..." 匹配 nvcontainer）
    /// </summary>
    private static (string? name, int pid) ResolveProcess(string? tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
            return (null, 0);

        var processes = Process.GetProcesses();
        var tooltipLower = tooltip.ToLowerInvariant();

        // 准备缓存：进程名 + FileDescription（延迟加载，避免所有进程都读文件版本信息）
        var fileDescCache = new Dictionary<int, string>();

        // 去标点后的紧凑版（用于第2级匹配）
        var tooltipCompact = new string(tooltipLower.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray());
        if (tooltipCompact.Length == 0) return (null, 0);

        Process? bestMatch = null;

        foreach (var p in processes)
        {
            try
            {
                var pName = p.ProcessName.ToLowerInvariant();

                // 第1级：原始包含匹配
                if (tooltipLower.Contains(pName) || pName.Contains(tooltipLower))
                {
                    bestMatch = p;
                    break;
                }

                // 第2级：紧凑版包含匹配
                var pNameCompact = new string(pName.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray());
                if (pNameCompact.Length >= 3 &&
                    (tooltipCompact.Contains(pNameCompact) || pNameCompact.Contains(tooltipCompact)))
                {
                    bestMatch = p;
                    break;
                }

                // 第3级：tooltip 拆 token 匹配（仅用 ≥4 字符的 token）
                if (bestMatch != null) continue;
                var tokens = tooltipLower.Split(' ', '\n', '\r', '\t', ':', '-', '（', '）', '(', ')')
                    .Select(t => t.Trim())
                    .Where(t => t.Length >= 4)
                    .ToHashSet();

                // 排除太通用的 token
                var blacklist = new HashSet<string> { "版本", "version", "系统", "system", "通知", "安全", "硬件", "设备", "弹出" };
                tokens.ExceptWith(blacklist);

                if (tokens.Any(t => pName.Contains(t)))
                    bestMatch = p;

                // 第4级：tooltip token 匹配 FileDescription
                if (bestMatch != null) continue;
                if (!fileDescCache.TryGetValue(p.Id, out var fileDesc))
                {
                    try
                    {
                        fileDesc = p.MainModule?.FileVersionInfo?.FileDescription ?? "";
                    }
                    catch
                    {
                        fileDesc = "";
                    }
                    fileDescCache[p.Id] = fileDesc;
                }
                if (!string.IsNullOrWhiteSpace(fileDesc))
                {
                    var fileDescLower = fileDesc.ToLowerInvariant();
                    if (tokens.Any(t => fileDescLower.Contains(t)))
                        bestMatch = p;
                }
            }
            catch
            {
                // 跳过无权限访问的进程
            }
        }

        if (bestMatch != null)
            return (bestMatch.ProcessName + ".exe", bestMatch.Id);

        return (null, 0);
    }
}
