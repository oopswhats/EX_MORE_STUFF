using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // A costume's renders (user 2026-10-23: "just the renders per costume with the renderer we made (pose/rotation/frame)"):
    // Costume creation's View... draws it (CostumeRender) in any color, any frame of any of the fighter's animations, turned
    // and on either side, and saves the picture. Character select's look (the fighting stance, frame 0, 1P, turned 90
    // degrees) is where it starts. Drawing runs off the window's thread, one picture at a time; what was asked meanwhile is
    // drawn after (only the last). While a slider moves or the picture is dragged, a quick look (CostumeRender's quick:
    // half size, one sample a pixel); the full picture once nothing has moved for Settle ms. Colors 11 and 12 (color 1 in
    // the game's two styles) are here only: they are no package's (PerfTest.ColorCount).
    class CostumeViewer : Form
    {
        const int DrawWidth = 600, DrawHeight = 800, Settle = 250;
        readonly string game, stem, title;
        readonly Func<CostumeRender.Costume> open;
        CostumeRender.Costume costume;
        List<KeyValuePair<string, int>> animations = new List<KeyValuePair<string, int>>();

        readonly PictureBox picture = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Well };
        readonly Label info = new Label { AutoSize = false, BackColor = Theme.Well, ForeColor = Theme.Muted, Font = Theme.Font(9f) };
        readonly FlatButton[] colorButtons = new FlatButton[CostumeRender.Colors];
        readonly ComboBox stance = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Button, ForeColor = Theme.Text, Font = Theme.Font(10f) };
        readonly ValueSlider frame = new ValueSlider(), turn = new ValueSlider { Maximum = 359, Step = 5 };
        readonly Label frameText = Small(""), turnText = Small("");
        readonly FlatButton side1 = new FlatButton("1P"), side2 = new FlatButton("2P");
        readonly FlatButton reset = new FlatButton("Back to character select's look"), save = new FlatButton("Save picture...", true), saveAll = new FlatButton("Save all 12 colors...");

        int color = 1;
        bool mirror = true, busy, saving, closed, loading = true;
        bool? pending;   // asked while drawing: the full picture (true) or a quick look (false), drawn after
        readonly Timer settle = new Timer { Interval = Settle };   // the full picture after a quick look
        string posed;   // the pose the costume has: animation, frame, side
        int dragX = -1, dragTurn;

        public CostumeViewer(string game, string stem, string title, Func<CostumeRender.Costume> open)
        {
            this.game = game; this.stem = stem; this.title = title; this.open = open;
            Text = title;
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            ClientSize = new Size(1160, 820);
            MinimumSize = new Size(900, 700);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var panel = new Panel { Dock = DockStyle.Right, Width = 380, BackColor = Theme.Card, Padding = new Padding(22) };
            var left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Well };
            picture.Dock = DockStyle.Fill;
            info.Dock = DockStyle.Bottom;
            info.Height = 30;
            info.Padding = new Padding(14, 0, 0, 8);
            left.Controls.Add(picture);
            left.Controls.Add(info);

            int y = 22, x = 22, w = panel.Width - 44;
            panel.Controls.Add(Small("Color", x, y)); y += 24;
            int cell = (w - 5 * 6) / 6;
            for (int i = 0; i < colorButtons.Length; i++)
            {
                int c = i + 1;
                var b = new FlatButton(c.ToString()) { Size = new Size(cell, 40), Location = new Point(x + i % 6 * (cell + 6), y + i / 6 * 46) };
                b.Click += (s, e) => { color = c; Changed(); };
                colorButtons[i] = b;
                panel.Controls.Add(b);
            }
            y += 2 * 46 + 12;
            panel.Controls.Add(Small("Stance", x, y)); y += 24;
            stance.Bounds = new Rectangle(x, y, w, 30);
            stance.DrawMode = DrawMode.OwnerDrawFixed;   // in the app's colors, the box and its list
            stance.ItemHeight = 24;
            stance.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool lit = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var back = new SolidBrush(lit ? Theme.ButtonHover : Theme.Button)) e.Graphics.FillRectangle(back, e.Bounds);
                TextRenderer.DrawText(e.Graphics, stance.Items[e.Index].ToString(), stance.Font, Rectangle.Inflate(e.Bounds, -6, 0), stance.Enabled ? Theme.Text : Theme.Muted,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            stance.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                loading = true;   // the frame back to 0 with it: one full picture, no quick look first
                frame.Maximum = Math.Max(0, Frames(stance.SelectedIndex) - 1); frame.Value = 0;
                loading = false;
                Changed();
            };
            panel.Controls.Add(stance); y += 46;
            panel.Controls.Add(Small("Frame", x, y));
            frameText.Bounds = new Rectangle(x + w - 160, y, 160, 22); frameText.TextAlign = ContentAlignment.TopRight;
            panel.Controls.Add(frameText); y += 24;
            frame.Bounds = new Rectangle(x, y, w, 28);
            frame.ValueChanged += Moved;
            panel.Controls.Add(frame); y += 44;
            panel.Controls.Add(Small("Turn", x, y));
            turnText.Bounds = new Rectangle(x + w - 220, y, 220, 22); turnText.TextAlign = ContentAlignment.TopRight;
            panel.Controls.Add(turnText); y += 24;
            turn.Bounds = new Rectangle(x, y, w, 28);
            turn.Value = (int)CostumeRender.YawDegrees;
            turn.ValueChanged += Moved;
            panel.Controls.Add(turn); y += 44;
            panel.Controls.Add(Small("Side", x, y)); y += 24;
            side1.Bounds = new Rectangle(x, y, (w - 6) / 2, 40);
            side2.Bounds = new Rectangle(x + (w - 6) / 2 + 6, y, (w - 6) / 2, 40);
            side1.Click += (s, e) => { mirror = true; Changed(); };
            side2.Click += (s, e) => { mirror = false; Changed(); };
            panel.Controls.Add(side1); panel.Controls.Add(side2);

            foreach (FlatButton b in new[] { saveAll, save, reset })
            {
                b.Size = new Size(w, 40);
                b.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
                panel.Controls.Add(b);
            }
            panel.Layout += (s, e) =>
            {
                saveAll.Location = new Point(x, panel.ClientSize.Height - 22 - 40);
                save.Location = new Point(x, saveAll.Top - 48);
                reset.Location = new Point(x, save.Top - 56);
            };
            reset.Click += (s, e) =>
            {
                loading = true;
                if (stance.Items.Count > 0) stance.SelectedIndex = 0;
                frame.Maximum = Math.Max(0, Frames(0) - 1);
                frame.Value = 0; turn.Value = (int)CostumeRender.YawDegrees; mirror = true;
                loading = false;
                Changed();
            };
            save.Click += (s, e) => SavePicture();
            saveAll.Click += (s, e) => SaveAll();
            settle.Tick += (s, e) => { settle.Stop(); Ask(true); };

            // dragging across the picture turns the fighter
            picture.Cursor = Cursors.SizeWE;
            picture.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left && turn.Enabled) { dragX = e.X; dragTurn = turn.Value; } };
            picture.MouseUp += (s, e) => dragX = -1;
            picture.MouseMove += (s, e) => { if (dragX >= 0) turn.Value = ((dragTurn - (e.X - dragX) / 2) % 360 + 360) % 360; };

            Controls.Add(left);
            Controls.Add(panel);
            Shown += async (s, e) => await Open();
            Sync();
        }

        static Label Small(string text, int x = 0, int y = 0)
        {
            return new Label { Text = text, AutoSize = false, Bounds = new Rectangle(x, y, 200, 22), BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(9.5f) };
        }

        int Frames(int animation) { return animation >= 0 && animation < animations.Count ? animations[animation].Value : 1; }

        async Task Open()
        {
            info.Text = "Reading the costume...";
            SetEnabled(false);
            try
            {
                await Task.Run(() => { costume = open(); animations = costume.Animations(); });
            }
            catch (Exception ex)
            {
                info.Text = "It can't be drawn: " + ex.Message;
                info.ForeColor = Theme.Warning;
                return;
            }
            stance.Items.Clear();
            for (int i = 0; i < animations.Count; i++)
                stance.Items.Add(i == 0 ? "Fighting stance (as character select)" : (animations[i].Key.Length > 0 ? animations[i].Key : "Animation " + i) + "   (" + animations[i].Value + " frames)");
            if (stance.Items.Count == 0) stance.Items.Add("As the model is made (no animations found)");
            stance.SelectedIndex = 0;
            frame.Maximum = Math.Max(0, Frames(0) - 1);
            posed = Pose();   // the costume comes posed as character select
            loading = false;
            SetEnabled(true);
            Changed();
        }

        void SetEnabled(bool on)
        {
            foreach (Control c in colorButtons.Cast<Control>().Concat(new Control[] { stance, frame, turn, side1, side2, reset, save, saveAll })) c.Enabled = on;
        }

        string Pose() { return Math.Max(0, stance.SelectedIndex) + "/" + frame.Value + "/" + mirror; }

        // the buttons and texts follow what's picked
        void Sync()
        {
            for (int i = 0; i < colorButtons.Length; i++) colorButtons[i].Primary = i + 1 == color;
            side1.Primary = mirror; side2.Primary = !mirror;
            foreach (Control c in colorButtons.Cast<Control>().Concat(new Control[] { side1, side2 })) c.Invalidate();
            frameText.Text = frame.Value + " of " + frame.Maximum;
            turnText.Text = turn.Value + "°" + (turn.Value == (int)CostumeRender.YawDegrees ? " (as character select)" : "");
        }

        // a button or the stance: the full picture
        void Changed()
        {
            Sync();
            if (loading) return;
            settle.Stop();
            Ask(true);
        }

        // a slider moved (the turn slider by a drag on the picture too): a quick look, the full picture when it rests
        void Moved()
        {
            Sync();
            if (loading || costume == null) return;
            Ask(false);
            settle.Stop();
            settle.Start();
        }

        // one drawing at a time (and none while all colors are being saved): what's asked meanwhile waits, the last only
        void Ask(bool full)
        {
            if (loading || costume == null || closed) return;
            if (busy || saving) { pending = full; return; }
            busy = true;
            int c = color, yaw = turn.Value, animation = Math.Max(0, stance.SelectedIndex), f = frame.Value;
            bool m = mirror, repose = Pose() != posed;
            string pose = Pose();
            info.ForeColor = Theme.Muted;
            if (full || repose) info.Text = repose ? "Posing..." : "Drawing...";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Task.Run(() =>
            {
                if (repose) costume.Pose(animation, f, m);
                return full ? costume.Draw(c, DrawWidth, DrawHeight, yaw) : costume.Draw(c, DrawWidth / 2, DrawHeight / 2, yaw, true);
            }).ContinueWith(t =>
            {
                if (IsDisposed) { if (t.Status == TaskStatus.RanToCompletion && t.Result != null) t.Result.Dispose(); return; }
                busy = false;
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                {
                    posed = pose;
                    Image old = picture.Image;
                    picture.Image = t.Result;
                    if (old != null) old.Dispose();
                    info.Text = "Color " + c + (full ? ", drawn in " + (watch.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s" : "") + "   ·   drag the picture to turn";
                }
                else
                {
                    if (t.Exception != null) posed = null;
                    info.ForeColor = Theme.Warning;
                    info.Text = t.Exception != null ? "It couldn't be drawn: " + t.Exception.GetBaseException().Message : "Color " + c + " has no textures";
                }
                if (pending != null && !saving) { bool next = pending.Value; pending = null; Ask(next); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        string FileName(int c) { return stem + "_" + c.ToString("D2") + ".png"; }

        void SavePicture()
        {
            if (picture.Image == null) return;
            using (var dialog = new SaveFileDialog { Filter = "PNG picture|*.png", FileName = FileName(color), Title = "Save the picture" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { picture.Image.Save(dialog.FileName, ImageFormat.Png); info.Text = "Saved " + Path.GetFileName(dialog.FileName); }
                catch (Exception ex) { info.ForeColor = Theme.Warning; info.Text = "It couldn't be saved: " + ex.Message; }
            }
        }

        // every color in the pose and turn shown, <CHR>_<NN>_<CC>.png each into a folder
        async void SaveAll()
        {
            string folder;
            using (var dialog = new FolderBrowserDialog { Description = "The folder for " + title + "'s 12 pictures (" + FileName(1) + " ...)" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                folder = dialog.SelectedPath;
            }
            SetEnabled(false);
            saving = true;
            settle.Stop();
            while (busy) { await Task.Delay(50); if (closed) return; }   // the picture being drawn first: one drawing at a time
            busy = true;
            int yaw = turn.Value, done = 0;
            string problem = null;
            try
            {
                for (int c = 1; c <= CostumeRender.Colors && !closed; c++)   // the window closed: no more
                {
                    info.ForeColor = Theme.Muted;
                    info.Text = "Saving color " + c + " of " + CostumeRender.Colors + "...";
                    int cc = c;
                    using (Bitmap b = await Task.Run(() => costume.Draw(cc, DrawWidth, DrawHeight, yaw)))
                        if (b != null && !closed) { b.Save(Path.Combine(folder, FileName(cc)), ImageFormat.Png); done++; }
                }
            }
            catch (Exception ex) { problem = ex.Message; }
            busy = false;
            saving = false;
            if (closed || IsDisposed) return;
            SetEnabled(true);
            info.ForeColor = problem != null ? Theme.Warning : Theme.Muted;
            info.Text = problem != null ? "It stopped: " + problem : done + " pictures saved in " + folder;
            if (problem == null) try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); } catch (Exception) { }
            if (pending != null) { bool next = pending.Value; pending = null; Ask(next); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closed = true;   // a save of all colors stops; nothing more is drawn
            settle.Stop();
            base.OnFormClosed(e);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { settle.Dispose(); if (picture.Image != null) picture.Image.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // A slider in the app's colors (Windows' TrackBar can't take them): a track, the part up to the value in the accent,
    // a round thumb. Click or drag sets it; the arrow keys step it (Step), Home and End go to the ends.
    class ValueSlider : Clear
    {
        int value, maximum = 1;
        bool dragging;
        public int Step = 1;
        public event Action ValueChanged;

        public ValueSlider() { SetStyle(ControlStyles.Selectable, true); TabStop = true; Cursor = Cursors.Hand; }

        public int Maximum { get { return maximum; } set { maximum = Math.Max(0, value); if (this.value > maximum) Value = maximum; Invalidate(); } }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(0, Math.Min(maximum, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged();
            }
        }

        int At(int x) { return maximum == 0 ? 0 : (int)Math.Round((double)(x - Height / 2) * maximum / Math.Max(1, Width - Height)); }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) { dragging = true; Value = At(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Value = At(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override bool IsInputKey(Keys keys)
        {
            return keys == Keys.Left || keys == Keys.Right || keys == Keys.Up || keys == Keys.Down || base.IsInputKey(keys);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= Step;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += Step;
            else if (e.KeyCode == Keys.Home) Value = 0;
            else if (e.KeyCode == Keys.End) Value = maximum;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            float r = Height / 2f, cx = r + (maximum == 0 ? 0 : (Width - Height) * (float)value / maximum);
            using (var track = Theme.Rounded(new RectangleF(r, r - 3, Width - Height, 6), 3))
            using (var brush = new SolidBrush(Theme.Button)) g.FillPath(brush, track);
            using (var brush = new SolidBrush(Enabled ? Theme.Accent : Theme.Off)) g.FillRectangle(brush, r, r - 3, cx - r, 6);
            using (var brush = new SolidBrush(Enabled ? (Focused ? Theme.AccentHover : Theme.Accent) : Theme.Off)) g.FillEllipse(brush, cx - r + 4, 4, Height - 8, Height - 8);
            if (Focused) using (var pen = new Pen(Theme.Text, 1.5f)) g.DrawEllipse(pen, cx - r + 3, 3, Height - 6, Height - 6);
        }
    }
}
