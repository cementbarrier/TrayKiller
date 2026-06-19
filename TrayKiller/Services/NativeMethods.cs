using System.Runtime.InteropServices;
using System.Text;

namespace TrayKiller.Services;

/// <summary>
/// Windows API P/Invoke 声明集中管理。
/// 仅使用公开标准 API，不使用任何底层钩子。
/// </summary>
internal static class NativeMethods
{
    #region 窗口查找与消息

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
        string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref TBBUTTON lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    #endregion

    #region 进程操作

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
        uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
        uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_VM_OPERATION = 0x0008;
    public const uint PROCESS_VM_WRITE = 0x0020;
    public const uint PROCESS_ALL_ACCESS_SAFE = PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION;
    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_RESERVE = 0x2000;
    public const uint MEM_RELEASE = 0x8000;
    public const uint PAGE_READWRITE = 0x04;

    #endregion

    #region Toolbar 消息与结构

    // -- Toolbar 控件消息 --
    public const uint TB_BUTTONCOUNT = 0x0418;
    public const uint TB_GETBUTTON = 0x0417;
    public const uint TB_GETBUTTONTEXTW = 0x044B;
    public const uint TB_GETIMAGELIST = 0x4000 + 2;   // TB_GETIMAGELIST
    public const uint TB_GETBITMAP = 0x042B;          // 获取按钮图标
    public const uint WM_USER = 0x0400;

    // -- ImageList 消息 --
    public const uint IL_GETIMAGERECT = 0x4000 + 6; // (未直接使用，备用)
    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_SMALLICON = 0x1;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TBBUTTON
    {
        public int iBitmap;       // offset 0
        public int idCommand;     // offset 4
        public byte fsState;      // offset 8
        public byte fsStyle;      // offset 9
        public ushort bReserved;  // offset 10
        public IntPtr dwData;     // offset 12 (x86) / 16 (x64, 4-byte pad auto)
        public IntPtr iString;    // offset 16 (x86) / 24 (x64, 4-byte pad auto)
    }

    // Real sizeof for TBBUTTON in explorer.exe (respects native alignment)
    public static int TbbuttonNativeSize => IntPtr.Size == 8 ? 32 : 20;

    #endregion

    #region 进程关闭消息

    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    #endregion

    #region DPI 与图标获取

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    [DllImport("shell32.dll")]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    #endregion

    #region 窗口置顶与样式

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    #endregion

    #region 开机自启注册表

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegOpenKeyEx(IntPtr hKey, string subKey, int ulOptions,
        int samDesired, out IntPtr hkResult);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegSetValueEx(IntPtr hKey, string lpValueName, int Reserved,
        int dwType, byte[] lpData, int cbData);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegCloseKey(IntPtr hKey);

    public static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
    public const int KEY_WRITE = 0x20006;
    public const int KEY_READ = 0x20019;
    public const int REG_SZ = 1;

    #endregion
}
