// The game's official update files, for Advanced > Mods in your game (ModScan): every file in the patch folders
// (patch, patch_ae2, patch_ae2_tu1, patch_ae2_tu1b, patch_ae2_tu2, patch_ae2_tu3) with its size and SHA-256, written to
// res\official.json. Made from a game folder whose official files are the ones dated no later than SSFIV.exe: mods are
// added after the game is installed (Steam dates what it installs). Names and numbers only, no game content.
// Usage: OfficialManifest <game folder> <res\official.json>   (build_test.bat OfficialManifest)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    static class OfficialManifest
    {
        static void Main(string[] args)
        {
            string game = args[0];
            DateTime installed = File.GetLastWriteTimeUtc(Path.Combine(game, "SSFIV.exe")).AddDays(1);
            var files = new List<Dictionary<string, object>>();
            using (var sha = SHA256.Create())
                foreach (string folder in ModScan.PatchFolders)
                {
                    string root = Path.Combine(game, folder);
                    if (!Directory.Exists(root)) continue;
                    int official = 0, later = 0;
                    foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        if (File.GetLastWriteTimeUtc(path) > installed) { later++; continue; }
                        string hash;
                        using (var s = File.OpenRead(path)) hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                        files.Add(new Dictionary<string, object>
                        {
                            { "path", path.Substring(game.TrimEnd('\\').Length + 1) }, { "size", new FileInfo(path).Length }, { "sha256", hash },
                        });
                        official++;
                    }
                    Console.WriteLine("{0,-16} {1,5} official, {2,4} added later (left out)", folder, official, later);
                }
            File.WriteAllText(args[1], new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object>
            {
                { "format", 1 }, { "game", "Ultra Street Fighter IV (Steam), installed " + installed.AddDays(-1).ToString("yyyy-MM-dd") }, { "files", files },
            }));
            Console.WriteLine(files.Count + " official files -> " + args[1] + " (" + new FileInfo(args[1]).Length / 1024 + " KB)");
        }
    }
}
