using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace IdeaPadEcoManager.Services
{
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "IdeaPadEcoManager";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return false;

                if (enable)
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName
                        ?? Path.Combine(AppContext.BaseDirectory, "IdeaPadEcoManager.exe");
                    key.SetValue(AppName, $"\"{exePath}\"");
                    AppLogger.Info($"Enabled auto-start with Windows: {exePath}");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    AppLogger.Info("Disabled auto-start with Windows.");
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to update auto-start registry entry", ex);
                return false;
            }
        }
    }
}
