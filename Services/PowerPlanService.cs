using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IdeaPadEcoManager.Services
{
    public class PowerPlanService
    {
        // Standard Windows GUIDs
        public const string SchemeBalanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string SchemeHighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
        public const string SettingProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
        public const string SettingPerfBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";

        public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
        public const string SettingAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";

        public const string SubIntelGraphics = "44f3beca-a7c0-460e-9df2-bb8b99e0cba6";
        public const string SettingIntelGpuPower = "3619c3f2-afb2-4afc-b0e9-e7fef372de36";

        public const string SubBattery = "e73a048d-bf27-4f12-9731-8b2076e8891f";
        public const string SettingBatLevelLow = "8183ba9a-e910-48da-8769-14ae6dc1170a";

        public const string EcoPlanName = "Coding Eco & Quiet";

        private string? _cachedEcoPlanGuid = null;
        private string? _cachedPerfPlanGuid = null;

        public string GetOrEnsureEcoPlan()
        {
            if (!string.IsNullOrEmpty(_cachedEcoPlanGuid))
            {
                return _cachedEcoPlanGuid;
            }

            var schemes = ListSchemes();
            foreach (var (guid, name, _) in schemes)
            {
                if (name.Contains(EcoPlanName, StringComparison.OrdinalIgnoreCase))
                {
                    _cachedEcoPlanGuid = guid;
                    AppLogger.Info($"Found existing '{EcoPlanName}' power scheme: {guid}");
                    ConfigureEcoSettings(guid);
                    return guid;
                }
            }

            // Create by duplicating Balanced
            AppLogger.Info($"Creating new custom power scheme '{EcoPlanName}'...");
            string duplicateOut = RunPowerCfg($"-duplicatescheme {SchemeBalanced}");
            var match = Regex.Match(duplicateOut, @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                string newGuid = match.Value;
                RunPowerCfg($"-changename {newGuid} \"{EcoPlanName}\" \"Lenovo IdeaPad Gaming 3 Coding & Battery Optimization\"");
                _cachedEcoPlanGuid = newGuid;
                AppLogger.Info($"Successfully created '{EcoPlanName}' scheme: {newGuid}");
                ConfigureEcoSettings(newGuid);
                return newGuid;
            }

            AppLogger.Warn("Could not duplicate scheme, falling back to Balanced");
            return SchemeBalanced;
        }

        public string GetOrEnsurePerformancePlan()
        {
            if (!string.IsNullOrEmpty(_cachedPerfPlanGuid))
            {
                return _cachedPerfPlanGuid;
            }

            var schemes = ListSchemes();
            foreach (var (guid, name, _) in schemes)
            {
                if (guid.Equals(SchemeHighPerformance, StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Teljesítmény", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Performance", StringComparison.OrdinalIgnoreCase))
                {
                    _cachedPerfPlanGuid = guid;
                    ConfigurePerformanceSettings(guid);
                    return guid;
                }
            }

            _cachedPerfPlanGuid = SchemeHighPerformance;
            ConfigurePerformanceSettings(SchemeHighPerformance);
            return SchemeHighPerformance;
        }

        private void ConfigureEcoSettings(string guid)
        {
            try
            {
                // DC (Battery) settings:
                // 1. Max CPU throttle state: 80%
                RunPowerCfg($"/setdcvalueindex {guid} {SubProcessor} {SettingProcThrottleMax} 80");
                // 2. CPU Boost mode: 0 (Disabled)
                RunPowerCfg($"/setdcvalueindex {guid} {SubProcessor} {SettingPerfBoostMode} 0");
                // 3. PCIe ASPM: 2 (Maximum Power Savings)
                RunPowerCfg($"/setdcvalueindex {guid} {SubPciExpress} {SettingAspm} 2");
                // 4. Intel Graphics: 0 (Maximum Battery Life)
                RunPowerCfg($"/setdcvalueindex {guid} {SubIntelGraphics} {SettingIntelGpuPower} 0");
                // 5. Battery Saver trigger (100% on battery)
                RunPowerCfg($"/setdcvalueindex {guid} {SubBattery} {SettingBatLevelLow} 100");

                AppLogger.Info($"Configured '{EcoPlanName}' settings (CPU max 80%, Boost off, PCIe Max Savings, Intel GPU Max Battery).");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error configuring Eco power settings", ex);
            }
        }

        private void ConfigurePerformanceSettings(string guid)
        {
            try
            {
                // AC settings:
                // 1. Max CPU throttle state: 100%
                RunPowerCfg($"/setacvalueindex {guid} {SubProcessor} {SettingProcThrottleMax} 100");
                // 2. CPU Boost mode: 2 (Aggressive)
                RunPowerCfg($"/setacvalueindex {guid} {SubProcessor} {SettingPerfBoostMode} 2");
                // 3. PCIe ASPM: 0 (Off)
                RunPowerCfg($"/setacvalueindex {guid} {SubPciExpress} {SettingAspm} 0");
                // 4. Intel Graphics: 2 (Maximum Performance)
                RunPowerCfg($"/setacvalueindex {guid} {SubIntelGraphics} {SettingIntelGpuPower} 2");
                // 5. Battery Saver normal (20%)
                RunPowerCfg($"/setacvalueindex {guid} {SubBattery} {SettingBatLevelLow} 20");

                AppLogger.Info($"Configured Full Performance settings (CPU max 100%, Boost Aggressive, PCIe Max Perf, Intel GPU Max Perf).");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error configuring Performance power settings", ex);
            }
        }

        public bool ApplyBatteryEcoPlan()
        {
            try
            {
                string ecoGuid = GetOrEnsureEcoPlan();
                RunPowerCfg($"/setactive {ecoGuid}");
                AppLogger.Info($"Activated Battery Eco power plan ({ecoGuid})");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to activate Battery Eco power plan", ex);
                return false;
            }
        }

        public bool ApplyAcPerformancePlan()
        {
            try
            {
                string perfGuid = GetOrEnsurePerformancePlan();
                RunPowerCfg($"/setactive {perfGuid}");
                AppLogger.Info($"Activated AC Full Performance power plan ({perfGuid})");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to activate Performance power plan", ex);
                return false;
            }
        }

        public (string Guid, string Name) GetActiveScheme()
        {
            try
            {
                string output = RunPowerCfg("/getactivescheme");
                var match = Regex.Match(output, @"([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})\s+\((.*?)\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return (match.Groups[1].Value, match.Groups[2].Value);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to get active power scheme", ex);
            }
            return (string.Empty, "Unknown");
        }

        public System.Collections.Generic.List<(string Guid, string Name, bool IsActive)> ListSchemes()
        {
            var list = new System.Collections.Generic.List<(string, string, bool)>();
            try
            {
                string output = RunPowerCfg("/l");
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"Power Scheme GUID:\s+([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})\s+\((.*?)\)(\s*\*?)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string guid = match.Groups[1].Value;
                        string name = match.Groups[2].Value;
                        bool isActive = match.Groups[3].Value.Contains("*");
                        list.Add((guid, name, isActive));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to list power schemes", ex);
            }
            return list;
        }

        private string RunPowerCfg(string arguments)
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (!string.IsNullOrWhiteSpace(err) && proc.ExitCode != 0)
            {
                AppLogger.Warn($"powercfg {arguments} returned code {proc.ExitCode}: {err.Trim()}");
            }

            return output;
        }
    }
}
