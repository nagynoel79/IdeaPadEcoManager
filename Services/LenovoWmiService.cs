using System;
using System.Management;
using System.Security.Principal;
using IdeaPadEcoManager.Models;

namespace IdeaPadEcoManager.Services
{
    public class LenovoWmiService
    {
        private const string WmiNamespace = @"\\.\root\WMI";
        private const string ClassName = "LENOVO_GAMEZONE_DATA";

        public static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public (bool Success, LenovoFanMode CurrentMode, string Message) GetSmartFanMode()
        {
            try
            {
                var scope = new ManagementScope(WmiNamespace);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {ClassName}"));
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        var outParams = obj.InvokeMethod("GetSmartFanMode", null, null);
                        if (outParams != null && outParams["Data"] != null)
                        {
                            uint modeVal = Convert.ToUInt32(outParams["Data"]);
                            var mode = (LenovoFanMode)modeVal;
                            return (true, mode, $"Current fan mode: {mode} ({modeVal})");
                        }
                    }
                }

                return (false, LenovoFanMode.Balanced, $"WMI class {ClassName} found, but GetSmartFanMode returned no data.");
            }
            catch (ManagementException mex)
            {
                string msg = mex.ErrorCode == ManagementStatus.AccessDenied
                    ? "WMI Access Denied: Administrator privileges are required to access Lenovo EC/Fan controls."
                    : $"Lenovo WMI Management error: {mex.Message} (Code: {mex.ErrorCode})";

                AppLogger.Warn(msg);
                return (false, LenovoFanMode.Balanced, msg);
            }
            catch (Exception ex)
            {
                string msg = $"Lenovo WMI query error: {ex.Message}";
                AppLogger.Warn(msg);
                return (false, LenovoFanMode.Balanced, msg);
            }
        }

        public (bool Success, LenovoFanMode AppliedMode, string Message) SetSmartFanMode(LenovoFanMode targetMode)
        {
            if (!IsAdministrator())
            {
                string adminWarning = "Cannot set Lenovo Fan Mode: App must be run as Administrator to control EC / SmartFan WMI interface.";
                AppLogger.Warn(adminWarning);
                return (false, targetMode, adminWarning);
            }

            try
            {
                var scope = new ManagementScope(WmiNamespace);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {ClassName}"));
                using var collection = searcher.Get();

                bool found = false;
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        found = true;
                        var inParams = obj.GetMethodParameters("SetSmartFanMode");
                        inParams["Data"] = (uint)targetMode;

                        AppLogger.Info($"Calling LENOVO_GAMEZONE_DATA.SetSmartFanMode({(uint)targetMode} - {targetMode})...");
                        var outParams = obj.InvokeMethod("SetSmartFanMode", inParams, null);

                        // Allow brief moment for EC to transition
                        System.Threading.Thread.Sleep(200);

                        // Verify switch
                        var verifyResult = GetSmartFanMode();
                        if (verifyResult.Success)
                        {
                            if (verifyResult.CurrentMode == targetMode)
                            {
                                string successMsg = $"Successfully switched Lenovo Fan Mode to {targetMode} ({targetMode}) and verified with EC.";
                                AppLogger.Info(successMsg);
                                return (true, verifyResult.CurrentMode, successMsg);
                            }
                            else
                            {
                                string mismatchMsg = $"Sent mode {targetMode}, but EC reports mode {verifyResult.CurrentMode}.";
                                AppLogger.Warn(mismatchMsg);
                                return (false, verifyResult.CurrentMode, mismatchMsg);
                            }
                        }
                        else
                        {
                            string unverifiedMsg = $"Invoked SetSmartFanMode({targetMode}), but could not verify via GetSmartFanMode: {verifyResult.Message}";
                            AppLogger.Info(unverifiedMsg);
                            return (true, targetMode, unverifiedMsg);
                        }
                    }
                }

                if (!found)
                {
                    string notFound = $"WMI class {ClassName} not found on this system.";
                    AppLogger.Warn(notFound);
                    return (false, targetMode, notFound);
                }

                return (false, targetMode, "Unknown WMI state");
            }
            catch (ManagementException mex)
            {
                string msg = mex.ErrorCode == ManagementStatus.AccessDenied
                    ? "WMI Access Denied when setting fan mode: Administrator privileges required."
                    : $"Lenovo WMI SetSmartFanMode error: {mex.Message} (Code: {mex.ErrorCode})";

                AppLogger.Error(msg);
                return (false, targetMode, msg);
            }
            catch (Exception ex)
            {
                string msg = $"Failed to set Lenovo Smart Fan Mode: {ex.Message}";
                AppLogger.Error(msg, ex);
                return (false, targetMode, msg);
            }
        }
    }
}
