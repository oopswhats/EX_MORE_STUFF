// Where a made song sounds scratchy (user 2026-11-18: The Cursed Image on Starlit Café): the bank's main song against the
// song it was made from, lined up, the difference (what the game file adds) per second; and clipping in each.
// Usage: MusicScratch <bank .csb or zip> <entry in the zip or -> <source song> <its song start (frames)> (build_test.bat MusicScratch)
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class MusicScratch
    {
        static void Main(string[] args)
        {
            byte[] bank;
            if (args[1] == "-") bank = File.ReadAllBytes(args[0]);
            else using (var z = ZipFile.OpenRead(args[0])) using (var s = z.Entries.First(e => e.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase)).Open()) using (var m = new MemoryStream()) { s.CopyTo(m); bank = m.ToArray(); }
            int ls;
            Song game = MusicBank.Decode(bank, 0, "bank", out ls);
            Song source = AudioFile.Load(args[2]);
            int start = int.Parse(args[3]);
            Console.WriteLine("bank: " + game.Frames / (double)Song.Rate + " s, loop at " + ls / (double)Song.Rate + " s, level " + MusicBank.MixGain(bank, 0).ToString("0.000") + ", loudness " + MusicBank.Loudness(game.Pcm).ToString("0.0"));
            Console.WriteLine("source: " + source.Frames / (double)Song.Rate + " s, loudness " + MusicBank.Loudness(source.Pcm).ToString("0.0"));
            Clips("source", source.Pcm);
            Clips("bank", game.Pcm);
            // lined up: the offset near the song start where they match best (on 4 seconds from 20 s in)
            int best = 0; double bestC = double.MinValue;
            int probe = 20 * Song.Rate, len = 4 * Song.Rate;
            for (int off = start - 3000; off <= start + 3000; off++)
            {
                if (off < 0) continue;
                double c = 0;
                for (int i = probe; i < probe + len; i += 7) c += (double)game.Pcm[2 * i] * source.Pcm[2 * (i + off)];
                if (c > bestC) { bestC = c; best = off; }
            }
            int n = Math.Min(game.Frames, source.Frames - best);
            double gg = 0, gs = 0;
            for (int i = 0; i < n; i++) for (int c = 0; c < 2; c++) { gg += (double)game.Pcm[2 * i + c] * source.Pcm[2 * (i + best) + c]; gs += (double)source.Pcm[2 * (i + best) + c] * source.Pcm[2 * (i + best) + c]; }
            double gain = gg / gs;
            Console.WriteLine("lined up at source frame " + best + " (song start " + start + "), gain " + gain.ToString("0.000"));
            // per second: the difference's level under the song's, both channels
            var worst = Enumerable.Range(0, n / Song.Rate).Select(sec =>
            {
                double sig = 0, err = 0, errL = 0, errR = 0;
                for (int i = sec * Song.Rate; i < (sec + 1) * Song.Rate; i++)
                    for (int c = 0; c < 2; c++)
                    {
                        double want = gain * source.Pcm[2 * (i + best) + c], d = game.Pcm[2 * i + c] - want;
                        sig += want * want; err += d * d; if (c == 0) errL += d * d; else errR += d * d;
                    }
                return new { sec, snr = 10 * Math.Log10(sig / Math.Max(1, err)), left = 10 * Math.Log10(sig / 2 / Math.Max(1, errL)), right = 10 * Math.Log10(sig / 2 / Math.Max(1, errR)) };
            }).ToList();
            Console.WriteLine("difference under the song, all: median " + worst.Select(w => w.snr).OrderBy(x => x).ElementAt(worst.Count / 2).ToString("0.0") + " dB");
            foreach (var w in worst.OrderBy(w => w.snr).Take(6))
                Console.WriteLine("  " + (w.sec / 60) + ":" + (w.sec % 60).ToString("00") + "  " + w.snr.ToString("0.0") + " dB (left " + w.left.ToString("0.0") + ", right " + w.right.ToString("0.0") + ")");
            // the game file's own noise, as a second encoding of what it plays adds it (the limiter's turning down left out)
            AdxInfo info;
            short[] again = Adx.Decode(Adx.Encode(game.Pcm, 2, Song.Rate), out info);
            Noise("encoding noise (ADX, as made now)", game.Pcm, again);
            short[] plain = Adx.Decode(Adx.Encode(game.Pcm, 2, Song.Rate, 0), out info);
            Noise("encoding noise (ADX, the old plain encoder)", game.Pcm, plain);
            for (int k = 4; k < args.Length; k++) { Song other = AudioFile.Load(args[k]); Clips(Path.GetFileName(args[k]), other.Pcm); Console.WriteLine("  loudness " + MusicBank.Loudness(other.Pcm).ToString("0.0")); }
        }

        static void Noise(string what, short[] want, short[] got)
        {
            int n = Math.Min(want.Length, got.Length) / 2;
            var secs = Enumerable.Range(0, n / Song.Rate).Select(sec =>
            {
                double sig = 0, err = 0;
                for (int i = sec * Song.Rate * 2; i < (sec + 1) * Song.Rate * 2; i++) { double d = got[i] - want[i]; sig += (double)want[i] * want[i]; err += d * d; }
                return new { sec, snr = 10 * Math.Log10(sig / Math.Max(1, err)) };
            }).ToList();
            Console.WriteLine(what + ": median " + secs.Select(s => s.snr).OrderBy(x => x).ElementAt(secs.Count / 2).ToString("0.0") + " dB under the song; worst " +
                              string.Join(", ", secs.OrderBy(s => s.snr).Take(5).Select(s => (s.sec / 60) + ":" + (s.sec % 60).ToString("00") + " " + s.snr.ToString("0.0") + " dB")));
        }

        static void Clips(string what, short[] pcm)
        {
            int full = 0, runs = 0, peak = 0; bool inRun = false;
            for (int i = 0; i < pcm.Length; i++)
            {
                int a = Math.Abs((int)pcm[i]);
                if (a > peak) peak = a;
                bool at = a >= 32700;
                if (at) { full++; if (!inRun) runs++; }
                inRun = at;
            }
            Console.WriteLine(what + ": peak " + (20 * Math.Log10(peak / 32768.0)).ToString("0.00") + " dBFS, " + full + " samples at full scale in " + runs + " runs");
        }
    }
}
