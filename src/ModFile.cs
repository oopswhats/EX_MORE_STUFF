using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // A mod file the player has (Advanced > Add Mod From File), or a GameBanana stage mod: its skins become costumes (SkinImport),
    // its new colors (30-99) colors of their own, and its stages become stages of their own, each in a personal slot or code (costumes in a personal seat 84-80, then a free one 40 down, then 79 down; stages U01 up, which
    // only that player sees). A stage mod usually replaces one of the game's (STG_TRN.emz ...): it's re-coded to its new
    // code, the part it doesn't bring (model or textures) comes from the game, and its music goes with it. Only the
    // game's file types are taken out of the archive and nothing in it is run (GameBanana.Unpack).
    static class ModFile
    {
        static readonly string[] GameRoots = { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" };
        static readonly Regex StageFile = new Regex(@"^STG_([A-Za-z0-9]{3})(\.tex)?\.emz$", RegexOptions.IgnoreCase);
        static readonly Regex MusicFile = new Regex(@"^BGM_([A-Za-z0-9]{3})([23])?\.csb$", RegexOptions.IgnoreCase);
        static readonly Regex ColorFile = new Regex(@"^([A-Za-z0-9]{3})_(\d\d)_(\d\d)\.(col\.emb|obj\.emm|png)$", RegexOptions.IgnoreCase);
        static readonly Regex CostumePicture = new Regex(@"^[A-Za-z]{3}_\d\d(_\d\d)?\.png$|^STG_", RegexOptions.IgnoreCase);

        public sealed class Stage
        {
            public string Code, Folder;   // the stage it was made from (TRN ...), and where in the archive
            public byte[] Emz, Tex;       // null: the game's
            public readonly Dictionary<string, byte[]> Music = new Dictionary<string, byte[]>();   // "", "2", "3": rounds
            public string Describe { get { return Stages.Name(Code) + (Folder != "" ? " (" + Folder.Substring(Folder.LastIndexOf('/') + 1) + ")" : ""); } }
        }

        /// <summary>A new color of a costume in a mod's files: <CHR>_<NN>_<CC>.col.emb (CC 30-99), its material and picture.</summary>
        public sealed class NewColor
        {
            public string Fighter;
            public int Costume, Number;
            public byte[] Col, Emm, Png;   // Emm null: the costume's color 1's
            public string Describe { get { return Fighters.Name(Fighter) + " " + (Costume < Catalog.FirstCustomCostume ? "costume " : "") + Costume.ToString("D2") + " color " + Number; } }
        }

        public static List<NewColor> FindColors(IList<KeyValuePair<string, byte[]>> files)
        {
            var colors = new List<NewColor>();
            foreach (var f in files)
            {
                Match m = ColorFile.Match(NameOf(f.Key));
                if (!m.Success) continue;
                string fighter = m.Groups[1].Value.ToUpperInvariant();
                int costume = int.Parse(m.Groups[2].Value), number = int.Parse(m.Groups[3].Value);
                if (!Fighters.IsCode(fighter) || costume < 1 || number < Catalog.FirstColor(costume) || number > Catalog.LastColor) continue;
                NewColor c = colors.FirstOrDefault(x => x.Fighter == fighter && x.Costume == costume && x.Number == number);
                if (c == null) colors.Add(c = new NewColor { Fighter = fighter, Costume = costume, Number = number });
                string kind = m.Groups[4].Value.ToLowerInvariant();
                if (kind == "col.emb") c.Col = c.Col ?? f.Value; else if (kind == "obj.emm") c.Emm = c.Emm ?? f.Value; else c.Png = c.Png ?? f.Value;
            }
            return colors.Where(c => c.Col != null).OrderBy(c => c.Fighter).ThenBy(c => c.Costume).ThenBy(c => c.Number).ToList();
        }

        /// <summary>Color `wanted` of a costume, or when a package of EX More Stuff's (or a file it didn't put there) has it,
        /// the first free one from 30 up (11 on a custom costume); 0 when none is left.</summary>
        public static int FreeColor(string game, string fighter, int costume, int wanted, ICollection<int> alsoTaken = null)
        {
            var taken = new HashSet<int>(Installer.Load(game).Concat(Installer.Removed(game)).Concat(Docked.Load())
                .Where(r => r.Item.IsColor && r.Item.Fighter == fighter && r.Item.Slot == costume).Select(r => r.Item.Color));
            if (alsoTaken != null) taken.UnionWith(alsoTaken);   // numbers given to the same pack's other colors
            Func<int, bool> free = n => !taken.Contains(n) && !File.Exists(Path.Combine(Game.PatchFolder(game), "battle", "chara", fighter,
                fighter + "_" + costume.ToString("D2") + "_" + n.ToString("D2") + ".col.emb"));
            if (wanted >= Catalog.FirstColor(costume) && wanted <= Catalog.LastColor && free(wanted)) return wanted;
            for (int n = Catalog.FirstColor(costume); n <= Catalog.LastColor; n++) if (free(n)) return n;
            return 0;
        }

        // a file of a costume as the game loads it: the patch folder's (a custom costume, or the player's edit), else the game's
        static byte[] CostumeFile(string game, string fighter, string name)
        {
            foreach (string root in new[] { "patch_ae2_tu3" }.Concat(GameRoots))
            {
                string path = Path.Combine(game, root, "battle", "chara", fighter, name);
                if (File.Exists(path)) return File.ReadAllBytes(path);
            }
            return null;
        }

        static string FolderOf(string path) { int i = path.LastIndexOf('/'); return i < 0 ? "" : path.Substring(0, i); }
        static string NameOf(string path) { return path.Substring(path.LastIndexOf('/') + 1); }

        /// <summary>The stages in a mod's files: each stage file pair (STG_xxx.emz, .tex.emz) in a folder, with its music.</summary>
        public static List<Stage> FindStages(IList<KeyValuePair<string, byte[]>> files)
        {
            var stages = new List<Stage>();
            foreach (var f in files)
            {
                Match m = StageFile.Match(NameOf(f.Key));
                if (!m.Success) continue;
                string code = m.Groups[1].Value.ToUpperInvariant(), folder = FolderOf(f.Key);
                Stage s = stages.FirstOrDefault(x => x.Code == code && string.Equals(x.Folder, folder, StringComparison.OrdinalIgnoreCase));
                if (s == null) stages.Add(s = new Stage { Code = code, Folder = folder });
                if (m.Groups[2].Success) s.Tex = f.Value; else s.Emz = f.Value;
            }
            // its music: beside it, else anywhere in the mod
            foreach (Stage s in stages)
                foreach (var f in files.OrderBy(f => string.Equals(FolderOf(f.Key), s.Folder, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                {
                    Match m = MusicFile.Match(NameOf(f.Key));
                    if (m.Success && m.Groups[1].Value.ToUpperInvariant() == s.Code && !s.Music.ContainsKey(m.Groups[2].Value)) s.Music[m.Groups[2].Value] = f.Value;
                }
            return stages.OrderBy(s => s.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Code).ToList();
        }

        static byte[] GameStageFile(string game, string name)
        {
            foreach (string root in GameRoots)
            {
                string path = Path.Combine(game, root, "battle", "stage", name);
                if (File.Exists(path)) return File.ReadAllBytes(path);
            }
            return null;
        }

        // players' own stage codes, in the order they're given: U01-U99, 0U0-9U9, 00U-99U
        static IEnumerable<string> PersonalStageCodes()
        {
            for (int n = 1; n <= 99; n++) yield return "U" + n.ToString("D2");
            for (int n = 0; n <= 99; n++) yield return (n / 10) + "U" + (n % 10);
            for (int n = 0; n <= 99; n++) yield return n.ToString("D2") + "U";
        }

        /// <summary>The first personal stage code (U01 up) nothing uses (no package of EX More Stuff's, no file there), or null.</summary>
        public static string FreeStageCode(string game)
        {
            var taken = new HashSet<string>(Installer.Load(game).Concat(Installer.Removed(game)).Concat(Docked.Load()).Where(r => r.Item.IsStage).Select(r => r.Item.Code));
            foreach (string code in PersonalStageCodes())
            {
                if (!taken.Contains(code) && !File.Exists(Path.Combine(Game.PatchFolder(game), "battle", "stage", "STG_" + code + ".emz"))) return code;
            }
            return null;
        }

        /// <summary>Writes the package for `stage` under `code` to `zipPath`: its files re-coded (the part it doesn't bring
        /// from the game), its music, `picture` as its picture.</summary>
        public static void BuildStage(string game, Stage stage, string code, byte[] picture, string zipPath)
        {
            string from = "STG_" + stage.Code, to = "STG_" + code;
            byte[] emz = stage.Emz ?? GameStageFile(game, from + ".emz"), tex = stage.Tex ?? GameStageFile(game, from + ".tex.emz");
            if (emz == null) throw new FileNotFoundException("it has no " + from + ".emz and the game has none to complete it");
            var package = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(to + ".emz", StagePack.Recode(emz, stage.Code, code)) };
            if (tex != null) package.Add(new KeyValuePair<string, byte[]>(to + ".tex.emz", StagePack.Recode(tex, stage.Code, code)));
            foreach (var m in stage.Music) package.Add(new KeyValuePair<string, byte[]>("BGM_" + code + m.Key + ".csb", m.Value));
            byte[] png = Png(picture);
            if (png != null) package.Add(new KeyValuePair<string, byte[]>(to + ".png", png));
            Write(package, zipPath);
        }

        // a picture that won't read: it goes in without one
        public static byte[] Png(byte[] picture) { return Pictures.Png(picture, Pictures.PackageSide); }   // a mod's own picture: made smaller

        public static void Write(List<KeyValuePair<string, byte[]>> package, string zipPath)
        {
            string temp = zipPath + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            if (File.Exists(temp)) File.Delete(temp);   // left by a write a crash stopped
            using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var file in package)
                    using (Stream s = zip.CreateEntry(file.Key, CompressionLevel.Optimal).Open()) s.Write(file.Value, 0, file.Value.Length);
            SafeFile.Swap(temp, zipPath);   // the old zip until the new one is complete
        }

        public static string Md5(string path)
        {
            using (var md5 = MD5.Create())
            using (var s = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>A name from a mod's file name: "dark_hado_blanka_ver1" -> "dark hado blanka ver1".</summary>
        public static string NameFrom(string path)
        {
            return Regex.Replace(Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('+', ' '), @"\s+", " ").Trim();
        }

        public sealed class Result
        {
            public readonly List<string> Into = new List<string>(), Problems = new List<string>();
            public bool Shared;   // a stage went in its shared code
        }

        /// <summary>Installs what a downloaded mod file holds, each costume and stage in a personal slot or code. `id`
        /// names where it came from ("deviantart:123", or null: the file itself), so installing it again updates it in
        /// place. `say` reports progress.</summary>
        public static Result Install(string game, string path, string id, string name, string author, string where, byte[] picture, Action<string> say,
                                     bool check = false)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            string patch = Game.PatchFolder(game);
            Directory.CreateDirectory(patch);
            say("Unpacking " + Path.GetFileName(path) + "...");
            var files = GameBanana.Unpack(path, Path.Combine(patch, "ex_more_stuff.unpack"));
            return InstallFiles(game, files, id ?? "file:" + Md5(path).Substring(0, 16), string.IsNullOrWhiteSpace(name) ? NameFrom(path) : name,
                                author, where ?? " (" + Path.GetFileName(path) + ")", picture, say, check: check);
        }

        // A package an earlier dock of this mod built (named as InstallFiles names them: "<name> - <code>.zip"), not in the
        // game: docked again as it is, with what was changed in it since (its code, its stage picture: user 2026-10-14,
        // Antarctica's chosen picture was lost when it was docked again and its zip built over). The newest if several.
        static string BuiltBefore(string game, string folder, string head, string tail, Func<Item, bool> kind)
        {
            if (!Directory.Exists(folder)) return null;
            var inGame = new HashSet<string>(Installer.Load(game).Concat(Installer.Removed(game)).Where(r => !string.IsNullOrEmpty(r.Package)).Select(r => Path.GetFullPath(r.Package)),
                                             StringComparer.OrdinalIgnoreCase);
            var named = new Regex("^" + Regex.Escape(head) + "[A-Za-z0-9]+" + Regex.Escape(tail) + @"\.zip$", RegexOptions.IgnoreCase);
            return Directory.GetFiles(folder, "*.zip")
                .Where(f => named.IsMatch(Path.GetFileName(f)) && !inGame.Contains(Path.GetFullPath(f)))
                .Where(f => { Item it = Installer.Package(f); return it != null && kind(it); })
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        static void DockAgain(string zip, Action<string> dock, Result result, string what)
        {
            if (Docked.Has(zip)) { result.Problems.Add(what + " (" + Path.GetFileName(zip) + ") is docked already"); return; }
            dock(zip);
            result.Into.Add(what + ", as you left it");
        }

        /// <summary>A mod file that isn't a package, docked for edits: its package built as Install builds it (in a slot or
        /// code of the player's own) and docked (Docked), not put in the game; the game may be open. A .zip that makes one
        /// package becomes that package itself (user 2026-10-14: "when i press the image button it should put the image in
        /// there", the zip they added, as their costume packages are): its files converted in place, so the picture and code
        /// changes go into it and it's the file to upload. A mod that makes several, or a .rar or .7z (not written), gets
        /// copies in Mods\ as Install builds them. Into: what was docked ("stage U03"); Problems: what wasn't.</summary>
        public static Result Dock(string game, string path, Action<string> say, bool check = false)
        {
            string patch = Game.PatchFolder(game);
            Directory.CreateDirectory(patch);
            say("Unpacking " + Path.GetFileName(path) + "...");
            var files = GameBanana.Unpack(path, Path.Combine(patch, "ex_more_stuff.unpack"));
            var built = new List<string>();
            var result = InstallFiles(game, files, "file:" + Md5(path).Substring(0, 16), NameFrom(path), null, " (" + Path.GetFileName(path) + ")", null, say, dock: built.Add, check: check);
            if (built.Count == 1 && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(built[0], path, true);   // the player's zip is the package now
                    File.Delete(built[0]);
                    built[0] = path;
                }
                catch (Exception ex) { result.Problems.Add("your zip couldn't be made the package (" + ex.Message + "), so a copy in the Mods folder is docked"); }
            }
            foreach (string zip in built) Docked.Add(zip);
            return result;
        }

        /// <summary>The same with a mod's files already unpacked (paths as "file/folder/name"); `where` says where it came
        /// from, for its description (" on GameBanana (page)").</summary>
        public static Result InstallFiles(string game, IList<KeyValuePair<string, byte[]>> files, string id, string name, string author, string where, byte[] picture, Action<string> say,
                                          string version = "", Func<string, string> sharedStage = null, Action<string> dock = null,
                                          bool check = false)
        {
            // check: each costume built gets the costume check (InstallCheck): fixed, or switched off and locked
            // dock: each package built goes to it (docked for edits, user 2026-10-14: "its saying i cant dock this stage")
            // instead of into the game, which then can stay open; music and override stages go straight into the game, so
            // they're left out
            if (dock == null && Game.IsRunning()) throw new InvalidOperationException("close the game first");
            string tooNew = Overrides.TooNew(files.Select(f => f.Key));   // needs_exms_<version>.txt: made for a newer EX More Stuff
            if (tooNew != null) throw new InvalidDataException("it " + tooNew);
            string patch = Game.PatchFolder(game);
            name = name.Trim();
            // an override stage (override_KOR.txt in it): in its game stage's place, no code of its own (Overrides)
            if (Overrides.Target(files.Select(f => f.Key)) != null)
            {
                if (dock != null) throw new InvalidDataException("an override stage goes straight into the game's stage, so it can't be docked: add it without docking");
                var replaced = new Result();
                string target = Overrides.Install(game, files, id + ":override", name, author, version, picture);
                replaced.Into.Add("in place of " + Stages.Name(target));
                return replaced;
            }
            var costumes = SkinImport.Find(files);
            var stages = FindStages(files);
            var colors = FindColors(files);
            // music named the library's way (TRN_3_MAIN_Jackson.csb): the game file goes where its name says
            var music = files.Where(f => f.Key.EndsWith(".csb", StringComparison.OrdinalIgnoreCase)).Select(f =>
            {
                int layer; string song;
                MusicSlot slot = MusicBank.TaggedSlot(NameOf(f.Key), out layer, out song);
                return slot == null ? null : new { Slot = slot, Layer = layer, Song = song, Bank = f.Value };
            }).Where(m => m != null).GroupBy(m => m.Slot.File, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(m => m.Layer).First()).ToList();
            var effects = files.Where(f => Effects.IsFile(f.Key)).ToList();   // a fighter's effects (RYX.vfx.emz ...: Effects)
            if (costumes.Count == 0 && stages.Count == 0 && colors.Count == 0 && music.Count == 0 && effects.Count == 0)
                throw new InvalidDataException("it has no costume, color, stage, music or effects files EX More Stuff can use (menus, HUDs, announcers and other sounds aren't supported)");
            // a skin that changes its fighter's moves too: its costumes don't go in (SkinImport.ChangesMoves)
            string moves = costumes.Count > 0 ? SkinImport.ChangesMoves(files.Select(f => f.Key)) : null;
            if (moves != null && stages.Count == 0 && colors.Count == 0 && music.Count == 0 && effects.Count == 0)
                throw new InvalidDataException(moves + ", for every costume (online that's a desync), and its model may need them: it can't go in as a costume");
            // its picture: the mod page's, else the first picture in it that isn't a costume's or stage's own
            if (picture == null)
                picture = files.Where(f => Regex.IsMatch(f.Key, @"\.(png|jpe?g)$", RegexOptions.IgnoreCase) && !CostumePicture.IsMatch(NameOf(f.Key))).Select(f => f.Value).FirstOrDefault();
            string clean = Regex.Replace(name, @"[^\w\s\-\.\(\)]", "").Trim();
            if (clean.Length > 60) clean = clean.Substring(0, 60).Trim();
            string credit = (!string.IsNullOrEmpty(author) ? " by " + author : "") + where;
            var result = new Result();
            if (moves != null)
            {
                result.Problems.Add(string.Join(", ", costumes.Select(c => Fighters.Name(c.Fighter)).Distinct()) + ": " + moves + ", so its costume can't go in");
                costumes.Clear();
            }
            foreach (var costume in costumes)
            {
                if (dock != null)
                {
                    string before = BuiltBefore(game, AppFolders.FighterPackages(costume.Fighter), clean + " - " + costume.Fighter + " ", "", it => it.IsCostume && it.Fighter == costume.Fighter);
                    if (before != null) { DockAgain(before, dock, result, Fighters.Name(costume.Fighter) + " " + Installer.Package(before).Slot.ToString("D2")); continue; }
                }
                var records = Installer.Load(game).Concat(Installer.Removed(game)).ToList();
                string itemId = id + ":" + costume.Fighter + costume.Number.ToString("D2");
                Installed again = records.FirstOrDefault(r => r.Item.Id == itemId);
                int slot = again != null ? again.Item.Slot : SkinImport.FreeSlot(game, costume.Fighter);
                if (slot == 0) { result.Problems.Add(SkinImport.NoRoom(costume.Fighter)); continue; }
                string zip = Path.Combine(AppFolders.FighterPackages(costume.Fighter), clean + " - " + costume.Fighter + " " + slot.ToString("D2") + ".zip");
                var item = new Item
                {
                    Id = itemId, Type = "costume", Fighter = costume.Fighter, Slot = slot, Code = "", Name = name, Author = author ?? "", Version = version,
                    Description = name + credit + ": " + costume.Describe + (costume.Versions > 1 ? ", its " + costume.Versions + " versions as colors" : ""),
                    Picture = "", Download = "", Sha256 = "",
                };
                say("Putting " + name + " in as " + Fighters.Name(costume.Fighter) + " " + slot.ToString("D2") + "...");
                string seat = Fighters.Name(costume.Fighter) + " " + slot.ToString("D2");
                // built beside it first: an older version's zip stays as it is if this one isn't wanted
                string fresh = zip.Substring(0, zip.Length - 4) + " (new).zip", said = null;
                try
                {
                    SkinImport.Build(game, costume, slot, picture, fresh);
                    string warning = null;
                    if (check) InstallCheck.Decide(game, fresh, true, out said, out warning);   // fixed in EX More Stuff's own zip
                    File.Copy(fresh, zip, true);
                    if (dock != null) dock(zip);
                    else
                    {
                        Installer.InstallPackage(game, item, zip, "file");
                        InstallCheck.SetWarning(item.Key, warning);
                        if (warning != null)
                        {
                            Installer.Remove(game, item.Key);   // it can't be fixed: switched off and locked
                            result.Problems.Add(seat + " is switched off: it has problems that can't be fixed (see " + InstallCheck.ListButton + ")");
                            continue;
                        }
                    }
                    result.Into.Add(seat + (said != null ? " (" + said + ")" : ""));
                }
                catch (Exception ex) { result.Problems.Add(Fighters.Name(costume.Fighter) + ": " + ex.Message); }
                finally { try { File.Delete(fresh); } catch (Exception) { } }
            }
            if (dock != null && music.Count > 0) { result.Problems.Add("its music isn't docked (music goes straight into the game): add it without docking for that"); music.Clear(); }
            foreach (var m in music)
            {
                string place = m.Slot.Name + (m.Slot.Round > 1 ? " round " + m.Slot.Round : "");
                say("Putting " + m.Song + " in for " + place + "...");
                try
                {
                    if (MusicBank.GameFile(game, m.Slot.TemplateFile) == null) throw new FileNotFoundException("the game has no music there");
                    MusicBank.Install(game, m.Slot, m.Bank, new InstalledSong { Song = m.Song, Layers = new[] { m.Song, m.Song, m.Song } });
                    string round = m.Slot.Round > 1 ? RoundMod.Ensure(game) : null;   // Tom's Round BGM mod comes with round 2/3 music
                    if (round != null) result.Problems.Add(place + ": " + round);
                    result.Into.Add(place + " music");
                }
                catch (Exception ex) { result.Problems.Add(place + ": " + ex.Message); }
            }
            // new colors: each its own number, unless another package has that one (then the first free from 30); a mod's
            // colors of one costume in one zip, one install (user 2026-10-18: "one install hold all the colors"), each color
            // still switched on and off by itself
            foreach (var costume in colors.GroupBy(c => c.Fighter + "_" + c.Costume.ToString("D2")))
            {
                var records = Installer.Load(game).Concat(Installer.Removed(game)).ToList();
                var numbers = new List<int>();
                var made = new List<Item>();
                var package = new List<KeyValuePair<string, byte[]>>();
                var before = new List<string>();   // the zips its colors had before (a zip each, older versions)
                foreach (var color in costume)
                {
                    string itemId = id + ":" + color.Fighter + color.Costume.ToString("D2") + "_" + color.Number.ToString("D2");
                    Installed again = records.FirstOrDefault(r => r.Item.Id == itemId);
                    int number = again != null ? again.Item.Color : FreeColor(game, color.Fighter, color.Costume, color.Number, numbers);
                    if (number == 0) { result.Problems.Add(color.Describe + ": colors " + Catalog.FirstColor(color.Costume) + "-" + Catalog.LastColor + " are all in use"); continue; }
                    byte[] emm = color.Emm ?? CostumeFile(game, color.Fighter, color.Fighter + "_" + color.Costume.ToString("D2") + "_01.obj.emm");
                    if (emm == null) { result.Problems.Add(color.Describe + ": it has no material (.obj.emm) and the costume's color 1 has none to lend"); continue; }
                    numbers.Add(number);
                    var item = new Item
                    {
                        Id = itemId, Type = "color", Fighter = color.Fighter, Slot = color.Costume, Color = number, Code = "", Name = name, Author = author ?? "", Version = version,
                        Description = name + credit + ": a new color", Picture = "", Download = "", Sha256 = "",
                    };
                    made.Add(item);
                    package.Add(new KeyValuePair<string, byte[]>(item.Prefix + ".col.emb", color.Col));
                    package.Add(new KeyValuePair<string, byte[]>(item.Prefix + ".obj.emm", emm));
                    byte[] png = color.Png ?? Png(picture);
                    if (png != null) package.Add(new KeyValuePair<string, byte[]>(item.Prefix + ".png", png));
                    if (again != null && !string.IsNullOrEmpty(again.Package)) before.Add(again.Package);
                }
                if (made.Count == 0) continue;
                Item one = made[0];
                string zip = Path.Combine(AppFolders.PackagesFor(one), clean + " - " + (made.Count == 1 ? one.Prefix : costume.Key + " colors") + ".zip");
                string into = Fighters.Name(one.Fighter) + " " + (made.Count == 1 ? one.TitleNamed(null).ToLowerInvariant()
                              : (one.Slot < Catalog.FirstCustomCostume ? "costume " : "slot ") + one.Slot + " colors " + string.Join(", ", made.Select(i => i.Color)));
                say("Putting " + name + " in as " + into + "...");
                try
                {
                    Write(package, zip);
                    if (dock != null) dock(zip);
                    else foreach (Item item in made) Installer.InstallPackage(game, item, zip, "file");
                    result.Into.Add(into);
                }
                catch (Exception ex) { result.Problems.Add(Fighters.Name(one.Fighter) + " " + costume.Key + " colors: " + ex.Message); continue; }
                // an older version's zips, now nothing uses them: to the Recycle Bin
                var now = Installer.Load(game).Concat(Installer.Removed(game)).Select(r => r.Package).Where(p => p != null).Concat(Docked.Load().Select(r => r.Package)).ToList();
                foreach (string old in before.Distinct(StringComparer.OrdinalIgnoreCase))
                    if (!string.Equals(Path.GetFullPath(old), Path.GetFullPath(zip), StringComparison.OrdinalIgnoreCase) && File.Exists(old) && AppFolders.Inside(old, AppFolders.Mods) &&
                        !now.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(old), StringComparison.OrdinalIgnoreCase)))
                        try { PackagesPanel.RecycleZip(old); } catch (Exception) { }
            }
            // code_<CODE>.txt: the stage's own code here, its files under a game stage's code for players without EX More Stuff
            string ownCode = stages.Count == 1 ? Overrides.OwnCode(files.Select(f => f.Key)) : null;
            foreach (var stage in stages)
            {
                if (dock != null)
                {
                    string before = BuiltBefore(game, AppFolders.StagePackages, clean + " - ", stages.Count > 1 ? " " + stage.Code : "", it => it.IsStage);
                    if (before != null) { DockAgain(before, dock, result, "stage " + Installer.Package(before).Code); continue; }
                }
                var records = Installer.Load(game).Concat(Installer.Removed(game)).ToList();
                string itemId = id + ":STG_" + stage.Code + (stage.Folder != "" ? ":" + stage.Folder : "");
                Installed again = records.FirstOrDefault(r => r.Item.Id == itemId);
                // its shared code (a GameBanana stage's), else a personal one
                string shared = sharedStage != null ? sharedStage(ownCode ?? stage.Code) : null;
                // in under a personal code from before its own could be shared (A## and T## codes, before 2026-10-15):
                // it moves to its own (taken out under the old one, its old zip to the Recycle Bin)
                if (again != null && shared != null && again.Item.Code != shared && dock == null)
                {
                    Installer.Purge(game, again.Item.Key);
                    if (!again.Theirs && !string.IsNullOrEmpty(again.Package) && File.Exists(again.Package)) PackagesPanel.RecycleZip(again.Package);
                    again = null;
                }
                string code = again != null ? again.Item.Code : shared ?? FreeStageCode(game);
                Installed other = shared == null ? null : records.FirstOrDefault(r => r.Item.IsStage && r.Item.Code == code && r.Item.Id != itemId);
                if (other != null) { result.Problems.Add(stage.Describe + ": your " + other.Item.TitleNamed(other.Shown ?? other.Item.Name) + " is in stage " + code + ", the code this stage shares with everyone; give yours another code in Advanced > Package codes first"); continue; }
                if (shared != null) result.Shared = true;
                if (code == null) { result.Problems.Add(stage.Describe + ": all personal stage codes (U01-U99, 0U0-9U9, 00U-99U) are in use"); continue; }
                string title = stages.Count > 1 ? name + " - " + stage.Describe : name;
                string zip = Path.Combine(AppFolders.StagePackages, clean + " - " + code + (stages.Count > 1 ? " " + stage.Code : "") + ".zip");
                var item = new Item
                {
                    Id = itemId, Type = "stage", Fighter = "", Slot = 0, Code = code, Name = title, Author = author ?? "", Version = version,
                    Description = title + credit + ": made from " + stage.Describe + (stage.Music.Count > 0 ? ", with its music" : ""),
                    Picture = "", Download = "", Sha256 = "",
                };
                say("Putting " + title + " in as stage " + code + "...");
                try
                {
                    BuildStage(game, stage, code, picture, zip);
                    if (dock != null) dock(zip); else Installer.InstallPackage(game, item, zip, "file");
                    result.Into.Add("stage " + code);
                }
                catch (Exception ex)
                {
                    result.Problems.Add(stage.Describe + ": " + ex.Message);
                    if (again == null) try { File.Delete(zip); } catch (Exception) { }
                }
            }
            // a fighter's effects: their own entry, on right away (not docked: they go straight in)
            if (effects.Count > 0)
            {
                if (dock != null) result.Problems.Add("its effects can't be docked: add it without docking for them");
                else
                    try { foreach (string fighter in Effects.Install(game, effects, id, name, author, version, picture)) result.Into.Add(Fighters.Name(fighter) + "'s effects"); }
                    catch (Exception ex) { result.Problems.Add("its effects: " + ex.Message); }
            }
            return result;
        }
    }
}
