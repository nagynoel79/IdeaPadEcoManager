using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace IdeaPadEcoManager.Services
{
    public class PowerStatusService : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus; // 0: Offline, 1: Online, 255: Unknown
            public byte BatteryFlag;  // 1: High, 2: Low, 4: Critical, 8: Charging, 128: No battery
            public byte BatteryLifePercent; // 0..100, 255: Unknown
            public byte SystemStatusFlag; // 1: Battery saver on
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        public event Action<bool, int>? PowerStatusChanged; // (isAcConnected, batteryPercent)

        private bool? _lastAcStatus = null;
        private int _lastPercent = -1;
        private bool _disposed = false;

        public PowerStatusService()
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            AppLogger.Info($"SystemEvents.PowerModeChanged: {e.Mode}");
            CheckPowerStatus(triggerEventIfChanged: true);
        }

        public (bool IsAc, int BatteryPercent, bool IsCharging, bool BatterySaverActive) GetCurrentStatus()
        {
            if (GetSystemPowerStatus(out var status))
            {
                bool isAc = status.ACLineStatus == 1;
                int percent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 0;
                bool isCharging = (status.BatteryFlag & 8) != 0;
                bool isSaver = status.SystemStatusFlag == 1;

                return (isAc, percent, isCharging, isSaver);
            }

            AppLogger.Warn("Failed to read GetSystemPowerStatus, defaulting to AC");
            return (true, 100, false, false);
        }

        public void CheckPowerStatus(bool triggerEventIfChanged = true)
        {
            var (isAc, percent, _, _) = GetCurrentStatus();

            bool statusChanged = _lastAcStatus == null || _lastAcStatus.Value != isAc;
            _lastAcStatus = isAc;
            _lastPercent = percent;

            if (statusChanged || !triggerEventIfChanged)
            {
                AppLogger.Info($"Power status updated: {(isAc ? "AC (Plugged in)" : "Battery")} | Battery: {percent}%");
                PowerStatusChanged?.Invoke(isAc, percent);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
        }
    }
}
