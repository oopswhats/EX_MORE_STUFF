using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
        public static readonly Color Badge = Color.FromArgb(34, 160, 84);
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
        bool hover, down;

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
            else if (Primary) { fill = down ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent; text = Theme.OnAccent; }
            else { fill = down ? Theme.ButtonDown : hover ? Theme.ButtonHover : Theme.Button; text = Theme.Text; }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, 7))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
                if (!Primary && Enabled) using (var pen = new Pen(Theme.Line)) e.Graphics.DrawPath(pen, path);
            }
            Theme.Draw(e.Graphics, Text, Font, text, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // A header tab: the open page is bright with an orange line under it.
    class TabButton : Clear
    {
        bool selected, hover, dot;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        static readonly Color Purple = Color.FromArgb(182, 108, 255);
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
            if (!dot) return;
            // the dot sits on the word's top right corner; its glow swells and fades over two seconds
            Size word = TextRenderer.MeasureText(Text, Font);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Smooth(e.Graphics);
            var track = new RectangleF(1, 3, 38, 20);
            using (var path = Theme.Rounded(track, 10))
            using (var brush = new SolidBrush(on ? Theme.Accent : Theme.Off))
                e.Graphics.FillPath(brush, path);
            float knob = on ? track.Right - 18 : track.X + 2;
            using (var brush = new SolidBrush(on ? Color.White : Color.FromArgb(200, 204, 212)))
                e.Graphics.FillEllipse(brush, knob, track.Y + 2, 16, 16);
            Theme.Draw(e.Graphics, Text, Font, on ? Theme.Text : Theme.Muted, new Rectangle(46, 0, Width - 46, Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // A fighter on the roster: their portrait, name, and their add-on costumes: on the left in grey how many are
    // switched off, on the right in green how many are on (no bubble for none).
    class FighterTile : Clear
    {
        public string FighterName;
        public int Inactive, Active;
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
            if (Inactive > 0) Bubble(g, Inactive, Theme.Off, false);
            if (Active > 0) Bubble(g, Active, Theme.Badge, true);
        }

        // A number in a pill, large and white, with a dark ring keeping it off the portrait.
        void Bubble(Graphics g, int number, Color fill, bool right)
        {
            string text = number.ToString();
            Font font = Theme.Bold(10.5f);
            const TextFormatFlags centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            int w = Math.Max(26, TextRenderer.MeasureText(text, font, Size.Empty, centred).Width + 14);
            var pill = new RectangleF(right ? Width - w - 5 : 5, 5, w, 26);
            using (var path = Theme.Rounded(pill, 13))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var ring = new Pen(Theme.Well, 2f)) g.DrawPath(ring, path);
            }
            Theme.Draw(g, text, font, right ? Color.White : Color.FromArgb(220, 223, 230),
                Rectangle.Round(new RectangleF(pill.X, pill.Y - 1, pill.Width, pill.Height)), centred);
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

        public ItemCard()
        {
            Size = new Size(286, 262);
            Use.Location = new Point(14, Height - 36);
            Controls.Add(Use);
            Controls.Add(Link);
            Controls.Add(Browse);
        }

        public void ShowLink(string text)
        {
            Link.Text = text;
            Link.Location = new Point(Width - Link.PreferredWidth - 16, Height - 31);
            Link.Visible = true;
        }

        public void ShowBrowse()
        {
            int right = Link.Visible ? Link.Left - 14 : Width - 16;
            Browse.Location = new Point(right - Browse.PreferredWidth, Height - 31);
            Browse.Visible = true;
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
            Theme.Draw(g, Detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 181, Width - 28, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (!string.IsNullOrEmpty(Note))
                Theme.Draw(g, Note, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 199, Width - 28, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // A scrolling list whose background art stays put while the cards move.
    class ScrollList : FlowLayoutPanel
    {
        public ScrollList()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            AutoScroll = true;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkScrollBars(Handle); }
        protected override void OnScroll(ScrollEventArgs se) { Invalidate(true); base.OnScroll(se); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(true); }
    }

    // A heading inside a list, with a hairline running on from it.
    // A modder's name, in gold with a light sweeping across it now and then: respect to the people who made the mods.
    // Clicking it opens the mod's page. One timer drives every name on screen, and only while a sweep is passing.
    class GoldName : Clear
    {
        public string Link;
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
                foreach (GoldName name in shown) if (name.Visible) name.Invalidate();
            };
            timer.Start();
        }

        public GoldName(string text)
        {
            Text = text;
            Font = Theme.Bold(11.5f);
            Cursor = Cursors.Hand;
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 2, 24);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); shown.Add(this); }
        protected override void OnHandleDestroyed(EventArgs e) { shown.Remove(this); base.OnHandleDestroyed(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!string.IsNullOrEmpty(Link)) try { System.Diagnostics.Process.Start(Link); } catch (Exception) { }
        }

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
            string key = Text + "\n" + Font.Size + "\n" + box.Size + "\n" + Wrap;
            if (shadow == null || shadowFor != key)
            {
                if (shadow != null) shadow.Dispose();
                shadow = Soft(box.Size, flags);
                shadowFor = key;
            }
            e.Graphics.DrawImageUnscaled(shadow, box.X + 1 - Pad, box.Y + 1 - Pad);
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

    // The About page's credits: logo, the EX MORE STUFF lettering, version, who made it.
    class AboutCard : Clear
    {
        readonly Image logo, wordmark;
        readonly string version;

        public AboutCard(Image logo, Image wordmark, string version)
        {
            this.logo = logo;
            this.wordmark = wordmark;
            System.Version v;
            this.version = System.Version.TryParse(version, out v) ? v.Major + "." + v.Minor : version;
            Size = new Size(520, 210);
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
            if (logo != null) g.DrawImage(logo, 24, 57, 96, 96);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding;
            if (wordmark != null) g.DrawImage(wordmark, 140, 22, 52f * wordmark.Width / wordmark.Height, 52);
            Theme.Draw(g, "Version " + version, Theme.Font(9f), Theme.Muted, new Rectangle(143, 78, Width - 160, 18), flags);
            // "Made by Claude and oops", the names in orange.
            int x = 142;
            foreach (var part in new[] { Tuple.Create("Made by ", false), Tuple.Create("Claude", true), Tuple.Create(" and ", false), Tuple.Create("oops", true) })
            {
                Font font = part.Item2 ? Theme.Bold(12f) : Theme.Font(12f);
                Theme.Draw(g, part.Item1, font, part.Item2 ? Theme.Accent : Theme.Text, new Rectangle(x, 102, Width - x, 26), flags);
                x += TextRenderer.MeasureText(g, part.Item1, font, Size.Empty, flags).Width;
            }
            Theme.Draw(g, "Round 2 and 3 music plays with Tom's Round BGM mod", Theme.Font(9f), Theme.Muted, new Rectangle(143, 138, Width - 160, 18), flags);
            Theme.Draw(g, "GameBanana files are verified safe: virus-scanned by GameBanana, checked on download, and only costume files are used.",
                Theme.Font(8f), Theme.Muted, new Rectangle(143, 162, Width - 160, 34), flags | TextFormatFlags.WordBreak);
        }
    }
}
