// Tom's Round BGM mod coming and going with round 2/3 music, in a made-up game folder: put in with the first round
// song (or a package's round file), kept while any is left, taken out after; another mod's dinput8.dll and a copy
// the player put in are never touched. Usage: RoundModTest <scratch folder>   (build_test.bat RoundModTest)
using System;
using System.IO;

namespace ExMoreStuff
{
    static class RoundModTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Songs(string game, params string[] files)
        {
            string list = "{\"format\":1,\"songs\":[";
            for (int i = 0; i < files.Length; i++)
                list += (i > 0 ? "," : "") + "{\"file\":\"" + files[i] + "\",\"song\":\"x\",\"sha256\":\"\",\"loopStart\":0,\"loopEnd\":1,\"backup\":null}";
            File.WriteAllText(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_music.json"), list + "]}");
            if (files.Length == 0) File.Delete(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_music.json"));
        }

        static void Main(string[] args)
        {
            string game = args[0], dll = Path.Combine(game, "dinput8.dll");
            if (Directory.Exists(game)) Directory.Delete(game, true);
            Directory.CreateDirectory(Path.Combine(game, "patch_ae2_tu3"));

            Check(RoundMod.IsRoundFile("BGM_RVR2.csb") && RoundMod.IsRoundFile("battle\\sound\\bgm\\BGM_C713.csb") &&
                  !RoundMod.IsRoundFile("BGM_RVR.csb") && !RoundMod.IsRoundFile("BGM_RYU_2CH.csb"), "round files are BGM_<stage>2/3.csb");
            Songs(game, "BGM_RVR.csb");
            Check(RoundMod.Ensure(game) == null && !File.Exists(dll), "round 1 music alone doesn't bring the mod");
            Songs(game, "BGM_RVR.csb", "BGM_RVR2.csb");
            Check(RoundMod.Ensure(game) == null && RoundMod.Check(game) == RoundMod.State.Tom, "a round 2 song brings Tom's mod");
            RoundMod.RemoveIfUnused(game);
            Check(File.Exists(dll), "kept while round music is in");
            File.WriteAllText(Path.Combine(game, "USF4_Round_BGM.log"), "log");
            Songs(game, "BGM_RVR.csb");
            RoundMod.RemoveIfUnused(game);
            Check(!File.Exists(dll) && !File.Exists(Path.Combine(game, "USF4_Round_BGM.log")), "taken out (with its log) when the last round song goes");

            // a package's round file counts too
            File.WriteAllText(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff.json"),
                "{\"format\":1,\"installed\":[{\"type\":\"stage\",\"code\":\"D05\",\"source\":\"file\",\"files\":[\"battle\\\\sound\\\\bgm\\\\BGM_D053.csb\"]}]}");
            Check(RoundMod.Ensure(game) == null && File.Exists(dll), "a stage package's round 3 music brings it");
            File.Delete(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff.json"));
            RoundMod.RemoveIfUnused(game);
            Check(!File.Exists(dll), "and it goes with the package");

            // another mod's dinput8.dll: left alone, with a note
            File.WriteAllBytes(dll, new byte[] { 1, 2, 3 });
            Songs(game, "BGM_RVR2.csb");
            string note = RoundMod.Ensure(game);
            Check(note != null && File.ReadAllBytes(dll).Length == 3 && RoundMod.Check(game) == RoundMod.State.Other, "another mod's dinput8.dll is left alone: " + note);
            Songs(game);
            RoundMod.RemoveIfUnused(game);
            Check(File.ReadAllBytes(dll).Length == 3, "and never taken out");

            // an older copy EX More Stuff put in (v0.2, given as the second argument) is brought up to the built-in one
            if (args.Length > 1)
            {
                File.Delete(dll);
                Songs(game, "BGM_RVR2.csb");
                RoundMod.Ensure(game);                    // ours, with its marker
                File.Copy(args[1], dll, true);            // as if an older EX More Stuff had put v0.2 in
                RoundMod.Ensure(game);
                Check(Installer.Sha256(dll) == Installer.Sha256(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "res", "roundbgm", "dinput8.dll")),
                      "an older copy EX More Stuff put in is upgraded");
                Songs(game);
                RoundMod.RemoveIfUnused(game);
                Check(!File.Exists(dll), "and taken out like the current one");
            }

            // Tom's mod the player put in: used, never taken out
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "res", "roundbgm", "dinput8.dll"), dll, true);
            Songs(game, "BGM_RVR2.csb");
            Check(RoundMod.Ensure(game) == null && RoundMod.Check(game) == RoundMod.State.Tom, "the player's own copy of the mod is used");
            Songs(game);
            RoundMod.RemoveIfUnused(game);
            Check(File.Exists(dll), "and stays when the round music goes");

            Directory.Delete(game, true);
            Console.WriteLine(failures == 0 ? "RoundModTest passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
