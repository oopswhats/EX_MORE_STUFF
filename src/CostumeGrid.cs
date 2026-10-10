using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // A fighter's game costumes on their Costumes page, to replace: a tile each (the fighter's portrait, the costume's
    // name), and for one that plays as another a "Plays as" band and a thick white border, as on the Stages page.
    class CostumeGrid : Clear
    {
        public const int TileWidth = 170, TileHeight = 120, Gap = 12, NameHeight = 26;
        public List<int> Costumes = new List<int>();
        public Image Portrait;
        public Func<int, string> PlaysAs = n => null;   // what the costume plays as, or null
        public event Action<int> Clicked;
        int hover = -1;

        public static string CostumeName(int n) { return n == 1 ? "Original" : "Alternate " + (n - 1); }

        public CostumeGrid(int width)
        {
            Width = width;
            Margin = new Padding(6, 0, 6, 16);
        }

        int PerRow { get { return Math.Max(1, (Width + Gap) / (TileWidth + Gap)); } }

        public void Lay()
        {
            int rows = (Costumes.Count + PerRow - 1) / PerRow;
            Height = Math.Max(1, rows * (TileHeight + Gap));
            Invalidate();
        }

        Rectangle Tile(int i) { return new Rectangle(i % PerRow * (TileWidth + Gap), i / PerRow * (TileHeight + Gap), TileWidth, TileHeight); }

        int At(Point p) { for (int i = 0; i < Costumes.Count; i++) if (Tile(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int at = At(e.Location);
            if (at != hover) { hover = at; Cursor = at >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int at = At(e.Location);
            var clicked = Clicked;
            if (at >= 0 && clicked != null) clicked(Costumes[at]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            for (int i = 0; i < Costumes.Count; i++)
            {
                Rectangle r = Tile(i);
                string playsAs = PlaysAs(Costumes[i]);
                using (var path = Theme.Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 8))
                {
                    using (var fill = new SolidBrush(i == hover ? Theme.TileHover : Theme.Card)) g.FillPath(fill, path);
                    g.SetClip(path);
                    var picture = new Rectangle(r.X, r.Y, r.Width, r.Height - NameHeight);
                    using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
                    if (Portrait != null) Theme.Fit(g, Portrait, picture, false);
                    if (playsAs != null)
                    {
                        var band = new Rectangle(r.X, picture.Bottom - 26, r.Width, 26);
                        using (var shade = new SolidBrush(Color.FromArgb(225, Theme.Well))) g.FillRectangle(shade, band);
                        Theme.Draw(g, "Plays as " + playsAs, Theme.Bold(8.5f), Theme.GoodText, new Rectangle(band.X + 6, band.Y, band.Width - 12, band.Height),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }
                    g.ResetClip();
                    if (playsAs != null)
                        using (var inner = Theme.Rounded(new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), 7))
                        using (var pen = new Pen(Color.White, 4f)) g.DrawPath(pen, inner);
                    else
                        using (var pen = new Pen(i == hover ? Theme.Accent : Theme.Line, i == hover ? 2f : 1f)) g.DrawPath(pen, path);
                }
                Theme.Draw(g, CostumeName(Costumes[i]), Theme.Font(9f), Theme.Text, new Rectangle(r.X + 6, r.Bottom - NameHeight, r.Width - 12, NameHeight),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
