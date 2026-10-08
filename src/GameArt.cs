using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ExMoreStuff
{
    // Pictures read from the player's own game, so EX More Stuff carries none of the game's art: each fighter's
    // character-select portrait (ui\chara_select\chara\sel_<CHR>.tex.emz, a 1024x1024 DXT5 texture, the same file
    // EMBER shows). The game stages' pictures are EMBER's (built in, Stages.Picture).
    static class GameArt
    {
        static readonly string[] Roots = { "patch_ae2_tu3", "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" };

        public static Bitmap FighterPortrait(string game, string code, int height)
        {
            foreach (string root in Roots)
            {
                string path = Path.Combine(game, root, "ui", "chara_select", "chara", "sel_" + code + ".tex.emz");
                if (!File.Exists(path)) continue;
                var entries = ReadContainer(Unpack(File.ReadAllBytes(path)));
                if (entries.Count == 0) return null;
                using (Bitmap full = DecodeDds(entries[0].Value))
                    return full == null ? null : CropToContent(full, height);
            }
            return null;
        }

        // An .emz is "#EMZ", CRC, size, header size 16, then deflated data; inside it an #EMB container.
        internal static byte[] Unpack(byte[] file)
        {
            if (file.Length < 16 || Encoding.ASCII.GetString(file, 0, 4) != "#EMZ") return file;
            int skip = 16 + (file[16] == 0x78 ? 2 : 0);          // a zlib header, or raw deflate
            using (var input = new DeflateStream(new MemoryStream(file, skip, file.Length - skip), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        internal static List<KeyValuePair<string, byte[]>> ReadContainer(byte[] d)
        {
            var list = new List<KeyValuePair<string, byte[]>>();
            if (d.Length < 32 || Encoding.ASCII.GetString(d, 0, 4) != "#EMB") return list;
            int count = BitConverter.ToInt32(d, 12), table = BitConverter.ToInt32(d, 24), names = BitConverter.ToInt32(d, 28);
            for (int i = 0; i < count; i++)
            {
                int entry = table + i * 8, start = BitConverter.ToInt32(d, entry) + entry, length = BitConverter.ToInt32(d, entry + 4);
                string name = "";
                if (names != 0)
                {
                    int at = BitConverter.ToInt32(d, names + i * 4), end = Array.IndexOf(d, (byte)0, at);
                    name = Encoding.ASCII.GetString(d, at, end - at);
                }
                var data = new byte[length];
                Buffer.BlockCopy(d, start, data, 0, length);
                list.Add(new KeyValuePair<string, byte[]>(name, data));
            }
            return list;
        }

        // The top level of a DXT1/DXT3/DXT5 or 32-bit DDS texture.
        internal static Bitmap DecodeDds(byte[] d)
        {
            if (d.Length < 128 || Encoding.ASCII.GetString(d, 0, 4) != "DDS ") return null;
            int height = BitConverter.ToInt32(d, 12), width = BitConverter.ToInt32(d, 16);
            string fourcc = Encoding.ASCII.GetString(d, 84, 4);
            var pixels = new byte[width * height * 4];             // B, G, R, A
            if (fourcc == "DXT1" || fourcc == "DXT3" || fourcc == "DXT5")
            {
                int blockBytes = fourcc == "DXT1" ? 8 : 16, at = 128;
                for (int by = 0; by < height; by += 4)
                    for (int bx = 0; bx < width; bx += 4, at += blockBytes)
                        DecodeBlock(d, at, fourcc, pixels, width, height, bx, by);
            }
            else if (BitConverter.ToInt32(d, 88) == 32)
                Buffer.BlockCopy(d, 128, pixels, 0, Math.Min(pixels.Length, d.Length - 128));
            else return null;
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData lockData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width * 4, lockData.Scan0 + y * lockData.Stride, width * 4);
            bitmap.UnlockBits(lockData);
            return bitmap;
        }

        static void DecodeBlock(byte[] d, int at, string fourcc, byte[] pixels, int width, int height, int bx, int by)
        {
            var alpha = new byte[16];
            int colour = at;
            if (fourcc == "DXT3")
            {
                for (int i = 0; i < 16; i++) alpha[i] = (byte)(((d[at + i / 2] >> (4 * (i % 2))) & 15) * 17);
                colour = at + 8;
            }
            else if (fourcc == "DXT5")
            {
                byte a0 = d[at], a1 = d[at + 1];
                var table = new byte[8];
                table[0] = a0; table[1] = a1;
                if (a0 > a1) for (int i = 1; i < 7; i++) table[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                else { for (int i = 1; i < 5; i++) table[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5); table[6] = 0; table[7] = 255; }
                ulong bits = 0;
                for (int i = 0; i < 6; i++) bits |= (ulong)d[at + 2 + i] << (8 * i);
                for (int i = 0; i < 16; i++) alpha[i] = table[(bits >> (3 * i)) & 7];
                colour = at + 8;
            }
            else for (int i = 0; i < 16; i++) alpha[i] = 255;
            int c0 = BitConverter.ToUInt16(d, colour), c1 = BitConverter.ToUInt16(d, colour + 2);
            uint indices = BitConverter.ToUInt32(d, colour + 4);
            var r = new int[4]; var g = new int[4]; var b = new int[4]; var a = new bool[4] { true, true, true, true };
            Rgb565(c0, out r[0], out g[0], out b[0]);
            Rgb565(c1, out r[1], out g[1], out b[1]);
            if (c0 > c1 || fourcc != "DXT1")
            {
                r[2] = (2 * r[0] + r[1]) / 3; g[2] = (2 * g[0] + g[1]) / 3; b[2] = (2 * b[0] + b[1]) / 3;
                r[3] = (r[0] + 2 * r[1]) / 3; g[3] = (g[0] + 2 * g[1]) / 3; b[3] = (b[0] + 2 * b[1]) / 3;
            }
            else
            {
                r[2] = (r[0] + r[1]) / 2; g[2] = (g[0] + g[1]) / 2; b[2] = (b[0] + b[1]) / 2;
                a[3] = false;
            }
            for (int i = 0; i < 16; i++)
            {
                int x = bx + i % 4, y = by + i / 4;
                if (x >= width || y >= height) continue;
                int k = (int)((indices >> (2 * i)) & 3), p = (y * width + x) * 4;
                pixels[p] = (byte)b[k]; pixels[p + 1] = (byte)g[k]; pixels[p + 2] = (byte)r[k];
                pixels[p + 3] = a[k] ? alpha[i] : (byte)0;
            }
        }

        static void Rgb565(int c, out int r, out int g, out int b)
        {
            r = ((c >> 11) & 31) * 255 / 31; g = ((c >> 5) & 63) * 255 / 63; b = (c & 31) * 255 / 31;
        }

        // The fighter without the empty canvas around them, scaled to `height` (as EMBER crops its portraits).
        static Bitmap CropToContent(Bitmap full, int height)
        {
            int left = full.Width, top = full.Height, right = 0, bottom = 0;
            BitmapData data = full.LockBits(new Rectangle(0, 0, full.Width, full.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new byte[full.Width * 4];
            for (int y = 0; y < full.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < full.Width; x++)
                    if (row[x * 4 + 3] >= 8)
                    {
                        if (x < left) left = x;
                        if (x >= right) right = x + 1;
                        if (y < top) top = y;
                        bottom = y + 1;
                    }
            }
            full.UnlockBits(data);
            if (right <= left || bottom <= top) { left = top = 0; right = full.Width; bottom = full.Height; }
            var source = new Rectangle(left, top, right - left, bottom - top);
            int width = Math.Max(1, source.Width * height / source.Height);
            var thumb = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics gfx = Graphics.FromImage(thumb))
            {
                gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gfx.DrawImage(full, new Rectangle(0, 0, width, height), source, GraphicsUnit.Pixel);
            }
            return thumb;
        }
    }
}
