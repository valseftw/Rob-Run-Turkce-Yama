using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RobRunTurkce
{
    /// <summary>Steam kütüphanelerini (yerel dosyalardan) tarayarak ROB RUN kurulum klasörünü bulur. İnternete bağlanmaz.</summary>
    public static class GameLocator
    {
        public const string AppId = "4262460";
        public const string DataFolder = "ROB RUN_Data";
        public static string BundleDir(string game) => Path.Combine(game, DataFolder, "StreamingAssets", "aa", "StandaloneWindows64");

        public static bool IsGameDir(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "ROB RUN.exe")) && Directory.Exists(BundleDir(dir));

        public static string Find()
        {
            foreach (var lib in SteamLibraries())
            {
                var manifest = Path.Combine(lib, "steamapps", "appmanifest_" + AppId + ".acf");
                string dir = null;
                if (File.Exists(manifest))
                {
                    var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success) dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
                }
                if (IsGameDir(dir)) return dir;
                var guess = Path.Combine(lib, "steamapps", "common", "Rob Run");
                if (IsGameDir(guess)) return guess;
            }
            return null;
        }

        static IEnumerable<string> SteamLibraries()
        {
            var roots = new List<string>();
            foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(key))
                        if (k?.GetValue("InstallPath") is string p) roots.Add(p);
                }
                catch { }
            }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k?.GetValue("SteamPath") is string p) roots.Add(p.Replace('/', '\\'));
            }
            catch { }
            roots.Add(@"C:\Program Files (x86)\Steam");

            var libs = new List<string>();
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                libs.Add(root);
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            return libs.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>boot.config içindeki build-guid: oyun her güncellendiğinde değişir.</summary>
        public static string BuildGuid(string game)
        {
            var p = Path.Combine(game, DataFolder, "boot.config");
            if (!File.Exists(p)) return "bilinmiyor";
            var m = Regex.Match(File.ReadAllText(p), @"build-guid=(\w+)");
            return m.Success ? m.Groups[1].Value : "bilinmiyor";
        }
    }
}
