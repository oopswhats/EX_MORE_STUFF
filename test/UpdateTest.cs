// Updates, the custom stage check and the program-version entry, in a made-up game folder; also renders a card
// with an update tag, WRITTEN to the third argument (a new .png, never one of the program's pictures).
// Usage: UpdateTest <real game folder> <scratch folder> <card picture to write, .png>   (with all of src\)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExMoreStuff
{
    static class UpdateTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        // A custom stage package: the given game stage's files as C<nn>, or a made-up stage holding one script.
        static string Package(string real, string folder, int slot, string from, string script)
        {
            string code = "C" + slot.ToString("D2"), zipPath = Path.Combine(folder, code + "_" + (from ?? "script") + ".zip");
            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                if (from != null)
                    foreach (string suffix in StagePack.Suffixes)
                    {
                        byte[] data = StagePack.Recode(File.ReadAllBytes(Path.Combine(real, "resource", StagePack.RelativePath(from, suffix))), from, code);
                        using (Stream s = zip.CreateEntry("STG_" + code + suffix).Open()) s.Write(data, 0, data.Length);
                    }
                else
                {
                    byte[] data = Container(code + "_SetupObj.lua", Encoding.ASCII.GetBytes("\0\0\0Setup\0"));
                    using (Stream s = zip.CreateEntry("STG_" + code + ".emz").Open()) s.Write(data, 0, data.Length);
                }
            }
            return zipPath;
        }

        // A one-entry #EMB container (uncompressed), laid out like the game's.
        static byte[] Container(string name, byte[] data)
        {
            var b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("#EMB")); b.AddRange(new byte[8]);
            b.AddRange(BitConverter.GetBytes(1)); b.AddRange(new byte[8]);
            b.AddRange(BitConverter.GetBytes(32)); b.AddRange(BitConverter.GetBytes(40));
            b.AddRange(new byte[12]);
            while (b.Count % 64 != 0) b.Add(0);
            int at = b.Count;
            b.AddRange(data);
            while (b.Count % 64 != 0) b.Add(0);
            int nameAt = b.Count;
            b.AddRange(Encoding.ASCII.GetBytes(name)); b.Add(0);
            var bytes = b.ToArray();
            BitConverter.GetBytes(at - 32).CopyTo(bytes, 32);
            BitConverter.GetBytes(data.Length).CopyTo(bytes, 36);
            BitConverter.GetBytes(nameAt).CopyTo(bytes, 40);
            return bytes;
        }

        static Item StageItem(int slot, string version, string zip)
        {
            return new Item { Id = "stage-" + slot, Type = "stage", Fighter = "", Code = "C" + slot.ToString("D2"), Name = "Test", Version = version, Sha256 = Installer.Sha256(zip), Size = new FileInfo(zip).Length };
        }

        static void Main(string[] args)
        {
            string real = args[0], game = args[1];
            if (!args[2].EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("the card picture is written to the third argument: give it a .png"); Environment.Exit(2); }
            if (Directory.Exists(game)) Directory.Delete(game, true);
            Directory.CreateDirectory(Path.Combine(game, "patch_ae2_tu3"));
            string packages = Path.Combine(game, "packages");
            Directory.CreateDirectory(packages);

            Console.WriteLine("Update");
            string v1 = Package(real, packages, 20, "TRN", null), v2 = Package(real, packages, 20, "DET", null);
            Item old = StageItem(20, "1.0", v1), latest = StageItem(20, "1.1", v2);
            Installer.InstallPackage(game, old, v1, "catalog");
            Installed record = Installer.Load(game).Single();
            Check(!Installer.Outdated(record, old), "the same package isn't an update");
            Check(Installer.Outdated(record, latest), "a changed package is an update");
            Installer.InstallPackage(game, latest, v2, "catalog");
            var after = Installer.Load(game);
            Check(after.Count == 1 && after[0].Item.Version == "1.1", "one record, version 1.1");
            string emz = Path.Combine(game, "patch_ae2_tu3", "battle", "stage", "STG_C20.emz");
            Check(GameArt.ReadContainer(GameArt.Unpack(File.ReadAllBytes(emz))).Count > 10, "the new files are in place (Pit Stop 109's entries)");
            Check(!Installer.Outdated(after[0], latest), "no update left");
            Check(!Installer.Outdated(new Installed { Item = old, Source = "file" }, latest), "a player's own package never shows an update");


            Console.WriteLine("Program version");
            string catalog = Path.Combine(game, "catalog.json");
            File.WriteAllText(catalog, "{\"format\":1,\"program\":{\"version\":\"0.2\",\"page\":\"https://example.com/ex-more-stuff\"},\"items\":[]}");
            Catalog.Load(catalog).Wait();
            Check(Catalog.ProgramVersion != null && Catalog.ProgramVersion > new Version(0, 1, 0, 0), "0.2 is newer than 0.1.0.0 (" + Catalog.ProgramVersion + ")");
            Check(Catalog.ProgramPage == "https://example.com/ex-more-stuff", "page read");

            Console.WriteLine("Stage codes");
            Check(Stages.IsCustomCode("C12") && Stages.IsCustomCode("D05") && Stages.IsCustomCode("XYZ"), "C12, D05, XYZ are custom codes");
            Check(!Stages.IsCustomCode("CHN") && !Stages.IsCustomCode("GAS") && !Stages.IsCustomCode("c12") && !Stages.IsCustomCode("AB"), "game codes, lower case and short codes aren't");
            Check(Stages.FallbackCode("C12") == "CNX" && Stages.FallbackCode("D12") == "CNX" && Stages.FallbackCode("C71") == "ELV" && Stages.FallbackCode("XYZ") == "AFX",
                "fallbacks: C12/D12 Run-down Back Alley, C71 Cosmic Elevator, XYZ Solar Eclipse");
            // A player's own D05 with music, made from Training Stage plus a copy of the JUR theme.
            string d05 = Path.Combine(packages, "Someone else's stage.zip");
            using (ZipArchive zip = ZipFile.Open(d05, ZipArchiveMode.Create))
            {
                foreach (string suffix in StagePack.Suffixes)
                {
                    byte[] data = StagePack.Recode(File.ReadAllBytes(Path.Combine(real, "resource", StagePack.RelativePath("TRN", suffix))), "TRN", "D05");
                    using (Stream s = zip.CreateEntry("STG_D05" + suffix).Open()) s.Write(data, 0, data.Length);
                }
                zip.CreateEntryFromFile(Path.Combine(real, @"dlc\04_ae2\battle\sound\bgm\BGM_JUR.csb"), "BGM_D05.csb");
            }
            Item own = Installer.Inspect(d05);
            Check(own != null && own.IsStage && own.Code == "D05" && own.Personal, "Add from file sees stage D05, a personal package");
            Installer.InstallPackage(game, own, d05, "file");
            string patchFolder = Path.Combine(game, "patch_ae2_tu3");
            Check(File.Exists(Path.Combine(patchFolder, @"battle\stage\STG_D05.emz")) && File.Exists(Path.Combine(patchFolder, @"battle\sound\bgm\BGM_D05.csb")), "stage in battle\\stage, music in battle\\sound\\bgm");
            // a song can't go over a custom stage's own music: removing or updating the stage would take it
            var d05Music = new MusicSlot { Code = "D05" };
            bool refused = false;
            try { MusicBank.Install(game, d05Music, new byte[] { 1 }, new InstalledSong()); } catch (InvalidOperationException) { refused = true; }
            Check(MusicBank.PackageOwning(game, d05Music) != null && refused &&
                  new FileInfo(Path.Combine(patchFolder, @"battle\sound\bgm\BGM_D05.csb")).Length > 1, "the Music page won't put a song over a stage package's music");
            Installer.Remove(game, own.Key);
            Check(!Directory.Exists(Path.Combine(patchFolder, @"battle\sound")), "removed: the music folder it made is gone too");
            string port = Path.Combine(packages, "port.zip");
            using (ZipArchive zip = ZipFile.Open(port, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(Path.Combine(real, @"resource\battle\stage\STG_TRN.emz"), "STG_CHN.emz");
            Check(Installer.Inspect(port) == null, "a package of a game stage's code (STG_CHN) isn't a custom stage");
            Check(Item.FromJson(new System.Collections.Generic.Dictionary<string, object> { { "type", "stage" }, { "slot", 71 } }).Code == "C71", "an old record with only number 71 is C71");

            Directory.Delete(game, true);

            using (var card = new ItemCard { Title = "Stage 20 - Test", Detail = "EX More Stuff test  ·  1.1", Note = "Others without it see Run-down Back Alley", Badge = "Update to 1.1" })
            using (var bitmap = new Bitmap(card.Width, card.Height))
            {
                card.BackColor = Theme.Base;
                card.Use.Checked = true;
                card.Picture = Stages.Picture("DET");
                card.DrawToBitmap(bitmap, new Rectangle(0, 0, card.Width, card.Height));
                bitmap.Save(args[2]);
            }
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        }
    }
}
