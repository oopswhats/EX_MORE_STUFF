using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ExMoreStuff
{
    // Where Ultra Street Fighter IV is installed, and where custom content goes: new files in its patch folder,
    // which the game reads before its own (patch_ae2_tu3\battle\chara\<CHR>\ and \battle\stage\).
    static class Game
    {
        const string SteamAppId = "45760";
        const string DefaultInstallDir = "Super Street Fighter IV - Arcade Edition";

        public static bool IsGameFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "SSFIV.exe"));
        }

        // Steam's own records: its install path in the registry, its library folders, the game's app manifest.
        public static string Find()
        {
            foreach (string library in SteamLibraries())
            {
                string installDir = DefaultInstallDir;
                string manifest = Path.Combine(library, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                if (File.Exists(manifest))
                {
                    Match m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success) installDir = m.Groups[1].Value;
                }
                string folder = Path.Combine(library, "steamapps", "common", installDir);
                if (IsGameFolder(folder)) return folder;
            }
            return null;
        }

        public static IEnumerable<string> SteamLibraries()
        {
            var libraries = new List<string>();
            string steam = null;
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using (RegistryKey key = root.OpenSubKey(@"Software\Valve\Steam") ?? root.OpenSubKey(@"Software\WOW6432Node\Valve\Steam"))
                {
                    if (key == null) continue;
                    steam = (key.GetValue("SteamPath") ?? key.GetValue("InstallPath")) as string;
                    if (steam != null) break;
                }
            }
            if (steam == null) return libraries;
            steam = steam.Replace('/', '\\');
            libraries.Add(steam);
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string path = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (!libraries.Contains(path)) libraries.Add(path);
                }
            return libraries;
        }

        public static bool IsRunning() { return Process.GetProcessesByName("SSFIV").Length > 0; }

        public static string PatchFolder(string game) { return Path.Combine(game, "patch_ae2_tu3"); }

        public static string FolderFor(string game, Item item)
        {
            return item.IsStage
                ? Path.Combine(PatchFolder(game), "battle", "stage")
                : Path.Combine(PatchFolder(game), "battle", "chara", item.Fighter);
        }
    }
}
