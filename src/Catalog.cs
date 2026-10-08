using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // One costume or stage. Costumes are slot 8..99 of a fighter, stages number 1..99; the catalog uses 8..70 and
    // 1..70, and 71..99 are free for players' own packages. EMBER finds them by these file names:
    // a costume is <CHR>_<NN>.* (RYU_12.obj.emo, RYU_12_01.col.emb, RYU_12_01.png ...),
    // a stage STG_C<NN>.* (STG_C12.emz, STG_C12.tex.emz, STG_C12.png).
    class Item
    {
        public string Id, Type, Fighter, Name, Author, Version, Description, Picture, Download, Sha256;
        public int Slot;
        public long Size;

        public bool IsStage { get { return Type == "stage"; } }
        public string Prefix { get { return IsStage ? "STG_C" + Slot.ToString("D2") : Fighter + "_" + Slot.ToString("D2"); } }
        public string Key { get { return IsStage ? "stage:" + Slot : "costume:" + Fighter + ":" + Slot; } }
        public bool Personal { get { return Slot >= Catalog.FirstPersonal; } }
        public string Title
        {
            get
            {
                string number = IsStage ? "Stage " + Slot : "Slot " + Slot;
                return string.IsNullOrEmpty(Name) ? number : number + " - " + Name;
            }
        }

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                { "id", Id }, { "type", Type }, { "fighter", Fighter }, { "slot", Slot }, { "name", Name },
                { "author", Author }, { "version", Version }, { "description", Description }, { "picture", Picture },
                { "download", Download }, { "sha256", Sha256 }, { "size", Size },
            };
        }

        public static Item FromJson(Dictionary<string, object> j)
        {
            Func<string, string> s = k => j.ContainsKey(k) && j[k] != null ? Convert.ToString(j[k]) : "";
            var item = new Item
            {
                Id = s("id"), Type = s("type").ToLowerInvariant(), Fighter = s("fighter").ToUpperInvariant(),
                Name = s("name"), Author = s("author"), Version = s("version"), Description = s("description"),
                Picture = s("picture"), Download = s("download"), Sha256 = s("sha256").ToLowerInvariant(),
            };
            int slot; long size;
            item.Slot = int.TryParse(s("slot"), out slot) ? slot : 0;
            item.Size = long.TryParse(s("size"), out size) ? size : 0;
            return item;
        }

        // Why an item can't be used, or null.
        public string Problem()
        {
            if (Type != "costume" && Type != "stage") return "unknown type '" + Type + "'";
            if (Type == "costume" && !Fighters.IsCode(Fighter)) return "unknown fighter '" + Fighter + "'";
            int lowest = IsStage ? 1 : Catalog.FirstCustomCostume;
            if (Slot < lowest || Slot > Catalog.LastSlot) return "number " + Slot + " is outside " + lowest + "-" + Catalog.LastSlot;
            return null;
        }
    }

    static class Catalog
    {
        public const int FirstCustomCostume = 8, FirstPersonal = 71, LastSlot = 99;

        static readonly HttpClient http = new HttpClient();

        // A web address (the GitHub catalog) or a file path (testing, or a catalog kept on disk).
        public static async Task<string> ReadText(string source)
        {
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return await http.GetStringAsync(source);
            return File.ReadAllText(source);
        }

        // The latest EX More Stuff, if the catalog names it: "program": { "version": "0.2", "page": "https://..." }.
        public static Version ProgramVersion { get; private set; }
        public static string ProgramPage { get; private set; }

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
            }
            if (root != null && root.TryGetValue("items", out list) && list is System.Collections.ArrayList)
                foreach (object entry in (System.Collections.ArrayList)list)
                {
                    var j = entry as Dictionary<string, object>;
                    if (j == null) continue;
                    Item item = Item.FromJson(j);
                    item.Download = Resolve(source, item.Download);
                    item.Picture = Resolve(source, item.Picture);
                    // The catalog only uses the shared numbers; 71-99 belong to players.
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
