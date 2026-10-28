using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Costume creation (user 2026-10-12: "a new little sub menu for costume creation"): tools for people
    // making costumes. The first: color pictures. A costume of the player's own (a package with its zip) in each of its
    // 10 colors, drawn by EX More Stuff in the stance (CostumeRender; the game's picture when it can't), no stage or life
    // bars (PerfTest.ColorPictures), put into its package
    // as <prefix>_<CC>.png for EMBER's character select, ready to share (Tom: "a render of the character with the costume
    // for each color in chara select"). Each row shows the pictures its package has. Color packs too (user 2026-10-18:
    // "where do i go to generate the colors for this?"): a row for a pack's colors of one costume (a zip each), each
    // pictured as the game draws it (PerfTest.PackPictures) into its zip as <prefix>.png, its picture on the Costumes page.
    class CreationPanel : Clear
    {
        string game;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4) };
        int filledWidth;
        bool stale, working;
        // a search over the list (user 2026-10-16: "costume creation tab needs a search function"): every word in its name,
        // fighter or zip; it stays put while the rows below it change
        readonly TextBox search = new TextBox { Width = 220, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10.5f), Margin = new Padding(8, 2, 8, 6) };
        readonly Timer typing = new Timer { Interval = 300 };

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public event Action<bool> Working;    // pictures being made (the rest of the window waits)
        public event Action<string> Changed;  // a package got new pictures (its key): the window reads its lists again
        public Func<string> Blocked;          // why nothing can be made right now, or null

        public CreationPanel()
        {
            Controls.Add(list);
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search costumes");   // EM_SETCUEBANNER: the grey hint
            search.TextChanged += (s, e) => { typing.Stop(); typing.Start(); };
            typing.Tick += (s, e) => { typing.Stop(); ShowRows(); };
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
            if (!Visible && !working) stale = true;   // left: all colors picked again next time (nothing is kept, user)
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Visible && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        // costumes and color packs docked for edits (Docked: not in the game) and the player's own whose zip is there, the
        // newest first (user 2026-10-16: "the top list should be the newest ones you added or installed"): by when its zip was
        // made (adding, installing or docking it makes it; color pictures go into the same zip and don't move it). A pack's
        // colors of one costume (a zip each) are one entry, its colors in order.
        List<List<Installed>> Costumes()
        {
            if (!Game.IsGameFolder(game)) return new List<List<Installed>>();
            Func<Installed, bool> wanted = r => r.Item.IsCostume || r.Item.IsColor;
            return Docked.Load().Where(wanted)
                .Concat(Installer.Load(game).Where(r => wanted(r) && r.Source == "file" && !string.IsNullOrEmpty(r.Package) && File.Exists(r.Package)))
                .GroupBy(r => r.Item.IsColor ? "pack|" + (DockedZip(r) != null) + "|" + r.Item.Fighter + "|" + r.Item.Slot + "|" + PackName(r) : "one|" + r.Source + "|" + r.Package)
                .Select(g => g.OrderBy(r => r.Item.Color).ToList())
                .OrderByDescending(g => g.Max(r => Made(r.Package))).ThenBy(g => Fighters.Name(g[0].Item.Fighter)).ThenBy(g => g[0].Item.Slot).ToList();
        }

        static DateTime Made(string zip)
        {
            try { return string.IsNullOrEmpty(zip) || !File.Exists(zip) ? DateTime.MinValue : File.GetCreationTimeUtc(zip); }
            catch (Exception) { return DateTime.MinValue; }
        }

        static string DockedZip(Installed record) { return record.Source == Docked.Source ? record.Package : null; }

        // the page's width: the window's, up to MaxWidth (user 2026-10-23: not everything full width on a big window)
        const int MaxWidth = 1120;
        int ContentWidth() { return Math.Max(400, Math.Min(MaxWidth, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16)); }

        void Fill(bool keepPlace = false)
        {
            Point place = list.AutoScrollPosition;
            stale = false;
            filledWidth = list.ClientSize.Width;
            int width = ContentWidth();
            var controls = new List<Control>
            {
                new Heading("Costume creation", width),
                new Heading("Costume check", width),
                CheckBar(width),
                new Heading("Color pictures", width),
                ShadowLabel.Note("EX More Stuff draws your costume or color pack in each color, in its fighting stance and " +
                                 "lit like the training stage, as rollback games' own costume pictures are made (no stage, no life bars). They go into the costume's package for rollback games' " +   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                                 "character select, ready to share. A costume it can't draw is pictured by the game instead: Steam needs to be running and the game closed then.", width),
            };
            controls.Add(search);
            status.Width = width - 16;
            controls.Add(status);
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c is CreationRow) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
            ShowRows();
            // made again after something done here (a debug color, pictures): where it was (user 2026-10-16: "it pulls the list
            // all the way to the top when its finished")
            if (keepPlace) list.AutoScrollPosition = new Point(-place.X, -place.Y);
        }

        // the costumes the search finds, under the search box (the top stays, so typing goes on)
        void ShowRows()
        {
            int width = ContentWidth();
            string[] words = search.Text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var all = Costumes();
            var costumes = all.Where(g => words.All(w => g.Any(r => (Weight.Name(r) + " " + Fighters.Name(r.Item.Fighter) + " " + Path.GetFileName(r.Package)).ToLowerInvariant().Contains(w)))).ToList();
            var rows = new List<Control>();
            if (all.Count == 0) rows.Add(ShadowLabel.Note("No costume packages of your own yet: add one with Advanced > Add Mod From File (docked for edits, or " +
                                                          "into the game), or from Browse Mods.", width));
            else if (costumes.Count == 0) rows.Add(ShadowLabel.Note("No costumes match \"" + search.Text.Trim() + "\".", width));
            foreach (List<Installed> group in costumes)
            {
                Installed record = group[0];
                if (record.Item.IsColor)
                {
                    List<Installed> packs = group;
                    var pack = new CreationRow(PackName(record) + (DockedZip(record) != null ? "   (docked)" : ""),
                                               (packs.Count == 1 ? "Color " : "Colors ") + string.Join(", ", packs.Select(p => p.Item.Color)) + " of " + Fighters.Name(record.Item.Fighter) + "'s " +
                                               (record.Item.Slot < Catalog.FirstCustomCostume ? CostumeGrid.CostumeName(record.Item.Slot) : "slot " + record.Item.Slot),
                                               packs.Select(p => PackPicture(p)).ToArray(), packs.Select(p => p.Item.Color).ToArray(), width, false) { Margin = new Padding(8, 4, 8, 4) };
                    pack.Make.Click += async (s, e) => await MakePack(packs, pack.Colors);
                    pack.FullSizeAsked += async color => await MakePackFullSize(packs.First(p => p.Item.Color == color));
                    pack.MakeBanner.Click += (s, e) => OpenPackBanner(packs);
                    pack.Busy = working;
                    rows.Add(pack);
                    continue;
                }
                var row = new CreationRow(Weight.Name(record) + (DockedZip(record) != null ? "   (docked)" : ""), Path.GetFileName(record.Package), Pictures(record),
                                          Enumerable.Range(1, PerfTest.ColorCount).ToArray(), width, true) { Margin = new Padding(8, 4, 8, 4) };
                Installed r = record;
                row.Make.Click += async (s, e) => await Make(r, row.Colors);
                row.FullSizeAsked += async color => await MakeFullSize(r, color);
                row.Check.Click += async (s, e) => await RunCheck(() => CostumeCheck.Find(r.Package), Weight.Name(r));
                row.Check.Enabled = !checking;
                row.View.Click += (s, e) => View(r);
                row.MakeBanner.Click += (s, e) => OpenBanner(r);
                row.ForceAsked += async color => await ForceDebug(r, color);
                row.Busy = working;
                rows.Add(row);
            }
            list.SuspendLayout();
            int keep = list.Controls.IndexOf(status) + 1;
            while (list.Controls.Count > keep) { Control c = list.Controls[keep]; list.Controls.RemoveAt(keep); c.Dispose(); }
            foreach (Control c in rows) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        // The banner maker (user 2026-10-27): every color picture the package has (<prefix>_01.png ... _99.png), laid out on
        // one 1680 x 840 picture that goes next to its zip
        void OpenBanner(Installed record)
        {
            var found = new SortedDictionary<int, Bitmap>();
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(record.Package))
                    for (int c = 1; c <= 99; c++)
                    {
                        string name = record.Item.Prefix + "_" + c.ToString("D2") + ".png";
                        ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) continue;
                        using (Stream s = entry.Open())
                        using (var data = new MemoryStream())
                        {
                            s.CopyTo(data);
                            using (var image = Image.FromStream(new MemoryStream(data.ToArray()))) found[c] = new Bitmap(image);
                        }
                    }
            }
            catch (Exception ex) { Say("Its color pictures couldn't be read: " + ex.Message, true); return; }
            if (found.Count == 0) { Say("Make its color pictures first, then its banner", true); return; }
            string saveTo = Path.Combine(Path.GetDirectoryName(record.Package), Path.GetFileNameWithoutExtension(record.Package) + " - banner.png");
            using (var window = new BannerWindow(Weight.Name(record), found, saveTo)) window.ShowDialog(FindForm());
        }

        // A color pack's banner: each of its colors' picture (<prefix>.png in each color's zip), next to the first one's zip
        void OpenPackBanner(List<Installed> packs)
        {
            var found = new SortedDictionary<int, Bitmap>();
            foreach (Installed pack in packs)
            {
                Image picture = PackPicture(pack);
                if (picture == null) continue;
                using (picture) found[pack.Item.Color] = new Bitmap(picture);
            }
            if (found.Count == 0) { Say("Make its color pictures first, then its banner", true); return; }
            string name = PackName(packs[0]);
            string saveTo = Path.Combine(Path.GetDirectoryName(packs[0].Package), string.Join("_", name.Split(Path.GetInvalidFileNameChars())) + " - banner.png");
            using (var window = new BannerWindow(name, found, saveTo)) window.ShowDialog(FindForm());
        }

        // the color pictures a package has (<prefix>_01.png ... _10.png in its zip), null where there's none
        static Image[] Pictures(Installed record)
        {
            var found = new Image[PerfTest.ColorCount];
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(record.Package))
                    for (int c = 1; c <= found.Length; c++)
                    {
                        string name = record.Item.Prefix + "_" + c.ToString("D2") + ".png";
                        ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) continue;
                        using (Stream s = entry.Open())
                        using (var data = new MemoryStream())
                        {
                            s.CopyTo(data);
                            found[c - 1] = Image.FromStream(new MemoryStream(data.ToArray()));
                        }
                    }
            }
            catch (Exception) { }
            return found;
        }

        // a color pack's name, without the one color's number Weight.Name gives (a row is the pack's colors)
        static string PackName(Installed pack)
        {
            string name = pack.Shown ?? pack.Item.Name;
            return string.IsNullOrEmpty(name) ? Fighters.Name(pack.Item.Fighter) + " colors" : name;
        }

        // a color pack's picture (<prefix>.png in its zip), or null
        static Image PackPicture(Installed pack)
        {
            try { byte[] bytes = Installer.PackagePicture(pack.Package, pack.Item); return bytes != null ? Image.FromStream(new MemoryStream(bytes)) : null; }
            catch (Exception) { return null; }
        }

        void Say(string text, bool problem = false)
        {
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
            if (problem) BugReport.Noted(text);
        }

        // the costume's renders in any color, pose and turn (CostumeViewer)
        void View(Installed record)
        {
            if (working) return;
            string g = game, docked = DockedZip(record);
            using (var viewer = new CostumeViewer(g, record.Item.Fighter + "_" + record.Item.Slot.ToString("D2"), Weight.Name(record), () => PerfTest.Costume(g, record.Item, docked)))
                viewer.ShowDialog(FindForm());
        }

        async Task Make(Installed record, List<int> colors)
        {
            if (colors.Count == 0) return;
            string blocked = Blocked != null ? Blocked() : null;   // the game only matters when a costume can't be drawn (PerfTest checks then)
            if (blocked != null) { Say("Can't make them now: " + blocked, true); return; }
            // an installed costume's package goes into the game again after (PutPictures)
            if (DockedZip(record) == null && Game.IsRunning()) { Say("Can't make them now: close the game first", true); return; }
            string name = Weight.Name(record), g = game, problem = null;
            string which = colors.Count == PerfTest.ColorCount ? "each color" : colors.Count == 1 ? "color " + colors[0] : "colors " + string.Join(", ", colors);
            if (MessageBox.Show(FindForm(), "EX More Stuff draws " + name + " standing in " + which + ", a second or two each. " + (colors.Count == 1 ? "It goes" : "They go") + " into its package for rollback games' character select.", "EX More Stuff",   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            bool gimmicks = Gimmicks(record);
            SortedDictionary<int, Bitmap> made = null;
            bool stopped = false;
            SetWorking(true);
            try
            {
                made = await Task.Run(() => PerfTest.ColorPictures(g, record.Item, colors, text => BeginInvoke((Action)(() => Say(text))), false, DockedZip(record), UseGame(name, colors.Count), gimmicks));
                Say("Putting the pictures into " + name + "'s package...");
                await Task.Run(() => PerfTest.PutPictures(g, record.Item, record, made));
            }
            catch (OperationCanceledException) { stopped = true; }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (stopped) { Say(name + ": no pictures made, the game wasn't opened"); return; }
            if (problem != null)
            {
                Say("The color pictures weren't made: " + problem, true);
                if (made != null) foreach (Bitmap b in made.Values) b.Dispose();
                return;
            }
            Say(name + ": " + (made.Count == 1 ? "color " + made.Keys.First() + "'s picture" : made.Count + " color pictures") + " made and put into its package");
            var changed = Changed;
            if (changed != null) changed(record.Item.Key);
            Fill(true);
            using (var preview = new PicturePreview(name + ": color pictures", made)) preview.ShowDialog(FindForm());
            // then its character's folder in Mods, its zip picked (user 2026-10-12): ready to share
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + record.Package + "\""); } catch (Exception) { }
        }

        // Whether the drawing has the fighter's fight-worn gimmick parts (Yang, Rose, Blanka; CostumePose.WearsGimmicks),
        // asked, Yes first (user 2026-10-23: "give the user the question, render gimmicks?", "default to yes"): most
        // costumes wear them, some put their own over the stock ones (Ghoul Yang's boots over Yang's sneakers)
        bool Gimmicks(Installed record)
        {
            string fighter = record.Item.Fighter;
            if (!CostumePose.WearsGimmicks(fighter)) return true;
            return MessageBox.Show(FindForm(), "Draw " + Fighters.Name(fighter) + "'s gimmick parts?\n\nYes for most costumes: they're part of what " +
                Fighters.Name(fighter) + " wears in a fight. No when this costume has its own in their place, so the stock ones don't show over them.",
                "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes;
        }

        // Asked once more before the game makes the pictures of a costume that couldn't be drawn (its confirm said "a
        // second or two each"); from the drawing's thread, the box on the window's
        Func<string, bool> UseGame(string name, int runs)
        {
            return why => (bool)Invoke((Func<bool>)(() => MessageBox.Show(FindForm(), name + " couldn't be drawn (" + why + "), so the game makes the pictures: it opens " +
                (runs == 1 ? "once, for a few seconds" : runs + " times, a few seconds each") + ". Go ahead?", "EX More Stuff",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK));
        }

        // a pack's colors pictured, each into its zip (PerfTest.PackPictures); as Make does for a costume
        async Task MakePack(List<Installed> packs, List<int> colors)
        {
            var picked = packs.Where(p => colors.Contains(p.Item.Color)).ToList();
            if (picked.Count == 0) return;
            string blocked = Blocked != null ? Blocked() : null;   // the game only matters when a costume can't be drawn (PerfTest checks then)
            if (blocked != null) { Say("Can't make them now: " + blocked, true); return; }
            // an installed pack goes into the game again after (PutPackPictures)
            if (picked.Any(p => DockedZip(p) == null) && Game.IsRunning()) { Say("Can't make them now: close the game first", true); return; }
            string name = PackName(packs[0]), g = game, problem = null;
            string which = picked.Count == 1 ? "color " + picked[0].Item.Color : "colors " + string.Join(", ", picked.Select(p => p.Item.Color));
            if (MessageBox.Show(FindForm(), "EX More Stuff draws " + Fighters.Name(packs[0].Item.Fighter) + " standing in " + which + ", a second or two each. " + (picked.Count == 1 ? "It goes into the color's package and shows" : "They go into each color's package and show") + " on the Costumes page.", "EX More Stuff",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            SortedDictionary<int, Bitmap> made = null;
            bool stopped = false;
            SetWorking(true);
            try
            {
                made = await Task.Run(() => PerfTest.PackPictures(g, picked, text => BeginInvoke((Action)(() => Say(text))), false, UseGame(name, picked.Count)));
                Say("Putting the pictures into " + name + "'s packages...");
                await Task.Run(() => PerfTest.PutPackPictures(g, picked, made));
            }
            catch (OperationCanceledException) { stopped = true; }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (stopped) { Say(name + ": no pictures made, the game wasn't opened"); return; }
            if (problem != null)
            {
                Say("The color pictures weren't made: " + problem, true);
                if (made != null) foreach (Bitmap b in made.Values) b.Dispose();
                return;
            }
            Say(name + ": " + (made.Count == 1 ? "color " + made.Keys.First() + "'s picture" : made.Count + " color pictures") + " made and put into " + (made.Count == 1 ? "its package" : "their packages"));
            var changed = Changed;
            if (changed != null) foreach (Installed p in picked) changed(p.Item.Key);
            Fill(true);
            using (var preview = new PicturePreview(name + ": color pictures", made)) preview.ShowDialog(FindForm());
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + picked[0].Package + "\""); } catch (Exception) { }
        }

        // one of a pack's colors at the game's own size, beside its zip as MakeFullSize does
        async Task MakePackFullSize(Installed pack)
        {
            string blocked = Blocked != null ? Blocked() : null;   // the game only matters when a costume can't be drawn (PerfTest checks then)
            if (blocked != null) { Say("Can't make it now: " + blocked, true); return; }
            string name = PackName(pack), g = game, problem = null;
            int color = pack.Item.Color;
            string file = Path.Combine(Path.GetDirectoryName(pack.Package), Path.GetFileNameWithoutExtension(pack.Package) + " - color " + color + ".png");
            SetWorking(true);
            try
            {
                var made = await Task.Run(() => PerfTest.PackPictures(g, new[] { pack }, text => BeginInvoke((Action)(() => Say(text))), true));
                using (Bitmap picture = made[color]) picture.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (problem != null) { Say("The full-size picture wasn't made: " + problem, true); return; }
            Say(name + ": color " + color + " at full size saved beside its zip (" + Path.GetFileName(file) + ")");
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch (Exception) { }
        }

        // One color at the game's own size (a thumbnail for the mod's page, say), beside the package's zip as
        // "<zip name> - color N.png" (made again: replaced); the package isn't touched. Then Explorer shows it.
        async Task MakeFullSize(Installed record, int color)
        {
            string blocked = Blocked != null ? Blocked() : null;   // the game only matters when a costume can't be drawn (PerfTest checks then)
            if (blocked != null) { Say("Can't make it now: " + blocked, true); return; }
            string name = Weight.Name(record), g = game, problem = null;
            string file = Path.Combine(Path.GetDirectoryName(record.Package), Path.GetFileNameWithoutExtension(record.Package) + " - color " + color + ".png");
            bool gimmicks = Gimmicks(record);
            SetWorking(true);
            try
            {
                var made = await Task.Run(() => PerfTest.ColorPictures(g, record.Item, new[] { color }, text => BeginInvoke((Action)(() => Say(text))), true, DockedZip(record), null, gimmicks));
                using (Bitmap picture = made[color]) picture.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (problem != null) { Say("The full-size picture wasn't made: " + problem, true); return; }
            Say(name + ": color " + color + " at full size saved beside its zip (" + Path.GetFileName(file) + ")");
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch (Exception) { }
        }

        // a color made a debug color (ColorVariants.Force): in its package, and in the game's files when it's in
        async Task ForceDebug(Installed record, int color)
        {
            if (working) return;
            bool inGame = DockedZip(record) == null;
            if (inGame && Game.IsRunning()) { Say("Can't change it now: close the game first", true); return; }
            string name = Weight.Name(record), g = game, problem = null;
            if (MessageBox.Show(FindForm(), "Make color " + color + " of " + name + " a debug color? One of EX More Stuff's patterns takes the place of its textures, in its package" +
                    (inGame ? " and in the game" : "") + ". Its own color " + color + " can only come back by installing the mod again.", "EX More Stuff",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetWorking(true);
            try
            {
                await Task.Run(() =>
                {
                    var made = ColorVariants.Force(record.Package, record.Item.Prefix, color);
                    if (!inGame) return;
                    // the game's copies of those files (the record names where each went)
                    foreach (string file in record.Files)
                    {
                        byte[] data;
                        if (made.TryGetValue(Path.GetFileName(file), out data)) SafeFile.WriteAllBytes(Path.Combine(Game.PatchFolder(g), file), data);
                    }
                });
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (problem != null) { Say("Color " + color + " wasn't changed: " + problem, true); return; }
            Say(name + ": color " + color + " is a debug color now");
            var changed = Changed;
            if (changed != null) changed(record.Item.Key);
            Fill(true);
        }

        // ---- the costume check ----

        readonly FlatButton checkInstalled = new FlatButton("Check installed costumes", true), checkFile = new FlatButton("Check a file or folder..."), showResults = new FlatButton("Show results");
        readonly Label lastCheck = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(9.5f), TextAlign = ContentAlignment.MiddleRight };
        CheckCard checkBar;
        List<CostumeCheck.Result> lastResults;   // only the results: a fix reads its costume again
        string lastTitle;
        bool checking;

        Control CheckBar(int width)
        {
            if (checkBar == null)
            {
                checkBar = new CheckCard { Margin = new Padding(8, 2, 8, 8) };
                checkBar.Controls.AddRange(new Control[] { checkInstalled, checkFile, lastCheck, showResults });
                showResults.Visible = false;
                // the game's costumes as it loads them (patch_ae2_tu3\battle\chara\<CHR>, the game's own 01-07 left out) and
                // the docked ones (out of the game)
                checkInstalled.Click += async (s, e) => await RunCheck(() => CostumeCheck.Installed(game), "installed costumes");
                // a zip, or the costume a file belongs to in its folder (not the folders below)
                checkFile.Click += async (s, e) =>
                {
                    string path;
                    using (var dialog = new OpenFileDialog { Filter = "A costume's zip, or any file in its folder|*.zip;*.emo;*.emm;*.emb|All files|*.*", Title = "Check a costume" })
                    {
                        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                        path = dialog.FileName;
                    }
                    await RunCheck(() => CostumeCheck.Find(path), Path.GetFileName(path));
                };
                showResults.Click += (s, e) => ShowCheck();
            }
            int textHeight = TextRenderer.MeasureText(CheckCard.About, Theme.Font(10.5f), new Size(width - 16 - 32, 0), TextFormatFlags.WordBreak).Height;
            int y = 14 + textHeight + 12;
            checkBar.Size = new Size(width - 16, y + 36 + 16);
            checkInstalled.Location = new Point(16, y);
            checkFile.Location = new Point(checkInstalled.Right + 10, y);
            showResults.Location = new Point(checkBar.Width - showResults.Width - 16, y);
            lastCheck.Bounds = new Rectangle(checkFile.Right + 10, y, Math.Max(0, (showResults.Visible ? showResults.Left : checkBar.Width - 6) - checkFile.Right - 20), 36);
            return checkBar;
        }

        // what the check does and its buttons on a card, as a costume's row
        class CheckCard : Clear
        {
            public const string About = "Finds what breaks a costume: online desyncs (bones the fight uses sitting differently from the costume a player without it sees), " +
                                        "parts that draw white, missing files. Only the files are read, so the game can stay open. A fix always goes into a copy.";

            protected override void OnPaint(PaintEventArgs e)
            {
                Smooth(e.Graphics);
                using (var card = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
                {
                    using (var fill = new SolidBrush(Color.FromArgb(215, Theme.Card))) e.Graphics.FillPath(fill, card);
                    using (var pen = new Pen(Theme.Line)) e.Graphics.DrawPath(pen, card);
                }
                TextRenderer.DrawText(e.Graphics, About, Theme.Font(10.5f), new Rectangle(16, 14, Width - 32, Height), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
                base.OnPaint(e);
            }
        }

        // the check off the window's thread, then its window
        async Task RunCheck(Func<List<CostumeCheck.Source>> find, string title)
        {
            if (checking) return;
            if (!Game.IsGameFolder(game)) { Say("The check needs the game's folder: choose it with Change at the bottom of the window.", true); return; }
            Checking(true);
            Say("Checking " + title + "...");
            string g = game, problem = null;
            var results = new List<CostumeCheck.Result>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // a costume at a time: only its results stay
                await Task.Run(() => { foreach (var source in find()) results.AddRange(CostumeCheck.Check(g, source)); });
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { Checking(false); }
            if (problem != null) { Say("The check stopped: " + problem, true); return; }
            if (results.Count == 0) { Say("No costume (CHR_NN.obj.emo) found in " + title + ".", true); return; }
            lastResults = results; lastTitle = title;
            int faulty = results.Count(r => r.Verdict == CostumeCheck.Verdict.Faulty);
            lastCheck.Text = "Last check: " + results.Count + (results.Count == 1 ? " costume" : " costumes") + (faulty > 0 ? ", " + faulty + " need a fix" : ", nothing to fix");
            lastCheck.ForeColor = faulty > 0 ? Theme.Warning : Theme.Muted;
            showResults.Visible = true;
            CheckBar(checkBar.Width + 16);   // the result's text makes room for Show results
            Say(title + " checked in " + (watch.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s");
            ShowCheck();
        }

        void ShowCheck()
        {
            if (lastResults == null) return;
            using (var window = new CostumeCheckWindow(game, "Costume check: " + lastTitle, lastResults)) window.ShowDialog(FindForm());
        }

        // while a check runs, no other: the card's buttons and every costume row's Check wait
        void Checking(bool on)
        {
            checking = on;
            checkInstalled.Enabled = checkFile.Enabled = !on;
            foreach (CreationRow row in list.Controls.OfType<CreationRow>()) row.Check.Enabled = !on;
        }

        void SetWorking(bool on)
        {
            working = on;
            UseWaitCursor = on;
            foreach (CreationRow row in list.Controls.OfType<CreationRow>()) row.Busy = on;
            var handler = Working;
            if (handler != null) handler(on);
        }
    }

    // One costume on Costume creation: its name and zip, the color pictures it has (empty frames where there's none),
    // and its buttons. Each color is picked (framed in yellow) to begin with; a click on one leaves it out or takes it
    // back, All picks them all or none (user 2026-10-12: to make again only color 10, say), and Make makes the picked.
    class CreationRow : Clear
    {
        public readonly FlatButton Make = new FlatButton("Make color pictures", true);
        public readonly FlatButton View = new FlatButton("View...");   // a costume's renders (CostumeViewer); not for a pack's row
        readonly FlatButton all = new FlatButton("All");
        public readonly FlatButton Check = new FlatButton("Check");   // the costume check on its zip (CostumeCheckWindow); not for a pack's row
        public readonly FlatButton MakeBanner = new FlatButton("Make a banner...");   // its color pictures on one picture (BannerWindow)
        public event Action<int> FullSizeAsked;   // a color's right-click menu: that color at the game's own size
        public event Action<int> ForceAsked;      // ... or that color made a debug color (user 2026-10-16: "right click > force debug color")
        readonly string title, detail;
        readonly Image[] pictures;
        readonly int[] numbers;   // each picture's color: 1-10, or a color pack's 30-99
        readonly bool[] picked;
        readonly bool canForce;   // Force debug color in its menu (a costume's; not a pack's)
        bool busy;
        const int Thumb = 44;

        public CreationRow(string title, string detail, Image[] pictures, int[] numbers, int width, bool canForce)
        {
            this.title = title;
            this.detail = detail;
            this.pictures = pictures;
            this.numbers = numbers;
            this.canForce = canForce;
            picked = Enumerable.Repeat(true, pictures.Length).ToArray();
            Make.Location = new Point(width - 16 - Make.Width - 14, 14);
            all.Size = new Size(Make.Width, 32);
            all.Location = new Point(Make.Left, Make.Bottom + 8);
            all.Click += (s, e) => { bool on = !picked.All(p => p); for (int i = 0; i < picked.Length; i++) picked[i] = on; Picked(); };
            // View... and Check side by side under All, as in the mockup
            View.Size = Check.Size = new Size((all.Width - 8) / 2, all.Height);
            View.Location = new Point(Make.Left, all.Bottom + 8);
            Check.Location = new Point(View.Right + 8, all.Bottom + 8);
            MakeBanner.Size = new Size(all.Width, all.Height);
            MakeBanner.Location = new Point(Make.Left, (canForce ? View : all).Bottom + 8);   // a pack's row too (user 2026-10-27: Decapre's colors 30-43)
            View.Visible = Check.Visible = canForce;
            int bottom = pictures.Length == 0 ? 64 : Cell(pictures.Length - 1).Bottom;
            Size = new Size(width - 16, Math.Max(bottom + 14, MakeBanner.Bottom + 14));
            Controls.Add(MakeBanner);
            Controls.Add(Make);
            Controls.Add(all);
            Controls.Add(View);
            Controls.Add(Check);
        }

        /// <summary>The colors picked (their numbers).</summary>
        public List<int> Colors { get { return Enumerable.Range(0, picked.Length).Where(i => picked[i]).Select(i => numbers[i]).ToList(); } }

        /// <summary>While pictures are being made (here or for another costume): nothing can be changed.</summary>
        public bool Busy { set { busy = value; Picked(); } }

        void Picked()
        {
            Make.Enabled = !busy && picked.Any(p => p);
            all.Enabled = !busy;
            View.Enabled = !busy;   // the viewer draws too: not while pictures are being made
            MakeBanner.Enabled = !busy && pictures.Any(p => p != null);   // once it has color pictures
            Invalidate();
        }

        // a picture's place: in a row left of the buttons, more rows under it when they don't fit (a pack of 14, say)
        Rectangle Cell(int i)
        {
            int across = Math.Max(1, (Make.Left - 24) / (Thumb + 6));
            return new Rectangle(14 + i % across * (Thumb + 6), 58 + i / across * (Thumb * 3 / 2 + 6), Thumb, Thumb * 3 / 2);
        }

        int CellAt(Point p) { for (int i = 0; i < pictures.Length; i++) if (Cell(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = !busy && CellAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = CellAt(e.Location);
            if (busy || i < 0) return;
            if (e.Button == MouseButtons.Right)   // its menu: this color at full size
            {
                ContextMenuStrip menu = DarkMenu.Create();
                int color = numbers[i];
                var full = new ToolStripMenuItem("Make a full-size picture of color " + color);
                full.Click += (s, a) => { var asked = FullSizeAsked; if (asked != null) asked(color); };
                menu.Items.Add(full);
                if (canForce)
                {
                    var force = new ToolStripMenuItem("Force debug color");
                    force.Click += (s, a) => { var asked = ForceAsked; if (asked != null) asked(color); };
                    menu.Items.Add(force);
                }
                menu.Closed += (s, a) => CharacterPicker.BeginDispose(menu);
                menu.Show(this, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            picked[i] = !picked[i];
            Picked();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            {
                using (var fill = new SolidBrush(Color.FromArgb(215, Theme.Card))) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            Theme.Draw(g, title, Theme.Bold(10.5f), Theme.Text, new Rectangle(14, 10, Make.Left - 24, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 32, Make.Left - 24, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < pictures.Length; i++)
            {
                Rectangle cell = Cell(i);
                using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, cell);
                if (pictures[i] != null) g.DrawImage(pictures[i], cell);
                if (!picked[i]) using (var dim = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) g.FillRectangle(dim, cell);   // left out
                Theme.Draw(g, numbers[i].ToString(), Theme.Font(7.5f), picked[i] ? Theme.Text : Theme.Muted, new Rectangle(cell.X, cell.Bottom - 14, cell.Width - 3, 14), TextFormatFlags.Right | TextFormatFlags.Bottom);
                if (picked[i]) using (var frame = new Pen(Theme.Accent, 2f)) g.DrawRectangle(frame, cell.X - 1, cell.Y - 1, cell.Width + 1, cell.Height + 1);   // picked
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Image p in pictures) if (p != null) p.Dispose();
            base.Dispose(disposing);
        }
    }
}
