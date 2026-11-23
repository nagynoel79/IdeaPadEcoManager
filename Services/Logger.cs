using System;
using System.Collections.Concurrent;
using System.IO;

namespace IdeaPadEcoManager.Services
{
    public static class AppLogger
    {
        public static event Action<string>? OnLog;

        private static readonly string LogDir;
        private static readonly string LogFilePath;
        private static readonly object FileLock = new();
        private static readonly ConcurrentQueue<string> InMemoryLogs = new();
        private const int MaxMemoryLogs = 200;

        static AppLogger()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                LogDir = Path.Combine(appData, "IdeaPadEcoManager");
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }
                LogFilePath = Path.Combine(LogDir, "manager.log");
            }
            catch
            {
                LogDir = Path.GetTempPath();
                LogFilePath = Path.Combine(LogDir, "ideapad_eco_manager.log");
            }
        }

        public static void Info(string message) => Log("INFO", message);
        public static void Warn(string message) => Log("WARN", message);
        public static void Error(string message, Exception? ex = null)
        {
            var msg = ex != null ? $"{message} | Ex: {ex.Message}" : message;
            Log("ERROR", msg);
        }

        private static void Log(string level, string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

            InMemoryLogs.Enqueue(line);
            while (InMemoryLogs.Count > MaxMemoryLogs)
            {
                InMemoryLogs.TryDequeue(out _);
            }

            try
            {
                lock (FileLock)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Silently swallow file write issues
            }

            try
            {
                OnLog?.Invoke(line);
            }
            catch
            {
                // Ignore subscriber issues
            }
        }

        public static string[] GetRecentLogs() => InMemoryLogs.ToArray();
    }
}
