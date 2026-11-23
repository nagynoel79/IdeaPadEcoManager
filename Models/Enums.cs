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
        CodingEco,
        FullPerformance
    }

    public enum OperationMode
    {
        Auto,
        ManualEco,
        ManualPerformance
    }

    public record GpuProcessInfo(uint Pid, string Name, ulong MemoryBytes, string FullPath = "");

    public record IdeGpuStatus(string Name, string Path, bool IsConfigured, string CurrentPreference);
}
