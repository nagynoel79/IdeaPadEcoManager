using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using IdeaPadEcoManager.Services;
using IdeaPadEcoManager.UI;

namespace IdeaPadEcoManager
{
    static class Program
    {
        private const string MutexName = @"Global\LenovoIdeaPadEcoManager_SingleInstance_Mutex";
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        static void Main(string[] args)
        {
            AppLogger.Info(">>> Program.Main entered.");

            // 1. Single-instance check
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
                if (!isNewInstance)
                {
                    AppLogger.Warn("Another instance is already running. Exiting.");
                    MessageBox.Show(
                        "A Lenovo IdeaPad Eco Manager már fut a háttérben (az óra mellett találod a tálcán).",
                        "Alkalmazás már fut",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Mutex check exception: {ex.Message}");
            }

            // 2. Exception handling
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLogger.Error("Application thread exception caught", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    AppLogger.Error("AppDomain unhandled exception caught", ex);
                }
            };

            // 3. High DPI and WinForms setup
            ApplicationConfiguration.Initialize();

            AppLogger.Info("Starting Lenovo IdeaPad Gaming 3 Eco Manager Profile Engine...");

            try
            {
                using var profileManager = new ProfileManager();
                profileManager.Initialize();

                bool startMinimized = args != null && args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

                AppLogger.Info($"Creating DashboardForm (startMinimized: {startMinimized})...");
                using var form = new DashboardForm(profileManager, startMinimized);

                AppLogger.Info("Entering Application.Run(form)...");
                Application.Run(form);
                AppLogger.Info("Application.Run(form) returned cleanly.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Fatal startup error in Main", ex);
                MessageBox.Show($"Váratlan hiba az alkalmazás indításakor:\n{ex.Message}", "Hiba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    _singleInstanceMutex?.ReleaseMutex();
                }
                catch { }
                _singleInstanceMutex?.Dispose();
            }
        }
    }
}
