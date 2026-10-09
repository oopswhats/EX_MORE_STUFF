// LoopFinder on known answers: a game theme laid out as a soundtrack rip (intro, the loop twice, a fade) must give
// back the game's loop length to the sample; then any song files given, with the time taken.
// Usage: LoopTest <game> <bank, e.g. BGM_RVR.csb> [song files...]
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class LoopTest
    {
        static void Main(string[] args)
        {
            string game = args[0];
            var bank = File.ReadAllBytes(MusicBank.GameFile(game, args[1]));
            int intro;
            var theme = MusicBank.Decode(bank, 0, args[1], out intro);
            int loop = theme.Frames - intro;
            // intro + loop + loop + 10 s fading out
            int fade = 10 * Song.Rate;
            var rip = new short[(intro + 2 * loop + fade) * 2];
            Array.Copy(theme.Pcm, rip, theme.Pcm.Length);
            Array.Copy(theme.Pcm, intro * 2, rip, theme.Pcm.Length, loop * 2);
            for (int i = 0; i < fade; i++)
                for (int c = 0; c < 2; c++)
                    rip[(intro + 2 * loop + i) * 2 + c] = (short)(theme.Pcm[(intro + i % loop) * 2 + c] * (1 - i / (double)fade));
            Console.WriteLine("seam score where the copies are identical: {0:P1}", LoopFinder.SeamMatch(rip, intro + 1000, intro + loop + 1000));
            Report(args[1] + " as a rip (intro " + WaveformView.Time(intro / (double)Song.Rate) + ", loop " + loop + " frames)", rip);
            foreach (var path in args.Skip(2)) Report(Path.GetFileName(path), AudioFile.Load(path).Pcm);
        }

        static void Report(string what, short[] pcm)
        {
            var t0 = DateTime.Now;
            var s = LoopFinder.Find(pcm);
            Console.WriteLine("{0}: {1:F1} s long -> loop {2} .. {3} = {4} frames ({5} .. {6}), match {7:P1}, repeat {8}, in {9:F1} s",
                what, pcm.Length / 2.0 / Song.Rate, s.Start, s.End, s.End - s.Start, WaveformView.Time(s.Start / (double)Song.Rate),
                WaveformView.Time(s.End / (double)Song.Rate), s.Match, s.Repeat, (DateTime.Now - t0).TotalSeconds);
        }
    }
}
