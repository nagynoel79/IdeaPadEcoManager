namespace IdeaPadEcoManager.Models
{
    public enum LenovoFanMode : uint
    {
        Quiet = 1,
        Balanced = 2,
        Performance = 3
    }

    public enum AppPowerProfile
    {
        CodingEco,         // Battery mode: Lenovo Quiet, CPU 80%, Boost Off, Brightness 45%
        AcCool,            // AC mode (idle / browsing / streaming): Lenovo Quiet/Balanced, CPU 100%, Boost Off, AC Brightness
        FullPerformance    // AC mode (game / 3D workload active): Lenovo Performance, CPU 100%, Boost Aggressive, Max CPU/GPU
    }

    public enum OperationMode
    {
        Auto,
        ManualEco,
        ManualAcCool,
        ManualPerformance
    }

    public record GpuProcessInfo(uint Pid, string Name, ulong MemoryBytes, string FullPath = "");

    public record IdeGpuStatus(string Name, string Path, bool IsConfigured, string CurrentPreference, string Category = "IDE");

    public record DemandingAppInfo(string Name, string Path, string Source);
}
