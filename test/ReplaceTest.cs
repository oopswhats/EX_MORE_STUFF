// Replaces, swaps and restores stages in a made-up game folder (copies of the real Training Stage and Pit Stop 109
// in resource, and a stand-in "player's edit" of Pit Stop 109 in the patch folder), checking every step.
// Usage: ReplaceTest <real game folder> <scratch folder>   (built with all of src\ except Program's Main)
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    static class ReplaceTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static string[] Names(string path) { return GameArt.ReadContainer(GameArt.Unpack(File.ReadAllBytes(path))).Select(e => e.Key).ToArray(); }

        static bool LuaHas(string path, string text)
        {
            return GameArt.ReadContainer(GameArt.Unpack(File.ReadAllBytes(path)))
                .Where(e => e.Key.EndsWith(".lua")).Any(e => Encoding.ASCII.GetString(e.Value).Contains(text));
        }

        static void Main(string[] args)
        {
            string real = args[0], game = args[1];
            if (Directory.Exists(game)) Directory.Delete(game, true);
            string resource = Path.Combine(game, "resource", "battle", "stage"), patch = Path.Combine(game, "patch_ae2_tu3", "battle", "stage");
            Directory.CreateDirectory(resource);
            Directory.CreateDirectory(patch);
            foreach (string code in new[] { "TRN", "DET" })
                foreach (string suffix in StagePack.Suffixes)
                    File.Copy(Path.Combine(real, "resource", "battle", "stage", "STG_" + code + suffix), Path.Combine(resource, "STG_" + code + suffix));
            string edit = Path.Combine(patch, "STG_DET.emz");
            File.WriteAllText(edit, "the player's own Pit Stop 109 edit");
            Func<string, string> name = Stages.Name;

            Console.WriteLine("Pit Stop 109 -> Training Stage");
            Check(Replacements.Problem(game, "DET", "TRN", name) == null, "allowed");
            Replacements.Replace(game, "DET", "TRN");
            string backup = Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_backup", "battle", "stage", "STG_DET.emz");
            Check(File.Exists(backup) && File.ReadAllText(backup) == "the player's own Pit Stop 109 edit", "the player's edit moved to the backup folder");
            string[] names = Names(edit);
            Check(names.Any(n => n.StartsWith("DET_")) && !names.Any(n => n.StartsWith("TRN_")), "entries renamed TRN_ -> DET_ (" + names.Length + ": " + string.Join(", ", names.Take(4)) + " ...)");
            Check(LuaHas(edit, "DET_BAS") && !LuaHas(edit, "TRN_BAS"), "scripts load DET_BAS, not TRN_BAS");
            Check(File.Exists(Path.Combine(patch, "STG_DET.tex.emz")), "textures written too");
            Check(Names(Path.Combine(patch, "STG_DET.tex.emz")).Length == Names(Path.Combine(resource, "STG_TRN.tex.emz")).Length, "textures: same entries as Training Stage's");

            Console.WriteLine("Training Stage -> Pit Stop 109 (a swap: from the game's own Pit Stop 109, not the replaced one)");
            Replacements.Replace(game, "TRN", "DET");
            string trn = Path.Combine(patch, "STG_TRN.emz");
            Check(Names(trn).Length == Names(Path.Combine(resource, "STG_DET.emz")).Length, "Training Stage now holds Pit Stop 109's " + Names(trn).Length + " entries");
            Check(Names(trn).Any(n => n.StartsWith("TRN_")) && !Names(trn).Any(n => n.StartsWith("DET_")), "renamed DET_ -> TRN_");
            Check(Replacements.Load(game).Count == 2, "two replacements recorded");
            Check(GameArt.Unpack(File.ReadAllBytes(trn)).SequenceEqual(GameArt.Unpack(StagePack.Recode(File.ReadAllBytes(Path.Combine(resource, "STG_DET.emz")), "DET", "TRN"))), "rebuilt from resource");

            Console.WriteLine("Restore both");
            Check(Replacements.Restore(game, "DET") == null, "Pit Stop 109 restored");
            Check(File.ReadAllText(edit) == "the player's own Pit Stop 109 edit", "the player's edit is back");
            Check(!File.Exists(Path.Combine(patch, "STG_DET.tex.emz")), "converted textures removed");
            Check(Replacements.Restore(game, "TRN") == null, "Training Stage restored");
            Check(!File.Exists(trn) && !File.Exists(Path.Combine(patch, "STG_TRN.tex.emz")), "Training Stage's converted files removed");
            Check(!Directory.Exists(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_backup")), "backup folder gone");
            Check(!File.Exists(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_stages.json")), "list gone");

            Console.WriteLine("A file changed after replacing is left alone");
            Replacements.Replace(game, "DET", "TRN");
            File.WriteAllText(Path.Combine(patch, "STG_DET.tex.emz"), "a newer edit");
            string note = Replacements.Restore(game, "DET");
            Check(note != null && File.ReadAllText(Path.Combine(patch, "STG_DET.tex.emz")) == "a newer edit", "kept: " + note);
            Check(File.ReadAllText(edit) == "the player's own Pit Stop 109 edit", "the untouched file's backup came back");

            Console.WriteLine("A docked stage's files in Training Stage's place (the stage preview)");
            File.Delete(Path.Combine(patch, "STG_DET.tex.emz"));
            var docked = StagePack.Suffixes.ToDictionary(s => s, s => File.ReadAllBytes(Path.Combine(resource, "STG_DET" + s)));
            Replacements.ReplaceWith(game, "TRN", "docked", "DET", docked);
            Check(GameArt.Unpack(File.ReadAllBytes(trn)).SequenceEqual(GameArt.Unpack(StagePack.Recode(docked[".emz"], "DET", "TRN"))), "its files, renamed DET_ -> TRN_");
            Check(Replacements.Load(game).Any(r => r.Stage == "TRN" && r.Source == "docked"), "listed as docked");
            Check(Replacements.Restore(game, "TRN") == null && !File.Exists(trn), "and taken back out");

            Console.WriteLine("With its own music (user 2026-10-15: the preview played Training Stage's)");
            string bgm = Path.Combine(Game.PatchFolder(game), "battle", "sound", "bgm", "BGM_TRN.csb");
            Directory.CreateDirectory(Path.GetDirectoryName(bgm));
            File.WriteAllText(bgm, "a song from the Music page");
            Replacements.ReplaceWith(game, "TRN", "docked", "DET", docked, Encoding.ASCII.GetBytes("its own music"));
            Check(File.ReadAllText(bgm) == "its own music", "its music in Training Stage's music's place");
            Check(Replacements.Restore(game, "TRN") == null && File.ReadAllText(bgm) == "a song from the Music page", "taken out, what was there back");
            File.Delete(bgm);

            Console.WriteLine("A custom stage in a game stage's place brings its own music (user 2026-10-30)");
            foreach (string suffix in StagePack.Suffixes)
                File.WriteAllBytes(Path.Combine(patch, "STG_U07" + suffix), StagePack.Recode(File.ReadAllBytes(Path.Combine(resource, "STG_DET" + suffix)), "DET", "U07"));
            string music = Path.GetDirectoryName(bgm);
            string own = Path.Combine(music, "BGM_U07.csb"), own3 = Path.Combine(music, "BGM_U073.csb");
            string det = Path.Combine(music, "BGM_DET.csb"), det2 = Path.Combine(music, "BGM_DET2.csb"), det3 = Path.Combine(music, "BGM_DET3.csb");
            File.WriteAllText(own, "U07's own music");
            File.WriteAllText(own3, "U07's round 3");
            File.WriteAllText(det, "a song on Pit Stop 109 from the Music page");
            Replacements.Replace(game, "DET", "U07");
            Check(File.ReadAllText(det) == "U07's own music", "BGM_U07.csb plays as BGM_DET.csb");
            Check(File.ReadAllText(det3) == "U07's round 3" && !File.Exists(det2), "its round 3 too; no round 2 of its own, none made");
            Check(Replacements.Restore(game, "DET") == null && File.ReadAllText(det) == "a song on Pit Stop 109 from the Music page" && !File.Exists(det3) && File.Exists(own),
                  "taken out after: the song that was on Pit Stop 109 back, its own stays");
            File.Delete(det);
            File.Delete(own);
            File.Delete(own3);
            Replacements.Replace(game, "DET", "U07");
            Check(!File.Exists(det), "a custom stage without music: Pit Stop 109's own music stays");
            Replacements.Restore(game, "DET");
            foreach (string suffix in StagePack.Suffixes) File.Delete(Path.Combine(patch, "STG_U07" + suffix));

            Console.WriteLine("Beautiful Bay");
            Directory.Delete(game, true);
            Directory.CreateDirectory(Path.Combine(game, "resource", "battle", "stage"));
            foreach (string suffix in StagePack.Suffixes)
                File.Copy(Path.Combine(real, "resource", "battle", "stage", "STG_VIE" + suffix), Path.Combine(game, "resource", "battle", "stage", "STG_VIE" + suffix));
            Check(Replacements.Problem(game, "VIE", null, name) == null, "can be replaced (its rocking deck is only a look)");
            Directory.Delete(game, true);
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        }
    }
}
