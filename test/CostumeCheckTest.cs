// The costume check on a costume made from the game's own C.Viper (costume 1, put in slot 90): clean, it's OK; with its
// neck moved it desyncs, a material taken out of a color draws white, a part left out is missing; the fixer's copy is
// OK again, and the original stays as it was. Then: a copy is never made over the costume or in the game's folder, the
// mod's marker files go with it, a white color alone is worth a look, a damaged model or zip is a finding while the rest
// are still checked, a skeleton whose parents run in a circle doesn't hang, too many bones in one palette closes the
// game, the game's own slots are left out of the installed check, one file picked checks only its costume.
// Build: csc -target:exe -main:ExMoreStuff.CostumeCheckTest (the references of build.bat) test\CostumeCheckTest.cs src\*.cs
// Usage: CostumeCheckTest <game> <empty work folder> [<results window picture.png>]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class CostumeCheckTest
    {
        static int failed;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) failed++; }

        static bool Refused(Action fix)
        {
            try { fix(); return false; }
            catch (InvalidOperationException ex) { Console.WriteLine("      refused: " + ex.Message); return true; }
        }

        // a costume's files copied under another name (AGL_90... -> AGL_92...)
        static void CopyAs(string from, string fromStem, string to, string toStem)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from, fromStem + "*")) File.Copy(f, Path.Combine(to, Path.GetFileName(f).Replace(fromStem, toStem)), true);
        }

        // every submesh record of a model (as CostumeCheck reads them)
        static IEnumerable<int> Submeshes(byte[] emo)
        {
            Func<int, int> i32 = o => BitConverter.ToInt32(emo, o);
            Func<int, int> u16 = o => BitConverter.ToUInt16(emo, o);
            int pt = i32(0xc);
            for (int p = 0; p < BitConverter.ToInt16(emo, pt); p++)
            {
                int po = pt + i32(pt + 8 + p * 4);
                for (int g = 0; g < i32(po); g++)
                {
                    int go = po + i32(po + 4 + g * 4);
                    for (int r = 0; r < u16(go + 6); r++)
                    {
                        int model = go + i32(go + 8 + r * 4);
                        for (int s = 0; s < u16(model + 0x1a); s++) yield return model + i32(model + i32(model + 0x1c) + s * 4) + 0x10;
                    }
                }
            }
        }

        static int BoneIndex(byte[] emo, string name)
        {
            int s = BitConverter.ToInt32(emo, 16), names = s + BitConverter.ToInt32(emo, s + 12);
            for (int i = 0; i < BitConverter.ToUInt16(emo, s); i++)
                if (System.Text.Encoding.ASCII.GetString(emo, s + BitConverter.ToInt32(emo, names + i * 4), name.Length + 1) == name + "\0") return i;
            return -1;
        }

        [STAThread]
        static void Main(string[] args)
        {
            string game = args[0], work = Path.Combine(args[1], "AGL_90");
            Directory.CreateDirectory(work);
            Func<string, string> stock = name => SkinImport.GameFile(game, "AGL", name);
            File.Copy(stock("AGL_01.obj.emo"), Path.Combine(work, "AGL_90.obj.emo"), true);
            foreach (string part in new[] { ".shd.emo", ".nml.emb", ".bsr" }) File.Copy(stock("AGL_01" + part), Path.Combine(work, "AGL_90" + part), true);
            for (int c = 1; c <= 10; c++)
                foreach (string part in new[] { ".col.emb", ".obj.emm" })
                    File.Copy(stock("AGL_01_" + c.ToString("D2") + part), Path.Combine(work, "AGL_90_" + c.ToString("D2") + part), true);
            // the mod's marker files (and another costume's, which stays behind)
            foreach (string marker in new[] { "slot_AGL_90.txt", "slot_AGL_91.txt", "needs_exms_0.97.txt" }) File.WriteAllText(Path.Combine(work, marker), "test");

            var clean = CostumeCheck.Check(game, CostumeCheck.FromFolder(work)).Single();
            Check(clean.Verdict == CostumeCheck.Verdict.Ok || clean.Verdict == CostumeCheck.Verdict.Mirror, "the game's own model is OK (" + clean.Verdict + ")");

            // the neck 0.1 higher (Samus AGL_09 had it 0.098 lower), color 3's first material (eye) renamed eyes, no .bsr
            string model = Path.Combine(work, "AGL_90.obj.emo");
            byte[] emo = File.ReadAllBytes(model);
            int s = BitConverter.ToInt32(emo, 16), nodes = s + BitConverter.ToInt32(emo, s + 8);
            int neck = BoneIndex(emo, "Neck");
            int y = nodes + neck * 80 + 16 + 13 * 4;
            Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.ToSingle(emo, y) + 0.1f), 0, emo, y, 4);
            File.WriteAllBytes(model, emo);
            string emm = Path.Combine(work, "AGL_90_03.obj.emm");
            byte[] m = File.ReadAllBytes(emm);
            int t = BitConverter.ToInt32(m, 0xc), first = t + BitConverter.ToInt32(m, t + 4);
            m[first + 3] = (byte)'s';
            File.WriteAllBytes(emm, m);
            File.Delete(Path.Combine(work, "AGL_90.bsr"));
            byte[] before = File.ReadAllBytes(model);

            var broken = CostumeCheck.Check(game, CostumeCheck.FromFolder(work)).Single();
            Check(broken.Verdict == CostumeCheck.Verdict.Faulty, "the changed costume is FAULTY");
            Check(broken.Findings.Any(f => f.Kind == "DESYNC" && f.Text.Contains("NECK")), "the moved neck desyncs");
            Check(broken.Findings.Any(f => f.Kind == "WHITE" && f.Text.Contains("colors 03")), "color 3 draws white");
            Check(broken.Findings.Any(f => f.Kind == "FILES" && f.Text.Contains("AGL_90.bsr")), "the .bsr is missing");
            string report = CostumeCheck.Report(game, new List<CostumeCheck.Result> { broken });
            Console.WriteLine(report);
            Check(!report.Contains("Ember"), "the report doesn't name Ember");

            // never over the costume itself, a file of its folder, or into the game's folder
            Check(Refused(() => CostumeCheck.Fix(game, CostumeCheck.FromFolder(work), new List<string>(), work)), "a copy over the costume's folder is refused");
            Check(Refused(() => CostumeCheck.Fix(game, CostumeCheck.FromFolder(work), new List<string>(), Path.Combine(work, "AGL_90.obj.emo"))), "a copy over one of its files is refused");
            string inGame = Path.Combine(Game.PatchFolder(game), "AGL_90 - fixed.zip");
            Check(Refused(() => CostumeCheck.Fix(game, CostumeCheck.FromFolder(work), new List<string>(), inGame)) && !File.Exists(inGame), "a copy into the game's folder is refused");
            Check(CostumeCheck.TargetProblem(game, CostumeCheck.FromFolder(work), Path.Combine(args[1], "elsewhere.zip")) == null, "a copy elsewhere is fine");

            // the fixer works on copies: a source that hands out the same bytes each time keeps them
            var cached = Directory.GetFiles(work).ToDictionary(Path.GetFileName, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
            var kept = cached.ToDictionary(p => p.Key, p => (byte[])p.Value.Clone());
            var memory = new CostumeCheck.Source { Where = work, Names = cached.Keys.ToList(), Read = n => { byte[] b; return cached.TryGetValue(n, out b) ? b : null; } };
            CostumeCheck.Fix(game, memory, new List<string>(), Path.Combine(args[1], "from memory.zip"));
            Check(cached.All(p => p.Value.SequenceEqual(kept[p.Key])), "the source's bytes stay as they were");

            var done = new List<string>();
            string copy = CostumeCheck.Fix(game, CostumeCheck.FromFolder(work), done);
            foreach (string d in done) Console.WriteLine("  fixed: " + d);
            var fixedResult = CostumeCheck.Check(game, CostumeCheck.FromFolder(copy)).Single();
            Check(fixedResult.Verdict == CostumeCheck.Verdict.Ok || fixedResult.Verdict == CostumeCheck.Verdict.Mirror, "the fixed copy is OK (" + fixedResult.Verdict + ")");
            Check(File.ReadAllBytes(model).SequenceEqual(before), "the original stays as it was");
            Check(done.Any(d => d.Contains("AGL_90_03: eyes -> eye")) && !done.Any(d => d.Contains("in the model")), "color 3 gets its eye back, the model keeps its names");

            // a folder of several costumes (as the game's chara folder): one of them fixed into a zip of its own, as the
            // results window does
            foreach (string f in Directory.GetFiles(copy, "AGL_90*")) File.Copy(f, Path.Combine(work, Path.GetFileName(f).Replace("AGL_90", "AGL_91")), true);
            var both = CostumeCheck.Check(game, CostumeCheck.FromFolder(work));
            Check(both.Count == 2 && both.Single(x => x.Name == "AGL_91").Verdict != CostumeCheck.Verdict.Faulty, "a folder of two: AGL_91 (the fixed files) isn't faulty");
            string zip = Path.Combine(args[1], "AGL_90 - fixed.zip");
            var zipDone = new List<string>();
            CostumeCheck.Fix(game, CostumeCheck.Only(CostumeCheck.FromFolder(work), "AGL_90"), zipDone, zip);
            var inZip = CostumeCheck.FromZip(zip);
            Check(inZip.Names.All(n => n.StartsWith("AGL_90") || n == "slot_AGL_90.txt" || n == "needs_exms_0.97.txt") && inZip.Names.Count > 20,
                  "the zip holds AGL_90's files only (" + inZip.Names.Count + ")");
            Check(inZip.Names.Contains("slot_AGL_90.txt") && inZip.Names.Contains("needs_exms_0.97.txt") && !inZip.Names.Contains("slot_AGL_91.txt"), "the mod's marker files go with it");
            var zipResult = CostumeCheck.Check(game, inZip).Single();
            Check(zipResult.Verdict != CostumeCheck.Verdict.Faulty, "the zipped copy isn't faulty (" + zipResult.Verdict + ")");
            Check(Refused(() => CostumeCheck.Fix(game, inZip, new List<string>(), zip)), "a copy over the zip itself is refused");

            // more costumes made from the fixed copy, each with one thing wrong, in one folder
            string more = Path.Combine(args[1], "more");
            Action<string, Func<byte[], byte[]>> made = (stem, change) =>
            {
                CopyAs(copy, "AGL_90", more, stem);
                string file = Path.Combine(more, stem + ".obj.emo");
                File.WriteAllBytes(file, change(File.ReadAllBytes(file)));
            };
            // AGL_92: color 3 lacks a material, nothing else
            made("AGL_92", d => d);
            byte[] e92 = File.ReadAllBytes(Path.Combine(more, "AGL_92_03.obj.emm"));
            int t92 = BitConverter.ToInt32(e92, 0xc);
            e92[t92 + BitConverter.ToInt32(e92, t92 + 4) + 3] = (byte)'s';
            File.WriteAllBytes(Path.Combine(more, "AGL_92_03.obj.emm"), e92);
            // AGL_93: the model cut in half
            made("AGL_93", d => d.Take(d.Length / 2).ToArray());
            // AGL_94: 25 bones in one submesh's palette (its last indices made the palette's first nodes, bone 0)
            made("AGL_94", d =>
            {
                int sub = Submeshes(d).OrderByDescending(x => BitConverter.ToUInt16(d, x + 4)).First();
                int idx = BitConverter.ToUInt16(d, sub + 2), count = BitConverter.ToUInt16(d, sub + 4), extra = 25 - count;
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)(idx - extra)), 0, d, sub + 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)25), 0, d, sub + 4, 2);
                for (int k = 0; k < extra; k++) Buffer.BlockCopy(BitConverter.GetBytes((ushort)0), 0, d, sub + 38 + (idx - extra + k) * 2, 2);
                return d;
            });
            // AGL_95: the neck's parent made the neck's child (a circle)
            made("AGL_95", d =>
            {
                int sk = BitConverter.ToInt32(d, 16), nd = sk + BitConverter.ToInt32(d, sk + 8), n = BoneIndex(d, "Neck");
                int parent = BitConverter.ToUInt16(d, nd + n * 80);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)n), 0, d, nd + parent * 80, 2);
                return d;
            });
            // AGL_05: in a game slot
            made("AGL_05", d => d);
            File.WriteAllBytes(Path.Combine(more, "damaged.zip"), new byte[] { 1, 2, 3, 4, 5 });

            var all = CostumeCheck.Find(more).SelectMany(x => CostumeCheck.Check(game, x)).ToList();
            foreach (var r in all) Console.WriteLine("  " + r.Name + " " + r.Verdict + ": " + string.Join(" | ", r.Findings.Select(f => f.Kind + " " + f.Text)));
            Func<string, CostumeCheck.Result> named = n => all.FirstOrDefault(r => r.Name == n);
            Check(named("AGL_92") != null && named("AGL_92").Verdict == CostumeCheck.Verdict.Look && named("AGL_92").Findings.Any(f => f.Kind == "WHITE" && f.Fixable),
                  "a white color alone is worth a look, and fixable");
            Check(named("AGL_93") != null && named("AGL_93").Verdict == CostumeCheck.Verdict.Faulty && named("AGL_93").Findings.Any(f => f.Kind == "BROKEN"), "a model cut short is BROKEN");
            Check(named("AGL_94") != null && named("AGL_94").Findings.Any(f => f.Kind == "PALETTE" && f.Text.StartsWith("25 ")), "25 bones in one palette is PALETTE");
            Check(named("AGL_95") != null, "a skeleton in a circle is checked (" + (named("AGL_95") == null ? "-" : named("AGL_95").Verdict.ToString()) + ")");
            Check(named("AGL_05") != null && named("AGL_05").Findings.Any(f => f.Kind == "SLOT"), "a game slot picked by hand is SLOT");
            Check(named("damaged.zip") != null && named("damaged.zip").Findings.Any(f => f.Kind == "UNREAD"), "a damaged zip couldn't be read");
            Check(all.All(r => r.Findings.All(f => !f.Text.Contains("Ember"))), "no finding names Ember");
            var circleDone = new List<string>();
            CostumeCheck.Fix(game, CostumeCheck.Only(CostumeCheck.FromFolder(more), "AGL_95"), circleDone, Path.Combine(args[1], "AGL_95 - fixed.zip"));
            Check(true, "a skeleton in a circle is fixed without hanging");

            var installed = CostumeCheck.FromFolder(more);
            installed.CustomOnly = true;
            Check(!CostumeCheck.Check(game, installed).Any(r => r.Name == "AGL_05"), "the installed check leaves the game's slots 01-07 out");
            var picked = CostumeCheck.Find(Path.Combine(more, "AGL_92_03.obj.emm")).SelectMany(x => CostumeCheck.Check(game, x)).ToList();
            Check(picked.Count == 1 && picked[0].Name == "AGL_92", "a file picked checks its costume only");

            // the results window, off screen, as a picture (args[2])
            if (args.Length > 2)
            {
                var window = new CostumeCheckWindow(game, "Costume check: test", both.Concat(all).ToList())
                { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-4000, -4000) };
                window.Shown += (o, e) =>
                {
                    using (var shot = new System.Drawing.Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, window.Width, window.Height));
                        shot.Save(args[2]);
                    }
                    window.Close();
                };
                System.Windows.Forms.Application.Run(window);
                Check(File.Exists(args[2]), "the results window drew");
            }
            Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
            Environment.Exit(failed == 0 ? 0 : 1);
        }
    }
}
