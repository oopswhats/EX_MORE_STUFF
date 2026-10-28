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
    // (kept in Mods\), and its original loose files move to Mods\Legacy mods backup (AppFolders.BackUpOldMod; user 2026-10-14:
    // "placed somewhere safe"; they went to the Recycle Bin before). Other files are only listed.
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
        public Func<Task> BeforeImport;       // the window's first-time PC check (PerfTest), before anything goes in
        public Func<string, BulkAdd, Task> AddMod;   // one mod file added as Add Mod From File adds it, nothing asked (the window's)

        // Fix a folder of mods (user 2026-10-27, Button Mashin' Mastah's idea): every mod file in a folder the player picks,
        // added as Add Mod From File adds it, each costume checked and fixed (or switched off and locked). One is always done first
        // to time it on this PC; then the player picks how many, with the time it takes. A rest of 5 s between mods keeps
        // the PC free (never said: the player sees "Preparing"); Stop ends it after the mod being added.
        readonly FlatButton fixFolder = new FlatButton("Fix a folder of mods..."), stop = new FlatButton("Stop") { Visible = false };
        const int RestSeconds = 5;
        bool stopping;
        System.Threading.CancellationTokenSource resting;

        public OldModsPanel()
        {
            Controls.Add(list);
            import.Margin = new Padding(8, 4, 8, 8);
            import.Click += async (s, e) => await Import();
            fixFolder.Margin = new Padding(8, 4, 8, 4);
            stop.Margin = new Padding(8, 4, 8, 4);
            fixFolder.Click += async (s, e) => await FixFolder();
            stop.Click += (s, e) =>
            {
                stopping = true;
                stop.Enabled = false;
                if (resting != null) resting.Cancel();
                Say("Stopping after this mod...");
            };
        }

        // an add lets the window go when it ends (Browse Mods' own install): it waits again until the folder is done
        void StillWorking()
        {
            var working = Working;
            if (working != null) working(true);
        }

        async Task FixFolder()
        {
            if (AddMod == null) return;
            if (!Game.IsGameFolder(game)) { Say("Choose the game's folder first: Change at the bottom of the window", true); return; }
            if (Game.IsRunning()) { Say("Close the game first, then fix a folder of mods", true); return; }
            string folder;
            using (var dialog = new FolderBrowserDialog { Description = "Pick the folder with your mods (zip, rar or 7z files)", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                folder = dialog.SelectedPath;
            }
            List<string> mods;
            try
            {
                mods = await Task.Run(() => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(p => new[] { ".zip", ".rar", ".7z" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    .OrderBy(p => p.Substring(folder.Length), StringComparer.OrdinalIgnoreCase).ToList());
            }
            catch (Exception ex) { Say("That folder couldn't be read: " + ex.Message, true); return; }
            string folderName = Path.GetFileName(folder.TrimEnd('\\'));
            if (folderName.Length == 0) folderName = folder;
            if (mods.Count == 0) { Say("No mods (zip, rar or 7z files) in " + folderName, true); return; }
            if (BeforeImport != null) await BeforeImport();
            var bulk = new BulkAdd();
            var lockedBefore = new HashSet<string>(InstallCheck.AllWarnings().Keys);
            stopping = false;
            int done = 0, wanted = 1;
            Busy(true);
            stop.Enabled = true;
            stop.Visible = mods.Count > 1;
            try
            {
                // one first, always: its time on this PC makes the estimate (user 2026-10-27)
                Say("Checking one mod first to time it on this PC...");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await AddMod(mods[0], bulk);
                done = 1;
                StillWorking();
                double each = Math.Max(1, watch.Elapsed.TotalSeconds) + RestSeconds;
                if (mods.Count > 1 && !stopping)
                {
                    using (var ask = new FolderCheckDialog(mods.Count, folderName, each)) { ask.ShowDialog(FindForm()); wanted = ask.Picked; }
                }
                for (int i = 1; i < wanted && !stopping; i++)
                {
                    Say("Preparing the next mod... (" + done + " of " + wanted + " done)");
                    resting = new System.Threading.CancellationTokenSource();
                    try { await Task.Delay(RestSeconds * 1000, resting.Token); }
                    catch (TaskCanceledException) { }
                    finally { resting.Dispose(); resting = null; }
                    if (stopping) break;
                    if (Game.IsRunning()) { bulk.NotAdded.Add("the game was opened, so the rest weren't added"); break; }
                    Say("Checking " + (i + 1) + " of " + wanted + "... " + FolderCheckDialog.FixTime((wanted - i) * each) + " left");
                    try { await AddMod(mods[i], bulk); }
                    catch (Exception ex) { bulk.NotAdded.Add(Path.GetFileName(mods[i]) + ": " + ex.Message); }
                    StillWorking();
                    done = i + 1;
                }
            }
            catch (Exception ex) { bulk.NotAdded.Add(Path.GetFileName(mods[done]) + ": " + ex.Message); }
            finally
            {
                stop.Visible = false;
                Busy(false);
            }
            var changed = Changed;
            if (changed != null) changed(); else Fill();   // the window's reload lists them, then its tests
            // costumes locked by this run (they can't be fixed), counted once each however they were said
            int switchedOff = InstallCheck.AllWarnings().Keys.Count(k => !lockedBefore.Contains(k));
            int fixedOnes = bulk.Added.Count(a => a.Contains("(fixed"));
            var notAdded = bulk.NotAdded.Where(n => !n.Contains(InstallCheck.CantBeFixed)).ToList();
            string summary = "Checked " + done + (done == 1 ? " mod" : " mods") + ": " + bulk.Added.Count + " added" + (fixedOnes > 0 ? " (" + fixedOnes + " fixed)" : "") +
                             (switchedOff > 0 ? ", " + switchedOff + " switched off" : "") +
                             (bulk.Already.Count > 0 ? ", " + bulk.Already.Count + " already in EX More Stuff" : "") +
                             (notAdded.Count > 0 ? ", " + notAdded.Count + " not added" : "") + ".";
            string report = summary +
                            (switchedOff > 0 ? "\n\nThe switched-off ones have problems that can't be fixed: they would make online matches go out of sync or crash the game. " +
                                               "See " + InstallCheck.ListButton + "." : "") +
                            (notAdded.Count > 0 ? "\n\nNot added:\n    " + string.Join("\n    ", notAdded) : "") +
                            (bulk.Already.Count > 0 ? "\n\nAlready in EX More Stuff, left as they are:\n    " + string.Join("\n    ", bulk.Already) : "") +
                            (bulk.Notes.Count > 0 ? "\n\nNote: " + string.Join("\n\n", bulk.Notes) : "");
            if (notAdded.Count > 0) BugReport.Noted("Fix a folder of mods, not added: " + string.Join("; ", notAdded));
            MessageBox.Show(FindForm(), report, "EX More Stuff", MessageBoxButtons.OK, notAdded.Count + switchedOff > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            Say(summary, notAdded.Count + switchedOff > 0);
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
            const string looking = "Looking through the game's folders...";
            Say(looking);
            string g = game;
            // cleared only while it still says that: an import's or a Put back's result said meanwhile stays
            try { found = await Task.Run(() => ModScan.Scan(g)); if (status.Text == looking) Say(""); }
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
                new Heading("Import Your Legacy Mods", width),   // user 2026-10-14 (was "Old mods")
                ShadowLabel.Note("Manage your installed mods with EX More Stuff instead of loose files over the game's own. Each stage and costume becomes a package " +
                                 "of yours in its own slot, and its original files are moved to Mods\\Legacy mods backup. To put one over a game stage or costume " +
                                 "again, pick it on the Stages page or that fighter's page. Other files (HUD, sounds, moves) stay as they are.", width),
            };
            import.Enabled = stages + costumes > 0;
            import.Text = stages + costumes > 0 ? "Import legacy mods (" + (stages + costumes) + ")" : "Import legacy mods";
            import.Width = TextRenderer.MeasureText(import.Text, import.Font).Width + 36;
            controls.Add(import);
            controls.Add(ShadowLabel.Note("Or a folder of mods you downloaded (zip, rar or 7z files): each is checked, fixed where it can be, and added " +
                                          "to EX More Stuff. One that can't be fixed is switched off, so it can't make online matches go out of sync.", width));
            fixFolder.Enabled = !stop.Visible;
            controls.Add(fixFolder);
            controls.Add(stop);
            status.Width = width - 16;
            controls.Add(status);
            if (mods.Count == 0 && !scanning) controls.Add(ShadowLabel.Note("No legacy mods found: everything in the game's folders is the game's own or EX More Stuff's.", width));
            foreach (var m in mods)
                controls.Add(new OldModRow
                {
                    Size = new Size(width, 64), Margin = new Padding(6, 3, 6, 3), Title = m.What,
                    Detail = m.Files.Count + " file" + (m.Files.Count > 1 ? "s" : "") + "   ·   " + m.Files[0] + (m.Files.Count > 1 ? " ..." : "") + "   ·   " + m.Newest.ToString("d MMM yyyy"),
                    State = m.CanImport ? (m.Kind == ModScan.Kind.Stage ? "Stage: can be imported" : "Costume: can be imported") : "Stays as it is",
                    CanImport = m.CanImport,
                });
            // what was imported, each with Put back (user 2026-10-15: "do it for each one individually"; wording approved)
            var imported = Game.IsGameFolder(game)
                ? Installer.Load(game).Concat(Installer.Removed(game)).Where(ModScan.Imported).GroupBy(r => r.Item.Key).Select(g => g.First()).ToList()
                : new List<Installed>();
            if (imported.Count > 0)
            {
                controls.Add(new Heading("Imported legacy mods", width));
                controls.Add(ShadowLabel.Note("Put one back to undo its import: EX More Stuff deletes its package and moves its original files from " +
                                              "Mods\\Legacy mods backup back to where they were in the game folder.", width));
                foreach (Installed r in imported)
                {
                    Installed record = r;
                    bool can = ModScan.Originals(record).Count > 0;
                    string where = record.Item.IsStage ? "Stage " + record.Item.Code : Fighters.Name(record.Item.Fighter) + " " + record.Item.Slot.ToString("D2");
                    var row = new ImportedRow
                    {
                        Size = new Size(width, 64), Margin = new Padding(6, 3, 6, 3), Title = record.Shown ?? record.Item.Name,
                        Detail = can ? where : where + "   ·   Its original files aren't in Mods\\Legacy mods backup any more",
                    };
                    row.PutBack.Enabled = can;
                    row.PutBack.Click += async (s, e) => await PutBack(record, row.Title);
                    controls.Add(row);
                }
            }
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != import && c != status && c != fixFolder && c != stop) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        async Task Import()
        {
            var importable = found.Where(m => m.CanImport).ToList();
            if (importable.Count == 0) return;
            if (Game.IsRunning()) { Say("Close the game first, then import", true); return; }
            // every mod that moves listed, then Replaced Content or Only As Bonus Content (LegacyDialog, user 2026-10-14)
            LegacyDialog.Choice how;
            using (var ask = new LegacyDialog(importable.Select(m => m.What).ToList())) { ask.ShowDialog(FindForm()); how = ask.Picked; }
            if (how == LegacyDialog.Choice.Cancel) return;
            if (BeforeImport != null) await BeforeImport();
            bool keep = how == LegacyDialog.Choice.Replaced;
            var done = new List<string>();
            var problems = new List<string>();
            string g = game;
            Action<string> backUp = path => AppFolders.BackUpOldMod(g, path.Substring(g.TrimEnd('\\').Length + 1));   // safe in Mods\Legacy mods backup
            var before = Weight.Keys(g);
            Busy(true);
            try
            {
                int step = 0;
                foreach (var m in importable)
                {
                    Say(string.Format("Importing {0} ({1}/{2})...", m.What, ++step, importable.Count));
                    try { string where = await Task.Run(() => ModScan.Import(g, m, keep, backUp)); done.Add(m.What + " -> " + where); }
                    catch (Exception ex) { problems.Add(m.What + ": " + ex.Message); }
                }
            }
            finally { Busy(false); }
            var changed = Changed;
            if (changed != null) changed(); else Fill();   // the window's reload lists them again
            var notes = Weight.New(g, before);   // size warnings for what came in: they inform, nothing was stopped
            string noted = notes.Count > 0 ? "\n\nNote: " + string.Join("\n\n", notes) : "";
            if (problems.Count > 0)
                MessageBox.Show(FindForm(), (done.Count > 0 ? done.Count + " imported.\n\n" : "") + "Some couldn't be imported:\n\n" + string.Join("\n", problems) + noted,
                    "EX More Stuff", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (notes.Count > 0)
                MessageBox.Show(FindForm(), done.Count + " imported." + noted, "EX More Stuff", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Say(done.Count + " imported" + (keep ? ", showing where they were" : "") + (problems.Count > 0 ? "; " + problems.Count + " couldn't be" : ""), problems.Count > 0);
        }

        // an import undone (ModScan.PutBack), after a Yes
        async Task PutBack(Installed record, string name)
        {
            if (Game.IsRunning()) { Say("Close the game first", true); return; }
            if (MessageBox.Show(FindForm(), "Put back " + name + "? Its package is deleted from EX More Stuff, and its original files go back into the game folder where they were.",
                                "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string g = game, note = null;
            bool done = false;
            Busy(true);
            try { note = await Task.Run(() => ModScan.PutBack(g, record)); done = true; }
            catch (Exception ex) { Say(name + " wasn't put back: " + ex.Message, true); }
            finally { Busy(false); }
            if (!done) return;
            var changed = Changed;
            if (changed != null) changed(); else Fill();   // listed again above, as a legacy mod
            Say(name + " is back in the game folder as it was" + (note != null ? ". " + note : ""), note != null);
        }

        void Busy(bool on)
        {
            import.Enabled = fixFolder.Enabled = !on;
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

    // The import question (user 2026-10-14, their words): the mods that move, that their files go to EX More Stuff's
    // folders, then Replaced Content (they keep playing in place of the game's) or Only As Bonus Content (the game's own
    // come back, theirs are extra picks in rollback games). Either way they're imported the same.
    class LegacyDialog : Form
    {
        public enum Choice { Cancel, Replaced, Bonus }

        public Choice Picked { get; private set; }

        public LegacyDialog(IList<string> mods)
        {
            Text = "Import Your Legacy Mods";
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            const int width = 600, pad = 22;
            const int shown = 10;   // more than this: "and N more"
            string list = string.Join("\n", mods.Take(shown).Select(m => "    " + m)) + (mods.Count > shown ? "\n    and " + (mods.Count - shown) + " more" : "");
            string moved = "Their files will be moved to EX More Stuff's folders: each becomes a package in Mods, and its original files go to Mods\\Legacy mods backup.";
            Func<string, Font, int, int> tall = (t, f, w) => TextRenderer.MeasureText(t, f, new Size(w, 0), TextFormatFlags.WordBreak).Height;
            int y = 18;
            var heading = new Label { Text = "Manage your legacy DLC as...", AutoSize = false, BackColor = Color.Transparent, Font = Theme.Bold(13f), ForeColor = Theme.Text,
                                      Bounds = new Rectangle(pad, y, width - 2 * pad, 30) };
            y += 38;
            var names = new Label { Text = list, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Text,
                                    Bounds = new Rectangle(pad, y, width - 2 * pad, tall(list, Theme.Font(9.5f), width - 2 * pad) + 4) };
            y = names.Bottom + 8;
            var move = new Label { Text = moved, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Muted,
                                   Bounds = new Rectangle(pad, y, width - 2 * pad, tall(moved, Theme.Font(9.5f), width - 2 * pad) + 4) };
            y = move.Bottom + 18;
            // the two choices side by side, each with what it means under it
            int column = (width - 2 * pad - 16) / 2;
            var replaced = new FlatButton("Replaced Content", true) { Width = column, Location = new Point(pad, y) };
            var bonus = new FlatButton("Only As Bonus Content") { Width = column, Location = new Point(pad + column + 16, y) };
            const string replacedText = "Your content replaces original SF4 Costumes and Stages", bonusText = "Your content becomes bonus items during rollback games.";   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
            int under = Math.Max(tall(replacedText, Theme.Font(9f), column), tall(bonusText, Theme.Font(9f), column)) + 4;
            var replacedAbout = new Label { Text = replacedText, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), ForeColor = Theme.Muted,
                                            TextAlign = ContentAlignment.TopCenter, Bounds = new Rectangle(pad, replaced.Bottom + 6, column, under) };
            var bonusAbout = new Label { Text = bonusText, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), ForeColor = Theme.Muted,
                                         TextAlign = ContentAlignment.TopCenter, Bounds = new Rectangle(bonus.Left, bonus.Bottom + 6, column, under) };
            y = replacedAbout.Bottom + 14;
            const string anyTime = "Your content can be set to Replaced or not at any time with EXMS";
            var note = new Label { Text = anyTime, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), ForeColor = Theme.Muted,
                                   TextAlign = ContentAlignment.TopCenter, Bounds = new Rectangle(pad, y, width - 2 * pad, tall(anyTime, Theme.Font(9f), width - 2 * pad) + 4) };
            y = note.Bottom + 14;
            var cancel = new FlatButton("Cancel");
            cancel.Location = new Point(width - pad - cancel.Width, y);
            ClientSize = new Size(width, cancel.Bottom + 18);
            replaced.Click += (s, e) => { Picked = Choice.Replaced; DialogResult = DialogResult.OK; };
            bonus.Click += (s, e) => { Picked = Choice.Bonus; DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; };
            Controls.AddRange(new Control[] { heading, names, move, replaced, bonus, replacedAbout, bonusAbout, note, cancel });
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }

    // One imported legacy mod: its name, where it is now, and Put back.
    class ImportedRow : Clear
    {
        public string Title, Detail;
        public readonly FlatButton PutBack = new FlatButton("Put back");

        public ImportedRow() { Controls.Add(PutBack); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PutBack.Location = new Point(Width - 16 - PutBack.Width, (Height - PutBack.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            int right = PutBack.Left - 12;
            Theme.Draw(g, Title, Theme.Bold(10.5f), Theme.Text, new Rectangle(14, 10, right - 14, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 34, right - 14, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // What Fix a folder of mods did, mod by mod (the window's AddFiles fills it): nothing is asked on the way
    sealed class BulkAdd
    {
        public readonly List<string> Added = new List<string>(), Already = new List<string>(), NotAdded = new List<string>(), Notes = new List<string>();
    }

    // How many of a folder's mods to check (Fix a folder of mods): one is done already, and timed; the time shown for the
    // number picked counts each mod's time on this PC and the rest between them (never named: user 2026-10-27).
    class FolderCheckDialog : Form
    {
        public int Picked { get; private set; }

        public FolderCheckDialog(int found, string folder, double secondsEach)
        {
            Text = "Fix a folder of mods";
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            const int width = 520, pad = 22;
            Picked = 1;   // Cancel: only the one already done
            Func<int, string> time = n => FixTime((n - 1) * secondsEach);
            var heading = new Label { Text = "Found " + found + " mods in " + folder, AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, Font = Theme.Bold(13f),
                                      ForeColor = Theme.Text, Bounds = new Rectangle(pad, 18, width - 2 * pad, 30) };
            var about = new Label { Text = "Checking all " + found + " takes " + time(found) + " on this PC.", AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f),
                                    ForeColor = Theme.Muted, Bounds = new Rectangle(pad, 56, width - 2 * pad, 22) };
            var check = new Label { Text = "Check", AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(10f), ForeColor = Theme.Text, Location = new Point(pad, 96) };
            var count = new NumericUpDown { Minimum = 1, Maximum = found, Value = found, Width = 70, BackColor = Theme.Well, ForeColor = Theme.Text, Font = Theme.Bold(10f),
                                            BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, Location = new Point(pad + 52, 93) };
            var ofThem = new Label { AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(10f), ForeColor = Theme.Text, Location = new Point(count.Right + 8, 96) };
            Action said = () => ofThem.Text = "of them: " + time((int)count.Value);
            said();
            count.ValueChanged += (s, e) => said();
            var go = new FlatButton("Check", true);
            var cancel = new FlatButton("Cancel");
            int top = 140;
            go.Location = new Point(width - pad - go.Width, top);
            cancel.Location = new Point(go.Left - 10 - cancel.Width, top);
            ClientSize = new Size(width, go.Bottom + 18);
            go.Click += (s, e) => { Picked = (int)count.Value; DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { Picked = 1; DialogResult = DialogResult.Cancel; };
            AcceptButton = null;
            Controls.AddRange(new Control[] { heading, about, check, count, ofThem, go, cancel });
        }

        // the time in a player's words: "under a minute", "about 5 minutes", "about 1.5 hours"
        public static string FixTime(double seconds)
        {
            if (seconds < 45) return "under a minute";
            int minutes = (int)Math.Round(seconds / 60);
            if (minutes < 60) return "about " + minutes + (minutes == 1 ? " minute" : " minutes");
            double hours = Math.Round(seconds / 3600, 1);
            return "about " + hours.ToString("0.#") + (hours == 1 ? " hour" : " hours");
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Picked = 1; DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keys);
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
