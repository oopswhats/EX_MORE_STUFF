// Contact sheet of every picture in the game's stage-select package (named stage icons and the unnamed
// StageSelect_I*.dds ones), to find the stage icons and their look. Usage: StageIconSheet <emz> <out.png>
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ExMoreStuff
{
    static class StageIconSheet
    {
        static void Main(string[] args)
        {
            var entries = GameArt.ReadContainer(GameArt.Unpack(File.ReadAllBytes(args[0])));
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Bitmap>>();
            foreach (var e in entries)
            {
                if (!e.Key.EndsWith(".dds")) continue;
                Bitmap b = GameArt.DecodeDds(e.Value);
                if (b != null && b.Width <= 256 && b.Height <= 256) list.Add(new System.Collections.Generic.KeyValuePair<string, Bitmap>(e.Key, b));
            }
            int cols = 10, cell = 140, rows = (list.Count + cols - 1) / cols;
            using (var sheet = new Bitmap(cols * cell, rows * (cell + 16)))
            using (Graphics g = Graphics.FromImage(sheet))
            using (var font = new Font("Segoe UI", 8f))
            {
                g.Clear(Color.FromArgb(60, 60, 70));
                for (int i = 0; i < list.Count; i++)
                {
                    int x = i % cols * cell, y = i / cols * (cell + 16);
                    Bitmap b = list[i].Value;
                    float s = System.Math.Min(128f / b.Width, 128f / b.Height);
                    g.DrawImage(b, x + 6, y + 2, b.Width * s, b.Height * s);
                    g.DrawString(list[i].Key.Replace(".dds", "") + " " + b.Width + "x" + b.Height, font, Brushes.White, x + 4, y + cell - 8);
                }
                sheet.Save(args[1], ImageFormat.Png);
            }
        }
    }
}
