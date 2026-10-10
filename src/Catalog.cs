using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // One costume or stage. EMBER finds them by these file names:
    // - a costume is slot 8..99 of a fighter, <CHR>_<NN>.* (RYU_12.obj.emo, RYU_12_01.col.emb, RYU_12_01.png ...);
    //   shared mods (GameBanana's, the catalog's) use 8..84; 85..99 aren't free;
    // - a stage has a code of its own, STG_<code>.* (STG_C12.emz, STG_C12.tex.emz, STG_C12.png, BGM_C12.csb): any
    //   three capital letters or digits that aren't a game stage's (Stages.IsCustomCode). This catalog owns C01..C99;
    //   every other code (D05, T99, ZZZ ...) is suggested for players' own packages and other people's.
    // Add from file takes any slot or code; it only stops at files of that slot it didn't install itself.
    class Item
    {
        public string Id, Type, Fighter, Code, Name, Author, Version, Description, Picture, Download, Sha256;
        public int Slot, Color;   // Color: a new color's number (30-99) on costume Slot
        public long Size;

        static readonly Regex CatalogStage = new Regex("^C[0-9][0-9]$");

        public bool IsStage { get { return Type == "stage"; } }
        public bool IsCostume { get { return Type == "costume"; } }
        // A new color (30-99) of one of the game's costumes (Slot 1-7) or of a custom one (8-99): <CHR>_<NN>_<CC> files
        // beside the costume's. Ember lists it after the costume's own colors; players without it see color 1.
        public bool IsColor { get { return Type == "color"; } }
        public string Prefix { get { return IsStage ? "STG_" + Code : Fighter + "_" + Slot.ToString("D2") + (IsColor ? "_" + Color.ToString("D2") : ""); } }
        public string Key { get { return IsStage ? "stage:" + Code : IsColor ? "color:" + Fighter + ":" + Slot + ":" + Color : "costume:" + Fighter + ":" + Slot; } }
        public bool Personal { get { return IsStage ? !CatalogStage.IsMatch(Code ?? "") : IsColor || Slot > Catalog.LastShared; } }
        public string Title { get { return TitleNamed(Name); } }
        public string TitleNamed(string name)
        {
            string number = IsStage ? "Stage " + Code
                          : IsColor ? (Slot < Catalog.FirstCustomCostume ? "Costume " : "Slot ") + Slot + " color " + Color : "Slot " + Slot;
            return string.IsNullOrEmpty(name) ? number : number + " - " + name;
        }

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                { "id", Id }, { "type", Type }, { "fighter", Fighter }, { "slot", Slot }, { "color", Color }, { "code", Code }, { "name", Name },
                { "author", Author }, { "version", Version }, { "description", Description }, { "picture", Picture },
                { "download", Download }, { "sha256", Sha256 }, { "size", Size },
            };
        }

        public static Item FromJson(Dictionary<string, object> j)
        {
            Func<string, string> s = k => j.ContainsKey(k) && j[k] != null ? Convert.ToString(j[k]) : "";
            var item = new Item
            {
                Id = s("id"), Type = s("type").ToLowerInvariant(), Fighter = s("fighter").ToUpperInvariant(), Code = s("code").ToUpperInvariant(),
                Name = s("name"), Author = s("author"), Version = s("version"), Description = s("description"),
                Picture = s("picture"), Download = s("download"), Sha256 = s("sha256").ToLowerInvariant(),
            };
            int slot, color; long size;
            item.Slot = int.TryParse(s("slot"), out slot) ? slot : 0;
            item.Color = int.TryParse(s("color"), out color) ? color : 0;
            item.Size = long.TryParse(s("size"), out size) ? size : 0;
            // Stages written before codes had only their number (stage 12 = C12).
            if (item.IsStage && item.Code == "" && item.Slot > 0) item.Code = "C" + item.Slot.ToString("D2");
            return item;
        }

        // Why an item can't be used, or null.
        public string Problem()
        {
            if (Type != "costume" && Type != "stage" && Type != "color") return "unknown type '" + Type + "'";
            if (IsStage) return Stages.IsCustomCode(Code) ? null : "'" + Code + "' isn't a custom stage code";
            if (!Fighters.IsCode(Fighter)) return "unknown fighter '" + Fighter + "'";
            if (IsColor)
            {
                if (Slot < 1 || Slot > Catalog.LastSlot) return "costume " + Slot + " is outside 1-" + Catalog.LastSlot;
                if (Color < Catalog.FirstCustomColor || Color > Catalog.LastColor) return "color " + Color + " is outside " + Catalog.FirstCustomColor + "-" + Catalog.LastColor;
                return null;
            }
            if (Slot < Catalog.FirstCustomCostume || Slot > Catalog.LastSlot) return "slot " + Slot + " is outside " + Catalog.FirstCustomCostume + "-" + Catalog.LastSlot;
            return null;
        }
    }

    static class Catalog
    {
        // Costume slots: 8-79 GameBanana's shared seats; 80-84 kept for players' own mods (5 per fighter: not shared, so
        // a mod added from a file never meets a GameBanana upload there); 85-99 not free.
        public const int FirstCustomCostume = 8, LastShared = 79, FirstPersonal = 80, LastPersonal = 84, LastSlot = 99;
        // New colors of any costume: 30-99 (after the game's own, which go up to 22). Not shared: any free number.
        public const int FirstCustomColor = 30, LastColor = 99;

        // Codes that aren't free: costume slots 85-99, and stage codes of one A or T with two digits (A12, 1A2, 12A,
        // T12, 1T2, 12T). Using one is allowed, after a warning.
        static readonly Regex NotFreeStage = new Regex(@"^(?:[AT]\d\d|\d[AT]\d|\d\d[AT])$");

        public static bool NotFree(bool stage, string code, int slot)
        {
            return stage ? NotFreeStage.IsMatch(code ?? "") : slot > LastPersonal && slot <= LastSlot;
        }

        // Kept for players' own mods (not on GameBanana): costume slots 80-84, and stage codes of one U with two digits
        // (U12, 1U2, 12U). Mods added from a file go there first; sharing one in them is allowed after a warning.
        static readonly Regex PersonalStage = new Regex(@"^(?:U\d\d|\dU\d|\d\dU)$");

        public static bool PersonalSeat(bool stage, string code, int slot)
        {
            return stage ? PersonalStage.IsMatch(code ?? "") : slot >= FirstPersonal && slot <= LastPersonal;
        }

        public static bool NotFree(Item item) { return NotFree(item.IsStage, item.Code, item.Slot); }

        static readonly HttpClient http = new HttpClient();

        // A web address (the GitHub catalog) or a file path (testing, or a catalog kept on disk).
        public static async Task<string> ReadText(string source)
        {
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return await http.GetStringAsync(source);
            return File.ReadAllText(source);
        }

        // The latest EX More Stuff, if the catalog names it: "program": { "version": "0.2", "page": "https://..." }, and
        // optionally "download" (the zip itself); without it, the zip GitHub serves for that version's release
        // (<page>/download/v0.2/EXMoreStuff-0.2.zip, as make_publish names it).
        public static Version ProgramVersion { get; private set; }
        public static string ProgramPage { get; private set; }
        public static string ProgramDownload { get; private set; }

        /// <summary>Whether `url` answers (a HEAD request, redirects followed): the release's zip is up.</summary>
        public static async Task<bool> Reachable(string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                using (var cancel = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15)))
                using (var response = await http.SendAsync(request, cancel.Token))
                    return response.IsSuccessStatusCode;
            }
            catch (Exception) { return false; }
        }

        public static async Task<List<Item>> Load(string source)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = json.Deserialize<Dictionary<string, object>>(await ReadText(source));
            var items = new List<Item>();
            object list, program;
            Version version;
            var about = root != null && root.TryGetValue("program", out program) ? program as Dictionary<string, object> : null;
            if (about != null && about.ContainsKey("version") && Version.TryParse(Convert.ToString(about["version"]), out version) &&
                about.ContainsKey("page") && Convert.ToString(about["page"]).StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                ProgramVersion = version;
                ProgramPage = Convert.ToString(about["page"]);
                string download = about.ContainsKey("download") ? Convert.ToString(about["download"]) : null;
                ProgramDownload = download != null && download.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? download
                                : ProgramPage.TrimEnd('/') + "/download/v" + version + "/EXMoreStuff-" + version + ".zip";
            }
            if (root != null && root.TryGetValue("items", out list) && list is System.Collections.ArrayList)
                foreach (object entry in (System.Collections.ArrayList)list)
                {
                    var j = entry as Dictionary<string, object>;
                    if (j == null) continue;
                    Item item = Item.FromJson(j);
                    item.Download = Resolve(source, item.Download);
                    item.Picture = Resolve(source, item.Picture);
                    // The catalog only uses the shared numbers (8-84).
                    if (item.Problem() == null && !item.Personal && !string.IsNullOrEmpty(item.Download)) items.Add(item);
                }
            return items;
        }

        // Links in a catalog may be relative to the catalog itself (packages kept beside it).
        public static string Resolve(string catalog, string link)
        {
            if (string.IsNullOrEmpty(link) || link.Contains("://") || Path.IsPathRooted(link)) return link;
            if (catalog.Contains("://")) return new Uri(new Uri(catalog), link).ToString();
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalog)), link);
        }

        public static async Task<byte[]> ReadBytes(string source)
        {
            if (source.Contains("://")) return await http.GetByteArrayAsync(source);
            return File.ReadAllBytes(source);
        }

        // Downloads (or copies) to a file, reporting the bytes done.
        public static async Task Fetch(string source, string target, IProgress<long> progress)
        {
            if (!source.Contains("://")) { File.Copy(source, target, true); return; }
            using (var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Create(target))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, n);
                        done += n;
                        if (progress != null) progress.Report(done);
                    }
                }
            }
        }
    }
}
