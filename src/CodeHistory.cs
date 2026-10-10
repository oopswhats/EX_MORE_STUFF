using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // GameBanana's shared codes kept for good: the list the repository's hourly job keeps on its "codes" branch
    // (tools\CodeTracker.cs), read once per run, kept beside the program for when GitHub can't be reached, and built in
    // as of this version (res\codes.json). A code in it stays taken after its mod is deleted from GameBanana: that mod
    // stands in SharedCodes.Holders as it was, so a newer upload isn't told the code is free while players still have
    // the old one.
    static class CodeHistory
    {
        public const string Address = "https://raw.githubusercontent.com/oopswhats/EX_MORE_STUFF/codes/codes.json";

        sealed class Entry { public string Code, Name, Author; public long Mod, Added; }

        static Dictionary<string, Entry> known;
        static Task refresh;
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        static string KeptFile { get { return AppFolders.DataFile("codes.json"); } }

        /// <summary>Reads the list once per run: GitHub's (kept beside the program), the kept one, the built-in one,
        /// merged (each code's earliest upload).</summary>
        public static Task Refresh()
        {
            if (refresh == null) refresh = Task.Run(async () =>
            {
                var merged = Offline();
                try
                {
                    string text = await http.GetStringAsync(Address);
                    var fetched = Parse(text);
                    if (fetched.Count > 0)
                    {
                        Merge(merged, fetched);
                        try { File.WriteAllText(KeptFile, text); } catch (Exception) { }
                    }
                }
                catch (Exception) { }   // offline: the kept and built-in lists
                known = merged;
            });
            return refresh;
        }

        /// <summary>For tests: the list as given, nothing read.</summary>
        public static void Use(string json)
        {
            known = new Dictionary<string, Entry>();
            Merge(known, Parse(json));
            refresh = Task.FromResult(0);
        }

        // the built-in list and the kept one (no network)
        static Dictionary<string, Entry> Offline()
        {
            var merged = new Dictionary<string, Entry>();
            try
            {
                using (Stream s = typeof(CodeHistory).Assembly.GetManifestResourceStream("codes.json"))
                    if (s != null) using (var r = new StreamReader(s)) Merge(merged, Parse(r.ReadToEnd()));
            }
            catch (Exception) { }
            try { if (File.Exists(KeptFile)) Merge(merged, Parse(File.ReadAllText(KeptFile))); } catch (Exception) { }
            return merged;
        }

        static List<Entry> Parse(string json)
        {
            var result = new List<Entry>();
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            object list;
            if (root == null || !root.TryGetValue("codes", out list) || !(list is System.Collections.ArrayList)) return result;
            foreach (var e in ((System.Collections.ArrayList)list).OfType<Dictionary<string, object>>())
                try
                {
                    result.Add(new Entry
                    {
                        Code = Convert.ToString(e["code"]), Mod = Convert.ToInt64(e["mod"]), Added = Convert.ToInt64(e["added"]),
                        Name = e.ContainsKey("name") ? Convert.ToString(e["name"]) : "", Author = e.ContainsKey("author") ? Convert.ToString(e["author"]) : "",
                    });
                }
                catch (Exception) { }   // an entry from a newer format: skipped
            return result;
        }

        // each code's earliest upload (the first upload holds it)
        static void Merge(Dictionary<string, Entry> into, IEnumerable<Entry> entries)
        {
            foreach (Entry e in entries)
            {
                Entry had;
                if (!into.TryGetValue(e.Code, out had) || e.Added < had.Added) into[e.Code] = e;
            }
        }

        /// <summary>The mods of the list's codes that GameBanana doesn't list any more, as they were (their codes in
        /// their files' names), to hold their codes with the listed ones.</summary>
        public static List<GameBanana.Mod> Gone(IEnumerable<GameBanana.Mod> listed)
        {
            if (known == null) known = Offline();
            var ids = new HashSet<long>(listed.Select(m => m.Id));
            var gone = new List<GameBanana.Mod>();
            foreach (var g in known.Values.Where(e => !ids.Contains(e.Mod)).GroupBy(e => e.Mod))
            {
                Entry first = g.First();
                var file = new GameBanana.ModFile { Clean = true, Contents = g.Select(e => FileFor(e.Code)).Where(f => f != null).ToList() };
                gone.Add(new GameBanana.Mod
                {
                    Id = first.Mod, Added = first.Added, Name = first.Name + " (deleted from GameBanana)", Author = first.Author, Category = "Skins",
                    Files = new List<GameBanana.ModFile> { file },
                });
            }
            return gone;
        }

        // "BLK 23" -> BLK_23.obj.emo, "stage XYZ" -> STG_XYZ.emz: what Holders reads a code from
        static string FileFor(string code)
        {
            if (code.StartsWith("stage ")) return "STG_" + code.Substring(6) + ".emz";
            var parts = code.Split(' ');
            return parts.Length == 2 ? parts[0] + "_" + parts[1] + ".obj.emo" : null;
        }
    }
}
