// How well our ADX encoder keeps a song (user 2026-11-17: a "fart noise" in the right headphone on one song in the
// game, fine elsewhere): each song matched as the Music tab does, encoded, decoded, and compared frame by frame (32
// samples); prints per channel the frames whose error is loud against the song there, and the worst ones.
// Usage: AdxQualityScan <song file>... (build_test.bat AdxQualityScan)
using System;
using System.Collections.Generic;
using System.Linq;

namespace ExMoreStuff
{
    static class AdxQualityScan
    {
        static void Main(string[] args)
        {
            foreach (string path in args)
            {
                var song = AudioFile.Load(path);
                double gain = MusicBank.MatchGain(MusicBank.Loudness(song.Pcm), MusicPanel.TypicalSong);
                short[] pcm = MusicBank.Level(song.Pcm, gain);
                short[] back = Adx.Decode(Adx.Encode(pcm, 2, Song.Rate), out AdxInfo info);
                int n = pcm.Length / 2;
                Console.WriteLine(System.IO.Path.GetFileName(path) + "  x" + gain.ToString("0.00"));
                for (int c = 0; c < 2; c++)
                {
                    var bad = new List<Tuple<double, int, double>>();   // error dB under full scale, frame, error vs signal dB
                    double errAll = 0, sigAll = 0;
                    for (int f = 0; f < n / 32; f++)
                    {
                        double e = 0, s = 0;
                        for (int k = 0; k < 32; k++)
                        {
                            int i = (f * 32 + k) * 2 + c;
                            double d = pcm[i] - back[i];
                            e += d * d; s += (double)pcm[i] * pcm[i];
                        }
                        errAll += e; sigAll += s;
                        double eDb = 10 * Math.Log10(e / 32 / (32768.0 * 32768) + 1e-12), rel = 10 * Math.Log10((e + 1) / (s + 1));
                        if (eDb > -30) bad.Add(Tuple.Create(eDb, f, rel));
                    }
                    Console.WriteLine("  ch" + c + (c == 1 ? " (right)" : " (left) ") + ": SNR " + (10 * Math.Log10(sigAll / (errAll + 1))).ToString("0.0") +
                        " dB, " + bad.Count + " frames with error over -30 dBFS" +
                        string.Concat(bad.OrderByDescending(b => b.Item1).Take(5).Select(b => "\n     " + (b.Item2 * 32.0 / Song.Rate).ToString("0.000") + " s: error " + b.Item1.ToString("0.0") + " dBFS (" + b.Item3.ToString("+0.0;-0.0") + " dB vs song)")));
                }
            }
        }
    }
}
