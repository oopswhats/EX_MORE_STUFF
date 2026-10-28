using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // A skin made into an EX More Stuff costume package in a slot of its own. Most of GameBanana's skins replace one of
    // the game's costumes (BLK_01.obj.emo, BLK_01_02.col.emb ...); newer ones may come in a code of their own (BLK_23 ...).
    // The skin's files are renamed to the slot, its versions become the costume's colors, and the rest of a full costume
    // (normals, shadow, physics, the colors it doesn't bring) comes from this PC's own game files. So the game's own
    // costume stays as it is, and the package is built on each PC from its own game.
    static class SkinImport
    {
        // the game's own costume files (its update and DLC folders; never patch_ae2_tu3, where mods go)
        static readonly string[] GameRoots = { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" };
        public static readonly Regex CostumeFile = new Regex(@"^([A-Za-z]{3})_(\d\d)(?:_(\d\d))?\.(obj\.emo|nml\.emb|shd\.emo|bsr|csb|col\.emb|obj\.emm|clw\.emo|msk\.emo)$", RegexOptions.IgnoreCase);
        static readonly Regex PictureFile = new Regex(@"^([A-Za-z]{3})_(\d\d)(?:_(\d\d))?\.png$", RegexOptions.IgnoreCase);
        // .clw/.msk.emo: Vega's claw and mask, which the game loads for every Vega costume (the base costume's when a mod has none)
        static readonly string[] Parts = { ".obj.emo", ".nml.emb", ".shd.emo", ".bsr", ".csb", ".clw.emo", ".msk.emo" };
        // a fighter's move files: a mod that changes them changes the fighter in every costume (online too: a desync), and
        // its model may need them (Juri gótica crashed without hers, user 2026-10-13: "dont install and why")
        static readonly Regex MovesFile = new Regex(@"^([A-Za-z]{3})\.(bac|bcm)$", RegexOptions.IgnoreCase);

        /// <summary>The user's own test switch (2026-10-16: "enable it just for my version ... without the moves"):
        /// allow_move_mods.txt in EX More Stuff's folder lets skins with move files go in as costumes, their move files left
        /// out (they're never unpacked). Nobody else has the file. TEST ONLY: take out before a release unless the user says.</summary>
        public static bool AllowMoveMods { get { try { return System.IO.File.Exists(System.IO.Path.Combine(AppFolders.Root, "allow_move_mods.txt")); } catch (Exception) { return false; } } }

        /// <summary>Why a mod with these files can't go in as costumes: "it changes Juri's moves too"; null when it doesn't.</summary>
        public static string ChangesMoves(IEnumerable<string> paths)
        {
            var fighters = paths.Select(p => MovesFile.Match(p.Substring(p.LastIndexOf('/') + 1))).Where(m => m.Success)
                .Select(m => m.Groups[1].Value.ToUpperInvariant()).Where(Fighters.IsCode).Distinct().ToList();
            return fighters.Count == 0 ? null : "it changes " + string.Join(" and ", fighters.Select(f => Fighters.Name(f) + "'s")) + " moves too";
        }

        // a fighter's voice (<F>_E.csb English, <F>_J.csb Japanese; its foley is in them too), or one costume's (<F>_NN_E.csb)
        static readonly Regex VoiceFile = new Regex(@"^([A-Za-z]{3})(?:_(\d\d))?_(E|J)\.csb$", RegexOptions.IgnoreCase);

        public sealed class Color
        {
            public byte[] Col, Emm;   // Emm null: the game's, for the color it was made over
            public int From;          // the color it was made over
        }

        /// <summary>One costume from a skin: one fighter's costume (one of the game's, or a code of its own), with all its
        /// versions (files or folders) as its colors.</summary>
        public sealed class Costume
        {
            public string Fighter;
            public int Number;                                       // the game's costume it replaces (1-7), or its own code
            public readonly Dictionary<string, byte[]> Parts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // by ending: ".obj.emo" ...
            public readonly Color[] Colors = new Color[10];          // colors 1-10; null: the game's
            // its colors past 10 (user 2026-10-19: Tom's 20-color costume lost 11-20): the game and Ember know colors 1-10 and
            // 30-99, so they're its extra colors from 30, in order (30-99 keep their number), in the same package
            public readonly SortedDictionary<int, Color> Extra = new SortedDictionary<int, Color>();
            public readonly Dictionary<string, byte[]> Pictures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // "" or "_01".."_10": its own pictures
            public readonly Dictionary<string, byte[]> Voices = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);     // "_E", "_J": the voice it came with
            public int Versions;                                     // versions that became colors
            public bool ModelsDiffer;                                // versions with models of their own: the first one's is used
            public bool OwnCode { get { return Number >= Catalog.FirstCustomCostume; } }
            public string Describe
            {
                get
                {
                    int own = Colors.Count(c => c != null);
                    return Fighters.Name(Fighter) + (Parts.ContainsKey(".obj.emo") ? ", new model" : "") + (own > 0 ? ", " + own + " color" + (own > 1 ? "s" : "") + " of its own" : "") +
                           (Extra.Count > 0 ? ", " + Extra.Count + " extra color" + (Extra.Count > 1 ? "s" : "") + " (" + Extra.Keys.First() + "-" + Extra.Keys.Last() + ")" : "") +
                           (Voices.Count > 0 ? ", its own voice" : "");
                }
            }
        }

        /// <summary>The costumes in a mod's files (every file and folder of it, paths as "file/folder/name").</summary>
        public static List<Costume> Find(IList<KeyValuePair<string, byte[]>> files)
        {
            var costumes = new List<Costume>();
            var groups = files.Select(f => new { f.Key, f.Value, Name = f.Key.Substring(f.Key.LastIndexOf('/') + 1), Folder = f.Key.LastIndexOf('/') < 0 ? "" : f.Key.Substring(0, f.Key.LastIndexOf('/')) })
                .Select(f => new { f.Value, f.Folder, Match = CostumeFile.Match(f.Name), Picture = PictureFile.Match(f.Name) })
                .Where(f => f.Match.Success || f.Picture.Success)
                .Select(f => { Match m = f.Match.Success ? f.Match : f.Picture; return new { f.Value, f.Folder, Fighter = m.Groups[1].Value.ToUpperInvariant(), Number = int.Parse(m.Groups[2].Value),
                                                                                         Color = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0, Ending = f.Match.Success ? "." + m.Groups[4].Value.ToLowerInvariant() : ".png" }; })
                .Where(f => Fighters.IsCode(f.Fighter) && f.Number >= 1 && f.Number <= Catalog.LastSlot)
                .GroupBy(f => f.Fighter + "_" + f.Number.ToString("D2"));
            foreach (var g in groups.OrderBy(g => g.Key))
            {
                var c = new Costume { Fighter = g.First().Fighter, Number = g.First().Number };
                var versions = g.GroupBy(f => f.Folder, StringComparer.OrdinalIgnoreCase).OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).ToList();
                // the parts: from the first version that has each
                foreach (string part in Parts)
                {
                    var found = versions.SelectMany(v => v).FirstOrDefault(f => f.Color == 0 && f.Ending == part);
                    if (found != null) c.Parts[part] = found.Value;
                }
                var models = versions.SelectMany(v => v.Where(f => f.Color == 0 && f.Ending == ".obj.emo")).Select(f => f.Value).ToList();
                // the colors: one version keeps its color numbers (color 1 shows its first when it has no color 1, so the
                // skin is what's picked first); several versions each become the next colors, in order
                Func<int, byte[], byte[]> emm = (number, ownFolderEmm) =>
                    ownFolderEmm ?? versions.SelectMany(v => v).Where(f => f.Ending == ".obj.emm" && f.Color == number).Select(f => f.Value).FirstOrDefault();
                var colored = versions.Where(v => v.Any(f => f.Ending == ".col.emb" && f.Color >= 1 && f.Color <= 10)).ToList();
                var own = colored.SelectMany(v => v.Where(f => f.Ending == ".col.emb" && f.Color >= 1 && f.Color <= 10).OrderBy(f => f.Color)
                    .Select(f => new Color { Col = f.Value, From = f.Color, Emm = emm(f.Color, v.Where(e => e.Ending == ".obj.emm" && e.Color == f.Color).Select(e => e.Value).FirstOrDefault()) })).ToList();
                if (colored.Count == 1)
                {
                    foreach (Color color in own) c.Colors[color.From - 1] = color;
                    if (c.Colors[0] == null && own.Count > 0) c.Colors[0] = own[0];
                }
                else
                    for (int i = 0; i < own.Count && i < 10; i++) c.Colors[i] = own[i];
                // colors past 10: several versions' 11th on, and color files numbered 11-99; 11-29 become 30 up
                var past = new List<Tuple<int, Color>>();
                if (colored.Count > 1) foreach (Color x in own.Skip(10)) past.Add(Tuple.Create(0, x));
                foreach (var v in versions)
                    foreach (var f in v.Where(f => f.Ending == ".col.emb" && f.Color > 10 && f.Color <= Catalog.LastColor).OrderBy(f => f.Color))
                        past.Add(Tuple.Create(f.Color, new Color { Col = f.Value, From = 1, Emm = emm(f.Color, v.Where(e => e.Ending == ".obj.emm" && e.Color == f.Color).Select(e => e.Value).FirstOrDefault()) }));
                var renumbered = new Dictionary<int, int>();   // a color file's number -> its extra color's
                foreach (var x in past.Where(x => x.Item1 >= Catalog.FirstCustomColor)) if (!c.Extra.ContainsKey(x.Item1)) { c.Extra[x.Item1] = x.Item2; renumbered[x.Item1] = x.Item1; }
                int next = Catalog.FirstCustomColor;
                foreach (var x in past.Where(x => x.Item1 < Catalog.FirstCustomColor))
                {
                    while (c.Extra.ContainsKey(next)) next++;
                    if (next > Catalog.LastColor) break;
                    c.Extra[next] = x.Item2;
                    if (x.Item1 > 0 && !renumbered.ContainsKey(x.Item1)) renumbered[x.Item1] = next;
                }
                c.Versions = Math.Max(1, colored.Count);
                c.ModelsDiffer = models.Skip(1).Any(m => !m.SequenceEqual(models[0]));
                // its own pictures, for a costume in a code of its own (they show its colors in Ember's menus)
                if (c.OwnCode)
                    foreach (var p in versions.SelectMany(v => v).Where(f => f.Ending == ".png"))
                    {
                        int number = p.Color;
                        if (number > 10 && !renumbered.TryGetValue(number, out number)) continue;   // a color past 10: its extra color's
                        string key = number == 0 ? "" : "_" + number.ToString("D2");
                        if (!c.Pictures.ContainsKey(key)) c.Pictures[key] = p.Value;
                    }
                // a costume needs a model or at least one color of its own
                if (c.Parts.ContainsKey(".obj.emo") || own.Count > 0) costumes.Add(c);
            }
            // voice files that came with them: part of the costume, as their creator meant (user 2026-10-13), kept under its
            // own name (<F>_80_E.csb) so the fighter's other costumes keep the game's voice; EMBER plays it with that costume
            // (to come). One named for a costume (<F>_NN_E.csb) goes with that costume only, and wins over the fighter's.
            var voices = files.Select(f => new { f.Value, Match = VoiceFile.Match(f.Key.Substring(f.Key.LastIndexOf('/') + 1)) }).Where(v => v.Match.Success)
                .OrderBy(v => v.Match.Groups[2].Success ? 0 : 1).ToList();
            foreach (Costume c in costumes)
                foreach (var v in voices.Where(v => v.Match.Groups[1].Value.ToUpperInvariant() == c.Fighter && (!v.Match.Groups[2].Success || int.Parse(v.Match.Groups[2].Value) == c.Number)))
                {
                    string language = "_" + v.Match.Groups[3].Value.ToUpperInvariant();
                    if (!c.Voices.ContainsKey(language)) c.Voices[language] = v.Value;
                }
            return costumes;
        }

        // two colors' materials the same: both their own and byte for byte the same, or both the game's for the same color
        static bool SameMaterial(Color a, Color b)
        {
            if (a.Emm == null || b.Emm == null) return a.Emm == null && b.Emm == null && a.From == b.From;
            return a.Emm.Length == b.Emm.Length && a.Emm.SequenceEqual(b.Emm);
        }

        /// <summary>The costumes a mod holds, from the names in its files alone (fighter and costume, nothing read).</summary>
        public static IEnumerable<Tuple<string, int>> Named(IEnumerable<string> paths)
        {
            return paths.Select(p => CostumeFile.Match(p.Substring(p.LastIndexOf('/') + 1))).Where(m => m.Success)
                .Select(m => Tuple.Create(m.Groups[1].Value.ToUpperInvariant(), int.Parse(m.Groups[2].Value)))
                .Where(t => Fighters.IsCode(t.Item1) && t.Item2 >= 1 && t.Item2 <= Catalog.LastSlot).Distinct();
        }

        internal static string GameFile(string game, string fighter, string name)
        {
            foreach (string root in GameRoots)
            {
                string path = Path.Combine(game, root, "battle", "chara", fighter, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>A free seat for a skin without a shared one: the personal seats 84 down to 80, then 40 down to 8, then 79
        /// down to 41 (Catalog.OverflowFrom); none of GameBanana's mods holds it, and nothing of this PC's uses it: no package
        /// of EX More Stuff's (in the game or switched off) and no files of it in the game folder. 0 when all are taken.</summary>
        /// <summary>What to say when a fighter has no free slot left (user 2026-10-12: the player makes room by uninstalling).</summary>
        public static string NoRoom(string fighter)
        {
            return Fighters.Name(fighter) + " has no free slot left: uninstall one of " + Fighters.Name(fighter) + "'s costumes to make room";
        }

        public static int FreeSlot(string game, string fighter)
        {
            var taken = new HashSet<int>(Installer.Load(game).Concat(Installer.Removed(game)).Concat(Docked.Load()).Where(r => r.Item.IsCostume && r.Item.Fighter == fighter).Select(r => r.Item.Slot));
            // the personal seats first (84 down to 80); past them the middle of the shared ones, 40 down, then from the top,
            // 79 down (user 2026-10-12: they upload their own GameBanana mods from 79 down, the community fills from 8 up)
            var order = Enumerable.Range(Catalog.FirstPersonal, Catalog.LastPersonal - Catalog.FirstPersonal + 1).Reverse()
                .Concat(Enumerable.Range(Catalog.FirstCustomCostume, Catalog.OverflowFrom - Catalog.FirstCustomCostume + 1).Reverse())
                .Concat(Enumerable.Range(Catalog.OverflowFrom + 1, Catalog.LastShared - Catalog.OverflowFrom).Reverse());
            foreach (int slot in order)
                if (!taken.Contains(slot) && !SharedCodes.Held.Contains(SharedCodes.Code(fighter, slot)) && !File.Exists(Path.Combine(Game.PatchFolder(game), "battle", "chara", fighter, fighter + "_" + slot.ToString("D2") + ".obj.emo")))
                    return slot;
            return 0;
        }

        /// <summary>Writes the package for `costume` in `slot` to `zipPath`: the skin's parts and colors where it has
        /// them, the rest from the game's own costume (the one it replaces, or costume 1); `picture` (any image, the mod's
        /// own) becomes its picture unless it brings its own.</summary>
        public static void Build(string game, Costume costume, int slot, byte[] picture, string zipPath)
        {
            int number = costume.OwnCode ? 1 : costume.Number;
            string from = costume.Fighter + "_" + number.ToString("D2"), to = costume.Fighter + "_" + slot.ToString("D2");
            Func<string, bool, byte[]> fromGame = (suffix, needed) =>
            {
                string path = GameFile(game, costume.Fighter, from + suffix);
                if (path != null) return File.ReadAllBytes(path);
                if (needed) throw new FileNotFoundException("the game's own " + from + suffix + " isn't there to complete the costume");
                return null;
            };
            // a model with many more bones than the game's costume it's built on, and no physics (.bsr) of its own for them:
            // the game crashes on it (Juri gótica: 361 bones on her Original's 252, user 2026-10-13). One with its own
            // physics is fine (the SFxT ports: Cammy X Lili's 302 on Cammy's 224).
            byte[] model;
            // (the user's test switch skips this check too, 2026-10-16: to find out whether the bones are really the cause)
            if (costume.Parts.TryGetValue(".obj.emo", out model) && !costume.Parts.ContainsKey(".bsr") && !AllowMoveMods)
            {
                string under = GameFile(game, costume.Fighter, from + ".obj.emo");
                int bones = Weight.Bones(model), theirs = under == null ? 0 : Weight.Bones(File.ReadAllBytes(under));
                if (theirs > 0 && bones > theirs * 1.1)
                    throw new InvalidDataException("its model has " + bones + " bones and no physics (.bsr) of its own for them, against the " + theirs + " of " +
                                                   Fighters.Name(costume.Fighter) + "'s " + CostumeGrid.CostumeName(number) + ": the game would crash on it");
            }
            var package = new List<KeyValuePair<string, byte[]>>();
            foreach (string part in Parts)
            {
                byte[] data;
                if (!costume.Parts.TryGetValue(part, out data) && part != ".csb") data = fromGame(part, part == ".obj.emo" || part == ".nml.emb");
                if (data != null) package.Add(new KeyValuePair<string, byte[]>(to + part, data));
            }
            // its voice, unless it's only a copy of the game's own (Juri gótica brings the game's JRI_E/J.csb, 9 MB that change nothing)
            foreach (var voice in costume.Voices)
            {
                string own = GameFile(game, costume.Fighter, costume.Fighter + voice.Key + ".csb");
                if (own != null && new FileInfo(own).Length == voice.Value.Length && File.ReadAllBytes(own).SequenceEqual(voice.Value)) continue;
                package.Add(new KeyValuePair<string, byte[]>(to + voice.Key + ".csb", voice.Value));
            }
            // A color the mod doesn't bring, when it brings some, and one it brings that looks the same as an earlier one (a
            // copy, as color 1 is of a skin's only color, or a change nobody would see), is a ready-made pattern
            // (ColorVariants): the first such color pattern 1, the next pattern 2 ..., with the material of the mod's
            // first color and the pattern's picture for Ember's gallery, so no two colors look alike and every PC gets the
            // same (user 2026-10-11). (A mod with no colors at all is made for the game's, so those stay.)
            var brought = new List<int>();
            var redone = new HashSet<int>();
            var looks = new Dictionary<int, List<byte[]>>();
            for (int i = 0; i < 10; i++)
            {
                if (costume.Colors[i] == null) continue;
                var look = ColorVariants.Look(costume.Colors[i].Col);
                // a copy only with the same material too: a color's material tints what its textures' alpha masks
                Color mine = costume.Colors[i];
                if (brought.Any(k => SameMaterial(costume.Colors[k], mine) && ColorVariants.LookSame(looks[k], look))) { redone.Add(i); continue; }
                looks[i] = look;
                brought.Add(i);
            }
            var missing = Enumerable.Range(0, 10).Where(i => !brought.Contains(i)).ToList();
            var madePictures = new HashSet<string>(redone.Select(i => "_" + (i + 1).ToString("D2")), StringComparer.OrdinalIgnoreCase);
            Color first = brought.Count > 0 ? costume.Colors[brought[0]] : null;
            for (int color = 1; color <= 10; color++)
            {
                Color own = brought.Contains(color - 1) ? costume.Colors[color - 1] : null;
                string suffix = "_" + color.ToString("D2");
                if (own != null || first == null)
                {
                    package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".col.emb", own != null ? own.Col : fromGame(suffix + ".col.emb", true)));
                    package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".obj.emm", own != null ? own.Emm ?? fromGame("_" + own.From.ToString("D2") + ".obj.emm", true) : fromGame(suffix + ".obj.emm", true)));
                    continue;
                }
                int pattern = missing.IndexOf(color - 1) + 1;
                package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".col.emb", ColorVariants.Patterned(first.Col, pattern)));
                package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".obj.emm", first.Emm ?? fromGame("_" + first.From.ToString("D2") + ".obj.emm", true)));
                if (costume.Pictures.ContainsKey(suffix) && !madePictures.Contains(suffix)) continue;
                package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".png", ColorVariants.PatternPicture(pattern)));
            }
            // its colors past 10, as extra colors 30 up in the same package (Ember lists 30-99 for a costume)
            foreach (var x in costume.Extra)
            {
                string suffix = "_" + x.Key.ToString("D2");
                package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".col.emb", x.Value.Col));
                package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".obj.emm", x.Value.Emm ?? (first != null ? first.Emm : null) ?? fromGame("_01.obj.emm", true)));
            }
            // the costume's own picture: the mod page's (`picture`, a GameBanana install: "thumbnail = the mod page's
            // picture", user 2026-10-09), never one in the mod's files, which may be older (user 2026-10-12: Seth's card
            // showed a render from before its upload; 2026-10-13: "just use the game banana picture ... card image in the
            // file, its not needed"), even when the page's won't read or couldn't be had (an empty `picture`). Its colors'
            // always stay.
            byte[] page = Pictures.Png(picture, Pictures.PackageSide);   // made smaller: a mod's poster can be huge (user 2026-10-18)
            foreach (var p in costume.Pictures.Where(p => !madePictures.Contains(p.Key) && !(p.Key == "" && picture != null)))
                package.Add(new KeyValuePair<string, byte[]>(to + p.Key + ".png", Pictures.Fit(p.Value, Pictures.PackageSide)));
            if (page != null) package.Add(new KeyValuePair<string, byte[]>(to + ".png", page));
            string temp = zipPath + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            if (File.Exists(temp)) File.Delete(temp);   // left by a write a crash stopped
            using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var file in package)
                    using (Stream s = zip.CreateEntry(file.Key, CompressionLevel.Optimal).Open()) s.Write(file.Value, 0, file.Value.Length);
            SafeFile.Swap(temp, zipPath);   // the old zip until the new one is complete
        }
    }
}
