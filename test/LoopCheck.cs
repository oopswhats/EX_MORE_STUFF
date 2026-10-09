// Does the game's loop end where the music matches the intro's end (both lead into the loop start)? Compares the
// intro's last samples with the loop's, shifted, for each cue of a bank; and the step at loop end -> loop start.
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class LoopCheck
    {
        static void Main(string[] args)
        {
            foreach (var path in args)
            {
                var csb = UtfTable.Read(File.ReadAllBytes(path));
                var sdl = UtfTable.Read((byte[])csb.Get(Enumerable.Range(0, csb.Rows.Count).First(i => (string)csb.Get(i, "name") == "SOUND_ELEMENT"), "utf"));
                for (int el = 0; el < sdl.Rows.Count; el++)
                {
                    var aax = UtfTable.Read((byte[])sdl.Get(el, "data"));
                    AdxInfo ii, li;
                    var intro = Adx.Decode((byte[])aax.Get(0, "data"), out ii);
                    var loop = Adx.Decode((byte[])aax.Get(1, "data"), out li);
                    int ch = ii.Channels, n = 2048;
                    int ni = intro.Length / ch, nl = loop.Length / ch;
                    int bestShift = 0; double best = double.MaxValue;
                    for (int shift = -4096; shift <= 4096; shift++)
                    {
                        double d = 0;
                        for (int k = 1; k <= n; k++)
                        {
                            int a = ni - k, b = nl - k + shift;
                            if (b < 0 || b >= nl) { d = double.MaxValue; break; }
                            for (int c = 0; c < ch; c++) { double e = intro[a * ch + c] - loop[b * ch + c]; d += e * e; }
                        }
                        if (d < best) { best = d; bestShift = shift; }
                    }
                    double energy = 0;
                    for (int k = 1; k <= n; k++) for (int c = 0; c < ch; c++) energy += (double)intro[(ni - k) * ch + c] * intro[(ni - k) * ch + c];
                    Console.WriteLine("{0} cue {1}: intro {2}, loop {3}; intro tail matches the loop tail best at shift {4} (residual {5:F3} of its energy); loop end->start step {6} {7}",
                        Path.GetFileName(path), el, ni, nl, bestShift, best / Math.Max(energy, 1), loop[(nl - 1) * ch] - loop[0], loop[(nl - 1) * ch + 1] - loop[1]);
                }
            }
        }
    }
}
