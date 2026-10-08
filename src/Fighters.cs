using System;
using System.Linq;

namespace ExMoreStuff
{
    // USFIV's fighters: the game's three-letter codes (they name the costume files, RYU_12.obj.emo) and names.
    // Same list and order as EMBER's FighterMetadata.inc (native fighter IDs 0..43).
    static class Fighters
    {
        public static readonly string[] Codes =
        {
            "RYU", "KEN", "CNL", "HND", "BLK", "ZGF", "GUL", "DSM", "BSN", "BLR", "SGT", "VEG", "AGL", "CHB", "RIC",
            "JHA", "BOS", "GKI", "GKN", "HWK", "CMY", "FLN", "DJY", "SKR", "ROS", "GEN", "DAN", "GUY", "CDY", "IBK",
            "MKT", "DDL", "ADN", "HKN", "JRI", "YUN", "YAN", "RYX", "GKX", "RLN", "ELN", "PSN", "HUG", "DCP",
        };

        public static readonly string[] Names =
        {
            "Ryu", "Ken", "Chun-Li", "E. Honda", "Blanka", "Zangief", "Guile", "Dhalsim", "Balrog", "Vega", "Sagat",
            "M. Bison", "C. Viper", "Rufus", "El Fuerte", "Abel", "Seth", "Akuma", "Gouken", "T. Hawk", "Cammy",
            "Fei Long", "Dee Jay", "Sakura", "Rose", "Gen", "Dan", "Guy", "Cody", "Ibuki", "Makoto", "Dudley", "Adon",
            "Hakan", "Juri", "Yun", "Yang", "Evil Ryu", "Oni", "Rolento", "Elena", "Poison", "Hugo", "Decapre",
        };

        // The fighters' IDs sorted by name (Abel, Adon, Akuma ... Zangief), the program's roster order.
        public static readonly int[] DisplayOrder = Enumerable.Range(0, Names.Length).OrderBy(i => Names[i], StringComparer.OrdinalIgnoreCase).ToArray();

        public static bool IsCode(string code) { return System.Array.IndexOf(Codes, code) >= 0; }

        public static string Name(string code)
        {
            int i = System.Array.IndexOf(Codes, code);
            return i >= 0 ? Names[i] : code;
        }
    }
}
