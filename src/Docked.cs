using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Files docked for edits (user 2026-10-12: "dock file for edits?" after Add Mod From File): a package of the player's
    // that stays where it is and out of the game, off the character and stage lists, its card at the top of Advanced
    // while it's made ready to share (its color pictures, its code: edits go into the file itself). Kept in docked.json
    // beside the program; one whose file is gone leaves the list the next time it's read, as other content does.
    static class Docked
    {
        public const string Source = "docked";   // an Installed's Source for a docked file (it isn't installed)

        static string ListPath { get { return AppFolders.DataFile("docked.json"); } }

        /// <summary>The docked files still there that are packages, oldest first (Source "docked", Package the file).</summary>
        public static List<Installed> Load()
        {
            List<string> paths = Paths(), kept = paths.Where(File.Exists).ToList();
            if (kept.Count != paths.Count) Save(kept);   // a file gone: off the list
            var result = new List<Installed>();
            foreach (string path in kept)
            {
                // one that isn't a package any more stays listed, shown again once it is; a color pack's zip: each color
                foreach (Item item in Installer.Packages(path)) result.Add(new Installed { Item = item, Package = path, Source = Source });
            }
            return result;
        }

        public static bool Has(string path) { return Paths().Any(p => Same(p, path)); }

        public static void Add(string path)
        {
            var paths = Paths();
            if (paths.Any(p => Same(p, path))) return;
            paths.Add(Path.GetFullPath(path));
            Save(paths);
        }

        /// <summary>Off the list; the file itself stays where it is.</summary>
        public static void Remove(string path)
        {
            var paths = Paths();
            if (paths.RemoveAll(p => Same(p, path)) > 0) Save(paths);
        }

        static bool Same(string a, string b) { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }

        static List<string> Paths()
        {
            try
            {
                if (!File.Exists(ListPath)) return new List<string>();
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(ListPath));
                object files;
                if (root == null || !root.TryGetValue("files", out files) || !(files is System.Collections.ArrayList)) return new List<string>();
                return ((System.Collections.ArrayList)files).OfType<string>().ToList();
            }
            catch (Exception) { return new List<string>(); }
        }

        static void Save(List<string> paths)
        {
            if (paths.Count == 0) { if (File.Exists(ListPath)) File.Delete(ListPath); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
            SafeFile.WriteAllText(ListPath, new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "format", 1 }, { "files", paths } }), true);
        }
    }

    // A docked file at the top of Advanced: its picture, name, what it is, where the file is (a click shows it), its
    // code to change, Color pictures (a costume), and Remove (off the list; the file stays).
    class DockedRow : Clear
    {
        public Image Picture;
        public string Title, Kind, FilePath;
        bool Stage { get { return Kind == "Stage"; } }
        public readonly TextBox Code = new TextBox { BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Bold(11f),
                                                     CharacterCasing = CharacterCasing.Upper, TextAlign = HorizontalAlignment.Center };
        // (user 2026-10-21: "change "change" to "confirm" and "remove" to "remove docked file"", the unused stage code its own button)
        public readonly FlatButton Change = new FlatButton("Confirm"), Remove = new FlatButton("Remove docked file"), ColorPictures = new FlatButton("Color pictures") { Visible = false },
                                   Preview = new FlatButton("Preview in game") { Visible = false }, UnusedCode = new FlatButton("Get an unused stage code") { Visible = false };
        public readonly LinkLabel ShowFile = new LinkLabel { AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, Font = Theme.Font(8.5f),
                                                         LinkColor = Theme.Muted, ActiveLinkColor = Theme.Text, LinkBehavior = LinkBehavior.HoverUnderline };
        // a stage's preview pictures (left, middle, right: PerfTest.StagePreview), under the card once there are some;
        // a click shows one full size (ShotClicked, its number)
        public readonly ShotThumb[] Thumbs = { new ShotThumb(), new ShotThumb(), new ShotThumb() };
        public event Action<int> ShotClicked;
        // right of the pictures: copies of them full size beside the zip (user 2026-11-18: "so i can use the images on gamebanana")
        public readonly FlatButton SavePictures = new FlatButton("Save pictures") { Visible = false };   // (where: its tooltip; a longer name didn't fit at 1000 wide)
        // taller (user 2026-10-21: "you can make the dock thing bigger vertically"): its name, kind and file across the top,
        // its buttons in a row along the bottom
        public const int RowHeight = 140, ShotsHeight = 112, ThumbWidth = 176, ThumbHeight = 99;

        public DockedRow()
        {
            Controls.AddRange(new Control[] { Code, Change, Remove, ColorPictures, Preview, UnusedCode, ShowFile, SavePictures });
            SavePictures.Width = TextRenderer.MeasureText(SavePictures.Text, SavePictures.Font).Width + 36;
            for (int i = 0; i < Thumbs.Length; i++)
            {
                int n = i;
                Thumbs[i].Visible = false;
                Thumbs[i].Click += (s, e) => { var clicked = ShotClicked; if (clicked != null) clicked(n); };
                Controls.Add(Thumbs[i]);
            }
            Code.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                bool ok = Stage ? char.IsLetterOrDigit(e.KeyChar) && e.KeyChar < 128 : char.IsDigit(e.KeyChar);
                if (!ok) e.Handled = true;
            };
            ShowFile.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + FilePath + "\""); } catch (Exception) { } };
        }

        // a costume: its Color pictures button shows; a stage: Preview in game
        bool pictures, preview, unused;
        public bool CanMakePictures { set { pictures = value; ColorPictures.Visible = value; Arrange(); } }
        public bool CanPreview { set { preview = value; Preview.Visible = value; Arrange(); } }
        public bool CanGetCode { set { unused = value; UnusedCode.Visible = value; Arrange(); } }   // a stage: Get an unused stage code

        /// <summary>The preview's pictures (made small for the card), shown under it; the card is as tall as they need
        /// (the caller sizes it: RowHeight, plus ShotsHeight with pictures).</summary>
        public void SetShots(IList<Image> shots)
        {
            for (int i = 0; i < Thumbs.Length; i++)
            {
                Thumbs[i].Picture = i < shots.Count ? shots[i] : null;
                Thumbs[i].Visible = Thumbs[i].Picture != null;
            }
            SavePictures.Visible = shots.Count > 0;
            Arrange();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }

        void Arrange()
        {
            int h = RowHeight, y = h - 14 - Change.Height;   // the button row; the pictures go under the card
            Remove.Location = new Point(Width - Remove.Width - 14, y);
            Change.Location = new Point(Remove.Left - Change.Width - 8, y);
            Code.Bounds = new Rectangle(Change.Left - 74, y + (Change.Height - 28) / 2, 64, 28);
            int left = Code.Left - 66 - 12;   // left of the code box's label
            foreach (var shown in new[] { Tuple.Create(UnusedCode, unused), Tuple.Create(Preview, preview), Tuple.Create(ColorPictures, pictures) })
            {
                FlatButton b = shown.Item1;
                if (!shown.Item2) continue;   // (its own flag: Visible is false until the card shows)
                b.Location = new Point(left - b.Width, y);
                left = b.Left - 8;
            }
            ShowFile.Bounds = new Rectangle(160, 64, Math.Max(40, TextRight - 160), 18);
            for (int i = 0; i < Thumbs.Length; i++) Thumbs[i].Bounds = new Rectangle(160 + i * (ThumbWidth + 10), h - 2, ThumbWidth, ThumbHeight);
            SavePictures.Location = new Point(160 + Thumbs.Length * (ThumbWidth + 10) + 6, h - 2 + (ThumbHeight - SavePictures.Height) / 2);
            Invalidate();
        }

        int TextRight { get { return Width - 14; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Color.FromArgb(150, Theme.OwnBadge))) g.DrawPath(pen, path);
            }
            var picture = new RectangleF(10, 10, 136, RowHeight - 20);
            using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
            if (Picture != null) Theme.Fit(g, Picture, picture, false);
            int x = 160, right = Math.Max(x + 40, TextRight);
            Theme.Draw(g, Title, Theme.Bold(11f), Theme.Text, new Rectangle(x, 14, right - x, 24), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Kind + "   ·   docked for edits", Theme.Font(9f), Theme.Accent, new Rectangle(x, 40, right - x, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Stage ? "Code" : Kind == "Color" || Kind == "Colors" ? "Color" : "Slot", Theme.Font(9f), Theme.Muted, new Rectangle(Code.Left - 60, Code.Top, 54, Code.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // One of a stage preview's pictures on its card: a click shows it full size (ShotViewer).
    class ShotThumb : Clear
    {
        public Image Picture;
        bool hover;

        public ShotThumb() { Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var well = new SolidBrush(Theme.Well)) e.Graphics.FillRectangle(well, ClientRectangle);
            if (Picture != null) Theme.Fit(e.Graphics, Picture, new RectangleF(0, 0, Width, Height), false);
            using (var pen = new Pen(hover ? Theme.Accent : Color.FromArgb(90, Theme.Muted))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // A stage preview's pictures full size (user 2026-10-14: "click it to see the full thing in the window of EXMS"):
    // fitted to the window; the arrows at its sides or the left and right keys go to the others; a click on the picture
    // or Esc closes it.
    // With `setPicture` (a docked stage's preview, user 2026-10-14), "Use as stage picture" at the bottom makes the shown
    // one its stage picture (true when done) and closes the viewer; the one that is already reads "This is the stage picture", greyed.
    class ShotViewer : Form
    {
        readonly IList<string> paths;
        int at;
        Image shown;
        const int Side = 64;   // the arrows' strips
        readonly Func<string, bool> setPicture;
        readonly FlatButton use = new FlatButton("Use as stage picture", true) { Visible = false };
        readonly string current;

        public ShotViewer(string title, IList<string> paths, int start, string current = null, Func<string, bool> setPicture = null)
        {
            this.paths = paths;
            this.current = current;
            this.setPicture = setPicture;
            if (setPicture != null)
            {
                use.Visible = true;
                use.Click += (s, e) => { if (this.setPicture(this.paths[at])) Close(); };   // set: the viewer closes (user 2026-10-14)
                Controls.Add(use);
            }
            at = start;
            Text = title;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int width = Math.Min(area.Width * 85 / 100, 1600);
            ClientSize = new Size(width, width * 9 / 16);
            Load(at);
        }

        new void Load(int i)
        {
            at = (i + paths.Count) % paths.Count;
            if (shown != null) shown.Dispose();
            try { shown = new Bitmap(new MemoryStream(File.ReadAllBytes(paths[at]))); }   // the file isn't kept open
            catch (Exception) { shown = null; }
            ShowUse();
            Invalidate();
        }

        // the button for the one shown: greyed when it's the stage picture already
        void ShowUse()
        {
            if (setPicture == null) return;
            bool already = current != null && string.Equals(Path.GetFullPath(current), Path.GetFullPath(paths[at]), StringComparison.OrdinalIgnoreCase);
            use.Text = already ? "This is the stage picture" : "Use as stage picture";
            use.Width = TextRenderer.MeasureText(use.Text, use.Font).Width + 36;
            use.Enabled = !already;
            PlaceUse();
        }

        void PlaceUse() { use.Location = new Point((ClientSize.Width - use.Width) / 2, ClientSize.Height - use.Height - 16); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); PlaceUse(); Invalidate(); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Close(); return true; }
            if (keys == Keys.Left) { Load(at - 1); return true; }
            if (keys == Keys.Right) { Load(at + 1); return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (paths.Count > 1 && e.X < Side) Load(at - 1);
            else if (paths.Count > 1 && e.X > ClientSize.Width - Side) Load(at + 1);
            else Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = paths.Count > 1 && (e.X < Side || e.X > ClientSize.Width - Side) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            if (shown != null) Theme.Fit(g, shown, new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), false);
            if (paths.Count < 2) return;
            // the arrows: a chevron on a dark round at each side
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            foreach (int side in new[] { -1, 1 })
            {
                float cx = side < 0 ? Side / 2f : ClientSize.Width - Side / 2f, cy = ClientSize.Height / 2f;
                using (var round = new SolidBrush(Color.FromArgb(140, 0, 0, 0))) g.FillEllipse(round, cx - 22, cy - 22, 44, 44);
                using (var pen = new Pen(Color.White, 3f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(cx - 4 * side, cy - 9), new PointF(cx + 5 * side, cy), new PointF(cx - 4 * side, cy + 9) });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && shown != null) shown.Dispose();
            base.Dispose(disposing);
        }
    }

    // Add Mod From File's question once the files are picked (user 2026-10-12): into the game as before, or docked.
    class DockDialog : Form
    {
        public enum Choice { Cancel, Add, Dock }

        public Choice Picked { get; private set; }

        public DockDialog(IList<string> paths)
        {
            Text = "Add Mod From File";
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            const int width = 560;
            string title = paths.Count == 1 ? "Dock " + Path.GetFileName(paths[0]) + " for edits?" : "Dock these " + paths.Count + " files for edits?";
            const string text = "Docked, it stays out of the game and off the character and stage lists. Its card waits at the top of Advanced while you " +
                                "make it ready to share: its color pictures and its code (Advanced > Uploading Guide says how). The file stays where it is and " +
                                "the edits go into it. Its card goes when you remove it, or when the file is gone the next time EX More Stuff opens.";
            int height = TextRenderer.MeasureText(text, Theme.Font(9.5f), new Size(width - 44, 0), TextFormatFlags.WordBreak).Height + 8;
            ClientSize = new Size(width, 60 + height + 66);
            var heading = new Label { Text = title, AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, Font = Theme.Bold(13f), ForeColor = Theme.Text,
                                      Bounds = new Rectangle(22, 18, width - 44, 30) };
            var about = new Label { Text = text, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, Bounds = new Rectangle(22, 54, width - 44, height) };
            var add = new FlatButton("Add to the game", true);
            var dock = new FlatButton("Dock for edits");
            var cancel = new FlatButton("Cancel");
            int top = ClientSize.Height - 54;
            add.Location = new Point(width - 22 - add.Width, top);
            dock.Location = new Point(add.Left - 10 - dock.Width, top);
            cancel.Location = new Point(dock.Left - 10 - cancel.Width, top);
            add.Click += (s, e) => { Picked = Choice.Add; DialogResult = DialogResult.OK; };
            dock.Click += (s, e) => { Picked = Choice.Dock; DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; };
            Controls.AddRange(new Control[] { heading, about, cancel, dock, add });
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }
}
