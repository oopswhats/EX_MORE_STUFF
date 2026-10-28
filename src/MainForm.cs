using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // One window: switch on the costumes and stages you want, Apply, close. Switching off removes. Nothing is
    // changed until Apply, and never while the game is running.
    class MainForm : Form
    {
        // All 44 fighters at once, no scrolling: 11 across, 4 down (the roster never grows).
        const int RosterColumns = 11, RosterRows = 4;

        string game;
        List<Item> catalog = new List<Item>();
        List<Installed> installed = new List<Installed>();
        List<Installed> removed = new List<Installed>();
        readonly Dictionary<string, bool> wanted = new Dictionary<string, bool>();
        readonly Dictionary<string, string> wantedNames = new Dictionary<string, string>();   // renames waiting for Apply
        // Game stages replaced on this PC (stage code -> the code of what it plays as): as applied, and as chosen.
        Dictionary<string, string> replaced = new Dictionary<string, string>();
        readonly Dictionary<string, string> wantedReplaced = new Dictionary<string, string>();
        // Game costumes played as another on this PC ("RYU:2" -> the costume it plays as): as applied, and as chosen.
        Dictionary<string, int> swapped = new Dictionary<string, int>();
        readonly Dictionary<string, int> wantedSwaps = new Dictionary<string, int>();
        // Each fighter's effects mod (Effects; fighter -> its id): as applied, and as chosen
        Dictionary<string, string> effectsOn = new Dictionary<string, string>();
        readonly Dictionary<string, string> wantedEffects = new Dictionary<string, string>();
        readonly List<Action> effectCards = new List<Action>();   // the open fighter's effects cards: each one's switch shown again
        readonly Dictionary<string, Image> pictures = new Dictionary<string, Image>();
        readonly Dictionary<string, Image> stagePictures = new Dictionary<string, Image>();
        readonly Dictionary<string, Image> portraits = new Dictionary<string, Image>();
        readonly Dictionary<string, FighterTile> tiles = new Dictionary<string, FighterTile>();
        string catalogProblem, openFighter;
        readonly Image art = Theme.Resource("background.jpg"), wordmark = Theme.Resource("wordmark.png");
        Bitmap backdrop;

        readonly TabButton costumesTab = new TabButton("Costumes"), stagesTab = new TabButton("Stages"), musicTab = new TabButton("Music"), communityTab = new TabButton("Browse Mods"),
                           advancedTab = new TabButton("Advanced"), aboutTab = new TabButton("About");
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(14, 12, 14, 12) };
        readonly Clear rosterPage = new Clear { Dock = DockStyle.Fill };
        readonly Clear costumePage = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly Label costumeTitle = new Label { AutoSize = true, BackColor = Color.Transparent, Font = Theme.Bold(15f), ForeColor = Theme.Text };
        readonly ScrollList costumeCards = new ScrollList { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        readonly ScrollList stagesPage = new ScrollList { Dock = DockStyle.Fill, Visible = false };
        // Music: a tab of its own, coming soon (user 2026-10-12); the music tools are in Advanced > Music meanwhile
        readonly MusicTab musicTabPage = new MusicTab { Dock = DockStyle.Fill, Visible = false };   // the Music tab (Advanced > Music is the song editor)
        // About: the credits in a small card at the top, leaving the background art in full view.
        readonly Clear aboutPage = new Clear { Dock = DockStyle.Fill, Visible = false };
        // Advanced: tools most players never need, each behind a card (each opens with a way back).
        readonly CommunityPanel communityPage = new CommunityPanel { Dock = DockStyle.Fill, Visible = false };
        readonly ScrollList advancedPage = new ScrollList { Dock = DockStyle.Fill, Visible = false };
        readonly Clear musicView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly MusicPanel musicPage = new MusicPanel { Dock = DockStyle.Fill };
        readonly Clear packagesView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly PackagesPanel packagesPage = new PackagesPanel { Dock = DockStyle.Fill };
        readonly Clear storageView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly StoragePanel storagePage = new StoragePanel { Dock = DockStyle.Fill };
        readonly Clear oldModsView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly OldModsPanel oldModsPage = new OldModsPanel { Dock = DockStyle.Fill };
        readonly Clear freeCodesView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly FreeCodesPanel freeCodesPage = new FreeCodesPanel { Dock = DockStyle.Fill };
        readonly Clear creationView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly CreationPanel creationPage = new CreationPanel { Dock = DockStyle.Fill };
        readonly Clear guideView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly GuidePanel guidePage = new GuidePanel { Dock = DockStyle.Fill };
        string openTool;   // the Advanced tool open: null (its cards), "music", "packages" or "storage"
        readonly Label gameLabel = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(8.5f), AutoEllipsis = true };
        readonly LinkLabel change = new LinkLabel { Text = "Change", AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(8.5f),
                                                    LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline };
        readonly Label status = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Bold(9.5f), AutoEllipsis = true };
        readonly Strip header = new Strip { Height = 76 }, footer = new Strip { Height = 70, LineOnTop = true };
        readonly ProgressLine progress = new ProgressLine();
        readonly ToolTip tips = new ToolTip();
        readonly FlatButton apply = new FlatButton("Apply changes", true) { Enabled = false };
        // the costumes the check switched off and locked (InstallCheck; user 2026-10-27: "add a warning button at the bottom to
        // the right of apply changes"): "⚠ Warnings (3)", always there; a click lists them with what's wrong
        readonly FlatButton lockedCostumes = new FlatButton("⚠ Warnings (0)");
        readonly FlatButton disableAll = new FlatButton("Disable everything");
        readonly FlatButton refresh = new FlatButton("Refresh");
        readonly FolderButton folderButton = new FolderButton();

        // A thin orange line along the top of the footer while downloading.
        class ProgressLine : Clear
        {
            int value = -1;
            public int Value { get { return value; } set { this.value = value; Invalidate(); } }
            protected override void OnPaint(PaintEventArgs e)
            {
                if (value < 0) return;
                using (var brush = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(brush, 0, 0, Width * Math.Min(100, value) / 100, Height);
            }
        }

        public MainForm()
        {
            Text = Program.Title;
            Font = Theme.Font(9f);
            ForeColor = Theme.Text;
            BackColor = Theme.Base;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(1000, 800);
            // a screen shorter than that for programs (150% scaling on a 1080p screen: 1280 x 720): as tall as fits,
            // the pages scroll
            int room = Screen.FromPoint(Cursor.Position).WorkingArea.Height;
            if (Height > room) Height = Math.Max(520, room);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            // Header: logo, the EX MORE STUFF lettering, what it's for, tabs.
            header.Dock = DockStyle.Top;
            var logo = new PictureBox { Image = Theme.Resource("logo.png"), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Bounds = new Rectangle(18, 14, 48, 48) };
            // Holding the shrine for 5 seconds switches developer mode on or off (DevMode); the shrine is blue while it's on.
            Image shrine = logo.Image, blueShrine = Blue(shrine);
            var hold = new Timer { Interval = 5000 };
            logo.Image = DevMode.On ? blueShrine : shrine;
            logo.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) hold.Start(); };
            logo.MouseUp += (s, e) => hold.Stop();
            logo.MouseLeave += (s, e) => hold.Stop();
            hold.Tick += (s, e) => { hold.Stop(); DevMode.On = !DevMode.On; logo.Image = DevMode.On ? blueShrine : shrine; };
            var title = new PictureBox { Image = wordmark, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Bounds = new Rectangle(76, 16, 205, 44) };
            var divider = new Panel { BackColor = Color.FromArgb(52, 55, 62), Bounds = new Rectangle(title.Right + 14, 22, 1, 32) };
            // on three lines, the tabs side by side (each has its own room around its word): six tabs fit beside it
            tagline = new Label { Text = "Custom costumes,\nstages and music\nfor Ultra Street Fighter IV", AutoSize = true, BackColor = Color.Transparent,
                                  Font = Theme.Font(9f), ForeColor = Theme.Muted, Location = new Point(divider.Right + 12, 15) };
            int right = ClientSize.Width - 18;
            foreach (TabButton tab in new[] { aboutTab, advancedTab, communityTab, musicTab, stagesTab, costumesTab })
            {
                tab.Location = new Point(right - tab.Width, 26);
                right = tab.Left;
            }
            costumesTab.Selected = true;
            // A tab always opens its main page (user): all fighters, the Advanced tools, all mods, and a page from its top.
            costumesTab.Click += (s, e) => { ShowRoster(); ShowTab(costumesTab); };
            stagesTab.Click += (s, e) => { ShowTab(stagesTab); stagesPage.AutoScrollPosition = Point.Empty; };
            musicTab.Click += (s, e) => ShowTab(musicTab);
            // all mods, even with something new from a favorite creator (user 2026-10-14: "don't make the browse mod take you to
            // notifications anymore"; the Notifications button is pink instead)
            communityTab.Click += (s, e) => { ShowTab(communityTab); communityPage.ShowAll(); };
            advancedTab.Click += (s, e) => { openTool = null; ShowTab(advancedTab); };
            aboutTab.Click += (s, e) => ShowTab(aboutTab);
            header.Controls.AddRange(new Control[] { logo, title, divider, tagline, costumesTab, stagesTab, musicTab, communityTab, advancedTab, aboutTab });

            // Footer: status, the game folder, buttons.
            footer.Dock = DockStyle.Bottom;
            progress.Bounds = new Rectangle(0, 0, ClientSize.Width, 3);
            // the buttons a little smaller than elsewhere (same text size), so three fit beside the status
            foreach (FlatButton b in new[] { refresh, disableAll, apply }) b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + 24, 32);   // (Restore previous fits in Disable everything's width)
            LayOutFooter();
            change.LinkClicked += (s, e) => ChooseGame();
            footer.Controls.AddRange(new Control[] { progress, status, gameLabel, change, folderButton, refresh, disableAll, apply, lockedCostumes });
            lockedCostumes.Click += (s, e) => ShowLockedCostumes();
            refresh.Click += async (s, e) => await RefreshAll();
            communityPage.RefreshAll = RefreshAll;   // Browse Mods' own Refresh does the same
            disableAll.Click += async (s, e) => { if (Previous() != null) await RestorePrevious(); else DisableEverything(); };   // or Restore previous
            apply.Click += async (s, e) => await Apply();

            // A fighter's costumes: back, name, cards.
            var back = new FlatButton("< All fighters") { Location = new Point(6, 2) };
            back.Click += (s, e) => ShowRoster();
            costumeTitle.Location = new Point(back.Right + 14, 6);
            var bar = new Clear { Dock = DockStyle.Top, Height = 46 };
            bar.Controls.Add(back);
            bar.Controls.Add(costumeTitle);
            costumePage.Controls.Add(costumeCards);
            costumePage.Controls.Add(bar);
            rosterPage.Resize += (s, e) => LayOutRoster();

            // The Advanced tools (from the page's cards), each with the way back to it.
            foreach (var view in new[] { Tuple.Create(musicView, (Control)musicPage), Tuple.Create(packagesView, (Control)packagesPage), Tuple.Create(storageView, (Control)storagePage), Tuple.Create(freeCodesView, (Control)freeCodesPage),
                                       Tuple.Create(oldModsView, (Control)oldModsPage), Tuple.Create(creationView, (Control)creationPage), Tuple.Create(guideView, (Control)guidePage) })
            {
                var toAdvanced = new FlatButton("< Advanced") { Location = new Point(6, 2) };
                backButtons.Add(toAdvanced);
                toAdvanced.Click += (s, e) => { openTool = null; ShowTab(advancedTab); };
                var toolBar = new Clear { Dock = DockStyle.Top, Height = 46 };
                toolBar.Controls.Add(toAdvanced);
                view.Item1.Controls.Add(view.Item2);
                view.Item1.Controls.Add(toolBar);
            }
            packagesPage.Changed += () => ReloadKeeping(null);   // a move or a delete there: the rest waiting for Apply stays
            packagesPage.Blocked = () => Pending() > 0 ? "apply or undo the other changes waiting first" : null;
            communityPage.Changed += Reload;
            communityPage.Blocked = () => Pending() > 0 ? "apply or undo the other changes waiting first" : null;
            communityPage.BeforeInstall = EnsurePcCheck;
            oldModsPage.BeforeImport = EnsurePcCheck;
            // Fix a folder of mods: each mod as Add Mod From File adds it, nothing asked; the tests wait for the end (Changed)
            oldModsPage.AddMod = async (path, bulk) => { holdTests = true; try { await AddFiles(new[] { path }, bulk); } finally { holdTests = false; } };
            communityPage.UpdatesChanged += count => communityTab.Dot = count > 0;   // the purple dot: updates on GameBanana
            communityPage.NewFromFavorites += unseen => communityTab.Heart = unseen;   // the heart: new from favorite creators
            freeCodesPage.Portrait = code => { Image portrait; return portraits.TryGetValue(code, out portrait) ? portrait : null; };
            communityPage.Portrait = code => { Image portrait; return portraits.TryGetValue(code, out portrait) ? portrait : null; };
            communityPage.Working += PageWorking;
            packagesPage.Working += PageWorking;
            musicPage.Working += PageWorking;
            musicPage.Saved += () => musicTabPage.SetGame(game);   // a song saved on Advanced > Music: in Your songs on the Music tab at once (user 2026-10-17)
            musicTabPage.Working += PageWorking;
            musicTabPage.Changed += () => { musicPage.SetGame(game); storagePage.SetGame(game); communityPage.SetGame(game); };   // a song set or uninstalled on the Music tab (a song mod: Browse Mods offers it again)
            oldModsPage.Working += PageWorking;
            creationPage.Working += PageWorking;
            creationPage.Blocked = () => Pending() > 0 ? "apply or undo the other changes waiting first" : null;
            creationPage.Changed += key => { pictures.Remove(key); ReloadKeeping(null); };   // its card shows the new color 1
            oldModsPage.Changed += () => { ReloadKeeping(null); BeginInvoke((Action)(async () => await TestUntested())); };   // old mods brought in: the rest waiting for Apply stays
            communityPage.Working += busy => { if (!busy) BeginInvoke((Action)(async () => await TestUntested())); };
            communityPage.FooterSays = SetStatus;
            communityPage.UninstallMod = UninstallMod;   // an install's steps in the footer, its usual line back after
            communityPage.Working += busy => { if (!busy) UpdateStatus(); };
            guidePage.Go += GuideGo;
            unlock.Tick += (s, e) => { unlock.Stop(); Lock(false); };

            var credits = new AboutCard(wordmark, typeof(MainForm).Assembly.GetName().Version.ToString());
            aboutPage.Controls.Add(credits);
            // 18+ mods off Browse Mods (user 2026-10-17: "In the about tab, default off, allow users to disable NSFW content from
            // appearing, and only there")
            var nsfw = new Toggle("Hide NSFW mods") { Checked = Settings.HideNsfw };
            tips.SetToolTip(nsfw, "Mods their creators rated on GameBanana for sexual content or themes, nudity, fetish content, intense violence or blood and gore don't show on Browse Mods");
            nsfw.CheckedChanged += (s, e) => { Settings.HideNsfw = nsfw.Checked; communityPage.NsfwChanged(); };
            aboutPage.Controls.Add(nsfw);
            aboutPage.Resize += (s, e) =>
            {
                credits.Location = new Point((aboutPage.Width - credits.Width) / 2, 8);
                nsfw.Location = new Point((aboutPage.Width - nsfw.Width) / 2, credits.Bottom + 14);
            };

            content.Controls.AddRange(new Control[] { rosterPage, costumePage, stagesPage, musicTabPage, communityPage, advancedPage, musicView, packagesView, storageView, freeCodesView, oldModsView, creationView, guideView, aboutPage });
            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
            // The window opens already drawn: invisible until its first paint is done (the first is slow: GDI+ starting,
            // every tile and picture drawn the first time), then it fades in. The stage pictures are decoded meanwhile.
            Opacity = 0;
            Task.Run(() => { foreach (string code in Stages.Codes) try { Stages.Picture(code); } catch (Exception) { } });
            Shown += (s, e) => FadeIn();
            Load += async (s, e) => await Start();
            FormClosing += (s, e) =>
            {
                if (!closingToUpdate && Pending() > 0 && MessageBox.Show(this, "You have changes that aren't applied yet. Close anyway?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) e.Cancel = true;
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (backdrop == null || backdrop.Size != ClientSize)
            {
                if (backdrop != null) backdrop.Dispose();
                if (musicBackdrop != null) { musicBackdrop.Dispose(); musicBackdrop = null; }
                backdrop = Theme.Backdrop(art, ClientSize, header.Height, ClientSize.Height - footer.Height);
            }
            e.Graphics.DrawImageUnscaled(musicTab.Selected ? MusicBackdrop() : backdrop, 0, 0);
        }

        // the Music tab's background: the art tinted blue between the header and the footer (user 2026-10-16)
        Bitmap musicBackdrop;

        Bitmap MusicBackdrop()
        {
            if (musicBackdrop != null) return musicBackdrop;
            musicBackdrop = new Bitmap(backdrop);
            using (Graphics g = Graphics.FromImage(musicBackdrop))
            using (var blue = new SolidBrush(Color.FromArgb(110, 20, 70, 200)))
                g.FillRectangle(blue, 0, header.Height, ClientSize.Width, ClientSize.Height - footer.Height - header.Height);
            return musicBackdrop;
        }

        async Task Start()
        {
            BuildAdvanced();
            AppFolders.ClearUpdateHint();   // left by the update link: this is the new version
            game = Settings.GameFolder;
            if (!Game.IsGameFolder(game)) game = Game.Find();
            if (!Game.IsGameFolder(game)) { Opacity = 1; if (!ChooseGame()) { Close(); return; } }
            Settings.GameFolder = game;
            ShowGame();
            string unfinished = PerfTest.Recover(game);   // a performance test that stopped halfway: what it changed goes back
            // half-written files a crash left (SafeFile: the real ones were never touched): cleared
            SafeFile.CleanUp(Game.PatchFolder(game));
            SafeFile.CleanUp(AppFolders.Root);
            if (unfinished != null) MessageBox.Show(this, unfinished, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await AdoptPackages();
            await ForgetMissingPackages();
            Reload();   // what's installed, right away: the catalog's items join when it comes
            SetStatus("Loading the catalog...");
            try { catalog = await Catalog.Load(Program.CatalogSource()); catalogProblem = null; }
            catch (Exception ex) { catalog = new List<Item>(); catalogProblem = ex.Message; }
            ShowProgramUpdate();
            Reload();
            await TakeOutPulled();
            BeginInvoke((Action)(async () => await CheckOldCostumes()));   // in the background, a costume at a time
            communityPage.CheckForUpdates();   // GameBanana's list, read in the background: updates for installed mods
            // GameBanana 1-Click links (OneClick): the one it was started with, and any handed over while it's open
            WatchLinks();
            string link = OneClick.Pending ?? OneClick.Take();
            OneClick.Pending = null;
            if (link != null) await OpenLink(link);
        }

        // mods pulled for now (Catalog.Pulled): out of the game, switched off and kept to come back, said once; with the game
        // open, at the next start
        async Task TakeOutPulled()
        {
            if (!Game.IsGameFolder(game) || Game.IsRunning()) return;
            var pulled = installed.Where(r => Catalog.PulledWhy(r.Item) != null).ToList();
            if (pulled.Count == 0) return;
            string g = game;
            var said = new List<string>();
            foreach (Installed r in pulled)
                try { await Task.Run(() => Installer.Remove(g, r.Item.Key)); said.Add(Weight.Name(r) + " is switched off for now: " + Catalog.PulledWhy(r.Item)); }
                catch (Exception) { }
            ReloadKeeping(null);
            if (said.Count > 0) MessageBox.Show(this, string.Join("\n\n", said.Distinct()), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // The player's own costumes that came in before the costume check (user 2026-10-27: "check installed"): each one's
        // package read in the background, one at a time with a rest between (nothing else waits meanwhile); then the faulty
        // ones fixed, or switched off and locked, with the window waiting for that short part. Each is checked once (again
        // only for a new version). With the game open, at the next start.
        bool checkingOld;

        async Task CheckOldCostumes()
        {
            if (checkingOld || !Game.IsGameFolder(game)) return;
            checkingOld = true;
            try
            {
                string g = game;
                var faulty = new List<Installed>();
                foreach (Installed r in await Task.Run(() => InstallCheck.Unchecked(g)))
                {
                    if (g != game || IsDisposed) return;
                    Installed each = r;
                    if (await Task.Run(() => InstallCheck.Faulty(g, each))) faulty.Add(r);
                    await Task.Delay(5000);   // light on the PC: a rest between costumes
                }
                if (faulty.Count == 0) return;
                while (locked && g == game && !IsDisposed) await Task.Delay(2000);   // after whatever is working now
                if (g != game || IsDisposed || Game.IsRunning()) return;
                var off = new List<string>();
                int fixedOnes = 0;
                PageWorking(true);
                try
                {
                    foreach (Installed r in faulty)
                    {
                        SetStatus("Fixing " + Weight.Name(r) + " for online matches...");
                        Installed each = r;
                        try
                        {
                            string why = await Task.Run(() => InstallCheck.CheckInstalled(g, each));
                            if (why != null) off.Add(Weight.Name(r)); else fixedOnes++;
                        }
                        catch (Exception ex) { BugReport.Noted("Costume check of " + Weight.Name(r) + ": " + ex.Message); }
                    }
                }
                finally { PageWorking(false); }
                ReloadKeeping(null);
                if (fixedOnes + off.Count == 0) return;
                MessageBox.Show(this, "Your costumes were checked for problems in online matches." +
                                (fixedOnes > 0 ? "\n\n" + fixedOnes + (fixedOnes == 1 ? " was" : " were") + " fixed." : "") +
                                (off.Count > 0 ? "\n\nSwitched off, with " + InstallCheck.CantBeFixed + ":\n    " + string.Join("\n    ", off) +
                                                 "\n\nThey would make online matches go out of sync or crash the game. See " + InstallCheck.ListButton + "." : ""),
                                Text, MessageBoxButtons.OK, off.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            finally { checkingOld = false; }
        }

        FileSystemWatcher links;

        void WatchLinks()
        {
            try
            {
                links = new FileSystemWatcher(AppFolders.Root, OneClick.InboxName) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                FileSystemEventHandler handed = (s, e) => BeginInvoke((Action)(async () => { string l = OneClick.Take(); if (l != null) await OpenLink(l); }));
                links.Created += handed;
                links.Changed += handed;
                links.Renamed += (s, e) => handed(s, e);
                links.EnableRaisingEvents = true;
            }
            catch (Exception) { }
        }

        // a 1-Click link: Browse Mods, its mod installed as its card's Install does
        async Task OpenLink(string link)
        {
            long id; string file;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (!OneClick.Parse(link, out id, out file)) { UpdateStatus("That link isn't one EX More Stuff can open"); return; }
            ShowTab(communityTab);
            await communityPage.InstallFromLink(id);
        }

        // packages' zips into their places in the Mods folder: from before it (once; it may take a moment for big zips),
        // and sorted into Stages\ and Characters\<fighter>\
        async Task AdoptPackages()
        {
            if (!Game.IsGameFolder(game)) return;
            string g = game;
            try { await Task.Run(() => { Installer.AdoptPackages(g); Installer.MergeColorZips(g); }); }   // a pack's colors in one zip (0.92)
            catch (Exception ex) { MessageBox.Show(this, "Some packages couldn't be put in " + AppFolders.Mods + ": " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // Refresh: the game folder and the Mods folder looked at again, as when the program starts (zips sorted into Mods\,
        // zips deleted by hand, what's installed, old mods, disk space); changes waiting for Apply stay.
        async Task RefreshAll()
        {
            if (locked || !Game.IsGameFolder(game)) return;
            Lock(true);
            try
            {
                await AdoptPackages();
                await ForgetMissingPackages();
            }
            finally { Lock(false); }
            ReloadKeeping(null);
            communityPage.ReadAgain();   // GameBanana's list too: new uploads, files checked since
            UpdateStatus("Refreshed");
        }

        // Packages whose zip the player deleted from the Mods folder: are they to come out of the game too (as with each
        // card's trash can)? "No" is remembered, so they aren't asked about again.
        async Task ForgetMissingPackages()
        {
            if (!Game.IsGameFolder(game) || Game.IsRunning()) return;   // asked next time instead
            var kept = Settings.KeptWithoutZip;
            var gone = Installer.MissingPackages(game).Where(r => !kept.Contains(r.Item.Key)).ToList();
            if (gone.Count == 0) return;
            bool one = gone.Count == 1;
            string names = string.Join("\n", gone.Take(10).Select(r => "    " + (r.Item.IsStage ? "" : Fighters.Name(r.Item.Fighter) + " ") + r.Item.TitleNamed(r.Shown ?? r.Item.Name))) +
                           (gone.Count > 10 ? "\n    and " + (gone.Count - 10) + " more" : "");
            var answer = MessageBox.Show(this, (one ? "This package's zip is" : "These " + gone.Count + " packages' zips are") + " gone from your Mods folder:\n\n" + names +
                    "\n\nDelete " + (one ? "it" : "them") + " from EX More Stuff too? " + (one ? "Its" : "Their") + " files come out of the game, as with the trash can on " + (one ? "its card" : "their cards") + "." +
                    "\n\nNo: " + (one ? "it stays" : "they stay") + " in the game, and you won't be asked about " + (one ? "it" : "them") + " again.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.No)
            {
                Settings.KeptWithoutZip = kept.Concat(gone.Select(r => r.Item.Key)).Distinct().ToList();
                return;
            }
            string g = game;
            var problems = new List<string>();
            SetStatus("Deleting " + gone.Count + " package" + (one ? "" : "s") + "...");
            await Task.Run(() =>
            {
                foreach (Installed r in gone)
                    try { PackagesPanel.PurgeForGood(g, r); }
                    catch (Exception ex) { lock (problems) problems.Add(r.Item.TitleNamed(r.Shown ?? r.Item.Name) + ": " + ex.Message); }
            });
            if (problems.Count > 0)
                MessageBox.Show(this, "Some couldn't be deleted:\n\n" + string.Join("\n", problems), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);

        // when it's shown: everything painted at once while still invisible (every child window too), then it fades in
        // over about 0.15 s
        void FadeIn()
        {
            if (Opacity >= 1) return;
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x80 | 0x100);   // RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW
            var fade = new Timer { Interval = 15 };
            fade.Tick += (s, e) => { Opacity = Math.Min(1, Opacity + 0.1); if (Opacity >= 1) fade.Dispose(); };
            fade.Start();
        }

        Label tagline;

        // A newer EX More Stuff in the catalog: a link to its page in the header, in the tagline's place (the tabs leave
        // no room beside it); nothing is downloaded by itself.
        void ShowProgramUpdate()
        {
            if (Catalog.ProgramVersion == null || Catalog.ProgramVersion <= typeof(MainForm).Assembly.GetName().Version) return;
            var link = new LinkLabel
            {
                Text = "Version " + Catalog.ProgramVersion + " is out  ›", AutoSize = true, BackColor = Color.Transparent,
                Font = Theme.Bold(9.5f), LinkColor = Theme.GoodText, ActiveLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline,
            };
            link.Location = new Point(tagline.Left, tagline.Top + (tagline.Height - TextRenderer.MeasureText(link.Text, link.Font).Height) / 2);
            tagline.Visible = false;
            link.LinkClicked += async (s, e) => await UpdateProgram();
            string news = Catalog.ProgramNews;   // what it brings, from the catalog (user 2026-10-18)
            tips.SetToolTip(link, (string.IsNullOrEmpty(news) ? "" : news + "\n\n") + "Get the new EX More Stuff: " + Catalog.ProgramPage);
            header.Controls.Add(link);
            link.BringToFront();
        }

        // The update link: asked first. On Yes, once nothing is working (an install, Apply, a download ... finishes first,
        // and any open question is answered), the program's folder opens with EXMoreStuff.exe picked, the new version's zip
        // starts downloading in the browser (its release page instead, if the zip isn't up yet), and EX More Stuff closes
        // so the new exe can take this one's place.
        bool updating, closingToUpdate;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindowEnabled(IntPtr window);   // false while a message box is open over the window

        async Task UpdateProgram()
        {
            if (updating) return;
            int pending = Pending();
            string exe = Application.ExecutablePath;
            if (MessageBox.Show(this, "Update to EX More Stuff " + Catalog.ProgramVersion + "?\n\n" +
                    "EX More Stuff closes" + (locked ? " once what it's doing now is finished" : "") + ", its folder opens with EXMoreStuff.exe selected, " +
                    "and the new version starts downloading in your browser.\n\nOpen the zip and put its EXMoreStuff.exe in place of this one. " +
                    "Your mods, songs and settings stay as they are." +
                    (pending > 0 ? "\n\nYour " + pending + " change" + (pending > 1 ? "s" : "") + " waiting for Apply won't be applied." : ""),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            updating = true;
            if (locked) SetStatus("Updating: EX More Stuff closes when what it's doing is finished...");
            var zipUp = Catalog.Reachable(Catalog.ProgramDownload);   // checked meanwhile
            while (locked || !IsWindowEnabled(Handle)) await Task.Delay(300);
            AppFolders.LeaveUpdateHint();   // "Drag EXE to update.txt" beside it, gone again when the new one starts
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + exe + "\""); } catch (Exception) { }
            try { System.Diagnostics.Process.Start(await zipUp ? Catalog.ProgramDownload : Catalog.ProgramPage); } catch (Exception) { }
            closingToUpdate = true;   // the question about changes not applied was part of this one
            Close();
        }

        bool ChooseGame()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the Ultra Street Fighter IV folder (the one with SSFIV.exe in it)." })
            {
                while (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (Game.IsGameFolder(dialog.SelectedPath))
                    {
                        game = dialog.SelectedPath;
                        Settings.GameFolder = game;
                        portraits.Clear();
                        ShowGame();
                        Reload();
                        return true;
                    }
                    MessageBox.Show(this, "That folder has no SSFIV.exe. Choose the game's own folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            return Game.IsGameFolder(game);
        }

        int gameLabelRoom;   // the most the game folder's line may take, left of the footer's buttons

        // The shrine in blue (developer mode): its red and blue swapped, so the orange turns blue.
        static Image Blue(Image image)
        {
            if (image == null) return null;
            var blue = new Bitmap(image.Width, image.Height);
            using (var g = Graphics.FromImage(blue))
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]
                {
                    new float[] { 0, 0, 1, 0, 0 }, new float[] { 0, 1, 0, 0, 0 }, new float[] { 1, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 },
                }));
                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
            return blue;
        }

        void ShowGame()
        {
            gameLabel.Text = "Game folder: " + game;
            musicPage.SetGame(game);
            FitGameLabel();
            ShowFolder();
            ShowLockedButton();
        }

        // the label only as wide as its text (it's see-through, and would hide the Change link under it)
        void FitGameLabel()
        {
            int width = Math.Min(gameLabelRoom, TextRenderer.MeasureText(gameLabel.Text, gameLabel.Font).Width);
            gameLabel.Width = width;
            change.Location = new Point(gameLabel.Left + width + 2, gameLabel.Top);
            change.BringToFront();
        }

        void ShowTab(TabButton tab)
        {
            costumesTab.Selected = tab == costumesTab;
            stagesTab.Selected = tab == stagesTab;
            musicTab.Selected = tab == musicTab;
            communityTab.Selected = tab == communityTab;
            advancedTab.Selected = tab == advancedTab;
            aboutTab.Selected = tab == aboutTab;
            Invalidate(true);   // the background: blue on the Music tab
            advancedPage.Visible = tab == advancedTab && openTool == null;
            // the Music page's list made again each time it opens: stages docked or added since show (user 2026-10-14: the stage
            // docked, then added, wasn't on it; it was made only at start); the song open and the row picked stay
            bool musicOpening = !musicView.Visible && tab == advancedTab && openTool == "music";
            musicView.Visible = tab == advancedTab && openTool == "music";
            if (musicOpening) musicPage.SetGame(game);
            packagesView.Visible = tab == advancedTab && openTool == "packages";
            storageView.Visible = tab == advancedTab && openTool == "storage";
            freeCodesView.Visible = tab == advancedTab && openTool == "freecodes";
            oldModsView.Visible = tab == advancedTab && openTool == "oldmods";
            creationView.Visible = tab == advancedTab && openTool == "creation";
            guideView.Visible = tab == advancedTab && openTool == "guide";
            if (!musicView.Visible) musicPage.StopPlaying();
            stagesPage.Visible = tab == stagesTab;
            musicTabPage.Visible = tab == musicTab;
            communityPage.Visible = tab == communityTab;
            aboutPage.Visible = tab == aboutTab;
            rosterPage.Visible = tab == costumesTab && openFighter == null;
            costumePage.Visible = tab == costumesTab && openFighter != null;
            ShowFolder();
        }

        // The footer's folder button: the open page's folder, its name under the icon (pages without one: none).
        void ShowFolder()
        {
            string path = null, name = null;
            bool haveGame = Game.IsGameFolder(game);
            if (costumesTab.Selected) path = openFighter != null ? AppFolders.FighterPackages(openFighter) : Path.Combine(AppFolders.Mods, "Characters");
            else if (stagesTab.Selected) path = AppFolders.StagePackages;
            else if (musicTab.Selected) path = AppFolders.Music;
            else if (communityTab.Selected) path = AppFolders.Mods;
            else if (aboutTab.Selected) path = AppFolders.Root;
            else if (advancedTab.Selected)
            {
                if (openTool == null || openTool == "packages") path = AppFolders.Mods;
                else if (openTool == "music") path = AppFolders.Music;
                else if (openTool == "storage" && haveGame) path = Game.PatchFolder(game);
                else if (openTool == "oldmods" && haveGame) { path = game; name = "Game folder"; }
                else if (openTool == "creation") path = Path.Combine(AppFolders.Mods, "Characters");
            }
            folderButton.SetFolder(path, name ?? (path == null ? null : Path.GetFileName(path.TrimEnd('\\'))));
            if (path != null) tips.SetToolTip(folderButton, "Open " + path);
        }

        // Everything the pages show: the catalog plus what's installed (players' own packages included) and the
        // players' own packages that were removed (switched off until they're switched back on or forgotten).
        IEnumerable<Item> AllItems()
        {
            var keys = new HashSet<string>(catalog.Select(i => i.Key));
            var listed = installed.Where(r => !keys.Contains(r.Item.Key)).Select(r => r.Item).ToList();
            keys.UnionWith(listed.Select(i => i.Key));
            return catalog.Concat(listed).Concat(removed.Where(r => !keys.Contains(r.Item.Key)).Select(r => r.Item));
        }

        Installed RemovedRecord(string key) { return removed.FirstOrDefault(r => r.Item.Key == key); }

        bool IsInstalled(string key) { return installed.Any(r => r.Item.Key == key); }

        // why an item can't be used on this PC: it failed its performance test, or it's a costume the check found problems in
        // that can't be fixed (InstallCheck); null: it can
        static string Unusable(Item item) { return PerfTest.Locked(item) ?? (item.IsCostume ? InstallCheck.Warning(item.Key) : null); }

        // a locked costume's words, on its card and in the footer's list
        static string SwitchedOff(string name, string why)
        {
            return name + " is switched off and can't be switched on: it has " + InstallCheck.CantBeFixed + ".\n\n" + why;
        }

        bool Wanted(Item item)
        {
            if (Unusable(item) != null) return false;   // failed its test with a size warning, or a costume that can't be fixed: never switched on here
            bool w;
            return wanted.TryGetValue(item.Key, out w) ? w : IsInstalled(item.Key);
        }

        // An installed catalog item whose catalog package is newer: switched on, Apply updates it.
        bool HasUpdate(Item item) { return installed.Any(r => Installer.Outdated(r, item)); }

        bool Changed(Item item) { return Wanted(item) != IsInstalled(item.Key) || (Wanted(item) && HasUpdate(item)); }

        int Pending() { return AllItems().Count(Changed) + ReplacementChanges().Count + SwapChanges().Count + Renames().Count + EffectChanges().Count; }

        static string EffectOf(Dictionary<string, string> map, string fighter) { string id; return map.TryGetValue(fighter, out id) ? id : null; }

        // The fighters whose effects were changed and not applied yet.
        List<string> EffectChanges() { return effectsOn.Keys.Union(wantedEffects.Keys).Where(f => EffectOf(effectsOn, f) != EffectOf(wantedEffects, f)).ToList(); }

        int Swap(Dictionary<string, int> map, string key) { int s; return map.TryGetValue(key, out s) ? s : 0; }

        // The game costumes whose replacement was changed and not applied yet.
        List<string> SwapChanges() { return swapped.Keys.Union(wantedSwaps.Keys).Where(k => Swap(swapped, k) != Swap(wantedSwaps, k)).ToList(); }

        // A costume's name: the game's (Original, Alternate 1 ...), or a custom one's as on its card.
        string CostumeLabel(string fighter, int costume)
        {
            if (costume < Catalog.FirstCustomCostume) return CostumeGrid.CostumeName(costume);
            Item item = AllItems().FirstOrDefault(i => i.IsCostume && i.Fighter == fighter && i.Slot == costume);
            Installed record = item == null ? null : installed.FirstOrDefault(r => r.Item.Key == item.Key);
            return item == null ? "Slot " + costume : CardTitle(item, record);
        }

        Image CostumePicture(string fighter, int costume)
        {
            Image image;
            if (costume >= Catalog.FirstCustomCostume && pictures.TryGetValue("costume:" + fighter + ":" + costume, out image) && image != null) return image;
            if (costume < Catalog.FirstCustomCostume && (image = CostumeArt.Picture(fighter, costume, 1)) != null) return image;   // the game's own, in color 1
            return portraits.TryGetValue(fighter, out image) ? image : null;
        }

        // Clicking a game costume: choose what plays in its place on this PC (applied with the rest): the Original or a
        // costume the player installed, never one of the game's alternates or DLC costumes (CostumeSwaps.MayStandIn).
        void ChooseCostume(string fighter, int costume)
        {
            var sources = new List<string>();
            if (costume != 1) sources.Add(CostumeSwaps.Key(fighter, 1));
            sources.AddRange(AllItems().Where(i => i.IsCostume && i.Fighter == fighter && IsInstalled(i.Key)).OrderBy(i => i.Slot).Select(i => CostumeSwaps.Key(fighter, i.Slot)));
            if (sources.Count == 0)
            {
                MessageBox.Show(this, "Only a costume you installed can play as " + Fighters.Name(fighter) + "'s Original: install one of " + Fighters.Name(fighter) + "'s first.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string folder = game, target = CostumeSwaps.Key(fighter, costume), name = CostumeLabel(fighter, costume);
            Func<string, int> number = k => int.Parse(k.Substring(k.IndexOf(':') + 1));
            int current = Swap(wantedSwaps, target);
            using (var dialog = new ReplaceDialog(target, current == 0 ? null : CostumeSwaps.Key(fighter, current), sources,
                       k => CostumeLabel(fighter, number(k)), k => CostumePicture(fighter, number(k)),
                       (t, source) => source == null ? null : CostumeSwaps.Problem(folder, fighter, costume, number(source)),
                       "Pick what you'd like to see whenever " + Fighters.Name(fighter) + "'s " + name + " is picked: the Original costume or one you installed " +
                       "(the game's alternates and DLC costumes can't stand in for another). EX More Stuff puts a copy in its place; the game's own files are kept " +
                       "and come back when you choose it as it is again.\n\nOnly you see it, online too: your opponent sees their own " + name + ".", "As it is", "costume"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.Choice == null) wantedSwaps.Remove(target);
                else if (StandInAnyway(AllItems().FirstOrDefault(i => i.IsCostume && i.Fighter == fighter && i.Slot == number(dialog.Choice))))
                    wantedSwaps[target] = number(dialog.Choice);
            }
            ShowCostumes(fighter);
            UpdateStatus();
        }

        // A custom stage or costume flagged for its size (Weight), picked to stand in for one of the game's: asked once
        // more. Yes (the default) goes ahead; nothing is ever stopped by its size alone.
        bool StandInAnyway(Item item)
        {
            Installed record = item == null ? null : installed.FirstOrDefault(r => r.Item.Key == item.Key);
            string warning = record == null ? null : Weight.InGame(game, item, Weight.Name(record));
            return warning == null || MessageBox.Show(this, warning + "\n\nPut it in anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        // Renames of items that stay installed.
        List<KeyValuePair<string, string>> Renames()
        {
            bool use;
            return wantedNames.Where(n => !wanted.TryGetValue(n.Key, out use) || use).ToList();
        }

        // A card's title with the name EMBER shows: a rename waiting for Apply, or the one applied.
        string CardTitle(Item item, Installed record)
        {
            string name;
            if (wantedNames.TryGetValue(item.Key, out name)) return item.TitleNamed(name);
            return record != null && record.Shown != null ? item.TitleNamed(record.Shown) : item.Title;
        }

        // A card's title: the name only, no slot or code (user 2026-10-12: "just hide that"); without a name, the slot or
        // code.
        string CardName(Item item, Installed record)
        {
            string name;
            if (!wantedNames.TryGetValue(item.Key, out name)) name = record != null && record.Shown != null ? record.Shown : item.Name;
            return string.IsNullOrEmpty(name) ? item.TitleNamed(null) : name;
        }


        string Replacement(Dictionary<string, string> map, string stage) { string s; return map.TryGetValue(stage, out s) ? s : null; }

        // The game stages whose replacement was changed and not applied yet.
        List<string> ReplacementChanges()
        {
            return replaced.Keys.Union(wantedReplaced.Keys).Where(s => Replacement(replaced, s) != Replacement(wantedReplaced, s)).ToList();
        }

        // A stage's name: the game's, or a custom stage's from the catalog or its package.
        string StageName(string code)
        {
            if (Array.IndexOf(Stages.Codes, code) >= 0) return Stages.Name(code);
            if (Overrides.IsOverride(code)) { Override o = Overrides.Find(game, code); return o != null && !string.IsNullOrEmpty(o.Name) ? o.Name : "Override stage"; }
            Item item = AllItems().FirstOrDefault(i => i.IsStage && i.Code == code);
            return item != null && !string.IsNullOrEmpty(item.Name) ? item.Name : "Custom stage " + code;
        }

        Image StagePicture(string code)
        {
            Image image;
            if (Overrides.IsOverride(code))   // an override stage: the picture in its zip
            {
                if (!stagePictures.TryGetValue(code, out image))
                {
                    byte[] png = Overrides.Picture(game, code);
                    try { image = png != null ? Pictures.Read(png) : null; } catch (Exception) { image = null; }
                    stagePictures[code] = image;
                }
                return image;
            }
            if (Array.IndexOf(Stages.Codes, code) < 0) return pictures.TryGetValue("stage:" + code, out image) ? image : null;
            if (!stagePictures.TryGetValue(code, out image)) stagePictures[code] = image = Stages.Picture(code);
            return image;
        }

        // Clicking a game stage: choose what plays in its place on this PC (applied with the rest).
        void ChooseReplacement(string stage)
        {
            // override stages made for this stage first (Overrides), then custom stages, then the game's
            var sources = Overrides.Load(game).Where(o => o.Target == stage).Select(o => Overrides.Prefix + o.Id)
                .Concat(AllItems().Where(i => i.IsStage && IsInstalled(i.Key)).OrderBy(i => i.Code).Select(i => i.Code))
                .Concat(StageGrid.Grid.Where(c => c != null && c != stage)).ToList();
            string folder = game;
            using (var dialog = new ReplaceDialog(stage, Replacement(wantedReplaced, stage), sources, StageName, StagePicture,
                       (s, source) => Replacements.Problem(folder, s, source, StageName)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.Choice == null) wantedReplaced.Remove(stage);
                else if (StandInAnyway(AllItems().FirstOrDefault(i => i.IsStage && i.Code == dialog.Choice))) wantedReplaced[stage] = dialog.Choice;
            }
            stagesPage.Invalidate(true);
            UpdateStatus();
        }

        // A fighter's tile bubbles, as currently chosen: switched off (grey), on from GameBanana (green), on and added by the player (blue),
        // costumes only; extra colors switched on (pink; user 2026-10-17: "a pink bubble number ... how many colors you have extra").
        void Count(FighterTile tile, string code)
        {
            tile.Colors = AllItems().Count(i => i.IsColor && i.Fighter == code && Wanted(i));
            var costumes = AllItems().Where(i => !i.IsStage && !i.IsColor && i.Fighter == code).ToList();
            var on = costumes.Where(Wanted).ToList();
            tile.Active = on.Count(i => (i.Id ?? "").StartsWith("gamebanana:"));   // green: from GameBanana
            tile.Own = on.Count - tile.Active;                                       // blue: added by the player
            tile.Inactive = costumes.Count - on.Count;                               // grey: switched off
            tile.Invalidate();
        }

        // Reload after one package changed (deleted, moved): the other changes waiting for Apply stay waiting.
        void ReloadKeeping(params string[] gone)
        {
            gone = gone ?? new string[0];   // ReloadKeeping(null): nothing gone
            var keepEffects = wantedEffects.ToList();
            var keepWanted = wanted.Where(w => !gone.Contains(w.Key)).ToList();
            var keepNames = wantedNames.Where(w => !gone.Contains(w.Key)).ToList();
            var keepReplaced = wantedReplaced.ToList();
            var keepSwaps = wantedSwaps.ToList();
            Reload();
            foreach (var w in keepWanted) wanted[w.Key] = w.Value;
            foreach (var n in keepNames) wantedNames[n.Key] = n.Value;
            // a replacement waiting for Apply whose stage is gone (deleted) is dropped
            var stagesLeft = new HashSet<string>(Stages.Codes.Concat(installed.Concat(removed).Where(r => r.Item.IsStage).Select(r => r.Item.Code))
                                                 .Concat(Overrides.Load(game).Select(o => Overrides.Prefix + o.Id)));
            // (nor one locked after failing its performance test)
            Func<Installed, bool> usable = r => Unusable(r.Item) == null;
            foreach (var r in keepReplaced)
                if (stagesLeft.Contains(r.Value) && !installed.Concat(removed).Any(x => x.Item.IsStage && x.Item.Code == r.Value && !usable(x))) wantedReplaced[r.Key] = r.Value;
            // costume replacements as chosen, but not by a costume that's gone or locked
            wantedSwaps.Clear();
            foreach (var s in keepSwaps)
            {
                string f; int c;
                if (CostumeSwaps.ParseKey(s.Key, out f, out c) && (s.Value == 1 || installed.Concat(removed).Any(r => r.Item.IsCostume && r.Item.Fighter == f && r.Item.Slot == s.Value && usable(r))))
                    wantedSwaps[s.Key] = s.Value;
            }
            // effects as chosen, the ones still there
            var effectIds = new HashSet<string>(Game.IsGameFolder(game) ? Effects.Load(game).Select(e => e.Id) : Enumerable.Empty<string>());
            wantedEffects.Clear();
            foreach (var e in keepEffects) if (e.Value == null || effectIds.Contains(e.Value)) wantedEffects[e.Key] = e.Value;
            BuildRoster();
            if (openFighter != null) ShowCostumes(openFighter);
            BuildStages();
            UpdateStatus();
        }

        void Reload()
        {
            installed = Game.IsGameFolder(game) ? Installer.Load(game) : new List<Installed>();
            removed = Game.IsGameFolder(game) ? Installer.Removed(game) : new List<Installed>();
            // the player's own packages' pictures read again (a reinstall brings a new one: user 2026-10-13, Seth X Ogre's
            // card kept its old picture until EX More Stuff was opened again)
            foreach (Installed r in installed.Concat(removed)) if (r.Source == "file") pictures.Remove(r.Item.Key);
            wanted.Clear();
            wantedNames.Clear();
            replaced = Game.IsGameFolder(game) ? Replacements.Load(game).ToDictionary(r => r.Stage, r => r.Source) : new Dictionary<string, string>();
            wantedReplaced.Clear();
            foreach (var r in replaced) wantedReplaced[r.Key] = r.Value;
            swapped = Game.IsGameFolder(game) ? CostumeSwaps.Load(game).ToDictionary(s => s.Key, s => s.Source) : new Dictionary<string, int>();
            wantedSwaps.Clear();
            foreach (var s in swapped) wantedSwaps[s.Key] = s.Value;
            effectsOn = Game.IsGameFolder(game) ? Effects.On(game) : new Dictionary<string, string>();
            wantedEffects.Clear();
            foreach (var e in effectsOn) wantedEffects[e.Key] = e.Value;
            BuildRoster();
            if (openFighter != null) ShowCostumes(openFighter);
            BuildStages();
            ShowLockedButton();
            packagesPage.SetGame(game);
            communityPage.SetGame(game);
            storagePage.SetGame(game);
            oldModsPage.SetGame(game);
            musicTabPage.SetGame(game);
            UpdateStatus();
        }

        // The fighters in alphabetical order, as their character-select portraits.
        void BuildRoster()
        {
            // the 44 tiles are made once; a reload updates their counts (and portraits, after the game folder changed)
            if (tiles.Count == Fighters.DisplayOrder.Length)
            {
                foreach (var t in tiles)
                {
                    Count(t.Value, t.Key);
                    if (!portraits.ContainsKey(t.Key)) { var reload = LoadPortrait(t.Key, t.Value); }
                }
                return;
            }
            rosterPage.SuspendLayout();
            rosterPage.Controls.Clear();
            tiles.Clear();
            foreach (int id in Fighters.DisplayOrder)
            {
                string code = Fighters.Codes[id];
                var tile = new FighterTile { FighterName = Fighters.Names[id] };
                Count(tile, code);
                tiles[code] = tile;
                tile.Click += (s, e) => ShowCostumes(code);
                rosterPage.Controls.Add(tile);
                var ignored = LoadPortrait(code, tile);
            }
            LayOutRoster();
            rosterPage.ResumeLayout();
        }

        void LayOutRoster()
        {
            Rectangle area = rosterPage.ClientRectangle;
            int width = area.Width / RosterColumns, height = area.Height / RosterRows;
            for (int i = 0; i < rosterPage.Controls.Count; i++)
                rosterPage.Controls[i].Bounds = new Rectangle(i % RosterColumns * width + 3, i / RosterColumns * height + 3, width - 6, height - 6);
        }

        async Task LoadPortrait(string code, FighterTile tile)
        {
            Image image;
            if (!portraits.TryGetValue(code, out image))
            {
                string folder = game;
                try { image = await Task.Run(() => (Image)GameArt.FighterPortrait(folder, code, 300)); }
                catch (Exception) { image = null; }
                if (image != null) portraits[code] = image;
            }
            tile.Portrait = image;
            tile.Invalidate();
        }

        void ShowRoster()
        {
            openFighter = null;
            costumePage.Visible = false;
            rosterPage.Visible = true;
            ShowFolder();
        }

        CostumeGrid costumeGrid;   // the open fighter's game costumes (their colors and extra colors), or null
        readonly Dictionary<string, ItemCard> colorCards = new Dictionary<string, ItemCard>();   // its extra colors' cards, by key
        readonly Dictionary<string, Action> packRefresh = new Dictionary<string, Action>();     // a color's pack card: its switch shown again

        void ShowCostumes(string code)
        {
            costumeGrid = null;
            openFighter = code;
            costumeTitle.Text = Fighters.Name(code);
            ShowFolder();
            var cards = AllItems().Where(i => !i.IsStage && !i.IsColor && i.Fighter == code).OrderBy(i => i.Slot).Select(Card).ToList();
            int width = ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 16;
            if (cards.Count == 0) cards.Add(ShadowLabel.Note("No custom costumes for " + Fighters.Name(code) + " yet.", width));
            // the game's costumes, to have one play as the Original or one of the player's (only on this PC)
            var gameCostumes = Game.IsGameFolder(game) ? CostumeSwaps.GameCostumes(game, code) : new List<int>();
            if (gameCostumes.Count > 0)
            {
                cards.Add(new Heading("The game's costumes", width));
                cards.Add(ShadowLabel.Note("Click one to have it play as the Original costume or one you installed. Only you see it, online too. " +
                                           "The game's alternates and DLC costumes can't stand in for another.", width));
                Image portrait;
                portraits.TryGetValue(code, out portrait);
                var grid = new CostumeGrid(width) { Costumes = gameCostumes, Fighter = code, Portrait = portrait };
                costumeGrid = grid;
                // each costume's extra colors under its colors, a switch over each (the same as its card's Use switch)
                grid.Extras = n => AllItems().Where(i => i.IsColor && i.Fighter == code && i.Slot == n).OrderBy(i => i.Color).Select(i => new CostumeGrid.Extra
                {
                    Number = i.Color,
                    Picture = () => { Image p; return pictures.TryGetValue(i.Key, out p) ? p : null; },
                    On = () => Wanted(i),
                    Switch = on =>
                    {
                        ItemCard card;
                        Action refresh;
                        if (colorCards.TryGetValue(i.Key, out card)) card.Use.Checked = on;   // its card's switch does the rest
                        else
                        {
                            wanted[i.Key] = on;
                            if (packRefresh.TryGetValue(i.Key, out refresh)) refresh();   // its pack's card: on while any of its colors is
                            FighterTile tile; if (tiles.TryGetValue(code, out tile)) Count(tile, code);
                            UpdateStatus();
                        }
                    },
                }).ToList();
                grid.PlaysAs = n => { int s = Swap(wantedSwaps, CostumeSwaps.Key(code, n)); return s == 0 ? null : CostumeLabel(code, s); };
                grid.Clicked += n => ChooseCostume(code, n);
                grid.Lay();
                cards.Add(grid);
            }
            // the extra colors (color packs) under the game's costumes, by the costume they're for ("Original: extra colors"),
            // a card per pack: its colors of that costume under one name (a zip each; user 2026-10-18: "this is supposed to be
            // one card"); each color is still switched by itself in the strip above (user 2026-10-17: "i can disable any number
            // of that packs colors"), the card's switch all of them
            colorCards.Clear();
            packRefresh.Clear();
            foreach (var costume in AllItems().Where(i => i.IsColor && i.Fighter == code).GroupBy(i => i.Slot).OrderBy(g => g.Key))
            {
                cards.Add(new Heading(CostumeLabel(code, costume.Key) + ": extra colors", width));
                foreach (var pack in costume.GroupBy(i => CardName(i, installed.FirstOrDefault(r => r.Item.Key == i.Key) ?? RemovedRecord(i.Key)))
                                            .Select(g => g.OrderBy(i => i.Color).ToList()).OrderBy(g => g[0].Color))
                {
                    if (pack.Count > 1) { cards.Add(PackCard(pack)); continue; }
                    Control card = Card(pack[0]);
                    if (card is ItemCard) colorCards[pack[0].Key] = (ItemCard)card;
                    cards.Add(card);
                }
            }
            // the fighter's effects (user 2026-10-18): one on at a time, the same in every costume
            effectCards.Clear();
            var fx = Game.IsGameFolder(game) ? Effects.Load(game).Where(e => e.Fighter == code).OrderBy(e => e.Name).ToList() : new List<Effect>();
            if (fx.Count > 0)
            {
                cards.Add(new Heading("Effects", width));
                cards.Add(ShadowLabel.Note("Changes " + Fighters.Name(code) + "'s effects in every costume. Only you see them, online too. One at a time.", width));
                foreach (Effect e in fx) cards.Add(EffectCard(e));
            }
            Fill(costumeCards, cards);
            rosterPage.Visible = false;
            costumePage.Visible = costumesTab.Selected;
        }

        void BuildStages()
        {
            int width = ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 16;
            var cards = new List<Control> { new Heading("Custom stages", width) };
            var custom = AllItems().Where(i => i.IsStage).OrderBy(i => i.Code).ToList();
            if (custom.Count == 0) cards.Add(ShadowLabel.Note("No custom stages yet.", width));
            cards.AddRange(custom.Select(Card));
            cards.Add(new Heading("The game's stages", width));
            cards.Add(ShadowLabel.Note("Click a stage to replace it with another one. Only you see it, online too, and its music stays the same.", width));
            var grid = new StageGrid(width) { Margin = new Padding(6, 0, 6, 16) };
            grid.ReplacedBy = code => { string source = Replacement(wantedReplaced, code); return source == null ? null : StageName(source); };
            grid.StageClicked += ChooseReplacement;
            cards.Add(grid);
            Fill(stagesPage, cards);
        }

        // Advanced: a heading, a word on what it's for, a card per tool (built like the other pages, once the window is up).
        void BuildAdvanced()
        {
            int width = ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 16;
            var music = new ToolCard
            {
                Symbol = "♫", Title = "Music",
                Detail = "Put your own songs on any stage's or fighter's theme: set where they loop, match the game's volume, and give the game's music back any time.",
                Margin = new Padding(6),
            };
            music.Click += (s, e) => { openTool = "music"; ShowTab(advancedTab); };
            var codes = new ToolCard
            {
                Symbol = "⇄", Title = "Package codes",
                Detail = "Change the stage code, costume slot or color number of your own packages (the zip is updated, ready to share), or delete them.",
                Margin = new Padding(6),
            };
            codes.Click += (s, e) => { openTool = "packages"; ShowTab(advancedTab); };
            var note = ShadowLabel.Note("Tools for going further. Nothing here is needed to play custom costumes and stages.", width);
            var space = new ToolCard
            {
                Symbol = "▤", Title = "Disk space",
                Detail = "How much room your costumes, stages and songs take altogether and each, biggest first.",
                Margin = new Padding(6),
            };
            space.Click += (s, e) => { openTool = "storage"; storagePage.SetGame(game); ShowTab(advancedTab); };
            var codesFree = new ToolCard
            {
                Symbol = "✓", Title = "Free codes",
                Detail = "Making a costume or stage to share on GameBanana? See which slots and stage codes are free first.",
                Margin = new Padding(6),
            };
            codesFree.Click += (s, e) => { openTool = "freecodes"; ShowTab(advancedTab); };
            var fromFile = new ToolCard
            {
                Symbol = "＋", Title = "Add Mod From File",
                Detail = "Put in a mod you have as a file (.zip, .rar or .7z): a package made for EX More Stuff, or a skin or stage downloaded from anywhere.",
                Margin = new Padding(6),
            };
            fromFile.Click += async (s, e) => await AddFromFile();
            var oldMods = new ToolCard
            {
                Symbol = "⤓", Title = "Import Your Legacy Mods",   // user 2026-10-14 (was "Old mods")
                Detail = "Mods that you previously put in the game's folders: let EX More Stuff manage them?",
                Margin = new Padding(6),
            };
            oldMods.Click += (s, e) => { openTool = "oldmods"; oldModsPage.SetGame(game); ShowTab(advancedTab); };
            var creation = new ToolCard
            {
                Symbol = "✎", Title = "Costume creation",
                Detail = "Costume check: what makes a costume desync online or draw white, fixed into a copy. And pictures of each color for rollback games' character select.",   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                Margin = new Padding(6),
            };
            creation.Click += (s, e) => { openTool = "creation"; creationPage.SetGame(game); ShowTab(advancedTab); };
            var guide = new ToolCard
            {
                Symbol = "↑", Title = "Uploading Guide",
                Detail = "A mod you made, ready to share: dock it, give it a free code and its color pictures.",
                Margin = new Padding(6),
            };
            guide.Click += (s, e) => { openTool = "guide"; ShowTab(advancedTab); };
            // files docked for edits (Docked): first, out of the game while they're made ready to share
            var docked = new List<Control>();
            List<Installed> dockedFiles = Docked.Load();
            if (dockedFiles.Count > 0)
            {
                docked.Add(new Heading("Docked for edits", width));
                docked.Add(ShadowLabel.Note("Out of the game while you get them ready to share; the edits go into the files themselves. Uploading Guide (below) says how.", width));
                foreach (var zip in dockedFiles.GroupBy(f => f.Package, StringComparer.OrdinalIgnoreCase)) docked.Add(DockedCard(zip.ToList(), width));   // a color pack's colors: one card
                docked.Add(new Heading("Tools", width));
            }
            // Performance warnings (Weight): where the size warnings start. Only warnings: nothing is ever stopped by them (user).
            var warnNote = ShadowLabel.Note("Stages and costumes much bigger than the game's own get a \"!\": they may not run well, but they still go in. " +
                                            "Tests on this PC set these lines. One with a \"!\" that fails its test is switched off " +
                                            "and can't be switched on here.", width);
            var pcNote = ShadowLabel.Note(PcText(), width);
            // each line shown with where it came from (the default, or your tests); not changed here (user 2026-10-14: "just make
            // it unchangeable")
            Func<bool, string> after = stage => (stage ? "times the biggest SF4 stage" : "times the game's biggest costume: its bones, model or a color") +
                                                " (" + Weight.Source(stage) + ")";
            var stageLine = new LineSetting("Warn about stages more than", after(true), Weight.StageLine, Weight.Lowest, Weight.Highest, width);
            var costumeLine = new LineSetting("Warn about costumes more than", after(false), Weight.CostumeLine, Weight.Lowest, Weight.Highest, width);
            stageLine.MakeFixed();
            costumeLine.MakeFixed();
            stageLine.AlignWith(costumeLine);
            // the tool cards two to a row, each half the page (user: "a lot of empty space on the right")
            foreach (ToolCard card in new[] { fromFile, oldMods, music, codes, space, codesFree, creation, guide }) card.Width = width / 2 - card.Margin.Horizontal - 1;
            var page = new List<Control> { new Heading("Advanced", width), note };
            page.AddRange(docked);
            page.AddRange(new Control[] { fromFile, oldMods, music, codes, space, codesFree, creation, guide,
                                          new Heading("Performance warnings", width), warnNote, pcNote, stageLine, costumeLine });
            Fill(advancedPage, page);
        }

        // A warning line changed: the cards' "!" marks follow, once the number stops moving.
        readonly Timer warningsChanged = new Timer { Interval = 500 };

        void WarningsChanged()
        {
            warningsChanged.Stop();
            warningsChanged.Tick -= RemarkCards;
            warningsChanged.Tick += RemarkCards;
            warningsChanged.Start();
        }

        void RemarkCards(object sender, EventArgs e)
        {
            warningsChanged.Stop();
            BuildStages();
            if (openFighter != null) ShowCostumes(openFighter);
            communityPage.SetGame(game);
        }

        // Cards (and Advanced's tool cards) flow left to right; a heading, a note or the stage grid has a line to itself.
        static void Fill(FlowLayoutPanel list, List<Control> cards)
        {
            list.SuspendLayout();
            list.Controls.Clear();
            foreach (Control card in cards) list.Controls.Add(card);
            for (int i = 0; i < list.Controls.Count; i++)
                if (!(list.Controls[i] is ItemCard) && !(list.Controls[i] is ToolCard))
                {
                    if (i > 0) list.SetFlowBreak(list.Controls[i - 1], true);
                    list.SetFlowBreak(list.Controls[i], true);
                }
            list.ResumeLayout();
        }

        Control Card(Item item)
        {
            // (a GameBanana mod's Version is which of its files it was built from, for Browse Mods' Update: not shown)
            string version = (item.Id ?? "").StartsWith("gamebanana:") ? null : item.Version;
            string by = string.Join("  ·  ", new[] { item.Author, version }.Where(s => !string.IsNullOrEmpty(s)));
            Installed record = installed.FirstOrDefault(r => r.Item.Key == item.Key), off = record == null ? RemovedRecord(item.Key) : null;
            // a GameBanana mod says so (it's built into a package on this PC, but it isn't the player's own: user 2026-10-12)
            if ((item.Id ?? "").StartsWith("gamebanana:")) by = "GameBanana" + (by.Length > 0 ? "  ·  " + by : "");
            else if ((record ?? off) != null ? (record ?? off).Source == "file" : item.Personal) by = "Your own package" + (by.Length > 0 ? "  ·  " + by : "");
            var card = new ItemCard
            {
                Title = CardName(item, record ?? off),
                Detail = by,
                Note = item.IsStage ? "Others without it see " + Stages.Name(Stages.FallbackCode(item.Code)) : item.IsColor ? "Others without it see color 1" : null,
                Badge = HasUpdate(item) ? (string.IsNullOrEmpty(item.Version) ? "Update" : "Update to " + item.Version) : null,
                Margin = new Padding(6),
            };
            card.Use.Checked = Wanted(item);
            // a GameBanana mod: its creator in gold, opening its page, as on Browse Mods (user 2026-10-12)
            Match page = Regex.Match(item.Id ?? "", @"^gamebanana:(-?\d+):");
            if (page.Success)
            {
                // its creator a favorite (or not) with the heart, as on Browse Mods: by their member number once GameBanana's
                // list is in, by name before
                GameBanana.Mod gb = GameBanana.Known(long.Parse(page.Groups[1].Value)) ?? new GameBanana.Mod { Author = item.Author };
                long id = long.Parse(page.Groups[1].Value);
                GameBanana.Support support;
                GameBanana.Supports.TryGetValue(gb.AuthorId, out support);   // their Ko-fi / Patreon, once the list is in
                card.ShowCreator(item.Author, new GameBanana.Mod { Id = id }.Page, Creators.IsFavorite(gb), support, tips);
                // the gold name: that creator's mods on Browse Mods (user 2026-10-12); the blue icon: this mod's page
                card.CreatorClicked += (s, e) => { ShowTab(communityTab); communityPage.ShowCreatorOf(id, item.Author); };
                tips.SetToolTip(card.Page, "Open its page on GameBanana");
                card.Heart.Click += (s, e) =>
                {
                    bool on = Creators.Toggle(gb);
                    card.Heart.On = on;
                    Toast.ShowAbove(card.Heart, card.Heart.ClientRectangle, on ? "Favorited this creator" : "Removed from favorites", this);
                    communityPage.CreatorChanged(gb, on);
                };
            }
            string locked = PerfTest.Locked(item), pulledWhy = Catalog.PulledWhy(item), broken = item.IsCostume ? InstallCheck.Warning(item.Key) : null;
            if (broken != null)
            {
                // the costume check found problems that can't be fixed (user 2026-10-27: "disable them. not allow users to use
                // them anyway"): off, and it can't be switched on; a version that's fine or fixable unlocks it
                card.Use.Enabled = false;
                card.Warning.SetMessage(SwitchedOff(Weight.Name(record ?? off ?? new Installed { Item = item }), broken));
            }
            else if (pulledWhy != null)
            {
                // pulled for now (the catalog's list, user 2026-10-22): off, and it can't be switched on until it's off the list
                card.Use.Enabled = false;
                card.Warning.SetMessage(Weight.Name(record ?? off ?? new Installed { Item = item }) + " is switched off for now: " + pulledWhy);
            }
            else if (locked != null)
            {
                // it had a size warning and failed its test on this PC: off, and it can't be switched on (user)
                card.Use.Enabled = false;
                card.Warning.SetMessage(Weight.Name(record ?? off ?? new Installed { Item = item }) + " can't be switched on on this PC: " + locked +
                                        ". It could crash the game in a match. A new version of it can be tried again.");
            }
            else if (record != null)
            {
                string warning = Weight.InGame(game, item, Weight.Name(record));   // its size warning, if any
                if (item.IsStage || item.IsCostume)
                {
                    // the "!" or a little green circle: clicking it offers the performance test (user)
                    PerfTest.Result last = PerfTest.Last(item.Key);
                    card.Warning.SetTestable(warning, last != null ? "Last test: " + Short(last) + ". Click to test it again." : "Click to test how it runs on this PC.");
                    card.Warning.Click += (s, e) => TestMenu(item, record, card.Warning);
                }
                else card.Warning.SetMessage(warning);
            }
            card.Use.CheckedChanged += (s, e) =>
            {
                wanted[item.Key] = card.Use.Checked;
                card.Invalidate();
                if (item.IsColor && costumeGrid != null && !costumeGrid.IsDisposed) costumeGrid.Invalidate();   // its switch in the colors strip
                FighterTile tile;
                if (!item.IsStage && tiles.TryGetValue(item.Fighter, out tile)) Count(tile, item.Fighter);
                UpdateStatus();
            };
            if (record != null)
            {
                card.ShowLink("Rename");
                card.Link.LinkClicked += (s, e) => RenameItem(item, card);
            }
            else if (off != null)
            {
                card.ShowLink("Forget");
                card.Link.LinkClicked += (s, e) => ForgetItem(item);
            }
            // a package of the player's (their own, or built from a GameBanana mod): deleted for good from its card
            Installed own = record ?? off;
            if (own != null && own.Source == "file")
            {
                card.ShowTrash();
                card.Trash.Click += async (s, e) => await DeleteItem(item);
                tips.SetToolTip(card.Trash, "Delete it (you choose whether its zip goes too)");
            }
            // a mod from GameBanana: its card on Browse Mods, lit up
            Match mod = Regex.Match(item.Id ?? "", @"^(gamebanana:[^:]+:)");
            if (mod.Success)
            {
                card.ShowBrowse();
                string key = mod.Groups[1].Value;
                card.Browse.LinkClicked += (s, e) => { ShowTab(communityTab); communityPage.Highlight(key); };
            }
            tips.SetToolTip(card, off != null ? "Switch it on to add it again from " + off.Package
                                              : string.IsNullOrEmpty(item.Description) ? item.Title : item.Description);
            var ignored = LoadPicture(item, card);
            // colors the mod didn't bring, pattern textures in their place: the checker under the mark (read in the background)
            string package = own != null ? own.Package : null;
            if (item.IsCostume && !string.IsNullOrEmpty(package) && File.Exists(package))
                Task.Run(() => ColorVariants.PatternColors(package, item.Prefix)).ContinueWith(t =>
                {
                    if (!t.IsFaulted && !card.IsDisposed) card.Incomplete.Show(t.Result);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            return card;
        }

        // A color pack's colors of one costume as one card (user 2026-10-18): its name, how many colors, its switch (all its
        // colors on or off; on while any is), Rename and Delete for all of them, and its colors' pictures side by side.
        Control PackCard(List<Item> colors)
        {
            Item first = colors[0];
            Func<Item, Installed> recordOf = i => installed.FirstOrDefault(r => r.Item.Key == i.Key);
            Installed own = recordOf(first) ?? RemovedRecord(first.Key);
            string by = string.Join("  ·  ", new[] { first.Author, (first.Id ?? "").StartsWith("gamebanana:") ? null : first.Version }.Where(t => !string.IsNullOrEmpty(t)));
            if ((first.Id ?? "").StartsWith("gamebanana:")) by = "GameBanana" + (by.Length > 0 ? "  ·  " + by : "");
            else if (own != null ? own.Source == "file" : first.Personal) by = "Your own package" + (by.Length > 0 ? "  ·  " + by : "");
            var card = new ItemCard
            {
                Title = CardName(first, own),
                Detail = by,
                Note = colors.Count + " colors (" + colors[0].Color + "-" + colors[colors.Count - 1].Color + "). Others without one see color 1",
                Margin = new Padding(6),
            };
            bool showing = false;
            Action shown = () => { showing = true; card.Use.Checked = colors.Any(Wanted); showing = false; card.Invalidate(); };
            shown();
            foreach (Item c in colors) packRefresh[c.Key] = shown;
            card.Use.CheckedChanged += (s, e) =>
            {
                if (showing) return;
                foreach (Item c in colors) wanted[c.Key] = card.Use.Checked;
                card.Invalidate();
                if (costumeGrid != null && !costumeGrid.IsDisposed) costumeGrid.Invalidate();   // their switches in the colors strip
                FighterTile tile;
                if (tiles.TryGetValue(first.Fighter, out tile)) Count(tile, first.Fighter);
                UpdateStatus();
            };
            if (colors.Any(c => recordOf(c) != null))
            {
                card.ShowLink("Rename");
                card.Link.LinkClicked += (s, e) => RenamePack(colors, card);
            }
            if (colors.Select(c => recordOf(c) ?? RemovedRecord(c.Key)).Any(r => r != null && r.Source == "file"))
            {
                card.ShowTrash();
                card.Trash.Click += async (s, e) => await DeletePack(colors);
                tips.SetToolTip(card.Trash, "Delete it (you choose whether its zips go too)");
            }
            tips.SetToolTip(card, string.IsNullOrEmpty(first.Description) ? first.Title : first.Description);
            var ignored = PackPicture(colors, card);
            return card;
        }

        // a pack card's picture: its colors' pictures side by side (the middle of each, up to 6), once they're read. All of
        // them are read: the colors strip above shows each (user 2026-10-27: a pack of 14 showed pictures only up to its 6th)
        async Task PackPicture(List<Item> colors, ItemCard card)
        {
            var found = new List<Image>();
            foreach (Item c in colors)
            {
                Image image = await ReadPicture(c);
                if (image != null && found.Count < 6) found.Add(image);
            }
            if (costumeGrid != null && !costumeGrid.IsDisposed) costumeGrid.Invalidate();
            if (found.Count == 0 || card.IsDisposed) return;
            const int height = 384, slot = 128;
            var strip = new Bitmap(slot * found.Count, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(strip))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                for (int i = 0; i < found.Count; i++)
                {
                    Image p = found[i];
                    int w = p.Width * height / Math.Max(1, p.Height);   // its width at the strip's height
                    g.SetClip(new Rectangle(i * slot, 0, slot, height));
                    g.DrawImage(p, new Rectangle(i * slot + (slot - w) / 2, 0, w, height));
                }
            }
            card.Picture = strip;
            card.Invalidate();
            if (costumeGrid != null && !costumeGrid.IsDisposed) costumeGrid.Invalidate();
        }

        // a pack's name for all its colors
        void RenamePack(List<Item> colors, ItemCard card)
        {
            var records = colors.Select(c => installed.FirstOrDefault(r => r.Item.Key == c.Key)).Where(r => r != null).ToList();
            if (records.Count == 0) return;
            Item first = records[0].Item;
            string current;
            if (!wantedNames.TryGetValue(first.Key, out current)) current = records[0].Shown ?? first.Name ?? "";
            using (var dialog = new NameDialog(Fighters.Name(first.Fighter) + " " + CostumeGrid.CostumeName(first.Slot).ToLowerInvariant() + " colors", current))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string name = Installer.CleanName(dialog.Value);
                foreach (Installed r in records)
                    if (r.Shown != null && name == r.Shown) wantedNames.Remove(r.Item.Key);
                    else wantedNames[r.Item.Key] = name;
            }
            card.Title = CardName(first, records[0]);
            card.Invalidate();
            UpdateStatus();
        }

        // a pack deleted for good: asked once, for all its colors
        async Task DeletePack(List<Item> colors)
        {
            var records = colors.Select(c => installed.FirstOrDefault(r => r.Item.Key == c.Key) ?? RemovedRecord(c.Key)).Where(r => r != null && r.Source == "file").ToList();
            if (records.Count == 0 || !GameClosed()) return;
            string title = CardName(records[0].Item, records[0]) + " (" + records.Count + " colors)";
            var choice = PackagesPanel.AskDelete(this, game, records[0], title);
            if (choice == DeleteDialog.Choice.Cancel) return;
            // a zip goes when only this pack's colors use it (one zip for the pack, or a zip each from before 0.92)
            var keys = new HashSet<string>(records.Select(r => r.Item.Key));
            var others = installed.Concat(removed).Where(r => !keys.Contains(r.Item.Key) && !string.IsNullOrEmpty(r.Package)).Select(r => Path.GetFullPath(r.Package)).ToList();
            var recycle = records.Select((r, i) => !string.IsNullOrEmpty(r.Package) && File.Exists(r.Package) && !r.Theirs &&
                                                   !others.Contains(Path.GetFullPath(r.Package), StringComparer.OrdinalIgnoreCase) &&
                                                   records.FindIndex(x => string.Equals(x.Package, r.Package, StringComparison.OrdinalIgnoreCase)) == i).ToList();
            string g = game;
            var notes = new List<string>();
            PageWorking(true);
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < records.Count; i++)
                    {
                        string note = PackagesPanel.PurgeForGood(g, records[i]);
                        if (note != null) notes.Add(note);
                        if (recycle[i]) PackagesPanel.RecycleZip(records[i].Package);
                    }
                });
            }
            catch (Exception ex) { MessageBox.Show(this, "It wasn't all deleted: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { PageWorking(false); }
            ReloadKeeping(records.Select(r => r.Item.Key).ToArray());
            UpdateStatus(title + " is deleted");
            if (notes.Count > 0) MessageBox.Show(this, string.Join("\n\n", notes.Distinct()), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // An effects mod's card: on means it's its fighter's effects (the others go off); Apply puts it in
        Control EffectCard(Effect effect)
        {
            bool downloaded = (effect.Id ?? "").StartsWith("gamebanana:");
            string by = string.Join("  ·  ", new[] { downloaded ? "GameBanana" : "Your own package", effect.Author }.Where(t => !string.IsNullOrEmpty(t)));
            var card = new ItemCard { Title = effect.Name, Detail = by, Margin = new Padding(6) };
            bool showing = false;
            Action shown = () => { showing = true; card.Use.Checked = EffectOf(wantedEffects, effect.Fighter) == effect.Id; showing = false; card.Invalidate(); };
            shown();
            effectCards.Add(shown);
            card.Use.CheckedChanged += (s, e) =>
            {
                if (showing) return;
                if (card.Use.Checked) wantedEffects[effect.Fighter] = effect.Id;
                else if (EffectOf(wantedEffects, effect.Fighter) == effect.Id) wantedEffects[effect.Fighter] = null;
                foreach (Action a in effectCards) a();
                UpdateStatus();
            };
            card.ShowTrash();
            card.Trash.Click += async (s, e) => await DeleteEffect(effect);
            tips.SetToolTip(card.Trash, "Delete it");
            Task.Run(() => Effects.Picture(effect)).ContinueWith(t =>
            {
                if (t.IsFaulted || t.Result == null || card.IsDisposed) return;
                card.Picture = Pictures.Read(t.Result);
                card.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return card;
        }

        async Task DeleteEffect(Effect effect)
        {
            if (!GameClosed()) return;
            bool downloaded = (effect.Id ?? "").StartsWith("gamebanana:");
            using (var dialog = new DeleteDialog(effect.Name, "It's taken out of the game and out of EX More Stuff.\n\n" +
                                                (downloaded ? "You can download it again from Browse Mods." : "The original mod file you added is untouched.")))
            {
                dialog.ShowDialog(this);
                if (dialog.Picked == DeleteDialog.Choice.Cancel) return;
            }
            string g = game;
            PageWorking(true);
            try { await Task.Run(() => Effects.Uninstall(g, effect.Id)); }
            catch (Exception ex) { MessageBox.Show(this, "It wasn't all deleted: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { PageWorking(false); }
            ReloadKeeping(null);
            UpdateStatus(effect.Name + " is deleted");
        }

        static string Short(PerfTest.Result r)
        {
            if (r.Estimated) return r.FullSpeed ? "60+ fps, full speed" : "about " + r.Average.ToString("0") + " fps, may not keep full speed";   // from the game's speed only
            return r.Average.ToString("0") + " fps, " + (r.FullSpeed ? "full speed" : "may not keep full speed") +
                   (r.Slower >= 5 ? ", " + r.Slower.ToString("0") + "% slower than the game's own" : "");
        }

        // A card's mark clicked: its size warning once more (user: "warn them another time"), the last test, and the test.
        void TestMenu(Item item, Installed record, WarningMark mark)
        {
            ContextMenuStrip menu = DarkMenu.Create();
            if (mark.Message != null) menu.Items.Add(new ToolStripLabel(Wrapped(mark.Message, 64)) { ForeColor = Theme.Warning, Margin = new Padding(6, 6, 6, 4) });
            PerfTest.Result last = PerfTest.Last(item.Key);
            if (last != null) menu.Items.Add(new ToolStripLabel("Last test: " + Short(last)) { ForeColor = Theme.Muted, Margin = new Padding(6, 4, 6, 4) });
            var test = new ToolStripMenuItem(item.IsStage ? "Test how it runs on this PC (Hugo vs Poison on it)"
                                                          : "Test how it runs on this PC (" + Fighters.Name(item.Fighter) + " on both sides)");
            test.Click += async (s, e) => await Test(item, record);
            menu.Items.Add(test);
            if (item.IsCostume && item.Slot >= Catalog.FirstCustomCostume && Game.IsGameFolder(game))
                menu.Items.Add(SoundsLikeMenu(item));
            if (item.IsStage)   // its music heard in the game (user 2026-10-15; the test's benchmark plays none)
            {
                var listen = new ToolStripMenuItem("Listen to stage");
                listen.Click += async (s, e) => await ListenTo(item, record);
                menu.Items.Add(listen);
            }
            menu.Closed += (s, e) => CharacterPicker.BeginDispose(menu);
            menu.Show(mark, new Point(0, mark.Height + 2));
        }

        // Sounds like (user 2026-10-27: "make bison have the mecha bison foley on his raiden costume"): the game costume a
        // custom one plays as in rollback games, so it gets that costume's foley (Installer.SoundsLike). Wording awaits the
        // user's OK.
        ToolStripMenuItem SoundsLikeMenu(Item item)
        {
            int now = Installer.SoundsLike(game, item);
            var sounds = new ToolStripMenuItem("Sounds like");   // ROLLBACK-NAME: only rollback games use it
            for (int n = 0; n <= 3; n++)
            {
                int pick = n;
                var choice = new ToolStripMenuItem(n == 0 ? "As usual" : CostumeLabel(item.Fighter, n)) { Checked = now == n };
                choice.Click += (s, e) =>
                {
                    try { Installer.SetSoundsLike(game, item.Key, pick); }
                    catch (Exception ex) { MessageBox.Show(this, "Couldn't change it: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    UpdateStatus(pick == 0 ? Fighters.Name(item.Fighter) + "'s costume sounds as usual in rollback games"
                                           : "In rollback games it sounds like " + Fighters.Name(item.Fighter) + "'s " + CostumeLabel(item.Fighter, pick));   // ROLLBACK-NAME
                };
                sounds.DropDownItems.Add(choice);
            }
            DarkMenu.Style(sounds.DropDown);
            return sounds;
        }

        // Listen to stage: the stage in the game with its own music for a minute (PerfTest.Listen); the player may close
        // the game sooner. Wording approved by the user.
        async Task ListenTo(Item item, Installed record)
        {
            string blocked = PerfTest.Blocked();
            if (blocked != null) { MessageBox.Show(this, blocked + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string name = Weight.Name(record), g = game, problem = null;
            Busy(true);
            SetStatus("Playing " + name + " in the game with its music for " + PerfTest.ListenSeconds + " seconds...");
            try { await Task.Run(() => PerfTest.Listen(g, item)); }
            catch (Exception ex) { problem = ex.Message; }
            finally { Busy(false); }
            UpdateStatus();
            if (problem != null) MessageBox.Show(this, "Listen to stage didn't finish: " + problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        static string Wrapped(string text, int width)
        {
            var lines = new List<string>();
            string line = "";
            foreach (string word in text.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width) { lines.Add(line); line = ""; }
                line += (line.Length > 0 ? " " : "") + word;
            }
            lines.Add(line);
            return string.Join("\n", lines);
        }

        // The performance test (PerfTest): the game opens with its benchmark for a few seconds, twice the first time
        // (the game's own first, to compare with), and closes by itself; then what it found. One with a size warning
        // that fails (doesn't load, or doesn't keep full speed) leaves the game and can't be switched on again (user).
        async Task Test(Item item, Installed record)
        {
            string blocked = PerfTest.Blocked();
            if (blocked != null) { MessageBox.Show(this, blocked + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string name = Weight.Name(record), g = game, problem = null, didntLoad = null;
            bool flagged = Weight.Lockable(game, item);   // past the lower of its warning lines: failing locks it (user)
            PerfTest.Result result = null;
            Busy(true);
            try { result = await Task.Run(() => PerfTest.Run(g, item, flagged, text => BeginInvoke((Action)(() => SetStatus(text))))); }
            catch (PerfTest.LoadFailed ex) { didntLoad = ex.Message; }
            catch (Exception ex) { problem = ex.Message; }
            finally { Busy(false); }
            BuildAdvanced();   // the warning lines may have moved (tests set them): Advanced and the Browse Mods marks follow
            communityPage.SetGame(game);
            string locked = PerfTest.Locked(item), removing = null;
            if (locked != null)
            {
                try
                {
                    Installer.Remove(game, item.Key);
                    // where it stands in for one of the game's stages or costumes, the game's own come back too
                    if (item.IsStage) foreach (var r in Replacements.Load(game).Where(r => r.Source == item.Code).ToList()) Replacements.Restore(game, r.Stage);
                    else foreach (var s in CostumeSwaps.Load(game).Where(s => s.Fighter == item.Fighter && s.Source == item.Slot).ToList()) CostumeSwaps.Restore(game, s.Fighter, s.Costume);
                }
                catch (Exception ex) { removing = ex.Message; }
                string round = RoundMusicMod();   // its round music may have been the last
                ReloadKeeping(item.Key);
                MessageBox.Show(this, (result != null ? PerfTest.Describe(name, result) + "\n\n" : name + " didn't load in the game's test on this PC: " + didntLoad + ".\n\n") +
                                (removing == null ? "It had a size warning, so it's switched off and can't be switched on on this PC: it could crash the game in a match. A new version of it can be tried again."
                                                  : "It had a size warning and failed, but it couldn't be taken out of the game: " + removing) +
                                (round != null ? "\n\nNote: " + round + "." : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            BuildStages();   // the marks' last test
            if (openFighter != null) ShowCostumes(openFighter);
            if (didntLoad != null) MessageBox.Show(this, name + " didn't load in the game's test on this PC: " + didntLoad + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (problem != null) MessageBox.Show(this, "The test didn't finish: " + problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else MessageBox.Show(this, PerfTest.Describe(name, result), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // The PC check (user: "the first time the user loads anything or applies anything"): Hugo vs Poison on Blast
        // Furnace, the game's heaviest stage, once per PC and graphics settings; custom stages' tests compare with it.
        // Asked once a session (Cancel: asked again next time); it waits while the game or Steam can't run it.
        bool pcAsked;

        async Task EnsurePcCheck()
        {
            if (pcAsked || PerfTest.PcResult() != null || PerfTest.Blocked() != null) return;
            pcAsked = true;
            if (MessageBox.Show(this, "First, a quick check of how this PC runs the game: Hugo vs Poison on Blast Furnace, the game's heaviest stage, in the game's own " +
                    "benchmark. The game opens for about 12 seconds and closes by itself, then EX More Stuff goes on.\n\nCustom stages you test are compared with it.",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            string g = game, problem = null;
            Busy(true);
            try { await Task.Run(() => PerfTest.CheckPc(g, text => BeginInvoke((Action)(() => SetStatus(text))))); }
            catch (Exception ex) { problem = ex.Message; }
            finally { Busy(false); }
            BuildAdvanced();   // its line on where this PC stands, and the stages' warning line it set
            RemarkCards(null, EventArgs.Empty);
            if (problem != null)
                MessageBox.Show(this, "The check didn't finish: " + problem + ". EX More Stuff goes on; it checks again next time.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        static string PcText()
        {
            double? fps = PerfTest.PcResult();
            if (fps == null) return "This PC hasn't been checked yet: EX More Stuff checks it the first time you install or apply something (Hugo vs Poison on Blast Furnace, " +
                                    "the game's heaviest stage, in the game's own benchmark).";
            if (PerfTest.PcEstimated() && fps.Value >= 59)   // from the game's speed only: 60 means 60 or more
                return "This PC runs the game's heaviest stage (Blast Furnace, Hugo vs Poison) at full speed (60 fps or more) in the game's own benchmark.";
            return "This PC runs the game's heaviest stage (Blast Furnace, Hugo vs Poison) at " + fps.Value.ToString("0") + " fps in the game's own benchmark" +
                   (fps.Value >= 59 ? ", so at full speed." : ", below full speed (60 fps).") + " Custom stages you test are compared with it.";
        }

        // The trash can on a card: the package deleted for good (as in Package codes), whatever else is waiting for Apply.
        // Browse Mods' Uninstall (user 2026-10-16: "each card should have an uninstall button in the browser too"):
        // everything a GameBanana mod put in comes out as the trash can's Delete does (its costumes and stages, an override,
        // a song mod), EX More Stuff's copies to the Recycle Bin. True when it went.
        async Task<bool> UninstallMod(string key, string name)
        {
            var records = installed.Concat(removed).Where(r => (r.Item.Id ?? "").StartsWith(key, StringComparison.OrdinalIgnoreCase)).ToList();
            var overrides = Overrides.Load(game).Where(o => (o.Id ?? "").StartsWith(key, StringComparison.OrdinalIgnoreCase)).ToList();
            var effects = Effects.Load(game).Where(e => (e.Id ?? "").StartsWith(key, StringComparison.OrdinalIgnoreCase)).ToList();
            bool song = SongMods.Load().Any(s => s.Key == key);
            if ((records.Count == 0 && overrides.Count == 0 && effects.Count == 0 && !song) || !GameClosed()) return false;
            string what = records.Count == 1 && overrides.Count == 0 && !song ? PackagesPanel.DeleteQuestion(game, records[0])
                        : song && records.Count == 0 && overrides.Count == 0
                            ? "It's taken out of EX More Stuff. A stage or fighter playing it now keeps it until you pick another song.\n\nYou can download it again from Browse Mods."
                            : "It's taken out of the game and out of EX More Stuff.\n\nYou can download it again from Browse Mods.";
            using (var dialog = new DeleteDialog(name, what, "Uninstall"))
            {
                dialog.ShowDialog(this);
                if (dialog.Picked == DeleteDialog.Choice.Cancel) return false;
            }
            string g = game;
            var notes = new List<string>();
            PageWorking(true);
            try
            {
                foreach (Installed record in records)
                {
                    bool recycle = PackagesPanel.RecyclesZip(g, record);
                    string note = await Task.Run(() => PackagesPanel.PurgeForGood(g, record));
                    if (recycle) PackagesPanel.RecycleZip(record.Package);
                    if (note != null) notes.Add(note);
                    wanted.Remove(record.Item.Key);
                }
                foreach (Override o in overrides) await Task.Run(() => Overrides.Uninstall(g, o.Id));
                foreach (Effect fx in effects) await Task.Run(() => Effects.Uninstall(g, fx.Id));
                if (song) SongMods.Remove(key);
            }
            catch (Exception ex) { notes.Add("It wasn't all uninstalled: " + ex.Message); }
            finally { PageWorking(false); }
            ReloadKeeping(null);
            UpdateStatus(name + " is uninstalled");
            if (notes.Count > 0) MessageBox.Show(this, string.Join("\n", notes), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        async Task DeleteItem(Item item)
        {
            Installed record = installed.FirstOrDefault(r => r.Item.Key == item.Key) ?? RemovedRecord(item.Key);
            if (record == null || !GameClosed()) return;
            var choice = PackagesPanel.AskDelete(this, game, record, Describe(item));
            if (choice == DeleteDialog.Choice.Cancel) return;
            bool recycle = PackagesPanel.RecyclesZip(game, record);
            string g = game, note = null;
            PageWorking(true);
            try
            {
                note = await Task.Run(() => PackagesPanel.PurgeForGood(g, record));
                if (recycle) PackagesPanel.RecycleZip(record.Package);
            }
            catch (Exception ex) { MessageBox.Show(this, "It wasn't all deleted: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { PageWorking(false); }
            ReloadKeeping(item.Key);
            UpdateStatus(Describe(item) + " is deleted");
            if (note != null) MessageBox.Show(this, note, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // A removed package of the player's off the list (its zip stays where it is).
        void ForgetItem(Item item)
        {
            if (MessageBox.Show(this, "Take " + Describe(item) + " off the list? Its zip stays where it is; Add from file brings it back.", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            try { Installer.Forget(game, item.Key); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "It wasn't taken off: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            removed = Installer.Removed(game);
            wanted.Remove(item.Key);
            FighterTile tile;
            if (!item.IsStage && tiles.TryGetValue(item.Fighter, out tile)) Count(tile, item.Fighter);
            if (openFighter != null) ShowCostumes(openFighter);
            BuildStages();
            UpdateStatus();
        }

        // An installed item's name in EMBER's menus (a small text file beside it), saved by Apply like any change.
        void RenameItem(Item item, ItemCard card)
        {
            Installed record = installed.FirstOrDefault(r => r.Item.Key == item.Key);
            if (record == null) return;
            string what = item.IsStage ? "stage " + item.Code : Fighters.Name(item.Fighter) + " " + item.TitleNamed(null).ToLowerInvariant();
            string current;
            if (!wantedNames.TryGetValue(item.Key, out current)) current = record.Shown ?? item.Name ?? "";
            using (var dialog = new NameDialog(what, current))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string name = Installer.CleanName(dialog.Value);
                if (record.Shown != null && name == record.Shown) wantedNames.Remove(item.Key);
                else wantedNames[item.Key] = name;
            }
            card.Title = CardName(item, record);
            card.Invalidate();
            UpdateStatus();
        }

        async Task LoadPicture(Item item, ItemCard card)
        {
            Image image = await ReadPicture(item);
            card.Picture = image;
            card.Invalidate();
            if (item.IsColor && costumeGrid != null && !costumeGrid.IsDisposed) costumeGrid.Invalidate();   // the colors strip shows it too
        }

        // an item's picture: kept once read (pictures), from the catalog, its installed files or its zip; null when none
        async Task<Image> ReadPicture(Item item)
        {
            Image image;
            if (!pictures.TryGetValue(item.Key, out image))
            {
                try
                {
                    byte[] catalogBytes = null;
                    if (!string.IsNullOrEmpty(item.Picture)) catalogBytes = await Catalog.ReadBytes(item.Picture);
                    // A player's own package: its picture is among the installed files, or in its zip (read in the
                    // background: a big zip read cold would stall the window)
                    string folder = Game.FolderFor(game, item);
                    Installed own = installed.FirstOrDefault(r => r.Item.Key == item.Key) ?? RemovedRecord(item.Key);
                    string package = own != null ? own.Package : null;
                    image = await Task.Run(() =>
                    {
                        byte[] bytes = catalogBytes;
                        if (bytes == null && string.IsNullOrEmpty(item.Picture))
                        {
                            foreach (string name in Installer.PictureNames(item))
                                if (File.Exists(Path.Combine(folder, name))) { bytes = File.ReadAllBytes(Path.Combine(folder, name)); break; }
                            if (bytes == null && package != null) bytes = Installer.PackagePicture(package, item);
                        }
                        return bytes != null ? Pictures.Read(bytes) : null;
                    });
                    if (image != null) pictures[item.Key] = image;
                }
                catch (Exception) { }
            }
            return image;
        }

        // Whenever nothing is running, the status line also says the program can simply be closed.
        const string Finished = "Finished? You can close EX More Stuff and play.";

        void UpdateStatus(string done = null)
        {
            ShowEverythingButton();
            int pending = Pending();
            apply.Enabled = pending > 0 && !locked;   // not while something works
            string count = installed.Count + " installed" + (replaced.Count > 0 ? ", " + replaced.Count + " replaced" : "");
            string text = pending > 0
                ? count + "  ·  " + pending + " change" + (pending > 1 ? "s" : "") + " to apply, then you can close EX More Stuff"
                : (done != null ? done + "  ·  " : "") + count + "  ·  " + Finished;
            if (catalogProblem != null) text += "  ·  Catalog unavailable: " + catalogProblem;
            SetStatus(text);
        }

        void SetStatus(string text)
        {
            status.Text = text;
            tips.SetToolTip(status, text);
        }

        // While anything works (Apply, an install, a move, a song), nothing else can be started: the tabs, the way back
        // from an Advanced tool, the footer's buttons and the game folder's link wait, and the page doing the work keeps
        // its own controls off. When it's done they come back after a short pause, so a double click can't land on the
        // next thing.
        readonly List<FlatButton> backButtons = new List<FlatButton>();
        readonly Timer unlock = new Timer { Interval = 400 };

        bool locked;

        // the footer's buttons from the right: Warnings (the locked costumes), Apply changes, Disable everything,
        // Refresh, the game folder; the status and the game folder's line take what's left
        void LayOutFooter()
        {
            int right = ClientSize.Width - 18;
            if (lockedCostumes.Visible)
            {
                lockedCostumes.Size = new Size(TextRenderer.MeasureText(lockedCostumes.Text, lockedCostumes.Font).Width + 24, 32);
                lockedCostumes.Location = new Point(right - lockedCostumes.Width, 19);
                right = lockedCostumes.Left - 8;
            }
            apply.Location = new Point(right - apply.Width, 19);
            disableAll.Location = new Point(apply.Left - disableAll.Width - 8, 19);
            refresh.Location = new Point(disableAll.Left - refresh.Width - 8, 19);
            folderButton.Location = new Point(refresh.Left - folderButton.Width - 6, 12);
            status.Bounds = new Rectangle(18, 14, folderButton.Left - 30, 20);
            gameLabelRoom = folderButton.Left - 94;
            gameLabel.Bounds = new Rectangle(18, 36, gameLabelRoom, 18);
        }

        // Every mod of this PC that's switched off and can't be switched on, with why, as its card's "!" says it (user
        // 2026-10-27: Samus, pulled, wasn't in the list): a costume the check found problems in that can't be fixed, a mod
        // pulled for now (Catalog.Pulled: only one that's here, installed or switched off), one that failed its test
        List<string> LockedMods()
        {
            var said = new List<Tuple<string, string>>();
            if (!Game.IsGameFolder(game)) return new List<string>();
            var here = new HashSet<string>(installed.Concat(removed).Select(r => r.Item.Key));
            foreach (Item item in AllItems().GroupBy(i => i.Key).Select(g => g.First()))
            {
                Installed record = installed.Concat(removed).FirstOrDefault(r => r.Item.Key == item.Key);
                string name = Weight.Name(record ?? new Installed { Item = item });
                string broken = item.IsCostume ? InstallCheck.Warning(item.Key) : null, pulledWhy = here.Contains(item.Key) ? Catalog.PulledWhy(item) : null, failed = PerfTest.Locked(item);
                if (broken != null) said.Add(Tuple.Create(name, SwitchedOff(name, broken)));
                else if (pulledWhy != null) said.Add(Tuple.Create(name, name + " is switched off for now: " + pulledWhy));
                else if (failed != null) said.Add(Tuple.Create(name, name + " can't be switched on on this PC: " + failed + ". It could crash the game in a match. A new version of it can be tried again."));
            }
            return said.OrderBy(s => s.Item1, StringComparer.OrdinalIgnoreCase).Select(s => s.Item2).ToList();
        }

        // always there (user 2026-10-27: "have that button always appear. Warnings (0) if i have none")
        void ShowLockedButton()
        {
            int count = LockedMods().Count;
            string text = "⚠ Warnings (" + count + ")";
            if (lockedCostumes.Visible && lockedCostumes.Text == text) return;
            lockedCostumes.Text = text;
            lockedCostumes.Visible = true;
            tips.SetToolTip(lockedCostumes, count == 0 ? "Nothing is switched off." :
                count + (count == 1 ? " mod is" : " mods are") + " switched off and can't be switched on. Click to see why.");
            LayOutFooter();
            FitGameLabel();
        }

        void ShowLockedCostumes()
        {
            var locked = LockedMods();
            ShowLockedButton();
            string text = locked.Count == 0 ? "Nothing is switched off." : string.Join("\n\n\n", locked);
            using (var window = new TextWindow("Warnings", "These mods are switched off and can't be switched on, each for the reason below.", text))
                window.ShowDialog(this);
        }

        void Lock(bool on)
        {
            locked = on;
            costumesTab.Enabled = stagesTab.Enabled = musicTab.Enabled = communityTab.Enabled = advancedTab.Enabled = aboutTab.Enabled = change.Enabled = !on;
            foreach (FlatButton back in backButtons) back.Enabled = !on;
            disableAll.Enabled = refresh.Enabled = !on;
            apply.Enabled = !on && Pending() > 0;
            UseWaitCursor = on;
            if (!on) UpdateStatus();
        }

        // a page's own work (Browse Mods, Package codes, Music): the rest of the window waits
        void PageWorking(bool on)
        {
            unlock.Stop();
            if (on) Lock(true); else unlock.Start();
        }

        // Apply's work: the pages wait too
        void Busy(bool busy)
        {
            content.Enabled = !busy;
            progress.Value = busy ? 0 : -1;
            PageWorking(busy);
        }

        bool GameClosed()
        {
            if (!Game.IsRunning()) return true;
            MessageBox.Show(this, "Street Fighter IV is running. Close the game, then try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        async Task Apply()
        {
            if (!GameClosed()) return;
            await EnsurePcCheck();
            var changes = AllItems().Where(Changed).ToList();
            // A package of the player's switched back on that's flagged for its size (Weight) asks once more; Yes (the
            // default) puts it in, No leaves it off. Anything else flagged is said once it's in.
            var before = Weight.Keys(game);
            foreach (Item item in changes.Where(i => Wanted(i) && RemovedRecord(i.Key) != null).ToList())
            {
                Installed off = RemovedRecord(item.Key);
                string warning = string.IsNullOrEmpty(off.Package) ? null : Weight.InZip(off.Package, item, Weight.Name(off));
                if (warning == null) continue;
                before.Add(item.Key);
                if (MessageBox.Show(this, warning + "\n\nPut it in anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) continue;
                changes.Remove(item);
                wanted.Remove(item.Key);
            }
            var problems = new List<string>();
            Busy(true);
            int done = 0;
            foreach (Item item in changes)
            {
                done++;
                if (Wanted(item) && Catalog.PulledWhy(item) != null) { problems.Add(Describe(item) + " is switched off for now: " + Catalog.PulledWhy(item)); continue; }
                try
                {
                    if (!Wanted(item))
                    {
                        SetStatus(string.Format("Removing {0} ({1}/{2})...", Describe(item), done, changes.Count));
                        Installer.Remove(game, item.Key);
                        continue;
                    }
                    // A removed package of the player's, switched back on: from its zip again.
                    Installed off = RemovedRecord(item.Key);
                    if (off != null)
                    {
                        if (string.IsNullOrEmpty(off.Package) || !File.Exists(off.Package))
                            throw new FileNotFoundException("its zip isn't at " + (off.Package ?? "its old place") + " any more; add it again with Add from file");
                        SetStatus(string.Format("Adding {0} ({1}/{2})...", Describe(item), done, changes.Count));
                        string target = game;
                        await Task.Run(() => Installer.InstallPackage(target, off.Item, off.Package, "file"));
                        continue;
                    }
                    SetStatus(string.Format("{0} {1} ({2}/{3})...", IsInstalled(item.Key) ? "Updating" : "Downloading", Describe(item), done, changes.Count));
                    progress.Value = 0;
                    var report = new Progress<long>(bytes => progress.Value = item.Size > 0 ? (int)Math.Min(100, bytes * 100 / item.Size) : 0);
                    await Installer.Install(game, item, report);
                }
                catch (Exception ex) { problems.Add(Describe(item) + ": " + ex.Message); }
            }
            // Replaced game stages: changed ones are put back first, then the new ones are made (converting a stage
            // takes a few seconds, so it runs off the window's thread).
            string folder = game;
            var stageChanges = ReplacementChanges();
            var restores = stageChanges.Where(s => Replacement(replaced, s) != null).ToList();
            var makes = stageChanges.Where(s => Replacement(wantedReplaced, s) != null).ToList();
            int step = 0, steps = restores.Count + makes.Count;
            foreach (string stage in restores)
            {
                SetStatus(string.Format("Putting back {0} ({1}/{2})...", StageName(stage), ++step, steps));
                progress.Value = step * 100 / steps;
                try
                {
                    string note = await Task.Run(() => Replacements.Restore(folder, stage));
                    if (note != null) problems.Add(StageName(stage) + ": " + note);
                }
                catch (Exception ex) { problems.Add(StageName(stage) + ": " + ex.Message); }
            }
            foreach (string stage in makes)
            {
                string source = wantedReplaced[stage];
                SetStatus(string.Format("Replacing {0} with {1} ({2}/{3})...", StageName(stage), StageName(source), ++step, steps));
                progress.Value = step * 100 / steps;
                try
                {
                    string problem = await Task.Run(() => Replacements.Problem(folder, stage, source, StageName));
                    if (problem != null) throw new InvalidOperationException(problem);
                    await Task.Run(() => Replacements.Replace(folder, stage, source));
                }
                catch (Exception ex) { problems.Add(StageName(stage) + ": " + ex.Message); }
            }
            // Replaced game costumes the same way: changed ones put back, then the new ones made (after the installs above,
            // so a costume just installed can stand in)
            var swapChanges = SwapChanges();
            foreach (string key in swapChanges.Where(k => Swap(swapped, k) != 0))
            {
                string f; int c;
                if (!CostumeSwaps.ParseKey(key, out f, out c)) continue;
                string what = Fighters.Name(f) + "'s " + CostumeGrid.CostumeName(c);
                SetStatus("Putting back " + what + "...");
                try
                {
                    string note = await Task.Run(() => CostumeSwaps.Restore(folder, f, c));
                    if (note != null) problems.Add(what + ": " + note);
                }
                catch (Exception ex) { problems.Add(what + ": " + ex.Message); }
            }
            foreach (string key in swapChanges.Where(k => Swap(wantedSwaps, k) != 0))
            {
                string f; int c;
                if (!CostumeSwaps.ParseKey(key, out f, out c)) continue;
                int source = wantedSwaps[key];
                string what = Fighters.Name(f) + "'s " + CostumeGrid.CostumeName(c);
                SetStatus("Replacing " + what + " with " + CostumeLabel(f, source) + "...");
                try { await Task.Run(() => CostumeSwaps.Replace(folder, f, c, source)); }
                catch (Exception ex) { problems.Add(what + ": " + ex.Message); }
            }
            // each fighter's effects as chosen (Effects): what's in goes, the chosen one goes in
            foreach (string f in EffectChanges())
            {
                string id = EffectOf(wantedEffects, f);
                SetStatus((id != null ? "Putting in " : "Taking out ") + Fighters.Name(f) + "'s effects...");
                try { await Task.Run(() => Effects.Set(folder, f, id)); }
                catch (Exception ex) { problems.Add(Fighters.Name(f) + "'s effects: " + ex.Message); }
            }
            foreach (var rename in Renames())
            {
                try { Installer.Rename(folder, rename.Key, rename.Value); }
                catch (Exception ex) { problems.Add("Renaming to " + rename.Value + ": " + ex.Message); }
            }
            string round = RoundMusicMod();
            if (round != null) problems.Add(round);
            Busy(false);
            Reload();
            var notes = Weight.New(game, before);
            string noted = notes.Count > 0 ? "Note: " + string.Join("\n\n", notes) : null;
            if (problems.Count > 0)
            {
                BugReport.Noted("Some changes didn't go through: " + string.Join("; ", problems));
                MessageBox.Show(this, "Some changes didn't go through:\n\n" + string.Join("\n", problems) + (noted != null ? "\n\n" + noted : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                UpdateStatus("Done");
                if (noted != null) MessageBox.Show(this, noted, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            await TestUntested();
        }

        // Mods with a size warning in the game that were never tested on this PC (user 2026-10-13: "Test on Apply", "ask the
        // user before you test"): after Apply or an install (Add Mod From File, Browse Mods, Old mods) the player is asked
        // once to test them; each failing one is switched off and locked, as a test from its mark does. No: they stay in,
        // not asked about again until EX More Stuff opens next. Nothing when the game or Steam can't run a test.
        readonly HashSet<string> testDeclined = new HashSet<string>();
        bool testingUntested, holdTests;

        async Task TestUntested()
        {
            if (testingUntested || holdTests || !Game.IsGameFolder(game) || PerfTest.Blocked() != null) return;
            var untested = Installer.Load(game).Where(r => Weight.Lockable(game, r.Item) && PerfTest.Last(r.Item.Key) == null && PerfTest.Locked(r.Item) == null &&
                                                           !testDeclined.Contains(r.Item.Key)).ToList();
            if (untested.Count == 0) return;
            testingUntested = true;
            try
            {
                bool one = untested.Count == 1;
                string which = one ? Weight.Name(untested[0]) + " has a size warning and hasn't been tested on this PC."
                                   : "These have a size warning and haven't been tested on this PC:\n    " + string.Join("\n    ", untested.Select(Weight.Name));
                if (MessageBox.Show(this, which + "\n\nTest " + (one ? "it" : "them") + " now? The game opens for a few seconds" + (one ? "" : " each") + " and closes by itself. " +
                        (one ? "If it doesn't load or doesn't keep full speed, it's" : "One that doesn't load or doesn't keep full speed is") + " switched off and can't be " +
                        "switched on on this PC.\n\nNo: " + (one ? "it stays" : "they stay") + " in, untested.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    foreach (Installed r in untested) testDeclined.Add(r.Item.Key);
                    return;
                }
                foreach (Installed r in untested) await Test(r.Item, r);
            }
            finally { testingUntested = false; }
        }

        // Tom's Round BGM mod in or out to match the round 2/3 music installed; what got in the way, or null.
        string RoundMusicMod()
        {
            try
            {
                string note = RoundMod.Ensure(game);
                RoundMod.RemoveIfUnused(game);
                return note;
            }
            catch (Exception ex) { return "Tom's Round BGM mod (for round 2 and 3 music): " + ex.Message; }
        }

        static string Describe(Item item)
        {
            return (item.IsStage ? "" : Fighters.Name(item.Fighter) + " ") + item.Title;
        }

        // Advanced > Add Mod From File. A package made for EX More Stuff (a costume slot 8-99 or a stage code of its own,
        // complete) goes in as it is; anything else (a skin or stage made over the game's own, downloaded from
        // anywhere, or a package that isn't complete) goes through Browse Mods, which builds it into a personal slot or
        // code with the game's files. So nothing of the game's own is ever written over.
        async Task AddFromFile()
        {
            string[] paths;
            using (var dialog = new OpenFileDialog { Filter = "Mod files (*.zip; *.rar; *.7z)|*.zip;*.rar;*.7z", Title = "Add mods from files (pick one or more)", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                paths = dialog.FileNames;
            }
            // into the game, or docked for edits (user 2026-10-12): docked, nothing goes in the game
            DockDialog.Choice how;
            using (var ask = new DockDialog(paths)) { ask.ShowDialog(this); how = ask.Picked; }
            if (how == DockDialog.Choice.Cancel) return;
            if (how == DockDialog.Choice.Dock) { await DockFiles(paths); return; }
            if (!GameClosed()) return;
            await EnsurePcCheck();
            holdTests = true;
            try { await AddFiles(paths); }
            finally { holdTests = false; }
            await TestUntested();
        }

        // bulk: Fix a folder of mods (OldModsPanel) adds one mod at a time through here: nothing is asked (a faulty costume is
        // fixed or goes in with its warning, one in a taken seat or already installed is left out) and nothing is shown;
        // what happened goes into bulk for the folder's own message
        async Task AddFiles(string[] paths, BulkAdd bulk = null)
        {
            var added = new List<string>();
            var already = new List<string>();
            var notAdded = new List<string>();
            var renames = new List<string>();   // color packs moved up to 30+ (RenumberColors)
            var before = Weight.Keys(game);
            string lastDone = null;   // what a mod built from its files went in as ("... is in: stage U05")
            foreach (string path in paths)
            {
                string file = Path.GetFileName(path);
                // the same file added before (a duplicate, perhaps under another name): left as it is
                string g = game;
                string twin = await Task.Run(() => Installer.AddedBefore(g, path));
                if (twin != null) { already.Add(file + "  (" + twin + ")"); continue; }
                string renumbered = await Task.Run(() => Installer.RenumberColors(path));   // colors 11-29 moved up to 30+ in the file (Tom 2026-10-26)
                if (renumbered != null) renames.Add(file + ": " + renumbered);
                List<Item> items = Installer.Packages(path);   // a color pack's zip: each of its colors (user 2026-10-18)
                Item item = items.FirstOrDefault();
                if (item == null)
                {
                    // built and put in by Browse Mods' installer, staying on this tab (user 2026-10-14: it switched to Browse
                    // Mods), its progress and result in the status line; a GameBanana mod's file goes to its card there
                    communityPage.Progress = SetStatus;
                    string problem;
                    try { problem = await communityPage.InstallFile(path); }
                    finally { communityPage.Progress = null; }
                    if (problem == null) { added.Add(file + (communityPage.LastDone != null && communityPage.LastDone.Contains("(fixed: ") ? " (fixed)" : "")); lastDone = communityPage.LastDone; }
                    else
                    {
                        notAdded.Add(file + ": " + problem);
                        if (problem.Contains("from GameBanana")) ShowTab(communityTab);   // its card is highlighted there
                    }
                    before.UnionWith(Weight.Keys(game));   // Browse Mods' own message says its size warnings
                    continue;
                }
                try
                {
                    // Any slot or code: it only stops at files of that slot EX More Stuff didn't put there (InstallPackage).
                    string warning = Seats.Warning(item);   // the game's, taken on GameBanana, or not free: the player's call
                    if (warning != null && bulk != null) { notAdded.Add(file + ": " + warning); continue; }
                    if (warning != null && MessageBox.Show(this, file + ": " + warning + ". Add it anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) { notAdded.Add(file + ": skipped"); continue; }
                    // a new color of a custom costume shows once that costume is in the game
                    string costume = item.Fighter + "_" + item.Slot.ToString("D2");
                    if (item.IsColor && bulk == null && item.Slot >= Catalog.FirstCustomCostume && !File.Exists(Path.Combine(Game.FolderFor(game, item), costume + ".obj.emo")) &&
                        MessageBox.Show(this, file + ": " + Fighters.Name(item.Fighter) + " " + item.Slot.ToString("D2") + " isn't in the game, so this color won't show until it is. Add it anyway?",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) { notAdded.Add(file + ": skipped"); continue; }
                    Item there = items.FirstOrDefault(i => installed.Concat(removed).Any(r => r.Item.Key == i.Key));
                    if (there != null && bulk != null) { already.Add(file + "  (" + Describe(there) + ")"); continue; }
                    if (there != null && MessageBox.Show(this, file + ": " + Describe(there) + (items.Count > 1 ? " and others in it are" : " is") + " already installed. Replace " +
                            (items.Count > 1 ? "them" : "it") + " with this one?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) { notAdded.Add(file + ": skipped"); continue; }
                    // a copy in Mods\, so the player's file may move; one picked from inside Mods\ is used as it is, and stays theirs
                    string kept = AppFolders.KeepPackage(path, item);
                    bool theirs = string.Equals(Path.GetFullPath(kept), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
                    // a costume: its check (InstallCheck), nothing asked. A fix goes into EX More Stuff's copy, or beside the
                    // player's own zip when it's that one (picked from inside Mods\), and the fixed copy is EX More Stuff's;
                    // one that can't be fixed goes in switched off and locked.
                    string said = null, faults = null;
                    if (item.IsCostume)
                    {
                        string k = kept, g2 = game;
                        bool own = !theirs;
                        string use = await Task.Run(() => InstallCheck.Decide(g2, k, own, out said, out faults));
                        if (use != kept) { kept = use; theirs = false; }
                    }
                    foreach (Item each in items)
                    {
                        Installer.InstallPackage(game, each, kept, "file", theirs);
                        if (!each.IsCostume) continue;
                        InstallCheck.SetWarning(each.Key, faults);
                        if (faults != null) Installer.Remove(game, each.Key);   // it can't be fixed: switched off and locked
                    }
                    Installer.AdoptPackages(game);   // one picked from inside Mods\ goes to its folder there
                    Reload();
                    if (faults != null) notAdded.Add(file + ": switched off, it has problems that can't be fixed (see " + InstallCheck.ListButton + ")");
                    else added.Add(file + (said != null ? " (" + said + ")" : ""));
                }
                catch (Exception ex) { notAdded.Add(file + ": " + ex.Message); }
            }
            Reload();
            string round = added.Count > 0 ? RoundMusicMod() : null;
            var notes = Weight.New(game, before);   // size warnings for what went in: they inform, nothing was stopped
            if (bulk != null)
            {
                bulk.Added.AddRange(added);
                bulk.Already.AddRange(already);
                bulk.NotAdded.AddRange(notAdded);
                bulk.Notes.AddRange(notes);
                if (round != null) bulk.Notes.Add(round);
                return;
            }
            if (paths.Length > 1 || already.Count > 0 || notAdded.Count > 0 || round != null || notes.Count > 0 || renames.Count > 0 ||
                added.Any(a => a.Contains("(fixed: ")))   // a costume fixed (InstallCheck): said
            {
                string report = (added.Count > 0 ? "Added (" + added.Count + "):\n    " + string.Join("\n    ", added) : "Nothing added.") +
                                (already.Count > 0 ? "\n\nAlready added before, left as they are (" + already.Count + "):\n    " + string.Join("\n    ", already) : "") +
                                (notAdded.Count > 0 ? "\n\nNot added (" + notAdded.Count + "):\n    " + string.Join("\n    ", notAdded) : "") +
                                (renames.Count > 0 ? "\n\nRenumbered in the file:\n    " + string.Join("\n    ", renames) : "") +
                                (round != null ? "\n\nNote: " + round + "." : "") +
                                (notes.Count > 0 ? "\n\nNote: " + string.Join("\n\n", notes) : "");
                if (notAdded.Count > 0) BugReport.Noted("Add Mod From File, not added: " + string.Join("; ", notAdded));
                MessageBox.Show(this, report, Text, MessageBoxButtons.OK, notAdded.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            if (added.Count > 0) UpdateStatus(added.Count == 1 && lastDone != null ? lastDone : "Added " + added.Count);
        }

        // Add Mod From File's files docked for edits (Docked); their cards at the top of Advanced. A package is docked as it
        // is; a mod that isn't one (made over the game's own: user 2026-10-14, Urban War Zone "went through just fine"
        // added, not docked) gets its packages built as an add builds them, in Mods\ in slots or codes of the player's
        // own, and those are docked (ModFile.Dock); the game may be open.
        async Task DockFiles(string[] paths)
        {
            var docked = new List<string>();
            var notDocked = new List<string>();
            string g = game;
            Busy(true);
            try
            {
                foreach (string path in paths)
                {
                    string file = Path.GetFileName(path);
                    if (Docked.Has(path)) { notDocked.Add(file + ": it's docked already"); continue; }
                    string renumbered = await Task.Run(() => Installer.RenumberColors(path));   // colors 11-29 moved up to 30+ in the file (Tom 2026-10-26)
                    Item package = Installer.Package(path);
                    if (package != null)
                    {
                        // a costume: its check; a fixed copy goes beside the player's zip, and that's what's docked (one
                        // that can't be fixed is docked as it is, for its maker to work on, and said)
                        string said = null, faults = null, use = path, p = path;
                        if (package.IsCostume) use = await Task.Run(() => InstallCheck.Decide(g, p, false, out said, out faults));
                        Docked.Add(use);
                        docked.Add(file + (use != path ? " (as " + Path.GetFileName(use) + ")" : "") + (renumbered != null ? ": " + renumbered : "") + (said != null ? ": " + said : "") +
                                   (faults != null ? ": it has problems that can't be fixed, so it can't go into the game as it is" : ""));
                        continue;
                    }
                    try
                    {
                        var made = await Task.Run(() => ModFile.Dock(g, path, text => BeginInvoke((Action)(() => SetStatus(text))), true));
                        if (made.Into.Count > 0) docked.Add(file + " (as " + string.Join(", ", made.Into) + ")");
                        foreach (string problem in made.Problems) notDocked.Add(file + ": " + problem);
                    }
                    catch (Exception ex) { notDocked.Add(file + ": " + ex.Message); }
                }
            }
            finally { Busy(false); }
            BuildAdvanced();
            openTool = null;
            ShowTab(advancedTab);
            advancedPage.AutoScrollPosition = Point.Empty;
            if (notDocked.Count > 0 || docked.Any(d => d.Contains(" are now ") || d.Contains("fixed: ") || d.Contains("couldn't be fixed")))   // a color pack renumbered (RenumberColors), a costume fixed: said
                MessageBox.Show(this, (docked.Count > 0 ? "Docked (" + docked.Count + "):\n    " + string.Join("\n    ", docked) : "Nothing docked.") +
                                (notDocked.Count > 0 ? "\n\nNot docked (" + notDocked.Count + "):\n    " + string.Join("\n    ", notDocked) : ""), Text, MessageBoxButtons.OK,
                                notDocked.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            if (docked.Count > 0) UpdateStatus("Docked " + docked.Count + " for edits");
        }

        // A docked file's card (Advanced): Color pictures (a costume) on Costume creation, its code changed in its zip,
        // Remove takes it off the list (the file stays).
        Control DockedCard(List<Installed> files, int width)
        {
            Installed file = files[0];
            Item item = file.Item;
            bool pack = files.Count > 1;   // a color pack's zip, its colors
            var shots = item.IsStage ? PerfTest.PreviewPictures(file.Package) : new List<string>();
            var row = new DockedRow
            {
                Size = new Size(width - 16, DockedRow.RowHeight + (shots.Count > 0 ? DockedRow.ShotsHeight : 0)),
                Margin = new Padding(8, 4, 8, 4),
                Title = pack ? Fighters.Name(item.Fighter) + " " + (file.Shown ?? item.Name) : Weight.Name(file),
                Kind = item.IsStage ? "Stage" : pack ? "Colors" : item.IsColor ? "Color" : "Costume",
                FilePath = file.Package,
                CanMakePictures = item.IsCostume || item.IsColor,   // a color pack's too (user 2026-10-18)
                CanPreview = item.IsStage,
            };
            // a stage: Preview in game, and the last preview's pictures under the card (made small in the background;
            // full size in ShotViewer on a click)
            tips.SetToolTip(row.Preview, "Watch this stage in the game: it pans from one side to the other, without fighters, then the game closes.");
            row.Preview.Click += async (s, e) => await PreviewStage(file, row);
            foreach (ShotThumb thumb in row.Thumbs) tips.SetToolTip(thumb, "Click to see it full size");
            // full size; one can be made the stage's picture (into its zip), the card's picture following
            Func<string, bool> setPicture = shot =>
            {
                try { PerfTest.UseAsStagePicture(file.Package, item, shot); }
                catch (Exception ex) { MessageBox.Show(this, "The stage picture wasn't set: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                byte[] bytes = Installer.PackagePicture(file.Package, item);
                if (bytes != null) { row.Picture = Pictures.Read(bytes); row.Invalidate(); }
                UpdateStatus(row.Title + ": stage picture set");
                return true;
            };
            row.ShotClicked += i => { using (var viewer = new ShotViewer(row.Title, shots, i, PerfTest.StagePictureShot(file.Package), setPicture)) viewer.ShowDialog(this); };
            if (shots.Count > 0)
                Task.Run(() => shots.Select(p =>
                {
                    using (var full = new Bitmap(new MemoryStream(File.ReadAllBytes(p))))
                    {
                        var small = new Bitmap(DockedRow.ThumbWidth * 2, DockedRow.ThumbHeight * 2);
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(full, 0, 0, small.Width, small.Height);
                        }
                        return (Image)small;
                    }
                }).ToList()).ContinueWith(t =>
                {
                    if (t.IsFaulted || row.IsDisposed) return;
                    row.SetShots(t.Result);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            row.ShowFile.Text = file.Package;
            tips.SetToolTip(row.ShowFile, "Show it in its folder");
            row.Code.MaxLength = item.IsStage ? 3 : 2;
            row.Code.Text = item.IsStage ? item.Code : item.IsColor ? item.Color.ToString() : item.Slot.ToString();
            if (pack)   // its colors' numbers: picked (PickSlot), they move together on Confirm
            {
                row.Code.MaxLength = 7;
                row.Code.Text = files.Min(f => f.Item.Color) + "-" + files.Max(f => f.Item.Color);
                row.Code.ReadOnly = true;
            }
            row.ColorPictures.Click += (s, e) => { openTool = "creation"; creationPage.SetGame(game); ShowTab(advancedTab); };
            row.Change.Click += async (s, e) =>
            {
                int start;
                string typed = row.Code.Text.Trim();
                if (pack) { if (int.TryParse(typed.Split('-')[0], out start)) await MoveDockedPack(files, start, row); }
                else await MoveDocked(file, row);
            };
            // a costume's or color's slot box: which slots are free and which are taken, a click moves it there (user 2026-10-21:
            // "when you click the slot option (if its a color or costume) it should show you which ones you will be taking")
            if (!item.IsStage)
            {
                row.Code.Cursor = Cursors.Hand;
                row.Code.Click += async (s, e) => await PickSlot(files, row);
            }
            else   // a stage's: Get an unused stage code puts the next one for GameBanana in its box (user 2026-10-21); Confirm moves it
            {
                row.CanGetCode = true;
                row.UnusedCode.Click += async (s, e) => await OfferStageCode(file, row);
            }
            row.Remove.Click += (s, e) =>
            {
                Docked.Remove(file.Package);
                if (item.IsStage) try { Directory.Delete(PerfTest.PreviewFolder(file.Package), true); } catch (Exception) { }   // its preview's pictures
                BuildAdvanced();
                UpdateStatus(row.Title + " is off Advanced (its file stays where it is)");
            };
            // its picture, read from the zip in the background
            Task.Run(() => { byte[] bytes = Installer.PackagePicture(file.Package, item); return bytes != null ? Pictures.Read(bytes) : null; }).ContinueWith(t =>
            {
                if (t.IsFaulted || row.IsDisposed) return;
                row.Picture = t.Result;
                row.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return row;
        }

        // A docked stage previewed in the game (PerfTest.StagePreview): about 17 s with the game starting; the pictures
        // then show under its card (Advanced is made again).
        async Task PreviewStage(Installed file, DockedRow row)
        {
            string blocked = PerfTest.Blocked();
            if (blocked != null) { MessageBox.Show(this, blocked + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string g = game, problem = null;
            row.Preview.Enabled = false;
            UpdateStatus("The game opens, pans across the stage and closes by itself...");
            Busy(true);
            try { await Task.Run(() => PerfTest.StagePreview(g, file.Item, file.Package)); }
            catch (Exception ex) { problem = ex.Message; }
            finally { Busy(false); }
            BuildAdvanced();
            if (problem != null)
            {
                UpdateStatus("The preview didn't finish: " + problem);
                BugReport.Noted("Stage preview: " + problem);
                MessageBox.Show(this, "The preview didn't finish: " + problem + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else UpdateStatus("Preview done: " + row.Title);
        }

        // a docked file to another slot, stage code or color number: only its zip is rewritten (it isn't in the game)
        // A docked costume's or color's slot picker under its slot box: the fighter's slots (8-99) or the costume's colors
        // (30-99) as Free codes shows them, slots and colors its own mods use counted as taken; hovering shows what it would
        // take (a color pack: all its colors, their spacing kept); a click moves it there (asked first)
        async Task PickSlot(List<Installed> files, DockedRow row)
        {
            Item item = files[0].Item;
            if (SharedCodes.HeldBy.Count == 0) try { SharedCodes.Holders(await GameBanana.All()); } catch (Exception) { }   // who holds what on GameBanana
            var mine = files.Select(f => f.Item.Key).ToList();
            var others = installed.Concat(removed).Concat(Docked.Load()).Where(r => !mine.Contains(r.Item.Key) && r.Item.Fighter == item.Fighter).ToList();
            int first = files.Min(f => f.Item.Color);
            var grid = new SeatGrid { Fighter = item.Fighter, Margin = Padding.Empty };
            if (item.IsColor)
            {
                grid.First = Catalog.FirstColor(item.Slot);
                grid.Last = Catalog.LastColor;
                grid.Takes = files.Select(f => f.Item.Color - first).OrderBy(t => t).ToArray();
                string folder = Game.IsGameFolder(game) ? Game.FolderFor(game, item) : null;
                grid.Answer = n =>
                {
                    Installed used = others.FirstOrDefault(r => r.Item.IsColor && r.Item.Slot == item.Slot && r.Item.Color == n);
                    if (used != null) return new Seats.Answer { Kind = Seats.Kind.Personal, Text = "Color " + n + " is used by " + Weight.Name(used) };
                    if (folder != null && File.Exists(Path.Combine(folder, item.Fighter + "_" + item.Slot.ToString("D2") + "_" + n.ToString("D2") + ".col.emb")) && !files.Any(f => f.Item.Color == n))
                        return new Seats.Answer { Kind = Seats.Kind.NotFree, Text = "The game folder already has color " + n };
                    return new Seats.Answer { Kind = Seats.Kind.Free, Text = "Color " + n + " is free" };
                };
            }
            else
                grid.Answer = n =>
                {
                    Installed used = others.FirstOrDefault(r => r.Item.IsCostume && r.Item.Slot == n);
                    return used != null ? new Seats.Answer { Kind = Seats.Kind.Personal, Text = Fighters.Name(item.Fighter) + " " + n.ToString("D2") + " is used by " + Weight.Name(used) }
                                        : Seats.Costume(item.Fighter, n);
                };
            grid.Selected = item.IsColor ? first : item.Slot;
            grid.Width = 13 * 43 - 5;   // thirteen across: room for the legend
            var panel = new Panel { BackColor = Theme.Card, Padding = new Padding(10), Size = new Size(grid.Width + 20, grid.Height + 20) };
            grid.Location = new Point(10, 10);
            panel.Controls.Add(grid);
            var drop = new ToolStripDropDown { Padding = Padding.Empty, BackColor = Theme.Card, DropShadowEnabled = true };
            var host = new ToolStripControlHost(panel) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = panel.Size };
            drop.Items.Add(host);
            drop.Closed += (s, e) => BeginInvoke((Action)(() => drop.Dispose()));
            // a pick goes in its box; Confirm moves it
            grid.Picked += n =>
            {
                drop.Close();
                row.Code.Text = item.IsColor && files.Count > 1 ? n + "-" + (n + grid.Takes.Max()) : n.ToString(item.IsColor ? "0" : "D2");
                row.Change.Focus();
            };
            drop.Show(row.Code, new Point(0, row.Code.Height + 2));
        }

        // GameBanana uploaders' stage codes (user 2026-10-21: "B and then just go up the letters. A and T are taken"): B01-B99,
        // then D01, E01 ... Z99; not C (the catalog's), U (players' own), A or T (ours)
        const string StageLetters = "BDEFGHIJKLMNOPQRSVWXYZ";

        /// <summary>The first of those no GameBanana mod holds (deleted ones too) and nothing on this PC uses, or null.</summary>
        static string UnusedStageCode(string game, IEnumerable<string> usedHere)
        {
            var used = new HashSet<string>(usedHere.Where(c => c != null), StringComparer.OrdinalIgnoreCase);
            foreach (char letter in StageLetters)
                for (int n = 1; n <= 99; n++)
                {
                    string code = letter + n.ToString("D2");
                    if (!Stages.IsCustomCode(code) || used.Contains(code) || SharedCodes.HeldBy.ContainsKey("stage " + code) ||
                        Catalog.NotFree(true, code, 0) || Catalog.PersonalSeat(true, code, 0)) continue;
                    if (Game.IsGameFolder(game) && File.Exists(Path.Combine(Game.PatchFolder(game), "battle", "stage", "STG_" + code + ".emz"))) continue;
                    return code;
                }
            return null;
        }

        // a docked stage's Get an unused stage code: the next one in its code box, for Confirm
        async Task OfferStageCode(Installed file, DockedRow row)
        {
            if (SharedCodes.HeldBy.Count == 0) try { SharedCodes.Holders(await GameBanana.All()); } catch (Exception) { }   // who holds what on GameBanana
            string code = UnusedStageCode(game, installed.Concat(removed).Concat(Docked.Load()).Where(r => r.Item.IsStage && r.Item.Key != file.Item.Key).Select(r => r.Item.Code));
            if (code == null) return;
            row.Code.Text = code;
            row.Change.Focus();
        }

        // a docked color pack's colors moved together, their spacing kept (each in its zip as Package codes does it)
        async Task MoveDockedPack(List<Installed> files, int start, DockedRow row)
        {
            var colors = files.OrderBy(f => f.Item.Color).ToList();
            int shift = start - colors[0].Item.Color, last = colors[colors.Count - 1].Item.Color;
            if (shift == 0) return;
            int lowest = Catalog.FirstColor(colors[0].Item.Slot);
            if (start < lowest || last + shift > Catalog.LastColor)
            {
                MessageBox.Show(this, "Can't change it: its colors would go past " + lowest + "-" + Catalog.LastColor + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string to = "colors " + start + "-" + (last + shift);
            if (MessageBox.Show(this, "Move " + row.Title + " to " + to + "?\n\nIts zip is rewritten with the new numbers (" + Path.GetFileName(colors[0].Package) + ").",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            // moving up: the highest first, down: the lowest first, so each lands on a number that's free by then
            var order = shift > 0 ? colors.AsEnumerable().Reverse().ToList() : colors;
            try { await Task.Run(() => { foreach (Installed f in order) CodeChange.MoveDocked(f, null, f.Item.Color + shift); }); }
            catch (Exception ex) { MessageBox.Show(this, "It didn't all move: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            BuildAdvanced();
            UpdateStatus(row.Title + " is now " + to);
        }

        async Task MoveDocked(Installed file, DockedRow row)
        {
            Item item = file.Item;
            string code = row.Code.Text.Trim().ToUpperInvariant();
            int slot = 0;
            if (!item.IsStage && !int.TryParse(code, out slot)) { MessageBox.Show(this, item.IsColor ? "A color is a number." : "A costume slot is a number.", Text); return; }
            string problem = CodeChange.DockedProblem(file, code, slot);
            if (problem != null) { MessageBox.Show(this, "Can't change it: " + problem + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string to = item.IsStage ? code : item.IsColor ? "color " + slot : "slot " + slot;
            // the game's, taken on GameBanana, or not free: a warning, the player's call
            string warning = Seats.Warning(new Item { Id = item.Id, Type = item.Type, Fighter = item.Fighter, Slot = item.IsStage ? 0 : slot, Code = item.IsStage ? code : "" });
            if (MessageBox.Show(this, (warning != null ? warning + ". Use it anyway?\n\n" : "") + "Move " + row.Title + " to " + to + "?\n\nIts zip is rewritten with the new " +
                    (item.IsStage ? "code" : item.IsColor ? "number" : "slot") + " (" + Path.GetFileName(file.Package) + ").", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            try { await Task.Run(() => CodeChange.MoveDocked(file, code, slot)); }
            catch (Exception ex) { MessageBox.Show(this, "It didn't move: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            BuildAdvanced();
            UpdateStatus(row.Title + " is now " + to);
        }

        // the guide's buttons (Advanced > Uploading Guide)
        void GuideGo(string where)
        {
            if (where == "addfile") { var ignored = AddFromFile(); }
            else if (where == "freecodes") { openTool = "freecodes"; ShowTab(advancedTab); }
            else if (where == "creation") { openTool = "creation"; creationPage.SetGame(game); ShowTab(advancedTab); }
            else if (where == "browse") { ShowTab(communityTab); communityPage.ShowAll(); }
        }

        // Everything off at once; it all stays listed (catalog items, and the player's own packages from their zips).
        void DisableEverything()
        {
            if ((installed.Count == 0 && replaced.Count == 0 && swapped.Count == 0) || !GameClosed()) return;
            string what = (installed.Count > 0 ? "Switch off all " + installed.Count + " costumes and stages EX More Stuff installed" : "") +
                          (installed.Count > 0 && replaced.Count > 0 ? " and put back " : replaced.Count > 0 ? "Put back " : "") +
                          (replaced.Count > 0 ? "the " + replaced.Count + " game stage" + (replaced.Count > 1 ? "s" : "") + " it replaced" : "");
            if (swapped.Count > 0) what += (what.Length > 0 ? " and put back " : "Put back ") + "the " + swapped.Count + " game costume" + (swapped.Count > 1 ? "s" : "") + " it replaced";
            if (MessageBox.Show(this, what + "? Their files leave the game, but they stay listed, so you can switch them back on. The game's own files are not touched.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            SaveEverything();   // what was on, for Restore previous
            var problems = new List<string>();
            foreach (Installed record in installed.ToList())
            {
                try { Installer.Remove(game, record.Item.Key); }
                catch (Exception ex) { problems.Add(Describe(record.Item) + ": " + ex.Message); }
            }
            foreach (string stage in replaced.Keys.ToList())
            {
                try
                {
                    string note = Replacements.Restore(game, stage);
                    if (note != null) problems.Add(StageName(stage) + ": " + note);
                }
                catch (Exception ex) { problems.Add(StageName(stage) + ": " + ex.Message); }
            }
            foreach (string key in swapped.Keys.ToList())
            {
                string f; int c;
                if (!CostumeSwaps.ParseKey(key, out f, out c)) continue;
                try
                {
                    string note = CostumeSwaps.Restore(game, f, c);
                    if (note != null) problems.Add(Fighters.Name(f) + "'s " + CostumeGrid.CostumeName(c) + ": " + note);
                }
                catch (Exception ex) { problems.Add(Fighters.Name(f) + "'s " + CostumeGrid.CostumeName(c) + ": " + ex.Message); }
            }
            string round = RoundMusicMod();
            if (round != null) problems.Add(round);
            Reload();
            if (problems.Count > 0)
                MessageBox.Show(this, "Some couldn't be switched off:\n\n" + string.Join("\n", problems), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Restore previous (user 2026-10-16: "we need the inverse of that button ... restore previous"): what Disable
        // everything switched off (everything.json beside the program, for this game folder) wanted on again, the game
        // stages and costumes they stood in for as they were, then Apply as usual (a flagged package asks once more);
        // ones gone since or locked by a failed test stay off.
        static string EverythingFile { get { return AppFolders.DataFile("everything.json"); } }

        void SaveEverything()
        {
            try
            {
                var j = new Dictionary<string, object>
                {
                    { "game", game }, { "on", installed.Select(r => r.Item.Key).ToList() },
                    { "replaced", replaced.ToDictionary(r => r.Key, r => (object)r.Value) }, { "swapped", swapped.ToDictionary(s => s.Key, s => (object)s.Value) },
                };
                SafeFile.WriteAllText(EverythingFile, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(j), true);
            }
            catch (Exception) { }   // not kept: Restore previous can't offer it
        }

        // what Disable everything switched off that isn't back on: (keys, replacements, swaps), or null
        Tuple<List<string>, Dictionary<string, string>, Dictionary<string, int>> Previous()
        {
            try
            {
                if (!File.Exists(EverythingFile)) return null;
                var j = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(EverythingFile));
                if (!string.Equals(Convert.ToString(j["game"]), game, StringComparison.OrdinalIgnoreCase)) return null;
                var items = AllItems().GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
                Func<Item, bool> usable = i => Unusable(i) == null;
                var on = ((System.Collections.ArrayList)j["on"]).Cast<object>().Select(Convert.ToString)
                         .Where(k => items.ContainsKey(k) && usable(items[k]) && !IsInstalled(k)).ToList();
                var stages = new HashSet<string>(Stages.Codes.Concat(AllItems().Where(i => i.IsStage && usable(i)).Select(i => i.Code)).Concat(Overrides.Load(game).Select(o => Overrides.Prefix + o.Id)));
                var rep = ((Dictionary<string, object>)j["replaced"]).ToDictionary(r => r.Key, r => Convert.ToString(r.Value))
                          .Where(r => stages.Contains(r.Value) && Replacement(replaced, r.Key) != r.Value).ToDictionary(r => r.Key, r => r.Value);
                var swaps = ((Dictionary<string, object>)j["swapped"]).ToDictionary(s => s.Key, s => Convert.ToInt32(s.Value)).Where(s =>
                {
                    string f; int c;
                    return CostumeSwaps.ParseKey(s.Key, out f, out c) && Swap(swapped, s.Key) != s.Value &&
                           (s.Value == 1 || AllItems().Any(i => i.IsCostume && i.Fighter == f && i.Slot == s.Value && usable(i)));
                }).ToDictionary(s => s.Key, s => s.Value);
                if (on.Count + rep.Count + swaps.Count == 0) { File.Delete(EverythingFile); return null; }   // all back on: nothing to restore
                return Tuple.Create(on, rep, swaps);
            }
            catch (Exception) { return null; }
        }

        async Task RestorePrevious()
        {
            var previous = Previous();
            if (previous == null || !GameClosed()) return;
            foreach (string key in previous.Item1) wanted[key] = true;
            foreach (var r in previous.Item2) wantedReplaced[r.Key] = r.Value;
            foreach (var s in previous.Item3) wantedSwaps[s.Key] = s.Value;
            await Apply();
            if (Previous() == null) try { File.Delete(EverythingFile); } catch (Exception) { }
            ShowEverythingButton();
        }

        // the footer's button: Disable everything, or Restore previous while something it switched off is still off; its
        // hover says which (wording approved by the user)
        void ShowEverythingButton()
        {
            var previous = Previous();
            if (previous == null)
            {
                disableAll.Text = "Disable everything";
                tips.SetToolTip(disableAll, "Switch off every costume and stage EX More Stuff put in the game, and give the game back its own stages and costumes " +
                                            "where they were replaced. Nothing is deleted: Restore previous turns it all back on.");
            }
            else
            {
                int stages = previous.Item2.Count + previous.Item3.Count;
                disableAll.Text = "Restore previous";
                tips.SetToolTip(disableAll, "Turn back on what Disable everything switched off: " + previous.Item1.Count + " costume" + (previous.Item1.Count == 1 ? "" : "s") + " and stage" +
                                            (previous.Item1.Count == 1 ? "" : "s") + (stages > 0 ? ", and " + stages + " game stage" + (stages == 1 ? "" : "s") + " and costume" + (stages == 1 ? "" : "s") + " they stood in for" : "") + ".");
            }
            disableAll.Invalidate();
        }
    }
}
