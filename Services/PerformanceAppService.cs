using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using IdeaPadEcoManager.Models;

namespace IdeaPadEcoManager.Services
{
    public class PerformanceAppService
    {
        private readonly string _configFilePath;
        private readonly HashSet<string> _customApps = new(StringComparer.OrdinalIgnoreCase);
        private readonly GpuRegistryService _gpuRegistry;

        // Built-in list of demanding games and 3D / creative applications
        private static readonly HashSet<string> BuiltInDemandingProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Games
            "cs2", "csgo", "fc26", "fc24", "fifa23", "fifa22", "fifa21",
            "gta5", "gtav", "valorant", "leagueclientux", "league of legends",
            "fortniteclient-win64-shipping", "overwatch", "cyberpunk2077",
            "witcher3", "r5apex", "deadbydaylight", "rocketleague", "dota2",
            "pubg", "tslgame", "rainbowsix", "forzahorizon5", "forzahorizon4",
            "callofduty", "cod", "warzone", "modernwarfare", "destiny2",
            "eldenring", "sekiro", "darktide", "helldivers2", "starfield",
            "baldursgate3", "bg3", "genshinimpact", "honkaistarrail", "zenlesszonezero",
            "diablo4", "worldofwarcraft", "wow", "minecraft", "javaw",

            // 3D / Creative Software
            "blender", "unrealeditor", "unity", "resolve", "premiere",
            "afterfx", "3dsmax", "maya", "cinema4d", "autocad", "revit"
        };

        // Processes that should NEVER trigger Performance Mode even if attached to dGPU or running
        private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Web Browsers (User strictly wants these on iGPU / Cool mode)
            "chrome", "brave", "msedge", "firefox", "opera", "vivaldi", "msedgewebview2",

            // Windows Shell & Desktop Components
            "dwm", "explorer", "searchhost", "startmenuexperiencehost", "widgetboard",
            "shellexperiencehost", "taskmgr", "systemsettings", "phoneexperiencehost",
            "crossdeviceresume", "lockapp", "applicationframehost", "textinputhost",
            "ctfmon", "smartscreen", "sihost", "fontdrvhost",

