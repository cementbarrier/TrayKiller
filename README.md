---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: a72a634ca6e5090ac9aedc6460e39d68_f521caed6bd411f1a0095254002afed2
    ReservedCode1: cm5j5ukCShO4r37f/6ExUiHuRYkiMO0fpPZm3hEo17pytCDjC7t60ABL5qrrXUiNImoNObV7PAh4ASq1t7NBostnftWsKPTwD8+ri3QQ3DZ1q/aBnVVP25kRx+SghfGFfXTNHNtqStpoLPTQnZpyjIA3xJc6Tx6usK3gdPGDC3zDGFfXS0vdQHxfLkA=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: a72a634ca6e5090ac9aedc6460e39d68_f521caed6bd411f1a0095254002afed2
    ReservedCode2: cm5j5ukCShO4r37f/6ExUiHuRYkiMO0fpPZm3hEo17pytCDjC7t60ABL5qrrXUiNImoNObV7PAh4ASq1t7NBostnftWsKPTwD8+ri3QQ3DZ1q/aBnVVP25kRx+SghfGFfXTNHNtqStpoLPTQnZpyjIA3xJc6Tx6usK3gdPGDC3zDGFfXS0vdQHxfLkA=
---

# TrayKiller

A lightweight Windows system tray process manager. Enumerate, inspect, and terminate tray icon processes with drag-and-drop simplicity.

## Features

- **Tray Icon Enumeration** — scans the system tray and notification overflow area via Win32 API, listing all visible tray icons with their tooltip text
- **Process Matching** — multi-level heuristic matching engine resolves tray tooltips to actual running processes (pid + executable name)
- **Icon Extraction** — extracts and displays the associated icon from each process's executable file
- **Drag-to-Kill** — drag any tray process entry onto the trash zone to terminate it; supports graceful shutdown (WM_CLOSE) with automatic fallback to force kill
- **Whitelist Protection** — built-in system whitelist protects critical processes (explorer.exe, input methods, security center, etc.), plus user-customizable blacklist/whitelist
- **Side Panel** — sleek auto-hide side panel (dock left or right) that slides out on mouse hover
- **Auto Refresh** — configurable auto-refresh interval keeps the process list up to date
- **System Tray Integration** — minimizes to system tray, with right-click menu for quick access to panel, settings, and exit
- **Single Instance** — prevents multiple instances from running simultaneously
- **Settings Persistence** — all preferences saved as JSON in `%AppData%\TrayKiller\settings.json`

## System Requirements

- **Windows 10** or later (x64 / x86)
- **.NET 9.0 Runtime** ([download](https://dotnet.microsoft.com/en-us/download/dotnet/9.0))

## Quick Start

```bash
# Clone the repository
git clone <repo-url>
cd TrayKiller\TrayKiller

# Build
dotnet build

# Run
dotnet run

# Publish as a self-contained single-file executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ../publish
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **UI Framework** | Windows Forms (.NET 9.0) |
| **System Integration** | Win32 API (P/Invoke) — `FindWindow` / `FindWindowEx` / `SendMessage` / `OpenProcess` / `ReadProcessMemory` / `TerminateProcess` |
| **Data** | JSON persistence via `System.Text.Json` |
| **Target Framework** | `net9.0-windows` |

## How It Works

1. **Enumeration**: Uses `FindWindow("NotifyIconOverflowWindow")` and `FindWindow("Shell_TrayWnd")` to locate the toolbar windows that host tray icons in explorer.exe
2. **Tooltip Extraction**: Sends `TB_GETBUTTON` messages and reads `TBBUTTON.iString` via cross-process memory (`ReadProcessMemory`) to obtain the tooltip text for each tray icon
3. **Process Resolution**: Matches tooltip text against running processes using a four-tier heuristic: substring match → compact match (whitespace/punctuation stripped) → token match → FileDescription match
4. **Termination**: `WM_CLOSE` (graceful, 2s timeout) → `TerminateProcess` (force kill), with configurable fallback behavior

## Project Structure

```
TrayKiller/
├── TrayKiller.csproj          # .NET 9.0 WinForms project
├── Program.cs                 # Entry point, single-instance guard
├── AppContext.cs              # Application lifecycle, tray icon, context menu
├── Models/
│   └── ProcessItem.cs         # Data model: process name, pid, tooltip, icon
├── Services/
│   ├── NativeMethods.cs       # Win32 P/Invoke declarations
│   ├── TrayEnumerator.cs      # Tray icon enumeration & process matching
│   ├── ProcessTerminator.cs   # Graceful + force kill logic
│   ├── WhitelistManager.cs    # System + custom whitelist/blacklist
│   └── SettingsManager.cs     # JSON settings persistence
├── UI/
│   ├── MainForm.cs            # Side panel, process list, drag-to-kill
│   └── SettingsForm.cs        # Settings dialog
├── .gitignore
├── LICENSE
└── README.md
```

## License

This project is licensed under the MIT License. See [LICENSE](./LICENSE) for details.
*（内容由AI生成，仅供参考）*
