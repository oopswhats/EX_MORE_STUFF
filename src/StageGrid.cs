using System;
using System.Drawing;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // The game's stages as cards (picture and name), five to a row in the order of USFIV's own stage select
    // (SSFIV.exe table 0xa58268); the two Random spots are left empty. Clicking a card asks to replace that stage
    // (StageClicked); a replaced stage says what it plays as.
    class StageGrid : Clear
    {
        public static readonly string[] Grid =
        {
            null,  "BLD", "IND", "KOR", "ELV",
            "AFX", "LBX", "USA", "CHN", "HFP",
            "RUS", "BRA", "AFR", "VIE", "MAD",
            "JPN", "EUR", "SCO", "JPX", "BFU",
            "LAB", "RVR", "VCN", "CNX", "JUR",
            "BRX", "VNX", "TRN", "DET", null,
        };
        const int Columns = 5, Rows = 6, Gap = 12, NameHeight = 30;
        readonly Image[] pictures = new Image[Grid.Length];
        int hover = -1;

        public Func<string, string> ReplacedBy = code => null;   // the stage's replacement's name, or null
        public event Action<string> StageClicked;

        public StageGrid(int width)
        {
            Width = width;
            Height = Rows * (CardHeight + Gap);
            for (int i = 0; i < Grid.Length; i++)
                if (Grid[i] != null)
                    try { pictures[i] = Stages.Picture(Grid[i]); } catch (Exception) { }
        }

        int CardWidth { get { return (Width - (Columns - 1) * Gap) / Columns; } }
        int PictureHeight { get { return CardWidth * 9 / 16; } }
        int CardHeight { get { return PictureHeight + NameHeight; } }

        Rectangle Card(int i) { return new Rectangle(i % Columns * (CardWidth + Gap), i / Columns * (CardHeight + Gap), CardWidth, CardHeight); }

        int CardAt(Point p)
        {
            for (int i = 0; i < Grid.Length; i++)
                if (Grid[i] != null && Card(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int at = CardAt(e.Location);
            if (at != hover) { hover = at; Cursor = at >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int at = CardAt(e.Location);
            if (at >= 0 && e.Button == MouseButtons.Left && StageClicked != null) StageClicked(Grid[at]);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            for (int i = 0; i < Grid.Length; i++)
            {
                if (Grid[i] == null) continue;
                Rectangle r = Card(i);
                string replacement = ReplacedBy(Grid[i]);
                using (var path = Theme.Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 8))
                {
                    using (var fill = new SolidBrush(i == hover ? Theme.TileHover : Theme.Card)) g.FillPath(fill, path);
                    g.SetClip(path);
                    var picture = new Rectangle(r.X, r.Y, r.Width, PictureHeight);
                    using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
                    if (pictures[i] != null) g.DrawImage(pictures[i], picture);
                    if (replacement != null)
                    {
                        // What it plays as, on a band across the bottom of the picture.
                        var band = new Rectangle(r.X, picture.Bottom - 26, r.Width, 26);
                        using (var shade = new SolidBrush(Color.FromArgb(225, Theme.Well))) g.FillRectangle(shade, band);
                        Theme.Draw(g, "Plays as " + replacement, Theme.Bold(8.5f), Theme.GoodText,
                            new Rectangle(band.X + 8, band.Y, band.Width - 16, band.Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    g.ResetClip();
                    if (replacement != null)
                        // replaced: a thick white border, seen at a glance (user)
                        using (var inner = Theme.Rounded(new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), 7))
                        using (var pen = new Pen(Color.White, 4f)) g.DrawPath(pen, inner);
                    else
                        using (var pen = new Pen(i == hover ? Theme.Accent : Theme.Line, i == hover ? 2f : 1f)) g.DrawPath(pen, path);
                }
                Theme.Draw(g, Stages.Name(Grid[i]), Theme.Font(9f), Theme.Text, new Rectangle(r.X + 6, r.Y + PictureHeight, r.Width - 12, NameHeight),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
