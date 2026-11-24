# Lenovo IdeaPad Eco & Performance Manager

A lightweight, event-driven Windows system tray utility in C# (.NET 8 Windows Forms) designed specifically for the Lenovo IdeaPad Gaming 3 15IHU6 (Intel Core i5-11320H, NVIDIA GeForce RTX 3050 4GB Laptop GPU, 60Hz display).

The application automatically manages power schemes, Lenovo embedded controller (EC) cooling profiles, screen brightness, and discrete GPU utilization depending on AC connection and real-time application workload demands.

---

## 1. System Requirements and Compatibility

- Hardware: Lenovo IdeaPad Gaming 3 15IHU6 (Intel 11th Gen + RTX 3050)
- Operating System: Windows 10 or Windows 11 (64-bit)
- Runtime: Microsoft .NET 8.0 Desktop Runtime (x64)
- Privileges: Administrator rights are required for Embedded Controller (WMI) fan mode changes. Other features (power plans, brightness, registry preferences, GPU monitoring) work with standard user privileges.

---

## 2. Core Functional Specifications

### A. Battery Mode (Coding Eco and Quiet)
Activated immediately upon disconnecting the AC charger:
- Lenovo Embedded Controller Cooling: Sets Smart Fan Mode to Quiet (Mode 1) via `root\WMI` (`LENOVO_GAMEZONE_DATA.SetSmartFanMode`), turning the power button LED blue.
- Windows Power Plan: Activates the custom "Coding Eco & Quiet" power scheme:
  - Maximum Processor State (`PROCTHROTTLEMAX`): 80%
  - Intel Turbo Boost (`PERFBOOSTMODE`): Disabled (0) to eliminate thermal spikes and reduce power draw
  - PCIe Link State Power Management (`ASPM`): Maximum Power Savings (2)
  - Intel Graphics Power Plan: Maximum Battery Life (0)
  - Windows Battery Saver: Automatically engaged
