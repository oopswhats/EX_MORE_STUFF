// The costume renderer against the Python renderer it is ported from (usf4-nesica tools). Modes:
//   pose <game> <folder> <CHR_NN> <python dump.txt> [<animation> <frame> [2p]]   the posed mesh, vertex by vertex,
//        against dump_pose.py's (same arguments)
//   animations <game> <CHR>   the fighter's animations: name and frame count
//   viewer <game> <folder> <CHR_NN> <out prefix>   the View... window off screen: <prefix>-1.png as it opens, <prefix>-2.png
//        after color 12, crouch frame 10, turned 180, 2P (set as its controls would)
//   draw <game> <folder> <CHR_NN> <color> <out.png> [<python picture.png>] [quick]   a picture (600 x 800; quick: the
//        viewer's quick look, 300 x 400), its time and peak memory, and how far it is from the Python renderer's (or
//        any other picture's): mean and 99th percentile of the per-pixel difference (0-255), coverage
//   damaged <game> <folder> <CHR_NN>   the costume with its skeleton made to loop: an error, not a stack overflow
//   pack <game> <work folder>   a color pack (Ryu's costume 1, color 31 made of the game's color 5) drawn as the
//        Costume creation page does, without the game
// Build: csc -target:exe -main:ExMoreStuff.CostumeRenderTest (the references of build.bat) test\CostumeRenderTest.cs src\*.cs
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class CostumeRenderTest
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args[0] == "pose") return Pose(args[1], args[2], args[3], args[4], args.Length > 5 ? int.Parse(args[5]) : 0, args.Length > 6 ? double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 0, args.Length <= 7 || args[7] != "2p");
            if (args[0] == "animations") { foreach (var a in CostumePose.Animations(File.ReadAllBytes(CostumePose.GameFile(args[1], args[2], args[2] + ".obj.ema")))) Console.WriteLine(a.Key + " " + a.Value); return 0; }
            if (args[0] == "pack") return Pack(args[1], args[2]);
            if (args[0] == "viewer") return Viewer(args[1], args[2], args[3], args[4]);
            if (args[0] == "draw") return Draw(args[1], args[2], args[3], int.Parse(args[4]), args[5], args.Length > 6 && args[6] != "quick" ? args[6] : null, args.Contains("quick"));
            if (args[0] == "damaged") return Damaged(args[1], args[2], args[3]);
            Console.WriteLine("unknown mode");
            return 2;
        }

        static int Viewer(string game, string folder, string stem, string prefix)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            var viewer = new CostumeViewer(game, stem, stem + " (test)", () => new CostumeRender.Costume(game, stem, name => File.Exists(Path.Combine(folder, name)) ? File.ReadAllBytes(Path.Combine(folder, name)) : null))
            { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-4000, -4000) };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Func<string, object> field = n => typeof(CostumeViewer).GetField(n, flags).GetValue(viewer);
            var info = (System.Windows.Forms.Label)field("info");
            int step = 0, code = 1;
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (s, e) =>
            {
                if (info.Text.StartsWith("It ")) { Console.WriteLine("FAIL  " + info.Text); timer.Stop(); viewer.Close(); return; }
                // the full picture (a quick look says no time), nothing more waiting to be drawn
                if (!info.Text.Contains("drawn in") || (bool)field("busy") || field("pending") != null || ((System.Windows.Forms.Timer)field("settle")).Enabled) return;
                using (var shot = new System.Drawing.Bitmap(viewer.Width, viewer.Height))
                {
                    viewer.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, viewer.Width, viewer.Height));
                    shot.Save(prefix + "-" + (step + 1) + ".png");
                }
                Console.WriteLine("ok    " + info.Text);
                if (step++ == 1) { code = 0; timer.Stop(); viewer.Close(); return; }
                typeof(CostumeViewer).GetField("color", flags).SetValue(viewer, 12);
                typeof(CostumeViewer).GetField("mirror", flags).SetValue(viewer, false);
                ((System.Windows.Forms.ComboBox)field("stance")).SelectedIndex = 1;
                ((ValueSlider)field("frame")).Value = 10;
                ((ValueSlider)field("turn")).Value = 180;
                info.Text = "";
            };
            viewer.Shown += (s, e) => timer.Start();
            System.Windows.Forms.Application.Run(viewer);
            return code;
        }

        static int Pack(string game, string work)
        {
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "RYU_01_31.zip");
            if (File.Exists(zip)) File.Delete(zip);
            using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
                System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(z, CostumePose.GameFile(game, "RYU", "RYU_01_05.col.emb"), "RYU_01_31.col.emb");
            var pack = new Installed { Item = new Item { Type = "color", Fighter = "RYU", Slot = 1, Color = 31 }, Package = zip };
            var made = PerfTest.DrawnPacks(game, new[] { pack }, Console.WriteLine, false);
            if (made == null || !made.ContainsKey(31)) { Console.WriteLine("FAIL  the pack wasn't drawn"); return 1; }
            made[31].Save(Path.Combine(work, "RYU_01_31.png"), System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("ok    color 31 drawn, " + made[31].Width + " x " + made[31].Height);
            return made[31].Width == PerfTest.PictureWidth && made[31].Height == PerfTest.PictureHeight ? 0 : 1;
        }

        // a costume whose skeleton loops (a bone its own parent; two bones each other's) gives an error, not a stack overflow
        static int Damaged(string game, string folder, string stem)
        {
            int code = 0;
            byte[] good = File.ReadAllBytes(Path.Combine(folder, stem + ".obj.emo"));
            int b = BitConverter.ToInt32(good, 16), start = BitConverter.ToInt32(good, b + 8);
            foreach (var loop in new[] { new[] { 5, 5 }, new[] { 3, 7, 7, 3 } })
            {
                var emo = (byte[])good.Clone();
                for (int i = 0; i < loop.Length; i += 2) BitConverter.GetBytes((ushort)loop[i + 1]).CopyTo(emo, b + start + loop[i] * 80);
                try
                {
                    new CostumeRender.Costume(game, stem, name => name == stem + ".obj.emo" ? emo : File.Exists(Path.Combine(folder, name)) ? File.ReadAllBytes(Path.Combine(folder, name)) : null);
                    Console.WriteLine("FAIL  a looping skeleton (" + string.Join(",", loop) + ") was drawn");
                    code = 1;
                }
                catch (CostumePose.Damaged ex) { Console.WriteLine("ok    " + ex.Message); }
            }
            return code;
        }

        static int Draw(string game, string folder, string stem, int color, string output, string reference, bool quick)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var costume = new CostumeRender.Costume(game, stem, name => File.Exists(Path.Combine(folder, name)) ? File.ReadAllBytes(Path.Combine(folder, name)) : null);
            long posed = watch.ElapsedMilliseconds;
            using (var picture = quick ? costume.Draw(color, 300, 400, CostumeRender.YawDegrees, true) : costume.Draw(color))
            {
                Console.WriteLine("posed in " + posed + " ms, drawn in " + (watch.ElapsedMilliseconds - posed) + " ms, peak memory " +
                                  System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024) + " MB");
                picture.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                if (reference == null) return 0;
                using (var theirs = new System.Drawing.Bitmap(reference))
                {
                    var diffs = new System.Collections.Generic.List<int>();
                    int mineOnly = 0, theirsOnly = 0;
                    for (int y = 0; y < picture.Height; y++)
                        for (int x = 0; x < picture.Width; x++)
                        {
                            var a = picture.GetPixel(x, y); var b = theirs.GetPixel(x, y);
                            if (a.A > 128 != b.A > 128) { if (a.A > 128) mineOnly++; else theirsOnly++; }
                            if (a.A == 0 && b.A == 0) continue;
                            diffs.Add(Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)))));
                        }
                    diffs.Sort();
                    Console.WriteLine("difference: mean " + diffs.Average().ToString("0.0") + ", 99th percentile " + diffs[(int)(diffs.Count * 0.99)] +
                                      "; covered only here " + mineOnly + " px, only there " + theirsOnly + " px");
                }
            }
            return 0;
        }

        // largest position and normal differences per part, and the kept triangles per submesh, against Python's dump
        static int Pose(string game, string folder, string stem, string dump, int animation, double frame, bool mirror)
        {
            string fighter = stem.Substring(0, 3);
            string bsr = Path.Combine(folder, stem + ".bsr");
            var parts = CostumePose.Posed(File.ReadAllBytes(Path.Combine(folder, stem + ".obj.emo")),
                                          File.ReadAllBytes(CostumePose.GameFile(game, fighter, fighter + ".obj.ema")),
                                          File.Exists(bsr) ? File.ReadAllBytes(bsr) : null, fighter, animation, frame, mirror);
            var lines = File.ReadAllLines(dump);
            int at = 0, bad = 0;
            double worstP = 0, worstN = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var head = lines[at++].Split(' ');
                int count = int.Parse(head[1]);
                string mine = string.Join(" ", parts[i].Subs.Select(s => s.Triangles.Length / 3)), theirs = string.Join(" ", head.Skip(2));
                if (count != parts[i].Positions.Length / 3 || mine != theirs) { Console.WriteLine("part " + i + ": triangles " + mine + " vs " + theirs); bad++; }
                double dp = 0, dn = 0;
                int worstVertex = 0;
                for (int v = 0; v < count; v++)
                {
                    var f = lines[at++].Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    for (int c = 0; c < 3; c++)
                    {
                        double d = Math.Abs(parts[i].Positions[v * 3 + c] - f[c]);
                        if (d > dp) { dp = d; worstVertex = v; }
                        dn = Math.Max(dn, Math.Abs(parts[i].Normals[v * 3 + c] - f[3 + c]));
                    }
                }
                Console.WriteLine("part " + i + ": " + count + " vertices, position off by " + dp.ToString("0.0e0") + " (vertex " + worstVertex + "), normal " + dn.ToString("0.0e0"));
                worstP = Math.Max(worstP, dp); worstN = Math.Max(worstN, dn);
            }
            Console.WriteLine("worst: position " + worstP.ToString("0.0e0") + ", normal " + worstN.ToString("0.0e0"));
            return bad == 0 && worstP < 1e-4 ? 0 : 1;
        }
    }
}
