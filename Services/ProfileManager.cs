using System;
using System.Threading;
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
        public PerformanceAppService PerformanceApps { get; }

        public OperationMode CurrentOperationMode { get; private set; } = OperationMode.Auto;
        public AppPowerProfile CurrentActiveProfile { get; private set; }

        public string CurrentDemandingAppName { get; private set; } = "";
        public string CurrentDemandingReason { get; private set; } = "";

        public event Action<AppPowerProfile, string>? ProfileApplied;

        private readonly System.Threading.Timer _acEvaluationTimer;
        private int _nonDemandingTicks = 0;
        private const int DebounceTicksRequired = 2; // ~10 seconds of no demanding apps before falling back to AC Cool

        public ProfileManager()
        {
            PowerStatus = new PowerStatusService();
            LenovoWmi = new LenovoWmiService();
            PowerPlan = new PowerPlanService();
            Brightness = new BrightnessService();
            GpuRegistry = new GpuRegistryService();
            RtxMonitor = new Rtx3050MonitorService(checkIntervalSeconds: 45);
            PerformanceApps = new PerformanceAppService(GpuRegistry);

            PowerStatus.PowerStatusChanged += OnPowerStatusChanged;

            // Timer runs every 5 seconds to evaluate demanding workload on AC
            _acEvaluationTimer = new System.Threading.Timer(OnAcEvaluationTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Initialize()
        {
            AppLogger.Info("Initializing Lenovo IdeaPad Eco Manager Profile Engine...");

            // 1. Ensure all browsers, media tools, and IDEs are pinned to integrated GPU
            try
            {
                int pinnedCount = GpuRegistry.PinAllNonGamingToIntegratedGpu();
                AppLogger.Info($"GPU Registry Check: {pinnedCount} application(s) (Browsers, Media, IDEs) verified for Intel iGPU.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"GPU Registry pinning warning: {ex.Message}");
            }

            // 2. Query initial power state and apply profile immediately
            var (isAc, percent, _, _) = PowerStatus.GetCurrentStatus();
            AppLogger.Info($"Startup power status: {(isAc ? "AC Connected" : "Battery")} ({percent}%)");

            if (isAc)
            {
                EvaluateAndApplyAcProfile("Application Startup (AC detected)");
                _acEvaluationTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            }
            else
            {
                ApplyBatteryEcoProfile("Application Startup (Battery detected)");
                _acEvaluationTimer.Change(Timeout.Infinite, Timeout.Infinite);
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
                AppLogger.Info($"AC charger plugged in (Battery: {batteryPercent}%). Starting workload monitoring.");
                EvaluateAndApplyAcProfile($"AC charger plugged in (Battery: {batteryPercent}%)");
                _acEvaluationTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            }
            else
            {
                AppLogger.Info($"AC charger disconnected - Running on Battery ({batteryPercent}%). Pausing AC workload timer.");
                _acEvaluationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                ApplyBatteryEcoProfile($"AC charger disconnected - Running on Battery ({batteryPercent}%)");
            }
        }

        private void OnAcEvaluationTimerTick(object? state)
        {
            if (CurrentOperationMode != OperationMode.Auto) return;

            var (isAc, _, _, _) = PowerStatus.GetCurrentStatus();
            if (!isAc) return;

            try
            {
                bool isDemanding = PerformanceApps.IsDemandingAppRunning(RtxMonitor, out string appName, out string reason);

                if (isDemanding)
                {
                    _nonDemandingTicks = 0;
                    CurrentDemandingAppName = appName;
                    CurrentDemandingReason = reason;

                    if (CurrentActiveProfile != AppPowerProfile.FullPerformance)
                    {
                        ApplyPerformanceProfile($"Nagy terhelésű alkalmazás indult: {appName} ({reason})");
                    }
                }
                else
                {
                    CurrentDemandingAppName = "";
                    CurrentDemandingReason = "";

                    if (CurrentActiveProfile == AppPowerProfile.FullPerformance)
                    {
                        _nonDemandingTicks++;
                        if (_nonDemandingTicks >= DebounceTicksRequired)
                        {
                            ApplyAcCoolProfile("Nagy terhelésű alkalmazások bezárva - Visszatérés Hűvös & Csendes AC profilba");
                            _nonDemandingTicks = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"AC workload evaluation error: {ex.Message}");
            }
        }

        public void EvaluateAndApplyAcProfile(string triggerReason)
        {
            bool isDemanding = PerformanceApps.IsDemandingAppRunning(RtxMonitor, out string appName, out string reason);
            if (isDemanding)
            {
                CurrentDemandingAppName = appName;
                CurrentDemandingReason = reason;
                ApplyPerformanceProfile($"{triggerReason} -> Teljesítményigényes app észlelve: {appName} ({reason})");
            }
            else
            {
                CurrentDemandingAppName = "";
                CurrentDemandingReason = "";
                ApplyAcCoolProfile($"{triggerReason} -> Nincs játék/3D terhelés (Csendes & Hűvös AC mód)");
            }
        }

        public void SetOperationMode(OperationMode mode)
        {
            CurrentOperationMode = mode;
            AppLogger.Info($"Operation mode changed to: {mode}");

            switch (mode)
            {
                case OperationMode.ManualEco:
                    _acEvaluationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    ApplyBatteryEcoProfile("Kézi felülbírálás -> Coding Eco & Quiet");
                    break;
                case OperationMode.ManualAcCool:
                    _acEvaluationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    ApplyAcCoolProfile("Kézi felülbírálás -> AC Cool & Quiet (Hűvös & Csendes)");
                    break;
                case OperationMode.ManualPerformance:
                    _acEvaluationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    ApplyPerformanceProfile("Kézi felülbírálás -> Full Performance (Maximális teljesítmény)");
                    break;
                case OperationMode.Auto:
                    var (isAc, percent, _, _) = PowerStatus.GetCurrentStatus();
                    if (isAc)
                    {
                        EvaluateAndApplyAcProfile($"Auto mód visszakapcsolva (Hálózat, {percent}%)");
                        _acEvaluationTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                    }
                    else
                    {
                        _acEvaluationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        ApplyBatteryEcoProfile($"Auto mód visszakapcsolva (Akkumulátor, {percent}%)");
                    }
                    break;
            }
        }

        public void ApplyBatteryEcoProfile(string reason)
        {
            CurrentActiveProfile = AppPowerProfile.CodingEco;
            AppLogger.Info($"--- [PROFIL AKTIVÁLVA: CODING ECO & QUIET] Ok: {reason} ---");

            // 1. Lenovo EC Cooling: Quiet Mode (1)
            var fanResult = LenovoWmi.SetSmartFanMode(LenovoFanMode.Quiet);
            AppLogger.Info($"Lenovo WMI Fan Mode (Quiet): {fanResult.Message}");

            // 2. Windows Power Plan: Coding Eco & Quiet (CPU 80%, Boost Off, PCIe Max Savings, Intel GPU Max Battery, Saver on)
            PowerPlan.ApplyBatteryEcoPlan();

            // 3. Brightness: Save AC brightness and set to 45%
            Brightness.ApplyBatteryEcoBrightness(45);

            // 4. Ensure non-gaming apps are pinned to iGPU
            GpuRegistry.PinAllNonGamingToIntegratedGpu();

            // 5. Start RTX 3050 oversight timer for battery drain alerts
            RtxMonitor.SetBatteryMode(true);

            ProfileApplied?.Invoke(AppPowerProfile.CodingEco, reason);
        }

        public void ApplyAcCoolProfile(string reason)
        {
            CurrentActiveProfile = AppPowerProfile.AcCool;
            AppLogger.Info($"--- [PROFIL AKTIVÁLVA: AC COOL & QUIET (NETEZÉS & FILM)] Ok: {reason} ---");

            // 1. Lenovo EC Cooling: Quiet Mode (1) (hűvös és hangtalan hétköznapi használathoz)
            var fanResult = LenovoWmi.SetSmartFanMode(LenovoFanMode.Quiet);
            AppLogger.Info($"Lenovo WMI Fan Mode (Quiet): {fanResult.Message}");

            // 2. Windows Power Plan: AC Cool & Quiet (CPU 100%, Boost OFF, PCIe Max Savings, Intel GPU Balanced)
            PowerPlan.ApplyAcCoolPlan();

            // 3. Brightness: Restore saved AC brightness
            Brightness.RestoreAcBrightness();

            // 4. Ensure non-gaming apps are pinned to iGPU
            GpuRegistry.PinAllNonGamingToIntegratedGpu();

            // 5. Stop battery alerts
            RtxMonitor.SetBatteryMode(false);

            ProfileApplied?.Invoke(AppPowerProfile.AcCool, reason);
        }

        public void ApplyPerformanceProfile(string reason)
        {
            CurrentActiveProfile = AppPowerProfile.FullPerformance;
            AppLogger.Info($"--- [PROFIL AKTIVÁLVA: FULL PERFORMANCE (JÁTÉK & 3D)] Ok: {reason} ---");

            // 1. Lenovo EC Cooling: Performance Mode (3)
            var fanResult = LenovoWmi.SetSmartFanMode(LenovoFanMode.Performance);
            AppLogger.Info($"Lenovo WMI Fan Mode (Performance): {fanResult.Message}");

            // 2. Windows Power Plan: High Performance (CPU 100%, Boost Aggressive, PCIe Max, Intel GPU Max)
            PowerPlan.ApplyAcPerformancePlan();

            // 3. Brightness: Restore saved AC brightness
            Brightness.RestoreAcBrightness();

            // 4. Stop battery alerts
            RtxMonitor.SetBatteryMode(false);

            ProfileApplied?.Invoke(AppPowerProfile.FullPerformance, reason);
        }

        public void Dispose()
        {
            _acEvaluationTimer?.Dispose();
            PowerStatus.PowerStatusChanged -= OnPowerStatusChanged;
            PowerStatus.Dispose();
            RtxMonitor.Dispose();
        }
    }
}
