using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // What EX More Stuff installed, and the files of each (paths under the game's patch folder). Kept in the game
    // folder, so the program can be moved or downloaded again and still knows. Only these files are ever removed.
    class Installed
    {
        public Item Item;
        public string Source;               // "catalog", or "file" for a player's own package
        public string Package;              // a player's own package: the zip it came from, to switch it back on
        public string Shown;                // its name in EMBER's menus (the player can rename it), in <prefix>.txt; null: never set
        public List<string> Files = new List<string>();
    }

    static class Installer
    {
        const string ListName = "ex_more_stuff.json";

        static string ListPath(string game) { return Path.Combine(Game.PatchFolder(game), ListName); }

        // What is installed, and the player's own packages that were switched off ("removed": no files, kept listed).
        public static List<Installed> Load(string game) { return Read(game, "installed"); }
        public static List<Installed> Removed(string game) { return Read(game, "removed"); }

        static List<Installed> Read(string game, string name)
        {
            var result = new List<Installed>();
            string path = ListPath(game);
            if (!File.Exists(path)) return result;
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            object list;
            if (root != null && root.TryGetValue(name, out list) && list is System.Collections.ArrayList)
                foreach (object entry in (System.Collections.ArrayList)list)
                {
                    var j = entry as Dictionary<string, object>;
                    if (j == null) continue;
                    var record = new Installed { Item = Item.FromJson(j), Source = Convert.ToString(j["source"]) };
                    if (j.ContainsKey("package") && j["package"] != null) record.Package = Convert.ToString(j["package"]);
                    if (j.ContainsKey("shown") && j["shown"] != null) record.Shown = Convert.ToString(j["shown"]);
                    if (j.ContainsKey("files") && j["files"] is System.Collections.ArrayList)
                        foreach (object f in (System.Collections.ArrayList)j["files"]) record.Files.Add(Convert.ToString(f));
                    result.Add(record);
                }
            return result;
        }

        static void Save(string game, List<Installed> records) { Save(game, records, Removed(game)); }

        static void Save(string game, List<Installed> records, List<Installed> removed)
        {
            Func<Installed, Dictionary<string, object>> entry = r =>
            {
                var j = r.Item.ToJson();
                j["source"] = r.Source;
                j["package"] = r.Package;
                j["shown"] = r.Shown;
                j["files"] = r.Files;
                return j;
            };
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Directory.CreateDirectory(Game.PatchFolder(game));
            File.WriteAllText(ListPath(game), json.Serialize(new Dictionary<string, object>
            {
                { "format", 1 }, { "installed", records.Select(entry).ToList() }, { "removed", removed.Select(entry).ToList() },
            }));
        }

        // A stage's own music (optional): BGM_<code>.csb, a CRI sound bank laid out like the game's stage themes (3
        // cues), and for rounds 2 and 3 (Tom's Round BGM mod, which EX More Stuff puts in with them) BGM_<code>2.csb
        // and BGM_<code>3.csb.
        static bool IsMusic(Item item, string name)
        {
            return item.IsStage && Regex.IsMatch(name, "^BGM_" + Regex.Escape(item.Code) + @"[23]?\.csb$", RegexOptions.IgnoreCase);
        }

        // An item's name in EMBER's menus: the first line of <prefix>.txt beside it (JHA_71.txt, STG_C71.txt), at most
        // 40 characters; without one EMBER says "Custom 71" or "Custom stage C71". Only English letters, digits, spaces
        // and _ - . (every font EMBER has draws them); anything else is dropped.
        public const int NameBytes = 40;

        public static bool NameCharacter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-' || c == '.';
        }

        public static string CleanName(string name)
        {
            string clean = Regex.Replace(new string((name ?? "").Where(NameCharacter).ToArray()), " {2,}", " ").Trim();
            return clean.Length > NameBytes ? clean.Substring(0, NameBytes).TrimEnd() : clean;
        }

        // Writes (or, for an empty name, removes) the record's name file.
        static void WriteName(string game, Installed record)
        {
            string path = Path.Combine(Game.FolderFor(game, record.Item), record.Item.Prefix + ".txt");
            string relative = path.Substring(Game.PatchFolder(game).Length + 1);
            record.Files.RemoveAll(f => string.Equals(f, relative, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(record.Shown)) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, record.Shown, new UTF8Encoding(false));
            record.Files.Add(relative);
        }

        // Renames an installed item in EMBER's menus (from its next start). The file isn't the game's, so the game may run.
        public static void Rename(string game, string key, string name)
        {
            var records = Load(game);
            Installed record = records.FirstOrDefault(r => r.Item.Key == key);
            if (record == null) return;
            record.Shown = CleanName(name);
            WriteName(game, record);
            Save(game, records);
        }

        // Where a package's file goes: a stage's music beside the game's music, everything else in the item's folder.
        static string FolderFor(string game, Item item, string name)
        {
            return IsMusic(item, name)
                ? Path.Combine(Game.PatchFolder(game), "battle", "sound", "bgm")
                : Game.FolderFor(game, item);
        }

        // The files a package may hold: only its own names, so nothing of the game's can be touched.
        static Regex AllowedNames(Item item)
        {
            string p = Regex.Escape(item.Prefix);
            if (item.IsColor) return new Regex("^" + p + @"\.(col\.emb|obj\.emm|png)$", RegexOptions.IgnoreCase);
            return item.IsStage
                ? new Regex("^(" + p + @"\.(emz|tex\.emz|png|jpg)|BGM_" + Regex.Escape(item.Code) + @"[23]?\.csb)$", RegexOptions.IgnoreCase)
                : new Regex("^" + p + @"(\.(obj\.emo|shd\.emo|nml\.emb|bsr|csb|png)|_(0[1-9]|10)\.(col\.emb|obj\.emm|png))$", RegexOptions.IgnoreCase);
        }

        // Why a package can't be installed as `item`, or null.
        static string CheckPackage(ZipArchive zip, Item item)
        {
            Regex allowed = AllowedNames(item);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                if (e.FullName.EndsWith("/")) continue;                      // folder entries carry nothing
                if (e.FullName != e.Name) return "files must not be in folders (" + e.FullName + ")";
                if (!allowed.IsMatch(e.Name)) return "unexpected file " + e.Name + " (a " + (item.IsStage ? "stage" : item.IsColor ? "color" : "costume") + " here holds only " + item.Prefix + " files)";
                names.Add(e.Name);
            }
            if (item.IsStage)
            {
                if (!names.Contains(item.Prefix + ".emz")) return "missing " + item.Prefix + ".emz";
                // Players without a custom stage play its fallback, so its scripts must leave the fighters alone.
                ZipArchiveEntry main = zip.Entries.First(e => e.Name.Equals(item.Prefix + ".emz", StringComparison.OrdinalIgnoreCase));
                using (var data = new MemoryStream())
                {
                    using (Stream s = main.Open()) s.CopyTo(data);
                    var calls = StagePack.GameplayCallsIn(data.ToArray());
                    if (calls.Count > 0)
                        return "its scripts change where the fighters stand (" + string.Join(", ", calls) + "), so players without it would play a different match online";
                }
            }
            else if (item.IsColor)
            {
                foreach (string need in new[] { ".col.emb", ".obj.emm" })
                    if (!names.Contains(item.Prefix + need)) return "missing " + item.Prefix + need;
            }
            else
            {
                foreach (string need in new[] { ".obj.emo", ".nml.emb" })
                    if (!names.Contains(item.Prefix + need)) return "missing " + item.Prefix + need;
                for (int c = 1; c <= 10; c++)
                    foreach (string need in new[] { ".col.emb", ".obj.emm" })
                    {
                        string name = item.Prefix + "_" + c.ToString("D2") + need;
                        if (!names.Contains(name)) return "missing " + name + " (a costume has 10 colors)";
                    }
            }
            return null;
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        // A catalog item installed in an older version: the catalog's package has changed since.
        public static bool Outdated(Installed record, Item latest)
        {
            if (record.Source != "catalog" || record.Item.Key != latest.Key) return false;
            if (!string.IsNullOrEmpty(latest.Sha256) && !string.IsNullOrEmpty(record.Item.Sha256)) return latest.Sha256 != record.Item.Sha256;
            return latest.Version != record.Item.Version;
        }

        // Downloads a catalog item, checks it, installs it (over an older version of it).
        public static async Task Install(string game, Item item, IProgress<long> progress)
        {
            string temp = Path.Combine(Game.PatchFolder(game), "ex_more_stuff.download");
            try
            {
                await Catalog.Fetch(item.Download, temp, progress);
                if (item.Size > 0 && new FileInfo(temp).Length != item.Size)
                    throw new InvalidDataException("the download is incomplete (" + new FileInfo(temp).Length + " of " + item.Size + " bytes)");
                if (!string.IsNullOrEmpty(item.Sha256) && Sha256(temp) != item.Sha256)
                    throw new InvalidDataException("the download doesn't match the catalog's fingerprint");
                InstallPackage(game, item, temp, "catalog");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        public static void InstallPackage(string game, Item item, string zipPath, string source)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var records = Load(game);
            var ours = new HashSet<string>(records.SelectMany(r => r.Files), StringComparer.OrdinalIgnoreCase);
            string patch = Game.PatchFolder(game);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                string problem = CheckPackage(zip, item);
                if (problem != null) throw new InvalidDataException(problem);
                var files = zip.Entries.Where(e => !e.FullName.EndsWith("/")).ToList();
                // Never write over a file EX More Stuff didn't put there.
                foreach (ZipArchiveEntry e in files)
                {
                    string target = Path.Combine(FolderFor(game, item, e.Name), e.Name);
                    string relative = target.Substring(patch.Length + 1);
                    if (File.Exists(target) && !ours.Contains(relative))
                        throw new IOException(e.Name + (IsSong(game, e.Name)
                            ? " is a song put in on the Music page; give the game's music back there first"
                            : " is already in the game folder and wasn't installed by EX More Stuff; it was left alone"));
                }
                // Replacing an older version of the same item (or switching a removed one back on): its files go
                // first; a name the player gave it stays.
                Installed old = records.FirstOrDefault(r => r.Item.Key == item.Key);
                if (old != null) { Delete(game, old); records.Remove(old); }
                var removed = Removed(game);
                Installed shelved = removed.FirstOrDefault(r => r.Item.Key == item.Key);
                if (shelved != null) removed.Remove(shelved);
                string shown = old != null && old.Shown != null ? old.Shown : shelved != null && shelved.Shown != null ? shelved.Shown : CleanName(item.Name);
                var record = new Installed { Item = item, Source = source, Package = source == "file" ? Path.GetFullPath(zipPath) : null, Shown = shown };
                foreach (ZipArchiveEntry e in files)
                {
                    string target = Path.Combine(FolderFor(game, item, e.Name), e.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    record.Files.Add(target.Substring(patch.Length + 1));
                }
                WriteName(game, record);
                records.Add(record);
                Save(game, records, removed);
            }
        }

        static bool IsSong(string game, string file)
        {
            try { return MusicBank.Load(game).ContainsKey(file); }
            catch (InvalidDataException) { return false; }
        }

        static void Delete(string game, Installed record)
        {
            foreach (string relative in record.Files)
            {
                string path = Path.Combine(Game.PatchFolder(game), relative);
                if (File.Exists(path)) File.Delete(path);
            }
            // Folders an install creates go too once nothing is left in them: a fighter's (battle\chara\<CHR>) and the
            // music's (battle\sound\bgm, then battle\sound).
            foreach (string folder in record.Files.Select(f => Path.Combine(Game.PatchFolder(game), Path.GetDirectoryName(f))).Distinct())
            {
                string parent = Path.GetDirectoryName(folder);
                if (string.Equals(Path.GetFileName(parent), "chara", StringComparison.OrdinalIgnoreCase)) RemoveIfEmpty(folder);
                else if (string.Equals(folder.Substring(Game.PatchFolder(game).Length + 1), Path.Combine("battle", "sound", "bgm"), StringComparison.OrdinalIgnoreCase)
                         && RemoveIfEmpty(folder)) RemoveIfEmpty(parent);
            }
        }

        static bool RemoveIfEmpty(string folder)
        {
            try
            {
                if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) return false;
                Directory.Delete(folder);
                return true;
            }
            catch (IOException) { return false; }
        }

        public static void Remove(string game, string key)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var records = Load(game);
            Installed record = records.FirstOrDefault(r => r.Item.Key == key);
            if (record == null) return;
            Delete(game, record);
            records.Remove(record);
            // A player's own package stays listed, switched off, so it can come back from its zip (the catalog lists its own).
            var removed = Removed(game);
            removed.RemoveAll(r => r.Item.Key == key);
            if (record.Source == "file") { record.Files.Clear(); removed.Add(record); }
            Save(game, records, removed);
        }

        // A player's own package's picture: among its files (installed, or still in its zip).
        public static string[] PictureNames(Item item) { return new[] { item.Prefix + ".png", item.Prefix + "_01.png", item.Prefix + ".jpg" }; }

        public static byte[] PackagePicture(string zipPath, Item item)
        {
            if (!File.Exists(zipPath)) return null;
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                foreach (string name in PictureNames(item))
                {
                    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;
                    using (Stream s = entry.Open())
                    using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
                }
            return null;
        }

        /// <summary>A player's own package now under `moved` (another stage code or costume slot; its zip already
        /// rewritten): in the game, it's put in again under the new code, then its old files go (its name kept);
        /// switched off, its listing changes.</summary>
        public static void Moved(string game, Installed record, Item moved)
        {
            var records = Load(game);
            if (records.Any(r => r.Item.Key == record.Item.Key))
            {
                InstallPackage(game, moved, record.Package, "file");   // the new files first, so nothing is lost on a failure
                records = Load(game);
                Installed old = records.First(r => r.Item.Key == record.Item.Key);
                Delete(game, old);
                records.Remove(old);
                Save(game, records);
                if (old.Shown != null) Rename(game, moved.Key, old.Shown);
                return;
            }
            var removed = Removed(game);
            Installed off = removed.FirstOrDefault(r => r.Item.Key == record.Item.Key);
            if (off == null) return;
            off.Item = moved;
            Save(game, records, removed);
        }

        /// <summary>A package gone for good (Package codes' Delete): its files out of the game when it's in, and off every
        /// list. Its zip stays where it is (the caller decides).</summary>
        public static void Purge(string game, string key)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var records = Load(game);
            var removed = Removed(game);
            Installed record = records.FirstOrDefault(r => r.Item.Key == key);
            if (record != null) { Delete(game, record); records.Remove(record); }
            removed.RemoveAll(r => r.Item.Key == key);
            Save(game, records, removed);
        }

        /// <summary>Whether a package's zip still holds the item's own files (it may have been overwritten with another
        /// slot's or code's since).</summary>
        public static bool PackageHolds(string zipPath, Item item)
        {
            try
            {
                string main = item.Prefix + (item.IsStage ? ".emz" : item.IsColor ? ".col.emb" : ".obj.emo");
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                    return zip.Entries.Any(e => e.Name.Equals(main, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }
        }

        // Takes a removed package of the player's off the list.
        public static void Forget(string game, string key)
        {
            var removed = Removed(game);
            if (removed.RemoveAll(r => r.Item.Key == key) > 0) Save(game, Load(game), removed);
        }

        // A player's own package (zip): what it is, from its main file's name (KEN_75.obj.emo, STG_C80.emz, STG_D05.emz,
        // RYU_01_30.col.emb: a new color).
        /// <summary>The item a complete EX More Stuff package installs as (a zip: a costume slot 8-99 with all its files, or
        /// a stage code of its own that leaves the fighters alone), or null: not one.</summary>
        public static Item Package(string path)
        {
            try
            {
                Item item = Inspect(path);
                if (item == null || item.Problem() != null) return null;
                using (ZipArchive zip = ZipFile.OpenRead(path))
                    return CheckPackage(zip, item) == null ? item : null;
            }
            catch (Exception) { return null; }   // not a zip, or not readable as one
        }

        public static Item Inspect(string zipPath)
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    Match costume = Regex.Match(e.Name, @"^([A-Z0-9]{3})_(\d\d)\.obj\.emo$", RegexOptions.IgnoreCase);
                    Match stage = Regex.Match(e.Name, @"^STG_([A-Za-z0-9]{3})\.emz$");
                    Match color = Regex.Match(e.Name, @"^([A-Z0-9]{3})_(\d\d)_(\d\d)\.col\.emb$", RegexOptions.IgnoreCase);
                    var item = new Item { Id = "file:" + Path.GetFileNameWithoutExtension(zipPath), Name = Path.GetFileNameWithoutExtension(zipPath), Author = "", Version = "" };
                    if (stage.Success && Stages.IsCustomCode(stage.Groups[1].Value.ToUpperInvariant()))
                    {
                        item.Type = "stage"; item.Fighter = ""; item.Code = stage.Groups[1].Value.ToUpperInvariant();
                        return item;
                    }
                    if (color.Success && Fighters.IsCode(color.Groups[1].Value.ToUpperInvariant()) && int.Parse(color.Groups[3].Value) >= Catalog.FirstCustomColor)
                    {
                        item.Type = "color"; item.Fighter = color.Groups[1].Value.ToUpperInvariant();
                        item.Slot = int.Parse(color.Groups[2].Value); item.Color = int.Parse(color.Groups[3].Value);
                        return item;
                    }
                    if (costume.Success && Fighters.IsCode(costume.Groups[1].Value.ToUpperInvariant()))
                    {
                        item.Type = "costume"; item.Fighter = costume.Groups[1].Value.ToUpperInvariant(); item.Slot = int.Parse(costume.Groups[2].Value);
                        return item;
                    }
                }
            return null;
        }
    }
}
