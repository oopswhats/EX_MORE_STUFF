using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Free codes: for a creator about to share a costume or stage on GameBanana. A character's seats 8-99 at a
    // glance (free, taken on GameBanana, not free), and any slot or stage code checked (Seats). It warns, never forbids.
    class FreeCodesPanel : Clear
    {
        public Func<string, Image> Portrait;   // the roster's portraits, for the Character list
        string fighter = Fighters.Codes[Fighters.DisplayOrder[0]];
        bool read, reading;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        readonly FlatButton character = new FlatButton("Character  ▾");
        readonly TextBox slot = new TextBox { Width = 64, MaxLength = 2, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Bold(11f), TextAlign = HorizontalAlignment.Center };
        readonly TextBox stage = new TextBox { Width = 76, MaxLength = 3, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Bold(11f),
                                               TextAlign = HorizontalAlignment.Center, CharacterCasing = CharacterCasing.Upper };
        readonly ShadowLabel slotAnswer = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight }, stageAnswer = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight };
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4), Visible = false };
        readonly SeatGrid grid = new SeatGrid { Margin = new Padding(8, 4, 8, 4) };
        readonly Clear costumeRow = new Clear { Height = 44, Margin = new Padding(8, 6, 8, 2) }, stageRow = new Clear { Height = 44, Margin = new Padding(8, 10, 8, 2) };
        ShadowLabel note, stageNote;
        Heading heading;
        int builtWidth;

        public FreeCodesPanel()
        {
            Controls.Add(list);
            character.Click += (s, e) => CharacterPicker.Show(character, fighter, Portrait,
                code => Enumerable.Range(Catalog.FirstCustomCostume, Catalog.LastShared - Catalog.FirstCustomCostume + 1).Count(n => SharedCodes.HeldBy.ContainsKey(SharedCodes.Code(code, n))),
                code => { fighter = code; ShowSeats(); });
            slot.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            slot.TextChanged += (s, e) => Answer();
            stage.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !(char.IsLetterOrDigit(e.KeyChar) && e.KeyChar < 128)) e.Handled = true; };
            stage.TextChanged += (s, e) => Answer();
            status.TextChanged += (s, e) => status.Visible = status.Text.Length > 0;   // no gap when there's nothing to say
            grid.Picked += n => { slot.Text = n.ToString("D2"); slot.Focus(); };
            slotLabel = new ShadowLabel { Text = "Slot", Wrap = false, Height = ShadowLabel.LineHeight };
            costumeRow.Controls.AddRange(new Control[] { character, slotLabel, slot, slotAnswer });
            stageLabel = new ShadowLabel { Text = "Stage code", Wrap = false, Height = ShadowLabel.LineHeight };
            stageRow.Controls.AddRange(new Control[] { stageLabel, stage, stageAnswer });
            character.Location = new Point(0, 4);
            costumeRow.Resize += (s, e) => LayOutRows();
            stageRow.Resize += (s, e) => LayOutRows();
        }

        readonly ShadowLabel slotLabel, stageLabel;

        void LayOutRows()
        {
            int line = ShadowLabel.LineHeight, y = (costumeRow.Height - line) / 2 + 2;
            foreach (ShadowLabel label in new[] { slotLabel, stageLabel }) label.Width = TextRenderer.MeasureText(label.Text, label.Font).Width + 8;
            slotLabel.Location = new Point(character.Right + 18, y);
            slot.Location = new Point(slotLabel.Right + 2, (costumeRow.Height - slot.Height) / 2);
            slotAnswer.Bounds = new Rectangle(slot.Right + 14, y, Math.Max(10, costumeRow.Width - slot.Right - 14), line);
            stageLabel.Location = new Point(0, y);
            stage.Location = new Point(stageLabel.Right + 2, (stageRow.Height - stage.Height) / 2);
            stageAnswer.Bounds = new Rectangle(stage.Right + 14, y, Math.Max(10, stageRow.Width - stage.Right - 14), line);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) return;
            Build();
            if (!read && !reading) { var ignored = Read(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Visible && Math.Abs(list.ClientSize.Width - builtWidth) > 4) Build();
        }

        // GameBanana's list (read once per run, shared with Browse Mods): who holds which seat
        async Task Read()
        {
            reading = true;
            status.Text = "Reading GameBanana...";
            try
            {
                SharedCodes.Holders(await GameBanana.All());
                read = true;
                status.Text = !GameBanana.Stale ? "" : "GameBanana isn't answering right now, so seats are checked against its list " +
                              (GameBanana.ReadAt == DateTime.MinValue ? "as EX More Stuff came with it" : "from " + GameBanana.ReadAt.ToString("d MMM yyyy"));
                status.ForeColor = Theme.Warning;
            }
            catch (Exception) { status.Text = "GameBanana isn't answering right now: seats taken there can't be checked. Try again later"; status.ForeColor = Theme.Warning; }
            finally { reading = false; }
            ShowSeats();
        }

        void Build()
        {
            builtWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            if (heading != null) { heading.Dispose(); note.Dispose(); stageNote.Dispose(); }
            heading = new Heading("Free codes", width);
            note = ShadowLabel.Note("Making a costume or stage to share on GameBanana? Check its code here first. Costume slots " + Catalog.FirstCustomCostume + "-" + Catalog.LastShared +
                        " are for sharing; " + Catalog.FirstPersonal + "-" + Catalog.LastPersonal + " are kept for players' own mods. A slot or code the game has, one a mod on GameBanana already uses, " +
                        "one kept for players' own mods or one that isn't free shows a warning. Hover a taken seat to see whose it is.", width);
            stageNote = ShadowLabel.Note("Stage codes are three letters or digits that aren't one of the game's (C01-C99 are the catalog's); U12, 1U2, 12U ... are kept for players' own mods.", width);
            foreach (Control c in new Control[] { costumeRow, stageRow, status }) c.Width = width - 16;
            grid.Width = width - 16;
            list.SuspendLayout();
            list.Controls.Clear();
            foreach (Control c in new Control[] { heading, note, status, costumeRow, grid, stageRow, stageNote }) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
            ShowSeats();
        }

        // the chosen character's seats, and the answers for what's typed
        void ShowSeats()
        {
            character.Text = Fighters.Name(fighter) + "  ▾";
            character.Width = TextRenderer.MeasureText(character.Text, character.Font).Width + 36;
            character.Invalidate();
            LayOutRows();
            grid.Fighter = fighter;
            grid.Invalidate();
            Answer();
        }

        void Answer()
        {
            int n;
            Seats.Answer a = int.TryParse(slot.Text, out n) ? Seats.Costume(fighter, n) : null;
            grid.Selected = a != null ? n : 0;
            grid.Invalidate();
            Tell(slotAnswer, a);
            Tell(stageAnswer, stage.Text.Length == 3 ? Seats.Stage(stage.Text) : null);
        }

        static void Tell(ShadowLabel label, Seats.Answer a)
        {
            label.Text = a == null ? "" : a.Text + (a.Kind == Seats.Kind.Free || a.Kind == Seats.Kind.Invalid ? "" : " (you can still use it)");
            label.ForeColor = a == null || a.Kind == Seats.Kind.Free ? Theme.GoodText : Theme.Warning;
        }
    }

    // A character's seats 8-99 as little numbered chips: free (green number), taken on GameBanana (orange; hover: whose),
    // not free (dim). Clicking one checks it.
    class SeatGrid : Clear
    {
        public string Fighter;
        public int Selected;
        public event Action<int> Picked;
        const int Chip = 38, Gap = 5, LegendHeight = 34, Swatch = 16;
        readonly ToolTip tip = new ToolTip { InitialDelay = 200 };
        int hover;

        // the legend: a swatch painted here, its word a shadowed label like the page's other text
        static readonly Tuple<Color, string>[] Legend = { Tuple.Create(Theme.Card, "free"), Tuple.Create(Color.FromArgb(170, Theme.Accent), "taken on GameBanana"),
                                                          Tuple.Create(Color.FromArgb(170, Theme.OwnBadge), "your own mods"), Tuple.Create(Theme.Well, "not free") };
        readonly ShadowLabel[] words;

        public SeatGrid()
        {
            Cursor = Cursors.Hand;
            words = Legend.Select(key => new ShadowLabel { Text = key.Item2, Wrap = false, Height = ShadowLabel.LineHeight, Cursor = Cursors.Default }).ToArray();
            int x = 0;
            foreach (ShadowLabel word in words)
            {
                word.Width = TextRenderer.MeasureText(word.Text, word.Font).Width + 8;
                word.Left = x + Swatch + 6;
                x = word.Right + 16;
            }
            Controls.AddRange(words);
        }

        int LegendTop { get { return Height - LegendHeight + 10; } }

        int PerRow { get { return Math.Max(1, (Width + Gap) / (Chip + Gap)); } }
        static int Count { get { return Catalog.LastSlot - Catalog.FirstCustomCostume + 1; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int rows = (Count + PerRow - 1) / PerRow;
            int height = rows * (Chip + Gap) + LegendHeight;
            if (Height != height) Height = height;
            foreach (ShadowLabel word in words) word.Top = LegendTop + Swatch / 2 - word.Height / 2 + 1;
        }

        Rectangle ChipAt(int index) { return new Rectangle(index % PerRow * (Chip + Gap), index / PerRow * (Chip + Gap), Chip, Chip); }

        int At(Point p)
        {
            for (int i = 0; i < Count; i++) if (ChipAt(i).Contains(p)) return Catalog.FirstCustomCostume + i;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int n = At(e.Location);
            if (n == hover) return;
            hover = n;
            Invalidate();
            tip.SetToolTip(this, n == 0 || Fighter == null ? "" : Seats.Costume(Fighter, n).Text);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int n = At(e.Location);
            var picked = Picked;
            if (n > 0 && picked != null) picked(n);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Fighter == null) return;
            Graphics g = e.Graphics;
            Smooth(g);
            for (int i = 0; i < Count; i++)
            {
                int n = Catalog.FirstCustomCostume + i;
                var kind = Seats.Costume(Fighter, n).Kind;
                Rectangle chip = ChipAt(i);
                Color fill = kind == Seats.Kind.Held ? Color.FromArgb(170, Theme.Accent) : kind == Seats.Kind.Personal ? Color.FromArgb(170, Theme.OwnBadge)
                           : kind == Seats.Kind.NotFree ? Theme.Well : Theme.Card;
                Color text = kind == Seats.Kind.Held ? Theme.OnAccent : kind == Seats.Kind.Personal ? Color.White
                           : kind == Seats.Kind.NotFree ? Color.FromArgb(90, Theme.Muted) : Theme.GoodText;
                using (var path = Theme.Rounded(new RectangleF(chip.X + 0.5f, chip.Y + 0.5f, chip.Width - 1, chip.Height - 1), 6))
                {
                    using (var brush = new SolidBrush(n == hover ? ControlPaint.Light(fill, 0.15f) : fill)) g.FillPath(brush, path);
                    Color edge = n == Selected ? Theme.AccentHover : n == hover ? Color.FromArgb(120, 255, 255, 255) : Theme.Line;
                    using (var pen = new Pen(edge, n == Selected ? 2f : 1f)) g.DrawPath(pen, path);
                }
                Theme.Draw(g, n.ToString("D2"), Theme.Bold(9.5f), text, chip, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            // the legend's swatches (its words are labels)
            for (int i = 0; i < Legend.Length; i++)
                using (var path = Theme.Rounded(new RectangleF(words[i].Left - Swatch - 6, LegendTop, Swatch, Swatch), 4))
                using (var brush = new SolidBrush(Legend[i].Item1)) { g.FillPath(brush, path); using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path); }
        }
    }
}
