using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using IdeaPadEcoManager.Models;

namespace IdeaPadEcoManager.Services
{
    public class Rtx3050MonitorService : IDisposable
    {
        public event Action<List<GpuProcessInfo>>? OnGpuWakeupAlert;

        private readonly System.Threading.Timer _timer;
        private bool _isBatteryMode = false;
        private HashSet<string> _lastNotifiedPids = new();
        private DateTime _lastAlertTime = DateTime.MinValue;

        public Rtx3050MonitorService(int checkIntervalSeconds = 45)
        {
            _timer = new System.Threading.Timer(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void SetBatteryMode(bool isBattery)
        {
            _isBatteryMode = isBattery;
            if (_isBatteryMode)
            {
                AppLogger.Info("RTX 3050 battery oversight started (interval: 45s).");
                // Check once after 5s, then every 45s
                _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(45));
            }
            else
            {
                AppLogger.Info("RTX 3050 battery oversight paused (AC connected).");
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _lastNotifiedPids.Clear();
            }
        }

        private void TimerCallback(object? state)
        {
            if (!_isBatteryMode) return;

            try
            {
                var procs = GetRunningProcesses();
                if (procs.Count > 0)
                {
                    var currentPids = procs.Select(p => p.Pid.ToString()).ToHashSet();
                    bool hasNewProcs = !currentPids.IsSubsetOf(_lastNotifiedPids);
                    bool timeoutExpired = (DateTime.Now - _lastAlertTime).TotalMinutes >= 5;

                    if (hasNewProcs || timeoutExpired)
                    {
                        _lastNotifiedPids = currentPids;
                        _lastAlertTime = DateTime.Now;

                        string names = string.Join(", ", procs.Take(3).Select(p => p.Name));
                        if (procs.Count > 3) names += $" és további {procs.Count - 3} db";

                        AppLogger.Warn($"RTX 3050 active on battery! Processes: {names}");
                        OnGpuWakeupAlert?.Invoke(procs);
                    }
                }
                else
                {
                    _lastNotifiedPids.Clear();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"RTX 3050 timer callback error: {ex.Message}");
            }
        }

        public List<GpuProcessInfo> GetRunningProcesses()
        {
            var list = new List<GpuProcessInfo>();
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-compute-apps=pid,process_name,used_memory --format=csv,noheader,nounits",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2 && uint.TryParse(parts[0].Trim(), out uint pid))
                    {
                        string fullPath = parts[1].Trim();
                        string name = Path.GetFileName(fullPath);
                        ulong memBytes = 0;
                        if (parts.Length >= 3 && ulong.TryParse(parts[2].Trim(), out ulong memMb))
                        {
                            memBytes = memMb * 1024 * 1024;
                        }

                        // Try to get process name from PID if full path is incomplete
                        if (string.IsNullOrEmpty(name) || name.Equals("[N/A]", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using var p = Process.GetProcessById((int)pid);
                                name = p.ProcessName;
                            }
                            catch { }
                        }

                        list.Add(new GpuProcessInfo(pid, name, memBytes, fullPath));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"nvidia-smi query error: {ex.Message}");
            }
            return list;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
