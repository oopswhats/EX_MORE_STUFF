// Music banks' ADX streams decoded as a decoder without clamping would (user 2026-11-17: a "fart noise" in the right
// headphone on one song in the game, fine elsewhere): counts the samples whose value goes past 16 bits before the clamp,
// per channel. Usage: AdxOverflowScan <folder or .csb>... (build_test.bat AdxOverflowScan)
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class AdxOverflowScan
    {
        static void Main(string[] args)
        {
            foreach (string a in args)
                foreach (string f in Directory.Exists(a) ? Directory.GetFiles(a, "*.csb") : new[] { a })
                {
                    try { Scan(f); } catch (Exception ex) { Console.WriteLine(Path.GetFileName(f) + ": " + ex.Message); }
                }
        }

        static void Scan(string file)
        {
            var csb = UtfTable.Read(File.ReadAllBytes(file));
            int r = -1;
            for (int i = 0; i < csb.Rows.Count; i++) if ((string)csb.Get(i, "name") == "SOUND_ELEMENT") r = i;
            var elements = UtfTable.Read((byte[])csb.Get(r, "utf"));
            for (int e = 0; e < elements.Rows.Count; e++)
            {
                var data = elements.Get(e, "data") as byte[];
                if (data == null || data.Length < 8) continue;
                UtfTable aax;
                try { aax = UtfTable.Read(data); } catch { continue; }
                for (int k = 0; k < aax.Rows.Count; k++)
                {
                    var adx = aax.Get(k, "data") as byte[];
                    if (!Adx.IsAdx(adx)) continue;
                    string what;
                    long[] over = Count(adx, out what);
                    Console.WriteLine(Path.GetFileName(file) + " " + elements.Get(e, "name") + " part " + k + ": " +
                        string.Join(" / ", over.Select((n, c) => "ch" + c + " " + n)) + what);
                }
            }
        }

        static long[] Count(byte[] b, out string first)
        {
            var info = Adx.ReadHeader(b);
            int ch = info.Channels;
            double z = Math.Cos(2.0 * Math.PI * info.HighPass / info.SampleRate);
            double x = Math.Sqrt(2.0) - z, y = Math.Sqrt(2.0) - 1.0, cc = (x - Math.Sqrt((x + y) * (x - y))) / y;
            int c1 = (int)(cc * 8192), c2 = (int)(cc * cc * -4096);
            var h1 = new int[ch]; var h2 = new int[ch];
            for (int c = 0; c < ch && c < 2; c++) { h1[c] = info.History[2 * c]; h2[c] = info.History[2 * c + 1]; }
            var over = new long[ch];
            int worst = 0;
            first = "";
            int frames = (info.Samples + 31) / 32, p = info.HeaderSize;
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < ch; c++, p += 18)
                {
                    if (p + 18 > b.Length) return over;
                    int scale = b[p] << 8 | b[p + 1];
                    if ((scale & 0x8000) != 0) return over;
                    for (int s = 0; s < 32; s++)
                    {
                        int nib = b[p + 2 + s / 2];
                        nib = (s & 1) == 0 ? nib >> 4 : nib & 15;
                        if (nib >= 8) nib -= 16;
                        int v = nib * scale + ((c1 * h1[c] + c2 * h2[c]) >> 12);
                        if (v > 32767 || v < -32768)
                        {
                            if (over[c] == 0 && first.Length == 0) first = "  first at " + ((f * 32 + s) / (double)info.SampleRate).ToString("0.00") + " s ch" + c + " (" + v + ")";
                            over[c]++;
                            worst = Math.Max(worst, Math.Abs(v));
                        }
                        v = v < -32768 ? -32768 : v > 32767 ? 32767 : v;
                        h2[c] = h1[c]; h1[c] = v;
                    }
                }
            return over;
        }
    }
}
