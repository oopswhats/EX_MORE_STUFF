using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // A costume's banner (user 2026-10-27: "a very easy image editor thing" on Costume creation, once its color pictures are
    // made): its color pictures laid out on a 1680 x 840 picture. One color stands at the left, filling the height with room
    // to spare; more stand in a line; 6 or more split in two groups facing each other (the right group mirrored, the left
    // one taking the odd one: 7 is 4 + 3); past 10 they get smaller in two rows. Sliders set their size, how far they spread,
    // the gap between the rows and how much of them is cut from the bottom up. The banner goes next to the costume's zip.
    static class Banner
    {
        public const int Width = 1680, Height = 840;

        public sealed class Settings
        {
            public int Size = 100;      // % of the row's height a color's picture takes (50-150)
            public int Spread = 100;    // % of the room they spread over (0: all in one place, 100: filling it, up to 200: further, off the edges)
            public int RowGap = 20;     // px between the two rows (0-120)
            public int Crop = 0;        // % of each picture cut from the bottom up (0-70)
            public int UpDown = 0;      // px everything moves down (up when below 0; -300-300)
            public int MiddleGap = 0;   // px between the two groups facing each other (below 0: pulled closer, over each other; -400-400)
            public bool SeeThrough;     // no black behind them
        }

        /// <summary>Where a picture's fighter is: the box around its pixels that aren't see-through (all of it when none are).</summary>
        public static Rectangle Trim(Bitmap picture)
        {
            int w = picture.Width, h = picture.Height, left = w, top = h, right = -1, bottom = -1;
            BitmapData data = picture.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                    {
                        if (row[x * 4 + 3] < 16) continue;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        bottom = y;
                    }
                }
            }
            finally { picture.UnlockBits(data); }
            return right < 0 ? new Rectangle(0, 0, w, h) : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        // where each picture goes: its row, its place in the row, whether it's mirrored, in drawing order (front ones last)
        sealed class Spot { public int Index, Row; public float X; public bool Mirrored; public int Order; }

        // how far apart k pictures w wide stand: spread evenly over the room, but never closer than an eighth of one (big
        // ones, cut from the bottom, leave little room: user 2026-10-27 "there is space for them to co-exist more evenly")
        static float Step(float room, int k, float w) { return Math.Max(room / (k - 1), w / 8f); }

        static List<Spot> Lay(int count, int rows, Func<int, float> widthOf, Settings s, float margin)
        {
            var spots = new List<Spot>();
            bool facing = count >= 6;
            int first = (count + rows - 1) / rows;
            for (int row = 0, start = 0; row < rows; row++)
            {
                int k = row == 0 ? first : count - first;
                if (k <= 0) break;
                // the first color at the outside, in front; each next one further in and behind the one before (user
                // 2026-10-27: "layered from behind not the front", then "the order is reversed" when the first stood in
                // the middle). Spread 100% fills the room; past it they spread further, the outer ones off the edge.
                float spread = s.Spread / 100f;
                if (!facing)
                {
                    // in a line from the left: the first at the left, in front
                    for (int i = 0; i < k; i++)
                    {
                        float w = widthOf(start + i);
                        float step = k > 1 ? Step(Width - 2 * margin - w, k, w) * spread : 0;
                        spots.Add(new Spot { Index = start + i, Row = row, X = margin + i * step, Order = k - i });
                    }
                }
                else
                {
                    // two groups facing each other, the right one mirrored; the left takes the odd one; each group's first
                    // at its outside edge, the last in the middle (Middle gap apart, or over each other)
                    int left = (k + 1) / 2, right = k - left;
                    float half = s.MiddleGap / 2f;
                    for (int i = 0; i < left; i++)
                    {
                        float w = widthOf(start + i);
                        float step = left > 1 ? Step(Width / 2f - w - margin, left, w) * spread : 0;
                        spots.Add(new Spot { Index = start + i, Row = row, X = Width / 2f - half - w - (left - 1 - i) * step, Order = (k - i) * 2 });
                    }
                    for (int j = 0; j < right; j++)
                    {
                        float w = widthOf(start + left + j);
                        float step = right > 1 ? Step(Width / 2f - w - margin, right, w) * spread : 0;
                        spots.Add(new Spot { Index = start + left + j, Row = row, X = Width / 2f + half + (right - 1 - j) * step, Mirrored = true, Order = (k - j) * 2 + 1 });
                    }
                }
                start += k;
            }
            return spots.OrderBy(p => p.Row).ThenBy(p => p.Order).ToList();
        }

        /// <summary>The banner: `pictures` (each with its Trim box) in order, laid out as Settings say.</summary>
        public static Bitmap Draw(IList<Bitmap> pictures, IList<Rectangle> boxes, Settings s)
        {
            var banner = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(banner))
            {
                g.Clear(s.SeeThrough ? Color.Transparent : Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                int count = pictures.Count;
                if (count == 0) return banner;
                int rows = count > 10 ? 2 : 1;
                float margin = Height * 0.04f, gap = rows > 1 ? s.RowGap : 0;
                float rowHeight = (Height - 2 * margin - gap * (rows - 1)) / rows;
                // each picture's kept part (cut from the bottom up) and its size on the banner
                var source = boxes.Select(b => new RectangleF(b.X, b.Y, b.Width, Math.Max(1, b.Height * (1 - s.Crop / 100f)))).ToList();
                Func<int, float> scale = i => rowHeight * s.Size / 100f / source[i].Height;
                Func<int, float> widthOf = i => source[i].Width * scale(i);
                foreach (Spot p in Lay(count, rows, widthOf, s, margin))
                {
                    float w = widthOf(p.Index), h = source[p.Index].Height * scale(p.Index);
                    float bottom = margin + p.Row * (rowHeight + gap) + rowHeight + s.UpDown;
                    var to = new RectangleF(p.X, bottom - h, w, h);
                    PointF[] corners = p.Mirrored
                        ? new[] { new PointF(to.Right, to.Top), new PointF(to.Left, to.Top), new PointF(to.Right, to.Bottom) }
                        : new[] { new PointF(to.Left, to.Top), new PointF(to.Right, to.Top), new PointF(to.Left, to.Bottom) };
                    g.DrawImage(pictures[p.Index], corners, source[p.Index], GraphicsUnit.Pixel);
                }
            }
            return banner;
        }
    }

    // The banner maker's window: the banner as it will be (half size), the colors to show in a column of their numbers (the
    // first only to begin with; All), the sliders, See-through background, and Save ("<zip name> - banner.png" next to the zip).
    class BannerWindow : Form
    {
        readonly SortedDictionary<int, Bitmap> pictures;
        readonly Dictionary<int, Rectangle> boxes = new Dictionary<int, Rectangle>();
        readonly Dictionary<int, Toggle> picks = new Dictionary<int, Toggle>();
        readonly Banner.Settings settings = new Banner.Settings();
        readonly PictureBox preview = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(40, 40, 40) };
        readonly Label status = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(9.5f) };
        readonly string saveTo;
        bool picking;   // All switching every color: one redraw at the end

        public BannerWindow(string name, SortedDictionary<int, Bitmap> pictures, string saveTo)
        {
            this.pictures = pictures;
            this.saveTo = saveTo;
            Text = "Banner: " + name;
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            const int pad = 20, side = 330, column = 104;
            foreach (var p in pictures) boxes[p.Key] = Banner.Trim(p.Value);
            preview.Bounds = new Rectangle(pad, pad, Banner.Width / 2, Banner.Height / 2);
            Controls.Add(preview);

            // the colors in one column beside the picture, just their numbers, with All over them (user 2026-10-27)
            int cx = preview.Right + pad;
            var all = new FlatButton("All") { Bounds = new Rectangle(cx, pad, column, 30) };
            var colors = new FlowLayoutPanel { Bounds = new Rectangle(cx, all.Bottom + 8, column, preview.Bottom - all.Bottom - 8), AutoScroll = true, FlowDirection = FlowDirection.TopDown,
                                               WrapContents = false, BackColor = Theme.Base };
            foreach (int c in pictures.Keys)
            {
                var t = new Toggle(c.ToString()) { Margin = new Padding(0, 2, 0, 4), Checked = c == pictures.Keys.First() };
                t.CheckedChanged += (s, e) => { if (!picking) Redraw(); };
                picks[c] = t;
                colors.Controls.Add(t);
            }
            all.Click += (s, e) =>
            {
                bool on = !picks.Values.All(t => t.Checked);
                picking = true;
                foreach (Toggle t in picks.Values) t.Checked = on;
                picking = false;
                Redraw();
            };
            Controls.Add(all);
            Controls.Add(colors);
            int x = colors.Right + pad, y = pad;

            Func<string, int, int, int, Action<int>, int> slider = (label, low, high, start, set) =>
            {
                var caption = new Label { AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Text, Bounds = new Rectangle(x, y, side, 20) };
                var bar = new ValueSlider { Maximum = high - low, Value = start - low, Step = 1, Bounds = new Rectangle(x, y + 22, side, 22) };
                Action said = () => caption.Text = label + ": " + (bar.Value + low) + (label == "Row gap" || label == "Up / down" || label == "Middle gap" ? " px" : "%");
                said();
                bar.ValueChanged += () => { said(); set(bar.Value + low); Redraw(); };
                Controls.Add(caption);
                Controls.Add(bar);
                return y + 52;
            };
            y = slider("Size", 50, 150, settings.Size, v => settings.Size = v);
            y = slider("Spread", 0, 200, settings.Spread, v => settings.Spread = v);
            y = slider("Row gap", 0, 120, settings.RowGap, v => settings.RowGap = v);
            y = slider("Crop from bottom", 0, 70, settings.Crop, v => settings.Crop = v);
            y = slider("Up / down", -300, 300, settings.UpDown, v => settings.UpDown = v);
            y = slider("Middle gap", -400, 400, settings.MiddleGap, v => settings.MiddleGap = v);
            var seeThrough = new Toggle("See-through background") { Location = new Point(x, y + 2) };
            seeThrough.CheckedChanged += (s, e) => { settings.SeeThrough = seeThrough.Checked; Redraw(); };
            Controls.Add(seeThrough);

            var save = new FlatButton("Save banner", true);
            var close = new FlatButton("Close");
            int bottom = Math.Max(preview.Bottom, seeThrough.Bottom) + 16;
            save.Location = new Point(x + side - save.Width, bottom);
            close.Location = new Point(save.Left - 10 - close.Width, bottom);
            status.Bounds = new Rectangle(pad, bottom + 6, close.Left - 2 * pad, 22);
            status.Text = "1680 x 840. Saved next to the costume's zip as " + Path.GetFileName(saveTo);
            save.Click += (s, e) => Save();
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { save, close, status });
            ClientSize = new Size(x + side + pad, save.Bottom + 18);
            Redraw();
        }

        List<int> Picked() { return picks.Where(p => p.Value.Checked).Select(p => p.Key).OrderBy(c => c).ToList(); }

        Bitmap Make()
        {
            var shown = Picked();
            return Banner.Draw(shown.Select(c => pictures[c]).ToList(), shown.Select(c => boxes[c]).ToList(), settings);
        }

        void Redraw()
        {
            Image old = preview.Image;
            preview.Image = Make();
            if (old != null) old.Dispose();
        }

        void Save()
        {
            try
            {
                using (Bitmap banner = Make()) banner.Save(saveTo, ImageFormat.Png);
                status.Text = "Saved: " + Path.GetFileName(saveTo);
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + saveTo + "\""); } catch (Exception) { }
            }
            catch (Exception ex) { MessageBox.Show(this, "The banner wasn't saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (preview.Image != null) preview.Image.Dispose();
                foreach (Bitmap b in pictures.Values) b.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
