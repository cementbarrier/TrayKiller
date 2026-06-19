---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: a72a634ca6e5090ac9aedc6460e39d68_d4f1c4c06ba211f1a99c5254007bceed
    ReservedCode1: 6/yx8zTE7xY3IeEuFf7KquUn5buv+DvdRwIKE/z7HRmIIoMNuL+UbqduWsJqQYXrPEmwQdNNQmQHtj4pRS2ESHnlAXVNlLTX9uFmzOwk6ePHlxz1TXgDJhbkYhwmFUvT02CZZ2cIusNm1BS026Q06STr3XSk6ecH66CYykbKkB89od0CJ+bdbFy7ZGM=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: a72a634ca6e5090ac9aedc6460e39d68_d4f1c4c06ba211f1a99c5254007bceed
    ReservedCode2: 6/yx8zTE7xY3IeEuFf7KquUn5buv+DvdRwIKE/z7HRmIIoMNuL+UbqduWsJqQYXrPEmwQdNNQmQHtj4pRS2ESHnlAXVNlLTX9uFmzOwk6ePHlxz1TXgDJhbkYhwmFUvT02CZZ2cIusNm1BS026Q06STr3XSk6ecH66CYykbKkB89od0CJ+bdbFy7ZGM=
---

# TrayKiller 技术学习文档

> **版本**：基于 2026-06-19 代码快照
> **目标框架**：.NET 9.0 (Windows)
> **项目类型**：WinForms 桌面应用（系统托盘进程管理工具）

---

## 目录

