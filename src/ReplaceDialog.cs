using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Choosing what plays in a game stage's place on this PC: the stage itself (the original), a custom stage or
    // another game stage (or, worded for it, what plays as a game costume). Each choice is checked before it can be used.
    class ReplaceDialog : Form
    {
        public string Choice { get; private set; }     // null: the original

        readonly string stage;
        readonly Func<string, string> name;
        readonly Func<string, string, string> problem;
        readonly List<StageTile> tiles = new List<StageTile>();
        readonly Label status = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(9.5f) };
        readonly FlatButton use = new FlatButton("Use this stage", true);
        int check;

        // `about`, `self` and `thing` word it for something else than a stage (a costume): the text at the top, the label on
        // the tile that keeps it as it is, and the button ("Use this costume")
        public ReplaceDialog(string stage, string current, IEnumerable<string> sources, Func<string, string> name,
                             Func<string, Image> picture, Func<string, string, string> problem, string about = null, string self = "Original", string thing = "stage")
        {
            use.Text = "Use this " + thing;
            use.Width = TextRenderer.MeasureText(use.Text, use.Font).Width + 36;
            this.stage = stage;
            this.name = name;
            this.problem = problem;
            Choice = current;
            Text = "Replace " + name(stage);
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            ClientSize = new Size(880, 650);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var preview = new PictureBox { Image = picture(stage), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Well, Bounds = new Rectangle(24, 24, 240, 135) };
            var title = new Label { Text = "Replace " + name(stage), AutoSize = true, BackColor = Color.Transparent, Font = Theme.Bold(16f), ForeColor = Theme.Text, Location = new Point(284, 20) };
            var aboutLabel = new Label
            {
                Text = about ?? "Pick what you'd like to see whenever " + name(stage) + " is played. EX More Stuff puts a converted copy in its " +
                       "place; the game's own files are kept and come back when you choose the original again.\n\n" +
                       "Only you see it, online too: your opponent sees their own " + name(stage) + ". Its music stays the same.",
                AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, Bounds = new Rectangle(286, 60, 570, 100),
            };

            var list = new ScrollList { Bounds = new Rectangle(14, 178, 852, 396), Padding = new Padding(2) };
            AddTile(list, null, picture(stage), self, name(stage));
            foreach (string source in sources) AddTile(list, source, picture(source), null, name(source));

            status.Bounds = new Rectangle(24, 590, 520, 44);
            var cancel = new FlatButton("Cancel");
            use.Location = new Point(ClientSize.Width - use.Width - 24, 594);
            cancel.Location = new Point(use.Left - cancel.Width - 10, 594);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            use.Click += (s, e) => { if (use.Enabled) DialogResult = DialogResult.OK; };
            Controls.AddRange(new Control[] { preview, title, aboutLabel, list, status, cancel, use });
            CancelButton = null;
            Shown += (s, e) => Pick(Choice);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }

        void AddTile(ScrollList list, string code, Image picture, string note, string title)
        {
            var tile = new StageTile { Code = code, Picture = picture, Note = note, Title = title, Size = new Size(194, 142), Margin = new Padding(6) };
            tile.Click += (s, e) => Pick(code);
            tiles.Add(tile);
            list.Controls.Add(tile);
        }

        // Selecting a tile checks it in the background (reading a stage takes a moment); Use waits for the answer.
        void Pick(string code)
        {
            Choice = code;
            foreach (StageTile tile in tiles) { tile.Selected = tile.Code == code; tile.Invalidate(); }
            int ticket = ++check;
            use.Enabled = false;
            status.ForeColor = Theme.Muted;
            status.Text = code == null ? "Checking " + name(stage) + "..." : "Checking " + name(code) + "...";
            Task.Run(() => problem(stage, code)).ContinueWith(t =>
            {
                if (ticket != check || IsDisposed) return;
                string found = t.IsFaulted ? t.Exception.GetBaseException().Message : t.Result;
                if (found != null && code == null)
                {
                    // The stage itself can't be replaced: only the original stays possible.
                    foreach (StageTile tile in tiles) { tile.Enabled = tile.Code == null; tile.Invalidate(); }
                    status.ForeColor = Theme.Warning;
                    status.Text = found;
                    use.Enabled = true;
                    return;
                }
                status.ForeColor = found == null ? Theme.Muted : Theme.Warning;
                status.Text = found ?? (code == null ? name(stage) + " plays as itself." : name(stage) + " will play as " + name(code) + ".");
                use.Enabled = found == null;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    // A stage to choose: its picture and name, outlined when chosen.
    class StageTile : Clear
    {
        public string Code, Title;
        public Image Picture;
        public string Note;      // a small label on the picture ("Original")
        public bool Selected;
        bool hover;

        public StageTile() { Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            int pictureHeight = (Width - 2) * 9 / 16;
            var card = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(card, 8))
            {
                using (var fill = new SolidBrush(hover && Enabled ? Theme.TileHover : Theme.Card)) g.FillPath(fill, path);
                g.SetClip(path);
                var picture = new Rectangle(1, 1, Width - 2, pictureHeight);
                using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
                if (Picture != null) Theme.Fit(g, Picture, picture, false);
                if (!Enabled) using (var dim = new SolidBrush(Color.FromArgb(170, Theme.Base))) g.FillRectangle(dim, ClientRectangle);
                g.ResetClip();
                Color line = Selected ? Theme.Accent : hover && Enabled ? Color.FromArgb(120, Theme.Accent) : Theme.Line;
                using (var pen = new Pen(line, Selected ? 2.5f : 1f)) g.DrawPath(pen, path);
            }
            if (Note != null)
            {
                Font font = Theme.Bold(8f);
                int w = TextRenderer.MeasureText(Note, font).Width + 10;
                var pill = new RectangleF(8, 8, w, 20);
                using (var path = Theme.Rounded(pill, 10))
                using (var brush = new SolidBrush(Color.FromArgb(220, Theme.Well)))
                    g.FillPath(brush, path);
                Theme.Draw(g, Note, font, Theme.Text, Rectangle.Round(pill), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Theme.Draw(g, Title, Selected ? Theme.Bold(9f) : Theme.Font(9f), Enabled ? (Selected ? Theme.AccentHover : Theme.Text) : Theme.Muted,
                new Rectangle(8, pictureHeight + 2, Width - 16, Height - pictureHeight - 4), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
