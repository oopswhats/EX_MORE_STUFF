// New colors (30-99) in a made-up game folder: a color package (RYU_01_30) recognized and put in as it is, named; a
// mod holding a color in a folder without its material, which borrows color 1's and takes the next free number (31);
// a color of a custom costume (SKR_10_40); a color moved to another number in Package codes (its zip rewritten), the
// refusals; deleting one; colors not counted as costume slots. Usage: ColorTest <real game folder> <scratch folder>
// (build_test.bat ColorTest)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class ColorTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static string[] Names(string zip)
        {
            using (ZipArchive z = ZipFile.OpenRead(zip)) return z.Entries.Select(e => e.Name).OrderBy(n => n).ToArray();
        }

        static string RealFile(string real, string fighter, string name)
        {
            foreach (string root in new[] { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" })
            {
                string path = Path.Combine(real, root, "battle", "chara", fighter, name);
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException(name);
        }

        static void Main(string[] args)
        {
            string real = args[0], game = Path.Combine(args[1], "game"), packages = Path.Combine(args[1], "packages");
            if (Directory.Exists(args[1])) Directory.Delete(args[1], true);
            string patch = Path.Combine(game, "patch_ae2_tu3"), chara = Path.Combine(patch, "battle", "chara");
            Directory.CreateDirectory(patch);
            Directory.CreateDirectory(packages);
            // the game's Ryu color 1 material, which a color without one borrows
            string emm1 = RealFile(real, "RYU", "RYU_01_01.obj.emm");
            Directory.CreateDirectory(Path.Combine(game, "resource", "battle", "chara", "RYU"));
            File.Copy(emm1, Path.Combine(game, "resource", "battle", "chara", "RYU", "RYU_01_01.obj.emm"));
            byte[] col = File.ReadAllBytes(RealFile(real, "RYU", "RYU_01_02.col.emb")), emm = File.ReadAllBytes(RealFile(real, "RYU", "RYU_01_02.obj.emm"));

            Console.WriteLine("A color package");
            string zip = Path.Combine(packages, "Navy Ryu.zip");
            using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (Stream s = z.CreateEntry("RYU_01_30.col.emb").Open()) s.Write(col, 0, col.Length);
                using (Stream s = z.CreateEntry("RYU_01_30.obj.emm").Open()) s.Write(emm, 0, emm.Length);
                using (Stream s = z.CreateEntry("RYU_01_30.png").Open()) s.WriteByte(7);
            }
            Item item = Installer.Package(zip);
            Check(item != null && item.IsColor && !item.IsCostume && item.Fighter == "RYU" && item.Slot == 1 && item.Color == 30, "recognized: Ryu costume 1, color 30");
            Check(item.Prefix == "RYU_01_30" && item.Key == "color:RYU:1:30" && item.Title.StartsWith("Costume 1 color 30") && item.Problem() == null, "its prefix, key and title");
            Check(Item.FromJson(item.ToJson()).Key == item.Key, "its listing round-trips");
            Check(Seats.Warning(item) == null, "no seat warning: colors aren't shared");
            Installer.InstallPackage(game, item, zip, "file");
            Installer.Rename(game, item.Key, "Navy");
            string ryu = Path.Combine(chara, "RYU");
            Check(File.Exists(Path.Combine(ryu, "RYU_01_30.col.emb")) && File.Exists(Path.Combine(ryu, "RYU_01_30.obj.emm")) && File.Exists(Path.Combine(ryu, "RYU_01_30.png")),
                  "its files beside the costume's");
            Check(File.ReadAllText(Path.Combine(ryu, "RYU_01_30.txt")) == "Navy", "its name for Ember: RYU_01_30.txt");
            using (ZipArchive z = ZipFile.Open(Path.Combine(packages, "bad.zip"), ZipArchiveMode.Create))
                using (Stream s = z.CreateEntry("RYU_01_31.col.emb").Open()) s.Write(col, 0, col.Length);
            Check(Installer.Package(Path.Combine(packages, "bad.zip")) == null, "a package without its material isn't a complete package");

            Console.WriteLine("A mod with a color in a folder, without its material");
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("Ryu colors/RYU_01_30.col.emb", col) };
            var found = ModFile.FindColors(files);
            Check(found.Count == 1 && found[0].Number == 30 && found[0].Emm == null, "found: color 30, no material");
            var result = ModFile.InstallFiles(game, files, "file:test", "Navy Again", "me", "", new byte[] { 1, 2 }, s => { });
            Check(result.Problems.Count == 0 && result.Into.SequenceEqual(new[] { "Ryu costume 1 color 31" }), "it goes in color 31, 30 being taken (" + string.Join("; ", result.Problems.Concat(result.Into)) + ")");
            Check(File.ReadAllBytes(Path.Combine(ryu, "RYU_01_31.obj.emm")).SequenceEqual(File.ReadAllBytes(emm1)), "its material: the game's color 1's");
            Check(ModFile.FreeColor(game, "RYU", 1, 30) == 32 && ModFile.FreeColor(game, "RYU", 1, 50) == 50 && ModFile.FreeColor(game, "RYU", 2, 30) == 30, "free colors per costume");

            Console.WriteLine("A color of a custom costume");
            files = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("x/SKR_10_40.col.emb", col), new KeyValuePair<string, byte[]>("x/SKR_10_40.obj.emm", emm),
                new KeyValuePair<string, byte[]>("x/SKR_10_40.png", new byte[] { 9 }),
            };
            result = ModFile.InstallFiles(game, files, "file:skr", "Worker Blue", "me", "", null, s => { });
            Installed skr = Installer.Load(game).Single(r => r.Item.Fighter == "SKR");
            Check(result.Problems.Count == 0 && skr.Item.IsColor && skr.Item.Slot == 10 && skr.Item.Color == 40 && skr.Item.Title.StartsWith("Slot 10 color 40"), "Sakura 10 color 40");
            Check(File.ReadAllBytes(Path.Combine(chara, "SKR", "SKR_10_40.png")).SequenceEqual(new byte[] { 9 }), "with its own picture");

            Console.WriteLine("Colors aren't costume slots");
            files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("y/RYU_84_30.col.emb", col), new KeyValuePair<string, byte[]>("y/RYU_84_30.obj.emm", emm) };
            ModFile.InstallFiles(game, files, "file:y", "On 84", "me", "", null, s => { });
            Check(SkinImport.FreeSlot(game, "RYU") == 84, "a color on Ryu 84 leaves slot 84 free for a costume");

            Console.WriteLine("Package codes: another number");
            Installed navy = Installer.Load(game).Single(r => r.Item.Key == "color:RYU:1:30");
            Check(CodeChange.Problem(game, navy, null, 31) != null && CodeChange.Problem(game, navy, null, 29) != null && CodeChange.Problem(game, navy, null, 30) != null,
                  "refused: 31 (taken), 29 (not a new color), 30 (its own)");
            Check(CodeChange.Problem(game, navy, null, 35) == null, "35 is fine");
            CodeChange.Apply(game, navy, null, 35);
            Check(Names(zip).SequenceEqual(new[] { "RYU_01_35.col.emb", "RYU_01_35.obj.emm", "RYU_01_35.png" }), "its zip rewritten as RYU_01_35");
            Check(!File.Exists(Path.Combine(ryu, "RYU_01_30.col.emb")) && File.Exists(Path.Combine(ryu, "RYU_01_35.col.emb")) &&
                  File.ReadAllText(Path.Combine(ryu, "RYU_01_35.txt")) == "Navy", "in the game as color 35, its name kept");

            Console.WriteLine("Delete");
            navy = Installer.Load(game).Single(r => r.Item.Key == "color:RYU:1:35");
            Check(PackagesPanel.RecyclesZip(game, navy) && PackagesPanel.DeleteQuestion(game, navy, "Navy").Contains("goes to the Recycle Bin"), "its zip would go to the Recycle Bin");
            string movedZip = Path.Combine(args[1], "moved.zip");
            File.Move(zip, movedZip);
            Check(!PackagesPanel.RecyclesZip(game, navy) && PackagesPanel.DeleteQuestion(game, navy, "Navy").Contains("isn't where it was"), "a moved zip is left alone, and the question says so");
            PackagesPanel.PurgeForGood(game, navy);
            Check(!File.Exists(Path.Combine(ryu, "RYU_01_35.col.emb")) && !File.Exists(Path.Combine(ryu, "RYU_01_35.txt")) &&
                  !Installer.Load(game).Any(r => r.Item.Key == "color:RYU:1:35") && File.Exists(movedZip), "out of the game (its name file too) and off the list; the moved zip kept");
            Check(File.Exists(Path.Combine(ryu, "RYU_01_31.col.emb")), "the other color stays");

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