1. [项目架构概览](#1-项目架构概览)
2. [Win32 API P/Invoke 全景](#2-win32-api-pinvoke-全景)
3. [托盘图标枚举机制](#3-托盘图标枚举机制)
4. [进程匹配策略](#4-进程匹配策略)
5. [进程图标提取](#5-进程图标提取)
6. [进程终止](#6-进程终止)
7. [.NET 技术栈](#7-net-技术栈)
8. [设计模式与架构](#8-设计模式与架构)
9. [缓存与去重策略](#9-缓存与去重策略)
10. [设置持久化与注册表](#10-设置持久化与注册表)
11. [发布与部署](#11-发布与部署)

---

## 1. 项目架构概览

### 1.1 项目结构

```
TrayKiller/
├── Program.cs                  # 入口：单例 Mutex → ApplicationContext
├── AppContext.cs               # 应用上下文：托盘图标 + 窗口生命周期
├── TrayKiller.csproj           # .NET 9 WinForms 项目配置
├── Models/
│   └── ProcessItem.cs          # 数据模型：托盘进程条目
├── Services/
│   ├── NativeMethods.cs        # P/Invoke 声明集中管理
│   ├── TrayEnumerator.cs       # 托盘图标枚举 + 进程匹配
│   ├── ProcessTerminator.cs    # 进程关闭（温柔→强制）
│   ├── SettingsManager.cs      # JSON 配置持久化
│   └── WhitelistManager.cs     # 系统白名单 + 用户黑白名单
└── UI/
    ├── MainForm.cs             # 主悬浮面板 + 拖拽交互 + 图标提取
    └── SettingsForm.cs         # 设置窗口 + 名单管理
```

### 1.2 启动流程

| 步骤 | 位置 | 说明 |
|------|------|------|
| 单例检测 | `Program.cs` | 命名 Mutex `TrayKiller_SingleInstance` |
| 初始化 | `AppContext` 构造 | 加载设置 → 创建托盘图标 → 显示主面板 |
| 托盘图标 | `AppContext` | 右键菜单：打开面板 / 设置 / 退出 |
| 主面板 | `MainForm` | 刷新托盘列表 → 提取图标 → 拖拽关闭 |

---

## 2. Win32 API P/Invoke 全景

### 2.1 模块分布（NativeMethods.cs）

所有 P/Invoke 集中在 `NativeMethods` 静态类中，按功能分 6 个区域。

#### 2.1.1 窗口查找与消息（user32.dll）

| API | 签名 | 用途 |
|-----|------|------|
| `FindWindow` | `(string? lpClassName, string? lpWindowName) → IntPtr` | 查找顶层窗口 |
| `FindWindowEx` | `(IntPtr parent, IntPtr after, string? cls, string? win) → IntPtr` | 查找子窗口 |
| `GetClassName` | `(IntPtr hWnd, StringBuilder clsName, int max) → int` | 获取窗口类名 |
| `SendMessage` | `(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr/ref TBBUTTON lParam)` | 发送消息（3 重载） |
| `GetWindowThreadProcessId` | `(IntPtr hWnd, out uint pid) → uint` | 窗口→线程ID+进程ID |
| `IsWindowVisible` | `(IntPtr hWnd) → bool` | 窗口可见性判断 |
| `IsWindow` | `(IntPtr hWnd) → bool` | 句柄有效性校验 |
| `PostMessage` | `(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam) → bool` | 异步发送消息 |

**SendMessage 三个重载的必要性**：

| 重载 | 适用场景 | 示例 |
|------|----------|------|
| `IntPtr lParam` | 整数返回值（TB_BUTTONCOUNT） | `SendMessage(hTb, TB_BUTTONCOUNT, 0, 0)` |
| `StringBuilder lParam` | 读取字符串（TB_GETBUTTONTEXTW） | 获取按钮文本 |
| `ref TBBUTTON lParam` | 读取结构体（TB_GETBUTTON） | 获取按钮完整信息 |

> **注意事项**：`SendMessage` 的 `CharSet = CharSet.Auto`——跨进程发送时 Windows 自动处理 ANSI/Unicode 编组。若误用 `CharSet.Ansi`，在 x64 系统上 TB_GETBUTTONTEXTW 等消息将返回乱码。

#### 2.1.2 进程操作（kernel32.dll）

| API | 用途 |
|-----|------|
| `OpenProcess(dwDesiredAccess, bInherit, dwPid) → IntPtr` | 获取进程句柄 |
| `CloseHandle(hObj) → bool` | 关闭句柄 |
| `TerminateProcess(hProcess, uExitCode) → bool` | 强制终止进程 |
| `VirtualAllocEx(hProcess, lpAddr, dwSize, flType, flProtect) → IntPtr` | 远程进程内存分配 |
| `VirtualFreeEx(hProcess, lpAddr, dwSize, dwFree) → bool` | 远程进程内存释放 |
| `ReadProcessMemory(hProcess, lpBase, buf, nSize, out bytes) → bool` | 读取远程进程内存 |
| `WriteProcessMemory(hProcess, lpBase, buf, nSize, out bytes) → bool` | 写入远程进程内存 |
| `QueryFullProcessImageName(hProcess, dwFlags, sb, ref size) → bool` | 获取进程可执行文件路径 |

#### 2.1.3 Toolbar 控件结构与消息常量

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TBBUTTON
{
    public int iBitmap;       // offset 0
    public int idCommand;     // offset 4
    public byte fsState;      // offset 8
    public byte fsStyle;      // offset 9
    public ushort bReserved;  // offset 10
    public IntPtr dwData;     // offset 12(x86) / 16(x64)
    public IntPtr iString;    // offset 16(x86) / 24(x64)
}
```

| 消息常量 | 值 | 含义 |
|----------|-----|------|
| `TB_BUTTONCOUNT` | `0x0418` | 获取按钮总数 |
| `TB_GETBUTTON` | `0x0417` | 获取指定按钮信息 (填 TBBUTTON) |
| `TB_GETBUTTONTEXTW` | `0x044B` | 获取按钮文本 (Unicode) |
| `TB_GETIMAGELIST` | `0x4002` | 获取图像列表句柄 |
| `TB_GETBITMAP` | `0x042B` | 获取按钮位图 |

#### 2.1.4 进程关闭消息

| 常量 | 值 | 说明 |
|------|-----|------|
| `WM_CLOSE` | `0x0010` | 请求关闭窗口 |
| `WM_QUIT` | `0x0012` | 退出消息循环 |

#### 2.1.5 DPI 与图标获取（shell32.dll）

| API | 用途 |
|-----|------|
| `GetSystemMetrics(nIndex) → int` | 获取系统度量（小图标尺寸） |
| `SHGetFileInfo(path, attr, ref SHFILEINFO, cb, flags) → IntPtr` | 获取文件/图标信息 |

| 度量索引 | 值 | 说明 |
|----------|-----|------|
| `SM_CXSMICON` | 49 | 小图标宽度（像素） |
| `SM_CYSMICON` | 50 | 小图标高度（像素） |

#### 2.1.6 窗口置顶与样式（user32.dll）

| API | 用途 |
|-----|------|
| `SetWindowPos(hWnd, hAfter, X, Y, cx, cy, flags) → bool` | 设置窗口位置/层次 |

| 常量 | 值/含义 |
|------|----------|
| `HWND_TOPMOST` | `new IntPtr(-1)` — 置顶 |
| `HWND_NOTOPMOST` | `new IntPtr(-2)` — 取消置顶 |
| `SWP_NOMOVE` | `0x0002` |
| `SWP_NOSIZE` | `0x0001` |
| `SWP_NOACTIVATE` | `0x0010` |
| `SWP_SHOWWINDOW` | `0x0040` |

#### 2.1.7 注册表操作（advapi32.dll）

| API | 用途 |
|-----|------|
| `RegOpenKeyEx(hKey, subKey, opt, sam, out hkResult) → int` | 打开注册表键 |
| `RegSetValueEx(hKey, name, reserved, type, data, cb) → int` | 写入键值 |
| `RegDeleteValue(hKey, name) → int` | 删除键值 |
| `RegCloseKey(hKey) → int` | 关闭键句柄 |

| 常量 | 值 |
|------|-----|
| `HKEY_CURRENT_USER` | `0x80000001` |
| `KEY_WRITE` | `0x20006` |
| `KEY_READ` | `0x20019` |
| `REG_SZ` | 1 |

---

## 3. 托盘图标枚举机制

### 3.1 窗口层级链

Windows 任务栏通知区域的窗口层级因版本而异。本项目采用双路径降级策略：

```
路径 A（Win10 1703+ 主路径）：
  NotifyIconOverflowWindow
    └── ToolbarWindow32

路径 B（Win10 早期 / Win7 备用路径）：
  Shell_TrayWnd
    └── TrayNotifyWnd
      └── SysPager
        └── ToolbarWindow32
```

> **版本差异背景**：Win10 1703 起，可见托盘图标全部移至 `NotifyIconOverflowWindow` 下的独立 Toolbar 中，不再存放于 `Shell_TrayWnd` 链路的原始 Toolbar。路径 B 可能仅包含隐藏/溢出区域的遗留图标。

### 3.2 枚举流程（TrayEnumerator.cs）

```
FindWindow("NotifyIconOverflowWindow", null)
  └→ FindWindowEx(..., "ToolbarWindow32", null)
       └→ EnumerateToolbar()

FindWindow("Shell_TrayWnd", null)
  └→ FindWindowEx(..., "TrayNotifyWnd", null)
       └→ FindWindowEx(..., "SysPager", null)
            └→ FindWindowEx(..., "ToolbarWindow32", null)
                 └→ EnumerateToolbar()
```

### 3.3 EnumerateToolbar 跨进程读取细节

Toolbar 控件属于 `explorer.exe` 进程，读取其按钮信息需要跨进程内存操作：

```
1. SendMessage(hToolbar, TB_BUTTONCOUNT, 0, 0) → 获取按钮数 N
2. OpenProcess(PROCESS_ALL_ACCESS_SAFE, false, explorerPid)
3. VirtualAllocEx(hExplorer, ...) → 在 explorer 中分配 TBBUTTON 大小的缓冲区
4. for i in 0..N:
   a. WriteProcessMemory → 清零远程缓冲区
   b. SendMessage(TB_GETBUTTON, i, remoteBuf) → 让 explorer 写入 TBBUTTON
   c. ReadProcessMemory → 读回 TBBUTTON 结构
   d. 提取 iString 指针（x86 offset 16, x64 offset 24）
   e. ReadProcessMemory → 读取 tooltip 字符串（最多 256 bytes Unicode）
   f. ExtractNullTerminatedString → 截断到第一个 \0\0
   g. ResolveProcess(tooltip) → 匹配进程
5. VirtualFreeEx(hExplorer, remoteBuf, 0, MEM_RELEASE)
6. CloseHandle(hExplorer)
```

### 3.4 TBBUTTON 结构体内存布局

| 字段 | 偏移(x86) | 偏移(x64) | 大小 | 说明 |
|------|-----------|-----------|------|------|
| `iBitmap` | 0 | 0 | 4 | 位图索引 |
| `idCommand` | 4 | 4 | 4 | 命令 ID |
| `fsState` | 8 | 8 | 1 | 按钮状态 |
| `fsStyle` | 9 | 9 | 1 | 按钮样式 |
| `bReserved` | 10 | 10 | 2 | 保留 |
| `dwData` | 12 | 16 | IntPtr | 自定义数据 |
| `iString` | 16 | 24 | IntPtr | **字符串指针** |
| **总大小** | **20** | **32** | — | `TbbuttonNativeSize` |

> **Pack = 1 的影响**：`StructLayout(LayoutKind.Sequential, Pack = 1)` 强制逐字节对齐，避免 CLR 在 64 位下对 `dwData` 和 `iString` 之间插入 4 字节 padding。但实际 native TBBUTTON 并未使用 Pack=1，因此项目额外定义 `TbbuttonNativeSize`（32 字节）覆盖此偏差。

> **偏移量手算的合理性**：`bReserved` 是 `ushort`（2 bytes），在 Pack=1 下 offset=10。`dwData`(IntPtr) 的偏移 = 10+2=12(x86) / 但 x64 下 IntPtr 要求 8 字节对齐，CLR 会在 Pack=1 下仍然强制对齐，导致实际偏移为 16。`iString` 偏移为 16(x86) / 24(x64)。

### 3.5 空终止字符串提取

```csharp
// 跨进程读取的 tooltip 是 Unicode 字符串（每字符 2 字节）
// 以 \0\0（null 宽字符）结尾
private static string ExtractNullTerminatedString(byte[] buf, int bytesRead)
{
    int nullPos = -1;
    for (int i = 0; i < bytesRead - 1; i += 2)
    {
        if (buf[i] == 0 && buf[i + 1] == 0)
        { nullPos = i; break; }
    }
    int len = nullPos >= 0 ? nullPos : bytesRead;
    return len > 0 ? Encoding.Unicode.GetString(buf, 0, len) : "";
}
```

> **注意**：必须按 2 字节为 stride 扫描，因为单字节 `0x00` 可能是中文字符的组成部分。

### 3.6 安全边界

| 防护 | 机制 |
|------|------|
| 按钮数上限 | `count > 256` 直接返回，防止异常 Toolbar 数据 |
| 指针有效性 | `iStringPtr == 0 || -1 || 0xFFFFFFFF` 跳过 |
| 空 tooltip | `string.IsNullOrWhiteSpace` 过滤 |
| 单按钮异常 | try-catch 包裹，不影响其他按钮 |
| 内存泄漏 | `VirtualFreeEx` + `CloseHandle` 在 finally 中保证释放 |

> **改进方向**：当前代码对 `PROCESS_ALL_ACCESS_SAFE` 请求权限制较高（含 VM_OPERATION + VM_READ + VM_WRITE），在某些安全软件保护下可能被拦截。可考虑先尝试 `PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_LIMITED_INFORMATION`，失败则降级为仅读取（放弃跨进程内存分配，改用 `SendMessage(TB_GETBUTTONTEXTW)` 获取文本——但该消息只能拿到字符串，无法获取 `dwData` 等其他字段）。

---

## 4. 进程匹配策略

### 4.1 问题本质

从托盘 Tooltip 文字（如 "WeChat"、"NVIDIA 设置"）反查对应进程名和 PID，无直接 API 可用，需自建匹配算法。

### 4.2 四级匹配策略（TrayEnumerator.ResolveProcess）

| 级别 | 策略 | 说明 | 示例 |
|------|------|------|------|
| **L1** | 原始包含匹配 | 双向包含判断 | tooltip="WeChat" ↔ pName="wechat" |
| **L2** | 紧凑版包含匹配 | 去除空格/下划线/连字符后匹配 | tooltip="MSI Afterburner" → compact="msiafterburner" ↔ "msiafterburner.exe" |
| **L3** | tooltip 拆 token 匹配 | 拆分 tooltip 为 ≥4 字符的 token，逐 token 匹配进程名 | tooltip="NVIDIA 设置" → tokens=["nvidia"] → 匹配 "nvcontainer" |
| **L4** | tooltip token 匹配 FileDescription | 读取进程的 FileVersionInfo.FileDescription 进行匹配 | tooltip="Realtek HD Audio Manager" → FileDescription="Realtek HD Audio Manager" |

> **L3 黑名单**：排除 "版本", "version", "系统", "system", "通知", "安全", "硬件", "设备", "弹出" 等通用词，避免误匹配。

### 4.3 FileDescription 延迟加载

```csharp
var fileDescCache = new Dictionary<int, string>();
// 仅在 L1-L3 都未命中时才读取 FileDescription
if (!fileDescCache.TryGetValue(p.Id, out var fileDesc))
{
    fileDesc = p.MainModule?.FileVersionInfo?.FileDescription ?? "";
    fileDescCache[p.Id] = fileDesc;
}
```

> **延迟加载的价值**：`Process.MainModule` 触发对每个枚举进程的模块加载，若对全部 200+ 进程都执行，每次刷新耗时可超 2 秒。延迟加载仅在 L3 未命中时才触发 L4，平均命中率 95% 以上，实际开销极小。

### 4.4 匹配优先级

- **一旦命中立即返回**（L1 > L2 > L3 > L4），不做全量遍历
- L1/L2 使用 `break` 立即退出
- L3/L4 使用 `bestMatch` 变量，仅在未命中时继续下一级

> **改进方向**：当前 L3 使用 `ToHashSet()` 对每个进程重新分配——虽然不影响正确性，但在 200+ 进程列表下存在 GC 压力。可将 token 提取移到循环外，避免重复分割 tooltip：
> ```csharp
> // 提取到循环外
> var tokens = tooltipLower.Split(...).Where(t => t.Length >= 4).ToHashSet();
> tokens.ExceptWith(blacklist);
> foreach (var p in processes) { ... }
> ```

---

## 5. 进程图标提取

### 5.1 双通道降级方案（MainForm.cs）

```
通道 A：Process.MainModule?.FileName
  └→ 成功 → Icon.ExtractAssociatedIcon(exePath) → icon.ToBitmap()

通道 B（降级）：QueryFullProcessImageName
  └→ OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid)
       └→ QueryFullProcessImageName(hProcess, dwFlags=0, sb, ref size)
            └→ Icon.ExtractAssociatedIcon(path) → icon.ToBitmap()
```

### 5.2 关键 P/Invoke 细节

```csharp
private static string? GetProcessPathByApi(int processId)
{
    var hProcess = NativeMethods.OpenProcess(
        NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
    //                                              ↑
    //          0x1000 — Vista+ 引入，权限远低于 PROCESS_QUERY_INFORMATION(0x0400)
    //          对受保护/高完整性进程（如系统服务）成功率更高

    ...
    NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size);
    //                                                 ↑
    //          dwFlags = 0 → 返回 Win32 路径格式（如 C:\Windows\System32\notepad.exe）
    //          dwFlags = 1 → 返回 NT 路径格式（\Device\HarddiskVolume3\...）
}
```

### 5.3 PROCESS_QUERY_LIMITED_INFORMATION 权限技巧

| 权限常量 | 值 | 说明 |
|----------|-----|------|
| `PROCESS_QUERY_INFORMATION` | `0x0400` | 完整查询权限（部分进程拒绝） |
| `PROCESS_QUERY_LIMITED_INFORMATION` | `0x1000` | Vista+ 引入的受限查询权限，被拒绝概率低得多 |

> `PROCESS_QUERY_LIMITED_INFORMATION` 足以支持 `QueryFullProcessImageName`、`GetProcessTimes`、`IsProcessInJob` 等基础查询，但不能读 PEB、不能读环境变量、不能获取 Token 信息。对于仅获取路径而言是最优选择。

### 5.4 Icon.ExtractAssociatedIcon 和 Icon.ToBitmap

```csharp
using var icon = Icon.ExtractAssociatedIcon(exePath);
if (icon != null)
{
    var bitmap = icon.ToBitmap();
    _iconCache[processId] = bitmap;
    return bitmap;
}
```

| API | 说明 | 注意事项 |
|-----|------|----------|
| `Icon.ExtractAssociatedIcon(path)` | 从 exe/dll 中提取关联图标 | 返回系统默认大小图标；被锁定文件不会失败 |
| `icon.ToBitmap()` | Icon→Bitmap 转换 | **会丢失 Alpha 通道**（Icon 含 32-bit ARGB，ToBitmap 后可能降为 24-bit RGB） |

> **坑点**：`Icon.ToBitmap()` 在 .NET 中会将 32-bit ARGB 图标扁平化为 24-bit RGB，导致透明区域变为黑色。如果需要在 UI 上呈现带透明通道的图标，应直接使用 `Icon` 对象而非 `Bitmap`，或在 DrawImage 时使用支持 Alpha 的 Graphics 设置。

> **改进方向**：当前代码在提取图标失败时返回 `null`，UI 层用灰色矩形占位。可考虑使用 `SystemIcons.Application` 作为最终回退图标，改善视觉体验。

### 5.5 PID=0 时的兜底策略

当 `ProcessId == 0`（来自 tooltip 但未能匹配到进程）时：

```csharp
if (item.ProcessId > 0)
    item.TrayIcon = ExtractProcessIcon(item.ProcessId);
else if (!string.IsNullOrEmpty(item.ProcessName) && item.ProcessName != "（未识别）")
    item.TrayIcon = ExtractProcessIconByName(item.ProcessName);
```

```csharp
// ExtractProcessIconByName: 通过进程名回退查找
private Image? ExtractProcessIconByName(string processName)
{
    var name = Path.GetFileNameWithoutExtension(processName);
    var procs = Process.GetProcessesByName(name);
    if (procs.Length > 0)
        return ExtractProcessIcon(procs[0].Id);
    return null;
}
```

> **风险**：`GetProcessesByName` 返回多个同名进程时只取第一个，图标可能不准确。但作为兜底方案可接受。

---

## 6. 进程终止

### 6.1 三级终止策略（ProcessTerminator.cs）

```
Step 1: PostMessage(mainWindowHandle, WM_CLOSE, 0, 0)
   ↓
Step 2: process.WaitForExit(2000) → 等待 2 秒
   ↓ 未退出
Step 3: ForceKill → 方案A 或 方案B
```

### 6.2 ForceKill 双通道

```
方案 A（P/Invoke，优先）：
  OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid)
    → TerminateProcess(hProcess, 0)

方案 B（.NET 兜底）：
  Process.GetProcessById(pid).Kill()
    → process.WaitForExit(1000)
```

### 6.3 关键 API 对比

| API | 来源 | 权限要求 | 返回值 | 副作用 |
|-----|------|----------|--------|--------|
| `TerminateProcess` | kernel32 | PROCESS_TERMINATE (0x0001) | bool | 无 DLL 卸载、无 finally 块执行 |
| `Process.Kill()` | .NET | 内部自动提权 | void | 内部调用 TerminateProcess，异常包装为 Win32Exception |
| `PostMessage(WM_CLOSE)` | user32 | 无特殊要求（同完整性级别即可） | bool | 等效于点击关闭按钮，进程可拒绝（如弹出保存对话框） |

### 6.4 异常处理

| 异常 | 触发场景 | 处理 |
|------|----------|------|
| `ArgumentException` | 进程已退出 | 返回 "进程已退出" |
| `InvalidOperationException` | Process 对象未关联进程 | 返回 "进程已退出" |
| 其他 `Exception` | 权限不足等 | 返回具体错误信息 |

### 6.5 白名单保护

终止前必须通过 `WhitelistManager.IsProtected()` 检查，判断顺序：

```
系统白名单（硬编码 12 个关键进程）
  → 用户黑名单（即使不在系统白名单，用户标记永不关闭）
    → 用户白名单（额外保护）
      → 允许终止
```

| 系统白名单 | 原因 |
|------------|------|
| `explorer.exe` | 资源管理器（任务栏、桌面崩溃会导致 Shell 重启） |
| `sihost.exe` | Shell Infrastructure Host |
| `ctfmon.exe` | 输入法/语言栏 |
| `textinputhost.exe` | 触摸键盘/输入 |
| `securityhealthsystray.exe` | Windows 安全中心 |
| `sndvol.exe` / `sndvolsso.exe` | 音量控制 |
| `taskmgr.exe` | 任务管理器 |
| `conhost.exe` | 控制台宿主 |
| `traykiller.exe` | 本程序自身 |

---

## 7. .NET 技术栈

### 7.1 ApplicationContext 模式

不同于传统的 `Application.Run(new Form())`，本项目使用 `ApplicationContext` 管理应用生命周期：

| 模式 | 适用场景 | 本项目应用 |
|------|----------|------------|
| `Application.Run(Form)` | 有关闭即退出的主窗口 | — |
| `Application.Run(ApplicationContext)` | 系统托盘应用、多窗口应用 | 托盘图标 + 多表单管理 |

```csharp
public class AppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private MainForm? _mainForm;
    private SettingsForm? _settingsForm;

    public AppContext()
    {
        _notifyIcon = new NotifyIcon { ... };
        ShowMainPanel();  // 启动时显示主面板
    }

    // 窗口关闭 ≠ 应用退出
    private void ExitApplication()
    {
        _notifyIcon.Dispose();
        Application.Exit();
    }
}
```

### 7.2 NotifyIcon（系统托盘）

```csharp
_notifyIcon = new NotifyIcon
{
    Icon = SystemIcons.Application,
    Text = "TrayKiller - 托盘进程管理",
    Visible = true,
    ContextMenuStrip = BuildContextMenu(),
};
_notifyIcon.DoubleClick += (s, e) => ShowMainPanel();
```

### 7.3 单例 Mutex

```csharp
using var mutex = new Mutex(true, "TrayKiller_SingleInstance", out bool createdNew);
if (!createdNew)
{
    MessageBox.Show("TrayKiller 已经在运行中。", ...);
    return;
}
```

| 特性 | 说明 |
|------|------|
| 命名 Mutex | `"TrayKiller_SingleInstance"` 全局唯一标识 |
| `createdNew` | `true` 表示首次创建，`false` 表示已存在 |
| `using` | 确保程序退出时自动释放，不残留内核对象 |

> **注意**：命名 Mutex 作用于**当前会话**（Terminal Services 下每个用户会话独立）。如果需要跨会话单例，需使用 `Global\` 前缀（需 SeCreateGlobalPrivilege 权限）。

### 7.4 WinForms 高级特性

#### 7.4.1 OwnerDraw 列表

```csharp
listBox.DrawMode = DrawMode.OwnerDrawFixed;
listBox.ItemHeight = 36;
listBox.DrawItem += ListBox_DrawItem;
```

项目渲染：图标(24×24) + 显示名称 + PID 小字 + 锁定标记。

#### 7.4.2 拖拽操作

```csharp
// 发起拖拽
_dragSource.DoDragDrop(item, DragDropEffects.Move);

// 目标接收
trashPanel.DragEnter += (s, e) => { e.Effect = DragDropEffects.Move; };
trashPanel.DragDrop += async (s, e) =>
{
    var item = (ProcessItem)e.Data.GetData(typeof(ProcessItem));
    await _terminator.TerminateAsync(item);
};
```

#### 7.4.3 无边框窗口 + 贴边隐藏

```csharp
this.FormBorderStyle = FormBorderStyle.None;
this.ShowInTaskbar = false;
this.TopMost = true;
```

贴边隐藏逻辑：

| 状态 | Opacity | 位置变化 |
|------|---------|----------|
| 显示 | `1.0` | Left→X+offset / Right→X-offset |
| 隐藏 | `0.7` | Left→X-offset / Right→X+offset |

> **改进方向**：当前使用 `Opacity` 实现半透明，但 WinForms 的 Opacity 会对整个窗口做逐像素 Alpha 混合，在低端 GPU 上可能有性能问题。可考虑使用 `Layered Window` API（`UpdateLayeredWindow`）做更高效的逐像素 Alpha 控制。

### 7.5 项目配置（.csproj）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>           <!-- Windows GUI 应用（无控制台窗口） -->
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>               <!-- 启用 NRT 空引用类型检查 -->
    <UseWindowsForms>true</UseWindowsForms>   <!-- 启用 WinForms SDK -->
    <ImplicitUsings>enable</ImplicitUsings>   <!-- 自动 using -->
  </PropertyGroup>
</Project>
```

### 7.6 JSON 序列化配置

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    //                                       ↑
    // 避免中文被转义为 \uXXXX，输出可读 JSON
};
```

> **UnsafeRelaxedJsonEscaping**：允许中文字符以原始形式写入 JSON，不用 `\uXXXX` 转义。代价是：如果 JSON 被嵌入 `<script>` 标签中可能有 XSS 风险，但桌面应用的本地配置文件不受此影响。

---

## 8. 设计模式与架构

### 8.1 模式总览

| 模式 | 应用位置 | 说明 |
|------|----------|------|
| **单例模式** | `Program.cs` 的 Mutex | 命名 Mutex 确保单实例 |
| **ApplicationContext** | `AppContext.cs` | 替代 Form 作为应用生命周期载体 |
| **服务定位/DI** | 构造函数注入 | `SettingsManager` → `WhitelistManager` → `MainForm` |
| **策略模式** | `ProcessTerminator` 三级终止 | 温柔关闭 → 等待 → 强制终止，可配置开关 |
| **代理模式** | `WhitelistManager` | 系统白名单 + 用户黑白名单三层过滤 |
| **模板方法** | `TrayEnumerator.Enumerate` | 双路径枚举 + 统一 `EnumerateToolbar` |
| **缓存模式** | `_iconCache` | PID→Image 字典缓存，失败也缓存 null |

### 8.2 依赖注入链

```
SettingsManager (JSON 文件)
    ↓
WhitelistManager(SettingsManager)
    ↓
MainForm(SettingsManager, WhitelistManager)
    ├── TrayEnumerator()
    └── ProcessTerminator(SettingsManager)
```

> **不是传统 DI 容器**，而是手动构造函数注入。对于这个规模的应用是合适的：依赖关系清晰、无循环依赖、易于测试。

### 8.3 数据流

```
TrayEnumerator.Enumerate()
    ↓ List<ProcessItem> (含 ProcessName, ProcessId, TrayTooltip)
MainForm.RefreshList()
    ├── WhitelistManager.IsProtected() → 标记 IsWhitelisted
    ├── 进程名去重
    ├── ExtractProcessIcon() → 提取图标
    └── ListBox 绑定
```

---

## 9. 缓存与去重策略

### 9.1 图标缓存：Dictionary<int, Image>

```csharp
private readonly Dictionary<int, Image> _iconCache = new();

private Image? ExtractProcessIcon(int processId)
{
    if (_iconCache.TryGetValue(processId, out var cached))
        return cached;                          // 命中缓存，直接返回

    _iconCache[processId] = null!;              // 标记已尝试

    // ... 提取逻辑 ...

    if (提取成功)
        _iconCache[processId] = bitmap;         // 更新为真实结果
    return bitmap;                              // 失败时缓存 null! 不变
}
```

| 策略 | 细节 | 价值 |
|------|------|------|
| **成功缓存** | 按 PID 存储 `Image` 对象 | 避免重复提取同一进程图标 |
| **失败也缓存** | 缓存 `null!` | 防止对已失败的 PID 反复重试（如权限不足的系统进程） |
| **生命周期** | 与 MainForm 实例同生命周期 | 切换 PID 的进程需刷新 |
| **内存释放** | 未显式 Dispose 缓存图标 | `Image` 实现了 IDisposable，长期可能导致 GDI 对象泄漏 |

> **改进方向**：
> 1. MainForm 关闭时为 `_iconCache.Values` 中非 null 的 Image 调用 Dispose。
> 2. 可增加 TTL 机制：若进程已退出，缓存应失效（`Process.GetProcessById(pid)` 抛 ArgumentException 时清除缓存条目）。

### 9.2 PID 去重：HashSet<int>

```csharp
var seenPids = new HashSet<int>();

// 在 EnumerateToolbar 中：
if (processId > 0 && !seenPids.Add(processId))
    continue;  // 同一 PID 已处理，跳过
```

> 同一个进程可能在双路径枚举中（或同一 Toolbar 的不同状态）被重复发现。`HashSet<int>` 确保每个 PID 只出现一次。

### 9.3 进程名去重：HashSet<string>

```csharp
var deduped = new List<ProcessItem>();
var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var item in _items)
{
    if (!seenNames.Add(item.ProcessName))
    {
        // 同名已存在 → 优先保留有 PID 的条目
        var existing = deduped.Find(x => ...);
        if (existing != null && existing.ProcessId == 0 && item.ProcessId > 0)
        {
            deduped.Remove(existing);
            deduped.Add(item);
        }
        continue;
    }
    deduped.Add(item);
}
```

| 去重维度 | 数据结构 | 时机 | 说明 |
|----------|----------|------|------|
| PID 去重 | `HashSet<int>` | 枚举阶段 | 避免同一进程因多路径枚举重复 |
| 进程名去重 | `HashSet<string>` | 刷新阶段 | 同一进程名可能对应多个 PID（如多个 Chrome 进程），只保留一个 |
| 有 PID 优先替换 | `deduped.Find` | 去重冲突时 | 若已有条目 PID=0 而新条目 PID>0，用新条目替换 |

> **改进方向**：`deduped.Find` 在每次冲突时做 O(n) 线性查找。对于 200 个条目的规模无关紧要，但如果需要扩展到 1000+ 条目，可将 `seenNames` 改为 `Dictionary<string, ProcessItem>`，直接用进程名做键。

---

## 10. 设置持久化与注册表

### 10.1 JSON 配置文件

```
路径：%AppData%\TrayKiller\settings.json
```

| 设置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `AutoStart` | bool | false | 开机自启 |
| `AutoHide` | bool | true | 贴边自动隐藏 |
| `ForceKillAfterTimeout` | bool | true | 超时后强制终止 |
| `DockSide` | string | "Right" | 停靠位置 |
| `PanelX` / `PanelY` | int | -1 | 面板位置 |
| `AutoRefreshIntervalSeconds` | int | 0 | 自动刷新间隔 |
| `CustomWhitelist` | List\<string\> | [] | 用户自定义白名单 |
| `CustomBlacklist` | List\<string\> | [] | 用户自定义黑名单 |

### 10.2 开机自启注册表操作

```csharp
// 写入：
HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
  └── "TrayKiller" = REG_SZ: "<exe完整路径>"

// 删除：
RegDeleteValue(hKey, "TrayKiller")
```

> **注意**：使用 `advapi32.dll` 原生 API 而非 `Microsoft.Win32.Registry` 类。原生 API 的优势：更精确的错误码（int 返回值）、无额外 BCL 层封装开销。但对于应用级别的注册表操作，`Registry` 类通常更简洁安全。

### 10.3 错误处理

```csharp
public void Save()
{
    try { ... File.WriteAllText ... }
    catch (Exception ex)
    {
        MessageBox.Show($"设置保存失败：{ex.Message}\n\n文件：{SettingsFile}", ...);
    }
}

public void Load()
{
    try { ... JsonSerializer.Deserialize ... }
    catch { Data = new AppSettings(); }  // 损坏的 JSON → 静默重置
}
```

---

## 11. 发布与部署

### 11.1 dotnet publish 单文件发布

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

| 参数 | 含义 |
|------|------|
| `-r win-x64` | 目标运行时 RID（Windows x64） |
| `--self-contained false` | 框架依赖模式（需安装 .NET 9 Runtime） |
| `-p:PublishSingleFile=true` | 打包为单个 exe 文件 |
| `-c Release` | Release 配置 |

### 11.2 发布产物

```
publish/
├── TrayKiller.exe              # 主程序（含所有程序集打包）
├── TrayKiller.deps.json        # 依赖描述清单
└── TrayKiller.runtimeconfig.json  # 运行时配置
```

### 11.3 框架依赖 vs 自包含

| 模式 | 参数 | exe 大小 | 部署要求 |
|------|------|----------|----------|
| 框架依赖（默认） | `--self-contained false` | ~200KB | 目标机器需安装 .NET 9 Runtime |
| 自包含 | `--self-contained true` | ~70MB | 无需安装 Runtime，但 exe 体积大 |

> 当前项目使用框架依赖模式，适合内部分发场景。

### 11.4 开机自启路径

```csharp
var appPath = Application.ExecutablePath;
// 写入 Run 键的值为 publish 后的 exe 路径
```

> **单文件发布注意事项**：`PublishSingleFile=true` 打包后的 exe 路径即为 `Application.ExecutablePath`，注册表写入值和用户双击启动的是同一个文件，开机自启行为与手动启动完全一致。

---

## 附录 A：关键技术决策表

| 决策 | 方案 | 替代方案 | 选择理由 |
|------|------|----------|----------|
| 托盘枚举 | `FindWindowEx` 遍历窗口树 + 跨进程内存读取 | UI Automation、`Shell_NotifyIcon` 回调 | 不依赖 COM/UIA，纯 Win32 最底层、最稳定 |
| 进程匹配 | 四级字符串匹配 | 仅精确匹配 | tooltip 与进程名无标准映射，四级覆盖主要场景 |
| 图标提取 | `Icon.ExtractAssociatedIcon` | `SHGetFileInfo` | .NET 封装更简洁，内部已处理缓存 |
| 进程终止 | `PostMessage(WM_CLOSE)` → 等待 → `TerminateProcess` | 直接 `Process.Kill()` | 温柔优先，给进程保存数据的机会 |
| 设置存储 | JSON（System.Text.Json） | XML、INI、注册表 | 人类可读、易于调试、跨平台兼容 |
| UI 框架 | WinForms | WPF、WinUI 3 | 轻量、快速开发、无额外依赖 |
| 项目格式 | SDK-style .csproj | 旧格式 .csproj | 简洁、支持 NuGet 包引用简化 |

## 附录 B：代码中的潜在改进点汇总

1. **TBBUTTON 内存布局**：`Pack=1` 与实际 native 对齐不一致，手动维护 `TbbuttonNativeSize` 和 `iStringOffset` 易出错。建议使用 `LayoutKind.Sequential`（无 Pack）+ `Marshal.OffsetOf` 动态获取偏移量。

2. **图标缓存内存管理**：`_iconCache` 中的 `Image` 对象未显式 Dispose，Form 关闭时应遍历释放。

3. **进程匹配性能**：L3 中 `ToHashSet()` 在循环内重复调用，可将 token 提取移至循环外。

4. **图标 Alpha 通道**：`Icon.ToBitmap()` 丢失 Alpha 通道，暗色 UI 下透明区域显示为黑色。

5. **去重线性查找**：`deduped.Find` 可用 `Dictionary<string, ProcessItem>` 优化。

6. **OpenProcess 权限降级**：可先尝试低权限，失败后降级而非直接请求 `PROCESS_ALL_ACCESS_SAFE`。

7. **枚举按钮数上限 256**：极端场景（某些虚拟桌面软件）可能超过 256，但目前未见实际案例。

---

> **文档版本**：v1.0 | **生成日期**：2026-06-19
*（内容由AI生成，仅供参考）*
