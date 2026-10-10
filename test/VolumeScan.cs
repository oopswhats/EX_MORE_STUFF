// How loud the game's music is: every BGM bank of the game (its own files, not patch_ae2_tu3), each cue decoded and
// measured as the ear hears it (LUFS, MusicBank.Loudness), the level its bank plays it at (MusicBank.MixGain), and the
// two together (what's heard in the game, before the player's own music volume); also each song EX More Stuff put in
// (patch_ae2_tu3) against the game's own bank for that slot. Every SYNTH column whose name speaks of volume is listed
// per bank, to see whether anything else there changes the level. Read only.
// Usage: VolumeScan <game folder> <out .tsv>   (build_test.bat VolumeScan)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    static class VolumeScan
    {
        static readonly string[] Roots = { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" };

        sealed class Measure { public string Bank, Kind; public int Cue; public double Lufs, MixDb, Heard, PeakDb; public string Synth; }

        static Measure Measure1(string bank, byte[] bytes, int cue, string kind)
        {
            int ls;
            Song s = MusicBank.Decode(bytes, cue, bank, out ls);
            double peak = s.Pcm.Length == 0 ? 0 : s.Pcm.Max(v => Math.Abs((int)v)) / 32768.0;
            double lufs = MusicBank.Loudness(s.Pcm), mix = MusicBank.MixGain(bytes, cue);
            return new Measure { Bank = bank, Kind = kind, Cue = cue, Lufs = lufs, MixDb = 20 * Math.Log10(mix), Heard = lufs + 20 * Math.Log10(mix), PeakDb = 20 * Math.Log10(Math.Max(peak, 1e-9)), Synth = SynthVolumes(bytes, cue) };
        }

        // the cue's track and waveform synth rows: every column whose name is about volume, with its value
        static string SynthVolumes(byte[] bank, int cue)
        {
            try
            {
                var csb = UtfTable.Read(bank);
                Func<string, UtfTable> sub = name => { for (int i = 0; i < csb.Rows.Count; i++) if ((string)csb.Get(i, "name") == name) return UtfTable.Read((byte[])csb.Get(i, "utf")); return null; };
                UtfTable cues = sub("CUE"), synths = sub("SYNTH");
                string track = (string)cues.Get(cue, "synth");
                var rows = new List<int>();
                for (int i = 0; i < synths.Rows.Count; i++) if ((string)synths.Get(i, "synname") == track) rows.Add(i);
                if (rows.Count == 0) return "";
                foreach (string link in ((string)synths.Get(rows[0], "lnkname") ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    for (int i = 0; i < synths.Rows.Count; i++) if ((string)synths.Get(i, "synname") == link) rows.Add(i);
                var cols = synths.Columns.Select((c, i) => new { c.Name, i }).Where(c => System.Text.RegularExpressions.Regex.IsMatch(c.Name, "vol|dry|wet|gain|level|send|aisac|cat", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
                return string.Join(" | ", rows.Select(r => string.Join(",", cols.Select(c => c.Name + "=" + Convert.ToString(synths.Rows[r][c.i], CultureInfo.InvariantCulture)))));
            }
            catch (Exception ex) { return "?" + ex.Message; }
        }

        static void Main(string[] args)
        {
            string game = args[0];
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in Roots)
            {
                string dir = Path.Combine(game, root, MusicBank.Folder);
                if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir, "BGM_*.csb")) names.Add(Path.GetFileName(f));
            }
            var all = new List<Measure>();
            foreach (string name in names)
            {
                string path = MusicBank.GameFile(game, name);
                if (path == null) continue;
                byte[] bytes = File.ReadAllBytes(path);
                string stem = Path.GetFileNameWithoutExtension(name).Substring(4);
                string kind = stem.EndsWith("_2CH") || stem.EndsWith("_EXT") ? "fighter" : stem.Length == 3 ? "stage" : stem.Length == 4 && (stem.EndsWith("2") || stem.EndsWith("3")) ? "round" : "other";
                int cues;
                try { cues = MusicBank.CueCount(bytes); } catch (Exception) { continue; }
                for (int c = 0; c < Math.Min(cues, 3); c++)
                {
                    try { all.Add(Measure1(name, bytes, c, kind)); }
                    catch (Exception ex) { Console.WriteLine(name + " cue " + c + ": " + ex.Message); }
                }
            }
            var mine = new List<Tuple<Measure, Measure>>();
            foreach (var song in MusicBank.Load(game))
            {
                string path = MusicBank.PatchFile(game, song.Key);
                if (!File.Exists(path)) continue;
                byte[] bytes = File.ReadAllBytes(path);
                Measure ours = Measure1(song.Key, bytes, 0, "ours");
                Measure theirs = all.FirstOrDefault(m => m.Bank.Equals(song.Key, StringComparison.OrdinalIgnoreCase) && m.Cue == 0);
                mine.Add(Tuple.Create(ours, theirs));
            }

            var tsv = new StringBuilder("bank\tkind\tcue\tlufs\tmix_db\theard\tpeak_db\tsynth volumes\n");
            foreach (var m in all.Concat(mine.Select(t => t.Item1)))
                tsv.AppendLine(string.Join("\t", m.Bank, m.Kind, m.Cue, m.Lufs.ToString("F2", CultureInfo.InvariantCulture), m.MixDb.ToString("F2", CultureInfo.InvariantCulture),
                    m.Heard.ToString("F2", CultureInfo.InvariantCulture), m.PeakDb.ToString("F2", CultureInfo.InvariantCulture), m.Synth));
            File.WriteAllText(args[1], tsv.ToString());

            Func<IEnumerable<double>, string> stats = v =>
            {
                var x = v.OrderBy(d => d).ToList();
                if (x.Count == 0) return "none";
                return string.Format(CultureInfo.InvariantCulture, "{0} songs: lowest {1:F1}, median {2:F1}, highest {3:F1}", x.Count, x[0], x[x.Count / 2], x[x.Count - 1]);
            };
            foreach (string kind in new[] { "stage", "round", "fighter", "other" })
                foreach (int cue in kind == "stage" ? new[] { 0, 1, 2 } : new[] { 0 })
                {
                    var set = all.Where(m => m.Kind == kind && m.Cue == cue).ToList();
                    if (set.Count == 0) continue;
                    Console.WriteLine("{0} cue {1}:", kind, cue);
                    Console.WriteLine("   own loudness  " + stats(set.Select(m => m.Lufs)));
                    Console.WriteLine("   bank level dB " + stats(set.Select(m => m.MixDb)));
                    Console.WriteLine("   heard         " + stats(set.Select(m => m.Heard)));
                }
            Console.WriteLine("Main layers, quietest and loudest as heard:");
            foreach (var m in all.Where(m => m.Cue == 0 && (m.Kind == "stage" || m.Kind == "fighter")).OrderBy(m => m.Heard).Take(5).Concat(
                     all.Where(m => m.Cue == 0 && (m.Kind == "stage" || m.Kind == "fighter")).OrderBy(m => m.Heard).Skip(Math.Max(0, all.Count(m => m.Cue == 0 && (m.Kind == "stage" || m.Kind == "fighter")) - 5))))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0,-18} {1,-8} own {2,6:F1}  level {3,6:F1}  heard {4,6:F1}", m.Bank, m.Kind, m.Lufs, m.MixDb, m.Heard));
            Console.WriteLine("Songs EX More Stuff put in, against the game's own for that slot:");
            foreach (var t in mine)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0,-18} ours own {1,6:F1} level {2,6:F1} heard {3,6:F1}   game's own {4} level {5} heard {6}",
                    t.Item1.Bank, t.Item1.Lufs, t.Item1.MixDb, t.Item1.Heard,
                    t.Item2 == null ? "-" : t.Item2.Lufs.ToString("F1", CultureInfo.InvariantCulture), t.Item2 == null ? "-" : t.Item2.MixDb.ToString("F1", CultureInfo.InvariantCulture),
                    t.Item2 == null ? "-" : t.Item2.Heard.ToString("F1", CultureInfo.InvariantCulture)));
            Console.WriteLine("Written: " + args[1]);
        }
    }
}
