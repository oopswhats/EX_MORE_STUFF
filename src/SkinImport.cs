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
        public static readonly Regex CostumeFile = new Regex(@"^([A-Za-z]{3})_(\d\d)(?:_(\d\d))?\.(obj\.emo|nml\.emb|shd\.emo|bsr|csb|col\.emb|obj\.emm)$", RegexOptions.IgnoreCase);
        static readonly Regex PictureFile = new Regex(@"^([A-Za-z]{3})_(\d\d)(?:_(\d\d))?\.png$", RegexOptions.IgnoreCase);
        static readonly string[] Parts = { ".obj.emo", ".nml.emb", ".shd.emo", ".bsr", ".csb" };

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
            public readonly Dictionary<string, byte[]> Pictures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // "" or "_01".."_10": its own pictures
            public int Versions;                                     // versions that became colors
            public bool ModelsDiffer;                                // versions with models of their own: the first one's is used
            public bool OwnCode { get { return Number >= Catalog.FirstCustomCostume; } }
            public string Describe
            {
                get { int own = Colors.Count(c => c != null); return Fighters.Name(Fighter) + (Parts.ContainsKey(".obj.emo") ? ", new model" : "") + (own > 0 ? ", " + own + " color" + (own > 1 ? "s" : "") + " of its own" : ""); }
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
                c.Versions = Math.Max(1, colored.Count);
                c.ModelsDiffer = models.Skip(1).Any(m => !m.SequenceEqual(models[0]));
                // its own pictures, for a costume in a code of its own (they show its colors in Ember's menus)
                if (c.OwnCode)
                    foreach (var p in versions.SelectMany(v => v).Where(f => f.Ending == ".png"))
                    {
                        string key = p.Color == 0 ? "" : "_" + p.Color.ToString("D2");
                        if (!c.Pictures.ContainsKey(key)) c.Pictures[key] = p.Value;
                    }
                // a costume needs a model or at least one color of its own
                if (c.Parts.ContainsKey(".obj.emo") || own.Count > 0) costumes.Add(c);
            }
            return costumes;
        }

        /// <summary>The costumes a mod holds, from the names in its files alone (fighter and costume, nothing read).</summary>
        public static IEnumerable<Tuple<string, int>> Named(IEnumerable<string> paths)
        {
            return paths.Select(p => CostumeFile.Match(p.Substring(p.LastIndexOf('/') + 1))).Where(m => m.Success)
                .Select(m => Tuple.Create(m.Groups[1].Value.ToUpperInvariant(), int.Parse(m.Groups[2].Value)))
                .Where(t => Fighters.IsCode(t.Item1) && t.Item2 >= 1 && t.Item2 <= Catalog.LastSlot).Distinct();
        }

        static string GameFile(string game, string fighter, string name)
        {
            foreach (string root in GameRoots)
            {
                string path = Path.Combine(game, root, "battle", "chara", fighter, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>A free seat for a skin without a shared one, counting down from 84 (GameBanana's seats are claimed from 8 up): none of
        /// GameBanana's mods holds it, and nothing of this PC's uses it: no package of EX More Stuff's (in the game or
        /// switched off) and no files of it in the game folder. 0 when all are taken.</summary>
        public static int FreeSlot(string game, string fighter)
        {
            var taken = new HashSet<int>(Installer.Load(game).Concat(Installer.Removed(game)).Where(r => r.Item.IsCostume && r.Item.Fighter == fighter).Select(r => r.Item.Slot));
            for (int slot = Catalog.LastShared; slot >= Catalog.FirstCustomCostume; slot--)
                if (!taken.Contains(slot) && !SharedCodes.Held.Contains(SharedCodes.Code(fighter, slot)) && !File.Exists(Path.Combine(Game.PatchFolder(game), "battle", "chara", fighter, fighter + "_" + slot.ToString("D2") + ".obj.emo")))
                    return slot;
            return 0;
        }

        static byte[] unfinished;

        /// <summary>Ember's picture for a color a mod doesn't bring: "Unfinished mod" in white on black, the size of
        /// Ember's own color pictures (256 × 384).</summary>
        public static byte[] Unfinished
        {
            get
            {
                if (unfinished != null) return unfinished;
                using (var bitmap = new Bitmap(256, 384, PixelFormat.Format24bppRgb))
                using (var g = Graphics.FromImage(bitmap))
                using (var font = new Font("Segoe UI Semibold", 36f, GraphicsUnit.Pixel))
                using (var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var png = new MemoryStream())
                {
                    g.Clear(System.Drawing.Color.Black);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString("Unfinished\nmod", font, Brushes.White, new RectangleF(0, 0, 256, 384), centered);
                    bitmap.Save(png, ImageFormat.Png);
                    return unfinished = png.ToArray();
                }
            }
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
            var package = new List<KeyValuePair<string, byte[]>>();
            foreach (string part in Parts)
            {
                byte[] data;
                if (!costume.Parts.TryGetValue(part, out data) && part != ".csb") data = fromGame(part, part == ".obj.emo" || part == ".nml.emb");
                if (data != null) package.Add(new KeyValuePair<string, byte[]>(to + part, data));
            }
            // A color the mod doesn't bring, when it brings some, is the game's, made for the game's model: its picture in
            // Ember says so. (A mod with no colors at all is made for the game's, so they need no picture.)
            bool bringsColors = costume.Colors.Any(c => c != null);
            for (int color = 1; color <= 10; color++)
            {
                Color own = costume.Colors[color - 1];
                string suffix = "_" + color.ToString("D2");
                package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".col.emb", own != null ? own.Col : fromGame(suffix + ".col.emb", true)));
                package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".obj.emm", own != null ? own.Emm ?? fromGame("_" + own.From.ToString("D2") + ".obj.emm", true) : fromGame(suffix + ".obj.emm", true)));
                if (own == null && bringsColors && !costume.Pictures.ContainsKey(suffix))
                    package.Add(new KeyValuePair<string, byte[]>(to + suffix + ".png", Unfinished));
            }
            foreach (var p in costume.Pictures) package.Add(new KeyValuePair<string, byte[]>(to + p.Key + ".png", p.Value));
            if (!costume.Pictures.ContainsKey("") && picture != null)
                try
                {
                    using (var image = Image.FromStream(new MemoryStream(picture)))
                    using (var png = new MemoryStream())
                    {
                        image.Save(png, ImageFormat.Png);
                        package.Add(new KeyValuePair<string, byte[]>(to + ".png", png.ToArray()));
                    }
                }
                catch (Exception) { }   // a picture that won't read: the costume goes in without one
            string temp = zipPath + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var file in package)
                    using (Stream s = zip.CreateEntry(file.Key, CompressionLevel.Optimal).Open()) s.Write(file.Value, 0, file.Value.Length);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(temp, zipPath);
        }
    }
}
