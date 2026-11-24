using System;
using System.Drawing;
using System.IO;
using IdeaPadEcoManager.Models;

namespace IdeaPadEcoManager.UI
{
    public static class IconHelper
    {
        private static Icon? _ecoIcon;
        private static Icon? _acCoolIcon;
        private static Icon? _perfIcon;

        public static Icon CreateTrayIcon(AppPowerProfile profile)
        {
            try
            {
                switch (profile)
                {
                    case AppPowerProfile.CodingEco:
                        if (_ecoIcon != null) return _ecoIcon;
                        string ecoPath = FindIconPath("eco.ico");
                        if (File.Exists(ecoPath))
                        {
                            return _ecoIcon = new Icon(ecoPath, 32, 32);
                        }
                        break;

                    case AppPowerProfile.AcCool:
                        if (_acCoolIcon != null) return _acCoolIcon;
                        string coolPath = FindIconPath("ac_cool.ico");
                        if (File.Exists(coolPath))
                        {
                            return _acCoolIcon = new Icon(coolPath, 32, 32);
                        }
                        break;

                    case AppPowerProfile.FullPerformance:
                        if (_perfIcon != null) return _perfIcon;
                        string perfPath = FindIconPath("perf.ico");
                        if (File.Exists(perfPath))
                        {
                            return _perfIcon = new Icon(perfPath, 32, 32);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Services.AppLogger.Error("Failed to load icon from file", ex);
            }

            return SystemIcons.Application;
        }

        private static string FindIconPath(string filename)
        {
            string p1 = Path.Combine(AppContext.BaseDirectory, filename);
            if (File.Exists(p1)) return p1;

            string p2 = Path.Combine(AppContext.BaseDirectory, "Resources", filename);
            if (File.Exists(p2)) return p2;

            return p1;
        }
    }
}
