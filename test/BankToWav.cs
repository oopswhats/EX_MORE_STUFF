// A music bank's main song (cue 0) as the game decodes it, intro then loop, written as a .wav to listen to
// outside the game (user 2026-11-17: a "fart noise" in the right headphone in the game only).
// Usage: BankToWav <out folder> <.csb>... (build_test.bat BankToWav)
using System;
using System.IO;

namespace ExMoreStuff
{
    static class BankToWav
    {
        static void Main(string[] args)
        {
            Directory.CreateDirectory(args[0]);
            for (int i = 1; i < args.Length; i++)
            {
                int loopStart;
                var song = MusicBank.Decode(File.ReadAllBytes(args[i]), 0, Path.GetFileNameWithoutExtension(args[i]), out loopStart);
                string wav = Path.Combine(args[0], Path.GetFileNameWithoutExtension(args[i]) + " as in game.wav");
                AudioFile.WriteWav(wav, song.Pcm, 2, Song.Rate);
                Console.WriteLine(wav + "  (loop starts at " + (loopStart / (double)Song.Rate).ToString("0.00") + " s)");
            }
        }
    }
}
