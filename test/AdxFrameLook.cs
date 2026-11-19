// A song's ADX frames around a moment, one channel: the song's peak and loudness there, the error, and how many of the
// frame's 32 samples the encoder had to clamp (nibble -8 or 7). Usage: AdxFrameLook <song> <channel> <seconds> [frames]
using System;
using System.Linq;

namespace ExMoreStuff
{
    static class AdxFrameLook
    {
        static void Main(string[] args)
        {
            var song = AudioFile.Load(args[0]);
            int c = int.Parse(args[1]);
            double at = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            int count = args.Length > 3 ? int.Parse(args[3]) : 12;
            double gain = MusicBank.MatchGain(MusicBank.Loudness(song.Pcm), MusicPanel.TypicalSong);
            short[] pcm = MusicBank.Level(song.Pcm, gain);
            byte[] adx = Adx.Encode(pcm, 2, Song.Rate);
            short[] back = Adx.Decode(adx, out AdxInfo info);
            int first = (int)(at * Song.Rate / 32) - count / 2;
            for (int f = first; f < first + count; f++)
            {
                int p = 40 + (f * 2 + c) * 18, scale = adx[p] << 8 | adx[p + 1], clamped = 0;
                for (int s = 0; s < 32; s++) { int nib = adx[p + 2 + s / 2]; nib = (s & 1) == 0 ? nib >> 4 : nib & 15; if (nib == 8 || nib == 7) clamped++; }
                double e = 0, sig = 0; int peak = 0, other = 0;
                for (int k = 0; k < 32; k++)
                {
                    int i = (f * 32 + k) * 2 + c;
                    e += (double)(pcm[i] - back[i]) * (pcm[i] - back[i]); sig += (double)pcm[i] * pcm[i];
                    peak = Math.Max(peak, Math.Abs((int)pcm[i]));
                    other = Math.Max(other, Math.Abs((int)pcm[(f * 32 + k) * 2 + 1 - c]));
                }
                string wave = string.Join(" ", Enumerable.Range(0, 8).Select(k => pcm[(f * 32 + k * 4) * 2 + c] + "/" + back[(f * 32 + k * 4) * 2 + c]));
                Console.WriteLine((f * 32.0 / Song.Rate).ToString("0.0000") + " s  peak " + peak + " (other ch " + other + ")  rms " + Math.Sqrt(sig / 32).ToString("0") +
                    "  err rms " + Math.Sqrt(e / 32).ToString("0") + "  scale " + scale + "  clamped " + clamped + "   " + wave);
            }
        }
    }
}
