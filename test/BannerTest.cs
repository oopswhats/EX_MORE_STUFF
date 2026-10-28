// The banner maker's layouts (Banner, user 2026-10-27) on a costume's real color pictures: 1 color at the left, 4 in a
// line, 7 as 4 + 3 facing each other, 20 in two rows, 20 cut from the bottom, 7 spread wider; each saved as a picture to look at.
// Usage: BannerTest <a package zip with <prefix>_NN.png color pictures> <output folder>   (with all of src\, -main:ExMoreStuff.BannerTest)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    static class BannerTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        // the columns where the banner isn't black
        static List<int> Lit(Bitmap b)
        {
            var columns = new List<int>();
            for (int x = 0; x < b.Width; x += 4)
                for (int y = 0; y < b.Height; y += 4)
                    if (b.GetPixel(x, y).ToArgb() != Color.Black.ToArgb()) { columns.Add(x); break; }
            return columns;
        }

        static void Main(string[] args)
        {
            Directory.CreateDirectory(args[1]);
            var pictures = new List<Bitmap>();
            using (ZipArchive zip = ZipFile.OpenRead(args[0]))
                foreach (var entry in zip.Entries.Where(e => Regex.IsMatch(e.Name, @"^[A-Za-z]{3}_\d\d_\d\d\.png$")).OrderBy(e => e.Name))
                    using (Stream s = entry.Open())
                    using (var data = new MemoryStream())
                    {
                        s.CopyTo(data);
                        using (var image = Image.FromStream(new MemoryStream(data.ToArray()))) pictures.Add(new Bitmap(image));
                    }
            Check(pictures.Count > 0, pictures.Count + " color pictures read");
            if (pictures.Count == 0) Environment.Exit(1);
            var boxes = pictures.Select(Banner.Trim).ToList();
            Check(boxes.All(b => b.Width > 0 && b.Height > 0 && b.Width <= pictures[0].Width), "each fighter's box found: " + boxes[0]);
            Func<int, Banner.Settings, string, Bitmap> make = (n, s, file) =>
            {
                var picked = Enumerable.Range(0, n).Select(i => i % pictures.Count).ToList();
                Bitmap b = Banner.Draw(picked.Select(i => pictures[i]).ToList(), picked.Select(i => boxes[i]).ToList(), s);
                b.Save(Path.Combine(args[1], file), ImageFormat.Png);
                return b;
            };
            using (Bitmap one = make(1, new Banner.Settings(), "1 color.png"))
            {
                var lit = Lit(one);
                Check(one.Width == 1680 && one.Height == 840, "1680 x 840");
                Check(lit.Count > 0 && lit.Max() < 1680 / 2 && lit.Min() < 200, "1 color: at the left (columns " + lit.Min() + "-" + lit.Max() + ")");
            }
            using (Bitmap four = make(4, new Banner.Settings(), "4 colors.png"))
                Check(Lit(four).Max() > 1680 - 200, "4 colors: in a line across");
            using (Bitmap seven = make(7, new Banner.Settings(), "7 colors.png"))
            {
                var lit = Lit(seven);
                Check(lit.Min() < 200 && lit.Max() > 1680 - 200, "7 colors: from both sides");
            }
            using (Bitmap twenty = make(20, new Banner.Settings(), "20 colors.png")) { }
            using (Bitmap cut = make(20, new Banner.Settings { Crop = 45, Size = 100 }, "20 colors cut.png")) { }
            using (Bitmap clear = make(3, new Banner.Settings { SeeThrough = true }, "3 colors see-through.png"))
                Check(clear.GetPixel(1679, 0).A == 0, "see-through: the corner is clear");
            using (Bitmap wide = make(10, new Banner.Settings { Spread = 160, Crop = 35, Size = 94 }, "10 colors wider.png")) { }
            // the window, off screen, with a pack's numbers (30 up)
            var numbered = new SortedDictionary<int, Bitmap>();
            for (int i = 0; i < 14; i++) numbered[30 + i] = new Bitmap(pictures[i % pictures.Count]);
            using (var window = new BannerWindow("Test", numbered, Path.Combine(args[1], "unused.png")) { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(-4000, -4000) })
            {
                window.Show();
                System.Windows.Forms.Application.DoEvents();
                using (var shot = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(shot, new Rectangle(0, 0, window.Width, window.Height));
                    shot.Save(Path.Combine(args[1], "window.png"), ImageFormat.Png);
                }
            }
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
