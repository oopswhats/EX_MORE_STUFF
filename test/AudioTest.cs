// AudioFile: decodes the given files (MP3 through Media Foundation, WAV directly) to the game's format and reports
// length and level; resamples a 48 kHz test tone and checks its frequency and level survive.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class AudioTest
    {
        static void Main(string[] args)
        {
            string outDir = args[0];
            foreach (var path in args.Skip(1))
            {
                var clock = Stopwatch.StartNew();
                try
                {
                    var song = AudioFile.Load(path);
                    double rms = Math.Sqrt(song.Pcm.Select(s => (double)s * s).Average()) / 32768.0;
                    int peak = song.Pcm.Max(s => Math.Abs((int)s));
                    Console.WriteLine("{0}: {1:F1} s, {2} frames, rms {3:F3}, peak {4}; {5} ms", Path.GetFileName(path), song.Seconds, song.Frames, rms, peak, clock.ElapsedMilliseconds);
                    AudioFile.WriteWav(Path.Combine(outDir, "import_" + song.Name + ".wav"), song.Pcm, 2, 44100);
                }
                catch (Exception e) { Console.WriteLine("{0}: FAILED {1}", Path.GetFileName(path), e.Message); }
            }
            // a 1 kHz tone at 48 kHz, 10 s, amplitude 0.5 -> 44.1 kHz: zero crossings per second and level
            int n = 480000;
            var tone = new float[n * 2];
            for (int i = 0; i < n; i++) tone[2 * i] = tone[2 * i + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 48000.0));
            var sw = Stopwatch.StartNew();
            var o = AudioFile.ToGameFormat(tone, 48000, 2);
            int frames = o.Length / 2, crossings = 0;
            for (int i = 1; i < frames; i++) if ((o[2 * (i - 1)] < 0) != (o[2 * i] < 0)) crossings++;
            double amp = o.Skip(2000).Take(88200).Max(s => (double)s) / 32767.0;
            Console.WriteLine("resample 48k->44.1k: {0} frames (expect 441000), {1:F1} Hz (expect 1000), amplitude {2:F3} (expect 0.5), {3} ms", frames, crossings / 2.0 / (frames / 44100.0), amp, sw.ElapsedMilliseconds);
        }
    }
}
