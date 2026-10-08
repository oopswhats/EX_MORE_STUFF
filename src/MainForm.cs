using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        readonly Dictionary<string, bool> wanted = new Dictionary<string, bool>();
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

        readonly TabButton costumesTab = new TabButton("Costumes"), stagesTab = new TabButton("Stages"), aboutTab = new TabButton("About");
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(14, 12, 14, 12) };
        readonly Clear rosterPage = new Clear { Dock = DockStyle.Fill };
        readonly Clear costumePage = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly Label costumeTitle = new Label { AutoSize = true, BackColor = Color.Transparent, Font = Theme.Bold(15f), ForeColor = Theme.Text };
        readonly ScrollList costumeCards = new ScrollList { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        readonly ScrollList stagesPage = new ScrollList { Dock = DockStyle.Fill, Visible = false };
        // About: the credits in a small card at the top, leaving the background art in full view.
        readonly Clear aboutPage = new Clear { Dock = DockStyle.Fill, Visible = false };
        readonly Label gameLabel = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(8.5f), AutoEllipsis = true };
        readonly LinkLabel change = new LinkLabel { Text = "Change", AutoSize = true, BackColor = Color.Transparent, Font = Theme.Font(8.5f),
                                                    LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline };
        readonly Label status = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Bold(9.5f), AutoEllipsis = true };
        readonly Strip header = new Strip { Height = 76 }, footer = new Strip { Height = 70, LineOnTop = true };
        readonly ProgressLine progress = new ProgressLine();
        readonly ToolTip tips = new ToolTip();
        readonly FlatButton apply = new FlatButton("Apply changes", true) { Enabled = false };
        readonly FlatButton addFile = new FlatButton("Add from file...");
        readonly FlatButton removeAll = new FlatButton("Remove everything");

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
            var tagline = new Label { Text = "Custom costumes and stages\nfor Ultra Street Fighter IV", AutoSize = true, BackColor = Color.Transparent,
                                      Font = Theme.Font(9f), ForeColor = Theme.Muted, Location = new Point(divider.Right + 12, 21) };
            aboutTab.Location = new Point(ClientSize.Width - aboutTab.Width - 18, 26);
            stagesTab.Location = new Point(aboutTab.Left - stagesTab.Width - 4, 26);
            costumesTab.Location = new Point(stagesTab.Left - costumesTab.Width - 4, 26);
            costumesTab.Selected = true;
            costumesTab.Click += (s, e) => ShowTab(costumesTab);
            stagesTab.Click += (s, e) => ShowTab(stagesTab);
            aboutTab.Click += (s, e) => ShowTab(aboutTab);
            header.Controls.AddRange(new Control[] { logo, title, divider, tagline, costumesTab, stagesTab, aboutTab });

            // Footer: status, the game folder, buttons.
            footer.Dock = DockStyle.Bottom;
            progress.Bounds = new Rectangle(0, 0, ClientSize.Width, 3);
            apply.Location = new Point(ClientSize.Width - apply.Width - 18, 17);
            removeAll.Location = new Point(apply.Left - removeAll.Width - 10, 17);
            addFile.Location = new Point(removeAll.Left - addFile.Width - 10, 17);
            status.Bounds = new Rectangle(18, 14, addFile.Left - 36, 20);
            gameLabel.Bounds = new Rectangle(18, 36, addFile.Left - 100, 18);
            change.LinkClicked += (s, e) => ChooseGame();
            footer.Controls.AddRange(new Control[] { progress, status, gameLabel, change, addFile, removeAll, apply });
            addFile.Click += (s, e) => AddFromFile();
            removeAll.Click += (s, e) => RemoveEverything();
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

            var credits = new AboutCard(Theme.Resource("logo.png"), wordmark, typeof(MainForm).Assembly.GetName().Version.ToString());
            aboutPage.Controls.Add(credits);
            aboutPage.Resize += (s, e) => credits.Location = new Point((aboutPage.Width - credits.Width) / 2, 8);

            content.Controls.AddRange(new Control[] { rosterPage, costumePage, stagesPage, aboutPage });
            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
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
            game = Settings.GameFolder;
            if (!Game.IsGameFolder(game)) game = Game.Find();
            if (!Game.IsGameFolder(game) && !ChooseGame()) { Close(); return; }
            Settings.GameFolder = game;
            ShowGame();
            SetStatus("Loading the catalog...");
            try { catalog = await Catalog.Load(Program.CatalogSource()); catalogProblem = null; }
            catch (Exception ex) { catalog = new List<Item>(); catalogProblem = ex.Message; }
            ShowProgramUpdate();
            Reload();
        }

        // A newer EX More Stuff in the catalog: a link to its page in the header (nothing is downloaded by itself).
        void ShowProgramUpdate()
        {
            if (Catalog.ProgramVersion == null || Catalog.ProgramVersion <= typeof(MainForm).Assembly.GetName().Version) return;
            var link = new LinkLabel
            {
                Text = "Version " + Catalog.ProgramVersion + " is out  ›", AutoSize = true, BackColor = Color.Transparent,
                Font = Theme.Bold(9.5f), LinkColor = Theme.GoodText, ActiveLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline,
            };
            link.Location = new Point(costumesTab.Left - TextRenderer.MeasureText(link.Text, link.Font).Width - 24, 30);
            link.LinkClicked += (s, e) => System.Diagnostics.Process.Start(Catalog.ProgramPage);
            tips.SetToolTip(link, "Get the new EX More Stuff: " + Catalog.ProgramPage);
            header.Controls.Add(link);
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
            int width = Math.Min(gameLabel.Width, TextRenderer.MeasureText(gameLabel.Text, gameLabel.Font).Width);
            change.Location = new Point(gameLabel.Left + width + 2, gameLabel.Top);
        }

        void ShowTab(TabButton tab)
        {
            costumesTab.Selected = tab == costumesTab;
            stagesTab.Selected = tab == stagesTab;
            aboutTab.Selected = tab == aboutTab;
            stagesPage.Visible = tab == stagesTab;
            aboutPage.Visible = tab == aboutTab;
            rosterPage.Visible = tab == costumesTab && openFighter == null;
            costumePage.Visible = tab == costumesTab && openFighter != null;
        }

        // Everything the pages show: the catalog plus what's installed (players' own packages included).
        IEnumerable<Item> AllItems()
        {
            var keys = new HashSet<string>(catalog.Select(i => i.Key));
            return catalog.Concat(installed.Where(r => !keys.Contains(r.Item.Key)).Select(r => r.Item));
        }

        bool IsInstalled(string key) { return installed.Any(r => r.Item.Key == key); }

        bool Wanted(Item item) { bool w; return wanted.TryGetValue(item.Key, out w) ? w : IsInstalled(item.Key); }

        // An installed catalog item whose catalog package is newer: switched on, Apply updates it.
        bool HasUpdate(Item item) { return installed.Any(r => Installer.Outdated(r, item)); }

        bool Changed(Item item) { return Wanted(item) != IsInstalled(item.Key) || (Wanted(item) && HasUpdate(item)); }

        int Pending() { return AllItems().Count(Changed) + ReplacementChanges().Count; }

        string Replacement(Dictionary<string, string> map, string stage) { string s; return map.TryGetValue(stage, out s) ? s : null; }

        // The game stages whose replacement was changed and not applied yet.
        List<string> ReplacementChanges()
        {
            return replaced.Keys.Union(wantedReplaced.Keys).Where(s => Replacement(replaced, s) != Replacement(wantedReplaced, s)).ToList();
        }

        // A stage's name: the game's, or a custom stage's from the catalog (C12 = custom stage 12).
        string StageName(string code)
        {
            if (Array.IndexOf(Stages.Codes, code) >= 0) return Stages.Name(code);
            Item item = AllItems().FirstOrDefault(i => i.IsStage && "C" + i.Slot.ToString("D2") == code);
            return item != null && !string.IsNullOrEmpty(item.Name) ? item.Name : "Custom stage " + code.Substring(1).TrimStart('0');
        }

        Image StagePicture(string code)
        {
            Image image;
            if (Array.IndexOf(Stages.Codes, code) < 0)
                return pictures.TryGetValue("stage:" + int.Parse(code.Substring(1)), out image) ? image : null;
            if (!stagePictures.TryGetValue(code, out image)) stagePictures[code] = image = Stages.Picture(code);
            return image;
        }

        // Clicking a game stage: choose what plays in its place on this PC (applied with the rest).
        void ChooseReplacement(string stage)
        {
            var sources = AllItems().Where(i => i.IsStage && IsInstalled(i.Key)).OrderBy(i => i.Slot).Select(i => "C" + i.Slot.ToString("D2"))
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

        void Reload()
        {
            installed = Game.IsGameFolder(game) ? Installer.Load(game) : new List<Installed>();
            wanted.Clear();
            replaced = Game.IsGameFolder(game) ? Replacements.Load(game).ToDictionary(r => r.Stage, r => r.Source) : new Dictionary<string, string>();
            wantedReplaced.Clear();
            foreach (var r in replaced) wantedReplaced[r.Key] = r.Value;
            BuildRoster();
            if (openFighter != null) ShowCostumes(openFighter);
            BuildStages();
            UpdateStatus();
        }

        // The fighters in alphabetical order, as their character-select portraits.
        void BuildRoster()
        {
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
            var cards = AllItems().Where(i => !i.IsStage && i.Fighter == code).OrderBy(i => i.Slot).Select(Card).ToList();
            if (cards.Count == 0) cards.Add(Note("No custom costumes for " + Fighters.Name(code) + " yet."));
            Fill(costumeCards, cards);
            rosterPage.Visible = false;
            costumePage.Visible = costumesTab.Selected;
        }

        void BuildStages()
        {
            int width = ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 16;
            var cards = new List<Control> { new Heading("Custom stages", width) };
            var custom = AllItems().Where(i => i.IsStage).OrderBy(i => i.Slot).ToList();
            if (custom.Count == 0) cards.Add(Note("No custom stages yet."));
            cards.AddRange(custom.Select(Card));
            cards.Add(new Heading("The game's stages", width));
            cards.Add(Note("Click a stage to replace it with another one. Only you see it, online too, and its music stays the same."));
            var grid = new StageGrid(width) { Margin = new Padding(6, 0, 6, 16) };
            grid.ReplacedBy = code => { string source = Replacement(wantedReplaced, code); return source == null ? null : StageName(source); };
            grid.StageClicked += ChooseReplacement;
            cards.Add(grid);
            Fill(stagesPage, cards);
        }

        static Control Note(string text)
        {
            return new Label { Text = text, AutoSize = true, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(10f), Margin = new Padding(8, 12, 8, 12) };
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
            string by = string.Join("  ·  ", new[] { item.Author, item.Version }.Where(s => !string.IsNullOrEmpty(s)));
            if (item.Personal) by = "Your own package" + (by.Length > 0 ? "  ·  " + by : "");
            var card = new ItemCard
            {
                Title = item.Title,
                Detail = by,
                Note = item.IsStage ? "Others without it see " + Stages.Name(Stages.FallbackCode(item.Slot)) : null,
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
            new ToolTip().SetToolTip(card, string.IsNullOrEmpty(item.Description) ? item.Title : item.Description);
            var ignored = LoadPicture(item, card);
            return card;
        }

        async Task LoadPicture(Item item, ItemCard card)
        {
            Image image;
            if (!pictures.TryGetValue(item.Key, out image))
            {
                try
                {
                    byte[] bytes = null;
                    if (!string.IsNullOrEmpty(item.Picture)) bytes = await Catalog.ReadBytes(item.Picture);
                    else
                    {
                        // A player's own package: its picture is among the installed files.
                        string folder = Game.FolderFor(game, item);
                        foreach (string name in new[] { item.Prefix + ".png", item.Prefix + "_01.png", item.Prefix + ".jpg" })
                            if (File.Exists(Path.Combine(folder, name))) { bytes = File.ReadAllBytes(Path.Combine(folder, name)); break; }
                    }
                    if (bytes != null) { image = Image.FromStream(new MemoryStream(bytes)); pictures[item.Key] = image; }
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
            apply.Enabled = pending > 0;
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

        void Busy(bool busy)
        {
            apply.Enabled = addFile.Enabled = removeAll.Enabled = !busy;
            content.Enabled = costumesTab.Enabled = stagesTab.Enabled = aboutTab.Enabled = change.Enabled = !busy;
            progress.Value = busy ? 0 : -1;
            UseWaitCursor = busy;
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
            Busy(false);
            Reload();
            if (problems.Count > 0)
                MessageBox.Show(this, "Some changes didn't go through:\n\n" + string.Join("\n", problems), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else UpdateStatus("Done");
        }

        static string Describe(Item item)
        {
            return (item.IsStage ? "" : Fighters.Name(item.Fighter) + " ") + item.Title;
        }

        void AddFromFile()
        {
            if (!GameClosed()) return;
            using (var dialog = new OpenFileDialog { Filter = "Costume or stage package (*.zip)|*.zip", Title = "Add your own costume or stage" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Item item = Installer.Inspect(dialog.FileName);
                    if (item == null) throw new InvalidDataException("this isn't a costume or stage package (no <FIGHTER>_<NN>.obj.emo or STG_C<NN>.emz in it)");
                    if (!item.Personal)
                        throw new InvalidDataException(string.Format("it uses number {0}; packages you add yourself use 71-99 (the catalog uses the rest)", item.Slot));
                    if (IsInstalled(item.Key) && MessageBox.Show(this, Describe(item) + " is already installed. Replace it?", Text,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
                    Installer.InstallPackage(game, item, dialog.FileName, "file");
                    Reload();
                    UpdateStatus("Added");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "That package wasn't added: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void RemoveEverything()
        {
            if ((installed.Count == 0 && replaced.Count == 0) || !GameClosed()) return;
            string what = (installed.Count > 0 ? "Remove all " + installed.Count + " costumes and stages EX More Stuff installed" : "") +
                          (installed.Count > 0 && replaced.Count > 0 ? " and put back " : replaced.Count > 0 ? "Put back " : "") +
                          (replaced.Count > 0 ? "the " + replaced.Count + " game stage" + (replaced.Count > 1 ? "s" : "") + " it replaced" : "");
            if (MessageBox.Show(this, what + "? The game's own files are not touched.",
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
            Reload();
            if (problems.Count > 0)
                MessageBox.Show(this, "Some couldn't be removed:\n\n" + string.Join("\n", problems), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
