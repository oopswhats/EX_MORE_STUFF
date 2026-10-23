using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // EX More Stuff's look: dark charcoal panels over a dimmed copy of the background art, the icon's orange as the
    // accent, Segoe UI. Every control here is drawn by hand and lets the background show through.
    static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(242, 148, 22);
        public static readonly Color AccentHover = Color.FromArgb(255, 172, 58);
        public static readonly Color AccentDown = Color.FromArgb(204, 112, 0);
        public static readonly Color OnAccent = Color.FromArgb(26, 18, 6);
        public static readonly Color Update = Color.FromArgb(182, 108, 255);   // updates: the Browse Mods tab's dot and the Update buttons (user 2026-10-13)
        public static readonly Color Notify = Color.FromArgb(236, 84, 148);    // pink: Notifications with something to show (user 2026-10-14)
        public static readonly Color Packs = Color.FromArgb(57, 150, 90);      // green: the Mod Packs button (user 2026-10-16; 2026-10-18: "slightly darker", then "bump the saturation": 31% -> 45%, same lightness and hue)
        public static readonly Color Badge = Color.FromArgb(34, 160, 84);
        public static readonly Color OwnBadge = Color.FromArgb(48, 112, 214);   // the player's own mods (not GameBanana's)
        public static readonly Color GoodText = Color.FromArgb(120, 220, 150);
        public static readonly Color Warning = Color.FromArgb(240, 132, 100);
        public static readonly Color Base = Color.FromArgb(15, 16, 20);
        public static readonly Color Text = Color.FromArgb(238, 239, 242);
        public static readonly Color Muted = Color.FromArgb(148, 154, 166);
        public static readonly Color Strip = Color.FromArgb(236, 13, 14, 18);
        public static readonly Color Tile = Color.FromArgb(232, 13, 14, 18);
        public static readonly Color TileHover = Color.FromArgb(240, 30, 32, 39);
        public static readonly Color Card = Color.FromArgb(242, 27, 29, 35);
        public static readonly Color Well = Color.FromArgb(10, 11, 14);
        public static readonly Color Line = Color.FromArgb(36, 255, 255, 255);
        public static readonly Color Button = Color.FromArgb(44, 47, 55);
        public static readonly Color ButtonHover = Color.FromArgb(58, 62, 72);
        public static readonly Color ButtonDown = Color.FromArgb(34, 36, 43);
        public static readonly Color Off = Color.FromArgb(70, 74, 84);

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static Font Font(float size) { return Get("Segoe UI", size); }
        public static Font Bold(float size) { return Get("Segoe UI Semibold", size); }

        static Font Get(string family, float size)
        {
            string key = family + size;
            Font font;
            if (!fonts.TryGetValue(key, out font)) fonts[key] = font = new Font(family, size);
            return font;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Image Resource(string name)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            return stream == null ? null : Image.FromStream(stream);
        }

        public static void Draw(Graphics g, string text, Font font, Color colour, Rectangle box, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, box, colour, flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // An image scaled into a box (whole, centred; `bottom` rests it on the box's lower edge).
        public static void Fit(Graphics g, Image image, RectangleF box, bool bottom)
        {
            float scale = Math.Min(box.Width / image.Width, box.Height / image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            g.DrawImage(image, box.X + (box.Width - w) / 2, bottom ? box.Bottom - h : box.Y + (box.Height - h) / 2, w, h);
        }

        // The window's backdrop, made once: the art filling the band from `top` (the header's edge) to `bottom` (the
        // footer's), its full height shown and the sides trimmed equally to fit; contrast restored and dimmed.
        public static Bitmap Backdrop(Image art, Size size, int top, int bottom)
        {
            var backdrop = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(backdrop))
            {
                g.Clear(Base);
                if (art == null || bottom <= top) return backdrop;
                var band = new Rectangle(0, top, size.Width, bottom - top);
                float scale = Math.Max((float)band.Width / art.Width, (float)band.Height / art.Height);
                float width = band.Width / scale, height = band.Height / scale;
                var source = new RectangleF((art.Width - width) / 2, (art.Height - height) / 2, width, height);
                const float contrast = 1.5f, dim = 0.6f, lift = -0.42f * dim;
                var matrix = new ColorMatrix(new[]
                {
                    new[] { contrast * dim, 0, 0, 0, 0f },
                    new[] { 0, contrast * dim, 0, 0, 0f },
                    new[] { 0, 0, contrast * dim, 0, 0f },
                    new[] { 0, 0, 0, 1f, 0 },
                    new[] { lift, lift, lift, 0, 1f },
                });
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(matrix);
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(art, band, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            return backdrop;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string list);

        // Windows 10/11's dark title bar (attribute 20; 19 before Windows 10 20H1). Older Windows just ignore it.
        public static void DarkTitleBar(IntPtr window)
        {
            try { int on = 1; if (DwmSetWindowAttribute(window, 20, ref on, 4) != 0) DwmSetWindowAttribute(window, 19, ref on, 4); }
            catch (Exception) { }
        }

        // Dark scroll bars on a control (Windows 10 1809 and later).
        public static void DarkScrollBars(IntPtr window)
        {
            try { SetWindowTheme(window, "DarkMode_Explorer", null); } catch (Exception) { }
        }
    }

    // A hand-drawn control the background shows through.
    class Clear : Control
    {
        public Clear()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
        }

        protected static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }

    // The header and footer: a dark band across the window with a hairline on its inner edge.
    class Strip : Clear
    {
        public bool LineOnTop;

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var fill = new SolidBrush(Theme.Strip)) e.Graphics.FillRectangle(fill, ClientRectangle);
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, LineOnTop ? 0 : Height - 1, Width, LineOnTop ? 0 : Height - 1);
        }
    }

    // A flat, rounded button: Primary is the orange call to action, the others quiet.
    class FlatButton : Clear
    {
        public bool Primary;
        // A color of its own (white text), as the About page's Ko-fi button has; null: the usual ones.
        public Color? Fill;
        bool hover, down;

        static Color Mix(Color c, Color with, float amount)
        {
            return Color.FromArgb((int)(c.R + (with.R - c.R) * amount), (int)(c.G + (with.G - c.G) * amount), (int)(c.B + (with.B - c.B) * amount));
        }

        public FlatButton(string text, bool primary = false)
        {
            Text = text;
            Primary = primary;
            Font = Theme.Bold(9.5f);
            Cursor = Cursors.Hand;
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 36, 36);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Smooth(e.Graphics);
            Color fill, text;
            if (!Enabled) { fill = Color.FromArgb(150, Theme.Button); text = Color.FromArgb(110, 115, 125); }
            else if (Fill != null) { fill = down ? Mix(Fill.Value, Color.Black, 0.15f) : hover ? Mix(Fill.Value, Color.White, 0.15f) : Fill.Value; text = Color.White; }
            else if (Primary) { fill = down ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent; text = Theme.OnAccent; }
            else { fill = down ? Theme.ButtonDown : hover ? Theme.ButtonHover : Theme.Button; text = Theme.Text; }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, 7))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
                if (!Primary && Fill == null && Enabled) using (var pen = new Pen(Theme.Line)) e.Graphics.DrawPath(pen, path);
            }
            if (Sub == null) Theme.Draw(e.Graphics, Text, Font, text, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            else
            {
                // two lines: the text, and a smaller one under it ("UPDATE" over "2 New Mods": user 2026-10-14)
                Theme.Draw(e.Graphics, Text, Font, text, new Rectangle(0, 2, Width, Height / 2), TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine);
                Theme.Draw(e.Graphics, Sub, Theme.Font(8.5f), text, new Rectangle(0, Height / 2, Width, Height / 2 - 2), TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
            }
        }

        public string Sub;   // a smaller second line under the text, or null
    }

    // The footer's folder button: a folder icon with the folder's name under it, so it's clear which folder the open
    // page's button opens (MainForm points it at the open page's folder; none hides it).
    class FolderButton : Clear
    {
        static readonly Font Icon = new Font("Segoe MDL2 Assets", 15f);
        bool hover, down;
        public string Folder { get; private set; }

        public FolderButton()
        {
            Font = Theme.Font(8f);
            Cursor = Cursors.Hand;
            Size = new Size(84, 46);
        }

        public void SetFolder(string folder, string name)
        {
            Folder = folder;
            Text = name;
            Visible = folder != null;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Folder == null) return;
            try { Directory.CreateDirectory(Folder); System.Diagnostics.Process.Start("explorer.exe", "\"" + Folder + "\""); }
            catch (Exception) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Smooth(e.Graphics);
            Color icon = down ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Text;
            Theme.Draw(e.Graphics, "", Icon, icon, new Rectangle(0, 0, Width, 26), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Draw(e.Graphics, Text, Font, hover ? Theme.Text : Theme.Muted, new Rectangle(0, 26, Width, Height - 26),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // A heart to click: an outline when off, filled red when on (favorite creators on Browse Mods).
    class HeartButton : Clear
    {
        public static readonly Color Red = Color.FromArgb(240, 80, 100);
        bool on, hover;
        public bool On { get { return on; } set { on = value; Invalidate(); } }

        public HeartButton()
        {
            Size = new Size(20, 20);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics, ClientRectangle, on, hover, 10f); }

        /// <summary>A heart in `box`: filled red when on, an outline otherwise (brighter when hovered).</summary>
        public static void Draw(Graphics g, Rectangle box, bool on, bool hover, float size)
        {
            using (var glyph = new Font("Segoe MDL2 Assets", size))
                Theme.Draw(g, on ? "" : "", glyph, on ? Red : hover ? Theme.Text : Theme.Muted, box,
                           TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // A header tab: the open page is bright with an orange line under it.
    class TabButton : Clear
    {
        bool selected, hover, dot, heart;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        /// <summary>A small red heart at the top left of the word (Browse Mods: new mods from favorite creators).</summary>
        public bool Heart { get { return heart; } set { heart = value; Invalidate(); } }
        static readonly Color Purple = Theme.Update;
        readonly System.Windows.Forms.Timer pulse = new System.Windows.Forms.Timer { Interval = 50 };

        /// <summary>A little purple glowing dot at the top right of the word, breathing slowly: something's new there
        /// (Browse Mods: updates for installed mods).</summary>
        public bool Dot
        {
            get { return dot; }
            set { dot = value; if (dot) pulse.Start(); else pulse.Stop(); Invalidate(); }
        }

        public TabButton(string text)
        {
            Text = text;
            Font = Theme.Bold(11f);
            Cursor = Cursors.Hand;
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 18, 44);
            pulse.Tick += (s, e) => Invalidate();
        }

        protected override void Dispose(bool disposing) { if (disposing) pulse.Dispose(); base.Dispose(disposing); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Smooth(e.Graphics);
            Theme.Draw(e.Graphics, Text, Font, selected || hover ? Theme.Text : Theme.Muted,
                new Rectangle(0, 0, Width, Height - 6), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected)
                using (var path = Theme.Rounded(new RectangleF(12, Height - 5, Width - 24, 3), 1.5f))
                using (var brush = new SolidBrush(Theme.Accent))
                    e.Graphics.FillPath(brush, path);
            Size word = TextRenderer.MeasureText(Text, Font);
            if (heart)
            {
                var spot = new Rectangle((Width - word.Width) / 2 - 9, (Height - 6 - word.Height) / 2 - 5, 16, 16);
                HeartButton.Draw(e.Graphics, spot, true, false, 8f);
            }
            if (!dot) return;
            // the dot sits on the word's top right corner; its glow swells and fades over two seconds
            var center = new PointF((Width + word.Width) / 2f - 1, (Height - 6 - word.Height) / 2f + 3);
            double breath = (Math.Sin(Environment.TickCount / 2000.0 * 2 * Math.PI) + 1) / 2;
            float glow = 6.5f + (float)breath * 2f;
            using (var halo = new GraphicsPath())
            {
                halo.AddEllipse(center.X - glow, center.Y - glow, glow * 2, glow * 2);
                using (var brush = new PathGradientBrush(halo) { CenterColor = Color.FromArgb((int)(110 + 90 * breath), Purple), SurroundColors = new[] { Color.FromArgb(0, Purple) } })
                    e.Graphics.FillPath(brush, halo);
            }
            using (var brush = new SolidBrush(Purple)) e.Graphics.FillEllipse(brush, center.X - 3.5f, center.Y - 3.5f, 7, 7);
            using (var brush = new SolidBrush(Color.FromArgb(200, 240, 225, 255))) e.Graphics.FillEllipse(brush, center.X - 1.6f, center.Y - 2.2f, 2.4f, 2.4f);
        }
    }

    // An on/off switch with its label.
    class Toggle : Clear
    {
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked { get { return on; } set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }

        public Toggle(string text)
        {
            Text = text;
            Font = Theme.Bold(9f);
            Cursor = Cursors.Hand;
            // as wide as its switch and word, so it can't cover a link beside it
            Size = new Size(46 + TextRenderer.MeasureText(text, Font).Width + 6, 24);
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Smooth(e.Graphics);
            int alpha = Enabled ? 255 : 90;   // locked (it can't be switched on here): dimmed
            var track = new RectangleF(1, 3, 38, 20);
            using (var path = Theme.Rounded(track, 10))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, on ? Theme.Accent : Theme.Off)))
                e.Graphics.FillPath(brush, path);
            float knob = on ? track.Right - 18 : track.X + 2;
            using (var brush = new SolidBrush(Color.FromArgb(alpha, on ? Color.White : Color.FromArgb(200, 204, 212))))
                e.Graphics.FillEllipse(brush, knob, track.Y + 2, 16, 16);
            Theme.Draw(e.Graphics, Text, Font, Color.FromArgb(alpha, on ? Theme.Text : Theme.Muted), new Rectangle(46, 0, Width - 46, Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // A fighter on the roster: their portrait, name, and their add-on costumes: on the left in grey how many are
    // switched off, on the right in green how many are on (no bubble for none).
    class FighterTile : Clear
    {
        public string FighterName;
        public int Inactive, Active, Own;   // switched off; on, from GameBanana; on, added by the player
        public int Colors;                  // extra colors switched on (color packs: user 2026-10-17, the pink bubble)
        bool hover;
        Image portrait;
        Bitmap scaled;          // the portrait at the tile's size, made once (not scaled again on every paint)
        float seen = 1;         // how much of the portrait shows: it fades in when it arrives
        // one timer for every tile's fade
        static readonly System.Windows.Forms.Timer fading = new System.Windows.Forms.Timer { Interval = 15 };
        static readonly List<FighterTile> fadingTiles = new List<FighterTile>();

        static FighterTile()
        {
            fading.Tick += (s, e) =>
            {
                foreach (FighterTile tile in fadingTiles.ToArray())
                {
                    tile.seen = Math.Min(1, tile.seen + 0.08f);
                    if (tile.seen >= 1 || tile.IsDisposed) fadingTiles.Remove(tile);
                    if (!tile.IsDisposed) tile.Invalidate();
                }
                if (fadingTiles.Count == 0) fading.Stop();
            };
        }

        public FighterTile() { Cursor = Cursors.Hand; }

        public Image Portrait
        {
            get { return portrait; }
            set
            {
                if (value == portrait) return;
                bool first = portrait == null;
                portrait = value;
                Forget();
                if (value != null && first) FadeIn();
                Invalidate();
            }
        }

        void FadeIn()
        {
            seen = 0;
            if (!fadingTiles.Contains(this)) fadingTiles.Add(this);
            fading.Start();
        }

        void Forget() { if (scaled != null) { scaled.Dispose(); scaled = null; } }

        protected override void OnResize(EventArgs e) { Forget(); base.OnResize(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Forget(); fadingTiles.Remove(this); }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(r, 9))
            {
                using (var fill = new SolidBrush(hover ? Theme.TileHover : Theme.Tile)) g.FillPath(fill, path);
                g.SetClip(path);
                if (portrait != null && Width > 6 && Height > 10)
                {
                    // Larger than fitting whole (the sides may be cut), standing on the name.
                    float fit = Math.Min((Width - 6f) / portrait.Width, (Height - 10f) / portrait.Height);
                    float scale = Math.Min(fit * 1.4f, (Height - 10f) / portrait.Height);
                    int w = Math.Max(1, (int)(portrait.Width * scale)), h = Math.Max(1, (int)(portrait.Height * scale));
                    if (scaled == null)
                    {
                        scaled = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                        using (Graphics s = Graphics.FromImage(scaled))
                        {
                            s.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            s.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            s.DrawImage(portrait, 0, 0, w, h);
                        }
                    }
                    var at = new Rectangle((Width - w) / 2, Height - 6 - h, w, h);
                    if (seen >= 1) g.DrawImage(scaled, at.X, at.Y, w, h);
                    else
                        using (var attributes = new ImageAttributes())
                        {
                            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = seen });
                            g.DrawImage(scaled, at, 0, 0, w, h, GraphicsUnit.Pixel, attributes);
                        }
                }
                var shade = new RectangleF(0, Height * 0.55f, Width, Height * 0.45f + 1);
                using (var brush = new LinearGradientBrush(shade, Color.FromArgb(0, Theme.Well), Color.FromArgb(235, Theme.Well), LinearGradientMode.Vertical))
                    g.FillRectangle(brush, shade);
                g.ResetClip();
                using (var pen = new Pen(hover ? Theme.Accent : Theme.Line, hover ? 2f : 1f)) g.DrawPath(pen, path);
            }
            Theme.Draw(g, FighterName, Theme.Bold(8.5f), hover ? Theme.AccentHover : Theme.Text, new Rectangle(2, Height - 28, Width - 4, 22),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            DrawBubbles(g);
        }

        const TextFormatFlags Centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

        // On the left, grey: switched off. On the right, green: switched on, from GameBanana; and left of it, blue:
        // switched on, added by the player (costumes, not their extra colors). Smaller when all three don't fit at full
        // size. Under them on the right, pink: extra colors switched on.
        void DrawBubbles(Graphics g)
        {
            for (int size = 0; size < 3; size++)   // full, smaller, smallest (three two-digit numbers)
            {
                Font font = Theme.Bold(new[] { 10.5f, 9f, 7.5f }[size]);
                int height = new[] { 26, 22, 19 }[size], margin = new[] { 5, 4, 3 }[size], gap = new[] { 4, 2, 1 }[size], padding = new[] { 14, 10, 6 }[size];
                Func<int, int> width = n => n <= 0 ? 0 : Math.Max(height, TextRenderer.MeasureText(n.ToString(), font, Size.Empty, Centred).Width + padding);
                int shown = new[] { Inactive, Own, Active }.Count(n => n > 0);
                if (size < 2 && margin * 2 + width(Inactive) + width(Own) + width(Active) + gap * Math.Max(0, shown - 1) > Width) continue;
                if (Inactive > 0) Bubble(g, Inactive, Theme.Off, margin, width(Inactive), height, font, false);
                float right = Width - margin;
                if (Active > 0) { right -= width(Active); Bubble(g, Active, Theme.Badge, right, width(Active), height, font, true); right -= gap; }
                if (Own > 0) { right -= width(Own); Bubble(g, Own, Theme.OwnBadge, right, width(Own), height, font, true); }
                // pink, under them on the right: the extra colors switched on (a color pack of 10 colors: 10)
                if (Colors > 0) Bubble(g, Colors, Theme.Notify, Width - margin - width(Colors), width(Colors), height, font, true, Active > 0 || Own > 0 ? 5 + height + gap + 2 : 5);
                return;
            }
        }

        // A number in a pill, white (grey for switched off), with a dark ring keeping it off the portrait.
        void Bubble(Graphics g, int number, Color fill, float x, int w, int h, Font font, bool on, float y = 5)
        {
            var pill = new RectangleF(x, y, w, h);
            using (var path = Theme.Rounded(pill, h / 2f))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var ring = new Pen(Theme.Well, 2f)) g.DrawPath(ring, path);
            }
            Theme.Draw(g, number.ToString(), font, on ? Color.White : Color.FromArgb(220, 223, 230),
                Rectangle.Round(new RectangleF(pill.X, pill.Y - 1, pill.Width, pill.Height)), Centred);
        }
    }

    // A tool on the Advanced page: a clickable card with a symbol, its name and what it's for.
    class ToolCard : Clear
    {
        public string Symbol, Title, Detail;
        bool hover;
        static readonly Font SymbolFont = new Font("Segoe UI Symbol", 22f);

        public ToolCard() { Cursor = Cursors.Hand; Size = new Size(380, 112); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(hover ? Theme.TileHover : Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(hover ? Theme.Accent : Theme.Line, hover ? 2f : 1f)) g.DrawPath(pen, path);
            }
            var badge = new RectangleF(18, 18, 54, 54);
            using (var path = Theme.Rounded(badge, 12))
            using (var brush = new SolidBrush(Color.FromArgb(40, Theme.Accent))) g.FillPath(brush, path);
            const TextFormatFlags centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            Theme.Draw(g, Symbol, SymbolFont, Theme.Accent, Rectangle.Round(badge), centred);
            Theme.Draw(g, Title, Theme.Bold(13f), hover ? Theme.AccentHover : Theme.Text, new Rectangle(88, 14, Width - 104, 30),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Detail, Theme.Font(9f), Theme.Muted, new Rectangle(88, 46, Width - 104, Height - 54),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }

    // A costume or stage from the catalog (or a player's own): picture, name, details, and its on/off switch.
    class ItemCard : Clear
    {
        public Image Picture;
        public string Title, Detail, Note;
        public string Badge;                 // a green tag on the picture ("Update to 1.1")
        public readonly Toggle Use = new Toggle("Use");
        // A link at the bottom right ("Rename", "Forget"), hidden until ShowLink.
        public readonly LinkLabel Link = new LinkLabel { AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(9f), Visible = false,
                                                         LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline };

        // A mod from GameBanana or DeviantArt: a link to its card on Browse Mods, left of the other link.
        public readonly LinkLabel Browse = new LinkLabel { Text = "Browse Mods", AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(9f), Visible = false,
                                                           LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline };

        // A package that can be deleted for good: a trash can at the bottom right, the links to its left.
        public readonly TrashButton Trash = new TrashButton { Visible = false };

        // a size warning (Weight), at the picture's top right
        public readonly WarningMark Warning = new WarningMark();

        // under it: a costume with colors the mod didn't bring, given pattern textures (user 2026-10-13)
        public readonly CheckerMark Incomplete = new CheckerMark();

        // a GameBanana mod's creator: "by", the favorite heart and their name in gold, a click on the name showing their
        // mods on Browse Mods (as there; user 2026-10-12), on the line under the title in place of Detail; the mod's page
        // is the tiny blue icon at that line's right end (the bottom row has no room left: Use, Browse Mods, Rename, trash)
        GoldName creator;
        public readonly HeartButton Heart = new HeartButton { Visible = false };
        public readonly PageButton Page = new PageButton { Visible = false };
        public event EventHandler CreatorClicked;

        public void ShowCreator(string name, string page, bool favorite, GameBanana.Support support = null, ToolTip tips = null)
        {
            if (string.IsNullOrEmpty(name)) return;
            Heart.On = favorite;
            Heart.Location = new Point(14 + TextRenderer.MeasureText("by ", Theme.Font(8.5f)).Width - 4, 180);
            Heart.Visible = true;
            Page.Page = page;
            Page.Location = new Point(Width - 12 - Page.Width, 178);
            Page.Visible = true;
            // the creator's Ko-fi and Patreon, left of the page icon (user 2026-10-14)
            int right = Page.Left - 2;
            foreach (var button in SupportButton.For(support))
            {
                button.Location = new Point(right - button.Width, Page.Top);
                right = button.Left - 2;
                if (tips != null) tips.SetToolTip(button, "Support " + name + " on " + (button.Kofi ? "Ko-fi" : "Patreon"));
                Controls.Add(button);
            }
            creator = new GoldName(name, 10f, 22) { Location = new Point(Heart.Right + 1, 179) };
            creator.Width = Math.Min(creator.Width, right - 4 - creator.Left);
            creator.Click += (s, e) => { var handler = CreatorClicked; if (handler != null) handler(this, e); };
            Controls.Add(Heart);
            Controls.Add(creator);
            Invalidate();
        }

        public ItemCard()
        {
            Size = new Size(286, 262);
            Use.Location = new Point(14, Height - 36);
            Warning.Location = new Point(Width - 38, 12);
            Controls.Add(Warning);
            Incomplete.Location = new Point(Width - 38 + (Warning.Width - Incomplete.Width) / 2, Warning.Bottom + 4);
            Controls.Add(Incomplete);
            Controls.Add(Use);
            Controls.Add(Link);
            Controls.Add(Browse);
            Controls.Add(Trash);
            Controls.Add(Page);
        }

        public void ShowLink(string text) { Link.Text = text; Link.Visible = true; Arrange(); }
        public void ShowBrowse() { Browse.Visible = true; Arrange(); }
        public void ShowTrash() { Trash.Visible = true; Arrange(); }

        // right to left: the trash can, the link, Browse Mods
        void Arrange()
        {
            int right = Width - 14;
            if (Trash.Visible) { Trash.Location = new Point(right - Trash.Width, Height - 34); right = Trash.Left - 8; }
            if (Link.Visible) { Link.Location = new Point(right - Link.PreferredWidth, Height - 31); right = Link.Left - 12; }
            if (Browse.Visible) Browse.Location = new Point(right - Browse.PreferredWidth, Height - 31);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(r, 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                g.SetClip(path);
                var picture = new RectangleF(1, 1, Width - 3, 150);
                using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
                if (Picture != null) Theme.Fit(g, Picture, picture, false);
                g.ResetClip();
                using (var pen = new Pen(Use.Checked ? Color.FromArgb(150, Theme.Accent) : Theme.Line)) g.DrawPath(pen, path);
            }
            if (!string.IsNullOrEmpty(Badge))
            {
                Font font = Theme.Bold(8.5f);
                var pill = new RectangleF(10, 10, TextRenderer.MeasureText(Badge, font).Width + 14, 24);
                using (var path = Theme.Rounded(pill, 12))
                {
                    using (var brush = new SolidBrush(Theme.Badge)) g.FillPath(brush, path);
                    using (var ring = new Pen(Theme.Well, 2f)) g.DrawPath(ring, path);
                }
                Theme.Draw(g, Badge, font, Color.White, Rectangle.Round(pill), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Theme.Draw(g, Title, Theme.Bold(10.5f), Theme.Text, new Rectangle(14, 158, Width - 28, 24), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (creator != null) Theme.Draw(g, "by", Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 181, 30, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            else Theme.Draw(g, Detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 181, Width - 28, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (!string.IsNullOrEmpty(Note))
                Theme.Draw(g, Note, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 199, Width - 28, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // A tiny blue icon opening a mod's page on GameBanana (Segoe MDL2 Assets' Open in new window glyph), at the bottom
    // of its card (user 2026-10-12), brighter under the pointer.
    class PageButton : Clear
    {
        static readonly Color Blue = Color.FromArgb(92, 162, 250), BlueHover = Color.FromArgb(150, 200, 255);
        static readonly Font Glyph = new Font("Segoe MDL2 Assets", 12f);   // 20% larger (user 2026-10-16)
        public string Page;
        bool hover;

        public PageButton() { Size = new Size(24, 29); Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!string.IsNullOrEmpty(Page)) try { System.Diagnostics.Process.Start(Page); } catch (Exception) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Draw(e.Graphics, "\uE8A7", Glyph, hover ? BlueHover : Blue, ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // A creator's Ko-fi or Patreon (user 2026-10-14: "a little kofi/patreon logo on the left of the link button"): the
    // service's mark, drawn here (Ko-fi's cup with a heart, Patreon's white shape on black), opening their page there. Only links
    // GameBanana.Support took (ko-fi.com, patreon.com) come here.
    class SupportButton : Clear
    {
        static readonly Color KofiPeach = Color.FromArgb(255, 122, 96);
        public readonly bool Kofi;
        public readonly string Link;
        bool hover;

        public SupportButton(bool kofi, string link) { Kofi = kofi; Link = link; Size = new Size(24, 29); Cursor = Cursors.Hand; }

        const float Grow = 1.2f;   // the marks 20% larger than first drawn (user 2026-10-16)

        /// <summary>A creator's buttons, right to left (Patreon nearest the page icon, Ko-fi left of it); none for none.</summary>
        public static List<SupportButton> For(GameBanana.Support support)
        {
            var buttons = new List<SupportButton>();
            if (support == null) return buttons;
            if (support.Patreon != null) buttons.Add(new SupportButton(false, support.Patreon));
            if (support.Kofi != null) buttons.Add(new SupportButton(true, support.Kofi));
            return buttons;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            try { System.Diagnostics.Process.Start(Link); } catch (Exception) { }
        }

        // Patreon's white shape in its square (0-1 across and down), traced from its mark (user 2026-10-14: "black with a
        // white shape")
        static readonly PointF[] PatreonShape =
        {
            new PointF(0.219f, 0.456f), new PointF(0.313f, 0.245f), new PointF(0.517f, 0.189f), new PointF(0.704f, 0.228f), new PointF(0.781f, 0.372f),
            new PointF(0.721f, 0.517f), new PointF(0.610f, 0.560f), new PointF(0.517f, 0.619f), new PointF(0.449f, 0.755f), new PointF(0.355f, 0.806f),
            new PointF(0.253f, 0.687f), new PointF(0.219f, 0.551f),
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            g.ScaleTransform(Grow, Grow);
            float cy = Height / Grow / 2f;
            if (Kofi)
            {
                // Ko-fi's mark (user 2026-10-14: "a white cup with a peach colored heart and a dark outline")
                Color outline = hover ? Color.FromArgb(70, 70, 70) : Color.FromArgb(28, 28, 28), cup = Color.White;
                var body = new RectangleF(1.5f, cy - 6f, 12.5f, 11f);
                using (var path = Theme.Rounded(body, 2.5f))
                {
                    using (var fill = new SolidBrush(cup)) g.FillPath(fill, path);
                    using (var pen = new Pen(outline, 1.4f)) g.DrawPath(pen, path);
                }
                using (var pen = new Pen(outline, 3.6f)) g.DrawArc(pen, 11.2f, cy - 4.2f, 6.2f, 6.2f, -90, 180);   // the handle: dark, white inside
                using (var pen = new Pen(cup, 1.4f)) g.DrawArc(pen, 11.2f, cy - 4.2f, 6.2f, 6.2f, -90, 180);
                using (var heart = new SolidBrush(KofiPeach))
                {
                    g.FillEllipse(heart, 4.6f, cy - 3.6f, 3.8f, 3.8f);
                    g.FillEllipse(heart, 7.4f, cy - 3.6f, 3.8f, 3.8f);
                    g.FillPolygon(heart, new[] { new PointF(4.7f, cy - 1.4f), new PointF(11.1f, cy - 1.4f), new PointF(7.9f, cy + 3f) });
                }
            }
            else
            {
                // Patreon's mark: a black rounded square with its white shape
                var square = new RectangleF(2.5f, cy - 7.5f, 15f, 15f);
                using (var path = Theme.Rounded(square, 4f))
                {
                    using (var fill = new SolidBrush(Color.Black)) g.FillPath(fill, path);
                    using (var pen = new Pen(hover ? Color.FromArgb(150, 255, 255, 255) : Theme.Line)) g.DrawPath(pen, path);   // seen on a dark card
                }
                var shape = PatreonShape.Select(p => new PointF(square.X + p.X * square.Width, square.Y + p.Y * square.Height)).ToArray();
                using (var fill = new SolidBrush(Color.White)) g.FillClosedCurve(fill, shape, System.Drawing.Drawing2D.FillMode.Winding, 0.5f);
            }
        }
    }

    // A small trash can (Segoe MDL2 Assets' Delete glyph, in every Windows 10 and 11): muted, orange-red under the pointer.
    class TrashButton : Clear
    {
        static readonly Font Glyph = new Font("Segoe MDL2 Assets", 11f);
        bool hover;

        public TrashButton() { Size = new Size(22, 22); Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Draw(e.Graphics, "\uE74D", Glyph, !Enabled ? Theme.Off : hover ? Theme.Warning : Theme.Muted, ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // A card's mark at the picture's top right: an orange "!" with a size warning (Weight), its message on hover; on a
    // custom stage or costume without one, a little green circle (user). Clicking either offers the performance test
    // (PerfTest) when the card's owner listens. Hidden when there's neither.
    // A small black and white checker (2 squares of each, user 2026-10-13) on a costume's card: some of its colors aren't
    // the mod's own textures but EX More Stuff's patterns; the pointer on it says which.
    class CheckerMark : Clear
    {
        static readonly ToolTip tip = new ToolTip { InitialDelay = 200, AutoPopDelay = 30000, ReshowDelay = 100 };

        public CheckerMark() { Size = new Size(20, 20); Visible = false; }

        /// <summary>Shown for these pattern colors (1-10), hidden for none.</summary>
        public void Show(IList<int> colors)
        {
            // not Visible read back: it's false while the card isn't on screen yet, and the hover text was never set
            bool any = colors != null && colors.Count > 0;
            Visible = any;
            if (!any) return;
            tip.SetToolTip(this, "This costume has incomplete textures: the mod doesn't have " + (colors.Count == 1 ? "color " : "colors ") + Ranges(colors) +
                                 ",\nso EX More Stuff gave " + (colors.Count == 1 ? "it a pattern texture" : "them pattern textures") + " in their place.");
        }

        // 2, 5, 7-10 as "2, 5 and 7-10"
        static string Ranges(IList<int> colors)
        {
            var parts = new List<string>();
            var sorted = colors.OrderBy(c => c).ToList();
            for (int i = 0; i < sorted.Count; )
            {
                int j = i;
                while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
                parts.Add(j > i ? sorted[i] + "-" + sorted[j] : sorted[i].ToString());
                i = j + 1;
            }
            return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            var box = new Rectangle(2, 2, Width - 5, Height - 5);
            using (var path = Theme.Rounded(box, 3))
            {
                using (var white = new SolidBrush(Color.FromArgb(235, 235, 235))) g.FillPath(white, path);
                g.SetClip(path);
                int half = box.Width / 2;
                using (var black = new SolidBrush(Color.FromArgb(20, 20, 20)))
                {
                    g.FillRectangle(black, box.X, box.Y, half, half);
                    g.FillRectangle(black, box.X + half, box.Y + half, box.Width - half, box.Height - half);
                }
                g.ResetClip();
                using (var ring = new Pen(Color.FromArgb(175, 175, 180), 1.2f)) g.DrawPath(ring, path);   // light, so the black squares show on a dark picture
            }
        }
    }

    class WarningMark : Clear
    {
        static readonly ToolTip tip = new ToolTip { InitialDelay = 200, AutoPopDelay = 30000, ReshowDelay = 100 };
        public string Message;   // the size warning, or null
        bool good, hover;

        public WarningMark() { Size = new Size(26, 26); Visible = false; }

        public void SetMessage(string message)
        {
            Message = message;
            good = false;
            tip.SetToolTip(this, message);
            Visible = message != null;
        }

        /// <summary>Clickable for the test: the warning's "!" (and its message), or the green circle without one.</summary>
        public void SetTestable(string message, string hint)
        {
            Message = message;
            good = message == null;
            tip.SetToolTip(this, message != null ? message + "\n\n" + hint : hint);
            Cursor = Cursors.Hand;
            Visible = true;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            bool lit = hover && Cursor == Cursors.Hand;
            if (good)
            {
                var dot = new RectangleF(6, 6, Width - 13, Height - 13);   // little: it only says "fine"
                using (var brush = new SolidBrush(lit ? Color.FromArgb(70, 200, 120) : Theme.Badge)) g.FillEllipse(brush, dot);
                using (var ring = new Pen(Theme.Well, 2f)) g.DrawEllipse(ring, dot);
                return;
            }
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var brush = new SolidBrush(lit ? Color.FromArgb(255, 150, 90) : Theme.Warning)) g.FillEllipse(brush, r);
            using (var ring = new Pen(Theme.Well, 2f)) g.DrawEllipse(ring, r);
            Theme.Draw(g, "!", Theme.Bold(11f), Color.White, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // A number setting in its own cell, like a page's note: "<before> [−] 4.0 [+] <after>" (Advanced > Performance
    // warnings). Typed (Enter or leaving the box) or stepped by 0.5; kept between its lowest and highest.
    class LineSetting : Clear
    {
        readonly TextBox box = new TextBox { Width = 52, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                                             Font = Theme.Bold(10.5f), TextAlign = HorizontalAlignment.Center };
        readonly FlatButton less = new FlatButton("−") { Size = new Size(30, 28) }, more = new FlatButton("+") { Size = new Size(30, 28) };
        readonly string before;
        string after;
        readonly double lowest, highest;
        double value;
        static readonly Font TextFont = Theme.Font(10.5f);
        const double Step = 0.5;
        // a link at the cell's right end ("Let tests set it"), hidden until ShowLink
        readonly LinkLabel link = new LinkLabel { AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(9.5f), Visible = false,
                                                  LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline };

        public event Action<double> ValueChanged;
        public event Action LinkClicked;

        public void SetAfter(string text) { after = text; Invalidate(); }

        public void ShowLink(string text, bool visible)
        {
            link.Text = text;
            link.Visible = visible;
            link.Location = new Point(Width - 14 - link.PreferredWidth, (Height - link.PreferredHeight) / 2);
            Invalidate();
        }

        public LineSetting(string before, string after, double value, double lowest, double highest, int width)
        {
            this.before = before;
            this.after = after;
            this.lowest = lowest;
            this.highest = highest;
            this.value = Clamp(value);
            Size = new Size(width - 16, 46);
            Margin = new Padding(8, 4, 8, 4);
            box.Text = Shown(this.value);
            less.Click += (s, e) => Set(this.value - Step);
            more.Click += (s, e) => Set(this.value + Step);
            box.Leave += (s, e) => Typed();
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Typed(); e.SuppressKeyPress = true; } };
            link.LinkClicked += (s, e) => { var handler = LinkClicked; if (handler != null) handler(); };
            Controls.AddRange(new Control[] { less, box, more, link });
            Place(14 + TextRenderer.MeasureText(before, TextFont).Width + 6);
        }

        void Place(int left)
        {
            less.Location = new Point(left, (Height - less.Height) / 2);
            box.Location = new Point(fixedValue ? left : less.Right + 4, (Height - box.Height) / 2);
            more.Location = new Point(box.Right + 4, less.Top);
            Invalidate();
        }

        // shown only, not changed here (the performance warning lines: user 2026-10-14, "just make it unchangeable")
        bool fixedValue;
        public void MakeFixed()
        {
            fixedValue = true;
            less.Visible = more.Visible = false;
            box.ReadOnly = true;
            box.TabStop = false;
            box.Cursor = Cursors.Default;
            Place(less.Left);
        }

        int AfterLeft { get { return (fixedValue ? box.Right : more.Right) + 10; } }

        /// <summary>Both settings' boxes at the same place (the longer text's end).</summary>
        public void AlignWith(LineSetting other)
        {
            int left = Math.Max(less.Left, other.less.Left);
            Place(left);
            other.Place(left);
        }

        void Typed()
        {
            double typed;
            if (double.TryParse(box.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out typed)) Set(typed);
            else box.Text = Shown(value);
        }

        void Set(double to)
        {
            to = Clamp(Math.Round(to, 1));
            box.Text = Shown(to);
            if (to == value) return;
            value = to;
            var handler = ValueChanged;
            if (handler != null) handler(to);
        }

        double Clamp(double v) { return Math.Max(lowest, Math.Min(highest, v)); }

        static string Shown(double v) { return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            {
                using (var fill = new SolidBrush(Color.FromArgb(215, Theme.Card))) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            Theme.Draw(g, before, TextFont, Theme.Text, new Rectangle(14, 0, less.Left - 14, Height), line);
            int end = link.Visible ? link.Left - 12 : Width - 14;
            Theme.Draw(g, after, TextFont, Theme.Text, new Rectangle(AfterLeft, 0, end - AfterLeft, Height), line);
        }
    }

    // A costume's color pictures (MainForm.MakeColorPictures), five to a row, numbered; it owns the pictures.
    class PicturePreview : Form
    {
        readonly IDictionary<int, Bitmap> shown;

        public PicturePreview(string title, IDictionary<int, Bitmap> pictures)
        {
            shown = pictures;
            Text = title;
            BackColor = Theme.Well;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            const int w = 160, h = 240, gap = 12, perRow = 5;
            int rows = (pictures.Count + perRow - 1) / perRow, across = Math.Min(perRow, Math.Max(1, pictures.Count));
            ClientSize = new Size(gap + across * (w + gap), gap + rows * (h + 28 + gap));
            int i = 0;
            foreach (var picture in pictures)
            {
                int x = gap + (i % perRow) * (w + gap), y = gap + (i / perRow) * (h + 28 + gap);
                Controls.Add(new PictureBox { Image = picture.Value, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(x, y, w, h), BackColor = Theme.Card });
                Controls.Add(new Label { Text = "Color " + picture.Key, ForeColor = Theme.Muted, Font = Theme.Font(9.5f), AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                                         Bounds = new Rectangle(x, y + h + 2, w, 24) });
                i++;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Bitmap b in shown.Values) b.Dispose();
            base.Dispose(disposing);
        }
    }

    // A scrolling list whose background art stays put while the cards move. Its scroll bar is our own (ListSlider):
    // Windows' can't take our colors, so ours lies over it. The wheel glides (user 2026-10-17: "make the scroll bar
    // smoother with the mouse in all the tabs. right now it like ticks down many pixels"): each notch slides WheelStep
    // pixels over GlideMs, easing out, a notch during a glide adding to where it's going.
    class ScrollList : FlowLayoutPanel
    {
        const int WheelStep = 60;    // pixels a notch of the wheel moves (user 2026-10-17: "make it 60"; Windows' own list jumped 120 at once)
        const int GlideMs = 250;     // how long a notch takes to slide there (user 2026-10-17: at 100 "it doesnt glide that much")
        readonly ListSlider slider;
        readonly System.Windows.Forms.Timer glide = new System.Windows.Forms.Timer { Interval = 10 };
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        int glideFrom, glideTo;

        public ScrollList()
        {
            glide.Tick += (s, e) => Glide();
            slider = new ListSlider(this);   // first: setting AutoScroll lays the list out already
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            AutoScroll = true;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkScrollBars(Handle); }
        protected override void OnScroll(ScrollEventArgs se) { Invalidate(true); base.OnScroll(se); slider.Follow(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!VerticalScroll.Visible) { base.OnMouseWheel(e); return; }
            Wheel(e.Delta);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;   // not passed on to a list this one sits in
        }

        /// <summary>A turn of the wheel (`delta` 120 a notch, a touchpad less): a glide that far.</summary>
        public void Wheel(int delta)
        {
            int now = -AutoScrollPosition.Y;
            glideFrom = now;
            glideTo = Math.Max(0, Math.Min(Most, (glide.Enabled ? glideTo : now) - delta * WheelStep / 120));
            clock.Restart();
            glide.Start();
        }

        void Glide()
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / GlideMs), left = 1 - t;
            Place(glideFrom + (int)Math.Round((glideTo - glideFrom) * (1 - left * left * left)));
            if (t >= 1) glide.Stop();
        }

        int Most { get { return Math.Max(0, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1); } }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); slider.Follow(); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); slider.Follow(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); slider.Follow(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); slider.Follow(); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); slider.Follow(); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); slider.Follow(); }   // scrolled from code too

        /// <summary>Scrolls to `y` (0 at the top), as dragging the bar does.</summary>
        public void ScrollTo(int y)
        {
            glide.Stop();
            Place(y);
        }

        void Place(int y)
        {
            AutoScrollPosition = new Point(-AutoScrollPosition.X, Math.Max(0, Math.Min(Most, y)));
            Invalidate(true);
            slider.Follow();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { glide.Dispose(); slider.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // A list's scroll bar (user 2026-10-12: "the bar that slides yellow, like the rest of our colors"): over Windows' own
    // at the list's right edge, in its parent, the background art showing through; a slim rounded thumb in the accent
    // color (lighter under the pointer or while dragged). Drag it, click above or below it to page, or use the wheel.
    class ListSlider : Clear
    {
        readonly ScrollList list;
        bool hover, dragging;
        int grab, shownValue = -1, shownMax = -1, shownPage = -1;

        public ListSlider(ScrollList list) { this.list = list; Visible = false; Cursor = Cursors.Default; }

        // where the thumb is: its top and height in this control
        void Thumb(out int top, out int height)
        {
            var bar = list.VerticalScroll;
            int total = Math.Max(1, bar.Maximum + 1), most = Math.Max(1, bar.Maximum - bar.LargeChange + 1);
            height = Math.Max(36, Height * bar.LargeChange / total);
            top = (int)((long)(Height - height) * Math.Min(bar.Value, most) / most);
        }

        /// <summary>Keeps it over the list's scroll bar: there while the list scrolls, gone when it doesn't.</summary>
        public void Follow()
        {
            if (IsDisposed) return;
            if (list.Parent != Parent)
            {
                if (Parent != null) Parent.Controls.Remove(this);
                if (list.Parent != null) list.Parent.Controls.Add(this);
            }
            bool show = Parent != null && list.Visible && list.VerticalScroll.Visible;
            if (show)
            {
                int w = SystemInformation.VerticalScrollBarWidth;
                var bounds = new Rectangle(list.Right - w, list.Top, w, list.Height);
                if (Bounds != bounds) Bounds = bounds;
                BringToFront();
            }
            if (Visible != show) Visible = show;
            var bar = list.VerticalScroll;
            if (bar.Value != shownValue || bar.Maximum != shownMax || bar.LargeChange != shownPage)
            {
                shownValue = bar.Value; shownMax = bar.Maximum; shownPage = bar.LargeChange;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int top, height;
            Thumb(out top, out height);
            if (e.Y >= top && e.Y < top + height) { dragging = true; grab = e.Y - top; Capture = true; Invalidate(); return; }
            list.ScrollTo(list.VerticalScroll.Value + (e.Y < top ? -1 : 1) * list.VerticalScroll.LargeChange);   // a page up or down
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            int top, height;
            Thumb(out top, out height);
            int room = Math.Max(1, Height - height), most = Math.Max(0, list.VerticalScroll.Maximum - list.VerticalScroll.LargeChange + 1);
            list.ScrollTo((int)((long)(e.Y - grab) * most / room));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Capture = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            list.Wheel(e.Delta);   // as over the list itself
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            int top, height;
            Thumb(out top, out height);
            const int slim = 8;
            float x = (Width - slim) / 2f;
            using (var track = Theme.Rounded(new RectangleF(x, 2, slim, Height - 4), slim / 2f))
            using (var brush = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillPath(brush, track);
            using (var thumb = Theme.Rounded(new RectangleF(x, top + 2, slim, height - 4), slim / 2f))
            using (var brush = new SolidBrush(hover || dragging ? Theme.AccentHover : Theme.Accent)) g.FillPath(brush, thumb);
        }
    }

    // A heading inside a list, with a hairline running on from it.
    // A modder's name, in gold with a light sweeping across it now and then: respect to the people who made the mods.
    // Clicking it shows their mods on Browse Mods (its card's Click; user 2026-10-12), the mod's own page being the
    // card's blue PageButton. One timer drives every name on screen, and only while a sweep is passing.
    class GoldName : Clear
    {
        static readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };
        static readonly List<GoldName> shown = new List<GoldName>();
        const int Cycle = 3600, Sweep = 900;   // ms: a sweep every 3.6 s, taking 0.9 s
        static bool sweeping;
        bool hover;

        static GoldName()
        {
            timer.Tick += (s, e) =>
            {
                bool now = Environment.TickCount % Cycle < Sweep;
                if (!now && !sweeping) return;
                sweeping = now;
                // only the names on screen (user 2026-10-22: Browse Mods' hundreds of cards were all drawn again 33 times a
                // second during a sweep, the ones scrolled out of sight too)
                foreach (GoldName name in shown) if (name.Visible && OnScreen(name)) name.Invalidate();
            };
            timer.Start();
        }

        // whether any of it shows: inside every parent's visible area, its window not minimized
        static bool OnScreen(Control c)
        {
            Form form = c.FindForm();
            if (form == null || form.WindowState == FormWindowState.Minimized || !form.Visible) return false;
            Rectangle seen = c.RectangleToScreen(c.ClientRectangle);
            for (Control p = c.Parent; p != null && !seen.IsEmpty; p = p.Parent) seen.Intersect(p.RectangleToScreen(p.ClientRectangle));
            return !seen.IsEmpty;
        }

        public GoldName(string text) : this(text, 11.5f, 24) { }

        // smaller, as on a costume or stage card
        public GoldName(string text, float size, int height)
        {
            Text = text;
            Font = Theme.Bold(size);
            Cursor = Cursors.Hand;
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 2, height);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); shown.Add(this); }
        protected override void OnHandleDestroyed(EventArgs e) { shown.Remove(this); base.OnHandleDestroyed(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (var path = new GraphicsPath())
            {
                float size = g.DpiY * Font.SizeInPoints / 72f;
                path.AddString(Text, Font.FontFamily, (int)Font.Style, size, new PointF(0, 1), StringFormat.GenericTypographic);
                var box = new RectangleF(0, 0, Width, Height);
                // gold: bright on top, deeper below
                using (var gold = new LinearGradientBrush(box, Color.FromArgb(255, 236, 160), Color.FromArgb(214, 158, 36), LinearGradientMode.Vertical))
                    g.FillPath(gold, path);
                // the shine: a soft white band crossing the name at a slant
                int t = Environment.TickCount % Cycle;
                if (t < Sweep)
                {
                    float x = -40 + (Width + 80) * t / (float)Sweep;
                    var band = new RectangleF(x - 20, 0, 40, Height);
                    using (var shine = new LinearGradientBrush(band, Color.Transparent, Color.Transparent, 20f))
                    {
                        shine.InterpolationColors = new ColorBlend
                        {
                            Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(230, 255, 255, 240), Color.FromArgb(0, 255, 255, 255) },
                            Positions = new[] { 0f, 0.5f, 1f },
                        };
                        g.SetClip(path);
                        g.FillRectangle(shine, band);
                        g.ResetClip();
                    }
                }
                if (hover) using (var pen = new Pen(Color.FromArgb(214, 158, 36))) g.DrawLine(pen, 0, Height - 2, Width - 2, Height - 2);
            }
        }
    }

    // Text right on the background art: a dark shadow under it keeps it readable over bright parts of the picture.
    class ShadowLabel : Clear
    {
        public bool Wrap = true;

        public ShadowLabel() { Font = Theme.Font(11.5f); ForeColor = Color.FromArgb(214, 217, 223); }

        // one line of this label's text: what a single-line one needs for its height
        public static int LineHeight { get { return TextRenderer.MeasureText("Ag", Theme.Font(11.5f)).Height + 4; } }

        // Cell: the text sits in a little rounded panel of its own, like a card (a page's notes)
        public bool Cell;
        static readonly Padding CellPadding = new Padding(14, 10, 14, 10);

        // a page's note: wrapped text in its own cell, as tall as its text needs at this width
        public static ShadowLabel Note(string text, int width)
        {
            var note = new ShadowLabel { Text = text, Cell = true, Margin = new Padding(8, 6, 8, 6) };
            int inside = width - 16 - CellPadding.Horizontal;
            note.Size = new Size(width - 16, TextRenderer.MeasureText(text, note.Font, new Size(inside, 0), TextFormatFlags.WordBreak).Height + CellPadding.Vertical);
            return note;
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }

        // The shadow: the text's own shape, softened, a pixel down and right. Made once per text and size.
        const int Pad = 4;
        Bitmap shadow;
        string shadowFor;

        protected override void OnPaint(PaintEventArgs e)
        {
            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | (Wrap ? TextFormatFlags.WordBreak : TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            Rectangle box = new Rectangle(0, 0, Width - 2, Height - 2);
            if (Cell)
            {
                Smooth(e.Graphics);
                using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
                {
                    using (var fill = new SolidBrush(Color.FromArgb(215, Theme.Card))) e.Graphics.FillPath(fill, path);
                    using (var pen = new Pen(Theme.Line)) e.Graphics.DrawPath(pen, path);
                }
                box = new Rectangle(CellPadding.Left, CellPadding.Top, Width - CellPadding.Horizontal, Height - CellPadding.Vertical);
            }
            if (string.IsNullOrEmpty(Text) || box.Width < 1 || box.Height < 1) return;
            // in a cell the text needs no shadow (it looked worse there); on the background art it does
            if (!Cell)
            {
                string key = Text + "\n" + Font.Size + "\n" + box.Size + "\n" + Wrap;
                if (shadow == null || shadowFor != key)
                {
                    if (shadow != null) shadow.Dispose();
                    shadow = Soft(box.Size, flags);
                    shadowFor = key;
                }
                e.Graphics.DrawImageUnscaled(shadow, box.X + 1 - Pad, box.Y + 1 - Pad);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, box, ForeColor, flags);
        }

        // The text drawn white on black; its brightness is the shadow's darkness, blurred (a 3x3 box, twice: close to a
        // Gaussian), then made darker again so a thin stroke still stands off a bright background.
        Bitmap Soft(Size size, TextFormatFlags flags)
        {
            int w = size.Width + Pad * 2, h = size.Height + Pad * 2;
            var a = new float[w * h];
            using (var mask = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Black);
                    TextRenderer.DrawText(g, Text, Font, new Rectangle(new Point(Pad, Pad), size), Color.White, Color.Black, flags);
                }
                var data = mask.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                var bytes = new byte[data.Stride * h];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                mask.UnlockBits(data);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * data.Stride + x * 3;
                        a[y * w + x] = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
                    }
            }
            for (int pass = 0; pass < 2; pass++) { Blur(a, w, h, 1, w); Blur(a, w, h, w, 1); }
            var pixels = new int[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (int)Math.Min(225f, a[i] * 2.4f) << 24;
            var soft = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var bits = soft.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(pixels, y * w, bits.Scan0 + y * bits.Stride, w);
            soft.UnlockBits(bits);
            return soft;
        }

        // each value becomes the average of itself and its two neighbors along one direction (step 1: across, w: down)
        static void Blur(float[] a, int w, int h, int step, int across)
        {
            int lines = step == 1 ? h : w, length = step == 1 ? w : h;
            var line = new float[length];
            for (int l = 0; l < lines; l++)
            {
                int start = l * across;
                for (int i = 0; i < length; i++) line[i] = a[start + i * step];
                for (int i = 0; i < length; i++)
                    a[start + i * step] = ((i > 0 ? line[i - 1] : 0) + line[i] + (i < length - 1 ? line[i + 1] : 0)) / 3f;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && shadow != null) { shadow.Dispose(); shadow = null; }
            base.Dispose(disposing);
        }
    }

    class Heading : Clear
    {
        public Heading(string text, int width)
        {
            Text = text;
            Font = Theme.Bold(13f);
            Size = new Size(width, 42);
            Margin = new Padding(6, 8, 6, 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var box = new Rectangle(0, 0, Width, Height - 6);
            Theme.Draw(e.Graphics, Text, Font, Theme.Text, box, TextFormatFlags.Left | TextFormatFlags.Bottom);
            int end = TextRenderer.MeasureText(Text, Font).Width + 8, y = box.Bottom - 11;
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, end, y, Width, y);
        }
    }

    // The About page's credits: the EX MORE STUFF lettering, version, a button to support its development on Ko-fi, and
    // a link to USF4's mods on GameBanana (hovered, it says why the program only uses GameBanana).
    class AboutCard : Clear
    {
        public const string GameBananaPage = "https://gamebanana.com/games/6971", SupportPage = "https://ko-fi.com/oops_whats";
        static readonly Color KoFi = Color.FromArgb(249, 98, 81);   // Ko-fi's coral
        const int Inset = 24;
        const string WhyGameBanana =
            "Why only GameBanana?\n" +
            "\n" +
            "GameBanana is where USF4\u2019s modders can share their work, and it checks every file for\n" +
            "viruses before anyone downloads it. EX More Stuff only offers the files that passed.\n" +
            "\n" +
            "It also lists every mod and its files in the open, so EX More Stuff can show them here, make\n" +
            "sure each download is the real file, and give each skin the same place for everyone, so\n" +
            "players see each other\u2019s skins in rollback matches.\n" +   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
            "\n" +
            "DeviantArt requires a user login to download mods, it limits user downloads, its files\n" +
            "aren\u2019t checked for viruses, and it has no list of mods that EX More Stuff can read and be\n" +
            "accurate, so EX More Stuff couldn\u2019t check them or keep them in order. We tried it and left\n" +
            "it out.\n" +
            "\n" +
            "Nexus Mods needs an account to download, and downloading from inside another program is only\n" +
            "for its paying members, so it wouldn\u2019t work for everyone.";
        readonly Image wordmark;
        readonly string version;
        readonly ToolTip why = new ToolTip { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100 };

        public AboutCard(Image wordmark, string version)
        {
            this.wordmark = wordmark;
            System.Version v;
            this.version = System.Version.TryParse(version, out v) ? v.Major + "." + v.Minor : version;
            Size = new Size(430, 292 + Notice);   // (+24: the thanks line, user 2026-10-23)
            var support = new FlatButton("Support") { Fill = KoFi, Font = Theme.Bold(12f), Location = new Point(Inset, 96) };
            support.Size = new Size(TextRenderer.MeasureText(support.Text, support.Font).Width + 40, 40);
            support.Click += (s, e) => { try { System.Diagnostics.Process.Start(SupportPage); } catch (Exception) { } };
            why.SetToolTip(support, SupportPage);
            // a bug report: the online form in the player's browser, this copy's version and Windows filled in (BugReport)
            var report = new FlatButton("Report a bug") { Font = Theme.Bold(12f), Location = new Point(support.Right + 10, 96), Visible = BugReport.Ready };
            report.Size = new Size(TextRenderer.MeasureText(report.Text, report.Font).Width + 40, 40);
            report.Click += (s, e) => { try { System.Diagnostics.Process.Start(BugReport.Link(this.version)); } catch (Exception) { } };
            why.SetToolTip(report, "Tell us what went wrong: a form opens in your browser, no account or email needed.\nWith your report it also sends your EX More Stuff version, your Windows version (or Linux)\nand the last error message EX More Stuff showed (your user name taken out of any folder in it),\nto help find the problem. Nothing else.");
            Controls.Add(report);
            var link = new LinkLabel
            {
                Text = "USF4 mods on GameBanana  \u203a", AutoSize = true, BackColor = Color.Transparent, Font = Theme.Bold(9f),
                LinkColor = Theme.Accent, ActiveLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline, Location = new Point(Inset - 2, 256 + Notice),
            };
            link.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start(GameBananaPage); } catch (Exception) { } };
            why.SetToolTip(link, WhyGameBanana);
            Controls.Add(support);
            Controls.Add(link);
        }

        // room under the buttons for what a bug report sends (user 2026-10-13: "lets protect user privacy"), while there's a form
        static int Notice { get { return BugReport.Ready ? 36 : 0; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) why.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12))
            {
                using (var fill = new SolidBrush(Theme.Strip)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding;
            int width = Width - 2 * Inset;
            if (wordmark != null) g.DrawImage(wordmark, Inset - 3, 22, 52f * wordmark.Width / wordmark.Height, 52);
            Theme.Draw(g, "Version " + version, Theme.Font(9f), Theme.Muted, new Rectangle(Inset, 78, width, 18), flags);
            if (Notice > 0)
                Theme.Draw(g, "A bug report sends your EX More Stuff version, your Windows version and the last error message it showed, never your name or your folders.",
                    Theme.Font(8f), Theme.Muted, new Rectangle(Inset, 144, width, 34), flags | TextFormatFlags.WordBreak);
            Theme.Draw(g, "GameBanana files are verified safe: virus-scanned by GameBanana, checked on download, and only costume and stage files are used.",
                Theme.Font(8f), Theme.Muted, new Rectangle(Inset, 146 + Notice, width, 34), flags | TextFormatFlags.WordBreak);
            Theme.Draw(g, "Costume pictures: rollback games' selection art (photos by Street Fighter Galleries, EventHubs and the Street Fighter Wiki). Game art © Capcom.",   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                Theme.Font(8f), Theme.Muted, new Rectangle(Inset, 182 + Notice, width, 50), flags | TextFormatFlags.WordBreak);
            // the people who helped (user 2026-10-23, their wording)
            Theme.Draw(g, "Thanks to Katie, Tom and FRaccie for their help with EX More Stuff.", Theme.Font(9f), Theme.Text, new Rectangle(Inset, 232 + Notice, width, 20), flags);
        }
    }

    // A drop-down menu in the window's colors: dark, the hover a little lighter, headers muted.
    class DarkMenu : ToolStripProfessionalRenderer
    {
        static readonly Color Back = Color.FromArgb(22, 24, 30), Border = Color.FromArgb(70, 74, 84);

        DarkMenu() : base(new Colors()) { RoundedEdges = false; }

        public static ContextMenuStrip Create()
        {
            var menu = new ContextMenuStrip();
            Style(menu);
            return menu;
        }

        public static void Style(ToolStripDropDown drop)
        {
            drop.Renderer = new DarkMenu();
            drop.BackColor = Back;
            drop.ForeColor = Theme.Text;
            drop.Font = Theme.Font(9.5f);
            var menu = drop as ToolStripDropDownMenu;
            if (menu != null) menu.ShowImageMargin = false;
        }

        /// <summary>A line that names a group of the menu's items (not clickable).</summary>
        public static ToolStripItem Header(string text) { return new ToolStripLabel(text) { Font = Theme.Bold(8.5f), ForeColor = Theme.Muted, Margin = new Padding(6, 4, 0, 2) }; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem) e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Back; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Theme.Accent; } }
            public override Color MenuItemSelected { get { return Theme.ButtonHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.ButtonHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.ButtonHover; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.Button; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.Button; } }
            public override Color ImageMarginGradientBegin { get { return Back; } }
            public override Color ImageMarginGradientMiddle { get { return Back; } }
            public override Color ImageMarginGradientEnd { get { return Back; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Back; } }
        }
    }

    // A short message in the middle of the window that fades in, stays a moment and fades away by itself ("Songs saved
    // to disk"). Its own little window over the main one, so it can fade; it never takes the focus.
    class Toast : Form
    {
        readonly Timer timer = new Timer { Interval = 15 };
        int ticks;
        const int FadeIn = 8, Stay = 100, FadeOut = 20;   // in ticks of 15 ms: about 1.5 s shown

        readonly string mark;
        readonly float corner;

        Toast(string text, bool small = false)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(Theme.Card.R, Theme.Card.G, Theme.Card.B);
            Font = small ? Theme.Bold(9.5f) : Theme.Bold(12f);
            Text = text;
            mark = small ? "" : "✓  ";
            corner = small ? 7 : 10;
            Size textSize = TextRenderer.MeasureText(mark + text, Font);
            Size = small ? new Size(textSize.Width + 26, textSize.Height + 12) : new Size(textSize.Width + 72, textSize.Height + 30);
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), corner)) Region = new Region(path);
            Opacity = 0;
            timer.Tick += (s, e) =>
            {
                ticks++;
                Opacity = ticks < FadeIn ? ticks / (double)FadeIn : ticks < FadeIn + Stay ? 1 : Math.Max(0, 1 - (ticks - FadeIn - Stay) / (double)FadeOut);
                if (ticks >= FadeIn + Stay + FadeOut) { timer.Stop(); Close(); }
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x80 | 0x08000000;   // WS_EX_TOOLWINDOW (not in Alt+Tab), WS_EX_NOACTIVATE
                return p;
            }
        }

        /// <summary>Shows `text` in the middle of the window `over` is in.</summary>
        public static void Show(Control over, string text)
        {
            Form owner = over.FindForm();
            if (owner == null || !owner.Visible) return;
            var toast = new Toast(text);
            toast.Location = new Point(owner.Left + (owner.Width - toast.Width) / 2, owner.Top + (owner.Height - toast.Height) / 2);
            toast.FormClosed += (s, e) => toast.Dispose();
            toast.Show(owner);
            toast.timer.Start();
        }

        /// <summary>Shows `text` small, just above `area` of `anchor` (the heart that was clicked), in `owner`'s window.</summary>
        public static void ShowAbove(Control anchor, Rectangle area, string text, Control owner)
        {
            Form form = owner.FindForm();
            if (form == null || !form.Visible || anchor.IsDisposed) return;
            var toast = new Toast(text, true);
            Point top = anchor.PointToScreen(new Point(area.X + area.Width / 2, area.Y));
            toast.Location = new Point(top.X - toast.Width / 2, top.Y - toast.Height - 6);
            toast.FormClosed += (s, e) => toast.Dispose();
            toast.Show(form);
            toast.timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), corner))
            using (var pen = new Pen(Theme.Accent, 1.5f))
                g.DrawPath(pen, path);
            Theme.Draw(g, mark + Text, Font, Theme.Text, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
