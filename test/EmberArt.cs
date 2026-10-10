// Makes res\costumes from SF4 Ember's selection art (assets\selection\<CODE>\costume-<N>\color-<C>-cutout.png, N and C
// counted from 0; its README credits the photographs): one JPEG sheet per costume, <CODE>_<NN>.jpg, a cell per color
// (color 1 first) at CostumeArt's cell size on the costume tiles' dark background, and index.txt with each sheet's
// colors ("CNL_02 111111111111": 1 = that color has a picture). Usage: EmberArt <Ember's assets\selection> <res\costumes>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

static class EmberArt
{
    const int CellWidth = 96, CellHeight = 144;   // CostumeArt's
    static readonly Color Background = Color.FromArgb(10, 11, 14);   // Theme.Well

    static void Main(string[] args)
    {
        string from = args[0], to = args[1];
        Directory.CreateDirectory(to);
        foreach (string old in Directory.GetFiles(to)) File.Delete(old);
        ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
        var index = new List<string>();
        long bytes = 0;
        int pictures = 0;
        foreach (string fighter in Directory.GetDirectories(from).OrderBy(d => d))
        {
            string code = Path.GetFileName(fighter);
            if (!Regex.IsMatch(code, "^[A-Z]{3}$")) continue;
            foreach (string costumeDir in Directory.GetDirectories(fighter, "costume-*").OrderBy(d => d))
            {
                int costume = int.Parse(Path.GetFileName(costumeDir).Substring(8)) + 1;
                var colors = new Dictionary<int, string>();
                foreach (string file in Directory.GetFiles(costumeDir, "color-*-cutout.png"))
                {
                    Match m = Regex.Match(Path.GetFileName(file), @"^color-(\d+)-cutout\.png$");
                    if (m.Success) colors[int.Parse(m.Groups[1].Value) + 1] = file;
                }
                if (colors.Count == 0) continue;
                int count = colors.Keys.Max();
                using (var sheet = new Bitmap(CellWidth * count, CellHeight))
                {
                    using (Graphics g = Graphics.FromImage(sheet))
                    {
                        g.Clear(Background);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        foreach (var color in colors)
                            using (var picture = (Bitmap)Image.FromFile(color.Value))
                            {
                                float scale = Math.Min((float)CellWidth / picture.Width, (float)CellHeight / picture.Height);
                                float w = picture.Width * scale, h = picture.Height * scale;
                                g.DrawImage(picture, new RectangleF((color.Key - 1) * CellWidth + (CellWidth - w) / 2, CellHeight - h, w, h));
                                pictures++;
                            }
                    }
                    string name = code + "_" + costume.ToString("D2");
                    string path = Path.Combine(to, name + ".jpg");
                    sheet.Save(path, jpeg, quality);
                    bytes += new FileInfo(path).Length;
                    index.Add(name + " " + string.Concat(Enumerable.Range(1, count).Select(c => colors.ContainsKey(c) ? "1" : "0")));
                }
            }
        }
        File.WriteAllLines(Path.Combine(to, "index.txt"), index);
        Console.WriteLine("{0} pictures in {1} sheets: {2:N1} MB", pictures, index.Count, bytes / 1048576.0);
    }
}
