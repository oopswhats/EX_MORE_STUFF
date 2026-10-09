using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // GameBanana (gamebanana.com), where USF4's mods are shared. Its public API lists the game's mods (game 6971), each
    // mod's files (with GameBanana's own virus scan) and the names inside each file, so what a mod holds, and the codes it
    // uses, are known without downloading it. A file downloads from its own link, checked against the MD5 GameBanana
    // lists. Requests say who they're from (the User-Agent), as the site asks.
    static class GameBanana
    {
        public const int Game = 6971;
        const string Api = "https://gamebanana.com/apiv11/", Data = "https://api.gamebanana.com/Core/Item/Data?";
        static readonly HttpClient http = MakeClient();
        static Task<List<Mod>> all;

        static HttpClient MakeClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EXMoreStuff/" + typeof(GameBanana).Assembly.GetName().Version.ToString(2) + " (+https://github.com/oopswhats/EX_MORE_STUFF)");
            return client;
        }

        public sealed class ModFile
        {
            public long Id, Size, Added, Downloads;
            public string Name, Download, Md5;
            public bool Clean;                                   // GameBanana's analysis and virus scan passed it
            public List<string> Contents = new List<string>();   // paths inside the archive (nested archives as "x.7z/...")
            public string Base { get { return Path.GetFileNameWithoutExtension(Name); } }   // the same version as zip and rar shares it
        }

        public sealed class Mod
        {
            public long Id, Added;
            public string Name, Author, Thumbnail, Picture, Category;
            public int Likes;
            public string Page { get { return "https://gamebanana.com/mods/" + Id; } }
            public bool IsSkin { get { return Category == "Skins"; } }
            public long Downloads { get { return Files.Sum(f => f.Downloads); } }
            public List<ModFile> Files = new List<ModFile>();
        }

        static object Json(string text) { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text); }
        static string Text(object j, string key)
        {
            var d = j as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        static long Number(object j, string key) { long n; return long.TryParse(Text(j, key), out n) ? n : 0; }
        static object Get(object j, string key) { var d = j as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
        static IEnumerable<object> Items(object j) { var list = j as IList; return list != null ? list.Cast<object>() : Enumerable.Empty<object>(); }

        // the first picture of a submission's media: a small one for cards, a large one for the costume's picture
        static void Pictures(object j, out string small, out string large)
        {
            small = large = null;
            var image = Items(Get(Get(j, "_aPreviewMedia"), "_aImages")).FirstOrDefault();
            if (image == null) return;
            string root = Text(image, "_sBaseUrl") + "/";
            large = root + Text(image, "_sFile");
            small = Text(image, "_sFile220") != "" ? root + Text(image, "_sFile220") : large;
        }

        /// <summary>Every USF4 mod on GameBanana with its files and what's in them, read once per run.</summary>
        /// <summary>When the list in use was read from GameBanana (MinValue: the one built into EX More Stuff), and whether
        /// GameBanana couldn't be reached this time (the list kept from before is used).</summary>
        public static DateTime ReadAt { get; private set; }
        public static bool Stale { get; private set; }

        public static Task<List<Mod>> All()
        {
            if (all == null || all.IsFaulted || all.IsCanceled) all = ReadOrKept();
            return all;
        }

        static string KeptFile { get { return AppFolders.DataFile("gamebanana.json"); } }

        // GameBanana's list, kept on the PC each time it's read (gamebanana.json beside the settings): when GameBanana can't
        // be reached (offline for a day, its API changed in years), the list from before is used; failing that, the one
        // built into this version of EX More Stuff. Only the list (names, files, their links and checksums) is kept,
        // never the mods themselves.
        static async Task<List<Mod>> ReadOrKept()
        {
            try
            {
                var mods = await ReadAll();
                if (mods.Count == 0) throw new InvalidDataException("GameBanana listed no mods");
                ReadAt = DateTime.Now;
                Stale = false;
                try { Directory.CreateDirectory(Path.GetDirectoryName(KeptFile)); Save(mods, KeptFile); } catch (Exception) { }   // not kept: next time
                return mods;
            }
            catch (Exception)
            {
                Stale = true;
                DateTime at;
                List<Mod> kept = null;
                try { if (File.Exists(KeptFile)) kept = Load(File.ReadAllText(KeptFile), out at); else at = DateTime.MinValue; } catch (Exception) { at = DateTime.MinValue; }
                if (kept == null)
                    try
                    {
                        using (Stream s = typeof(GameBanana).Assembly.GetManifestResourceStream("gamebanana.json"))
                            if (s != null) { kept = Load(new StreamReader(s).ReadToEnd(), out at); at = DateTime.MinValue; }
                    }
                    catch (Exception) { kept = null; }
                if (kept == null) throw;
                ReadAt = at;
                return kept;
            }
        }

        /// <summary>Writes a list of mods (as read) to a file: EX More Stuff's kept copy, or the one built into it.</summary>
        public static void Save(List<Mod> mods, string path)
        {
            var data = new Dictionary<string, object>
            {
                { "read", DateTime.Now.ToString("o") },
                { "mods", mods.Select(m => new Dictionary<string, object>
                    {
                        { "id", m.Id }, { "added", m.Added }, { "name", m.Name }, { "author", m.Author }, { "thumbnail", m.Thumbnail }, { "picture", m.Picture },
                        { "category", m.Category }, { "likes", m.Likes },
                        { "files", m.Files.Select(f => new Dictionary<string, object>
                            {
                                { "id", f.Id }, { "size", f.Size }, { "added", f.Added }, { "downloads", f.Downloads }, { "name", f.Name }, { "download", f.Download },
                                { "md5", f.Md5 }, { "clean", f.Clean }, { "contents", f.Contents },
                            }).ToList() },
                    }).ToList() },
            };
            string temp = path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(data));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        static List<Mod> Load(string json, out DateTime read)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            read = DateTime.Parse((string)data["read"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            Func<object, long> n = v => Convert.ToInt64(v);
            return ((ArrayList)data["mods"]).Cast<Dictionary<string, object>>().Select(m => new Mod
            {
                Id = n(m["id"]), Added = n(m["added"]), Name = (string)m["name"], Author = (string)m["author"], Thumbnail = m["thumbnail"] as string, Picture = m["picture"] as string,
                Category = (string)m["category"], Likes = (int)n(m["likes"]),
                Files = ((ArrayList)m["files"]).Cast<Dictionary<string, object>>().Select(f => new ModFile
                {
                    Id = n(f["id"]), Size = n(f["size"]), Added = n(f["added"]), Downloads = n(f["downloads"]), Name = (string)f["name"], Download = (string)f["download"],
                    Md5 = (string)f["md5"], Clean = (bool)f["clean"], Contents = ((ArrayList)f["contents"]).Cast<string>().ToList(),
                }).ToList(),
            }).ToList();
        }

        static async Task<List<Mod>> ReadAll()
        {
            var mods = new List<Mod>();
            for (int page = 1; page <= 20; page++)
            {
                object j = Json(await http.GetStringAsync(Api + "Mod/Index?_nPage=" + page + "&_nPerpage=50&_aFilters%5BGeneric_Game%5D=" + Game + "&_sSort=Generic_Oldest"));
                foreach (object r in Items(Get(j, "_aRecords")))
                {
                    string small, large;
                    Pictures(r, out small, out large);
                    mods.Add(new Mod
                    {
                        Id = Number(r, "_idRow"), Name = Text(r, "_sName"), Author = Text(Get(r, "_aSubmitter"), "_sName"), Category = Text(Get(r, "_aRootCategory"), "_sName"),
                        Thumbnail = small, Picture = large, Likes = (int)Number(r, "_nLikeCount"), Added = Number(r, "_tsDateAdded"),
                    });
                }
                object meta = Get(j, "_aMetadata");
                if (meta == null || Text(meta, "_bIsComplete") == "True" || mods.Count >= Number(meta, "_nRecordCount")) break;
            }
            // each mod's files, many mods to a request
            foreach (var chunk in Chunks(mods, 25))
            {
                object answer = Json(await http.GetStringAsync(Data + string.Join("&", chunk.Select(m => "itemtype[]=Mod&itemid[]=" + m.Id + "&fields[]=Files().aFiles()"))));
                var rows = Items(answer).ToList();
                for (int i = 0; i < chunk.Count && i < rows.Count; i++)
                {
                    var files = Items(rows[i]).FirstOrDefault() as Dictionary<string, object>;
                    if (files == null) continue;
                    foreach (object f in files.Values)
                    {
                        var file = new ModFile
                        {
                            Id = Number(f, "_idRow"), Name = Text(f, "_sFile"), Size = Number(f, "_nFilesize"), Added = Number(f, "_tsDateAdded"), Downloads = Number(f, "_nDownloadCount"),
                            Download = Text(f, "_sDownloadUrl"), Md5 = Text(f, "_sMd5Checksum").ToLowerInvariant(),
                            Clean = Text(f, "_sAnalysisResult") == "ok" && Text(f, "_sAvResult") == "clean",
                        };
                        if (file.Download.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) chunk[i].Files.Add(file);
                    }
                    chunk[i].Files = chunk[i].Files.OrderBy(f => f.Added).ThenBy(f => f.Id).ToList();
                }
            }
            // the names inside each file
            foreach (var chunk in Chunks(mods.SelectMany(m => m.Files).ToList(), 40))
            {
                object answer = Json(await http.GetStringAsync(Data + string.Join("&", chunk.Select(f => "itemtype[]=File&itemid[]=" + f.Id + "&fields[]=Metadata().aArchiveFilesList()"))));
                var rows = Items(answer).ToList();
                for (int i = 0; i < chunk.Count && i < rows.Count; i++)
                    chunk[i].Contents = Flatten(Items(rows[i]).FirstOrDefault()).ToList();
            }
            return mods;
        }

        static IEnumerable<List<T>> Chunks<T>(List<T> items, int size)
        {
            for (int i = 0; i < items.Count; i += size) yield return items.GetRange(i, Math.Min(size, items.Count - i));
        }

        // the archive list: paths, or folders as objects of their contents
        static IEnumerable<string> Flatten(object j, string folder = "")
        {
            if (j is string) { yield return folder + (string)j; yield break; }
            var d = j as Dictionary<string, object>;
            if (d != null) { foreach (var e in d) foreach (string p in Flatten(e.Value, folder + e.Key + "/")) yield return p; yield break; }
            foreach (object item in Items(j)) foreach (string p in Flatten(item, folder)) yield return p;
        }

        public static Task<byte[]> Bytes(string url) { return http.GetByteArrayAsync(url); }

        /// <summary>Downloads a mod's file to `path`, checked against its size and MD5.</summary>
        public static async Task Download(ModFile file, string path, IProgress<long> progress)
        {
            using (var response = await http.GetAsync(file.Download, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (Stream from = await response.Content.ReadAsStreamAsync())
                using (Stream to = File.Create(path))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = await from.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await to.WriteAsync(buffer, 0, n);
                        done += n;
                        if (progress != null) progress.Report(done);
                    }
                }
            }
            if (file.Size > 0 && new FileInfo(path).Length != file.Size)
                throw new InvalidDataException("the download is incomplete (" + new FileInfo(path).Length + " of " + file.Size + " bytes)");
            if (file.Md5 != "")
                using (var md5 = MD5.Create())
                using (var s = File.OpenRead(path))
                    if (BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "").ToLowerInvariant() != file.Md5)
                        throw new InvalidDataException("the download doesn't match the MD5 GameBanana lists");
        }

        static readonly string[] ArchiveTypes = { ".zip", ".rar", ".7z" };
        // the only kinds of file taken out of a mod: the game's costume and stage files (all data), pictures, and archives inside it.
        // Anything else in it (a program, a script) never reaches the disk, and nothing in a mod is ever run.
        static readonly string[] Kept = { ".emo", ".emb", ".emm", ".bsr", ".csb", ".emz", ".png", ".jpg", ".jpeg", ".zip", ".rar", ".7z" };

        static bool Keep(string name) { return Kept.Any(k => name.EndsWith(k, StringComparison.OrdinalIgnoreCase)); }

        // a tar pattern for names ending in `ending`, in any case (tar's patterns match case as it is)
        static string Pattern(string ending)
        {
            return "*" + string.Concat(ending.Select(ch => char.IsLetter(ch) ? "[" + char.ToLowerInvariant(ch) + char.ToUpperInvariant(ch) + "]" : ch.ToString()));
        }

        /// <summary>The files of the kinds EX More Stuff uses in an archive (zip; rar and 7z through Windows' own tar), by
        /// their paths inside; an archive inside it is opened too (its files as "inner.7z/...", as GameBanana lists them).</summary>
        public static List<KeyValuePair<string, byte[]>> Unpack(string archive, string workFolder, int depth = 0)
        {
            var files = new List<KeyValuePair<string, byte[]>>();
            if (IsZip(archive))
            {
                using (ZipArchive zip = ZipFile.OpenRead(archive))
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (e.FullName.EndsWith("/") || !Keep(e.Name) || e.Length > 512L * 1024 * 1024) continue;
                        using (Stream s = e.Open())
                        using (var data = new MemoryStream()) { s.CopyTo(data); files.Add(new KeyValuePair<string, byte[]>(e.FullName.Replace('\\', '/'), data.ToArray())); }
                    }
            }
            else
            {
                // rar, 7z and the rest: Windows 10 and 11 have tar (libarchive), which reads them
                string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
                if (!File.Exists(tar)) throw new InvalidOperationException("this mod is a " + Path.GetExtension(archive) + " archive, which needs Windows' own tar (Windows 10 1803 or later)");
                string work = Path.Combine(workFolder, "x" + depth);
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                try
                {
                    string only = string.Join(" ", Kept.Select(k => "--include \"" + Pattern(k) + "\""));
                    var start = new ProcessStartInfo(tar, "-xf \"" + archive + "\" -C \"" + work + "\" " + only) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    using (var p = Process.Start(start))
                    {
                        var error = p.StandardError.ReadToEndAsync();
                        if (!p.WaitForExit(120000)) { p.Kill(); throw new IOException("unpacking took too long"); }
                        // a kind of file the archive doesn't have is reported too ("Not found in archive"): not a problem
                        var problems = error.Result.Split('\n').Select(l => l.Trim()).Where(l => l != "" && !l.EndsWith("Not found in archive") && !l.Contains("Error exit delayed")).ToList();
                        if (p.ExitCode != 0 && problems.Count > 0) throw new InvalidDataException("Windows couldn't unpack it: " + string.Join(" ", problems));
                    }
                    foreach (string path in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        files.Add(new KeyValuePair<string, byte[]>(path.Substring(work.Length + 1).Replace('\\', '/'), File.ReadAllBytes(path)));
                }
                finally
                {
                    try { Directory.Delete(work, true); } catch (Exception) { }
                }
            }
            if (depth >= 2) return files;
            var result = new List<KeyValuePair<string, byte[]>>();
            try
            {
                foreach (var f in files)
                {
                    if (!ArchiveTypes.Contains(Path.GetExtension(f.Key).ToLowerInvariant())) { result.Add(f); continue; }
                    Directory.CreateDirectory(workFolder);
                    string inner = Path.Combine(workFolder, "inner" + depth + Path.GetExtension(f.Key));
                    File.WriteAllBytes(inner, f.Value);
                    try { result.AddRange(Unpack(inner, workFolder, depth + 1).Select(e => new KeyValuePair<string, byte[]>(f.Key + "/" + e.Key, e.Value))); }
                    catch (InvalidDataException) { }   // not an archive after all
                    finally { File.Delete(inner); }
                }
            }
            finally
            {
                if (depth == 0 && Directory.Exists(workFolder)) try { Directory.Delete(workFolder, true); } catch (Exception) { }
            }
            return result;
        }

        static bool IsZip(string path)
        {
            using (var s = File.OpenRead(path))
                return s.Length > 4 && s.ReadByte() == 'P' && s.ReadByte() == 'K';
        }
    }
}
