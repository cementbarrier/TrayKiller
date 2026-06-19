using TrayKiller.Models;

namespace TrayKiller.Services;

/// <summary>
/// 系统关键进程白名单管理。
/// 内建基础白名单 + 用户自定义白名单/黑名单。
/// 判断顺序：系统白名单 > 用户黑名单 > 用户白名单。
/// ———— 可自定义修改位置：BuildSystemWhitelist() 方法内的列表 ————
/// </summary>
public class WhitelistManager
{
    private readonly SettingsManager _settings;
    private readonly HashSet<string> _systemWhitelist; // 小写进程名

    public WhitelistManager(SettingsManager settings)
    {
        _settings = settings;
        _systemWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BuildSystemWhitelist();
    }

    /// <summary>
    /// 【可自定义修改】内建系统关键进程白名单。
    /// 以下进程拖拽时会被拦截，不执行任何操作。
    /// </summary>
    private void BuildSystemWhitelist()
    {
        // -- Windows Shell / 桌面核心 --
        _systemWhitelist.Add("explorer.exe");      // 资源管理器（任务栏、桌面）
        _systemWhitelist.Add("sihost.exe");        // Shell Infrastructure Host

        // -- 系统托盘核心服务 --
        _systemWhitelist.Add("sndvol.exe");        // 音量控制
        _systemWhitelist.Add("sndvolsso.exe");     // 音量合成器
        _systemWhitelist.Add("mscorsvw.exe");      // .NET 优化服务

        // -- 输入法 --
        _systemWhitelist.Add("ctfmon.exe");        // 输入法/语言栏
        _systemWhitelist.Add("textinputhost.exe"); // 触摸键盘/输入

        // -- 网络 --
        _systemWhitelist.Add("networkservice.exe");// 网络服务（少见，预留）

        // -- 安全与系统 --
        _systemWhitelist.Add("securityhealthsystray.exe"); // Windows 安全中心
        _systemWhitelist.Add("securityhealthservice.exe");
        _systemWhitelist.Add("windowsdefender.exe");
        _systemWhitelist.Add("taskmgr.exe");       // 任务管理器
        _systemWhitelist.Add("conhost.exe");       // 控制台宿主

        // -- 本程序自身 --
        _systemWhitelist.Add("traykiller.exe");    // 防止误杀自身
    }

    /// <summary>
    /// 判断进程是否被保护（不可关闭）。
    /// </summary>
    public bool IsProtected(ProcessItem item)
    {
        var name = item.ProcessName;

        // 1. 系统白名单直接拦截
        if (_systemWhitelist.Contains(name))
            return true;

        // 2. 用户黑名单（即使不在系统白名单，用户标记永不关闭）
        if (_settings.Data.CustomBlacklist.Any(
                b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase)))
            return true;

        // 3. 用户白名单（额外保护）
        if (_settings.Data.CustomWhitelist.Any(
                w => string.Equals(w, name, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>
    /// 获取保护原因描述
    /// </summary>
    public string GetProtectionReason(ProcessItem item)
    {
        var name = item.ProcessName;
        if (_systemWhitelist.Contains(name))
            return "系统关键进程，已自动保护";
        if (_settings.Data.CustomBlacklist.Any(
                b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase)))
            return "用户黑名单保护";
        if (_settings.Data.CustomWhitelist.Any(
                w => string.Equals(w, name, StringComparison.OrdinalIgnoreCase)))
            return "用户白名单保护";
        return string.Empty;
    }

    public void AddToWhitelist(string processName) => _settings.Data.CustomWhitelist.Add(processName);
    public void RemoveFromWhitelist(string processName) => _settings.Data.CustomWhitelist.Remove(processName);
    public void AddToBlacklist(string processName) => _settings.Data.CustomBlacklist.Add(processName);
    public void RemoveFromBlacklist(string processName) => _settings.Data.CustomBlacklist.Remove(processName);
}
