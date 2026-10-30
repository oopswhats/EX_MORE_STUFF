// RAR5 archives read by EX More Stuff itself (Rar5) against Windows' own tar: every file the same, byte for byte; and through
// GameBanana.Unpack (as an install does), with no tar asked. The real archives: RedWarriorP's seth_titan.rar and
// juri_espinela.rar (user 2026-10-29, "needs Windows' own tar" on a PC without it).
// Usage: Rar5Test <rar5 file> [<rar5 file> ...]   (with all of src\, -main:ExMoreStuff.Rar5Test)
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class Rar5Test
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            foreach (string archive in args)
            {
                Console.WriteLine(Path.GetFileName(archive));
                Check(Rar5.Is(archive) && !Rar.Is(archive), "a RAR5 archive");
                var watch = Stopwatch.StartNew();
                var files = Rar5.Read(archive, name => true);
                Check(files.Count > 0, files.Count + " files read in " + watch.ElapsedMilliseconds + " ms: " + string.Join(", ", files.Select(f => f.Key + " " + f.Value.Length)));
                // tar's copy to compare with
                string work = Path.Combine(Path.GetTempPath(), "rar5test");
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe"), "-xf \"" + archive + "\" -C \"" + work + "\"") { UseShellExecute = false, CreateNoWindow = true });
                p.WaitForExit();
                var tarFiles = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).ToList();
                Check(tarFiles.Count == files.Count, "as many files as tar: " + tarFiles.Count);
                foreach (var f in files)
                {
                    string at = Path.Combine(work, f.Key.Replace('/', Path.DirectorySeparatorChar));
                    Check(File.Exists(at) && File.ReadAllBytes(at).SequenceEqual(f.Value), f.Key + ": the same as tar's");
                }
                Directory.Delete(work, true);
                var unpacked = GameBanana.Unpack(archive, Path.Combine(Path.GetTempPath(), "rar5test_unpack"));
                Check(unpacked.Count > 0 && unpacked.All(u => files.Any(f => f.Key == u.Key && f.Value.SequenceEqual(u.Value))),
                      "Unpack keeps the game's files: " + string.Join(", ", unpacked.Select(u => u.Key)));
            }
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
