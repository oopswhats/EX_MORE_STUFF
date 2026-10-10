using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // A fighter's game costumes on their Costumes page, to replace: a tile each (the costume's picture in color 1, from
    // CostumeArt, else the fighter's portrait; its name), and for one that plays as another a "Plays as" band and a thick
    // white border, as on the Stages page. Under them, the colors of the costume the mouse was last on (the first at
    // first), each with its number.
    class CostumeGrid : Clear
    {
        public const int TileWidth = CostumeArt.CellWidth + 24, NameHeight = 26, TileHeight = CostumeArt.CellHeight + 8 + NameHeight, Gap = 12;
        const int SwatchWidth = CostumeArt.CellWidth * 2 / 3, SwatchHeight = CostumeArt.CellHeight * 2 / 3, SwatchGap = 6, NumberHeight = 16, StripTitle = 30;
        public List<int> Costumes = new List<int>();
        public string Fighter;   // its code, for the pictures
        public Image Portrait;
        public Func<int, string> PlaysAs = n => null;   // what the costume plays as, or null
        public event Action<int> Clicked;
        int hover = -1, shown;

        public static string CostumeName(int n) { return n == 1 ? "Original" : "Alternate " + (n - 1); }

        public CostumeGrid(int width)
        {
            Width = width;
            Margin = new Padding(6, 0, 6, 16);
        }

        int PerRow { get { return Math.Max(1, (Width + Gap) / (TileWidth + Gap)); } }
        int SwatchesPerRow { get { return Math.Max(1, (Width + SwatchGap) / (SwatchWidth + SwatchGap)); } }
        int TilesHeight { get { return (Costumes.Count + PerRow - 1) / PerRow * (TileHeight + Gap); } }

        // the colors' rows: as many as the costume with the most colors needs, so the page doesn't jump between costumes
        int StripRows { get { return Costumes.Count == 0 ? 0 : Costumes.Max(n => (CostumeArt.Colors(Fighter, n) + SwatchesPerRow - 1) / SwatchesPerRow); } }

        public void Lay()
        {
            int rows = StripRows;
            Height = Math.Max(1, TilesHeight + (rows > 0 ? StripTitle + rows * (SwatchHeight + NumberHeight + SwatchGap) : 0));
            Invalidate();
        }

        Rectangle Tile(int i) { return new Rectangle(i % PerRow * (TileWidth + Gap), i / PerRow * (TileHeight + Gap), TileWidth, TileHeight); }

        int At(Point p) { for (int i = 0; i < Costumes.Count; i++) if (Tile(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int at = At(e.Location);
            if (at == hover) return;
            hover = at;
            if (at >= 0) shown = at;   // the colors stay on the last costume pointed at
            Cursor = at >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
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
                    Image art = CostumeArt.Picture(Fighter, Costumes[i], 1);
                    if (art != null) g.DrawImage(art, picture.X + (picture.Width - art.Width) / 2, picture.Bottom - art.Height - 4, art.Width, art.Height);
                    else if (Portrait != null) Theme.Fit(g, Portrait, picture, false);
                    if (playsAs != null)
                    {
                        var band = new Rectangle(r.X, picture.Bottom - 26, r.Width, 26);
                        using (var shade = new SolidBrush(Color.FromArgb(225, Theme.Well))) g.FillRectangle(shade, band);
                        Theme.Draw(g, "Plays as " + playsAs, Theme.Bold(8.5f), Theme.GoodText, new Rectangle(band.X + 4, band.Y, band.Width - 8, band.Height),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }
                    g.ResetClip();
                    if (playsAs != null)
                        using (var inner = Theme.Rounded(new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), 7))
                        using (var pen = new Pen(Color.White, 4f)) g.DrawPath(pen, inner);
                    else
                        using (var pen = new Pen(i == hover || (i == shown && StripRows > 0) ? Theme.Accent : Theme.Line, i == hover ? 2f : 1f)) g.DrawPath(pen, path);
                }
                Theme.Draw(g, CostumeName(Costumes[i]), Theme.Font(9f), Theme.Text, new Rectangle(r.X + 4, r.Bottom - NameHeight, r.Width - 8, NameHeight),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            PaintColors(g);
        }

        // the colors of the costume last pointed at, numbered like the game's color select
        void PaintColors(Graphics g)
        {
            if (StripRows == 0 || shown >= Costumes.Count) return;
            int costume = Costumes[shown], count = CostumeArt.Colors(Fighter, costume), top = TilesHeight;
            Theme.Draw(g, CostumeName(costume) + "'s colors", Theme.Bold(10.5f), Theme.Text, new Rectangle(0, top, Width, StripTitle - 6),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (count == 0)
            {
                Theme.Draw(g, "No pictures of this costume's colors", Theme.Font(9f), Theme.Muted, new Rectangle(0, top + StripTitle, Width, NumberHeight + 4),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }
            for (int c = 1; c <= count; c++)
            {
                int column = (c - 1) % SwatchesPerRow, row = (c - 1) / SwatchesPerRow;
                var cell = new Rectangle(column * (SwatchWidth + SwatchGap), top + StripTitle + row * (SwatchHeight + NumberHeight + SwatchGap), SwatchWidth, SwatchHeight);
                using (var path = Theme.Rounded(new RectangleF(cell.X + 0.5f, cell.Y + 0.5f, cell.Width - 1, cell.Height - 1), 6))
                {
                    using (var well = new SolidBrush(Theme.Well)) g.FillPath(well, path);
                    Image art = CostumeArt.Picture(Fighter, costume, c);
                    if (art != null)
                    {
                        g.SetClip(path);
                        g.DrawImage(art, cell);
                        g.ResetClip();
                    }
                    using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
                    Theme.Draw(g, c.ToString(), Theme.Font(8.5f), art != null ? Theme.Text : Theme.Off, new Rectangle(cell.X, cell.Bottom, cell.Width, NumberHeight),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
