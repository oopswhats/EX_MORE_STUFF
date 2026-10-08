using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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
        public List<string> Files = new List<string>();
    }

    static class Installer
    {
        const string ListName = "ex_more_stuff.json";

        static string ListPath(string game) { return Path.Combine(Game.PatchFolder(game), ListName); }

        public static List<Installed> Load(string game)
        {
            var result = new List<Installed>();
            string path = ListPath(game);
            if (!File.Exists(path)) return result;
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            object list;
            if (root != null && root.TryGetValue("installed", out list) && list is System.Collections.ArrayList)
                foreach (object entry in (System.Collections.ArrayList)list)
                {
                    var j = entry as Dictionary<string, object>;
                    if (j == null) continue;
                    var record = new Installed { Item = Item.FromJson(j), Source = Convert.ToString(j["source"]) };
                    if (j.ContainsKey("files") && j["files"] is System.Collections.ArrayList)
                        foreach (object f in (System.Collections.ArrayList)j["files"]) record.Files.Add(Convert.ToString(f));
                    result.Add(record);
                }
            return result;
        }

        static void Save(string game, List<Installed> records)
        {
            var list = records.Select(r =>
            {
                var j = r.Item.ToJson();
                j["source"] = r.Source;
                j["files"] = r.Files;
                return j;
            }).ToList();
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Directory.CreateDirectory(Game.PatchFolder(game));
            File.WriteAllText(ListPath(game), json.Serialize(new Dictionary<string, object> { { "format", 1 }, { "installed", list } }));
        }

        // The files a package may hold: only its own names, so nothing of the game's can be touched.
        static Regex AllowedNames(Item item)
        {
            string p = Regex.Escape(item.Prefix);
            return item.IsStage
                ? new Regex("^" + p + @"\.(emz|tex\.emz|png|jpg)$", RegexOptions.IgnoreCase)
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
                if (!allowed.IsMatch(e.Name)) return "unexpected file " + e.Name + " (a " + (item.IsStage ? "stage" : "costume") + " here holds only " + item.Prefix + " files)";
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
            else
            {
                foreach (string need in new[] { ".obj.emo", ".nml.emb" })
                    if (!names.Contains(item.Prefix + need)) return "missing " + item.Prefix + need;
                for (int c = 1; c <= 10; c++)
                    foreach (string need in new[] { ".col.emb", ".obj.emm" })
                    {
                        string name = item.Prefix + "_" + c.ToString("D2") + need;
                        if (!names.Contains(name)) return "missing " + name + " (a costume has 10 colours)";
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
            string folder = Game.FolderFor(game, item);
            string patch = Game.PatchFolder(game);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                string problem = CheckPackage(zip, item);
                if (problem != null) throw new InvalidDataException(problem);
                var files = zip.Entries.Where(e => !e.FullName.EndsWith("/")).ToList();
                // Never write over a file EX More Stuff didn't put there.
                foreach (ZipArchiveEntry e in files)
                {
                    string target = Path.Combine(folder, e.Name);
                    string relative = target.Substring(patch.Length + 1);
                    if (File.Exists(target) && !ours.Contains(relative))
                        throw new IOException(e.Name + " is already in the game folder and wasn't installed by EX More Stuff; it was left alone");
                }
                // Replacing an older version of the same item: its files go first.
                Installed old = records.FirstOrDefault(r => r.Item.Key == item.Key);
                if (old != null) { Delete(game, old); records.Remove(old); }
                Directory.CreateDirectory(folder);
                var record = new Installed { Item = item, Source = source };
                foreach (ZipArchiveEntry e in files)
                {
                    string target = Path.Combine(folder, e.Name);
                    e.ExtractToFile(target, true);
                    record.Files.Add(target.Substring(patch.Length + 1));
                }
                records.Add(record);
                Save(game, records);
            }
        }

        static void Delete(string game, Installed record)
        {
            foreach (string relative in record.Files)
            {
                string path = Path.Combine(Game.PatchFolder(game), relative);
                if (File.Exists(path)) File.Delete(path);
            }
            // A fighter's folder the install created (battle\chara\<CHR>) goes too once nothing is left in it.
            foreach (string folder in record.Files.Select(f => Path.Combine(Game.PatchFolder(game), Path.GetDirectoryName(f))).Distinct())
                try
                {
                    string parent = Path.GetDirectoryName(folder);
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any() &&
                        string.Equals(Path.GetFileName(parent), "chara", StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(folder);
                }
                catch (IOException) { }
        }

        public static void Remove(string game, string key)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var records = Load(game);
            Installed record = records.FirstOrDefault(r => r.Item.Key == key);
            if (record == null) return;
            Delete(game, record);
            records.Remove(record);
            Save(game, records);
        }

        // A player's own package (zip): what it is, from its main file's name (KEN_75.obj.emo, STG_C80.emz).
        public static Item Inspect(string zipPath)
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    Match costume = Regex.Match(e.Name, @"^([A-Z0-9]{3})_(\d\d)\.obj\.emo$", RegexOptions.IgnoreCase);
                    Match stage = Regex.Match(e.Name, @"^STG_C(\d\d)\.emz$", RegexOptions.IgnoreCase);
                    var item = new Item { Id = "file:" + Path.GetFileNameWithoutExtension(zipPath), Name = Path.GetFileNameWithoutExtension(zipPath), Author = "", Version = "" };
                    if (stage.Success) { item.Type = "stage"; item.Fighter = ""; item.Slot = int.Parse(stage.Groups[1].Value); return item; }
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
