// SFxT music (SfxtMusic, Hca) against the importer agent's proof: every bank listed; Mishima Estate's Level1 mixed
// to stereo at 48 kHz compared with ffmpeg's decode and downmix of the same six HCA files (its WAV, lined up by the
// best lag), its loop as docs\EXMS_SFXT_MUSIC.md has it (1 224 000 - 4 662 401), the song in the game's format, and
// a bank whose audio is in the cue sheet (CA_MIX). Read only.
// Usage: SfxtMusicTest <SFxT folder> <MSR Level1 WAV from ffmpeg, 48 kHz stereo>   (build_test.bat SfxtMusicTest)
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class SfxtMusicTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            string sfxt = args[0];
            Console.WriteLine("Banks");
            var banks = SfxtMusic.Banks(sfxt);
            foreach (var b in banks) Console.WriteLine("       " + b.Code + (b.Stage ? " (stage: " + b.Name + ")" : "") + ": " + string.Join(", ", b.Cues) + (b.Awb == null ? "  [in the cue sheet]" : ""));
            Check(banks.Count == 23 && banks.Count(b => b.Stage) == 11, "23 banks, 11 of them stages: " + banks.Count);

            Console.WriteLine("Mishima Estate, Level 1");
            var msr = banks.First(b => b.Code == "MSR");
            var clock = Stopwatch.StartNew();
            int rate, ls, le;
            float[] ours = SfxtMusic.Stereo(msr, 0, out rate, out ls, out le);
            Console.WriteLine("       decoded and mixed in {0:F1} s: {1:F1} s of audio", clock.Elapsed.TotalSeconds, ours.Length / 2.0 / rate);
            Check(rate == 48000 && ls == 1224000 && le == 4662401, "48 kHz, loop " + ls + " - " + le);

            float[] theirs; int trate, tch;
            Check(AudioFile.TryReadWav(File.ReadAllBytes(args[1]), out theirs, out trate, out tch) && trate == 48000 && tch == 2, "ffmpeg's WAV read");
            // the lag that lines them up best (ffmpeg keeps the encoder delay at the start, which this takes off, as
            // vgmstream does) and the polarity (ffmpeg's comes out inverted, which can't be heard), over a few seconds
            int best = 0; double bestCorr = 0;
            int from = 48000 * 30, span = 48000 * 2;
            for (int lag = 0; lag <= 8192; lag++)
            {
                double corr = 0;
                for (int i = from; i < from + span; i += 5)
                {
                    int j = i + lag;
                    if (2 * j + 1 >= theirs.Length || 2 * i + 1 >= ours.Length) continue;
                    corr += ours[2 * i] * theirs[2 * j];
                }
                if (Math.Abs(corr) > Math.Abs(bestCorr)) { bestCorr = corr; best = lag; }
            }
            double sign = bestCorr < 0 ? -1 : 1, signal = 0, noise = 0;
            int n = Math.Min(ours.Length / 2, theirs.Length / 2 - best) - 1;
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < 2; c++)
                {
                    double a = ours[2 * i + c] * sign, b = theirs[2 * (i + best) + c];
                    signal += b * b;
                    noise += (a - b) * (a - b);
                }
            }
            double snr = 10 * Math.Log10(signal / Math.Max(noise, 1e-20));
            Console.WriteLine("       ffmpeg's is {0} samples later (the encoder delay){1}; {2:F1} dB signal to difference", best, sign < 0 ? " and inverted" : "", snr);
            if (args.Length > 2)
            {
                // diagnostics: levels, the gain that fits best at that lag, and our mix as a WAV
                Func<float[], double> rms = x => Math.Sqrt(x.Select(v => (double)v * v).Average());
                double ab = 0, aa = 0;
                for (int i = Math.Max(0, -best); i < n; i++) for (int c = 0; c < 2; c++) { double a = ours[2 * i + c], b = theirs[2 * (i + best) + c]; ab += a * b; aa += a * a; }
                double g = ab / aa, fitNoise = 0;
                for (int i = Math.Max(0, -best); i < n; i++) for (int c = 0; c < 2; c++) { double a = ours[2 * i + c] * g, b = theirs[2 * (i + best) + c]; fitNoise += (a - b) * (a - b); }
                Console.WriteLine("       rms ours {0:F4} theirs {1:F4}; best gain {2:F3} gives {3:F1} dB; peak ours {4:F3}", rms(ours), rms(theirs), g,
                    10 * Math.Log10(signal / Math.Max(fitNoise, 1e-20)), ours.Max(v => Math.Abs(v)));
                var pcm = ours.Select(v => (short)Math.Max(-32768, Math.Min(32767, v * 32767))).ToArray();
                AudioFile.WriteWav(args[2], pcm, 2, 48000);
            }
            Check(snr > 40, "the same audio as ffmpeg's (over 40 dB)");

            Song song = SfxtMusic.Load(msr, 0);
            Check(song.LoopStart == (int)(1224000L * 44100 / 48000) && song.LoopEnd > song.LoopStart && song.LoopEnd <= song.Frames,
                  "as a song: 44.1 kHz, loop " + song.LoopStart + " - " + song.LoopEnd + ", " + song.Seconds.ToString("F1") + " s, \"" + song.Name + "\"");

            Console.WriteLine("A bank with its audio in the cue sheet");
            var mix = banks.First(b => b.Code == "CA_MIX");
            Song jingle = SfxtMusic.Load(mix, 0);
            Check(jingle.Frames > Song.Rate && jingle.Pcm.Any(v => v != 0), "CA_MIX: " + jingle.Seconds.ToString("F1") + " s of sound");

            Console.WriteLine("Every bank's first song decodes");
            foreach (var b in banks)
            {
                try { Song s = SfxtMusic.Load(b, 0); Console.WriteLine("       " + b.Code + ": " + s.Seconds.ToString("F1") + " s, loop " + (s.LoopStart >= 0 ? s.LoopStart + "-" + s.LoopEnd : "none")); }
                catch (Exception ex) { Check(false, b.Code + ": " + ex.Message); }
            }

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
