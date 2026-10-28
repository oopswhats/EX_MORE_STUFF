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
        public int SoundsLike;              // a custom costume's: the game costume (1-3) it plays as in rollback games, in <prefix>.foley.txt; 0: as usual
        public bool Theirs;                 // Package is the player's own file (picked from inside Mods\, so not copied): deleting leaves it
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
            // the list, or the one before it when a crash left it unreadable (SafeFile)
            string text = SafeFile.ReadList(path, t => { try { json.Deserialize<Dictionary<string, object>>(t); return true; } catch (Exception) { return false; } });
            var root = json.Deserialize<Dictionary<string, object>>(text);
            object list;
            if (root != null && root.TryGetValue(name, out list) && list is System.Collections.ArrayList)
                foreach (object entry in (System.Collections.ArrayList)list)
                {
                    var j = entry as Dictionary<string, object>;
                    if (j == null) continue;
                    var record = new Installed { Item = Item.FromJson(j), Source = Convert.ToString(j["source"]) };
                    if (j.ContainsKey("package") && j["package"] != null) record.Package = Convert.ToString(j["package"]);
                    if (j.ContainsKey("shown") && j["shown"] != null) record.Shown = Convert.ToString(j["shown"]);
                    if (j.ContainsKey("theirs") && j["theirs"] is bool) record.Theirs = (bool)j["theirs"];
                    if (j.ContainsKey("sounds_like") && j["sounds_like"] is int) record.SoundsLike = (int)j["sounds_like"];
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
                if (r.Theirs) j["theirs"] = true;
                if (r.SoundsLike > 0) j["sounds_like"] = r.SoundsLike;
                j["files"] = r.Files;
                return j;
            };
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Directory.CreateDirectory(Game.PatchFolder(game));
            SafeFile.WriteAllText(ListPath(game), json.Serialize(new Dictionary<string, object>
            {
                { "format", 1 }, { "installed", records.Select(entry).ToList() }, { "removed", removed.Select(entry).ToList() },
            }), true);
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
        // and _ - . (every font EMBER has draws them); an accented letter becomes its plain one ("Juri gótica" -> "Juri
        // gotica", user 2026-10-13: it showed as "Juri gtica"), anything else is dropped.
        public const int NameBytes = 40;

        public static bool NameCharacter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-' || c == '.';
        }

        public static string CleanName(string name)
        {
            string plain = new string((name ?? "").Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
            string clean = Regex.Replace(new string(plain.Where(NameCharacter).ToArray()), " {2,}", " ").Trim();
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
            SafeFile.WriteAllText(path, record.Shown);
            record.Files.Add(relative);
        }

        /// <summary>The game costume (1-3) an installed custom costume plays as in rollback games, for its foley (Bison's
        /// Alternate 2 clanks like a robot): its &lt;CHR&gt;_&lt;NN&gt;.foley.txt. 0: not chosen (the usual stand-in).</summary>
        public static int SoundsLike(string game, Item item)   // ROLLBACK-NAME: read by EMBER (CustomContentFiles)
        {
            try
            {
                string text = File.ReadAllText(Path.Combine(Game.FolderFor(game, item), item.Prefix + ".foley.txt")).Trim();
                return text.Length > 0 && text[0] >= '1' && text[0] <= '3' ? text[0] - '0' : 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>Sets SoundsLike (0 removes it), from the game's next start. The file isn't the game's, so it may run.</summary>
        public static void SetSoundsLike(string game, string key, int costume)
        {
            var records = Load(game);
            Installed record = records.FirstOrDefault(r => r.Item.Key == key);
            if (record == null || !record.Item.IsCostume) return;
            record.SoundsLike = costume >= 1 && costume <= 3 ? costume : 0;
            WriteSoundsLike(game, record, true);
            Save(game, records);
        }

        // Writes the record's choice (a package's own .foley.txt stays when the player made none); `clear`: none removes it.
        static void WriteSoundsLike(string game, Installed record, bool clear)
        {
            string path = Path.Combine(Game.FolderFor(game, record.Item), record.Item.Prefix + ".foley.txt");
            string relative = path.Substring(Game.PatchFolder(game).Length + 1);
            if (record.SoundsLike == 0)
            {
                if (!clear) return;
                record.Files.RemoveAll(f => string.Equals(f, relative, StringComparison.OrdinalIgnoreCase));
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            SafeFile.WriteAllText(path, record.SoundsLike.ToString());
            if (!record.Files.Any(f => string.Equals(f, relative, StringComparison.OrdinalIgnoreCase))) record.Files.Add(relative);
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
            // a color's zip may hold the pack's other colors of that costume too (user 2026-10-18: "one install hold all the
            // colors"): only its own go in (InstallPackage)
            if (item.IsColor) return new Regex("^" + Regex.Escape(item.Fighter + "_" + item.Slot.ToString("D2")) + @"_\d\d\.(col\.emb|obj\.emm|png)$", RegexOptions.IgnoreCase);
            return item.IsStage
                ? new Regex("^(" + p + @"\.(emz|tex\.emz|png|jpg)|BGM_" + Regex.Escape(item.Code) + @"[23]?\.csb)$", RegexOptions.IgnoreCase)
                : new Regex("^" + p + @"(\.(obj\.emo|shd\.emo|nml\.emb|bsr|csb|png|clw\.emo|msk\.emo|foley\.txt)|_(E|J)\.csb|_(" + (Catalog.FirstColor(item.Slot) < Catalog.FirstCustomColor ? @"0[1-9]|[1-9]\d" : @"0[1-9]|10|[3-9]\d") + @")\.(col\.emb|obj\.emm|png))$", RegexOptions.IgnoreCase);   // _E/_J.csb: its voice; 30-99: its extra colors; .clw/.msk.emo: Vega's claw and mask (the game loads them for every Vega costume: without them his match never loads, 2026-10-26)
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
                if (!allowed.IsMatch(e.Name)) return "unexpected file " + e.Name + " (a " + (item.IsStage ? "stage" : item.IsColor ? "color" : "costume") + " here holds only " +
                                                      (item.IsColor ? item.Fighter + "_" + item.Slot.ToString("D2") + "_<color>" : item.Prefix) + " files)";
                names.Add(e.Name);
            }
            if (item.IsStage)
            {
                if (!names.Contains(item.Prefix + ".emz")) return "missing " + item.Prefix + ".emz";
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
                    throw new InvalidDataException("the download is incomplete (" + StoragePanel.Megabytes(new FileInfo(temp).Length) + " of " + StoragePanel.Megabytes(item.Size) + ")");
                if (!string.IsNullOrEmpty(item.Sha256) && Sha256(temp) != item.Sha256)
                    throw new InvalidDataException("the download doesn't match the catalog's fingerprint");
                InstallPackage(game, item, temp, "catalog");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>`theirs`: the zip is the player's own file, not EX More Stuff's copy (Installed.Theirs); a package put in
        /// again from the same zip (an update, switched back on, a new code) stays theirs.</summary>
        public static void InstallPackage(string game, Item item, string zipPath, string source, bool theirs = false)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var records = Load(game);
            var ours = new HashSet<string>(records.SelectMany(r => r.Files), StringComparer.OrdinalIgnoreCase);
            string patch = Game.PatchFolder(game);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                string problem = CheckPackage(zip, item);
                if (problem != null) throw new InvalidDataException(problem);
                // (a color: its own files, not the pack's other colors in the same zip)
                var files = zip.Entries.Where(e => !e.FullName.EndsWith("/") && (!item.IsColor || e.Name.StartsWith(item.Prefix + ".", StringComparison.OrdinalIgnoreCase))).ToList();
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
                string package = source == "file" ? Path.GetFullPath(zipPath) : null;
                Func<Installed, bool> same = r => r != null && r.Theirs && r.Package != null && package != null && string.Equals(Path.GetFullPath(r.Package), package, StringComparison.OrdinalIgnoreCase);
                theirs = package != null && (theirs || same(old) || same(shelved));
                int soundsLike = old != null && old.SoundsLike > 0 ? old.SoundsLike : shelved != null ? shelved.SoundsLike : 0;   // the player's choice stays too
                var record = new Installed { Item = item, Source = source, Package = package, Shown = shown, Theirs = theirs, SoundsLike = soundsLike };
                foreach (ZipArchiveEntry e in files)
                {
                    string target = Path.Combine(FolderFor(game, item, e.Name), e.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    SafeFile.Extract(e, target);   // each file whole or not at all
                    record.Files.Add(target.Substring(patch.Length + 1));
                }
                WriteName(game, record);
                if (item.IsCostume) WriteSoundsLike(game, record, false);
                records.Add(record);
                Save(game, records, removed);
            }
            PerfTest.Unlock(item.Key);   // new files in: one that failed its performance test may be tried again
        }

        static bool IsSong(string game, string file)
        {
            try { return MusicBank.Load(game).ContainsKey(file); }
            catch (InvalidDataException) { return false; }
        }

        static void Delete(string game, Installed record)
        {
            // A song put in on the Music page over the package's own music comes off first, so the package's file is
            // back in its place to go with the rest.
            Dictionary<string, InstalledSong> songs;
            try { songs = MusicBank.Load(game); }
            catch (InvalidDataException) { songs = new Dictionary<string, InstalledSong>(); }
            foreach (string relative in record.Files)
                if (string.Equals(Path.GetDirectoryName(relative), MusicBank.Folder, StringComparison.OrdinalIgnoreCase) && songs.ContainsKey(Path.GetFileName(relative)))
                    MusicBank.Remove(game, MusicSlot.FromFile(relative));
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
                InstallPackage(game, moved, record.Package, "file", record.Theirs);   // the new files first, so nothing is lost on a failure
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

        /// <summary>Each package's zip (in the game or switched off) into its place in Mods\ (AppFolders.PackagesFor:
        /// Stages\, or Characters\<the fighter>\). Zips already in Mods\ and EX More Stuff's own builds from before it
        /// (patch_ae2_tu3\ex_more_stuff_mods) are moved; the player's own zips elsewhere are copied, theirs staying where
        /// they are; a zip that isn't there any more is left as it is. Returns how many packages moved.</summary>
        public static int AdoptPackages(string game)
        {
            var records = Load(game);
            var removed = Removed(game);
            string old = Path.Combine(Game.PatchFolder(game), "ex_more_stuff_mods");
            int count = 0;
            // a zip two packages share moves once; one with several fighters' packages stays in Mods\ itself
            foreach (var group in records.Concat(removed).Where(r => !string.IsNullOrEmpty(r.Package)).GroupBy(r => Path.GetFullPath(r.Package), StringComparer.OrdinalIgnoreCase))
            {
                string from = group.Key;
                var folders = group.Select(r => AppFolders.PackagesFor(r.Item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string folder = folders.Count == 1 ? folders[0] : AppFolders.Mods;
                if (string.Equals(Path.GetDirectoryName(from), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || !File.Exists(from)) continue;
                bool move = AppFolders.Inside(from, AppFolders.Mods) || AppFolders.Inside(from, old);
                Directory.CreateDirectory(folder);
                string to = AppFolders.PlaceFor(folder, Path.GetFileName(from), from);
                if (!File.Exists(to)) { if (move) File.Move(from, to); else File.Copy(from, to); }
                else if (AppFolders.Inside(from, old)) File.Delete(from);
                foreach (Installed r in group) r.Package = to;
                count += group.Count();
            }
            if (count > 0) Save(game, records, removed);
            try { if (Directory.Exists(old) && !Directory.EnumerateFileSystemEntries(old).Any()) Directory.Delete(old); }
            catch (IOException) { }
            return count;
        }

        /// <summary>The player's own packages (in the game or switched off) whose zip is gone: deleted from the Mods folder
        /// by hand. Not ones on a drive that isn't there now (a USB stick, a network drive): those may come back.</summary>
        public static List<Installed> MissingPackages(string game)
        {
            return Load(game).Concat(Removed(game)).Where(r =>
            {
                if (r.Source != "file" || string.IsNullOrEmpty(r.Package)) return false;
                try { return !File.Exists(r.Package) && Directory.Exists(Path.GetPathRoot(Path.GetFullPath(r.Package))); }
                catch (Exception) { return false; }
            }).GroupBy(r => r.Item.Key).Select(g => g.First()).ToList();
        }

        /// <summary>What a file was added as before (Add Mod From File), or null: the same zip as a package's own (kept in
        /// Mods\), or a mod file installed from the same file (its packages' ids carry the file's MD5), whatever it's
        /// called now.</summary>
        public static string AddedBefore(string game, string path)
        {
            var records = Load(game).Concat(Removed(game)).ToList();
            long size = new FileInfo(path).Length;
            Func<Installed, string> named = r => (r.Item.IsStage ? "" : Fighters.Name(r.Item.Fighter) + " ") + r.Item.TitleNamed(r.Shown ?? r.Item.Name);
            foreach (Installed r in records)
                if (r.Source == "file" && !string.IsNullOrEmpty(r.Package) && File.Exists(r.Package) && new FileInfo(r.Package).Length == size && AppFolders.SameContent(r.Package, path))
                    return named(r);
            string id = "file:" + ModFile.Md5(path).Substring(0, 16) + ":";
            Installed made = records.FirstOrDefault(r => r.Item.Id != null && r.Item.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
            return made == null ? null : named(made);
        }

        // Takes a removed package of the player's off the list.
        public static void Forget(string game, string key)
        {
            var removed = Removed(game);
            if (removed.RemoveAll(r => r.Item.Key == key) > 0) Save(game, Load(game), removed);
        }

        // A player's own package (zip): what it is, from its main file's name (KEN_75.obj.emo, STG_U01.emz, STG_D05.emz,
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

        /// <summary>A color pack numbered past the game's 10 below 30 (Tom 2026-10-26: colors 13-32, and only 30-32 were
        /// found, the rest skipped without a word): its extra colors moved up together, order and gaps kept, so the lowest is
        /// 30 (13-32 -> 30-49), in the zip itself (its .col.emb, .obj.emm and .png each). Colors 1-10 stay. Returns what
        /// moved ("colors 13-32 are now 30-49"), or null: nothing to move, not a zip, or they wouldn't fit under 100.</summary>
        public static string RenumberColors(string path)
        {
            try
            {
                if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)) return null;
                var color = new Regex(@"^([A-Za-z]{3}_\d\d)_(\d\d)\.(col\.emb|obj\.emm|png)$", RegexOptions.IgnoreCase);
                using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update))
                {
                    var files = zip.Entries.Where(e => !e.FullName.EndsWith("/")).Select(e => new { Entry = e, Match = color.Match(e.Name) })
                        .Where(f => f.Match.Success && Catalog.FirstColor(int.Parse(f.Match.Groups[1].Value.Substring(4))) == Catalog.FirstCustomColor).ToList();   // a custom costume's 11-29 are fine
                    var extras = files.Select(f => int.Parse(f.Match.Groups[2].Value)).Where(n => n > 10).Distinct().OrderBy(n => n).ToList();
                    if (extras.Count == 0 || extras[0] >= Catalog.FirstCustomColor) return null;
                    int shift = Catalog.FirstCustomColor - extras[0];
                    if (extras.Last() + shift > Catalog.LastColor) return null;
                    foreach (var f in files)
                    {
                        int n = int.Parse(f.Match.Groups[2].Value);
                        if (n <= 10) continue;
                        string name = f.Match.Groups[1].Value + "_" + (n + shift).ToString("D2") + "." + f.Match.Groups[3].Value;
                        string full = f.Entry.FullName.Substring(0, f.Entry.FullName.Length - f.Entry.Name.Length) + name;
                        ZipArchiveEntry moved = zip.CreateEntry(full);
                        using (Stream from = f.Entry.Open()) using (Stream to = moved.Open()) from.CopyTo(to);
                        f.Entry.Delete();
                    }
                    return "colors " + extras[0] + "-" + extras.Last() + " are now " + (extras[0] + shift) + "-" + (extras.Last() + shift) +
                           " (extra colors" + (Catalog.CustomColorsFromEleven ? " of the game's costumes" : "") + " are numbered " + Catalog.FirstCustomColor + "-" + Catalog.LastColor + ")";
                }
            }
            catch (Exception) { return null; }   // not a zip, or not writable: left as it is
        }

        /// <summary>Every item a package installs as: one, or a color pack's colors of one costume in one zip (user 2026-10-18:
        /// "one install hold all the colors"), an item each (a color switched off by itself is out of the game, its files
        /// still in the zip). None: not a package.</summary>
        public static List<Item> Packages(string path)
        {
            var items = new List<Item>();
            Item first = Package(path);
            if (first == null) return items;
            if (!first.IsColor) { items.Add(first); return items; }
            try
            {
                var color = new Regex("^" + Regex.Escape(first.Fighter + "_" + first.Slot.ToString("D2")) + @"_(\d\d)\.col\.emb$", RegexOptions.IgnoreCase);
                using (ZipArchive zip = ZipFile.OpenRead(path))
                    foreach (int n in zip.Entries.Select(e => color.Match(e.Name)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n))
                    {
                        Item item = Item.FromJson(first.ToJson());
                        item.Color = n;
                        if (item.Problem() == null && CheckPackage(zip, item) == null) items.Add(item);
                    }
            }
            catch (Exception) { }
            return items;
        }

        /// <summary>A pack's colors added before 0.92, a zip each in its folder in Mods\: put in one zip there (user
        /// 2026-10-18: "one install hold all the colors"), their old zips to the Recycle Bin. Only EX More Stuff's own zips
        /// that hold one color and nothing another package uses. Returns how many packs.</summary>
        public static int MergeColorZips(string game, Action<string> discard = null)
        {
            var records = Load(game);
            var removed = Removed(game);
            var all = records.Concat(removed).ToList();
            Func<string, string, bool> same = (a, b) => a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            int merged = 0;
            var old = new List<string>();
            foreach (var pack in all.Where(r => r.Item.IsColor && r.Source == "file" && !r.Theirs && !string.IsNullOrEmpty(r.Package) && File.Exists(r.Package) && AppFolders.Inside(r.Package, AppFolders.Mods))
                                    .GroupBy(r => r.Item.Fighter + "|" + r.Item.Slot + "|" + (r.Shown ?? r.Item.Name) + "|" + Path.GetDirectoryName(Path.GetFullPath(r.Package)).ToLowerInvariant()))
            {
                var members = pack.ToList();
                var zips = members.Select(r => Path.GetFullPath(r.Package)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (zips.Count < 2 || zips.Count != members.Count) continue;   // already one zip, or zips shared some other way
                if (zips.Any(z => all.Any(r => !members.Contains(r) && same(r.Package, z)))) continue;
                var entries = new List<KeyValuePair<string, byte[]>>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool ok = true;
                foreach (Installed r in members)
                    using (ZipArchive zip = ZipFile.OpenRead(r.Package))
                        foreach (ZipArchiveEntry e in zip.Entries.Where(e => !e.FullName.EndsWith("/")))
                        {
                            if (!e.Name.StartsWith(r.Item.Prefix + ".", StringComparison.OrdinalIgnoreCase)) { ok = false; break; }   // not one color's zip
                            if (!names.Add(e.Name)) continue;
                            using (Stream s = e.Open())
                            using (var data = new MemoryStream()) { s.CopyTo(data); entries.Add(new KeyValuePair<string, byte[]>(e.Name, data.ToArray())); }
                        }
                if (!ok) continue;
                Item first = members[0].Item;
                string stem = Path.GetFileNameWithoutExtension(members[0].Package);
                string suffix = " - " + first.Prefix;
                if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - suffix.Length);
                string folder = Path.GetDirectoryName(Path.GetFullPath(members[0].Package));
                string to = AppFolders.PlaceFor(folder, stem + " - " + first.Fighter + "_" + first.Slot.ToString("D2") + " colors.zip", null);
                ModFile.Write(entries, to);
                foreach (Installed r in members) r.Package = to;
                old.AddRange(zips);
                merged++;
            }
            if (merged == 0) return 0;
            Save(game, records, removed);
            foreach (string z in old) try { (discard ?? PackagesPanel.RecycleZip)(z); } catch (Exception) { }   // (a test deletes them)
            return merged;
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
                    if (color.Success && Fighters.IsCode(color.Groups[1].Value.ToUpperInvariant()) && int.Parse(color.Groups[3].Value) >= Catalog.FirstColor(int.Parse(color.Groups[2].Value)) &&
                        !zip.Entries.Any(x => string.Equals(x.Name, color.Groups[1].Value + "_" + color.Groups[2].Value + ".obj.emo", StringComparison.OrdinalIgnoreCase)))   // a costume's own extra colors: the costume
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
