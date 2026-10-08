// Decodes a few fighters' character-select portraits with GameArt (the program's own code) into PNGs side by
// side, to check the DXT5 decoder and the cropping. Usage: PortraitTest <game folder> <out.png>
using System.Drawing;
using System.Drawing.Imaging;

namespace ExMoreStuff
{
    static class PortraitTest
    {
        static void Main(string[] args)
        {
            string[] codes = { "RYU", "JHA", "HWK", "DCP", "YUN", "CNL" };
            using (var sheet = new Bitmap(codes.Length * 200, 300))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(30, 30, 34));
                for (int i = 0; i < codes.Length; i++)
                    using (Bitmap p = GameArt.FighterPortrait(args[0], codes[i], 280))
                        if (p != null) g.DrawImage(p, i * 200 + (200 - p.Width * 280 / p.Height) / 2, 10, p.Width * 280 / p.Height, 280);
                sheet.Save(args[1], ImageFormat.Png);
            }
        }
    }
}
