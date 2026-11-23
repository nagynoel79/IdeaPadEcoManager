using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using IdeaPadEcoManager.Models;

namespace IdeaPadEcoManager.Services
{
    public class GpuRegistryService
    {
        private const string RegistrySubKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string PowerSavingPref = "GpuPreference=1;";

        public List<string> DetectInstalledIdePaths()
        {
            var idePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Visual Studio via vswhere
            try
            {
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var vswhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
                if (File.Exists(vswhere))
                {
                    using var proc = new Process();
                    proc.StartInfo = new ProcessStartInfo
                    {
                        FileName = vswhere,
                        Arguments = "-all -property productPath",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    proc.Start();
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (File.Exists(line.Trim()))
                        {
                            idePaths.Add(line.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"vswhere error: {ex.Message}");
            }

            // 2. Visual Studio fallback scan
            ScanWildcards(idePaths, @"C:\Program Files\Microsoft Visual Studio", "*devenv.exe", 4);
            ScanWildcards(idePaths, @"C:\Program Files (x86)\Microsoft Visual Studio", "*devenv.exe", 4);

            // 3. VS Code
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            var vsCodeLocal = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
            if (File.Exists(vsCodeLocal)) idePaths.Add(vsCodeLocal);

            var vsCodeInsidersLocal = Path.Combine(localAppData, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe");
            if (File.Exists(vsCodeInsidersLocal)) idePaths.Add(vsCodeInsidersLocal);

            var vsCodePf = Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");
            if (File.Exists(vsCodePf)) idePaths.Add(vsCodePf);

            // 4. JetBrains IDEs (IntelliJ, Rider, PyCharm, WebStorm, Android Studio)
            ScanWildcards(idePaths, Path.Combine(programFiles, "JetBrains"), "*.exe", 3, p =>
                p.EndsWith("idea64.exe", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("rider64.exe", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("pycharm64.exe", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("webstorm64.exe", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("clion64.exe", StringComparison.OrdinalIgnoreCase));

            ScanWildcards(idePaths, Path.Combine(localAppData, "JetBrains", "Toolbox", "apps"), "*.exe", 5, p =>
                p.EndsWith("idea64.exe", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("rider64.exe", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("pycharm64.exe", StringComparison.OrdinalIgnoreCase));

            var androidStudio = Path.Combine(programFiles, "Android", "Android Studio", "bin", "studio64.exe");
            if (File.Exists(androidStudio)) idePaths.Add(androidStudio);

            // 5. Currently running IDE processes
            try
            {
                var targetNames = new[] { "devenv", "Code", "idea64", "rider64", "pycharm64", "studio64" };
                foreach (var name in targetNames)
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            string? path = proc.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                idePaths.Add(path);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return new List<string>(idePaths);
        }

        private static void ScanWildcards(HashSet<string> results, string rootDir, string pattern, int maxDepth, Func<string, bool>? filter = null)
        {
            if (!Directory.Exists(rootDir)) return;

            try
            {
                ScanRecursive(results, rootDir, pattern, 0, maxDepth, filter);
            }
            catch { }
        }

        private static void ScanRecursive(HashSet<string> results, string currentDir, string pattern, int currentDepth, int maxDepth, Func<string, bool>? filter)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                foreach (var file in Directory.GetFiles(currentDir, pattern))
                {
                    if (filter == null || filter(file))
                    {
                        results.Add(file);
                    }
                }

                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    ScanRecursive(results, subDir, pattern, currentDepth + 1, maxDepth, filter);
                }
            }
            catch { }
        }

        public List<IdeGpuStatus> CheckIdePreferences()
        {
            var detected = DetectInstalledIdePaths();
            var list = new List<IdeGpuStatus>();

            using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, writable: false);

            foreach (var path in detected)
            {
                string name = Path.GetFileName(path);
                string currentPref = "";
                bool isConfigured = false;

                if (key != null)
                {
                    var val = key.GetValue(path) as string;
                    if (!string.IsNullOrEmpty(val))
                    {
                        currentPref = val;
                        isConfigured = val.Contains("GpuPreference=1;");
                    }
                }

                list.Add(new IdeGpuStatus(name, path, isConfigured, currentPref));
            }

            return list;
        }

        public int PinAllIdesToIntegratedGpu()
        {
            int configuredCount = 0;
            var detected = DetectInstalledIdePaths();

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true);
                if (key == null)
                {
                    AppLogger.Error("Failed to open or create UserGpuPreferences registry key.");
                    return 0;
                }

                foreach (var path in detected)
                {
                    try
                    {
                        string current = (key.GetValue(path) as string) ?? "";
                        string updated;

                        if (string.IsNullOrEmpty(current))
                        {
                            updated = PowerSavingPref;
                        }
                        else if (Regex.IsMatch(current, @"GpuPreference=\d+;"))
                        {
                            updated = Regex.Replace(current, @"GpuPreference=\d+;", PowerSavingPref);
                        }
                        else
                        {
                            updated = current.TrimEnd(';') + ";" + PowerSavingPref;
                        }

                        if (current != updated)
                        {
                            key.SetValue(path, updated, RegistryValueKind.String);
                            AppLogger.Info($"Pinned to Intel iGPU: {Path.GetFileName(path)} => '{updated}'");
                        }
                        configuredCount++;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"Failed to set registry preference for {path}", ex);
                    }
                }

                AppLogger.Info($"Pinned {configuredCount} IDE(s) to Intel iGPU in UserGpuPreferences.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error pinning IDEs to integrated GPU", ex);
            }

            return configuredCount;
        }
    }
}
