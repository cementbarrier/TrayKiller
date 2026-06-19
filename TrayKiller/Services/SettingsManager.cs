using System.Text.Json;
using System.Windows.Forms;

namespace TrayKiller.Services;

/// <summary>
/// 应用设置持久化管理。使用 JSON 文件存储，路径为 %AppData%\TrayKiller\settings.json。
/// </summary>
public class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayKiller");

    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>当前设置实例</summary>
    public AppSettings Data { get; private set; } = new();

    public SettingsManager()
    {
        Directory.CreateDirectory(SettingsDir);
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                Data = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            Data = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Data, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置保存失败：{ex.Message}\n\n文件：{SettingsFile}",
                "TrayKiller - 保存错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void Reset()
    {
        Data = new AppSettings();
        Save();
    }
}

/// <summary>
/// 应用设置数据模型
/// </summary>
public class AppSettings
{
    /// <summary>是否开机自启</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>是否启用贴边自动隐藏</summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>关闭模式：true=温柔+超时强制, false=仅温柔关闭</summary>
    public bool ForceKillAfterTimeout { get; set; } = true;

    /// <summary>悬浮窗口停靠位置：Left / Right</summary>
    public string DockSide { get; set; } = "Right";

    /// <summary>用户自定义白名单（进程名，不区分大小写）</summary>
    public List<string> CustomWhitelist { get; set; } = new();

    /// <summary>用户自定义黑名单（进程名，不区分大小写）</summary>
    public List<string> CustomBlacklist { get; set; } = new();

    /// <summary>面板最后位置 X</summary>
    public int PanelX { get; set; } = -1;

    /// <summary>面板最后位置 Y</summary>
    public int PanelY { get; set; } = -1;

    /// <summary>自动刷新间隔（秒）。0=关闭自动刷新。</summary>
    public int AutoRefreshIntervalSeconds { get; set; } = 0;
}
