// Updates, the custom stage check and the program-version entry, in a made-up game folder; also renders a card
// with an update tag. Usage: UpdateTest <real game folder> <scratch folder> <card.png>   (with all of src\)
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
                    byte[] data = Container(code + "_SetupObj.lua", Encoding.ASCII.GetBytes("\0\0\0Setup\0SetFloorHeight\0"));
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
            return new Item { Id = "stage-" + slot, Type = "stage", Fighter = "", Slot = slot, Name = "Test", Version = version, Sha256 = Installer.Sha256(zip), Size = new FileInfo(zip).Length };
        }

        static void Main(string[] args)
        {
            string real = args[0], game = args[1];
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

            Console.WriteLine("Custom stage check");
            string bad = Package(real, packages, 21, null, null);
            try { Installer.InstallPackage(game, StageItem(21, "1.0", bad), bad, "catalog"); Check(false, "refused"); }
            catch (InvalidDataException ex) { Check(ex.Message.Contains("SetFloorHeight"), "refused: " + ex.Message); }
            Check(!File.Exists(Path.Combine(game, "patch_ae2_tu3", "battle", "stage", "STG_C21.emz")), "nothing written");

            Console.WriteLine("Program version");
            string catalog = Path.Combine(game, "catalog.json");
            File.WriteAllText(catalog, "{\"format\":1,\"program\":{\"version\":\"0.2\",\"page\":\"https://example.com/ex-more-stuff\"},\"items\":[]}");
            Catalog.Load(catalog).Wait();
            Check(Catalog.ProgramVersion != null && Catalog.ProgramVersion > new Version(0, 1, 0, 0), "0.2 is newer than 0.1.0.0 (" + Catalog.ProgramVersion + ")");
            Check(Catalog.ProgramPage == "https://example.com/ex-more-stuff", "page read");

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
