// The physics check (CostumeCheck.PhysicsProblems, PHYSICS): every Capcom .bsr in the game passes it; Tom's Dictator
// Nightmare (VEG_11, 2026-11-12: two groups with no bones and links on another group's, closed the game) is FAULTY and
// fixable, and its fixed copy passes with those 2 groups out. Usage: PhysicsCheckTest <game folder> <Tom's zip> <scratch>
// (build_test.bat PhysicsCheckTest, with all of src\)
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;

namespace ExMoreStuff
{
    static class PhysicsCheckTest
    {
        static int failed;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) failed++; }

        static void Main(string[] args)
        {
            string game = args[0], tom = args[1], scratch = args[2];
            AppFolders.Root = Path.Combine(scratch, "data");
            AppFolders.NoMigrate = true;
            Directory.CreateDirectory(AppFolders.Root);
            var capcom = new[] { "resource", "dlc" }.Select(d => Path.Combine(game, d)).Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.bsr", SearchOption.AllDirectories)).Where(f => f.Contains(Path.Combine("battle", "chara"))).ToList();
            var bad = capcom.Where(f => CostumeCheck.PhysicsProblems(File.ReadAllBytes(f)).Count > 0).ToList();
            foreach (string f in bad.Take(5)) Console.WriteLine("   " + f + ": " + string.Join("; ", CostumeCheck.PhysicsProblems(File.ReadAllBytes(f)).Select(p => p.Item2)));
            Check(capcom.Count >= 280 && bad.Count == 0, capcom.Count + " Capcom .bsr files, " + bad.Count + " with problems");

            var r = CostumeCheck.Check(game, CostumeCheck.FromZip(tom)).Single();
            var physics = r.Findings.FirstOrDefault(f => f.Kind == "PHYSICS");
            Check(r.Verdict == CostumeCheck.Verdict.Faulty && physics != null && physics.Fixable, "Tom's VEG_11: " + r.Verdict + ", " + (physics != null ? physics.Text : "no PHYSICS"));

            string fixedZip = Path.Combine(scratch, "fixed.zip");
            var done = new List<string>();
            CostumeCheck.Fix(game, CostumeCheck.FromZip(tom), done, fixedZip);
            foreach (string d in done) Console.WriteLine("   done: " + d);
            var again = CostumeCheck.Check(game, CostumeCheck.FromZip(fixedZip)).Single();
            Check(again.Verdict != CostumeCheck.Verdict.Faulty && !again.Findings.Any(f => f.Kind == "PHYSICS"), "fixed copy: " + again.Verdict);
            byte[] bsr;
            using (var z = ZipFile.OpenRead(fixedZip))
            using (var s = z.Entries.First(e => e.Name.Equals("VEG_11.bsr", StringComparison.OrdinalIgnoreCase)).Open())
            using (var m = new MemoryStream()) { s.CopyTo(m); bsr = m.ToArray(); }
            Check(BitConverter.ToInt32(bsr, 12) == 9, "fixed .bsr has " + BitConverter.ToInt32(bsr, 12) + " groups (11 less the 2 empty)");

            Console.WriteLine(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            Environment.Exit(failed == 0 ? 0 : 1);
        }
    }
}
