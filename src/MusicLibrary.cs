using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // The Music tab's songs (user 2026-10-15: "you pick a stage and pick a song ... grab that .csb ... use that for the
    // selected stage"): every song a stage or fighter can be given, each a bank (.csb) as the game plays it, in groups
    // (user: "SF4 songs, SF4 Character themes and then songs by creators"):
    // - SF4 songs: the game's stage themes; SF4 character themes: the 44 fighters' (its own files, never ours in
    //   patch_ae2_tu3);
    // - Songs by <creator>: the custom stages installed that bring their own (their package's BGM_<code>.csb), by the
    //   mod's GameBanana creator;
    // - Your songs: the songs made on Advanced > Music (Music\*.csb), stages of your own with music, and song files in
    //   Mods\Music (a zip of BGM_*.csb files, or a .csb).
    // A song goes in whole when it's the same kind (a stage's three layers onto a stage, a fighter's theme onto a
    // fighter); otherwise its main song is built onto the slot's own bank (MusicPanel.BuildFor), as the Music page does.
    // Song mods from GameBanana (user 2026-10-16: their "Music" section; a zip of BGM_*.csb files, nothing else): kept as
    // Mods\Music\<mod name>.zip with only those files, and noted in Mods\Music\songs.json (its GameBanana key, name,
    // creator, the files it was built from) for Browse Mods' Installed / Update and the Music tab's "Songs by <creator>".
    static class SongMods
    {
        public sealed class Record { public string Key, File, Name, Author, Version; }

        static string ListFile { get { return Path.Combine(MusicLibrary.SongsFolder, "songs.json"); } }

        public static List<Record> Load()
        {
            try
            {
                if (!System.IO.File.Exists(ListFile)) return new List<Record>();
                var list = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(System.IO.File.ReadAllText(ListFile));
                return list.Select(d => new Record
                {
                    Key = Convert.ToString(d["key"]), File = Convert.ToString(d["file"]), Name = Convert.ToString(d["name"]),
                    Author = d.ContainsKey("author") ? Convert.ToString(d["author"]) : "", Version = d.ContainsKey("version") ? Convert.ToString(d["version"]) : "",
                }).Where(r => System.IO.File.Exists(Path.Combine(MusicLibrary.SongsFolder, r.File))).ToList();
            }
            catch (Exception) { return new List<Record>(); }
        }

        static void Save(List<Record> list)
        {
            Directory.CreateDirectory(MusicLibrary.SongsFolder);
            var rows = list.Select(r => new Dictionary<string, object> { { "key", r.Key }, { "file", r.File }, { "name", r.Name }, { "author", r.Author }, { "version", r.Version } }).ToList();
            SafeFile.WriteAllText(ListFile, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(rows), true);
        }

        /// <summary>A song mod in (or its new version over the old): its BGM_*.csb files into Mods\Music\<name>.zip.</summary>
        public static void Put(string key, string name, string author, string version, IEnumerable<KeyValuePair<string, byte[]>> banks)
        {
            Directory.CreateDirectory(MusicLibrary.SongsFolder);
            var list = Load();
            Record old = list.FirstOrDefault(r => r.Key == key);
            string clean = name;
            foreach (char c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_');
            string file = old != null ? old.File : clean.Trim() + ".zip";
            for (int n = 2; old == null && System.IO.File.Exists(Path.Combine(MusicLibrary.SongsFolder, file)); n++) file = clean.Trim() + " (" + n + ").zip";
            string path = Path.Combine(MusicLibrary.SongsFolder, file), temp = path + ".tmp";
            if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);   // left by a write a crash stopped
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var b in banks.GroupBy(b => Path.GetFileName(b.Key), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                    using (Stream s = zip.CreateEntry(Path.GetFileName(b.Key)).Open()) s.Write(b.Value, 0, b.Value.Length);
            SafeFile.Swap(temp, path);
            list.RemoveAll(r => r.Key == key);
            list.Add(new Record { Key = key, File = file, Name = name, Author = author ?? "", Version = version ?? "" });
            Save(list);
        }

        /// <summary>A song mod out (its zip to the Recycle Bin).</summary>
        public static void Remove(string key)
        {
            var list = Load();
            foreach (var r in list.Where(r => r.Key == key).ToList())
            {
                string path = Path.Combine(MusicLibrary.SongsFolder, r.File);
                if (System.IO.File.Exists(path)) PackagesPanel.RecycleZip(path);
                list.Remove(r);
            }
            Save(list);
        }
    }

    static class MusicLibrary
    {
        public const string StageSongs = "SF4 songs", FighterThemes = "SF4 character themes", YourSongs = "Your songs";
        // every creator's songs in one group, in folders: the creator, then their GameBanana collections (user 2026-10-17:
        // "it should say songs by creators, and then list them in folders ... Creators, oops_whats, Second Impact BGM")
        public const string CreatorSongs = "Songs by creators";

        public sealed class Entry
        {
            public string Key, Name, Group, From;   // From: more about it, under its name ("With Downtime in Antarctica"), or null
            public string Creator, Collection;      // a creator's song: whose, and the GameBanana collection it's in (or null)
            public string PictureZip;               // the zip whose picture is its thumbnail (Thumbnail), or null
            public Func<byte[]> Read;
        }

        public static string SongsFolder { get { return Path.Combine(AppFolders.Mods, "Music"); } }

        static readonly Regex BankFile = new Regex(@"^BGM_(.+)\.csb$", RegexOptions.IgnoreCase);

        /// <summary>The name a slot's own song goes by: "Mishima Estate Rooftop Song", "Guile's Theme".</summary>
        public static string OwnName(MusicSlot slot, string stageName)
        {
            return slot.Fighter ? Fighters.Name(slot.Code) + "'s Theme" : stageName + " Song";
        }

        public static string Creators(string author) { return "Songs by " + author; }

        // the GameBanana collection a mod of ours came from ("gamebanana:<number>:..."), by its name; null: in none, or not known yet
        static string CollectionOf(string id)
        {
            var m = Regex.Match(id ?? "", @"^gamebanana:(-?\d+):");
            long n;
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out n) || GameBanana.Collections == null) return null;
            var c = GameBanana.Collections.FirstOrDefault(x => x.Mods.Contains(n));
            return c != null ? c.Name : null;
        }

        /// <summary>Every song there is: the game's stages' (as on its Stages page), the 44 fighters' (as on the Costumes
        /// page), then the creators' and your own.</summary>
        public static List<Entry> Songs(string game)
        {
            var songs = new List<Entry>();
            if (!Game.IsGameFolder(game)) return songs;
            foreach (string code in StageGrid.Grid.Where(c => c != null))
            {
                string file = MusicBank.GameFile(game, "BGM_" + code + ".csb");
                if (file != null) songs.Add(new Entry { Key = "stage:" + code, Name = OwnName(new MusicSlot { Code = code }, Stages.Name(code)), Group = StageSongs, Read = () => File.ReadAllBytes(file) });
            }
            foreach (int id in Fighters.DisplayOrder)
            {
                var slot = new MusicSlot { Code = Fighters.Codes[id], Fighter = true };
                string file = MusicBank.GameFile(game, slot.File);
                if (file != null) songs.Add(new Entry { Key = "fighter:" + slot.Code, Name = OwnName(slot, null), Group = FighterThemes, Read = () => File.ReadAllBytes(file) });
            }
            var creators = new List<Entry>();
            foreach (Installed r in Installer.Load(game).Where(r => r.Item.IsStage && !string.IsNullOrEmpty(r.Package)).OrderBy(r => r.Item.Code))
            {
                Installed record = r;
                string entry = "BGM_" + record.Item.Code + ".csb", name = record.Shown ?? record.Item.Name;
                if (!ZipHas(record.Package, entry)) continue;
                bool gamebanana = (record.Item.Id ?? "").StartsWith("gamebanana:") && !string.IsNullOrEmpty(record.Item.Author);
                creators.Add(new Entry { Key = "custom:" + record.Item.Code, Name = OwnName(new MusicSlot { Code = record.Item.Code }, name),
                                         Group = gamebanana ? CreatorSongs : YourSongs, From = "With the stage " + name,
                                         Creator = gamebanana ? record.Item.Author : null, Collection = gamebanana ? CollectionOf(record.Item.Id) : null, PictureZip = record.Package,
                                         Read = () => FromZip(record.Package, entry) });
            }
            songs.AddRange(creators.OrderBy(e => e.Group == YourSongs ? 1 : 0).ThenBy(e => e.Creator, StringComparer.OrdinalIgnoreCase));
            var fromGameBanana = SongMods.Load().ToDictionary(r => r.File, r => r, StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(SongsFolder))
                foreach (string path in Directory.GetFiles(SongsFolder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileNameWithoutExtension(path), file = path;
                    SongMods.Record mod;
                    if (fromGameBanana.TryGetValue(Path.GetFileName(path), out mod))
                    {
                        // a song mod from GameBanana: by its creator, under its name (a zip of several: one song each)
                        var all = ZipBanks(path).Where(b => !Regex.IsMatch(b, @"^BGM_[A-Za-z0-9]{3}[23]\.csb$", RegexOptions.IgnoreCase)).ToList();
                        foreach (string bank in all)
                        {
                            string inside = bank;
                            songs.Add(new Entry { Key = "mod:" + Path.GetFileName(path) + "/" + bank, Name = all.Count > 1 ? mod.Name + " (" + BankFile.Match(bank).Groups[1].Value + ")" : mod.Name,
                                                  Group = string.IsNullOrEmpty(mod.Author) ? YourSongs : CreatorSongs, From = "Song mod",
                                                  Creator = string.IsNullOrEmpty(mod.Author) ? null : mod.Author, Collection = CollectionOf(mod.Key), PictureZip = file, Read = () => FromZip(file, inside) });
                        }
                        continue;
                    }
                    if (path.EndsWith(".csb", StringComparison.OrdinalIgnoreCase))
                        songs.Add(new Entry { Key = "mod:" + Path.GetFileName(path), Name = name, Group = YourSongs, From = "A song file", Read = () => File.ReadAllBytes(file) });
                    else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        // a zip's round 1 banks (BGM_<x>.csb; BGM_<x>2/3.csb are rounds 2 and 3 of the same song): one song each
                        var banks = ZipBanks(path).Where(b => !Regex.IsMatch(b, @"^BGM_[A-Za-z0-9]{3}[23]\.csb$", RegexOptions.IgnoreCase)).ToList();
                        foreach (string bank in banks)
                        {
                            string inside = bank;
                            songs.Add(new Entry { Key = "mod:" + Path.GetFileName(path) + "/" + bank, Name = banks.Count > 1 ? name + " (" + BankFile.Match(bank).Groups[1].Value + ")" : name,
                                                  Group = YourSongs, From = "A song file", PictureZip = file, Read = () => FromZip(file, inside) });
                        }
                    }
                }
            if (Directory.Exists(AppFolders.Music))
                foreach (string path in Directory.GetFiles(AppFolders.Music, "*.csb").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string file = path, name = Path.GetFileNameWithoutExtension(path);
                    var tagged = Regex.Match(name, @"^[A-Za-z0-9]{3}_[123]_(?:MAIN|ULTRA|LOWHP)_(.+)$", RegexOptions.IgnoreCase);   // TRN_1_MAIN_Jackson
                    songs.Add(new Entry { Key = "yours:" + Path.GetFileName(path), Name = tagged.Success ? tagged.Groups[1].Value : name, Group = YourSongs,
                                          From = "Made on Advanced > Music", Read = () => File.ReadAllBytes(file) });
                }
            return songs;
        }

        static readonly Dictionary<string, System.Drawing.Image> thumbnails = new Dictionary<string, System.Drawing.Image>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A song's thumbnail (user 2026-10-17: its zip's picture on its row in the song window and Manage Music):
        /// the zip's cover.* or first .png/.jpg, cut to 128 x 72; null when it has none. Read once per zip and its time
        /// (run it away from the window: it opens the zip).</summary>
        public static System.Drawing.Image Thumbnail(string zip)
        {
            if (string.IsNullOrEmpty(zip) || !File.Exists(zip)) return null;
            string key = zip + "|" + File.GetLastWriteTimeUtc(zip).Ticks;
            lock (thumbnails) { System.Drawing.Image known; if (thumbnails.TryGetValue(key, out known)) return known; }
            System.Drawing.Image made = null;
            try
            {
                using (var z = ZipFile.OpenRead(zip))
                {
                    var pictures = z.Entries.Where(e => Regex.IsMatch(e.Name, @"\.(png|jpe?g)$", RegexOptions.IgnoreCase)).ToList();
                    var picked = pictures.FirstOrDefault(e => e.Name.StartsWith("cover.", StringComparison.OrdinalIgnoreCase)) ?? pictures.FirstOrDefault();
                    if (picked != null && picked.Length < 16 * 1024 * 1024)
                        using (Stream s = picked.Open())
                        using (var data = new MemoryStream())
                        {
                            s.CopyTo(data);
                            using (var full = Pictures.Read(data.ToArray()))
                            {
                                var small = new System.Drawing.Bitmap(128, 72);
                                using (var g = System.Drawing.Graphics.FromImage(small))
                                {
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    float scale = Math.Max(128f / full.Width, 72f / full.Height);   // filling it, the middle kept
                                    float w = full.Width * scale, h = full.Height * scale;
                                    g.DrawImage(full, (128 - w) / 2, (72 - h) / 2, w, h);
                                }
                                made = small;
                            }
                        }
                }
            }
            catch (Exception) { made = null; }   // not a picture EX More Stuff can read: none
            lock (thumbnails) thumbnails[key] = made;
            return made;
        }

        static bool ZipHas(string zip, string name)
        {
            try { using (var z = ZipFile.OpenRead(zip)) return z.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
            catch (Exception) { return false; }
        }

        static List<string> ZipBanks(string zip)
        {
            try { using (var z = ZipFile.OpenRead(zip)) return z.Entries.Select(e => e.Name).Where(n => BankFile.IsMatch(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception) { return new List<string>(); }
        }

        static byte[] FromZip(string zip, string name)
        {
            using (var z = ZipFile.OpenRead(zip))
            {
                var entry = z.Entries.First(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                using (Stream s = entry.Open())
                using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
            }
        }

        /// <summary>One of the player's own songs, for MANAGE MUSIC: a song mod from GameBanana (its key), or files (a song
        /// made on Advanced > Music: its game file, the song it was made from, its loop settings; a song file in Mods\Music).</summary>
        public sealed class Owner
        {
            public string Name, From, ModKey;
            public string PictureZip;   // the zip whose picture is its thumbnail, or null
            public List<string> Files = new List<string>();
            // when it was added (user 2026-11-17: "date and time"), from its file, so songs added before this was shown
            // get theirs too: a song mod's zip or a song file when it came; a made song when it was last saved (saved
            // again, it's the new song: "look its not here", a song saved again still showing its first save's time)
            public DateTime Added;
        }

        static DateTime Created(string path) { try { return File.GetCreationTime(path); } catch (Exception) { return DateTime.MinValue; } }
        static DateTime Saved(string path) { try { return File.GetLastWriteTime(path); } catch (Exception) { return DateTime.MinValue; } }

        /// <summary>The player's own songs: song mods (by their creator), songs made on Advanced > Music, song files.</summary>
        public static List<Owner> Owned(string game)
        {
            var owned = new List<Owner>();
            var mods = SongMods.Load();
            foreach (var m in mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                owned.Add(new Owner { Name = m.Name, From = string.IsNullOrEmpty(m.Author) ? "Song mod" : "Song mod by " + m.Author, ModKey = m.Key, PictureZip = Path.Combine(SongsFolder, m.File), Added = Created(Path.Combine(SongsFolder, m.File)) });
            var modFiles = new HashSet<string>(mods.Select(m => m.File), StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(SongsFolder))
                foreach (string path in Directory.GetFiles(SongsFolder).Where(f => (f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csb", StringComparison.OrdinalIgnoreCase)) && !modFiles.Contains(Path.GetFileName(f))))
                    owned.Add(new Owner { Name = Path.GetFileNameWithoutExtension(path), From = "A song file in Mods\\Music", Files = { path }, PictureZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : null, Added = Created(path) });
            // custom stages by their names ("Mishima Estate Rooftop"), as the Music tab shows them
            var stageNames = Installer.Load(game).Concat(Installer.Removed(game)).Where(r => r.Item.IsStage).GroupBy(r => r.Item.Code, StringComparer.OrdinalIgnoreCase)
                                      .ToDictionary(g => g.Key, g => g.First().Shown ?? g.First().Item.Name, StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(AppFolders.Music))
                foreach (string bank in Directory.GetFiles(AppFolders.Music, "*.csb").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string stem = Path.GetFileNameWithoutExtension(bank);
                    var tagged = Regex.Match(stem, @"^([A-Za-z0-9]{3})_([123])_(MAIN|ULTRA|LOWHP)_(.+)$", RegexOptions.IgnoreCase);   // TRN_1_MAIN_Jackson
                    var o = new Owner { Name = tagged.Success ? tagged.Groups[4].Value : stem, From = "Made on Advanced > Music" + (tagged.Success ? " for " + Where(tagged.Groups[1].Value, stageNames) : ""), Added = Saved(bank) };
                    // its game file, and the song it was made from and its settings beside it (TRN_1_MAIN_Jackson.mp3, .json)
                    o.Files.AddRange(Directory.GetFiles(AppFolders.Music, stem + ".*"));
                    owned.Add(o);
                }
            return owned;
        }

        static string Where(string code, Dictionary<string, string> stageNames)
        {
            code = code.ToUpperInvariant();
            string named;
            if (Fighters.IsCode(code)) return Fighters.Name(code);
            if (Array.IndexOf(Stages.Codes, code) >= 0) return Stages.Name(code);
            return stageNames.TryGetValue(code, out named) && !string.IsNullOrWhiteSpace(named) ? named + " (" + code + ")" : "stage " + code;
        }

        /// <summary>Uninstalls one of the player's songs: a song mod out of Mods\Music (and its list), files to the Recycle Bin.
        /// What plays on stages and fighters now stays as it is.</summary>
        public static void Uninstall(Owner song)
        {
            if (song.ModKey != null) SongMods.Remove(song.ModKey);
            foreach (string f in song.Files) if (File.Exists(f)) PackagesPanel.RecycleZip(f);
        }

        /// <summary>The song set on a slot from the Music tab or Advanced > Music, or null (its own plays).</summary>
        public static string Playing(Dictionary<string, InstalledSong> installed, MusicSlot slot)
        {
            InstalledSong s;
            return installed.TryGetValue(slot.File, out s) ? s.Layers[0] ?? s.Song : null;
        }

        /// <summary>What the slot plays now, to hear: ours or the stage package's in the patch folder, else the game's own
        /// (a custom stage without music of its own: its stand-in's).</summary>
        public static Entry Now(string game, MusicSlot slot, string name)
        {
            string path = MusicBank.PatchFile(game, slot.File);
            if (!File.Exists(path)) path = MusicBank.GameFile(game, slot.File) ?? MusicBank.GameFile(game, slot.TemplateFile);
            return path == null ? null : new Entry { Key = "now:" + slot.File, Name = name, Read = () => File.ReadAllBytes(path) };
        }

        /// <summary>`song` on `slot` (the game closed): whole when it's the same kind, else its main song built onto the
        /// slot's own bank. Off the window's thread.</summary>
        public static void Use(string game, MusicSlot slot, Entry song)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            byte[] bank = song.Read();
            bool stageBank = MusicBank.CueCount(bank) >= 3;
            InstalledSong about;
            if (stageBank == !slot.Fighter)
                about = new InstalledSong { Song = song.Name, Layers = new[] { song.Name, song.Name, song.Name } };
            else
            {
                int loopStart;
                Song decoded = MusicBank.Decode(bank, 0, song.Name, out loopStart);
                bank = MusicPanel.BuildFor(game, new Dictionary<string, InstalledSong>(), slot, 0, decoded.Pcm, loopStart, decoded.Frames, song.Name, 0, 0, null, out about);
            }
            MusicBank.Install(game, slot, bank, about);
        }

        /// <summary>A slot's own music back (the song set on it off).</summary>
        public static string Own(string game, MusicSlot slot) { return MusicBank.Remove(game, slot); }

        /// <summary>What a docked stage's slot plays, to hear: its docked file's music there, else its stand-in's.</summary>
        public static Entry DockedNow(string game, string zip, MusicSlot slot, string name)
        {
            string standIn = MusicBank.GameFile(game, slot.TemplateFile);
            return new Entry { Key = "docked:" + zip + "|" + slot.File, Name = name, Read = () => DockedMusic.Bank(zip, slot.File) ?? File.ReadAllBytes(standIn) };
        }

        /// <summary>`song` into a docked stage's file for `slot` (user 2026-11-18: picked on the Music tab), as Use puts it in
        /// the game: whole when it's a stage's, else its main song built onto the stand-in's music. Off the window's thread.</summary>
        public static void UseDocked(string game, string zip, MusicSlot slot, Entry song)
        {
            byte[] bank = song.Read();
            InstalledSong about;
            if (MusicBank.CueCount(bank) >= 3)
                about = new InstalledSong { Song = song.Name, Layers = new[] { song.Name, song.Name, song.Name } };
            else
            {
                int loopStart;
                Song decoded = MusicBank.Decode(bank, 0, song.Name, out loopStart);
                byte[] standIn = File.ReadAllBytes(MusicBank.GameFile(game, slot.TemplateFile));
                bank = MusicPanel.BuildFor(game, null, slot, 0, decoded.Pcm, loopStart, decoded.Frames, song.Name, 0, 0, null, out about, standIn);
            }
            DockedMusic.Put(zip, slot.File, bank, about);
        }

        /// <summary>A docked stage's music for `slot` out of its file (the zip written only when it has some).</summary>
        public static void TakeOutDocked(string zip, MusicSlot slot)
        {
            if (DockedMusic.Has(zip, slot.File)) DockedMusic.TakeOut(zip, slot.File);
        }

        /// <summary>A song's main song whole, to play in the Music tab's bar, and where it loops back to. Off the window's thread.</summary>
        public static short[] Decode(Entry song, out int loopStart)
        {
            return MusicBank.Decode(song.Read(), 0, song.Name, out loopStart).Pcm;
        }

        /// <summary>A few seconds of a song to hear: its main song from 30 seconds in (from its start when shorter).
        /// Off the window's thread.</summary>
        public static short[] Taste(Entry song, int seconds)
        {
            int loopStart;
            short[] pcm = MusicBank.Decode(song.Read(), 0, song.Name, out loopStart).Pcm;
            int frames = pcm.Length / 2, from = frames > (30 + seconds) * Song.Rate ? 30 * Song.Rate : 0, take = Math.Min(frames - from, seconds * Song.Rate);
            var part = new short[take * 2];
            Array.Copy(pcm, from * 2, part, 0, part.Length);
            return part;
        }
    }
}
