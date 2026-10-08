using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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

    // The few things remembered between runs (only the game folder), in %APPDATA%\EX More Stuff.
    static class Settings
    {
        static string SettingsFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Program.Title, "settings.json"); } }

        public static string GameFolder
        {
            get
            {
                try
                {
                    var j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsFile));
                    return j != null && j.ContainsKey("gameFolder") ? Convert.ToString(j["gameFolder"]) : null;
                }
                catch (Exception) { return null; }
            }
            set
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                    File.WriteAllText(SettingsFile, new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "gameFolder", value } }));
                }
                catch (Exception) { }
            }
        }
    }
}
