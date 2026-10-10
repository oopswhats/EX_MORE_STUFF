using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Old mods: mods put in the game's patch folders by hand (ModScan), and "Import old mods", which brings the
    // stages and costumes among them into EX More Stuff: each becomes a package of the player's in one of their own seats
    // (kept in Mods\), and its loose files go to the Recycle Bin. Other files are only listed.
    class OldModsPanel : Clear
    {
        string game;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        readonly FlatButton import = new FlatButton("Import old mods", true);
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4) };
        List<ModScan.Found> found = new List<ModScan.Found>();
        int filledWidth;
        bool stale, scanning;

        public event Action Changed;          // packages were added (the window reads its lists again)
        public event Action<bool> Working;    // an import started or ended (the rest of the window waits)

        public OldModsPanel()
        {
            Controls.Add(list);
            import.Margin = new Padding(8, 4, 8, 8);
            import.Click += async (s, e) => await Import();
        }

        public void SetGame(string folder)
        {
            game = folder;
            if (Visible) Fill(); else stale = true;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && (stale || filledWidth == 0)) Fill();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (game != null && Visible && filledWidth > 0 && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Show(found);
        }

        // scans in the background (a few thousand files), then lists what it found
        async void Fill()
        {
            if (scanning) { stale = true; return; }   // again once this scan is done
            stale = false;
            if (!Game.IsGameFolder(game)) { found = new List<ModScan.Found>(); Show(found); return; }
            scanning = true;
            Say("Looking through the game's folders...");
            string g = game;
            try { found = await Task.Run(() => ModScan.Scan(g)); Say(""); }
            catch (Exception ex) { found = new List<ModScan.Found>(); Say("The game's folders couldn't be read: " + ex.Message, true); }
            finally { scanning = false; }
            Show(found);
            if (stale && Visible) Fill();
        }

        void Show(List<ModScan.Found> mods)
        {
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            int stages = mods.Count(m => m.Kind == ModScan.Kind.Stage), costumes = mods.Count(m => m.Kind == ModScan.Kind.Costume);
            var controls = new List<Control>
            {
                new Heading("Old mods", width),
                ShadowLabel.Note("Mods put in the game's folders by hand, not by EX More Stuff. Import old mods brings the stages and costumes among them in: each " +
                                 "becomes a package of yours in one of your own seats (stages U01 up, costumes " + Catalog.LastPersonal + " down), kept in the Mods folder, " +
                                 "and its loose files go to the Recycle Bin. You can have them keep showing where they are now. Other files stay as they are.", width),
            };
            import.Enabled = stages + costumes > 0;
            import.Text = stages + costumes > 0 ? "Import old mods (" + (stages + costumes) + ")" : "Import old mods";
            import.Width = TextRenderer.MeasureText(import.Text, import.Font).Width + 36;
            controls.Add(import);
            status.Width = width - 16;
            controls.Add(status);
            if (mods.Count == 0 && !scanning) controls.Add(ShadowLabel.Note("No old mods found: everything in the game's folders is the game's own or EX More Stuff's.", width));
            foreach (var m in mods)
                controls.Add(new OldModRow
                {
                    Size = new Size(width, 64), Margin = new Padding(6, 3, 6, 3), Title = m.What,
                    Detail = m.Files.Count + " file" + (m.Files.Count > 1 ? "s" : "") + "   ·   " + m.Files[0] + (m.Files.Count > 1 ? " ..." : "") + "   ·   " + m.Newest.ToString("d MMM yyyy"),
                    State = m.CanImport ? (m.Kind == ModScan.Kind.Stage ? "Stage: can be imported" : "Costume: can be imported") : "Stays as it is",
                    CanImport = m.CanImport,
                });
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != import && c != status) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        async Task Import()
        {
            var importable = found.Where(m => m.CanImport).ToList();
            if (importable.Count == 0) return;
            if (Game.IsRunning()) { Say("Close the game first, then import", true); return; }
            int stages = importable.Count(m => m.Kind == ModScan.Kind.Stage), costumes = importable.Count - stages;
            string what = (stages > 0 ? stages + " stage" + (stages > 1 ? "s" : "") : "") + (stages > 0 && costumes > 0 ? " and " : "") +
                          (costumes > 0 ? costumes + " costume" + (costumes > 1 ? "s" : "") : "");
            var answer = MessageBox.Show(FindForm(), "Import " + what + " into EX More Stuff?\n\nEach becomes a package of yours in one of your own seats, kept in the Mods folder; " +
                    "its loose files go to the Recycle Bin.\n\nKeep them showing where they are now?\n\nYes: they keep playing in place of the game's stages and costumes " +
                    "(as replacements, which you can change on the Stages page and each fighter's page).\nNo: the game's own come back, and yours wait in your own seats.",
                    "EX More Stuff", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            bool keep = answer == DialogResult.Yes;
            var done = new List<string>();
            var problems = new List<string>();
            string g = game;
            Action<string> recycle = path => Invoke((Action)(() =>
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)));
            Busy(true);
            try
            {
                int step = 0;
                foreach (var m in importable)
                {
                    Say(string.Format("Importing {0} ({1}/{2})...", m.What, ++step, importable.Count));
                    try { string where = await Task.Run(() => ModScan.Import(g, m, keep, recycle)); done.Add(m.What + " -> " + where); }
                    catch (Exception ex) { problems.Add(m.What + ": " + ex.Message); }
                }
            }
            finally { Busy(false); }
            var changed = Changed;
            if (changed != null) changed(); else Fill();   // the window's reload lists them again
            if (problems.Count > 0)
                MessageBox.Show(FindForm(), (done.Count > 0 ? done.Count + " imported.\n\n" : "") + "Some couldn't be imported:\n\n" + string.Join("\n", problems),
                    "EX More Stuff", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Say(done.Count + " imported" + (keep ? ", showing where they were" : "") + (problems.Count > 0 ? "; " + problems.Count + " couldn't be" : ""), problems.Count > 0);
        }

        void Busy(bool on)
        {
            import.Enabled = !on;
            UseWaitCursor = on;
            var working = Working;
            if (working != null) working(on);
        }

        void Say(string text, bool problem = false)
        {
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
        }
    }

    // One old mod: what it is, its files, and whether it can be imported.
    class OldModRow : Clear
    {
        public string Title, Detail;
        public string State;
        public bool CanImport;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            int right = Width - 16;
            Theme.Draw(g, State, Theme.Bold(9f), CanImport ? Theme.GoodText : Theme.Muted, new Rectangle(right - 220, 10, 220, 22), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Title, Theme.Bold(10.5f), Theme.Text, new Rectangle(14, 10, right - 240, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 34, right - 14, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
        }
    }
}
