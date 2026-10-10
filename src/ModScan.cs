using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // Mods put in the game by hand (Advanced > Old mods): files in the game's patch folders that aren't the game's
    // official update files (res\official.json: each one's path and size, made by test\OfficialManifest.cs from a clean
    // install) and weren't put there by EX More Stuff. Grouped by what they are. A stage or a costume can be brought in
    // (Import): it becomes a package of the player's in one of their own seats, and its loose files leave the game folder.
    static class ModScan
    {
        public static readonly string[] PatchFolders = { "patch", "patch_ae2", "patch_ae2_tu1", "patch_ae2_tu1b", "patch_ae2_tu2", "patch_ae2_tu3" };

        public enum Kind { Stage, Costume, Other }

        public sealed class Found
        {
            public Kind Kind;
            public string Key, What, Code, Fighter;   // Code: a stage's; Fighter: a costume's
            public int Costume;
            public readonly List<string> Files = new List<string>();   // paths in the game folder
            public DateTime Newest;
            public bool CanImport { get { return Kind != Kind.Other; } }
        }

        static Dictionary<string, long> official;

        /// <summary>For tests: the official files as given (path in the game folder -> size).</summary>
        public static void UseOfficial(Dictionary<string, long> files) { official = new Dictionary<string, long>(files, StringComparer.OrdinalIgnoreCase); }

        static Dictionary<string, long> Official()
        {
            if (official != null) return official;
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            using (Stream s = typeof(ModScan).Assembly.GetManifestResourceStream("official.json"))
                if (s != null)
                    using (var r = new StreamReader(s))
                    {
                        var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(r.ReadToEnd());
                        foreach (var f in ((System.Collections.ArrayList)root["files"]).OfType<Dictionary<string, object>>())
                            result[Convert.ToString(f["path"])] = Convert.ToInt64(f["size"]);
                    }
            return official = result;
        }

        // what EX More Stuff put in the patch folder itself: packages, songs, replaced stages and costumes
        static HashSet<string> Ours(string game)
        {
            var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<string, string> inPatch = f => Path.Combine("patch_ae2_tu3", f);
            foreach (Installed r in Installer.Load(game)) foreach (string f in r.Files) ours.Add(inPatch(f));
            try { foreach (string f in MusicBank.Load(game).Keys) ours.Add(inPatch(Path.Combine("battle", "sound", "bgm", f))); } catch (Exception) { }
            foreach (Replacement r in Replacements.Load(game)) foreach (string f in r.Files.Keys) ours.Add(inPatch(f));
            foreach (CostumeSwap s in CostumeSwaps.Load(game)) foreach (string f in s.Files.Keys) ours.Add(inPatch(f));
            return ours;
        }

        static readonly Regex StageFile = new Regex(@"^STG_([A-Za-z0-9]{3})(\.tex)?\.emz$", RegexOptions.IgnoreCase);
        static readonly Regex CostumeFile = new Regex(@"^([A-Za-z0-9]{3})_(\d\d)(\.|_)", RegexOptions.IgnoreCase);

        /// <summary>The mods in the game's patch folders, grouped by what they are.</summary>
        public static List<Found> Scan(string game)
        {
            var ours = Ours(game);
            var known = Official();
            var groups = new Dictionary<string, Found>();
            foreach (string folder in PatchFolders)
            {
                string root = Path.Combine(game, folder);
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string inside = path.Substring(root.Length + 1), relative = Path.Combine(folder, inside);
                    if (inside.StartsWith("ex_more_stuff", StringComparison.OrdinalIgnoreCase) || ours.Contains(relative)) continue;
                    long size;
                    var info = new FileInfo(path);
                    if (known.TryGetValue(relative, out size) && size == info.Length) continue;   // the game's own
                    Found found = Describe(inside);
                    Found group;
                    if (!groups.TryGetValue(found.Key, out group)) groups[found.Key] = group = found;
                    group.Files.Add(relative);
                    if (info.LastWriteTime > group.Newest) group.Newest = info.LastWriteTime;
                }
            }
            return groups.Values.OrderBy(g => g.Kind).ThenBy(g => g.What, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // what a file in a patch folder is (its path inside the folder)
        static Found Describe(string inside)
        {
            string folder = Path.GetDirectoryName(inside) ?? "", name = Path.GetFileName(inside);
            string[] parts = folder.Split(Path.DirectorySeparatorChar);
            Match m = StageFile.Match(name);
            if (m.Success && folder.Equals(Path.Combine("battle", "stage"), StringComparison.OrdinalIgnoreCase))
            {
                string code = m.Groups[1].Value.ToUpperInvariant();
                bool game = Array.IndexOf(Stages.Codes, code) >= 0;
                return new Found { Kind = Kind.Stage, Key = "stage:" + code, Code = code,
                                   What = game ? Stages.Name(code) + " (the game's stage, replaced)" : "Stage " + code + " (put in by hand)" };
            }
            m = CostumeFile.Match(name);
            if (parts.Length == 3 && parts[0].Equals("battle", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("chara", StringComparison.OrdinalIgnoreCase))
            {
                string fighter = parts[2].ToUpperInvariant();
                if (m.Success && Fighters.IsCode(fighter) && m.Groups[1].Value.ToUpperInvariant() == fighter)
                {
                    int costume = int.Parse(m.Groups[2].Value);
                    return new Found { Kind = Kind.Costume, Key = "costume:" + fighter + ":" + costume, Fighter = fighter, Costume = costume,
                                       What = Fighters.Name(fighter) + (costume < Catalog.FirstCustomCostume ? "'s " + CostumeGrid.CostumeName(costume) + " (the game's costume, replaced)"
                                                                                                              : " " + costume.ToString("D2") + " (a costume put in by hand)") };
                }
                if (Fighters.IsCode(fighter)) return new Found { Kind = Kind.Other, Key = "chara:" + fighter, What = Fighters.Name(fighter) + ": other files (moves, effects ...)" };
            }
            if (folder.Equals(Path.Combine("battle", "sound", "bgm"), StringComparison.OrdinalIgnoreCase) && name.EndsWith(".csb", StringComparison.OrdinalIgnoreCase))
            {
                MusicSlot slot = MusicSlot.FromFile(name);
                return new Found { Kind = Kind.Other, Key = "music:" + name.ToUpperInvariant(), What = "Music: " + slot.Name + (slot.Round > 1 ? ", round " + slot.Round : "") };
            }
            return new Found { Kind = Kind.Other, Key = "folder:" + folder.ToLowerInvariant(), What = "Files in " + (folder == "" ? "the folder itself" : folder) };
        }

        /// <summary>Brings a found stage or costume into EX More Stuff: its files made into a package of the player's in one of
        /// their own seats (a stage U01 up, a costume 84 down), its loose files taken out of the game folder by `discard`
        /// (the Recycle Bin), the package installed. `keep`: what it replaced plays as it again (a stage or costume
        /// replacement, switched on the Stages page or the fighter's page; if that can't be done, the import still stands and
        /// the answer says why). Returns where it went ("stage U01", "Abel 84").</summary>
        public static string Import(string game, Found found, bool keep, Action<string> discard)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            if (!found.CanImport) throw new InvalidOperationException(found.What + " can't be brought in");
            var files = found.Files.Select(f => new KeyValuePair<string, byte[]>("old/" + Path.GetFileName(f), File.ReadAllBytes(Path.Combine(game, f)))).ToList();
            string where;
            if (found.Kind == Kind.Stage)
            {
                var stage = ModFile.FindStages(files).FirstOrDefault();
                if (stage == null) throw new InvalidDataException("no stage file in it");
                string code = ModFile.FreeStageCode(game);
                if (code == null) throw new InvalidOperationException("all your own stage codes are in use");
                string name = (Array.IndexOf(Stages.Codes, found.Code) >= 0 ? Stages.Name(found.Code) : "Stage " + found.Code) + " mod";
                Directory.CreateDirectory(AppFolders.StagePackages);
                string zip = AppFolders.PlaceFor(AppFolders.StagePackages, name + " - " + code + ".zip", null);
                ModFile.BuildStage(game, stage, code, null, zip);
                foreach (string f in found.Files) discard(Path.Combine(game, f));
                Installer.InstallPackage(game, new Item { Id = "import:stage:" + found.Code, Type = "stage", Fighter = "", Code = code, Name = name,
                                                          Description = name + ": brought in from the game folder" }, zip, "file");
                where = "stage " + code;
                if (keep && Array.IndexOf(Stages.Codes, found.Code) >= 0) where += KeptShowing(() => Replacements.Replace(game, found.Code, code));
            }
            else
            {
                var costume = SkinImport.Find(files).FirstOrDefault();
                if (costume == null) throw new InvalidDataException("no model or color of its own in it");
                int slot = SkinImport.FreeSlot(game, found.Fighter);
                if (slot == 0) throw new InvalidOperationException(Fighters.Name(found.Fighter) + " has no free slot left");
                string name = (found.Costume < Catalog.FirstCustomCostume ? CostumeGrid.CostumeName(found.Costume) : "Costume " + found.Costume.ToString("D2")) + " mod";
                string folder = AppFolders.FighterPackages(found.Fighter);
                Directory.CreateDirectory(folder);
                string zip = AppFolders.PlaceFor(folder, name + " - " + found.Fighter + " " + slot.ToString("D2") + ".zip", null);
                SkinImport.Build(game, costume, slot, null, zip);
                foreach (string f in found.Files) discard(Path.Combine(game, f));
                Installer.InstallPackage(game, new Item { Id = "import:costume:" + found.Fighter + ":" + found.Costume, Type = "costume", Fighter = found.Fighter, Slot = slot,
                                                          Code = "", Name = name, Description = name + ": brought in from the game folder" }, zip, "file");
                where = Fighters.Name(found.Fighter) + " " + slot.ToString("D2");
                if (keep && found.Costume < Catalog.FirstCustomCostume) where += KeptShowing(() => CostumeSwaps.Replace(game, found.Fighter, found.Costume, slot));
            }
            return where;
        }

        // the replacement that keeps an imported mod showing where it was: "" when made, else why not
        static string KeptShowing(Action replace)
        {
            try { replace(); return ""; }
            catch (Exception ex) { return " (it couldn't keep showing where it was: " + ex.Message + ")"; }
        }
    }
}
