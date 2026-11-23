using System;
using System.Drawing;
using System.IO;
using IdeaPadEcoManager.Models;

namespace IdeaPadEcoManager.UI
{
    public static class IconHelper
    {
        private static Icon? _ecoIcon;
        private static Icon? _perfIcon;

        public static Icon CreateTrayIcon(AppPowerProfile profile)
        {
            try
            {
                if (profile == AppPowerProfile.CodingEco)
                {
                    if (_ecoIcon != null) return _ecoIcon;
                    string path = FindIconPath("eco.ico");
                    if (File.Exists(path))
                    {
                        return _ecoIcon = new Icon(path, 32, 32);
                    }
                }
                else
                {
                    if (_perfIcon != null) return _perfIcon;
                    string path = FindIconPath("perf.ico");
                    if (File.Exists(path))
                    {
                        return _perfIcon = new Icon(path, 32, 32);
                    }
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
