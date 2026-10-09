using System;
using System.Collections.Generic;
using System.Linq;

namespace ExMoreStuff
{
    // Which costume codes are shared, so that everyone who installs the same skin has it under the same code and players
    // see each other's (a code is the fighter and slot, "BLK 08"). Nobody keeps a list up to date: GameBanana is the list.
    //  - The skins from before EX More Stuff replace one of the game's costumes, so they have no code of their own: each
    //    got one, once, below (8 up per fighter, oldest first). Its versions become that costume's colors.
    //  - Newer mods come in a code of their own (their files are BLK_23 ...): it's theirs, read from GameBanana's list of
    //    what's in each file. If two use the same code, the one uploaded first keeps it.
    // Slots 85-99 aren't shared seats: a mod made in one of them gets none. Everything without a shared seat goes in a
    // free seat on the player's PC, counting down from 84 (seats are claimed from 8 up): only that player sees it.
    static class SharedCodes
    {
        // GameBanana mod -> its code (given 2026-10-09; never change one, players have them installed)
        static readonly Dictionary<long, string> Given = new Dictionary<long, string>
        {
            { 251562, "JHA 08" },   // USF4 Alex for Abel
            { 251565, "RYU 08" },   // USF4 True Ryu
            { 369461, "RYU 09" },   // Classic MK Ninja for Ryu
            { 251563, "RYX 08" },   // USF4 True Evil Ryu
            { 251564, "CMY 08" },   // SF5 Inspired Cammy
            { 369660, "CMY 09" },   // Kitana MK vs DC for Cammy
            { 299321, "BOS 08" },   // Seth Venom
            { 323687, "BOS 09" },   // Seth colossal titan
            { 369457, "BOS 10" },   // Shao Kahn for Seth
            { 320422, "JRI 08" },   // Juri cosplay Spinela
            { 324405, "JRI 09" },   // Juri gotica
            { 369479, "JRI 10" },   // Mileena MK9 for Juri
            { 369454, "SGT 08" },   // Kintaro MK9 for Sagat
            { 369458, "PSN 08" },   // Female Shao Kahn for Poison
            { 369471, "PSN 09" },   // Jade MK9 for Poison
            { 369463, "HWK 08" },   // Nightwolf for T.Hawk
            { 369464, "AGL 08" },   // C.Viper as MK9 Sonya
            { 369465, "SKR 08" },   // MK1 Sonya for Sakura
            { 369468, "SKR 09" },   // MK3 Sonya for Sakura
            { 688241, "SKR 10" },   // Little Arcade Worker
            { 369475, "IBK 08" },   // Kitana MK9 for Ibuki
            { 369646, "GEN 08" },   // Shang Tsung MKSM for Gen
            { 369649, "VEG 08" },   // Raiden MKvsDC for M.Bison
            { 369651, "VEG 09" },   // Kung Lao MK2 for M.Bison
            { 369654, "VEG 10" },   // Sub-Zero MKvsDC for M.Bison
            { 371736, "KEN 08" },   // SF Alpha Ken
            { 680656, "BLK 08" },   // Dark Hado Blanka
        };

        // GameBanana stage mods from before EX More Stuff (each replaces one of the game's stages, so it has no code of its
        // own) -> their stage code, B01 up, oldest first (given 2026-10-09; never change one)
        static readonly Dictionary<long, string> GivenStages = new Dictionary<long, string>
        {
            { 684723, "B01" },   // The Retrowave Zone
        };

        /// <summary>The shared stage code a mod's stage (made from `code`) goes in: its given B## (a mod replacing one of the
        /// game's stages), or its own code when it holds it; null: a personal one.</summary>
        public static string StageCode(GameBanana.Mod mod, string code)
        {
            string given;
            if (!Stages.IsCustomCode(code)) return GivenStages.TryGetValue(mod.Id, out given) ? given : null;
            GameBanana.Mod holder;
            return HeldBy.TryGetValue("stage " + code, out holder) && holder.Id == mod.Id && !Catalog.NotFree(true, code, 0) ? code : null;
        }

        public static string Code(string fighter, int slot) { return fighter + " " + slot.ToString("D2"); }

        /// <summary>The shared seats GameBanana's mods hold ("BLK 08"), as last read: a seat-less install doesn't take one.</summary>
        public static HashSet<string> Held = new HashSet<string>();

        /// <summary>Who holds what on GameBanana, as last read: costume seats ("BLK 08") and stage codes of their own
        /// ("stage XYZ", a mod's STG_XYZ.emz; the first upload holds it). For the free-code checker.</summary>
        public static Dictionary<string, GameBanana.Mod> HeldBy = new Dictionary<string, GameBanana.Mod>();

        static readonly System.Text.RegularExpressions.Regex StageFile = new System.Text.RegularExpressions.Regex(@"^STG_([A-Za-z0-9]{3})(\.tex)?\.emz$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Each shared code and the GameBanana mod it belongs to.</summary>
        public static Dictionary<string, GameBanana.Mod> Holders(IEnumerable<GameBanana.Mod> mods)
        {
            var holders = new Dictionary<string, GameBanana.Mod>();
            var list = mods.ToList();
            foreach (var given in Given)
            {
                var mod = list.FirstOrDefault(m => m.Id == given.Key);
                if (mod != null) holders[given.Value] = mod;
            }
            foreach (var mod in list.OrderBy(m => m.Added).ThenBy(m => m.Id))
                foreach (var costume in SkinImport.Named(mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents)))
                {
                    string code = Code(costume.Item1, costume.Item2);
                    if (costume.Item2 >= Catalog.FirstCustomCostume && costume.Item2 <= Catalog.LastShared && !holders.ContainsKey(code)) holders[code] = mod;
                }
            Held = new HashSet<string>(holders.Keys);
            var heldBy = new Dictionary<string, GameBanana.Mod>(holders);
            foreach (var given in GivenStages)
            {
                var mod = list.FirstOrDefault(m => m.Id == given.Key);
                if (mod != null) heldBy["stage " + given.Value] = mod;
            }
            foreach (var mod in list.OrderBy(m => m.Added).ThenBy(m => m.Id))
                foreach (string path in mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents))
                {
                    var m = StageFile.Match(path.Substring(path.LastIndexOf('/') + 1));
                    string code = m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
                    if (code != null && Stages.IsCustomCode(code) && !heldBy.ContainsKey("stage " + code)) heldBy["stage " + code] = mod;
                }
            HeldBy = heldBy;
            return holders;
        }

        /// <summary>The shared slot a mod's costume goes in (the one it replaces, or its own code), or 0: a personal one.</summary>
        public static int Slot(GameBanana.Mod mod, string fighter, int number, Dictionary<string, GameBanana.Mod> holders)
        {
            if (number > Catalog.LastShared) return 0;   // not free
            if (number >= Catalog.FirstCustomCostume)
            {
                GameBanana.Mod holder;
                return holders.TryGetValue(Code(fighter, number), out holder) && holder.Id == mod.Id ? number : 0;
            }
            string given;
            if (!Given.TryGetValue(mod.Id, out given) || !given.StartsWith(fighter + " ")) return 0;
            // a mod replacing several of the game's costumes: the code is the first one's
            var first = SkinImport.Named(mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents)).Where(c => c.Item1 == fighter && c.Item2 < Catalog.FirstCustomCostume).OrderBy(c => c.Item2).FirstOrDefault();
            return first != null && first.Item2 == number ? int.Parse(given.Substring(4)) : 0;
        }
    }
}