            // Background Utilities & Launchers
            "logioptionsplus_agent", "intelgraphicssoftware", "fdm", "steam",
            "epicgameslauncher", "antigravity", "ideapadecomanager", "nvcontainer",
            "nvidia share", "conhost", "cmd", "powershell", "pwsh", "rundll32",
            "devenv", "code", "idea64", "rider64", "pycharm64", "studio64", "vlc", "spotify"
        };

        public PerformanceAppService(GpuRegistryService gpuRegistry)
        {
            _gpuRegistry = gpuRegistry;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "IdeaPadEcoManager");
            Directory.CreateDirectory(dir);
            _configFilePath = Path.Combine(dir, "performance_apps.json");

            LoadCustomApps();
        }

        private void LoadCustomApps()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            if (!string.IsNullOrWhiteSpace(item))
                            {
                                _customApps.Add(item.Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load custom performance apps: {ex.Message}");
            }
        }

        public void SaveCustomApps()
        {
            try
            {
                string json = JsonSerializer.Serialize(_customApps.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save custom performance apps", ex);
            }
        }

        public void AddCustomApp(string appNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(appNameOrPath)) return;
            string clean = Path.GetFileNameWithoutExtension(appNameOrPath).Trim();
            if (_customApps.Add(clean))
            {
                SaveCustomApps();
                AppLogger.Info($"Added custom performance app trigger: {clean}");
            }
        }

        public void RemoveCustomApp(string appNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(appNameOrPath)) return;
            string clean = Path.GetFileNameWithoutExtension(appNameOrPath).Trim();
            if (_customApps.Remove(clean))
            {
                SaveCustomApps();
                AppLogger.Info($"Removed custom performance app trigger: {clean}");
            }
        }

        public List<DemandingAppInfo> GetAllTriggerApps()
        {
            var list = new List<DemandingAppInfo>();

            // 1. Windows Graphics Registry (e.g. FC 26, CS2, FC 24)
            foreach (var regApp in _gpuRegistry.GetConfiguredPerformanceAppsFromRegistry())
            {
                list.Add(regApp);
            }

            // 2. Custom user apps
            foreach (var custom in _customApps)
            {
                list.Add(new DemandingAppInfo(custom + ".exe", custom, "Egyéni Beállítás (Felhasználó által hozzáadva)"));
            }

            // 3. Steam library games
            var steamGames = ScanSteamGames();
            foreach (var game in steamGames)
            {
                if (!list.Any(x => x.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(game);
                }
            }

            return list;
        }

        private List<DemandingAppInfo> ScanSteamGames()
        {
            var list = new List<DemandingAppInfo>();
            try
            {
                var steamCommonPaths = new[]
                {
                    @"C:\Program Files (x86)\Steam\steamapps\common",
                    @"C:\Program Files\Steam\steamapps\common",
                    @"D:\SteamLibrary\steamapps\common",
                    @"E:\SteamLibrary\steamapps\common"
                };

                foreach (var common in steamCommonPaths)
                {
                    if (!Directory.Exists(common)) continue;

                    foreach (var gameDir in Directory.GetDirectories(common))
                    {
                        try
                        {
                            foreach (var exe in Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                            {
                                string exeName = Path.GetFileName(exe);
                                string exeLower = exeName.ToLowerInvariant();
                                if (exeLower.Contains("installer") || exeLower.Contains("crash") || exeLower.Contains("setup") || exeLower.Contains("redist") || exeLower.Contains("unitycrash"))
                                    continue;

                                list.Add(new DemandingAppInfo(exeName, exe, "Steam Játékkönyvtár"));
                                if (list.Count > 50) break; // Keep scan fast
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        public bool IsDemandingAppRunning(Rtx3050MonitorService rtxMonitor, out string activeAppName, out string detectionReason)
        {
            activeAppName = "";
            detectionReason = "";

            // 1. Fast process check: check active OS processes
            var activeProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        activeProcessNames.Add(p.ProcessName);
                    }
                    catch { }
                }
            }
            catch { }

            // Check against built-in demanding list
            foreach (var builtIn in BuiltInDemandingProcessNames)
            {
                if (activeProcessNames.Contains(builtIn))
                {
                    activeAppName = builtIn + ".exe";
                    detectionReason = "Ismert játék vagy 3D alkalmazás fut";
                    return true;
                }
            }

            // Check against user custom apps
            foreach (var custom in _customApps)
            {
                if (activeProcessNames.Contains(custom))
                {
                    activeAppName = custom + ".exe";
                    detectionReason = "Egyénileg felvett teljesítmény-alkalmazás fut";
                    return true;
                }
            }

            // Check against configured registry games (e.g. FC26, CS2)
            var regApps = _gpuRegistry.GetConfiguredPerformanceAppsFromRegistry();
            foreach (var reg in regApps)
            {
                string procName = Path.GetFileNameWithoutExtension(reg.Name);
                if (activeProcessNames.Contains(procName))
                {
                    activeAppName = reg.Name;
                    detectionReason = "Windows Nagy Teljesítményű (GpuPreference=2) játék aktív";
                    return true;
                }
            }

            // 2. Hardware GPU query check: check if a non-ignored process is actively using RTX 3050 dGPU
            try
            {
                var gpuProcs = rtxMonitor.GetRunningProcesses();
                foreach (var gp in gpuProcs)
                {
                    string pName = Path.GetFileNameWithoutExtension(gp.Name);
                    if (string.IsNullOrWhiteSpace(pName)) continue;

                    if (!IgnoredProcessNames.Contains(pName))
                    {
                        activeAppName = gp.Name;
                        detectionReason = $"Aktív NVIDIA RTX 3050 dGPU terhelés ({gp.Name})";
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"GPU demanding check error: {ex.Message}");
            }

            return false;
        }
    }
}
