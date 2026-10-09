using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Browse Mods' Character list: every fighter, alphabetical down four columns, each with their portrait and how many
    // mods there are for them. A dark panel that drops down under the Character button (which also closes it).
    class CharacterPicker : Clear
    {
        public event Action<string> Picked;    // a fighter's code
        public Func<string, Image> Portrait;   // the roster's portraits (null while not read yet)
        public Func<string, int> Count;        // mods for a fighter
        public string Selected;

        const int Columns = 4, ColumnWidth = 200, RowHeight = 42, Pad = 14, Header = 54;
        static readonly int Rows = (Fighters.Codes.Length + Columns - 1) / Columns;
        int hover = -1;   // a fighter's place in the order, -1 nothing

        public CharacterPicker()
        {
            Size = new Size(Pad * 2 + Columns * ColumnWidth, Header + Rows * RowHeight + Pad);
            Cursor = Cursors.Hand;
            BackColor = Theme.Strip;
        }

        Rectangle Cell(int place)
        {
            int column = place / Rows, row = place % Rows;   // alphabetical down each column
            return new Rectangle(Pad + column * ColumnWidth, Header + row * RowHeight, ColumnWidth - 8, RowHeight - 4);
        }

        int At(Point p)
        {
            for (int place = 0; place < Fighters.DisplayOrder.Length; place++) if (Cell(place).Contains(p)) return place;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int now = At(e.Location);
            if (now != hover) { hover = now; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int place = At(e.Location);
            if (place < 0) return;
            var picked = Picked;
            if (picked != null) picked(Fighters.Codes[Fighters.DisplayOrder[place]]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var fill = new SolidBrush(Theme.Strip)) g.FillRectangle(fill, ClientRectangle);
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            Theme.Draw(g, "Characters", Theme.Bold(13f), Theme.Text, new Rectangle(Pad + 4, 12, 300, 30), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, Pad, Header - 8, Width - Pad, Header - 8);
            for (int place = 0; place < Fighters.DisplayOrder.Length; place++)
            {
                int id = Fighters.DisplayOrder[place];
                string code = Fighters.Codes[id];
                int count = Count != null ? Count(code) : 0;
                Rectangle cell = Cell(place);
                bool chosen = code == Selected;
                if (chosen || place == hover)
                    using (var path = Theme.Rounded(cell, 8))
                    using (var brush = new SolidBrush(chosen ? Color.FromArgb(70, Theme.Accent) : Color.FromArgb(22, 255, 255, 255)))
                    {
                        g.FillPath(brush, path);
                        if (chosen) using (var pen = new Pen(Theme.Accent)) g.DrawPath(pen, path);
                    }
                // the portrait: the top of the select-screen art, where the face is, in a rounded square
                var face = new Rectangle(cell.X + 6, cell.Y + 3, cell.Height - 6, cell.Height - 6);
                using (var path = Theme.Rounded(face, 6))
                {
                    using (var well = new SolidBrush(Theme.Well)) g.FillPath(well, path);
                    Image portrait = Portrait != null ? Portrait(code) : null;
                    if (portrait != null)
                    {
                        g.SetClip(path);
                        int side = Math.Min(portrait.Width, portrait.Height);
                        var from = new Rectangle((portrait.Width - side) / 2, (int)(portrait.Height * 0.04), side, side);
                        g.DrawImage(portrait, face, from, GraphicsUnit.Pixel);
                        g.ResetClip();
                    }
                }
                Color name = count > 0 || chosen ? Theme.Text : Color.FromArgb(120, Theme.Muted);
                Theme.Draw(g, Fighters.Names[id], Theme.Bold(10f), name, new Rectangle(face.Right + 10, cell.Y, cell.Width - face.Width - 52, cell.Height),
                           TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (count > 0)
                    Theme.Draw(g, count.ToString(), Theme.Font(9f), chosen ? Theme.AccentHover : Theme.Muted, new Rectangle(cell.Right - 40, cell.Y, 32, cell.Height),
                               TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        // a drop-down that shows the picker under `under`, closing when one is picked or the player clicks elsewhere
        public static ToolStripDropDown Show(Control under, string selected, Func<string, Image> portrait, Func<string, int> count, Action<string> picked)
        {
            var picker = new CharacterPicker { Selected = selected, Portrait = portrait, Count = count };
            var host = new ToolStripControlHost(picker) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = picker.Size };
            var drop = new ToolStripDropDown { Padding = Padding.Empty, Renderer = new Dark(), DropShadowEnabled = true };
            drop.Items.Add(host);
            picker.Picked += code => { drop.Close(); picked(code); };
            drop.Closed += (s, e) => BeginDispose(drop);
            drop.Show(under, new Point(0, under.Height + 4));
            return drop;
        }

        static void BeginDispose(ToolStripDropDown drop)
        {
            var timer = new Timer { Interval = 1 };
            timer.Tick += (s, e) => { timer.Dispose(); drop.Dispose(); };
            timer.Start();
        }

        // the drop-down itself: dark, no light border
        sealed class Dark : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var fill = new SolidBrush(Theme.Strip)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }
    }
}
