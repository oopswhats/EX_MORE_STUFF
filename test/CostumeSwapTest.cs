// Costume replacement in a made-up game folder with Ryu's real Original (1), Alternate 1 (2) and DLC Alternate 4 (5, 20
// colors) and a custom Ryu 12 installed: only the Original or an installed costume may stand in (never an alternate or
// DLC costume); Alternate 1 played as the Original (its files copied under RYU_02's names, a modded file there moved
// aside and back); Alternate 4 played as Ryu 12 (colors 13-22 from 1-10); putting back; deleting Ryu 12 gives
// Alternate 4 back. Usage: CostumeSwapTest <real game folder> <scratch folder>   (build_test.bat CostumeSwapTest)
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class CostumeSwapTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void CopyCostume(string real, string root, string game, int costume)
        {
            string from = Path.Combine(real, root, "battle", "chara", "RYU"), to = Path.Combine(game, root, "battle", "chara", "RYU");
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from, "RYU_" + costume.ToString("D2") + "*")) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        }

        static bool Same(string a, string b) { return File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)); }

        static void Main(string[] args)
        {
            string real = args[0], game = Path.Combine(args[1], "game");
            if (Directory.Exists(args[1])) Directory.Delete(args[1], true);
            string patch = Path.Combine(game, "patch_ae2_tu3"), chara = Path.Combine(patch, "battle", "chara", "RYU");
            CopyCostume(real, "resource", game, 1);
            CopyCostume(real, "resource", game, 2);
            CopyCostume(real, @"dlc\04_costume", game, 5);
            string res = Path.Combine(game, "resource", "battle", "chara", "RYU"), dlc = Path.Combine(game, @"dlc\04_costume", "battle", "chara", "RYU");
            // a custom Ryu 12 (Ryu's Original under another slot) installed in the patch folder
            Directory.CreateDirectory(chara);
            foreach (string f in Directory.GetFiles(res, "RYU_01*")) File.Copy(f, Path.Combine(chara, "RYU_12" + Path.GetFileName(f).Substring(6)));

            Console.WriteLine("Which costumes may stand in");
            Check(CostumeSwaps.GameCostumes(game, "RYU").SequenceEqual(new[] { 1, 2, 5 }), "the game's costumes here: 1, 2, 5");
            Check(CostumeSwaps.Problem(game, "RYU", 2, 5) != null && CostumeSwaps.Problem(game, "RYU", 1, 5) != null && CostumeSwaps.Problem(game, "RYU", 5, 2) != null,
                  "an alternate or DLC costume never stands in (2 as 5, 1 as 5, 5 as 2)");
            Check(CostumeSwaps.Problem(game, "RYU", 2, 1) == null && CostumeSwaps.Problem(game, "RYU", 5, 1) == null, "the Original may stand in for an alternate or a DLC costume");
            Check(CostumeSwaps.Problem(game, "RYU", 1, 12) == null && CostumeSwaps.Problem(game, "RYU", 5, 12) == null, "an installed costume may stand in for any, the Original too");
            Check(CostumeSwaps.Problem(game, "RYU", 1, 1) != null && CostumeSwaps.Problem(game, "RYU", 12, 1) != null && CostumeSwaps.Problem(game, "RYU", 3, 1) != null,
                  "refused: itself, a custom costume as the one replaced, a costume the game doesn't have here");
            try { CostumeSwaps.Replace(game, "RYU", 2, 5); Check(false, "Replace refuses a DLC costume too"); }
            catch (InvalidOperationException) { Check(!File.Exists(Path.Combine(chara, "RYU_02.obj.emo")), "Replace refuses a DLC costume too, writing nothing"); }

            Console.WriteLine("Alternate 1 as the Original");
            File.WriteAllText(Path.Combine(chara, "RYU_02_03.col.emb"), "a mod of the player's");
            CostumeSwaps.Replace(game, "RYU", 2, 1);
            Check(Same(Path.Combine(chara, "RYU_02.obj.emo"), Path.Combine(res, "RYU_01.obj.emo")) && Same(Path.Combine(chara, "RYU_02_07.col.emb"), Path.Combine(res, "RYU_01_07.col.emb")),
                  "RYU_02's files are RYU_01's");
            Check(File.Exists(Path.Combine(patch, "ex_more_stuff_backup", "battle", "chara", "RYU", "RYU_02_03.col.emb")), "the modded file there moved aside");
            Check(CostumeSwaps.Load(game).Single().Key == "RYU:2", "on the list");
            CostumeSwaps.Restore(game, "RYU", 2);
            Check(!File.Exists(Path.Combine(chara, "RYU_02.obj.emo")) && File.ReadAllText(Path.Combine(chara, "RYU_02_03.col.emb")) == "a mod of the player's" &&
                  CostumeSwaps.Load(game).Count == 0, "put back: the copies gone, the modded file back");

            Console.WriteLine("Alternate 4 (DLC, 20 colors) as the custom Ryu 12");
            CostumeSwaps.Replace(game, "RYU", 5, 12);
            Check(Same(Path.Combine(chara, "RYU_05.obj.emo"), Path.Combine(chara, "RYU_12.obj.emo")), "its model is Ryu 12's");
            Check(Same(Path.Combine(chara, "RYU_05_02.col.emb"), Path.Combine(chara, "RYU_12_02.col.emb")) && Same(Path.Combine(chara, "RYU_05_13.col.emb"), Path.Combine(chara, "RYU_12_01.col.emb")) &&
                  Same(Path.Combine(chara, "RYU_05_22.obj.emm"), Path.Combine(chara, "RYU_12_10.obj.emm")), "colors 1-10 Ryu 12's own, 13-22 its 1-10 again");
            Check(!File.Exists(Path.Combine(chara, "RYU_05_11.col.emb")), "no colors the costume doesn't have");
            Check(!File.Exists(Path.Combine(chara, "RYU_05.csb")), "its sounds stay the DLC costume's (Ryu 12 has none of its own)");

            Console.WriteLine("Deleting Ryu 12 gives Alternate 4 back");
            Installer.InstallPackage(game, new Item { Id = "x", Type = "costume", Fighter = "RYU", Slot = 12, Name = "Twelve" }, MakePackage(args[1], chara), "file");
            Installed twelve = Installer.Load(game).Single();
            Check(PackagesPanel.DeleteQuestion(game, twelve, "Ryu 12").Contains("Alternate 4, which plays as it now, gets its own costume back"), "the question says so");
            PackagesPanel.PurgeForGood(game, twelve);
            Check(CostumeSwaps.Load(game).Count == 0 && !File.Exists(Path.Combine(chara, "RYU_05.obj.emo")) && !File.Exists(Path.Combine(chara, "RYU_12.obj.emo")),
                  "Alternate 4 has its own files back, Ryu 12 is gone");

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }

        // Ryu 12's files as a package (they're already in the game folder; installing it makes them EX More Stuff's)
        static string MakePackage(string scratch, string chara)
        {
            string zip = Path.Combine(scratch, "Ryu 12.zip");
            using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
                foreach (string f in Directory.GetFiles(chara, "RYU_12*"))
                {
                    System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(z, f, Path.GetFileName(f));
                    File.Delete(f);
                }
            return zip;
        }
    }
}