- Screen Brightness: Saves the current AC brightness level and lowers brightness to 45% via `WmiMonitorBrightnessMethods`.
- Application GPU Affinity: Enforces `GpuPreference=1;` (Power Saving / Integrated GPU) in the Windows Registry (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`) for IDEs, web browsers, and media players.
- Discrete GPU Watchdog: Inspects NVIDIA RTX 3050 compute and graphics processes every 45 seconds using `nvidia-smi`. If an application triggers the discrete GPU while running on battery, a system notification alerts the user with the process name and PID.

### B. AC Normal Mode (AC Cool and Quiet)
Active on AC power when no heavy 3D rendering software or games are running (e.g. web browsing, movie streaming, coding):
- Lenovo Embedded Controller Cooling: Sets Smart Fan Mode to Quiet (Mode 1), keeping fan noise near-silent.
- Windows Power Plan: Activates the custom "IdeaPad AC Cool & Quiet" power scheme:
  - Maximum Processor State: 100% (full base performance)
  - Intel Turbo Boost: Disabled (0) to prevent excessive voltage, heating, and loud fan ramp-ups during everyday tasks and video decoding
  - PCIe Link State Power Management: Maximum Power Savings (2)
  - Intel Graphics Power Plan: Balanced (1)
- GPU Routing: Web browsers (Chrome, Brave, Edge, Firefox, Opera, Edge WebView2) and media players (VLC, MPC-HC, Spotify, Windows Media Player) are pinned to the Intel Iris Xe GPU, preventing the RTX 3050 from waking up and keeping package power low.
- Screen Brightness: Restores the user's preferred AC brightness level.

### C. AC On-Demand Performance Mode (Full Performance)
Active on AC power only when a demanding application is actively running:
- Automatic Trigger Detection:
  - Windows Graphics Registry games configured with `GpuPreference=2;` (such as EA Sports FC, Counter-Strike 2).
  - Built-in detection for popular 3D games and creative tools (e.g., Blender, Unreal Engine, Premiere Pro).
  - Installed Steam library games.
  - User-defined executables added via the Dashboard interface or `performance_apps.json`.
  - Any process actively using the RTX 3050 discrete GPU that is not on the whitelist of background utilities and browsers.
- System Actions:
  - Lenovo Embedded Controller Cooling: Sets Smart Fan Mode to Performance (Mode 3), maximizing airflow and turning the power button LED red.
  - Windows Power Plan: Activates the High Performance scheme with Intel Turbo Boost set to Aggressive (Mode 2) for maximum clock frequency.
  - PCIe Link State Power Management: Off (0) for maximum bandwidth.
  - Intel Graphics Power Plan: Maximum Performance (2).
- Automatic Fallback: When the demanding application exits, a debounce timer verifies the workload has subsided and smoothly returns the laptop to the AC Cool and Quiet profile.

---

## 3. Project Structure

```
IdeaPadEcoManager/
|-- Models/
|   |-- Enums.cs                 # Enums: LenovoFanMode, AppPowerProfile, GpuProcessInfo, DemandingAppInfo
|-- Services/
|   |-- AppLogger.cs             # Thread-safe in-memory and persistent file logging
|   |-- BrightnessService.cs     # WMI screen brightness read/write operations
|   |-- GpuRegistryService.cs    # DirectX UserGpuPreferences scanner and configuration
|   |-- LenovoWmiService.cs      # LENOVO_GAMEZONE_DATA interface for fan mode control
|   |-- PerformanceAppService.cs # On-demand game and 3D application workload detector
|   |-- PowerPlanService.cs      # Windows powercfg scheme duplication and parameter tuning
|   |-- PowerStatusService.cs    # Win32 GetSystemPowerStatus and SystemEvents monitoring
|   |-- ProfileManager.cs        # Main coordinator and state machine
|   |-- Rtx3050MonitorService.cs # Discrete GPU process watchdog via nvidia-smi
|   |-- StartupService.cs        # Registry and shortcut auto-start helper
|-- UI/
|   |-- DashboardForm.cs         # Dark-themed management and monitoring interface
|   |-- IconHelper.cs            # Dynamic and file-based system tray icon loader
|-- Resources/
|   |-- eco.ico                  # Green Eco tray icon (32x32)
|   |-- ac_cool.ico              # Blue AC Cool & Quiet tray icon (32x32)
|   |-- perf.ico                 # Red Performance tray icon (32x32)
|-- app.manifest                 # Per-monitor DPI awareness and execution level
|-- IdeaPadEcoManager.csproj     # .NET 8 WinForms project file
|-- Install-ElevatedTask.bat     # Installs scheduled task to run elevated without UAC
|-- Uninstall-ElevatedTask.bat   # Removes the scheduled task
|-- Program.cs                   # Application entry point with single-instance mutex
```

---

## 4. Installation and Setup

### Option A: Automated Startup via Windows Task Scheduler (Recommended)
Running through Windows Task Scheduler with highest privileges enables Lenovo WMI fan mode switching without triggering a UAC prompt on every login:

1. Right-click `Install-ElevatedTask.bat`.
2. Select "Run as administrator".
3. The script registers a task named `IdeaPadEcoManager` set to run at user logon with elevated privileges and the `--minimized` argument.
4. To remove it later, right-click `Uninstall-ElevatedTask.bat` and run as administrator.

### Option B: Standard Startup (Registry / Startup Folder)
1. Double-click `IdeaPadEcoManager.exe`.
2. Check the "Run at Windows startup" checkbox in the Dashboard Settings tab.
3. This creates a startup entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Note: In standard user mode, power plan switching, brightness control, browser/IDE pinning, and dGPU monitoring function normally. To allow fan profile changes via WMI, click "Restart as Administrator" from the tray menu or dashboard.

---

## 5. Usage and Controls

### System Tray
- Icon Appearance:
  - Green circle with "E": Battery mode (Coding Eco and Quiet).
  - Blue circle with "C": AC Cool and Quiet mode (browsing, films, light work).
  - Red circle with "P": Full Performance mode (demanding game or 3D app running).
- Single / Double Click: Opens the live Dashboard window.
- Right-Click Context Menu:
  - Operation Mode: Switch between Automatic (follows charger and workload), Manual Eco, Manual AC Cool, or Manual Performance.
  - Lenovo Cooling Mode: Force Quiet (1), Balanced (2), or Performance (3).
  - RTX 3050 Status: View count of active processes on the discrete GPU.
  - Open Dashboard: Displays the full graphical interface.
  - Exit: Terminates the background process.

### Dashboard Window
- Overview Tab: Displays battery percentage, current power source, charging state, active profile, real-time workload status, fan mode, brightness slider, and active Windows power plan.
- GPU and Applications Tab:
  - Lists applications pinned to the integrated Intel GPU (browsers, media players, IDEs) with one-click configuration.
  - Lists performance trigger applications (detected registry games, Steam games, and custom additions) with add/remove controls.
  - Displays a real-time table of processes using the RTX 3050 (PID, process name, memory).
- Logs and Settings Tab: Real-time scrolling event log with options to clear logs or open the log directory (`%APPDATA%\IdeaPadEcoManager`).

---

## 6. Logging and Diagnostics

Log files are stored at:
`%APPDATA%\IdeaPadEcoManager\manager.log`

Log entries include power mode transitions, power scheme GUIDs, WMI invocation results, and discrete GPU alerts.
