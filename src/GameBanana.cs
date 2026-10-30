using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
            public bool Checking;                                // not yet: GameBanana is still checking it (a new upload)
            // A file in its first 2 minutes on GameBanana counts as still checking, passed or not (user 2026-10-15: "it just
            // breaks if you try using it": its download isn't there yet): Ready to offer, Waiting on the card meanwhile.
            public const int Settling = 120;
            public long SettlesIn { get { return Added <= 0 ? 0 : Math.Max(0, Added + Settling - DateTimeOffset.UtcNow.ToUnixTimeSeconds()); } }
            public bool Ready { get { return Clean && SettlesIn == 0; } }
            public bool Waiting { get { return Checking || (Clean && SettlesIn > 0); } }
            public List<string> Contents = new List<string>();   // paths inside the archive (nested archives as "x.7z/...")
            public string Base { get { return Path.GetFileNameWithoutExtension(Name); } }   // the same version as zip and rar shares it
        }

        public sealed class Mod
        {
            public long Id, Added;
            public long Modified;          // when it last changed (as read; not kept)
            public long AuthorId;         // the creator's GameBanana member number (0 in lists kept before it was read)
            public string AuthorPicture;   // the creator's profile picture (an address), or null
            public string Name, Author, Thumbnail, Picture, Category;
            public string Preview;         // a sound's audio preview on GameBanana (an mp3's address), or null
            public bool Rated;             // its maker filled in GameBanana's content ratings (then Ratings names them)
            public List<string> Ratings = new List<string>();
            // 18+ (user 2026-10-17: "the flags for the sexual content"): any of these content ratings; hidden from Browse Mods
            // when the player turns on Settings.HideNsfw on the About tab
            static readonly string[] Adult = { "sexual content", "sexual themes", "partial nudity", "full nudity", "intense violence", "fetishistic", "blood and gore" };
            public bool IsNsfw { get { return Ratings.Any(r => Adult.Contains((r ?? "").ToLowerInvariant().Replace("&", "and").Trim())); } }
            public int Likes;
            // GameBanana's Sounds section (user 2026-10-16: music belongs there, a moderator moved their song mod out of Mods):
            // its entries kept with their number negative, so a sound and a mod of the same number never mix
            public bool IsSound { get { return Id < 0; } }
            public long ItemId { get { return Math.Abs(Id); } }
            public string ItemType { get { return IsSound ? "Sound" : "Mod"; } }
            public string Page { get { return "https://gamebanana.com/" + (IsSound ? "sounds/" : "mods/") + ItemId; } }
            public bool IsSkin { get { return Category == "Skins"; } }
            // all its downloads: the mod's own count, which keeps the downloads of files replaced since (user 2026-10-14: Cammy X
            // Lili went back to 0 when its file was updated; GameBanana's count for it was 4), or its files' (older lists)
            public long TotalDownloads;
            public long Downloads { get { return Math.Max(TotalDownloads, Files.Sum(f => f.Downloads)); } }
            public List<ModFile> Files = new List<ModFile>();
        }

        /// <summary>A GameBanana collection of USF4 mods by one creator (user 2026-10-14: "put all my SFxTK mods in one
        /// collection for users to hit download all"). Anyone can list anyone's mods in a collection, so EX More Stuff's
        /// rule: only a collection whose every item its maker uploaded (Collections leaves the others out).</summary>
        public sealed class Collection
        {
            public long Id, OwnerId;
            public string Name, Owner;
            public List<long> Mods = new List<long>();   // its USF4 mods on the list, by id
            public string Page { get { return "https://gamebanana.com/collections/" + Id; } }
        }

        /// <summary>A creator's support links from their GameBanana profile (its donation methods; user 2026-10-14: "store
        /// kofi / patreon links that are on a users profile"): only addresses on ko-fi.com and patreon.com are taken, so
        /// a profile can't put any other link on a card.</summary>
        public sealed class Support
        {
            public string Kofi, Patreon;
        }

        /// <summary>The creators' support links by member number, as last read with the list (or kept from before).</summary>
        public static Dictionary<long, Support> Supports { get; private set; } = new Dictionary<long, Support>();

        static readonly System.Text.RegularExpressions.Regex KofiLink = new System.Text.RegularExpressions.Regex(@"^https://(www\.)?ko-fi\.com/[A-Za-z0-9_\-]+/?$"),
                                                            PatreonLink = new System.Text.RegularExpressions.Regex(@"^https://(www\.)?patreon\.com/[A-Za-z0-9_\-]+/?$");

        // each creator's profile on the list, 6 at a time (one small request each): their Ko-fi and Patreon links
        static async Task<Dictionary<long, Support>> ReadSupport(List<Mod> mods)
        {
            var gate = new System.Threading.SemaphoreSlim(6);
            var read = await Task.WhenAll(mods.Select(m => m.AuthorId).Where(id => id > 0).Distinct().Select(async id =>
            {
                await gate.WaitAsync();
                try
                {
                    object profile = Json(await http.GetStringAsync(Api + "Member/" + id + "/ProfilePage?" + Fresh));
                    var support = new Support();
                    foreach (object way in Items(Get(profile, "_aDonationMethods")).Concat(Items(Get(profile, "_aContactInfo"))))
                    {
                        string link = Text(way, "_sValue").Trim(), icon = Text(way, "_sIconClasses");
                        if (support.Kofi == null && icon.Contains("Kofi") && KofiLink.IsMatch(link)) support.Kofi = link;
                        if (support.Patreon == null && icon.Contains("Patreon") && PatreonLink.IsMatch(link)) support.Patreon = link;
                    }
                    return new KeyValuePair<long, Support>(id, support);
                }
                catch (Exception) { return new KeyValuePair<long, Support>(id, null); }   // that one's profile not read: none
                finally { gate.Release(); }
            }));
            return read.Where(p => p.Value != null && (p.Value.Kofi != null || p.Value.Patreon != null)).ToDictionary(p => p.Key, p => p.Value);
        }

        /// <summary>The collections that keep EX More Stuff's rule, as last read with the list (or kept from before).</summary>
        public static List<Collection> Collections { get; private set; } = new List<Collection>();

        // USF4's collections and their items (one request each), those that are all their maker's own with USF4 mods on
        // the list in them
        static async Task<List<Collection>> ReadCollections(List<Mod> mods)
        {
            var listed = new HashSet<long>(mods.Select(m => m.Id));
            var found = new List<Collection>();
            // USF4's collections, and each creator's on the list (GameBanana's USF4 list didn't have the user's new one,
            // 2026-10-14; by its maker it did)
            var lists = new List<string> { "_aFilters%5BGeneric_Game%5D=" + Game };
            lists.AddRange(mods.Select(m => m.AuthorId).Where(id => id > 0).Distinct().Select(id => "_aFilters%5BGeneric_Submitter%5D=" + id));
            // 6 lists at a time (a creator's is one small request), then each found once
            var gate = new System.Threading.SemaphoreSlim(6);
            var pages = await Task.WhenAll(lists.Select(async filter =>
            {
                var records = new List<object>();
                await gate.WaitAsync();
                try
                {
                    for (int page = 1; page <= 20; page++)
                    {
                        object j = Json(await http.GetStringAsync(Api + "Collection/Index?_nPage=" + page + "&_nPerpage=50&" + filter + "&" + Fresh));
                        records.AddRange(Items(Get(j, "_aRecords")));
                        object meta = Get(j, "_aMetadata");
                        if (meta == null || Text(meta, "_bIsComplete") == "True") break;
                    }
                }
                finally { gate.Release(); }
                return records;
            }));
            foreach (object r in pages.SelectMany(p => p))
            {
                long id = Number(r, "_idRow");
                if (found.Any(c => c.Id == id)) continue;
                found.Add(new Collection { Id = id, Name = Text(r, "_sName"), Owner = Text(Get(r, "_aSubmitter"), "_sName"), OwnerId = Number(Get(r, "_aSubmitter"), "_idRow") });
            }
            var kept = new List<Collection>();
            foreach (var c in found)
            {
                bool ownOnly = c.OwnerId != 0;
                for (int page = 1; ownOnly && page <= 20; page++)
                {
                    object j = Json(await http.GetStringAsync(Api + "Collection/" + c.Id + "/Items?_nPage=" + page + "&_nPerpage=50&" + Fresh));
                    foreach (object r in Items(Get(j, "_aRecords")))
                    {
                        if (Number(Get(r, "_aSubmitter"), "_idRow") != c.OwnerId) { ownOnly = false; break; }
                        // a mod, or a sound (user 2026-10-17: their songs' collection "BGM - Second Impact"), by its number on the list
                        string model = Text(r, "_sModelName");
                        long id = model == "Sound" ? -Number(r, "_idRow") : Number(r, "_idRow");
                        if ((model == "Mod" || model == "Sound") && listed.Contains(id) && !c.Mods.Contains(id)) c.Mods.Add(id);
                    }
                    object meta = Get(j, "_aMetadata");
                    if (meta == null || Text(meta, "_bIsComplete") == "True") break;
                }
                if (ownOnly && c.Mods.Count > 0) kept.Add(c);
            }
            return kept;
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
            if (all == null || all.IsFaulted || all.IsCanceled) all = ReadWithHistory();
            return all;
        }

        /// <summary>A mod on GameBanana's list as last read (for its creator on a costume or stage card), or null: not
        /// read yet, or not on it.</summary>
        public static Mod Known(long id)
        {
            return all != null && all.Status == TaskStatus.RanToCompletion && all.Result != null ? all.Result.FirstOrDefault(m => m.Id == id) : null;
        }

        /// <summary>The list read from GameBanana again (Refresh): uploads and checks since the first read show up.</summary>
        public static Task<List<Mod>> Again()
        {
            if (all == null || all.IsCompleted) all = ReadWithHistory();
            return all;
        }

        // the list, and meanwhile the codes kept for good (CodeHistory), so holders are worked out with both
        static async Task<List<Mod>> ReadWithHistory()
        {
            Task history = CodeHistory.Refresh();
            var mods = await ReadOrKept();
            try { await history; } catch (Exception) { }
            return mods;
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
                // its collections (EX More Stuff's rule kept); not answering, the ones kept from before (Load reads them)
                // and its creators' support links (one failed: none for them); not answering, both as kept from before
                try { Collections = await ReadCollections(mods); Supports = await ReadSupport(mods); }
                catch (Exception) { try { DateTime ignored; if (File.Exists(KeptFile)) Load(File.ReadAllText(KeptFile), out ignored); } catch (Exception) { } }
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
                        { "id", m.Id }, { "added", m.Added }, { "name", m.Name }, { "author", m.Author }, { "authorId", m.AuthorId }, { "authorPicture", m.AuthorPicture }, { "thumbnail", m.Thumbnail }, { "picture", m.Picture }, { "preview", m.Preview }, { "rated", m.Rated }, { "ratings", m.Ratings },
                        { "category", m.Category }, { "likes", m.Likes }, { "downloads", m.TotalDownloads },
                        { "files", m.Files.Select(f => new Dictionary<string, object>
                            {
                                { "id", f.Id }, { "size", f.Size }, { "added", f.Added }, { "downloads", f.Downloads }, { "name", f.Name }, { "download", f.Download },
                                { "md5", f.Md5 }, { "clean", f.Clean }, { "checking", f.Checking }, { "contents", f.Contents },
                            }).ToList() },
                    }).ToList() },
                { "collections", (Collections ?? new List<Collection>()).Select(c => new Dictionary<string, object>
                    {
                        { "id", c.Id }, { "name", c.Name }, { "owner", c.Owner }, { "ownerId", c.OwnerId }, { "mods", c.Mods },
                    }).ToList() },
                { "support", (Supports ?? new Dictionary<long, Support>()).Select(p => new Dictionary<string, object>
                    {
                        { "member", p.Key }, { "kofi", p.Value.Kofi }, { "patreon", p.Value.Patreon },
                    }).ToList() },
            };
            string temp = path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(data));
            SafeFile.Swap(temp, path);
        }

        static List<Mod> Load(string json, out DateTime read)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            read = DateTime.Parse((string)data["read"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            Func<object, long> n = v => Convert.ToInt64(v);
            // the collections kept with it (lists kept before collections were read have none)
            if (data.ContainsKey("collections") && data["collections"] is ArrayList)
                Collections = ((ArrayList)data["collections"]).Cast<Dictionary<string, object>>().Select(c => new Collection
                {
                    Id = n(c["id"]), Name = (string)c["name"], Owner = (string)c["owner"], OwnerId = n(c["ownerId"]), Mods = ((ArrayList)c["mods"]).Cast<object>().Select(n).ToList(),
                }).ToList();
            // the support links kept with it, checked again as they're read
            if (data.ContainsKey("support") && data["support"] is ArrayList)
                Supports = ((ArrayList)data["support"]).Cast<Dictionary<string, object>>().Select(x => new KeyValuePair<long, Support>(n(x["member"]), new Support
                {
                    Kofi = x["kofi"] is string && KofiLink.IsMatch((string)x["kofi"]) ? (string)x["kofi"] : null,
                    Patreon = x["patreon"] is string && PatreonLink.IsMatch((string)x["patreon"]) ? (string)x["patreon"] : null,
                })).Where(p => p.Value.Kofi != null || p.Value.Patreon != null).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value);
            return ((ArrayList)data["mods"]).Cast<Dictionary<string, object>>().Select(m => new Mod
            {
                Id = n(m["id"]), Added = n(m["added"]), Name = (string)m["name"], Author = (string)m["author"], AuthorId = m.ContainsKey("authorId") ? n(m["authorId"]) : 0,
                AuthorPicture = m.ContainsKey("authorPicture") ? m["authorPicture"] as string : null, Thumbnail = m["thumbnail"] as string, Picture = m["picture"] as string, Preview = m.ContainsKey("preview") ? m["preview"] as string : null, Rated = m.ContainsKey("rated") && m["rated"] is bool && (bool)m["rated"],
                Ratings = m.ContainsKey("ratings") && m["ratings"] is ArrayList ? ((ArrayList)m["ratings"]).Cast<object>().Select(x => Convert.ToString(x)).ToList() : new List<string>(),
                Category = (string)m["category"], Likes = (int)n(m["likes"]), TotalDownloads = m.ContainsKey("downloads") ? n(m["downloads"]) : 0,
                Files = ((ArrayList)m["files"]).Cast<Dictionary<string, object>>().Select(f => new ModFile
                {
                    Id = n(f["id"]), Size = n(f["size"]), Added = n(f["added"]), Downloads = n(f["downloads"]), Name = (string)f["name"], Download = (string)f["download"],
                    Md5 = (string)f["md5"], Clean = (bool)f["clean"], Checking = f.ContainsKey("checking") && (bool)f["checking"],
                    Contents = ((ArrayList)f["contents"]).Cast<string>().ToList(),
                }).ToList(),
            }).ToList();
        }

        // GameBanana's servers keep its collection lists, a collection's items and members' profiles for up to 10 minutes
        // (Cache-Control max-age=600; user 2026-10-14: the Mod Pack showed 3 mods after 2 more were added): these requests
        // carry a marker new for each read of the list, so each Refresh gets them as they are now (a few small requests)
        static string Fresh = "_r=0";

        static async Task<List<Mod>> ReadAll()
        {
            Fresh = "_r=" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);
            var mods = new List<Mod>();
            // the Mods section, then the Sounds section (songs; their numbers kept negative, Mod.IsSound)
            foreach (string section in new[] { "Mod", "Sound" })
            {
                int before = mods.Count;
                for (int page = 1; page <= 20; page++)
                {
                    // newest first: GameBanana's servers keep the oldest-first list for up to 10 minutes (Cache-Control max-age=600,
                    // Cloudflare HIT), so a mod just uploaded wasn't on it (user 2026-10-14: Refresh didn't bring up Mishima Estate
                    // Rooftop, uploaded a minute before); the newest-first list isn't cached (BYPASS)
                    object j = Json(await http.GetStringAsync(Api + section + "/Index?_nPage=" + page + "&_nPerpage=50&_aFilters%5BGeneric_Game%5D=" + Game + "&_sSort=Generic_Newest"));
                    foreach (object r in Items(Get(j, "_aRecords")))
                    {
                        string small, large;
                        Pictures(r, out small, out large);
                        bool sound = section == "Sound";
                        mods.Add(new Mod
                        {
                            Id = sound ? -Number(r, "_idRow") : Number(r, "_idRow"), Name = Text(r, "_sName"), Author = Text(Get(r, "_aSubmitter"), "_sName"), AuthorId = Number(Get(r, "_aSubmitter"), "_idRow"),
                            AuthorPicture = Text(Get(r, "_aSubmitter"), "_sAvatarUrl"), Category = sound ? "Sounds" : Text(Get(r, "_aRootCategory"), "_sName"),
                            Preview = sound ? Text(Get(Get(r, "_aPreviewMedia"), "_aMetadata"), "_sAudioUrl") : null,
                            Rated = Text(r, "_bHasContentRatings") == "True",
                            Thumbnail = small, Picture = large, Likes = (int)Number(r, "_nLikeCount"), Added = Number(r, "_tsDateAdded"),
                            Modified = Number(r, "_tsDateModified"),
                        });
                    }
                    object meta = Get(j, "_aMetadata");
                    if (meta == null || Text(meta, "_bIsComplete") == "True" || mods.Count - before >= Number(meta, "_nRecordCount")) break;
                }
            }
            // a mod uploaded while the pages were read moves the others down one: none listed twice
            mods = mods.GroupBy(m => m.Id).Select(g => g.First()).ToList();
            // each mod's files and its download count, many mods to a request (the API pairs each field with one item, so a mod
            // is asked for twice: its files, then its downloads)
            foreach (var chunk in Chunks(mods, 25))
            {
                object answer = Json(await http.GetStringAsync(Data + string.Join("&", chunk.Select(m => "itemtype[]=" + m.ItemType + "&itemid[]=" + m.ItemId + "&fields[]=Files().aFiles()&" +
                                                                                                         "itemtype[]=" + m.ItemType + "&itemid[]=" + m.ItemId + "&fields[]=downloads")) + "&" + Fresh));
                var rows = Items(answer).ToList();
                for (int i = 0; i < chunk.Count && 2 * i < rows.Count; i++)
                {
                    if (2 * i + 1 < rows.Count)
                    {
                        object total = Items(rows[2 * i + 1]).FirstOrDefault();
                        if (total is int || total is long || total is decimal) chunk[i].TotalDownloads = Convert.ToInt64(total);
                    }
                    var files = Items(rows[2 * i]).FirstOrDefault() as Dictionary<string, object>;
                    if (files == null) continue;
                    chunk[i].Files = FilesFrom(files.Values);
                }
            }
            // that API answers from a copy for a few minutes (user 2026-10-15: "our refresh seems to be stale often"; C.Viper X
            // Nina's replaced file still listed 3 minutes after its new upload, the current API already naming the new one):
            // the mods changed in the last day have their files read from the current API too, 6 at a time
            long since = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 24 * 3600;
            foreach (var chunk in Chunks(mods.Where(m => m.Modified >= since).ToList(), 6))
                await Task.WhenAll(chunk.Select(async m =>
                {
                    try
                    {
                        object files = Get(Json(await http.GetStringAsync(Api + m.ItemType + "/" + m.ItemId + "?_csvProperties=_aFiles&" + Fresh)), "_aFiles");
                        if (files is IList) m.Files = FilesFrom(Items(files));
                    }
                    catch (Exception) { }   // kept as the other API said
                }));
            // the content ratings of those that have any (only the mod's own page names them): 6 at a time
            foreach (var chunk in Chunks(mods.Where(m => m.Rated).ToList(), 6))
                await Task.WhenAll(chunk.Select(async m =>
                {
                    try
                    {
                        var ratings = Get(Json(await http.GetStringAsync(Api + m.ItemType + "/" + m.ItemId + "/ProfilePage?" + Fresh)), "_aContentRatings") as Dictionary<string, object>;
                        if (ratings != null) m.Ratings = ratings.Values.Select(v => Convert.ToString(v)).ToList();
                    }
                    catch (Exception) { }   // not read now: shown as not rated
                }));
            // the names inside each file
            foreach (var chunk in Chunks(mods.SelectMany(m => m.Files).ToList(), 40))
            {
                object answer = Json(await http.GetStringAsync(Data + string.Join("&", chunk.Select(f => "itemtype[]=File&itemid[]=" + f.Id + "&fields[]=Metadata().aArchiveFilesList()")) + "&" + Fresh));
                var rows = Items(answer).ToList();
                for (int i = 0; i < chunk.Count && i < rows.Count; i++)
                    chunk[i].Contents = Flatten(Items(rows[i]).FirstOrDefault()).ToList();
            }
            return mods;
        }

        // a check's state that isn't finished ("" when GameBanana doesn't say: not taken as still checking)
        static bool Pending(string state) { return !string.IsNullOrEmpty(state) && !string.Equals(state, "done", StringComparison.OrdinalIgnoreCase); }

        // a mod's files as either API lists them (the same fields), oldest first
        static List<ModFile> FilesFrom(IEnumerable<object> listed)
        {
            var files = new List<ModFile>();
            foreach (object f in listed)
            {
                var file = new ModFile
                {
                    Id = Number(f, "_idRow"), Name = Text(f, "_sFile"), Size = Number(f, "_nFilesize"), Added = Number(f, "_tsDateAdded"), Downloads = Number(f, "_nDownloadCount"),
                    Download = Text(f, "_sDownloadUrl"), Md5 = Text(f, "_sMd5Checksum").ToLowerInvariant(),
                    Clean = Text(f, "_sAnalysisResult") == "ok" && Text(f, "_sAvResult") == "clean",
                    // its analysis or virus scan not done yet (user 2026-10-13: a file read 16 s after its upload)
                    Checking = Pending(Text(f, "_sAnalysisState")) || Pending(Text(f, "_sAvState")),
                };
                if (file.Download.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) files.Add(file);
            }
            return files.OrderBy(f => f.Added).ThenBy(f => f.Id).ToList();
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

        /// <summary>The picture inside a zip on GameBanana, read without downloading the zip (user 2026-10-17: GameBanana's
        /// Sounds section takes no picture, so a song mod's card shows the .png or .jpg in its zip): its end, where the zip
        /// lists what's in it, then only that picture's bytes (GameBanana answers parts of a file). A picture named cover.*
        /// first, else the first; null when there's none or GameBanana won't give parts.</summary>
        public static async Task<byte[]> ZipPicture(string url)
        {
            Tuple<byte[], long> end = await Part(url, "bytes=-66000");
            if (end == null) return null;
            byte[] tail = end.Item1;
            long total = end.Item2, tailStart = total - tail.Length;
            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--) if (BitConverter.ToUInt32(tail, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) return null;
            long cdSize = BitConverter.ToUInt32(tail, eocd + 12), cdAt = BitConverter.ToUInt32(tail, eocd + 16);
            byte[] cd;
            if (cdAt >= tailStart) { cd = new byte[cdSize]; Array.Copy(tail, cdAt - tailStart, cd, 0, cdSize); }
            else { var got = await Part(url, "bytes=" + cdAt + "-" + (cdAt + cdSize - 1)); if (got == null) return null; cd = got.Item1; }
            int pick = -1, pickMethod = 0;
            long pickAt = 0, pickSize = 0, pickFull = 0;
            for (int at = 0; at + 46 <= cd.Length && BitConverter.ToUInt32(cd, at) == 0x02014b50;)
            {
                int method = BitConverter.ToUInt16(cd, at + 10), nameLength = BitConverter.ToUInt16(cd, at + 28);
                int extra = BitConverter.ToUInt16(cd, at + 30), comment = BitConverter.ToUInt16(cd, at + 32);
                long size = BitConverter.ToUInt32(cd, at + 20), full = BitConverter.ToUInt32(cd, at + 24), local = BitConverter.ToUInt32(cd, at + 42);
                string name = System.Text.Encoding.UTF8.GetString(cd, at + 46, Math.Min(nameLength, cd.Length - at - 46)).Replace('\\', '/');
                string file = name.Substring(name.LastIndexOf('/') + 1);
                bool picture = Regex.IsMatch(file, @"\.(png|jpe?g)$", RegexOptions.IgnoreCase) && (method == 0 || method == 8) && full < 8 * 1024 * 1024;
                bool cover = file.StartsWith("cover.", StringComparison.OrdinalIgnoreCase);
                if (picture && (pick < 0 || cover)) { pick = at; pickMethod = method; pickAt = local; pickSize = size; pickFull = full; if (cover) break; }
                at += 46 + nameLength + extra + comment;
            }
            if (pick < 0) return null;
            // its entry in the zip: a 30-byte header with its name and extra field, then its data
            var head = await Part(url, "bytes=" + pickAt + "-" + (pickAt + 29));
            if (head == null || head.Item1.Length < 30 || BitConverter.ToUInt32(head.Item1, 0) != 0x04034b50) return null;
            long dataAt = pickAt + 30 + BitConverter.ToUInt16(head.Item1, 26) + BitConverter.ToUInt16(head.Item1, 28);
            var data = await Part(url, "bytes=" + dataAt + "-" + (dataAt + pickSize - 1));
            if (data == null) return null;
            if (pickMethod == 0) return data.Item1;
            using (var inflate = new DeflateStream(new MemoryStream(data.Item1), CompressionMode.Decompress))
            using (var output = new MemoryStream((int)pickFull)) { inflate.CopyTo(output); return output.ToArray(); }
        }

        // part of a file on GameBanana (`range`, as HTTP writes it) and the file's whole length; null when it isn't given in parts
        static async Task<Tuple<byte[], long>> Part(string url, string range)
        {
            using (var ask = new HttpRequestMessage(HttpMethod.Get, url))
            {
                ask.Headers.TryAddWithoutValidation("Range", range);
                using (var answer = await http.SendAsync(ask, HttpCompletionOption.ResponseHeadersRead))
                {
                    if ((int)answer.StatusCode != 206 || answer.Content.Headers.ContentRange == null || answer.Content.Headers.ContentRange.Length == null) return null;
                    byte[] bytes = await answer.Content.ReadAsByteArrayAsync();
                    return Tuple.Create(bytes, answer.Content.Headers.ContentRange.Length.Value);
                }
            }
        }

        /// <summary>A file the list named is gone from GameBanana: its link leads to the mod's download page.</summary>
        public sealed class FileReplaced : Exception
        {
            public FileReplaced() : base("its file was replaced on GameBanana") { }
        }

        /// <summary>Downloads a mod's file to `path`, checked against its size and MD5.</summary>
        public static async Task Download(ModFile file, string path, IProgress<long> progress)
        {
            using (var response = await http.GetAsync(file.Download, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                // a file replaced since the list was read (user 2026-10-15: C.Viper X Nina's new upload, read 15 s after it
                // went up with the old file still listed): GameBanana sends its download page instead
                string type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (type.Equals("text/html", StringComparison.OrdinalIgnoreCase)) throw new FileReplaced();
                using (Stream from = await response.Content.ReadAsStreamAsync())
                using (Stream to = File.Create(path))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int n, told = 0;
                    while ((n = await from.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await to.WriteAsync(buffer, 0, n);
                        done += n;
                        // told about 10 times a second, not for every piece (each one is a repaint of the footer)
                        if (progress != null && Environment.TickCount - told >= 100) { progress.Report(done); told = Environment.TickCount; }
                    }
                    if (progress != null) progress.Report(done);
                }
            }
            if (file.Size > 0 && new FileInfo(path).Length != file.Size)
                throw new InvalidDataException("the download is incomplete (" + StoragePanel.Megabytes(new FileInfo(path).Length) + " of " + StoragePanel.Megabytes(file.Size) + ")");
            if (file.Md5 != "")
                using (var md5 = MD5.Create())
                using (var s = File.OpenRead(path))
                    if (BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "").ToLowerInvariant() != file.Md5)
                        throw new InvalidDataException("the download doesn't match the MD5 GameBanana lists");
        }

        static readonly string[] ArchiveTypes = { ".zip", ".rar", ".7z" };

        // a 7z archive's kept files by EX More Stuff's own reader (Windows 10's tar can't unpack LZMA, user 2026-10-13); null
        // when it isn't a 7z or uses a method the reader doesn't know (then tar tries)
        static List<KeyValuePair<string, byte[]>> SevenZipFiles(string archive)
        {
            if (!SevenZip.Is(archive)) return null;
            try { return SevenZip.Read(archive, name => Keep(name)).Where(f => f.Value.LongLength <= 512L * 1024 * 1024).ToList(); }
            catch (NotSupportedException) { return null; }
        }
        // a rar's kept files by EX More Stuff's own readers (Windows 10's tar can't undo old RAR's filters, user 2026-10-16;
        // some Windows have no tar at all, user 2026-10-29: RAR5 too); null when it isn't one or uses what the readers don't
        // know (then tar tries)
        static List<KeyValuePair<string, byte[]>> RarFiles(string archive)
        {
            try
            {
                if (Rar.Is(archive)) return Rar.Read(archive, name => Keep(name));
                if (Rar5.Is(archive)) return Rar5.Read(archive, name => Keep(name));
                return null;
            }
            catch (NotSupportedException) { return null; }
        }
        // the only kinds of file taken out of a mod: the game's costume and stage files (all data), pictures, and archives inside it.
        // Anything else in it (a program, a script) never reaches the disk, and nothing in a mod is ever run.
        static readonly string[] Kept = { ".emo", ".emb", ".emm", ".bsr", ".csb", ".emz", ".bvs", ".png", ".jpg", ".jpeg", ".zip", ".rar", ".7z" };   // .bvs: effects (Effects)

        static bool Keep(string name) { return Kept.Any(k => name.EndsWith(k, StringComparison.OrdinalIgnoreCase)); }

        // a tar pattern for names ending in `ending`, in any case (tar's patterns match case as it is)
        static string Pattern(string ending)
        {
            return "*" + string.Concat(ending.Select(ch => char.IsLetter(ch) ? "[" + char.ToLowerInvariant(ch) + char.ToUpperInvariant(ch) + "]" : ch.ToString()));
        }

        /// <summary>The files of the kinds EX More Stuff uses in an archive (zip; 7z by SevenZip; rar by Rar and Rar5; 7z methods SevenZip
        /// and rar features they don't know, through Windows' own tar), by their paths inside; an archive inside it is opened too (its files as
        /// "inner.7z/...", as GameBanana lists them).</summary>
        public static List<KeyValuePair<string, byte[]>> Unpack(string archive, string workFolder, int depth = 0)
        {
            var files = SevenZipFiles(archive) ?? RarFiles(archive);
            if (files == null && IsZip(archive))
            {
                files = new List<KeyValuePair<string, byte[]>>();
                using (ZipArchive zip = ZipFile.OpenRead(archive))
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (e.FullName.EndsWith("/") || !Keep(e.Name) || e.Length > 512L * 1024 * 1024) continue;
                        using (Stream s = e.Open())
                        using (var data = new MemoryStream()) { s.CopyTo(data); files.Add(new KeyValuePair<string, byte[]>(e.FullName.Replace('\\', '/'), data.ToArray())); }
                    }
            }
            else if (files == null)
            {
                files = new List<KeyValuePair<string, byte[]>>();
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
                        // only pictures it couldn't read (user 2026-10-16: kenji490's Ninja Ryu rar, "all.jpg: Parsing filters is
                        // unsupported", tar can't undo RAR's picture filter; the costume beside it came out whole): those
                        // pictures are left out, the rest is used
                        var badPictures = problems.Select(l => Regex.Match(l, @"^(?:tar(?:\.exe)?: )?(.+?\.(?:png|jpe?g)): ", RegexOptions.IgnoreCase))
                                                  .Where(m => m.Success).Select(m => m.Groups[1].Value.Replace('\\', '/')).ToList();
                        if (p.ExitCode != 0 && problems.Count > 0 && badPictures.Count == problems.Count)
                            foreach (string bad in badPictures) { string at = Path.Combine(work, bad.Replace('/', Path.DirectorySeparatorChar)); if (File.Exists(at)) File.Delete(at); }
                        else if (p.ExitCode != 0 && problems.Count > 0) throw new InvalidDataException("Windows couldn't unpack it: " + string.Join(" ", problems));
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
