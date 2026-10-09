// Package codes in a made-up game folder: a stage package (Training Stage re-coded to D05, with music) moved to E07
// while in the game - its zip renamed and re-coded inside, put in again under E07 with its name, a Music-page round 2
// song moving with it - and a switched-off costume moved to another slot; refusals for game codes, the same code and
// a slot already taken. Usage: CodeTest <real game folder> <scratch folder>   (build_test.bat CodeTest)
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    static class CodeTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static string[] Names(string zip)
        {
            using (ZipArchive z = ZipFile.OpenRead(zip)) return z.Entries.Select(e => e.Name).OrderBy(n => n).ToArray();
        }

        static byte[] Entry(string zip, string name)
        {
            using (ZipArchive z = ZipFile.OpenRead(zip))
            using (Stream s = z.Entries.First(e => e.Name == name).Open())
            using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
        }

        static void Main(string[] args)
        {
            string real = args[0], game = Path.Combine(args[1], "game"), packages = Path.Combine(args[1], "packages");
            if (Directory.Exists(args[1])) Directory.Delete(args[1], true);
            Directory.CreateDirectory(Path.Combine(game, "patch_ae2_tu3"));
            Directory.CreateDirectory(packages);
            string patch = Path.Combine(game, "patch_ae2_tu3");

            // a stage package D05: Training Stage re-coded, its own music
            string stageZip = Path.Combine(packages, "My Stage.zip");
            using (ZipArchive zip = ZipFile.Open(stageZip, ZipArchiveMode.Create))
            {
                foreach (string suffix in StagePack.Suffixes)
                {
                    byte[] data = StagePack.Recode(File.ReadAllBytes(Path.Combine(real, "resource", StagePack.RelativePath("TRN", suffix))), "TRN", "D05");
                    using (Stream s = zip.CreateEntry("STG_D05" + suffix).Open()) s.Write(data, 0, data.Length);
                }
                zip.CreateEntryFromFile(Path.Combine(real, @"dlc\04_ae2\battle\sound\bgm\BGM_JUR.csb"), "BGM_D05.csb");
                using (Stream s = zip.CreateEntry("STG_D05.png").Open()) s.WriteByte(1);
            }
            Item stage = Installer.Inspect(stageZip);
            Installer.InstallPackage(game, stage, stageZip, "file");
            Installer.Rename(game, stage.Key, "Over Pipe");
            // a round 2 song on it from the Music page
            string song = Path.Combine(patch, @"battle\sound\bgm\BGM_D052.csb");
            File.Copy(Path.Combine(real, @"dlc\04_ae2\battle\sound\bgm\BGM_JUR.csb"), song);
            File.WriteAllText(Path.Combine(patch, "ex_more_stuff_music.json"), "{\"format\":1,\"songs\":[{\"file\":\"BGM_D052.csb\",\"song\":\"x\",\"sha256\":\"" +
                Installer.Sha256(song) + "\",\"loopStart\":0,\"loopEnd\":1,\"backup\":null}]}");

            Installed record = Installer.Load(game).Single(r => r.Item.Key == stage.Key);
            Check(CodeChange.Problem(game, record, "CHN", 0) != null && CodeChange.Problem(game, record, "D05", 0) != null &&
                  CodeChange.Problem(game, record, "D5", 0) != null, "refuses a game code, its own code and a short one");
            Check(CodeChange.Problem(game, record, "E07", 0) == null, "E07 is free");
            string note = CodeChange.Apply(game, record, "E07", 0);
            Check(Names(stageZip).SequenceEqual(new[] { "BGM_E07.csb", "STG_E07.emz", "STG_E07.png", "STG_E07.tex.emz" }), "the zip's files are renamed: " + string.Join(" ", Names(stageZip)));
            byte[] inner = GameArt.Unpack(Entry(stageZip, "STG_E07.emz"));
            string text = Encoding.ASCII.GetString(inner);
            Check(text.Contains("E07_") && !text.Contains("D05_"), "the stage file inside is re-coded to E07");
            Check(!File.Exists(stageZip + ".was") && !File.Exists(stageZip + ".tmp"), "no leftovers beside the zip");
            Installed now = Installer.Load(game).SingleOrDefault();
            Check(now != null && now.Item.Code == "E07" && now.Shown == "Over Pipe" && now.Package == Path.GetFullPath(stageZip), "in the game under E07, its name kept");
            Check(File.Exists(Path.Combine(patch, @"battle\stage\STG_E07.emz")) && File.Exists(Path.Combine(patch, @"battle\sound\bgm\BGM_E07.csb")) &&
                  File.ReadAllText(Path.Combine(patch, @"battle\stage\STG_E07.txt")) == "Over Pipe", "its files and name file are E07's");
            Check(!File.Exists(Path.Combine(patch, @"battle\stage\STG_D05.emz")) && !File.Exists(Path.Combine(patch, @"battle\sound\bgm\BGM_D05.csb")) &&
                  !File.Exists(Path.Combine(patch, @"battle\stage\STG_D05.txt")), "D05's files are gone");
            Check(note == null && MusicBank.Load(game).ContainsKey("BGM_E072.csb") && File.Exists(Path.Combine(patch, @"battle\sound\bgm\BGM_E072.csb")) &&
                  !File.Exists(song), "the round 2 song moved with it");

            // what its zip holds, then deleting it for good (Package codes' Delete, minus the Recycle Bin)
            Check(Installer.PackageHolds(stageZip, now.Item) && !Installer.PackageHolds(stageZip, stage), "the zip holds E07 now, not D05");
            Installer.Purge(game, now.Item.Key);
            string left = MusicBank.RemoveCode(game, "E07");
            Check(left == null && Installer.Load(game).Count == 0 && Installer.Removed(game).Count == 0, "deleted: off both lists");
            Check(!File.Exists(Path.Combine(patch, @"battle\stage\STG_E07.emz")) && !File.Exists(Path.Combine(patch, @"battle\sound\bgm\BGM_E07.csb")) &&
                  !File.Exists(Path.Combine(patch, @"battle\sound\bgm\BGM_E072.csb")) && MusicBank.Load(game).Count == 0, "its files and its Music-page song are gone");
            Check(File.Exists(stageZip), "its zip is left to the caller");

            // a costume, switched off, to another slot
            string costumeZip = Path.Combine(packages, "Armor.zip");
            using (ZipArchive zip = ZipFile.Open(costumeZip, ZipArchiveMode.Create))
                foreach (string name in new[] { "JHA_75.obj.emo", "JHA_75.nml.emb", "JHA_75.png" }.Concat(Enumerable.Range(1, 10).SelectMany(c =>
                    new[] { "JHA_75_" + c.ToString("D2") + ".col.emb", "JHA_75_" + c.ToString("D2") + ".obj.emm" })))
                    using (Stream s = zip.CreateEntry(name).Open()) s.WriteByte(2);
            Item costume = Installer.Inspect(costumeZip);
            Installer.InstallPackage(game, costume, costumeZip, "file");
            Installer.Remove(game, costume.Key);
            // another one in slot 77
            string otherZip = Path.Combine(packages, "Other.zip");
            using (ZipArchive zip = ZipFile.Open(otherZip, ZipArchiveMode.Create))
                foreach (string name in new[] { "JHA_77.obj.emo", "JHA_77.nml.emb" }.Concat(Enumerable.Range(1, 10).SelectMany(c =>
                    new[] { "JHA_77_" + c.ToString("D2") + ".col.emb", "JHA_77_" + c.ToString("D2") + ".obj.emm" })))
                    using (Stream s = zip.CreateEntry(name).Open()) s.WriteByte(3);
            Installer.InstallPackage(game, Installer.Inspect(otherZip), otherZip, "file");

            Installed off = Installer.Removed(game).Single();
            Check(CodeChange.Problem(game, off, "", 77) != null && CodeChange.Problem(game, off, "", 7) != null, "refuses a slot taken and one under 8");
            CodeChange.Apply(game, off, "", 76);
            Check(Names(costumeZip).All(n => n.StartsWith("JHA_76")) && Names(costumeZip).Length == 23, "the costume zip's files are JHA_76's");
            Installed moved = Installer.Removed(game).Single();
            Check(moved.Item.Slot == 76 && !File.Exists(Path.Combine(patch, @"battle\chara\JHA\JHA_76.obj.emo")), "still switched off, now listed as slot 76");
            Installer.InstallPackage(game, moved.Item, moved.Package, "file");
            Check(File.Exists(Path.Combine(patch, @"battle\chara\JHA\JHA_76_10.col.emb")), "switching it on puts slot 76's files in");

            Directory.Delete(args[1], true);
            Console.WriteLine(failures == 0 ? "CodeTest passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
