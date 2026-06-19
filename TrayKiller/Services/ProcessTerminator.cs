using System.Diagnostics;
using System.Runtime.InteropServices;
using TrayKiller.Models;

namespace TrayKiller.Services;

/// <summary>
/// 进程关闭工具：温柔退出（WM_CLOSE）→ 等待2秒 → 强制终止。
/// </summary>
public class ProcessTerminator
{
    private readonly SettingsManager _settings;

    public ProcessTerminator(SettingsManager settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// 关闭指定进程。返回结果描述。
    /// </summary>
    public async Task<string> TerminateAsync(ProcessItem item)
    {
        try
        {
            using var process = Process.GetProcessById(item.ProcessId);

            // Step 1: 尝试温柔关闭（向主窗口发送 WM_CLOSE）
            if (item.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(item.MainWindowHandle))
            {
                NativeMethods.PostMessage(item.MainWindowHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            // Step 2: 等待进程自然退出
            var waited = await Task.Run(() => process.WaitForExit(2000));

            if (waited)
            {
                return $"已温柔关闭 {item.DisplayName}";
            }

            // Step 3: 超时后强制终止（用户可选择关闭此行为）
            if (!_settings.Data.ForceKillAfterTimeout)
            {
                return $"已发送关闭信号给 {item.DisplayName}（未启用强制终止）";
            }

            return ForceKill(item);
        }
        catch (ArgumentException)
        {
            return $"{item.DisplayName} 进程已退出";
        }
        catch (Exception ex)
        {
            return $"关闭 {item.DisplayName} 失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 强制终止进程。先尝试 OpenProcess + TerminateProcess，失败则回退到 Process.Kill()。
    /// </summary>
    private static string ForceKill(ProcessItem item)
    {
        // 方案 A：直接提权 OpenProcess（含 PROCESS_TERMINATE + PROCESS_QUERY_LIMITED_INFORMATION）
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_TERMINATE | 0x1000, // PROCESS_QUERY_LIMITED_INFORMATION
            false, item.ProcessId);

        if (hProcess != IntPtr.Zero)
        {
            try
            {
                if (NativeMethods.TerminateProcess(hProcess, 0))
                    return $"已强制终止 {item.DisplayName}";
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }

        // 方案 B：回退到 .NET Process.Kill()（内部自动处理权限提升）
        try
        {
            using var process = Process.GetProcessById(item.ProcessId);
            process.Kill();
            process.WaitForExit(1000);
            return $"已强制终止 {item.DisplayName}";
        }
        catch (InvalidOperationException)
        {
            return $"{item.DisplayName} 进程已退出";
        }
        catch (Exception ex)
        {
            return $"关闭 {item.DisplayName} 失败：{ex.Message}";
        }
    }
}
