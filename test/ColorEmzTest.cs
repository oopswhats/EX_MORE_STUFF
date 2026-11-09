// A color packed as one container (<CHR>_<NN>_<CC>.col.emz with its .obj.emm and .col.emb inside, Bogart Willey's
// CMY_01_03.col.emz.zip, 2026-11-08): GameBanana.Unpack takes its two files out, and SkinImport finds the costume in them.
// Usage: ColorEmzTest <zip with a .col.emz>   (build_test.bat ColorEmzTest, with all of src\)
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class ColorEmzTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            var files = GameBanana.Unpack(args[0], Path.Combine(Path.GetTempPath(), "coloremztest"));
            foreach (var f in files) Console.WriteLine("  " + f.Key + "  " + f.Value.Length + " bytes");
            Check(!files.Any(f => f.Key.EndsWith(".col.emz", StringComparison.OrdinalIgnoreCase)), "no .col.emz left");
            Check(files.Any(f => f.Key.EndsWith(".col.emb", StringComparison.OrdinalIgnoreCase)) && files.Any(f => f.Key.EndsWith(".obj.emm", StringComparison.OrdinalIgnoreCase)),
                  "its .col.emb and .obj.emm taken out");
            var costumes = SkinImport.Find(files);
            Check(costumes.Count == 1, "a costume found: " + string.Join(", ", costumes.Select(c => c.Fighter + "_" + c.Number.ToString("D2") + " (" + c.Describe + ")")));
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
