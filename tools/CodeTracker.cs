// GameBanana's shared codes, kept for good. Run every hour by .github/workflows/codes.yml on GitHub, which saves
// codes.json on this repository's "codes" branch; EX More Stuff reads it there (CodeHistory). Every shared code a
// GameBanana mod holds (a costume seat "BLK 23", a stage code "stage XYZ") is written down with the mod that held it
// first, and never taken off: a mod deleted from GameBanana keeps its code, so a newer upload isn't told that code is
// free while players still have the old one. When GameBanana can't be read, nothing is written.
// Built with all of src\ (-main:ExMoreStuff.CodeTracker). Usage: CodeTracker <codes.json>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    static class CodeTracker
    {
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            AppFolders.Root = Path.Combine(Path.GetTempPath(), "exmorestuff-codetracker");   // GameBanana's list kept here, not beside the tool
            Directory.CreateDirectory(AppFolders.Root);
            string path = args[0];
            var mods = GameBanana.All().GetAwaiter().GetResult();
            if (GameBanana.Stale) { Console.WriteLine("GameBanana couldn't be read: nothing written"); return 0; }
            SharedCodes.Holders(mods);

            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var codes = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            if (File.Exists(path))
            {
                var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                object list;
                if (root != null && root.TryGetValue("codes", out list) && list is System.Collections.ArrayList)
                    foreach (var e in ((System.Collections.ArrayList)list).OfType<Dictionary<string, object>>())
                        codes[Convert.ToString(e["code"])] = e;
            }
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            int added = 0;
            foreach (var held in SharedCodes.HeldBy)
            {
                Dictionary<string, object> known;
                // a code seen for the first time, or held by an older upload than the one written down (first upload wins)
                if (codes.TryGetValue(held.Key, out known) && (Convert.ToInt64(known["mod"]) == held.Value.Id || Convert.ToInt64(known["added"]) <= held.Value.Added)) continue;
                codes[held.Key] = new Dictionary<string, object>
                {
                    { "code", held.Key }, { "mod", held.Value.Id }, { "name", held.Value.Name }, { "author", held.Value.Author },
                    { "added", held.Value.Added }, { "first", today }, { "listed", true },
                };
                added++;
            }
            var listed = new HashSet<long>(mods.Select(m => m.Id));
            foreach (var e in codes.Values) e["listed"] = listed.Contains(Convert.ToInt64(e["mod"]));
            // "checked" changes once a month, so the job commits at least that often and GitHub keeps it scheduled
            File.WriteAllText(path, json.Serialize(new Dictionary<string, object>
            {
                { "format", 1 }, { "checked", DateTime.UtcNow.ToString("yyyy-MM") }, { "codes", codes.Values.ToList() },
            }));
            Console.WriteLine(codes.Count + " codes, " + added + " new, " + codes.Values.Count(e => !(bool)e["listed"]) + " of mods no longer listed");
            return 0;
        }
    }
}
