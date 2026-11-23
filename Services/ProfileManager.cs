using System;
using System.Collections.Generic;
using IdeaPadEcoManager.Models;

namespace IdeaPadEcoManager.Services
{
    public class ProfileManager : IDisposable
    {
        public PowerStatusService PowerStatus { get; }
        public LenovoWmiService LenovoWmi { get; }
        public PowerPlanService PowerPlan { get; }
        public BrightnessService Brightness { get; }
        public GpuRegistryService GpuRegistry { get; }
        public Rtx3050MonitorService RtxMonitor { get; }

        public OperationMode CurrentOperationMode { get; private set; } = OperationMode.Auto;
        public AppPowerProfile CurrentActiveProfile { get; private set; }

        public event Action<AppPowerProfile, string>? ProfileApplied;

        public ProfileManager()
        {
            PowerStatus = new PowerStatusService();
            LenovoWmi = new LenovoWmiService();
            PowerPlan = new PowerPlanService();
            Brightness = new BrightnessService();
            GpuRegistry = new GpuRegistryService();
            RtxMonitor = new Rtx3050MonitorService(checkIntervalSeconds: 45);

            PowerStatus.PowerStatusChanged += OnPowerStatusChanged;
        }

        public void Initialize()
        {
            AppLogger.Info("Initializing Lenovo IdeaPad Eco Manager Profile Engine...");

            // 1. Ensure IDEs are pinned to integrated GPU
            try
            {
                int pinnedCount = GpuRegistry.PinAllIdesToIntegratedGpu();
                AppLogger.Info($"IDE Registry Check: {pinnedCount} IDE executable(s) verified for Intel iGPU.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"IDE Registry pinning warning: {ex.Message}");
            }

            // 2. Query initial power state and apply profile immediately
            var (isAc, percent, _, _) = PowerStatus.GetCurrentStatus();
            AppLogger.Info($"Startup power status: {(isAc ? "AC Connected" : "Battery")} ({percent}%)");

            if (isAc)
            {
                ApplyPerformanceProfile("Application Startup (AC detected)");
            }
            else
            {
                ApplyBatteryEcoProfile("Application Startup (Battery detected)");
            }
        }

        private void OnPowerStatusChanged(bool isAc, int batteryPercent)
        {
            if (CurrentOperationMode != OperationMode.Auto)
            {
                AppLogger.Info($"Power status changed, but manual mode '{CurrentOperationMode}' is active. Skipping auto switch.");
                return;
            }

            if (isAc)
            {
                ApplyPerformanceProfile($"AC charger plugged in (Battery: {batteryPercent}%)");
            }
            else
            {
                ApplyBatteryEcoProfile($"AC charger disconnected - Running on Battery ({batteryPercent}%)");
            }
        }

        public void SetOperationMode(OperationMode mode)
        {
            CurrentOperationMode = mode;
            AppLogger.Info($"Operation mode changed to: {mode}");

            switch (mode)
            {
                case OperationMode.ManualEco:
                    ApplyBatteryEcoProfile("Manual Override -> Coding Eco & Quiet");
                    break;
                case OperationMode.ManualPerformance:
                    ApplyPerformanceProfile("Manual Override -> Full Performance");
                    break;
                case OperationMode.Auto:
                    var (isAc, percent, _, _) = PowerStatus.GetCurrentStatus();
                    if (isAc)
                    {
                        ApplyPerformanceProfile($"Auto mode resumed (AC detected, {percent}%)");
                    }
                    else
                    {
                        ApplyBatteryEcoProfile($"Auto mode resumed (Battery detected, {percent}%)");
                    }
                    break;
            }
        }

        public void ApplyBatteryEcoProfile(string reason)
        {
            CurrentActiveProfile = AppPowerProfile.CodingEco;
            AppLogger.Info($"--- [APPLYING CODING ECO & QUIET PROFILE] Reason: {reason} ---");

            // 1. Lenovo EC Cooling: Quiet Mode (1)
            var fanResult = LenovoWmi.SetSmartFanMode(LenovoFanMode.Quiet);
            AppLogger.Info($"Lenovo WMI Fan Mode (Quiet): {fanResult.Message}");

            // 2. Windows Power Plan: Coding Eco & Quiet (CPU 80%, Boost Off, PCIe Max Savings, Intel GPU Max Battery, Saver on)
            PowerPlan.ApplyBatteryEcoPlan();

            // 3. Brightness: Save AC brightness and set to 45%
            Brightness.ApplyBatteryEcoBrightness(45);

            // 4. Ensure IDEs are pinned to iGPU
            GpuRegistry.PinAllIdesToIntegratedGpu();

            // 5. Start RTX 3050 oversight timer
            RtxMonitor.SetBatteryMode(true);

            ProfileApplied?.Invoke(AppPowerProfile.CodingEco, reason);
        }

        public void ApplyPerformanceProfile(string reason)
        {
            CurrentActiveProfile = AppPowerProfile.FullPerformance;
            AppLogger.Info($"--- [APPLYING FULL PERFORMANCE PROFILE] Reason: {reason} ---");

            // 1. Lenovo EC Cooling: Performance Mode (3)
            var fanResult = LenovoWmi.SetSmartFanMode(LenovoFanMode.Performance);
            AppLogger.Info($"Lenovo WMI Fan Mode (Performance): {fanResult.Message}");

            // 2. Windows Power Plan: High Performance (CPU 100%, Boost Aggressive, PCIe Max, Intel GPU Max)
            PowerPlan.ApplyAcPerformancePlan();

            // 3. Brightness: Restore saved AC brightness
            Brightness.RestoreAcBrightness();

            // 4. Stop RTX 3050 oversight alerts
            RtxMonitor.SetBatteryMode(false);

            ProfileApplied?.Invoke(AppPowerProfile.FullPerformance, reason);
        }

        public void Dispose()
        {
            PowerStatus.PowerStatusChanged -= OnPowerStatusChanged;
            PowerStatus.Dispose();
            RtxMonitor.Dispose();
        }
    }
}
