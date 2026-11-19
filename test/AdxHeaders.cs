// The ADX streams in the game's music banks: their header settings tallied (rate, prediction cutoff, encoding, version,
// flags), to see how CRI made the game's own music (user 2026-11-18: our songs sound scratchy in the game format).
// Usage: AdxHeaders <folder> (build_test.bat AdxHeaders)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class AdxHeaders
    {
        static void Main(string[] args)
        {
            var tally = new Dictionary<string, int>();
            foreach (string f in Directory.GetFiles(args[0], "BGM_*.csb", SearchOption.AllDirectories))
            {
                byte[] b = File.ReadAllBytes(f);
                for (int i = 6; i + 6 < b.Length; i++)
                {
                    if (b[i] != 0x28 || b[i + 1] != 0x63 || b[i + 2] != 0x29 || b[i + 3] != 0x43 || b[i + 4] != 0x52 || b[i + 5] != 0x49) continue;   // "(c)CRI"
                    for (int s = Math.Max(0, i - 0x400); s < i; s++)
                    {
                        if (b[s] != 0x80 || b[s + 1] != 0 || s + ((b[s + 2] << 8) | b[s + 3]) + 4 - 6 != i) continue;
                        int rate = (b[s + 8] << 24) | (b[s + 9] << 16) | (b[s + 10] << 8) | b[s + 11], cut = (b[s + 16] << 8) | b[s + 17];
                        string k = "encoding " + b[s + 4] + ", block " + b[s + 5] + ", " + b[s + 7] + " ch, " + rate + " Hz, cutoff " + cut + ", version " + b[s + 18] + ", flags " + b[s + 19];
                        int c; tally.TryGetValue(k, out c); tally[k] = c + 1;
                        break;
                    }
                }
            }
            foreach (var t in tally.OrderByDescending(t => t.Value)) Console.WriteLine(t.Value + " x " + t.Key);
        }
    }
}
