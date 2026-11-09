// Old .rar archives (RAR 2.9-4.x) read by EX More Stuff itself (Rar) against Windows' own tar: every file the same, byte for
// byte; and through GameBanana.Unpack (as an install does). The PPMd ones: monkeygigabuster's DeviantArt stages (user
// 2026-11-01, Button Mashin' Mastah on Linux: "needs Windows' own tar").
// Usage: RarTest <rar file> [<rar file> ...]   (build_test.bat RarTest, with all of src\)
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class RarTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            foreach (string archive in args)
            {
                Console.WriteLine(Path.GetFileName(archive));
                Check(Rar.Is(archive) && !Rar5.Is(archive), "an old-style RAR archive");
                var watch = Stopwatch.StartNew();
                System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, byte[]>> files;
                try { files = Rar.Read(archive, name => true); }
                catch (NotSupportedException e) { Check(false, "read: " + e.Message); continue; }
                Check(files.Count > 0, files.Count + " files read in " + watch.ElapsedMilliseconds + " ms, each matching the CRC in the archive");
                foreach (var f in files.Where(f => f.Key.EndsWith(".emz", StringComparison.OrdinalIgnoreCase)))
                {
                    int entries = -1;
                    try { entries = GameArt.ReadContainer(GameArt.Unpack(f.Value)).Count; } catch (Exception) { }
                    Check(entries > 0, f.Key + " opens: " + entries + " entries");
                }
                // tar's copy to compare with, when tar can read it (it can't undo RAR's filters: monkeygigabuster's stages)
                string work = Path.Combine(Path.GetTempPath(), "rartest");
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe"), "-xf \"" + archive + "\" -C \"" + work + "\"") { UseShellExecute = false, CreateNoWindow = true });
                p.WaitForExit();
                if (p.ExitCode != 0) Console.WriteLine("  (tar can't read it, exit " + p.ExitCode + ": no comparison)");
                else
                {
                    var tarFiles = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).ToList();
                    Check(tarFiles.Count == files.Count, "as many files as tar: " + tarFiles.Count);
                    int same = 0;
                    foreach (var f in files)
                    {
                        string at = Path.Combine(work, f.Key.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(at) && File.ReadAllBytes(at).SequenceEqual(f.Value)) same++;
                        else Check(false, f.Key + ": not the same as tar's");
                    }
                    Check(same == files.Count, same + " of " + files.Count + " the same as tar's");
                }
                Directory.Delete(work, true);
                var unpacked = GameBanana.Unpack(archive, Path.Combine(Path.GetTempPath(), "rartest_unpack"));
                Check(unpacked.Count > 0 && unpacked.All(u => files.Any(f => f.Key == u.Key && f.Value.SequenceEqual(u.Value))),
                      "Unpack keeps the game's files: " + unpacked.Count);
            }
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
