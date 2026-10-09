// GameBanana's list kept on the PC and built into the program, without GameBanana: the built-in list reads back whole
// (mods, files, links, checksums, contents), and what's saved reads back the same. Usage: GameBananaKeptTest <scratch folder>
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ExMoreStuff
{
    static class GameBananaKeptTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            var load = typeof(GameBanana).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static);
            Func<string, System.Collections.Generic.List<GameBanana.Mod>> read = text => { var a = new object[] { text, DateTime.MinValue }; return (System.Collections.Generic.List<GameBanana.Mod>)load.Invoke(null, a); };
            Console.WriteLine("The list built into the program");
            string builtIn;
            using (var s = typeof(GameBanana).Assembly.GetManifestResourceStream("gamebanana.json")) builtIn = new StreamReader(s).ReadToEnd();
            var mods = read(builtIn);
            Check(mods.Count >= 30, mods.Count + " mods");
            var hado = mods.FirstOrDefault(m => m.Id == 680656);
            Check(hado != null && hado.Name.StartsWith("Dark Hado Blanka") && hado.Author == "retrostuff64" && hado.IsSkin, "Dark Hado Blanka, by retrostuff64, a skin");
            Check(hado != null && hado.Files.Count == 8 && hado.Files.All(f => f.Download.StartsWith("https://") && f.Md5.Length == 32 && f.Clean && f.Contents.Count > 0),
                  "its 8 files, each with its link, MD5, scan result and contents");
            Check(SharedCodes.Holders(mods).ContainsKey("BLK 08") && SharedCodes.HeldBy.ContainsKey("stage B01"), "seats from it: Blanka 08, stage B01");

            Console.WriteLine("Saved and read back");
            Directory.CreateDirectory(args[0]);
            string path = Path.Combine(args[0], "kept.json");
            GameBanana.Save(mods, path);
            var again = read(File.ReadAllText(path));
            Check(again.Count == mods.Count && again.Zip(mods, (a, b) => a.Id == b.Id && a.Name == b.Name && a.Files.Count == b.Files.Count &&
                  a.Files.Zip(b.Files, (x, y) => x.Download == y.Download && x.Md5 == y.Md5 && x.Contents.SequenceEqual(y.Contents)).All(z => z)).All(z => z), "the same mods, files, links and contents");
            Directory.Delete(args[0], true);
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
