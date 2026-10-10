using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class Program
    {
        public const string Title = "EX More Stuff";
        // The catalog everyone uses. A catalog.txt beside the program (one line: an address or a file path) points
        // it elsewhere, for testing or a catalog kept on disk.
        public const string DefaultCatalog = "https://raw.githubusercontent.com/oopswhats/EX_MORE_STUFF/main/catalog.json";

        [STAThread]
        static void Main()
        {
            // One window at a time: starting it again brings the open one to the front.
            bool first;
            using (var single = new Mutex(true, @"Local\EXMoreStuff", out first))
            {
                if (!first) { ShowOpenWindow(); return; }
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);

        static void ShowOpenWindow()
        {
            int self = Process.GetCurrentProcess().Id;
            Process open = Process.GetProcesses().FirstOrDefault(p => p.Id != self && p.MainWindowTitle == Title &&
                                                                      !p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase));
            if (open == null) return;
            if (IsIconic(open.MainWindowHandle)) ShowWindow(open.MainWindowHandle, 9);   // SW_RESTORE
            SetForegroundWindow(open.MainWindowHandle);
        }

        public static string CatalogSource()
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.txt");
            if (File.Exists(local))
                foreach (string line in File.ReadAllLines(local))
                    if (line.Trim().Length > 0) return line.Trim();
            return DefaultCatalog;
        }
    }

    // The few things remembered between runs (the game folder; SFxT's folder; packages kept in the game without their zip), in
    // settings.json beside the program (AppFolders).
    static class Settings
    {
        static string SettingsFile { get { return AppFolders.DataFile("settings.json"); } }

        static Dictionary<string, object> Read()
        {
            try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsFile)) ?? new Dictionary<string, object>(); }
            catch (Exception) { return new Dictionary<string, object>(); }
        }

        static void Write(string key, object value)
        {
            var j = Read();
            j[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, new JavaScriptSerializer().Serialize(j));
            }
            catch (Exception) { }
        }

        public static string GameFolder
        {
            get { object v; return Read().TryGetValue("gameFolder", out v) && v != null ? Convert.ToString(v) : null; }
            set { Write("gameFolder", value); }
        }

        /// <summary>Street Fighter X Tekken's folder, once chosen for its music (Music page), or null.</summary>
        public static string SfxtFolder
        {
            get { object v; return Read().TryGetValue("sfxtFolder", out v) && v != null ? Convert.ToString(v) : null; }
            set { Write("sfxtFolder", value); }
        }

        /// <summary>Packages (Item.Key) the player chose to keep in the game after deleting their zip: not asked about again.</summary>
        public static List<string> KeptWithoutZip
        {
            get
            {
                object v;
                var list = Read().TryGetValue("keptWithoutZip", out v) ? v as System.Collections.ArrayList : null;
                return list == null ? new List<string>() : list.Cast<object>().Select(Convert.ToString).ToList();
            }
            set { Write("keptWithoutZip", value); }
        }
    }
}
