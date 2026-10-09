using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        readonly Dictionary<string, Image> pictures = new Dictionary<string, Image>();
        readonly Dictionary<string, Image> stagePictures = new Dictionary<string, Image>();
        readonly Dictionary<string, Image> portraits = new Dictionary<string, Image>();
        readonly Dictionary<string, FighterTile> tiles = new Dictionary<string, FighterTile>();
        string catalogProblem, openFighter;
        readonly Image art = Theme.Resource("background.jpg"), wordmark = Theme.Resource("wordmark.png");
        Bitmap backdrop;

        readonly TabButton costumesTab = new TabButton("Costumes"), stagesTab = new TabButton("Stages"), communityTab = new TabButton("Browse Mods"), advancedTab = new TabButton("Advanced"), aboutTab = new TabButton("About");
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(14, 12, 14, 12) };
        readonly Clear rosterPage = new Clear { Dock = DockStyle.Fill };
        readonly Clear costumePage = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly Label costumeTitle = new Label { AutoSize = true, BackColor = Color.Transparent, Font = Theme.Bold(15f), ForeColor = Theme.Text };
        readonly ScrollList costumeCards = new ScrollList { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        readonly ScrollList stagesPage = new ScrollList { Dock = DockStyle.Fill, Visible = false };
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
        readonly Clear freeCodesView = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly FreeCodesPanel freeCodesPage = new FreeCodesPanel { Dock = DockStyle.Fill };
        string openTool;   // the Advanced tool open: null (its cards), "music", "packages" or "storage"
        readonly Label gameLabel = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(8.5f), AutoEllipsis = true };
        readonly LinkLabel change = new LinkLabel { Text = "Change", AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(8.5f),
                                                    LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline };
        readonly Label status = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Bold(9.5f), AutoEllipsis = true };
        readonly Strip header = new Strip { Height = 76 }, footer = new Strip { Height = 70, LineOnTop = true };
        readonly ProgressLine progress = new ProgressLine();
        readonly ToolTip tips = new ToolTip();
        readonly FlatButton apply = new FlatButton("Apply changes", true) { Enabled = false };
        readonly FlatButton disableAll = new FlatButton("Disable everything");

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
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            // Header: logo, the EX MORE STUFF lettering, what it's for, tabs.
            header.Dock = DockStyle.Top;
            var logo = new PictureBox { Image = Theme.Resource("logo.png"), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Bounds = new Rectangle(18, 14, 48, 48) };
            var title = new PictureBox { Image = wordmark, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Bounds = new Rectangle(76, 16, 205, 44) };
            var divider = new Panel { BackColor = Color.FromArgb(52, 55, 62), Bounds = new Rectangle(title.Right + 14, 22, 1, 32) };
            tagline = new Label { Text = "Custom costumes, stages and music\nfor Ultra Street Fighter IV", AutoSize = true, BackColor = Color.Transparent,
                                  Font = Theme.Font(9f), ForeColor = Theme.Muted, Location = new Point(divider.Right + 12, 21) };
            aboutTab.Location = new Point(ClientSize.Width - aboutTab.Width - 18, 26);
            advancedTab.Location = new Point(aboutTab.Left - advancedTab.Width - 4, 26);
            communityTab.Location = new Point(advancedTab.Left - communityTab.Width - 4, 26);
            stagesTab.Location = new Point(communityTab.Left - stagesTab.Width - 4, 26);
            costumesTab.Location = new Point(stagesTab.Left - costumesTab.Width - 4, 26);
            costumesTab.Selected = true;
            costumesTab.Click += (s, e) => ShowTab(costumesTab);
            stagesTab.Click += (s, e) => ShowTab(stagesTab);
            communityTab.Click += (s, e) => ShowTab(communityTab);
            advancedTab.Click += (s, e) => ShowTab(advancedTab);
            aboutTab.Click += (s, e) => ShowTab(aboutTab);
            header.Controls.AddRange(new Control[] { logo, title, divider, tagline, costumesTab, stagesTab, communityTab, advancedTab, aboutTab });

            // Footer: status, the game folder, buttons.
            footer.Dock = DockStyle.Bottom;
            progress.Bounds = new Rectangle(0, 0, ClientSize.Width, 3);
            apply.Location = new Point(ClientSize.Width - apply.Width - 18, 17);
            disableAll.Location = new Point(apply.Left - disableAll.Width - 10, 17);
            status.Bounds = new Rectangle(18, 14, disableAll.Left - 36, 20);
            gameLabel.Bounds = new Rectangle(18, 36, disableAll.Left - 100, 18);
            change.LinkClicked += (s, e) => ChooseGame();
            footer.Controls.AddRange(new Control[] { progress, status, gameLabel, change, disableAll, apply });
            disableAll.Click += (s, e) => DisableEverything();
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
            foreach (var view in new[] { Tuple.Create(musicView, (Control)musicPage), Tuple.Create(packagesView, (Control)packagesPage), Tuple.Create(storageView, (Control)storagePage), Tuple.Create(freeCodesView, (Control)freeCodesPage) })
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
            communityPage.UpdatesChanged += count => communityTab.Dot = count > 0;   // the purple dot: updates on GameBanana
            freeCodesPage.Portrait = code => { Image portrait; return portraits.TryGetValue(code, out portrait) ? portrait : null; };
            communityPage.Portrait = code => { Image portrait; return portraits.TryGetValue(code, out portrait) ? portrait : null; };
            communityPage.Working += PageWorking;
            packagesPage.Working += PageWorking;
            musicPage.Working += PageWorking;
            unlock.Tick += (s, e) => { unlock.Stop(); Lock(false); };

            var credits = new AboutCard(Theme.Resource("logo.png"), wordmark, typeof(MainForm).Assembly.GetName().Version.ToString());
            aboutPage.Controls.Add(credits);
            aboutPage.Resize += (s, e) => credits.Location = new Point((aboutPage.Width - credits.Width) / 2, 8);

            content.Controls.AddRange(new Control[] { rosterPage, costumePage, stagesPage, communityPage, advancedPage, musicView, packagesView, storageView, freeCodesView, aboutPage });
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
                if (Pending() > 0 && MessageBox.Show(this, "You have changes that aren't applied yet. Close anyway?", Text,
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
                backdrop = Theme.Backdrop(art, ClientSize, header.Height, ClientSize.Height - footer.Height);
            }
            e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
        }

        async Task Start()
        {
            BuildAdvanced();
            game = Settings.GameFolder;
            if (!Game.IsGameFolder(game)) game = Game.Find();
            if (!Game.IsGameFolder(game)) { Opacity = 1; if (!ChooseGame()) { Close(); return; } }
            Settings.GameFolder = game;
            ShowGame();
            await AdoptPackages();
            Reload();   // what's installed, right away: the catalog's items join when it comes
            SetStatus("Loading the catalog...");
            try { catalog = await Catalog.Load(Program.CatalogSource()); catalogProblem = null; }
            catch (Exception ex) { catalog = new List<Item>(); catalogProblem = ex.Message; }
            ShowProgramUpdate();
            Reload();
            communityPage.CheckForUpdates();   // GameBanana's list, read in the background: updates for installed mods
        }

        // packages' zips from before the Mods folder come into it (once; it may take a moment for big zips)
        async Task AdoptPackages()
        {
            if (!Game.IsGameFolder(game)) return;
            string g = game;
            try { await Task.Run(() => Installer.AdoptPackages(g, AppFolders.Mods)); }
            catch (Exception ex) { MessageBox.Show(this, "Some packages couldn't be copied into " + AppFolders.Mods + ": " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            link.LinkClicked += (s, e) => System.Diagnostics.Process.Start(Catalog.ProgramPage);
            tips.SetToolTip(link, "Get the new EX More Stuff: " + Catalog.ProgramPage);
            header.Controls.Add(link);
            link.BringToFront();
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

        void ShowGame()
        {
            gameLabel.Text = "Game folder: " + game;
            musicPage.SetGame(game);
            int width = Math.Min(gameLabel.Width, TextRenderer.MeasureText(gameLabel.Text, gameLabel.Font).Width);
            change.Location = new Point(gameLabel.Left + width + 2, gameLabel.Top);
        }

        void ShowTab(TabButton tab)
        {
            costumesTab.Selected = tab == costumesTab;
            stagesTab.Selected = tab == stagesTab;
            communityTab.Selected = tab == communityTab;
            advancedTab.Selected = tab == advancedTab;
            aboutTab.Selected = tab == aboutTab;
            advancedPage.Visible = tab == advancedTab && openTool == null;
            musicView.Visible = tab == advancedTab && openTool == "music";
            packagesView.Visible = tab == advancedTab && openTool == "packages";
            storageView.Visible = tab == advancedTab && openTool == "storage";
            freeCodesView.Visible = tab == advancedTab && openTool == "freecodes";
            if (!musicView.Visible) musicPage.StopPlaying();
            stagesPage.Visible = tab == stagesTab;
            communityPage.Visible = tab == communityTab;
            aboutPage.Visible = tab == aboutTab;
            rosterPage.Visible = tab == costumesTab && openFighter == null;
            costumePage.Visible = tab == costumesTab && openFighter != null;
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

        bool Wanted(Item item) { bool w; return wanted.TryGetValue(item.Key, out w) ? w : IsInstalled(item.Key); }

        // An installed catalog item whose catalog package is newer: switched on, Apply updates it.
        bool HasUpdate(Item item) { return installed.Any(r => Installer.Outdated(r, item)); }

        bool Changed(Item item) { return Wanted(item) != IsInstalled(item.Key) || (Wanted(item) && HasUpdate(item)); }

        int Pending() { return AllItems().Count(Changed) + ReplacementChanges().Count + Renames().Count; }

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
            Item item = AllItems().FirstOrDefault(i => i.IsStage && i.Code == code);
            return item != null && !string.IsNullOrEmpty(item.Name) ? item.Name : "Custom stage " + code;
        }

        Image StagePicture(string code)
        {
            Image image;
            if (Array.IndexOf(Stages.Codes, code) < 0) return pictures.TryGetValue("stage:" + code, out image) ? image : null;
            if (!stagePictures.TryGetValue(code, out image)) stagePictures[code] = image = Stages.Picture(code);
            return image;
        }

        // Clicking a game stage: choose what plays in its place on this PC (applied with the rest).
        void ChooseReplacement(string stage)
        {
            var sources = AllItems().Where(i => i.IsStage && IsInstalled(i.Key)).OrderBy(i => i.Code).Select(i => i.Code)
                .Concat(StageGrid.Grid.Where(c => c != null && c != stage)).ToList();
            string folder = game;
            using (var dialog = new ReplaceDialog(stage, Replacement(wantedReplaced, stage), sources, StageName, StagePicture,
                       (s, source) => Replacements.Problem(folder, s, source, StageName)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.Choice == null) wantedReplaced.Remove(stage);
                else wantedReplaced[stage] = dialog.Choice;
            }
            stagesPage.Invalidate(true);
            UpdateStatus();
        }

        // A fighter's tile bubbles: their costumes switched off (grey) and on (green), as currently chosen.
        void Count(FighterTile tile, string code)
        {
            var costumes = AllItems().Where(i => !i.IsStage && i.Fighter == code).ToList();
            tile.Active = costumes.Count(Wanted);
            tile.Inactive = costumes.Count - tile.Active;
            tile.Invalidate();
        }

        // Reload after one package changed (deleted, moved): the other changes waiting for Apply stay waiting.
        void ReloadKeeping(string gone)
        {
            var keepWanted = wanted.Where(w => w.Key != gone).ToList();
            var keepNames = wantedNames.Where(w => w.Key != gone).ToList();
            var keepReplaced = wantedReplaced.ToList();
            Reload();
            foreach (var w in keepWanted) wanted[w.Key] = w.Value;
            foreach (var n in keepNames) wantedNames[n.Key] = n.Value;
            // a replacement waiting for Apply whose stage is gone (deleted) is dropped
            var stagesLeft = new HashSet<string>(Stages.Codes.Concat(installed.Concat(removed).Where(r => r.Item.IsStage).Select(r => r.Item.Code)));
            foreach (var r in keepReplaced) if (stagesLeft.Contains(r.Value)) wantedReplaced[r.Key] = r.Value;
            BuildRoster();
            if (openFighter != null) ShowCostumes(openFighter);
            BuildStages();
            UpdateStatus();
        }

        void Reload()
        {
            installed = Game.IsGameFolder(game) ? Installer.Load(game) : new List<Installed>();
            removed = Game.IsGameFolder(game) ? Installer.Removed(game) : new List<Installed>();
            wanted.Clear();
            wantedNames.Clear();
            replaced = Game.IsGameFolder(game) ? Replacements.Load(game).ToDictionary(r => r.Stage, r => r.Source) : new Dictionary<string, string>();
            wantedReplaced.Clear();
            foreach (var r in replaced) wantedReplaced[r.Key] = r.Value;
            BuildRoster();
            if (openFighter != null) ShowCostumes(openFighter);
            BuildStages();
            packagesPage.SetGame(game);
            communityPage.SetGame(game);
            storagePage.SetGame(game);
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
        }

        void ShowCostumes(string code)
        {
            openFighter = code;
            costumeTitle.Text = Fighters.Name(code);
            var cards = AllItems().Where(i => !i.IsStage && i.Fighter == code).OrderBy(i => i.Slot).ThenBy(i => i.Color).Select(Card).ToList();
            int width = ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 16;
            if (cards.Count == 0) cards.Add(ShadowLabel.Note("No custom costumes for " + Fighters.Name(code) + " yet.", width));
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
            Fill(advancedPage, new List<Control> { new Heading("Advanced", width), note, fromFile, music, codes, space, codesFree });
        }

        // Cards flow left to right; a heading, a note or the stage grid has a line to itself.
        static void Fill(FlowLayoutPanel list, List<Control> cards)
        {
            list.SuspendLayout();
            list.Controls.Clear();
            foreach (Control card in cards) list.Controls.Add(card);
            for (int i = 0; i < list.Controls.Count; i++)
                if (!(list.Controls[i] is ItemCard))
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
            if ((record ?? off) != null ? (record ?? off).Source == "file" : item.Personal) by = "Your own package" + (by.Length > 0 ? "  ·  " + by : "");
            var card = new ItemCard
            {
                Title = CardTitle(item, record ?? off),
                Detail = by,
                Note = item.IsStage ? "Others without it see " + Stages.Name(Stages.FallbackCode(item.Code)) : item.IsColor ? "Others without it see color 1" : null,
                Badge = HasUpdate(item) ? (string.IsNullOrEmpty(item.Version) ? "Update" : "Update to " + item.Version) : null,
                Margin = new Padding(6),
            };
            card.Use.Checked = Wanted(item);
            card.Use.CheckedChanged += (s, e) =>
            {
                wanted[item.Key] = card.Use.Checked;
                card.Invalidate();
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
                tips.SetToolTip(card.Trash, "Delete it for good");
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
            return card;
        }

        // The trash can on a card: the package deleted for good (as in Package codes), whatever else is waiting for Apply.
        async Task DeleteItem(Item item)
        {
            Installed record = installed.FirstOrDefault(r => r.Item.Key == item.Key) ?? RemovedRecord(item.Key);
            if (record == null || !GameClosed()) return;
            if (MessageBox.Show(this, PackagesPanel.DeleteQuestion(game, record, Describe(item)), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;
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
            card.Title = CardTitle(item, record);
            card.Invalidate();
            UpdateStatus();
        }

        async Task LoadPicture(Item item, ItemCard card)
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
                        return bytes != null ? Image.FromStream(new MemoryStream(bytes)) : null;
                    });
                    if (image != null) pictures[item.Key] = image;
                }
                catch (Exception) { }
            }
            card.Picture = image;
            card.Invalidate();
        }

        // Whenever nothing is running, the status line also says the program can simply be closed.
        const string Finished = "Finished? You can close EX More Stuff and play.";

        void UpdateStatus(string done = null)
        {
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

        void Lock(bool on)
        {
            locked = on;
            costumesTab.Enabled = stagesTab.Enabled = communityTab.Enabled = advancedTab.Enabled = aboutTab.Enabled = change.Enabled = !on;
            foreach (FlatButton back in backButtons) back.Enabled = !on;
            disableAll.Enabled = !on;
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
            var changes = AllItems().Where(Changed).ToList();
            var problems = new List<string>();
            Busy(true);
            int done = 0;
            foreach (Item item in changes)
            {
                done++;
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
            foreach (var rename in Renames())
            {
                try { Installer.Rename(folder, rename.Key, rename.Value); }
                catch (Exception ex) { problems.Add("Renaming to " + rename.Value + ": " + ex.Message); }
            }
            string round = RoundMusicMod();
            if (round != null) problems.Add(round);
            Busy(false);
            Reload();
            if (problems.Count > 0)
                MessageBox.Show(this, "Some changes didn't go through:\n\n" + string.Join("\n", problems), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else UpdateStatus("Done");
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
            if (!GameClosed()) return;
            string path;
            using (var dialog = new OpenFileDialog { Filter = "Mod files (*.zip; *.rar; *.7z)|*.zip;*.rar;*.7z", Title = "Add a mod from a file" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
            }
            Item item = Installer.Package(path);
            if (item == null)
            {
                ShowTab(communityTab);
                await communityPage.InstallFile(path);
                return;
            }
            try
            {
                // Any slot or code: it only stops at files of that slot EX More Stuff didn't put there (InstallPackage).
                string warning = Seats.Warning(item);   // the game's, taken on GameBanana, or not free: the player's call
                if (warning != null && MessageBox.Show(this, warning + ". Add it anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;
                // a new color of a custom costume shows once that costume is in the game
                string costume = item.Fighter + "_" + item.Slot.ToString("D2");
                if (item.IsColor && item.Slot >= Catalog.FirstCustomCostume && !File.Exists(Path.Combine(Game.FolderFor(game, item), costume + ".obj.emo")) &&
                    MessageBox.Show(this, Fighters.Name(item.Fighter) + " " + item.Slot.ToString("D2") + " isn't in the game, so this color won't show until it is. Add it anyway?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;
                if (IsInstalled(item.Key) && MessageBox.Show(this, Describe(item) + " is already installed. Replace it?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
                Installer.InstallPackage(game, item, AppFolders.KeepPackage(path), "file");   // a copy in Mods\, so the player's file may move
                string round = RoundMusicMod();
                if (round != null) MessageBox.Show(this, "Added, but " + round + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Reload();
                UpdateStatus("Added");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "That package wasn't added: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Everything off at once; it all stays listed (catalog items, and the player's own packages from their zips).
        void DisableEverything()
        {
            if ((installed.Count == 0 && replaced.Count == 0) || !GameClosed()) return;
            string what = (installed.Count > 0 ? "Switch off all " + installed.Count + " costumes and stages EX More Stuff installed" : "") +
                          (installed.Count > 0 && replaced.Count > 0 ? " and put back " : replaced.Count > 0 ? "Put back " : "") +
                          (replaced.Count > 0 ? "the " + replaced.Count + " game stage" + (replaced.Count > 1 ? "s" : "") + " it replaced" : "");
            if (MessageBox.Show(this, what + "? Their files leave the game, but they stay listed, so you can switch them back on. The game's own files are not touched.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
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
            string round = RoundMusicMod();
            if (round != null) problems.Add(round);
            Reload();
            if (problems.Count > 0)
                MessageBox.Show(this, "Some couldn't be switched off:\n\n" + string.Join("\n", problems), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
