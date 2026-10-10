using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // One of a fighter's game costumes played as another on this PC (the Costumes page, like Replacements for stages):
    // the other costume's files are copied in under this costume's names (RYU_05.obj.emo -> RYU_02.obj.emo ...) in the
    // patch folder. The other one may be another game costume (a DLC one too) or a custom costume EX More Stuff
    // installed. Files already there are moved to ex_more_stuff_backup and come back when it's put back. Only this PC
    // sees it, online too: the game still plays costume 2, so nothing else changes.
    class CostumeSwap
    {
        public string Fighter;
        public int Costume, Source;
        public Dictionary<string, string> Files = new Dictionary<string, string>();   // written file -> its SHA-256
        public List<string> Backups = new List<string>();                             // patch files moved aside
        public string Key { get { return CostumeSwaps.Key(Fighter, Costume); } }
    }

    static class CostumeSwaps
    {
        const string ListName = "ex_more_stuff_costumes.json", BackupFolder = "ex_more_stuff_backup";
        // the game's own costume files (never patch_ae2_tu3: the player's mods and these swaps are there)
        static readonly string[] GameRoots = { "patch_ae2_tu2", "patch_ae2", @"dlc\04_costume", @"dlc\02_costume", @"dlc\04_ae2", @"dlc\03_character", @"dlc\03_character_free", "resource" };
        static readonly string[] Parts = { ".obj.emo", ".shd.emo", ".nml.emb", ".bsr", ".csb" };

        public static string Key(string fighter, int costume) { return fighter + ":" + costume; }

        public static bool ParseKey(string key, out string fighter, out int costume)
        {
            fighter = null; costume = 0;
            var parts = (key ?? "").Split(':');
            return parts.Length == 2 && int.TryParse(parts[1], out costume) && Fighters.IsCode(fighter = parts[0]);
        }

        static string ListPath(string game) { return Path.Combine(Game.PatchFolder(game), ListName); }
        static string Chara(string game, string fighter) { return Path.Combine(Game.PatchFolder(game), "battle", "chara", fighter); }

        public static List<CostumeSwap> Load(string game)
        {
            var result = new List<CostumeSwap>();
            if (!File.Exists(ListPath(game))) return result;
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(ListPath(game)));
            object list;
            if (root == null || !root.TryGetValue("swaps", out list) || !(list is System.Collections.ArrayList)) return result;
            foreach (var j in ((System.Collections.ArrayList)list).OfType<Dictionary<string, object>>())
            {
                var s = new CostumeSwap { Fighter = Convert.ToString(j["fighter"]), Costume = Convert.ToInt32(j["costume"]), Source = Convert.ToInt32(j["source"]) };
                var files = j.ContainsKey("files") ? j["files"] as Dictionary<string, object> : null;
                if (files != null) foreach (var f in files) s.Files[f.Key] = Convert.ToString(f.Value);
                var backups = j.ContainsKey("backups") ? j["backups"] as System.Collections.ArrayList : null;
                if (backups != null) foreach (object b in backups) s.Backups.Add(Convert.ToString(b));
                result.Add(s);
            }
            return result;
        }

        static void Save(string game, List<CostumeSwap> list)
        {
            if (list.Count == 0) { if (File.Exists(ListPath(game))) File.Delete(ListPath(game)); return; }
            var entries = list.Select(s => new Dictionary<string, object>
            {
                { "fighter", s.Fighter }, { "costume", s.Costume }, { "source", s.Source }, { "files", s.Files }, { "backups", s.Backups },
            }).ToList();
            File.WriteAllText(ListPath(game), new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "format", 1 }, { "swaps", entries } }));
        }

        /// <summary>A costume's files by what follows its prefix (".obj.emo", "_01.col.emb" ...): a game costume's (1-7)
        /// from the game's own folders, a custom costume's (8-99) as installed in the patch folder.</summary>
        public static Dictionary<string, string> CostumeFiles(string game, string fighter, int costume)
        {
            string prefix = fighter + "_" + costume.ToString("D2");
            var pattern = new Regex("^" + Regex.Escape(prefix) + @"(\.(obj\.emo|shd\.emo|nml\.emb|bsr|csb)|_\d\d\.(col\.emb|obj\.emm))$", RegexOptions.IgnoreCase);
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var folders = costume < Catalog.FirstCustomCostume
                ? GameRoots.Select(r => Path.Combine(game, r, "battle", "chara", fighter))
                : new[] { Chara(game, fighter) };
            foreach (string folder in folders.Where(Directory.Exists))
                foreach (string path in Directory.GetFiles(folder, prefix + "*"))
                {
                    string name = Path.GetFileName(path);
                    if (!pattern.IsMatch(name)) continue;
                    string rest = name.Substring(prefix.Length);
                    if (!found.ContainsKey(rest)) found[rest] = path;
                }
            return found;
        }

        static List<int> Colors(Dictionary<string, string> files)
        {
            return files.Keys.Where(k => k.EndsWith(".col.emb", StringComparison.OrdinalIgnoreCase))
                .Select(k => int.Parse(k.Substring(1, 2))).Where(c => files.ContainsKey("_" + c.ToString("D2") + ".obj.emm")).OrderBy(c => c).ToList();
        }

        /// <summary>The fighter's game costumes in this game (1-7, those whose model is there: DLC ones only when installed).</summary>
        public static List<int> GameCostumes(string game, string fighter)
        {
            var result = new List<int>();
            for (int n = 1; n < Catalog.FirstCustomCostume; n++)
                if (GameRoots.Any(r => File.Exists(Path.Combine(game, r, "battle", "chara", fighter, fighter + "_" + n.ToString("D2") + ".obj.emo")))) result.Add(n);
            return result;
        }

        /// <summary>Whether `source` may stand in for another costume: only the Original costume (1) or a custom one
        /// installed. The alternates and DLC costumes cost money, so none of them ever shows in another's place (owning
        /// a cheap one would show an expensive one); a costume from the player's own files or GameBanana is fair game.</summary>
        public static bool MayStandIn(int source) { return source == 1 || source >= Catalog.FirstCustomCostume; }

        /// <summary>Why costume `costume` can't play as `source` (or null).</summary>
        public static string Problem(string game, string fighter, int costume, int source)
        {
            if (costume >= Catalog.FirstCustomCostume || !GameCostumes(game, fighter).Contains(costume)) return "costume " + costume + " isn't one of the game's here";
            if (source == costume) return "that's itself";
            if (!MayStandIn(source)) return "only the Original costume or a costume you installed can stand in for another (the game's alternates and DLC costumes can't)";
            var from = CostumeFiles(game, fighter, source);
            if (!from.ContainsKey(".obj.emo") || !from.ContainsKey(".nml.emb")) return "costume " + source + "'s files weren't found";
            if (Colors(from).Count == 0) return "costume " + source + " has no colors";
            return null;
        }

        /// <summary>Costume `costume` plays as `source`: the files the game loads for it are `source`'s; colors it has that
        /// `source` doesn't come from `source`'s in turn (a 10-color costume on a 20-color one: 13 is 1, 14 is 2 ...).</summary>
        public static void Replace(string game, string fighter, int costume, int source)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            string problem = Problem(game, fighter, costume, source);
            if (problem != null) throw new InvalidOperationException(problem);
            var list = Load(game);
            if (list.Any(s => s.Fighter == fighter && s.Costume == costume)) throw new InvalidOperationException("it is already replaced");
            var from = CostumeFiles(game, fighter, source);
            var own = CostumeFiles(game, fighter, costume);
            var colors = Colors(from);
            string prefix = fighter + "_" + costume.ToString("D2");
            var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // file name -> where it comes from
            foreach (string part in Parts) if (from.ContainsKey(part)) writes[prefix + part] = from[part];
            var wanted = Colors(own).Union(colors).OrderBy(c => c).ToList();
            var ownColors = Colors(own);
            for (int i = 0; i < wanted.Count; i++)
            {
                int c = wanted[i];
                if (ownColors.Count > 0 && !ownColors.Contains(c)) continue;   // colors the costume doesn't have can't be picked
                int take = colors.Contains(c) ? c : colors[ownColors.IndexOf(c) % colors.Count];
                foreach (string kind in new[] { ".col.emb", ".obj.emm" })
                    writes[prefix + "_" + c.ToString("D2") + kind] = from["_" + take.ToString("D2") + kind];
            }
            var record = new CostumeSwap { Fighter = fighter, Costume = costume, Source = source };
            string folder = Chara(game, fighter), patch = Game.PatchFolder(game);
            try
            {
                Directory.CreateDirectory(folder);
                foreach (var w in writes)
                {
                    string path = Path.Combine(folder, w.Key), relative = path.Substring(patch.Length + 1), backup = Path.Combine(patch, BackupFolder, relative);
                    byte[] data = File.ReadAllBytes(w.Value);   // read first: a custom source is in this same folder
                    if (File.Exists(path))
                    {
                        if (File.Exists(backup)) throw new IOException("an older copy of " + w.Key + " is already in " + BackupFolder);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        File.Move(path, backup);
                        record.Backups.Add(relative);
                    }
                    File.WriteAllBytes(path, data);
                    record.Files[relative] = Installer.Sha256(path);
                }
            }
            catch (Exception)
            {
                Undo(game, record);
                throw;
            }
            list.Add(record);
            Save(game, list);
        }

        /// <summary>Puts the costume back: its copied files go (unless something else has changed them since), the moved
        /// files come back. Returns what couldn't be put back, or null.</summary>
        public static string Restore(string game, string fighter, int costume)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var list = Load(game);
            CostumeSwap record = list.FirstOrDefault(s => s.Fighter == fighter && s.Costume == costume);
            if (record == null) return null;
            string problem = Undo(game, record);
            list.Remove(record);
            Save(game, list);
            return problem;
        }

        static string Undo(string game, CostumeSwap record)
        {
            string patch = Game.PatchFolder(game);
            var kept = new List<string>();
            foreach (var file in record.Files)
            {
                string path = Path.Combine(patch, file.Key);
                if (!File.Exists(path)) continue;
                if (Installer.Sha256(path) == file.Value) File.Delete(path);
                else kept.Add(Path.GetFileName(file.Key));   // changed since: someone else's file now, left alone
            }
            foreach (string relative in record.Backups)
            {
                string path = Path.Combine(patch, relative), backup = Path.Combine(patch, BackupFolder, relative);
                if (File.Exists(backup) && !File.Exists(path)) File.Move(backup, path);
            }
            string backups = Path.Combine(patch, BackupFolder);
            try
            {
                foreach (string dir in Directory.Exists(backups) ? Directory.GetDirectories(backups, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToArray() : new string[0])
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                if (Directory.Exists(backups) && !Directory.EnumerateFileSystemEntries(backups).Any()) Directory.Delete(backups);
            }
            catch (IOException) { }
            return kept.Count == 0 ? null : string.Join(", ", kept) + " changed since and stayed";
        }
    }
}
