using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

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

        // Every code the game has, the bonus rounds GAS and SCX included: none of them can be a custom stage's.
        static readonly string[] GameCodes = Codes.Concat(new[] { "GAS", "SCX" }).ToArray();

        // A custom stage's code: three capital letters or digits that aren't one of the game's (C12, D05, XYZ ...).
        // EMBER sends the code itself as the stage's number, so any catalog can use its own letters.
        public static bool IsCustomCode(string code)
        {
            return code != null && Regex.IsMatch(code, "^[A-Z0-9]{3}$") && Array.IndexOf(GameCodes, code) < 0;
        }

        // What players without a custom stage see (EMBER's StageCatalog): a letter and a number (C12, D05) by its
        // number, 1 CHN, 2 RUS ... 18 HFP, 19 CHN ...; any other code by the sum of its characters.
        public static string FallbackCode(string code)
        {
            int number = char.IsDigit(code[1]) && char.IsDigit(code[2]) ? (code[1] - '0') * 10 + (code[2] - '0') : 0;
            bool numbered = code[0] >= 'A' && code[0] <= 'Z' && number > 0;
            return Fallbacks[(numbered ? number - 1 : code[0] + code[1] + code[2]) % Fallbacks.Length];
        }

        public static string Name(string code)
        {
            int i = System.Array.IndexOf(Codes, code);
            return i >= 0 ? Names[i] : code;
        }

        // decoded once: the stage pages are built again on every reload
        static readonly System.Collections.Generic.Dictionary<string, Image> pictures = new System.Collections.Generic.Dictionary<string, Image>();

        public static Image Picture(string code)
        {
            Image image;
            lock (pictures)
            {
                if (pictures.TryGetValue(code, out image)) return image;
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("stages." + code + ".jpg");
                image = stream == null ? null : Image.FromStream(stream);
                pictures[code] = image;
                return image;
            }
        }
    }
}
