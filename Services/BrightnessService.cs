using System;
using System.Management;

namespace IdeaPadEcoManager.Services
{
    public class BrightnessService
    {
        private const string WmiNamespace = @"\\.\root\wmi";
        private int _savedAcBrightness = 70;

        public int GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                using var results = searcher.Get();

                foreach (ManagementObject obj in results)
                {
                    using (obj)
                    {
                        if (obj["CurrentBrightness"] != null)
                        {
                            return Convert.ToInt32(obj["CurrentBrightness"]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to query brightness via WMI: {ex.Message}");
            }

            return -1;
        }

        public bool SetBrightness(int brightnessPercent)
        {
            int target = Math.Clamp(brightnessPercent, 0, 100);
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, "SELECT * FROM WmiMonitorBrightnessMethods");
                using var results = searcher.Get();

                bool applied = false;
                foreach (ManagementObject obj in results)
                {
                    using (obj)
                    {
                        var inParams = obj.GetMethodParameters("WmiSetBrightness");
                        inParams["Timeout"] = (uint)1;
                        inParams["Brightness"] = (byte)target;

                        obj.InvokeMethod("WmiSetBrightness", inParams, null);
                        applied = true;
                    }
                }

                if (applied)
                {
                    AppLogger.Info($"Brightness set to {target}%");
                    return true;
                }
                else
                {
                    AppLogger.Warn("WmiMonitorBrightnessMethods instance not found to set brightness.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to set brightness to {target}%", ex);
                return false;
            }
        }

        public void ApplyBatteryEcoBrightness(int targetEcoBrightness = 45)
        {
            int current = GetBrightness();
            if (current > 0)
            {
                // Only save AC brightness if it looks reasonable (e.g. higher than eco target)
                if (current > targetEcoBrightness)
                {
                    _savedAcBrightness = current;
                    AppLogger.Info($"Saved AC brightness: {_savedAcBrightness}%");
                }
            }

            SetBrightness(targetEcoBrightness);
        }

        public void RestoreAcBrightness()
        {
            AppLogger.Info($"Restoring AC brightness to {_savedAcBrightness}%");
            SetBrightness(_savedAcBrightness);
        }

        public int SavedAcBrightness => _savedAcBrightness;
    }
}
