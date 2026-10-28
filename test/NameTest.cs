// Names in EMBER's menus: install writes <prefix>.txt, Rename changes it, an update keeps it, Remove deletes it.
// In a made-up game folder. Usage: NameTest <scratch folder>   (with all of src\, -main:ExMoreStuff.NameTest)
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    static class NameTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static string Zip(string folder, string name, params string[] entries)
        {
            string path = Path.Combine(folder, name + ".zip");
            if (File.Exists(path)) File.Delete(path);
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (string entry in entries)
                    using (Stream s = zip.CreateEntry(entry).Open()) s.WriteByte(1);
            return path;
        }

        static void Main(string[] args)
        {
            string game = args[0];
            if (Directory.Exists(game)) Directory.Delete(game, true);
            Directory.CreateDirectory(Path.Combine(game, "patch_ae2_tu3"));
            var files = new[] { "JHA_71.obj.emo", "JHA_71.nml.emb" }.Concat(Enumerable.Range(1, 10).SelectMany(c =>
                new[] { "JHA_71_" + c.ToString("D2") + ".col.emb", "JHA_71_" + c.ToString("D2") + ".obj.emm" })).ToArray();
            string package = Zip(game, "Jyuratodus Armor", files);
            Item item = Installer.Inspect(package);
            string nameFile = Path.Combine(game, "patch_ae2_tu3", "battle", "chara", "JHA", "JHA_71.txt");

            Installer.InstallPackage(game, item, package, "file");
            Check(File.ReadAllText(nameFile) == "Jyuratodus Armor", "install names it after the package");
            Check(File.ReadAllBytes(nameFile)[0] == (byte)'J', "no byte-order mark");
            Installer.Rename(game, item.Key, "  Monster Hunter\t ");
            Check(File.ReadAllText(nameFile) == "Monster Hunter" && Installer.Load(game)[0].Shown == "Monster Hunter", "rename (trimmed, control characters dropped)");
            Installer.InstallPackage(game, Installer.Inspect(package), package, "file");
            Check(File.ReadAllText(nameFile) == "Monster Hunter", "an update keeps the player's name");
            Check(Installer.Load(game)[0].Files.Count(f => f.EndsWith("JHA_71.txt")) == 1, "the name file is listed once");
            Installer.Rename(game, item.Key, "");
            Check(!File.Exists(nameFile) && Installer.Load(game)[0].Shown == "", "an empty name removes the file");
            Installer.Rename(game, item.Key, "Monster Hunter");
            // sounds like (user 2026-10-27): the game costume it plays as in rollback games, for its foley
            string foley = Path.Combine(game, "patch_ae2_tu3", "battle", "chara", "JHA", "JHA_71.foley.txt");
            Check(Installer.SoundsLike(game, item) == 0 && !File.Exists(foley), "no sounds like at first");
            Installer.SetSoundsLike(game, item.Key, 3);
            Check(File.ReadAllText(foley) == "3" && Installer.SoundsLike(game, item) == 3 && Installer.Load(game)[0].Files.Count(f => f.EndsWith("JHA_71.foley.txt")) == 1,
                  "sounds like Alternate 2: JHA_71.foley.txt says 3, listed with its files");
            Installer.InstallPackage(game, Installer.Inspect(package), package, "file");
            Check(Installer.SoundsLike(game, item) == 3, "an update keeps it");
            Installer.SetSoundsLike(game, item.Key, 0);
            Check(!File.Exists(foley) && Installer.SoundsLike(game, item) == 0, "as usual removes the file");
            Installer.SetSoundsLike(game, item.Key, 2);
            Installer.Remove(game, item.Key);
            Check(!File.Exists(foley), "remove takes it too");
            string extra = Catalog.FirstColor(71).ToString("D2");   // 11 once Ember takes a custom costume's 11-29, 30 until then
            string withFoley = Zip(game, "With foley", files.Concat(new[] { "JHA_71.foley.txt", "JHA_71_" + extra + ".col.emb", "JHA_71_" + extra + ".obj.emm" }).ToArray());
            Check(Installer.Package(withFoley) != null, "a package may carry its own .foley.txt and a custom costume's color " + extra);
            Installer.InstallPackage(game, item, package, "file");
            Check(Installer.SoundsLike(game, item) == 2 && File.ReadAllText(foley) == "2", "switched back on, it still sounds like Alternate 1");
            Installer.Rename(game, item.Key, "Monster Hunter");
            Installer.Remove(game, item.Key);
            Check(!File.Exists(nameFile) && !Directory.Exists(Path.GetDirectoryName(nameFile)), "remove takes the name file and the folder");

            // A player's own package stays listed when removed, and comes back from its zip with its name.
            Installed off = Installer.Removed(game).SingleOrDefault();
            Check(off != null && off.Item.Key == item.Key && off.Package == Path.GetFullPath(package) && off.Shown == "Monster Hunter" && off.Files.Count == 0,
                  "a removed package of the player's stays listed, with its zip and name");
            Check(Installer.PackagePicture(package, item) == null, "no picture in this zip");
            Installer.InstallPackage(game, off.Item, off.Package, "file");
            Check(File.ReadAllText(nameFile) == "Monster Hunter" && Installer.Removed(game).Count == 0 && Installer.Load(game).Count == 1,
                  "switched back on from its zip, name kept, off the removed list");
            Installer.Remove(game, item.Key);
            Installer.Forget(game, item.Key);
            Check(Installer.Removed(game).Count == 0 && Installer.Load(game).Count == 0, "forget takes it off the list");

            Check(Installer.CleanName("Monster_Hunter-2.0") == "Monster_Hunter-2.0", "English letters, digits, spaces and _ - . stay");
            Check(Installer.CleanName("Café Ryu's #1!") == "Cafe Ryus 1" && Installer.CleanName("A & B") == "A B", "an accented letter becomes its plain one, anything else is dropped, spaces collapse");
            Check(Installer.CleanName("Juri gótica") == "Juri gotica", "Juri gótica: Juri gotica (it was Juri gtica)");
            DateTime? built = BugReport.Built();
            Check(built != null && Math.Abs((built.Value - System.IO.File.GetLastWriteTime(typeof(BugReport).Assembly.Location)).TotalMinutes) < 5,
                  "this copy's build date read from its header: " + built);
            var day = new DateTime(2026, 10, 13);
            Check(!BugReport.TooOld(day, day.AddYears(2).AddDays(-1)) && BugReport.TooOld(day, day.AddYears(2).AddDays(1)) && !BugReport.TooOld(null, day.AddYears(10)),
                  "Report a bug: shown until 2 years after the build, hidden after; shown when the build date can't be read");
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Check(BugReport.Scrub("its zip isn't at " + profile + "\\Downloads\\mod.zip any more") == "its zip isn't at %USERPROFILE%\\Downloads\\mod.zip any more" &&
                  BugReport.Scrub("not at D:\\Users\\Someone Else\\Mods\\x.zip; or X:/Users/name/y.zip") == "not at D:\\Users\\(user)\\Mods\\x.zip; or X:/Users/(user)/y.zip" &&
                  BugReport.Scrub("X:\\Games\\SF4 MODDING\\EXMS\\Mods") == "X:\\Games\\SF4 MODDING\\EXMS\\Mods", "a bug report's error message: no user names in its folders, other folders as they are");
            Check(Installer.CleanName("日本語 \U0001F600") == "", "a name of only other characters is no name");
            Check(Installer.CleanName(new string('a', 45)) == new string('a', 40), "names are cut to 40 characters");

            // Records from before names (no "shown"): no file until renamed or updated.
            Installer.InstallPackage(game, item, package, "file");
            string list = Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff.json");
            File.WriteAllText(list, File.ReadAllText(list).Replace("\"shown\":\"Jyuratodus Armor\"", "\"shown\":null"));
            Check(Installer.Load(game)[0].Shown == null, "an old record has no name");
            Installer.InstallPackage(game, item, package, "file");
            Check(File.ReadAllText(nameFile) == "Jyuratodus Armor", "updating an old record names it");

            Directory.Delete(game, true);
            Console.WriteLine(failures == 0 ? "NameTest passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
