using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace ExMoreStuff
{
    // Where EX More Stuff keeps its own things: beside the program, so they all move with it: Mods\ (every package it
    // installs, as a zip), Music\ (the songs put in the game or saved), settings.json and gamebanana.json. When the
    // program's folder can't be written to (Program Files ...) they go in %APPDATA%\EX More Stuff instead. The game
    // folder only gets what the game loads, and the list of what's installed there.
    static class AppFolders
    {
        static string root;

        public static string Root
        {
            get
            {
                if (root != null) return root;
                string here = AppDomain.CurrentDomain.BaseDirectory;
                return root = Writable(here) ? here : AppData;
            }
            set { root = value; }   // tests
        }

        static string AppData { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Program.Title); } }

        public static string Mods { get { return Path.Combine(Root, "Mods"); } }
        public static string Music { get { return Path.Combine(Root, "Music"); } }

        /// <summary>A file of EX More Stuff's own beside the program; one an older version kept in %APPDATA% is copied over first.</summary>
        public static string DataFile(string name)
        {
            string here = Path.Combine(Root, name), old = Path.Combine(AppData, name);
            try { if (!File.Exists(here) && File.Exists(old) && !Same(here, old)) { Directory.CreateDirectory(Root); File.Copy(old, here); } }
            catch (Exception) { }
            return here;
        }

        static bool Writable(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, ".exmorestuff_write_test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        static bool Same(string a, string b) { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }

        public static bool Inside(string path, string folder)
        {
            string f = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A place in `folder` for a copy of `source` named `name`: that name, or the same file already there
        /// (same size and content), or "name (2)" ... when another file has it.</summary>
        public static string PlaceFor(string folder, string name, string source)
        {
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int n = 1; ; n++)
            {
                string path = Path.Combine(folder, n == 1 ? name : stem + " (" + n + ")" + ext);
                if (!File.Exists(path) || SameContent(path, source)) return path;
            }
        }

        static bool SameContent(string a, string b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using (var sha = SHA256.Create())
            {
                byte[] x, y;
                using (var s = File.OpenRead(a)) x = sha.ComputeHash(s);
                using (var s = File.OpenRead(b)) y = sha.ComputeHash(s);
                return x.SequenceEqual(y);
            }
        }

        /// <summary>A package the player adds: copied into Mods\ (unless it's there already), so their own file can move
        /// or go without EX More Stuff losing it. Returns the copy's path.</summary>
        public static string KeepPackage(string path)
        {
            if (Inside(path, Mods)) return path;
            Directory.CreateDirectory(Mods);
            string to = PlaceFor(Mods, Path.GetFileName(path), path);
            if (!File.Exists(to)) File.Copy(path, to);
            return to;
        }
    }
}
