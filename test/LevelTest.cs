// "Match game volume" against the game's music: one song put on very different slots (Training Stage, one of the
// quietest; Half Pipe, one of the loudest; Ryu's theme; Decapre's, whose bank sets no sends; a custom stage, built on
// its stand-in's bank) as the Music page builds it, then what the game would play measured (the cue's own loudness plus
// its bank's level): about -20 LUFS on main and Ultra layers and -20.7 on low health (the bass cut) everywhere, the
// volume moving it, and unmatched each layer back at the slot's own level. Nothing is written to the game.
// Usage: LevelTest <real game folder> <a song file (WAV/MP3)>   (build_test.bat LevelTest, with all of src\)
using System;
using System.Collections.Generic;
using System.IO;

namespace ExMoreStuff
{
    static class LevelTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static double Heard(byte[] bank, int cue)
        {
            int ls;
            return MusicBank.Loudness(MusicBank.Decode(bank, cue, "", out ls).Pcm) + 20 * Math.Log10(MusicBank.MixGain(bank, cue));
        }

        static void Main(string[] args)
        {
            string game = args[0];
            Song song = AudioFile.Load(args[1]);
            short[] pcm = MusicBank.Level(song.Pcm, MusicBank.MatchGain(MusicBank.Loudness(song.Pcm), MusicPanel.TypicalSong));
            int ls = Song.Rate, le = song.Frames;
            var none = new Dictionary<string, InstalledSong>();
            foreach (var slot in new[] { new MusicSlot { Code = "TRN" }, new MusicSlot { Code = "HFP" }, new MusicSlot { Code = "U01" },
                                         new MusicSlot { Code = "RYU", Fighter = true }, new MusicSlot { Code = "DCP", Fighter = true } })
            {
                Console.WriteLine(slot.Name + " (" + slot.TemplateFile + ")");
                byte[] gameBank = File.ReadAllBytes(MusicBank.GameFile(game, slot.TemplateFile));
                int cues = MusicBank.CueCount(gameBank);
                InstalledSong about;
                // matched: main and Ultra the same song, low health its bass cut
                byte[] bank = MusicPanel.BuildFor(game, none, slot, 0, pcm, ls, le, "test", 0, 1, MusicPanel.LevelsFor(0), out about);
                for (int c = 0; c < Math.Min(cues, 3); c++)
                {
                    double heard = Heard(bank, c), want = MusicPanel.TypicalHeard[c];
                    Check(Math.Abs(heard - want) < 0.5, string.Format("layer {0}: heard {1:F1} LUFS (game's own here {2:F1}), wanted {3:F1}", c, heard, Heard(gameBank, c), want));
                }
                // the volume moves it
                bank = MusicPanel.BuildFor(game, none, slot, 0, pcm, ls, le, "test", 0, 1, MusicPanel.LevelsFor(-6), out about);
                Check(Math.Abs(Heard(bank, 0) - (MusicPanel.TypicalHeard[0] - 6)) < 0.5, string.Format("volume -6 dB: heard {0:F1}", Heard(bank, 0)));
                // not matched: the slot's own levels
                bank = MusicPanel.BuildFor(game, none, slot, 0, pcm, ls, le, "test", 0, 1, null, out about);
                bool same = true;
                for (int c = 0; c < Math.Min(cues, 3); c++) same &= Math.Abs(MusicBank.MixGain(bank, c) - MusicBank.MixGain(gameBank, c)) < 0.002;
                Check(same, "not matched: every layer at the game's own level for it");
            }
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
