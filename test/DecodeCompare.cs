// A song as EX More Stuff reads it (AudioFile.Load: Windows' Media Foundation) against another decoder's raw 16-bit
// 44.1 kHz stereo (ffmpeg's), lined up: where they differ, per second (user 2026-11-18: an mp3 "scratchy in EXMS but not
// in my mp3 player"). Usage: DecodeCompare <song> <its raw s16le from ffmpeg> (build_test.bat DecodeCompare)
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class DecodeCompare
    {
        static void Main(string[] args)
        {
            short[] ours = AudioFile.Load(args[0]).Pcm;
            byte[] raw = File.ReadAllBytes(args[1]);
            var theirs = new short[raw.Length / 2];
            Buffer.BlockCopy(raw, 0, theirs, 0, theirs.Length * 2);
            Console.WriteLine("ours " + ours.Length / 2 + " frames, ffmpeg's " + theirs.Length / 2);
            int best = 0; double bestC = double.MinValue;
            int probe = 20 * Song.Rate, len = 2 * Song.Rate;
            for (int off = -3000; off <= 3000; off++)
            {
                double c = 0;
                for (int i = probe; i < probe + len; i += 3) { int j = i + off; if (j >= 0 && j < theirs.Length / 2) c += (double)ours[2 * i] * theirs[2 * j]; }
                if (c > bestC) { bestC = c; best = off; }
            }
            Console.WriteLine("ffmpeg's is " + best + " frames " + (best >= 0 ? "later" : "earlier"));
            int n = Math.Min(ours.Length / 2, theirs.Length / 2 - Math.Max(0, best));
            var secs = Enumerable.Range(0, n / Song.Rate).Select(sec =>
            {
                double sig = 0, err = 0; int big = 0;
                for (int i = sec * Song.Rate; i < (sec + 1) * Song.Rate; i++)
                    for (int c = 0; c < 2; c++)
                    {
                        int j = i + best; if (j < 0) continue;
                        double d = ours[2 * i + c] - theirs[2 * j + c];
                        sig += (double)theirs[2 * j + c] * theirs[2 * j + c]; err += d * d;
                        if (Math.Abs(d) > 2000) big++;
                    }
                return new { sec, snr = 10 * Math.Log10(Math.Max(1, sig) / Math.Max(1, err)), big };
            }).ToList();
            Console.WriteLine("difference under the song: median " + secs.Select(s => s.snr).OrderBy(x => x).ElementAt(secs.Count / 2).ToString("0.0") + " dB");
            foreach (var s in secs.OrderBy(s => s.snr).Take(10))
                Console.WriteLine("  " + s.sec / 60 + ":" + (s.sec % 60).ToString("00") + "  " + s.snr.ToString("0.0") + " dB, " + s.big + " samples off by more than 2000");
        }
    }
}
