// A custom stage's song made on another stage's bank than its stand-in's (user 2026-11-17: Battle Fantasia - Ship, U15,
// its BGM_U15.csb on BRX's bank, U15's stand-in JPX: "the bank lacks the game's sound Synth/STAGE/JPX/s_bgm_00_wav"):
// a new song for it builds (MusicPanel.BuildFor). Usage: MusicOtherBankTest <real game> <that BGM_U15.csb> <scratch>
// (a made-up game in scratch with the real BGM_JPX.csb and BGM_BRX.csb). build_test.bat MusicOtherBankTest
using System;
using System.Collections.Generic;
using System.IO;

namespace ExMoreStuff
{
    static class MusicOtherBankTest
    {
        static void Main(string[] args)
        {
            string real = args[0], u15 = args[1], game = Path.Combine(args[2], "game");
            string bgm = Path.Combine(game, "resource", MusicBank.Folder), patch = MusicBank.PatchFile(game, "BGM_U15.csb");
            Directory.CreateDirectory(bgm);
            Directory.CreateDirectory(Path.GetDirectoryName(patch));
            foreach (string code in new[] { "JPX", "BRX" }) File.Copy(MusicBank.GameFile(real, "BGM_" + code + ".csb"), Path.Combine(bgm, "BGM_" + code + ".csb"), true);
            File.Copy(u15, patch, true);
            var target = MusicSlot.FromFile("BGM_U15.csb");
            Console.WriteLine("stand-in " + target.TemplateCode);
            var have = new Dictionary<string, InstalledSong> { { "BGM_U15.csb", new InstalledSong { File = "BGM_U15.csb", Song = "old", Sha256 = Installer.Sha256(patch), Layers = new[] { "old", "old", "old" } } } };
            var pcm = new short[2 * 44100 * 4];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(Math.Sin(i * 0.05) * 8000);
            InstalledSong about;
            bool ok;
            try
            {
                byte[] bank = MusicPanel.BuildFor(game, have, target, 0, pcm, 0, pcm.Length / 2, "new", 0, 0, null, out about);
                ok = bank != null && bank.Length > 0 && about.Song == "new";
                Console.WriteLine("built " + bank.Length + " bytes, layers " + string.Join(" / ", about.Layers));
            }
            catch (Exception ex) { ok = false; Console.WriteLine("FAILED: " + ex.Message); }
            Console.WriteLine(ok ? "ALL PASSED" : "FAILED");
            Environment.Exit(ok ? 0 : 1);
        }
    }
}
