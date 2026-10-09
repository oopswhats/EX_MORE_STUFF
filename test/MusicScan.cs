// Checks the music code against the game's banks: every .csb rebuilt (nested @UTF tables too) must come out byte for
// byte; one bank's main cue decoded (intro + loop) to WAV, the seam between them measured, the ADX v4 header values
// compared with the intro's last samples; the intro re-encoded and decoded again (signal-to-noise).
// Usage: MusicScan <game folder> <out folder> [bank name for the decode test, e.g. BGM_RVR]
// Built with src\CriUtf.cs and src\Adx.cs (test\build_test.bat MusicScan).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class MusicScan
    {
        static byte[] Rewrite(byte[] b)
        {
            if (!UtfTable.IsUtf(b)) return b;
            var t = UtfTable.Read(b);
            foreach (var row in t.Rows)
                for (int c = 0; c < t.Columns.Count; c++)
                    if (row[c] is byte[] && t.Columns[c].Storage == UtfTable.PerRow) row[c] = Rewrite((byte[])row[c]);
            return t.Write();
        }

        static void Main(string[] args)
        {
            string game = args[0], outDir = args[1];
            string bank = args.Length > 2 ? args[2] : "BGM_RVR";
            Directory.CreateDirectory(outDir);
            var roots = new[] { "patch_ae2_tu3", "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" };
            int ok = 0, bad = 0;
            string bankPath = null;
            foreach (var r in roots)
            {
                string d = Path.Combine(game, r, @"battle\sound\bgm");
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*.csb").OrderBy(x => x))
                {
                    var b = File.ReadAllBytes(f);
                    byte[] w;
                    try { w = Rewrite(b); }
                    catch (Exception e) { Console.WriteLine("FAIL {0}: {1}", f, e.Message); bad++; continue; }
                    if (w.SequenceEqual(b)) ok++;
                    else
                    {
                        int at = 0;
                        while (at < Math.Min(w.Length, b.Length) && w[at] == b[at]) at++;
                        Console.WriteLine("DIFF {0}: {1} vs {2} bytes, first difference at {3}", f, w.Length, b.Length, at);
                        bad++;
                    }
                    if (bankPath == null && Path.GetFileNameWithoutExtension(f).Equals(bank, StringComparison.OrdinalIgnoreCase)) bankPath = f;
                }
            }
            Console.WriteLine("rebuilt byte for byte: {0}, different: {1}", ok, bad);
            if (bankPath == null) { Console.WriteLine("no " + bank); return; }

            // the main cue's two streams
            var csb = UtfTable.Read(File.ReadAllBytes(bankPath));
            var sdl = UtfTable.Read((byte[])csb.Get(Enumerable.Range(0, csb.Rows.Count).First(i => (string)csb.Get(i, "name") == "SOUND_ELEMENT"), "utf"));
            var aax = UtfTable.Read((byte[])sdl.Get(0, "data"));
            AdxInfo ii, li;
            var introAdx = (byte[])aax.Get(0, "data");
            var loopAdx = (byte[])aax.Get(1, "data");
            var intro = Adx.Decode(introAdx, out ii);
            var loop = Adx.Decode(loopAdx, out li);
            Console.WriteLine("{0}: intro {1} samples, loop {2}, {3} ch, {4} Hz; nsmpl {5}", Path.GetFileName(bankPath), ii.Samples, li.Samples, ii.Channels, ii.SampleRate, sdl.Get(0, "nsmpl"));
            int ch = ii.Channels;
            Console.WriteLine("intro header 0x18: {0}", string.Join(" ", ii.History));
            Console.WriteLine("loop header 0x18:  {0}", string.Join(" ", li.History));
            Console.WriteLine("intro last 2 samples per channel: ch0 {0} {1}  ch1 {2} {3}", intro[intro.Length - 2 * ch], intro[intro.Length - ch], intro[intro.Length - 2 * ch + 1], intro[intro.Length - ch + 1]);
            Console.WriteLine("intro first 2 samples: ch0 {0} {1}  ch1 {2} {3}", intro[0], intro[ch], intro[1], intro[ch + 1]);
            Console.WriteLine("loop first 2 samples:  ch0 {0} {1}  ch1 {2} {3}", loop[0], loop[ch], loop[1], loop[ch + 1]);
            Console.WriteLine("loop last 2 samples:   ch0 {0} {1}  ch1 {2} {3}", loop[loop.Length - 2 * ch], loop[loop.Length - ch], loop[loop.Length - 2 * ch + 1], loop[loop.Length - ch + 1]);
            // seam: intro end -> loop start, and loop end -> loop start (what the game plays forever)
            Console.WriteLine("seam intro->loop: step {0}, typical step {1}", Math.Abs(loop[0] - intro[intro.Length - ch]), TypicalStep(loop, ch));
            Console.WriteLine("seam loop->loop:  step {0}", Math.Abs(loop[0] - loop[loop.Length - ch]));
            WriteWav(Path.Combine(outDir, Path.GetFileNameWithoutExtension(bankPath) + "_cue0_intro_then_loop_x2.wav"),
                     intro.Concat(loop).Concat(loop).ToArray(), ch, ii.SampleRate);
            // re-encode the intro, decode, compare
            var again = Adx.Encode(intro, ch, ii.SampleRate);
            AdxInfo ai;
            var back = Adx.Decode(again, out ai);
            double sig = 0, noise = 0;
            for (int i = 0; i < intro.Length; i++) { sig += (double)intro[i] * intro[i]; noise += (double)(intro[i] - back[i]) * (intro[i] - back[i]); }
            Console.WriteLine("re-encoded intro: {0} -> {1} bytes (game's {2}); SNR {3:F1} dB", intro.Length * 2, again.Length, introAdx.Length, 10 * Math.Log10(sig / Math.Max(noise, 1)));
        }

        static double TypicalStep(short[] x, int ch)
        {
            double s = 0;
            int n = 0;
            for (int i = ch; i < Math.Min(x.Length, 44100 * ch); i += ch) { s += Math.Abs(x[i] - x[i - ch]); n++; }
            return n > 0 ? s / n : 0;
        }

        static void WriteWav(string path, short[] pcm, int ch, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = pcm.Length * 2;
                w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVEfmt ".ToCharArray());
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(rate); w.Write(rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(bytes);
                foreach (var s in pcm) w.Write(s);
            }
        }
    }
}
