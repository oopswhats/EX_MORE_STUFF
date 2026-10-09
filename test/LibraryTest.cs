// EX More Stuff's own folders beside the program, in a made-up game folder and a scratch "program folder": songs kept
// as TRN_3_MAIN_<song> (the song file, its loop and settings, the game file), names read back; a music mod named that
// way going in where its name says (round 2 bringing Tom's Round BGM mod); packages' zips from before the Mods folder
// coming in (EX More Stuff's own builds moved, the player's own copied); a package added from a file copied in.
// Usage: LibraryTest <real game folder> <scratch folder> <a song file (WAV/MP3)>   (build_test.bat LibraryTest)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class LibraryTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static string ColorZip(string path, string prefix, byte[] col, byte[] emm)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (ZipArchive z = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (Stream s = z.CreateEntry(prefix + ".col.emb").Open()) s.Write(col, 0, col.Length);
                using (Stream s = z.CreateEntry(prefix + ".obj.emm").Open()) s.Write(emm, 0, emm.Length);
            }
            return path;
        }

        static void Main(string[] args)
        {
            string real = args[0], scratch = args[1], songFile = args[2];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            string game = Path.Combine(scratch, "game"), patch = Path.Combine(game, "patch_ae2_tu3");
            Directory.CreateDirectory(patch);
            AppFolders.Root = Path.Combine(scratch, "program");
            Directory.CreateDirectory(AppFolders.Root);
            string trn = MusicBank.GameFile(real, "BGM_TRN.csb");
            Directory.CreateDirectory(Path.Combine(game, "resource", "battle", "sound", "bgm"));
            File.Copy(trn, Path.Combine(game, "resource", "battle", "sound", "bgm", "BGM_TRN.csb"));
            byte[] bank = File.ReadAllBytes(trn);

            Console.WriteLine("Songs kept in Music\\");
            var trn3 = new MusicSlot { Code = "TRN", Round = 3 };
            Check(MusicBank.LibraryName(trn3, 0, "Jackson") == "TRN_3_MAIN_Jackson", "TRN round 3, main: TRN_3_MAIN_Jackson");
            Check(MusicBank.LibraryName(new MusicSlot { Code = "JPN" }, 1, "TRN_3_MAIN_Jackson") == "JPN_1_ULTRA_Jackson", "a kept song put elsewhere keeps only its own name");
            Check(MusicBank.LibraryName(new MusicSlot { Code = "RYU", Fighter = true }, 0, "a:b?c") == "RYU_1_MAIN_a_b_c", "a fighter's theme, and characters a file name can't have");
            string name = MusicBank.Keep(trn3, 2, "Jackson", bank, songFile, 1000, 50000, "{\"volumeDb\":-2}");
            string stem = Path.Combine(AppFolders.Music, "TRN_3_LOWHP_Jackson"), ext = Path.GetExtension(songFile).ToLowerInvariant();
            Check(name == "TRN_3_LOWHP_Jackson" && File.ReadAllBytes(stem + ".csb").SequenceEqual(bank) &&
                  File.ReadAllBytes(stem + ext).SequenceEqual(File.ReadAllBytes(songFile)) && File.Exists(stem + ".json"), "the game file, the song as it was, its loop and settings");
            int ls, le; string settings;
            Check(MusicBank.ReadKept(stem + ext, out ls, out le, out settings) && ls == 1000 && le == 50000 && settings == "{\"volumeDb\":-2}", "its loop and settings read back");
            MusicBank.Keep(trn3, 2, "Jackson", new byte[] { 1, 2, 3 }, songFile, 2000, 60000, null);
            Check(File.ReadAllBytes(stem + ".csb").Length == 3 && MusicBank.ReadKept(stem + ext, out ls, out le, out settings) && ls == 2000, "kept again: overwritten");
            MusicBank.Keep(new MusicSlot { Code = "RVR" }, 0, "From the game", bank, null, 0, 10, null);
            Check(File.Exists(Path.Combine(AppFolders.Music, "RVR_1_MAIN_From the game.csb")) && !File.Exists(Path.Combine(AppFolders.Music, "RVR_1_MAIN_From the game.json")),
                  "a game file opened as the song: only the game file it made");

            Console.WriteLine("Names read back");
            int layer; string song;
            MusicSlot slot = MusicBank.TaggedSlot("TRN_3_MAIN_Jackson.csb", out layer, out song);
            Check(slot != null && !slot.Fighter && slot.Code == "TRN" && slot.Round == 3 && layer == 0 && song == "Jackson" && slot.File == "BGM_TRN3.csb", "TRN_3_MAIN_Jackson.csb: BGM_TRN3.csb");
            slot = MusicBank.TaggedSlot("ryu_1_main_x.csb", out layer, out song);
            Check(slot != null && slot.Fighter && slot.File == "BGM_RYU_2CH.csb", "ryu_1_main_x.csb: Ryu's theme");
            Check(MusicBank.TaggedSlot("RYU_2_MAIN_x.csb", out layer, out song) == null && MusicBank.TaggedSlot("notes.csb", out layer, out song) == null &&
                  MusicBank.TaggedSlot("TRN_4_MAIN_x.csb", out layer, out song) == null, "not one: a fighter's round 2, other names, round 4");
            slot = MusicBank.TaggedSlot("C71_2_ULTRA_y.csb", out layer, out song);
            Check(slot != null && slot.Code == "C71" && slot.Round == 2 && layer == 1, "a custom stage's: C71 round 2");

            Console.WriteLine("A music mod named that way");
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("my music/TRN_2_MAIN_Jackson.csb", bank) };
            var result = ModFile.InstallFiles(game, files, "file:music", "My music", "me", "", null, s => { });
            string bgm = Path.Combine(patch, "battle", "sound", "bgm", "BGM_TRN2.csb");
            Check(result.Problems.Count == 0 && result.Into.Count == 1 && File.Exists(bgm) && File.ReadAllBytes(bgm).SequenceEqual(bank),
                  "in as BGM_TRN2.csb (" + string.Join("; ", result.Problems.Concat(result.Into)) + ")");
            InstalledSong kept;
            Check(MusicBank.Load(game).TryGetValue("BGM_TRN2.csb", out kept) && kept.Song == "Jackson", "on the Music page's list, so it can be given back");
            Check(File.Exists(Path.Combine(game, "dinput8.dll")), "round 2: Tom's Round BGM mod put in");

            Console.WriteLine("Packages' zips come into Mods\\");
            byte[] col = File.ReadAllBytes(Path.Combine(real, "resource", "battle", "chara", "RYU", "RYU_01_02.col.emb"));
            byte[] emm = File.ReadAllBytes(Path.Combine(real, "resource", "battle", "chara", "RYU", "RYU_01_02.obj.emm"));
            string built = ColorZip(Path.Combine(patch, "ex_more_stuff_mods", "Built.zip"), "RYU_01_30", col, emm);
            string mine = ColorZip(Path.Combine(scratch, "my zips", "Mine.zip"), "RYU_01_31", col, emm);
            Installer.InstallPackage(game, Installer.Package(built), built, "file");
            Installer.InstallPackage(game, Installer.Package(mine), mine, "file");
            Check(Installer.AdoptPackages(game, AppFolders.Mods) == 2, "two came");
            var records = Installer.Load(game);
            Check(records.All(r => r.Package == null || AppFolders.Inside(r.Package, AppFolders.Mods)) && records.Where(r => r.Package != null).All(r => File.Exists(r.Package)),
                  "both packages now point into Mods\\");
            Check(!File.Exists(built) && !Directory.Exists(Path.Combine(patch, "ex_more_stuff_mods")) && File.Exists(mine),
                  "EX More Stuff's own build moved (its old folder gone); the player's own zip copied, still where it was");
            Check(Installer.AdoptPackages(game, AppFolders.Mods) == 0, "a second start: nothing to do");
            string again = AppFolders.KeepPackage(mine);
            Check(AppFolders.Inside(again, AppFolders.Mods) && Path.GetFileName(again) == "Mine.zip", "a package added from a file: the copy already in Mods\\ is used");
            string other = ColorZip(Path.Combine(scratch, "elsewhere", "Mine.zip"), "RYU_01_32", col, emm);
            Check(Path.GetFileName(AppFolders.KeepPackage(other)) == "Mine (2).zip", "another zip with the same name: Mine (2).zip");

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
