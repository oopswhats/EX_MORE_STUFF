using System.Drawing;
using System.IO;
using System.Reflection;

namespace ExMoreStuff
{
    // The game's own stages (EMBER's StageCatalog list) with EMBER's stage pictures, built into the program, and
    // the rule EMBER uses for custom stages: players without custom stage n see Fallbacks[(n - 1) % 18].
    static class Stages
    {
        public static readonly string[] Codes =
        {
            "TRN", "CHN", "USA", "RUS", "BRA", "AFR", "VIE", "EUR", "RVR", "VCN", "SCO", "JPN", "LAB", "IND",
            "KOR", "BLD", "CNX", "BRX", "VNX", "JPX", "AFX", "LBX", "DET", "ELV", "HFP", "MAD", "BFU", "JUR",
        };

        public static readonly string[] Names =
        {
            "Training Stage", "Crowded Downtown", "Drive-in at Night", "Snowy Rail Yard", "Inland Jungle", "Small Airfield",
            "Beautiful Bay", "Cruise Ship Stern", "Overpass", "Volcanic Rim", "Historic Distillery", "Old Temple",
            "Secret Laboratory", "Exciting Street Scene", "Festival at the Old Temple", "Skyscraper Under Construction",
            "Run-down Back Alley", "Pitch-black Jungle", "Morning Mist Bay", "Deserted Temple", "Solar Eclipse",
            "Crumbling Laboratory", "Pit Stop 109", "Cosmic Elevator", "Half Pipe", "Mad Gear Hideout", "Blast Furnace",
            "Jurassic Era Research Facility",
        };

        static readonly string[] Fallbacks =
            { "CHN", "RUS", "BRA", "AFR", "VIE", "EUR", "RVR", "SCO", "JPN", "IND", "KOR", "CNX", "BRX", "VNX", "JPX", "AFX", "ELV", "HFP" };

        public static string FallbackCode(int number) { return Fallbacks[(number - 1) % Fallbacks.Length]; }

        public static string Name(string code)
        {
            int i = System.Array.IndexOf(Codes, code);
            return i >= 0 ? Names[i] : code;
        }

        public static Image Picture(string code)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("stages." + code + ".jpg");
            return stream == null ? null : Image.FromStream(stream);
        }
    }
}
