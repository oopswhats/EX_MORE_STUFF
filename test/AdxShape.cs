// The ADX encoder's noise feedback (Adx.Shape; user 2026-11-19: The Cursed Image "scratchy", the new encoder "way better";
// the burst treble trim that came before it dropped, Forsaken Sanctuary "2 sounds way better" without it): a song, matched
// as Put matches it, encoded the old plain way (shape 0) and the new; the grit under 4 and 8 kHz, and all of it, each
// second (median and worst). "ab": clips to hear.
// Usage: AdxShape <song> [ab <folder> <from s> <seconds> <name>] (build_test.bat AdxShape)
using System;
using System.Linq;

namespace ExMoreStuff
{
    static class AdxShape
    {
        static void Main(string[] args)
        {
            short[] raw = AudioFile.Load(args[0]).Pcm;
            double gain = MusicBank.MatchGain(MusicBank.Loudness(raw), MusicPanel.TypicalSong);
            short[] song = MusicBank.Level(raw, gain);
            Console.WriteLine(System.IO.Path.GetFileName(args[0]) + ", matched x" + gain.ToString("0.000"));
            AdxInfo info;
            if (args.Length > 5 && args[1] == "ab")
            {
                int from = int.Parse(args[3]) * Song.Rate * 2, take = int.Parse(args[4]) * Song.Rate * 2;
                Func<short[], short[]> cut = p => p.Skip(from).Take(take).ToArray();
                string at = System.IO.Path.Combine(args[2], args[5]);
                AudioFile.WriteWav(at + " 1 original.wav", cut(song), 2, Song.Rate);
                AudioFile.WriteWav(at + " 2 old encoder.wav", cut(Adx.Decode(Adx.Encode(song, 2, Song.Rate, 0), out info)), 2, Song.Rate);
                AudioFile.WriteWav(at + " 3 new encoder.wav", cut(Adx.Decode(Adx.Encode(song, 2, Song.Rate), out info)), 2, Song.Rate);
                return;
            }
            Measure("old, plain", song, Adx.Decode(Adx.Encode(song, 2, Song.Rate, 0), out info));
            Measure("new (Adx.Shape)", song, Adx.Decode(Adx.Encode(song, 2, Song.Rate), out info));
        }

        static void Measure(string what, short[] input, short[] back)
        {
            int n = Math.Min(input.Length, back.Length);
            var err = new short[n];
            for (int i = 0; i < n; i++) err[i] = (short)Math.Max(-32768, Math.Min(32767, back[i] - input[i]));
            short[] under8 = LowPass(err, 8000), under4 = LowPass(err, 4000);
            var secs = Enumerable.Range(0, n / 2 / Song.Rate).Select(sec =>
            {
                double sig = 0, e8 = 0, e4 = 0, all = 0;
                for (int i = sec * Song.Rate * 2; i < (sec + 1) * Song.Rate * 2; i++)
                { sig += (double)input[i] * input[i]; e8 += (double)under8[i] * under8[i]; e4 += (double)under4[i] * under4[i]; all += (double)err[i] * err[i]; }
                Func<double, double> db = e => 10 * Math.Log10(sig / Math.Max(1, e));
                return new { sec, e8 = db(e8), e4 = db(e4), all = db(all) };
            }).ToList();
            Func<double[], string> med = v => v.OrderBy(t => t).ElementAt(v.Length / 2).ToString("0.0");
            Console.WriteLine(what.PadRight(22) + " grit under 4 kHz " + med(secs.Select(s => s.e4).ToArray()) + " dB (worst " + secs.Min(s => s.e4).ToString("0.0") +
                              "), under 8 kHz " + med(secs.Select(s => s.e8).ToArray()) + " dB (worst " + secs.Min(s => s.e8).ToString("0.0") + "), all " + med(secs.Select(s => s.all).ToArray()) + " dB");
            Console.WriteLine("   worst seconds under 8 kHz: " + string.Join(", ", secs.OrderBy(s => s.e8).Take(5).Select(s => s.sec / 60 + ":" + (s.sec % 60).ToString("00") + " " + s.e8.ToString("0.0"))));
        }

        static short[] LowPass(short[] pcm, int hz)
        {
            const int count = 127;
            var taps = new double[count];
            double fc = hz / (double)Song.Rate, sum = 0;
            for (int i = 0; i < count; i++)
            {
                double x = i - (count - 1) / 2.0;
                double sinc = x == 0 ? 2 * Math.PI * fc : Math.Sin(2 * Math.PI * fc * x) / x;
                taps[i] = sinc * (0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (count - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (count - 1)));
                sum += taps[i];
            }
            for (int i = 0; i < count; i++) taps[i] /= sum;
            int n = pcm.Length / 2, half = count / 2;
            var o = new short[pcm.Length];
            for (int c = 0; c < 2; c++)
                for (int j = 0; j < n; j++)
                {
                    double v = 0;
                    for (int t = 0; t < count; t++) { int at = j + t - half; if (at >= 0 && at < n) v += taps[t] * pcm[at * 2 + c]; }
                    o[j * 2 + c] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(v)));
                }
            return o;
        }
    }
}
