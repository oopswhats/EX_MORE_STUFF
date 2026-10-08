// Checks StagePack against the game's stages: rebuilding each file under its own code must give the same container
// byte for byte; lists the gameplay calls in each stage's scripts and where the stage code appears in them.
// Usage: StageScan <game folder> [code ...]   (built with src\GameArt.cs and src\StagePack.cs)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    static class StageScan
    {
        static void Main(string[] args)
        {
            string game = args[0];
            bool original = args.Contains("--original");      // the game's own files, not the patch folder's
            var codes = args.Skip(1).Where(a => a != "--original").ToArray();
            if (codes.Length == 0) codes = Stages.Codes;
            foreach (string code in codes)
            {
                var clock = Stopwatch.StartNew();
                var line = new StringBuilder(code + ":");
                foreach (string suffix in StagePack.Suffixes)
                {
                    string path = StagePack.GameFile(game, code, suffix, original);
                    if (path == null) { line.Append(" no " + suffix); continue; }
                    byte[] file = File.ReadAllBytes(path);
                    byte[] same = GameArt.Unpack(StagePack.Recode(file, code, code));
                    bool identical = same.SequenceEqual(GameArt.Unpack(file));
                    line.AppendFormat(" {0} {1} ({2:N1} MB){3}", suffix, identical ? "rebuilds identical" : "REBUILD DIFFERS",
                        file.Length / 1048576.0, path.Contains("patch_ae2_tu3") ? " [patch]" : "");
                    if (suffix == ".emz")
                    {
                        var calls = StagePack.GameplayCallsIn(file);
                        line.Append(calls.Count > 0 ? " CALLS " + string.Join(",", calls) : " scenery only");
                        foreach (var lua in GameArt.ReadContainer(GameArt.Unpack(file)))
                            foreach (string call in calls)
                                if (lua.Key.EndsWith(".lua") && Encoding.ASCII.GetString(lua.Value).Contains(call)) line.Append("\n    " + call + " in " + lua.Key);
                        foreach (var entry in GameArt.ReadContainer(GameArt.Unpack(file)))
                            if (entry.Key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                            {
                                var seen = new HashSet<string>();
                                string text = Encoding.ASCII.GetString(entry.Value);
                                for (int at = text.IndexOf(code); at >= 0; at = text.IndexOf(code, at + 1))
                                {
                                    if (StagePack.StartsName(entry.Value, at, code.Length)) continue;   // recoded; list the rest
                                    int from = Math.Max(0, at - 6), to = Math.Min(text.Length, at + 18);
                                    string around = new string(text.Substring(from, to - from).Select(c => c >= 32 && c < 127 ? c : '.').ToArray());
                                    if (seen.Add(around) && seen.Count <= 6) line.Append("\n    kept in " + entry.Key + ": " + around);
                                }
                            }
                    }
                }
                line.AppendFormat("  [{0:N1}s]", clock.Elapsed.TotalSeconds);
                Console.WriteLine(line);
            }
        }
    }
}
