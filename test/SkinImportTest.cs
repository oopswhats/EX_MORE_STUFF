// GameBanana skins into costume packages, without the internet: a real skin's files (Dark Hado Blanka ver1, replacing
// BLK_01 with a model and color 2) alone and as several versions (they become colors 1, 2 ...), a True Ryu-like layout
// (model variations, a numbered color set, materials in another folder), a mod in a code of its own with its pictures;
// the package built with the real game's files (read only); shared codes (given, own, first upload wins, personal);
// unpacking a zip holding a 7z (Windows' tar). Usage: SkinImportTest <real game folder> <skin zip> <scratch folder>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class SkinImportTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static byte[] Entry(string zip, string name)
        {
            using (ZipArchive z = ZipFile.OpenRead(zip))
            {
                var e = z.Entries.FirstOrDefault(x => x.Name == name);
                if (e == null) return null;
                using (Stream s = e.Open())
                using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
            }
        }

        static string GameFile(string game, string name)
        {
            foreach (string root in new[] { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" })
            {
                string path = Path.Combine(game, root, "battle", "chara", name.Substring(0, 3), name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        static bool Same(byte[] a, byte[] b) { return a != null && b != null && a.SequenceEqual(b); }

        static KeyValuePair<string, byte[]> F(string path, byte[] data) { return new KeyValuePair<string, byte[]>(path, data); }

        static void Main(string[] args)
        {
            string game = args[0], skin = args[1], scratch = args[2];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);

            Console.WriteLine("A real skin, one version");
            var files = GameBanana.Unpack(skin, Path.Combine(scratch, "work")).Select(f => F("ver1/" + f.Key, f.Value)).ToList();
            byte[] model = files.First(f => f.Key.EndsWith("BLK_01.obj.emo")).Value, color2 = files.First(f => f.Key.EndsWith("BLK_01_02.col.emb")).Value;
            var found = SkinImport.Find(files);
            Check(found.Count == 1 && found[0].Fighter == "BLK" && found[0].Number == 1, "one costume: Blanka's costume 1");
            var c = found[0];
            Check(Same(c.Parts[".obj.emo"], model), "its model");
            Check(Same(c.Colors[1].Col, color2) && c.Colors[0] == c.Colors[1], "color 2 is its own, and color 1 shows it too");
            Check(c.Colors.Skip(2).All(x => x == null), "colors 3-10 are the game's");
            string zip = Path.Combine(scratch, "one.zip");
            SkinImport.Build(game, c, 8, null, zip);
            using (ZipArchive z = ZipFile.OpenRead(zip))
            {
                var names = z.Entries.Select(e => e.Name).ToList();
                Check(names.Count == 32 && names.All(n => n.StartsWith("BLK_08")), "32 files, all BLK_08 (got " + names.Count + ")");
                Check(names.Count(n => n.EndsWith(".png")) == 8 && !names.Contains("BLK_08_01.png") && !names.Contains("BLK_08_02.png"),
                      "a picture for each of the 8 colors it doesn't bring, none for its own");
            }
            Check(Same(Entry(zip, "BLK_08_03.png"), SkinImport.Unfinished), "color 3's picture: unfinished");
            using (var picture = System.Drawing.Image.FromStream(new MemoryStream(SkinImport.Unfinished)))
                Check(picture.Width == 256 && picture.Height == 384 && SkinImport.Unfinished.Length < 20000,
                      "the unfinished picture: 256 x 384, small (" + SkinImport.Unfinished.Length + " bytes)");
            string modelZip = Path.Combine(scratch, "model.zip");
            SkinImport.Build(game, SkinImport.Find(new List<KeyValuePair<string, byte[]>> { F("m/BLK_01.obj.emo", model) }).Single(), 8, null, modelZip);
            using (ZipArchive z = ZipFile.OpenRead(modelZip))
                Check(!z.Entries.Any(e => e.Name.EndsWith(".png")), "a model alone, made for the game's colors: no unfinished pictures");
            Check(Same(Entry(zip, "BLK_08.obj.emo"), model), "the skin's model");
            Check(Same(Entry(zip, "BLK_08.nml.emb"), File.ReadAllBytes(GameFile(game, "BLK_01.nml.emb"))), "the game's normals");
            Check(Same(Entry(zip, "BLK_08_01.col.emb"), color2) && Same(Entry(zip, "BLK_08_02.col.emb"), color2), "colors 1 and 2: the skin's color");
            Check(Same(Entry(zip, "BLK_08_01.obj.emm"), File.ReadAllBytes(GameFile(game, "BLK_01_02.obj.emm"))), "color 1's material: the game's for color 2, which it was made over");
            Check(Same(Entry(zip, "BLK_08_05.col.emb"), File.ReadAllBytes(GameFile(game, "BLK_01_05.col.emb"))), "color 5: the game's");

            Console.WriteLine("Several versions become colors");
            var v2 = (byte[])color2.Clone(); v2[v2.Length - 1] ^= 1;
            var v3 = (byte[])color2.Clone(); v3[v3.Length - 1] ^= 2;
            var versions = new List<KeyValuePair<string, byte[]>>(files)
            {
                F("ver2/BLK_01.obj.emo", model), F("ver2/BLK_01_02.col.emb", v2),
                F("ver3/BLK_01.obj.emo", model), F("ver3/BLK_01_02.col.emb", v3),
            };
            c = SkinImport.Find(versions).Single();
            Check(c.Versions == 3 && !c.ModelsDiffer, "3 versions, one model");
            Check(Same(c.Colors[0].Col, color2) && Same(c.Colors[1].Col, v2) && Same(c.Colors[2].Col, v3) && c.Colors[3] == null, "colors 1-3: versions 1-3, 4-10 the game's");
            zip = Path.Combine(scratch, "versions.zip");
            SkinImport.Build(game, c, 8, null, zip);
            Check(Same(Entry(zip, "BLK_08_03.col.emb"), v3) && Same(Entry(zip, "BLK_08_03.obj.emm"), File.ReadAllBytes(GameFile(game, "BLK_01_02.obj.emm"))), "color 3: version 3 with the game's color-2 material");
            var other = (byte[])model.Clone(); other[other.Length - 1] ^= 1;
            versions[versions.Count - 2] = F("ver3/BLK_01.obj.emo", other);
            Check(SkinImport.Find(versions).Single().ModelsDiffer, "a version with another model is noticed");

            Console.WriteLine("A True Ryu-like layout");
            var ryu = new List<KeyValuePair<string, byte[]>>
            {
                F("t/Ryu/Model Variations/B/RYU_01.obj.emo", new byte[] { 2 }), F("t/Ryu/Model Variations/A/RYU_01.obj.emo", new byte[] { 1 }),
                F("t/Ryu/RYU_01.nml.emb", new byte[] { 9 }), F("t/Ryu/Colors/Full Gi/RYU_01_Alpha.col.emb", new byte[] { 7 }),
            };
            for (int n = 1; n <= 10; n++) { ryu.Add(F("t/Ryu/Colors/RYU_01_" + n.ToString("D2") + ".col.emb", new byte[] { 100, (byte)n })); ryu.Add(F("t/Ryu/RYU_01_" + n.ToString("D2") + ".obj.emm", new byte[] { 200, (byte)n })); }
            c = SkinImport.Find(ryu).Single();
            Check(Same(c.Parts[".obj.emo"], new byte[] { 1 }) && Same(c.Parts[".nml.emb"], new byte[] { 9 }), "the first model variation, its normals");
            Check(Enumerable.Range(0, 10).All(i => c.Colors[i] != null && c.Colors[i].Col[1] == i + 1 && c.Colors[i].Emm[1] == i + 1), "its 10 numbered colors with their materials from the other folder");

            Console.WriteLine("A mod in a code of its own");
            var own = new List<KeyValuePair<string, byte[]>> { F("m/BLK_23.obj.emo", model), F("m/BLK_23_01.col.emb", color2), F("m/BLK_23.png", new byte[] { 5 }), F("m/BLK_23_01.png", new byte[] { 6 }) };
            c = SkinImport.Find(own).Single();
            Check(c.OwnCode && c.Number == 23 && c.Pictures.Count == 2, "its code 23 and its pictures");
            zip = Path.Combine(scratch, "own.zip");
            SkinImport.Build(game, c, 23, new byte[] { 1, 2, 3 }, zip);
            Check(Same(Entry(zip, "BLK_23.png"), new byte[] { 5 }) && Same(Entry(zip, "BLK_23_01.png"), new byte[] { 6 }), "its own pictures, not the mod page's");
            Check(Same(Entry(zip, "BLK_23_02.col.emb"), File.ReadAllBytes(GameFile(game, "BLK_01_02.col.emb"))), "the colors it doesn't bring: the game's costume 1");
            Check(Same(Entry(zip, "BLK_23_02.png"), SkinImport.Unfinished), "and their pictures say unfinished");

            Console.WriteLine("Shared codes");
            Func<long, long, string[], GameBanana.Mod> mod = (id, added, names) => new GameBanana.Mod
            {
                Id = id, Added = added, Category = "Skins",
                Files = new List<GameBanana.ModFile> { new GameBanana.ModFile { Id = id * 10, Name = "x.zip", Clean = true, Contents = names.ToList() } },
            };
            var hado = mod(680656, 100, new[] { "BLK_01.obj.emo", "BLK_01_02.col.emb" });
            var first = mod(1, 200, new[] { "BLK_23.obj.emo" });
            var later = mod(2, 300, new[] { "BLK_23.obj.emo", "BLK_24.obj.emo" });
            var newer = mod(3, 400, new[] { "RYU_01.obj.emo" });
            var holders = SharedCodes.Holders(new[] { later, hado, first, newer });
            Check(SharedCodes.Slot(hado, "BLK", 1, holders) == 8, "Dark Hado Blanka: its given code, Blanka 08");
            Check(SharedCodes.Slot(first, "BLK", 23, holders) == 23, "a mod's own code: its own");
            Check(SharedCodes.Slot(later, "BLK", 23, holders) == 0 && SharedCodes.Slot(later, "BLK", 24, holders) == 24, "the same code later: personal; its other code: its own");
            Check(SharedCodes.Slot(newer, "RYU", 1, holders) == 0, "a new skin replacing a game costume: personal");
            var taken = mod(4, 500, new[] { "KEN_85.obj.emo" });
            Check(SharedCodes.Slot(taken, "KEN", 85, SharedCodes.Holders(new[] { taken })) == 0, "a mod made in slot 85 (80-99 are taken): no shared seat");

            Console.WriteLine("A zip holding a 7z");
            string inner = Path.Combine(scratch, "pack"), sevenZip = Path.Combine(scratch, "RYU.7z"), outer = Path.Combine(scratch, "outer.zip");
            Directory.CreateDirectory(Path.Combine(inner, "RYU"));
            File.WriteAllBytes(Path.Combine(inner, "RYU", "RYU_01.obj.emo"), model);
            File.WriteAllBytes(Path.Combine(inner, "RYU", "setup.exe"), new byte[] { 77, 90 });
            File.WriteAllBytes(Path.Combine(inner, "RYU", "RYU_01_02.COL.EMB"), color2);
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            using (var p = Process.Start(new ProcessStartInfo(tar, "--format 7zip -cf \"" + sevenZip + "\" -C \"" + inner + "\" RYU") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit();
            using (ZipArchive z = ZipFile.Open(outer, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(sevenZip, "RYU.7z");
                z.CreateEntry("readme.txt");
                z.CreateEntry("tool/run.bat");
            }
            var unpacked = GameBanana.Unpack(outer, Path.Combine(scratch, "work"));
            Check(unpacked.Any(f => f.Key == "RYU.7z/RYU/RYU_01.obj.emo" && Same(f.Value, model)), "its model, as RYU.7z/RYU/RYU_01.obj.emo");
            Check(unpacked.Any(f => f.Key == "RYU.7z/RYU/RYU_01_02.COL.EMB"), "a color in capitals too");
            Check(unpacked.Count == 2, "nothing else: no program, script or text file is taken out (got " + string.Join(", ", unpacked.Select(f => f.Key)) + ")");
            Check(!Directory.Exists(Path.Combine(scratch, "work", "x1")), "the work folder is cleaned up");

            Console.WriteLine("A downloaded mod file with a stage and a skin, into a made-up game folder");
            string fake = Path.Combine(scratch, "game");
            foreach (string name in new[] { "BLK_01.nml.emb", "BLK_01.shd.emo", "BLK_01.bsr" }.Concat(Enumerable.Range(1, 10).SelectMany(n => new[] { "BLK_01_" + n.ToString("D2") + ".col.emb", "BLK_01_" + n.ToString("D2") + ".obj.emm" })))
            {
                string from = GameFile(game, name);
                if (from == null) continue;
                string to = Path.Combine(fake, "resource", "battle", "chara", "BLK", name);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to);
            }
            string stageEmz = null, stageTex = null;
            foreach (string root in new[] { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" })
            {
                string at = Path.Combine(game, root, "battle", "stage");
                if (stageEmz == null && File.Exists(Path.Combine(at, "STG_TRN.emz"))) stageEmz = Path.Combine(at, "STG_TRN.emz");
                if (stageTex == null && File.Exists(Path.Combine(at, "STG_TRN.tex.emz"))) stageTex = Path.Combine(at, "STG_TRN.tex.emz");
            }
            Directory.CreateDirectory(Path.Combine(fake, "resource", "battle", "stage"));
            File.Copy(stageTex, Path.Combine(fake, "resource", "battle", "stage", "STG_TRN.tex.emz"));
            Directory.CreateDirectory(Path.Combine(fake, "patch_ae2_tu3"));
            File.WriteAllBytes(Path.Combine(fake, "SSFIV.exe"), new byte[0]);
            string modZip = Path.Combine(scratch, "my_stage_mod.zip");
            using (ZipArchive z = ZipFile.Open(modZip, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(stageEmz, "Neon Training/STG_TRN.emz");             // a stage mod bringing its model only
                using (Stream s = z.CreateEntry("Neon Training/BGM_TRN.csb").Open()) s.Write(new byte[] { 1, 2, 3 }, 0, 3);
                using (Stream s = z.CreateEntry("Skin/BLK_01.obj.emo").Open()) s.Write(model, 0, model.Length);
                using (Stream s = z.CreateEntry("Skin/BLK_01_02.col.emb").Open()) s.Write(color2, 0, color2.Length);
                z.CreateEntry("Install me.exe");
            }
            var result = ModFile.Install(fake, modZip, null, null, "", null, null, text => { });
            Check(result.Problems.Count == 0, "no problems" + (result.Problems.Count > 0 ? ": " + string.Join("; ", result.Problems) : ""));
            Check(result.Into.SequenceEqual(new[] { "Blanka 84", "stage C80" }), "in: " + string.Join(", ", result.Into));
            string stageDir = Path.Combine(fake, "patch_ae2_tu3", "battle", "stage");
            byte[] built = File.Exists(Path.Combine(stageDir, "STG_C80.emz")) ? File.ReadAllBytes(Path.Combine(stageDir, "STG_C80.emz")) : null;
            Check(built != null && File.Exists(Path.Combine(stageDir, "STG_C80.tex.emz")), "STG_C80.emz and its textures (from the game)");
            Check(built != null && GameArt.ReadContainer(GameArt.Unpack(built)).All(e => !e.Key.StartsWith("TRN_", StringComparison.OrdinalIgnoreCase)), "re-coded inside: nothing named TRN_ left");
            Check(Same(File.ReadAllBytes(Path.Combine(fake, "patch_ae2_tu3", "battle", "sound", "bgm", "BGM_C80.csb")), new byte[] { 1, 2, 3 }), "its music as BGM_C80.csb");
            Check(File.Exists(Path.Combine(fake, "patch_ae2_tu3", "battle", "chara", "BLK", "BLK_84.obj.emo")), "the skin as Blanka 84 (a free seat from 84 down)");
            var records = Installer.Load(fake);
            Check(records.Count == 2 && records.All(r => r.Source == "file" && File.Exists(r.Package)), "two of your own packages, each with its zip");
            Check(records.Any(r => r.Item.IsStage && r.Shown == "my stage mod"), "named after the file");
            Check(!Directory.EnumerateFiles(fake, "*.exe", SearchOption.AllDirectories).Any(f => !f.EndsWith("SSFIV.exe")), "the .exe inside never came out");
            result = ModFile.Install(fake, modZip, null, null, "", null, null, text => { });
            Check(result.Into.SequenceEqual(new[] { "Blanka 84", "stage C80" }) && Installer.Load(fake).Count == 2, "installing it again updates the same slot and code");

            Directory.Delete(scratch, true);
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
