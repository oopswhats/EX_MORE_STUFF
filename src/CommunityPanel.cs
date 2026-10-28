using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // The Browse Mods tab: USF4's skins and stages on GameBanana, made and shared for free. Respect to the modders: each
    // card names its maker in gold (a click shows all their mods), and a tiny blue icon opens the mod's page. A skin installs in one click into its shared code
    // (SharedCodes), built with this PC's game files; a stage into a stage code of its own (ModFile), with its music.
    class CommunityPanel : Clear
    {
        public event Action Changed;          // EX More Stuff's lists need reading again
        public event Action<bool> Working;    // an install started or ended (the rest of the window waits)
        public Func<string> Blocked;          // why nothing can install right now, or null
        public Func<Task> BeforeInstall;      // the window's first-time PC check (PerfTest), before anything goes in
        public event Action<int> UpdatesChanged;   // how many installed mods have a newer version on GameBanana (the tab's dot)
        public event Action<bool> NewFromFavorites;   // Notifications has something not seen yet (the tab's heart)

        // one card's mod
        sealed class Entry
        {
            public GameBanana.Mod Mod;
            public bool Stage;
            public HashSet<string> Fighters;   // who it's for, from its files
            public string Words;               // what a search looks in: title, fighters
            public DateTime Date { get { return DateTimeOffset.FromUnixTimeSeconds(Mod.Added).LocalDateTime; } }
            public string Key { get { return "gamebanana:" + Mod.Id + ":"; } }
        }

        string game;
        List<GameBanana.Mod> mods;
        Dictionary<string, GameBanana.Mod> holders = new Dictionary<string, GameBanana.Mod>();
        List<Entry> entries = new List<Entry>();
        string problem;
        bool loaded, loading, working, quiet;
        // the order: by date (newest or oldest first) or by downloads (most or least first); each button remembers its way
        bool byDownloads, newestFirst = true, mostFirst = true;
        int shownCount = PageSize, filledWidth, topWidth;
        const int PageSize = 36;
        string pointKey;   // a card to scroll to and light up (Highlight)
        Tuple<long, string> creatorOf;   // a creator to show once the list is in: a mod of theirs and their name (ShowCreatorOf)
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill, AllowDrop = true };
        static readonly Dictionary<string, Task<Image>> thumbnails = new Dictionary<string, Task<Image>>();
        readonly ToolTip tips = new ToolTip { InitialDelay = 1000 };

        // the top of the page stays while the cards change, so the search box keeps its place as you type
        Heading heading;
        ShadowLabel note;
        readonly Clear tools = new Clear { Height = 40, Margin = new Padding(8, 2, 8, 2) };
        readonly TextBox search = new TextBox { Width = 150, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10.5f) };
        readonly FlatButton stagesButton = new FlatButton("Stages"), characterButton = new FlatButton("Costumes  ▾");
        readonly FlatButton musicButton = new FlatButton("Music");   // song mods only (user 2026-10-16)
        // only what isn't installed, on top of any other pick (user 2026-10-16: "another search option ... for mods that
        // are not installed")
        readonly FlatButton notInstalledButton = new FlatButton("Not Installed");
        bool notInstalledOnly;
        HashSet<string> installedKeys = new HashSet<string>();
        // Refresh here too, left of Stages, in our orange (user 2026-10-14): the window's Refresh (RefreshAll), else GameBanana's list again
        readonly FlatButton refreshButton = new FlatButton("Refresh", true);
        public Func<Task> RefreshAll;
        // while it works (pressed, or GameBanana's list being read): "Refreshing." ".." "..." (user 2026-10-14: "make it more obvious
        // when its refreshing"), clicks waiting until it's done
        bool refreshPressed;
        readonly Timer refreshDots = new Timer { Interval = 350 };
        int dots;

        void ShowRefresh()
        {
            bool busy = refreshPressed || loading;
            if (busy && !refreshDots.Enabled) { dots = 0; refreshDots.Start(); }
            if (!busy) refreshDots.Stop();
            refreshButton.Text = busy ? "Refreshing" + new string('.', dots % 3 + 1) : "Refresh";
            refreshButton.Invalidate();
        }
        readonly FlatButton creatorsButton = new FlatButton("Creators  ▾"), notificationsButton = new FlatButton("♥  Notifications");
        ToolStripDropDown creatorsPicking;   // the Creators list while it's open
        int creatorsClosedAt;
        bool creatorsClosedOnButton;
        List<GameBanana.Mod> news = new List<GameBanana.Mod>();   // Notifications: new mods from the favorite creators
        HashSet<long> newIds = new HashSet<long>();
        bool? newTold;
        readonly FlatButton byDate = new FlatButton("Newest", true), byCount = new FlatButton("Most Downloaded");
        readonly FlatButton updatesButton = new FlatButton("Updates"), updateAll = new FlatButton("Update all", true) { Margin = new Padding(8, 4, 8, 6), Fill = Theme.Update };
        HashSet<string> outdated = new HashSet<string>();   // installed mods with a newer version (their keys)
        int updatesTold = -1;
        bool chaining, stopChain;   // Update all running, and asked to stop after the one being updated
        string pick;   // what's shown: everything (null), "stages", "updates", a fighter's code, "favorites", "notifications", "creator:<key>" or "collection:<id>"
        bool CreatorPicked { get { return pick != null && pick.StartsWith("creator:"); } }
        // Collections (user 2026-10-14): a creator's GameBanana collection of their own mods (GameBanana.Collections), its
        // mods as cards with Install all / New Mods, which install each in turn as Update all does
        readonly FlatButton collectionsButton = new FlatButton("Mod Packs") { Visible = false };   // GameBanana's collections, called Mod Packs (user 2026-10-14)
        readonly FlatButton collectionAction = new FlatButton("Install all", true) { Margin = new Padding(8, 4, 8, 6) };
        // Notifications' Install all (user 2026-10-17: "install all for your notifications"): its new mods not in yet, queued
        readonly FlatButton notifyAction = new FlatButton("Install all", true) { Margin = new Padding(8, 4, 8, 6) };
        bool notifyChain;
        ShadowLabel collectionNote;
        bool CollectionPicked { get { return pick != null && pick.StartsWith("collection:"); } }
        GameBanana.Collection PickedCollection
        {
            get { long id; return CollectionPicked && long.TryParse(pick.Substring(11), out id) ? GameBanana.Collections.FirstOrDefault(c => c.Id == id) : null; }
        }
        ToolStripDropDown picking;   // the Character list while it's open
        int pickerClosedAt;
        bool closedOnButton;
        public Func<string, Image> Portrait;   // the roster's portraits, for the Character list
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, ForeColor = Theme.Text, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4) };
        readonly Timer typing = new Timer { Interval = 300 };

        public CommunityPanel()
        {
            previewEnded.Tick += (s, e) => { if (previewPlayer.Playing) return; previewEnded.Stop(); previewPlaying = null; ShowPreviewButtons(); };
            Controls.Add(list);
            AllowDrop = true;
            foreach (Control target in new Control[] { this, list })
            {
                target.DragEnter += (s, e) => e.Effect = Dropped(e) != null && !working ? DragDropEffects.Copy : DragDropEffects.None;
                target.DragDrop += async (s, e) => { string file = Dropped(e); if (file != null) await InstallFile(file); };
            }
            // Stages: only stages (again: everything); Character: the list of fighters to pick one from
            stagesButton.Click += (s, e) => { pick = pick == "stages" ? null : "stages"; Refilter(); };
            musicButton.Click += (s, e) => { pick = pick == "music" ? null : "music"; Refilter(); };
            notInstalledButton.Click += (s, e) => { notInstalledOnly = !notInstalledOnly; Refilter(); };
            // Costumes (user 2026-10-17: "the costumes button doesnt show all costumes when you click it"): every costume, and
            // the fighters' list opens to narrow it to one; again while it's open: it closes; again on all costumes: off.
            // (A click on the button first closes the list as a click outside it, so a close that just happened there is
            // that click's doing.)
            characterButton.Click += (s, e) =>
            {
                if (picking != null) { picking.Close(); return; }
                if (closedOnButton && Environment.TickCount - pickerClosedAt < 400) { closedOnButton = false; return; }
                if (pick == "costumes") { pick = null; Refilter(); return; }
                pick = "costumes";
                Refilter();
                picking = CharacterPicker.Show(characterButton, null, Portrait, code => entries.Count(m => m.Fighters.Contains(code)),
                                               code => { pick = code; Refilter(); });
                picking.Closed += (s2, e2) =>
                {
                    picking = null;
                    pickerClosedAt = Environment.TickCount;
                    closedOnButton = characterButton.ClientRectangle.Contains(characterButton.PointToClient(Cursor.Position));
                };
            };
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search mods");   // EM_SETCUEBANNER: the grey hint
            search.TextChanged += (s, e) => { if (!quiet) { typing.Stop(); typing.Start(); } };
            typing.Tick += (s, e) => { typing.Stop(); Refilter(); };
            // two sort buttons: the other one switches to it; the one in use turns around (Newest <-> Oldest, Most <-> Least Downloaded)
            byDate.Click += (s, e) => { if (byDownloads) byDownloads = false; else newestFirst = !newestFirst; Refilter(); };
            byCount.Click += (s, e) => { if (!byDownloads) byDownloads = true; else mostFirst = !mostFirst; Refilter(); };
            // Updates: only the installed mods with a newer version (again: everything); Update all: each in turn
            updatesButton.Click += (s, e) => { pick = pick == "updates" ? null : "updates"; Refilter(); };
            updateAll.Click += async (s, e) => await UpdateAll();
            // Creators: the list of creators (Favorites first) to pick from, as Character's; Notifications: what's new from them
            creatorsButton.Click += (s, e) =>
            {
                if (creatorsPicking != null) { creatorsPicking.Close(); return; }
                if (creatorsClosedOnButton && Environment.TickCount - creatorsClosedAt < 400) { creatorsClosedOnButton = false; return; }
                if (pick == CreatorPicker.FavoritesKey || CreatorPicked) { pick = null; Refilter(); }
                creatorsPicking = CreatorPicker.Show(creatorsButton, CreatorList(), key => { pick = key == CreatorPicker.FavoritesKey ? key : "creator:" + key; Refilter(); });
                creatorsPicking.Closed += (s2, e2) =>
                {
                    creatorsPicking = null;
                    creatorsClosedAt = Environment.TickCount;
                    creatorsClosedOnButton = creatorsButton.ClientRectangle.Contains(creatorsButton.PointToClient(Cursor.Position));
                };
            };
            notificationsButton.Click += (s, e) => { if (pick == "notifications") { pick = null; Refilter(); } else ShowNotifications(); };
            tips.SetToolTip(notificationsButton, "New mods from your favorite creators: the last 6 months, not installed");
            // Mod Packs: a page of the packs, a card each (user 2026-10-14: "open it like another browser, each pack gets its own
            // card"); pressed again, or from a pack's mods: everything
            collectionsButton.Click += (s, e) => { pick = pick == "packs" || CollectionPicked ? null : "packs"; Refilter(); list.AutoScrollPosition = Point.Empty; };
            collectionAction.Click += async (s, e) => { if (PickedCollection != null) await InstallCollection(PickedCollection); };
            notifyAction.Click += async (s, e) => await InstallNotified();
            refreshButton.Click += async (s, e) =>
            {
                if (refreshPressed || loading) return;
                refreshPressed = true;
                ShowRefresh();
                try { if (RefreshAll != null) await RefreshAll(); else ReadAgain(); }
                finally { refreshPressed = false; ShowRefresh(); }
            };
            refreshDots.Tick += (s, e) => { dots++; ShowRefresh(); };
            // a new file's 2 minutes are up: its card offers it (not mid-install: a moment later)
            settled.Tick += (s, e) =>
            {
                settled.Stop();
                if (working || loading) { settled.Interval = 3000; settled.Start(); }
                else if (Visible) Fill(); else { stale = true; Recount(); }   // the tab's dot told too
            };
            tools.Controls.AddRange(new Control[] { search, refreshButton, stagesButton, characterButton, musicButton, notInstalledButton, creatorsButton, collectionsButton, notificationsButton, updatesButton, byDate, byCount });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        static string Dropped(DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            return files != null && files.Length == 1 && Regex.IsMatch(files[0], @"\.(zip|rar|7z)$", RegexOptions.IgnoreCase) ? files[0] : null;
        }

        bool stale;   // the lists changed while the page was hidden: its cards are built when it's shown
        string standing;   // how GameBanana answered (a warning that stays), or null

        public void SetGame(string folder)
        {
            game = folder;
            if (Visible) Fill(); else { stale = true; Recount(); }
        }

        /// <summary>Reads GameBanana's list now (at start), so updates for installed mods show before the tab is opened.</summary>
        public void CheckForUpdates()
        {
            if (!loaded && !loading) { var ignored = Load(); }
        }

        /// <summary>The About tab's NSFW switch changed: the cards made again with or without 18+ mods.</summary>
        public void NsfwChanged()
        {
            if (mods == null) return;
            entries = Merge();
            if (Visible) Fill(); else { stale = true; Recount(); }
        }

        /// <summary>Reads GameBanana's list again (the footer's Refresh; user 2026-10-12: a mod just uploaded didn't come
        /// up): new uploads and files GameBanana has finished checking since show up.</summary>
        public void ReadAgain()
        {
            if (!loading && !working) { var ignored = Load(true); }
        }

        // The player changed what's shown (filter, search, sort): from the top, and what an install said goes.
        void Refilter()
        {
            shownCount = PageSize;
            ClearSaid();
            Fill();
        }

        // What an install or update said goes (not while one runs); how GameBanana answered stays.
        void ClearSaid() { if (!working) Say(standing ?? "", standing != null); }

        /// <summary>A GameBanana 1-Click link's mod (OneClick): its card shown and installed as its Install button does it
        /// (updated when a newer one's out; nothing when it's in already). A mod not in the list read yet: the list read again.</summary>
        public async Task InstallFromLink(long id)
        {
            while (loading) await Task.Delay(200);
            if (!loaded) await Load();
            GameBanana.Mod mod = mods == null ? null : mods.FirstOrDefault(m => m.Id == id);
            if (mod == null && mods != null) { await Load(true); mod = mods == null ? null : mods.FirstOrDefault(m => m.Id == id); }   // a new upload
            if (mod == null) { Say("That mod isn't one EX More Stuff can install yet. Try again in a few minutes", true); return; }
            string key = "gamebanana:" + mod.Id + ":";
            Highlight(key);
            Recount();
            if (installedKeys.Contains(key) && !outdated.Contains(key)) { Say(mod.Name + " is already installed"); return; }
            if (Wanted(mod).Count == 0) { Say(mod.Name + ": GameBanana is still checking its file. Try again in a few minutes", true); return; }
            if (await Install(mod)) Fill();
        }

        /// <summary>Shows a mod's card (its key: "gamebanana:123:"), lit up for a moment.</summary>
        public void Highlight(string key)
        {
            pointKey = key;
            quiet = true;
            search.Text = "";
            pick = null;
            quiet = false;
            if (!loaded && !loading) { var ignored = Load(); return; }   // it shows once the list is in
            Fill();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { ClearSaid(); previewPlayer.Stop(); }   // a preview stops when the page is left
            if (Visible && !loaded && !loading) { var ignored = Load(); }
            else if (Visible && stale) Fill();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (game != null && Visible && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        async Task Load(bool again = false)
        {
            loading = true;
            ShowRefresh();
            Say("Reading GameBanana...");
            try { mods = await (again ? GameBanana.Again() : GameBanana.All()); holders = SharedCodes.Holders(mods); problem = null; }
            catch (Exception ex) { mods = null; problem = ex.Message; }
            loading = false;
            ShowRefresh();
            loaded = mods != null;
            entries = Merge();
            if (creatorOf != null) { PickCreator(creatorOf.Item1, creatorOf.Item2); creatorOf = null; }
            // read at start for the updates dot: the cards wait until the page is opened
            if (Visible) Fill(); else { stale = true; Recount(); }
            standing = problem != null ? "GameBanana isn't answering right now: check your internet connection, then try again later"
                     : GameBanana.Stale ? "GameBanana isn't answering right now, so this is its list " + Kept() + ". Installing needs it back" : null;
            if (standing != null) Say(standing, true);
            else if (status.ForeColor != Theme.Warning) Say("");
        }

        // GameBanana's skins, and its mods with stage files (STG_xxx.emz) in them
        static readonly Regex StageName = new Regex(@"^(STG_[A-Za-z0-9]{3}(\.tex)?\.emz|BGM_[A-Za-z0-9]{3}[23]?\.csb)$", RegexOptions.IgnoreCase);

        static bool StageFile(string path) { return StageName.IsMatch(path.Substring(path.LastIndexOf('/') + 1)); }

        // the stages a mod's files are made from (TRN, or codes of their own)
        static IEnumerable<string> StageCodes(GameBanana.Mod mod)
        {
            string own = Overrides.OwnCode(mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents));   // code_<CODE>.txt
            if (own != null) return new[] { own };
            return mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents).Select(c => c.Substring(c.LastIndexOf('/') + 1))
                .Where(n => n.StartsWith("STG_", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".emz", StringComparison.OrdinalIgnoreCase) && n.Length >= 11)
                .Select(n => n.Substring(4, 3).ToUpperInvariant()).Distinct();
        }

        // a song mod (user 2026-10-16: GameBanana's "Music" section for USF4): music files (BGM_*.csb) and no stage or
        // costume files, whatever section it's in
        static readonly Regex SongFile = new Regex(@"^BGM_.+\.csb$", RegexOptions.IgnoreCase);
        static readonly Regex SongExtra = new Regex(@"\.(txt|md|rtf|pdf|nfo|url|html?|png|jpe?g|gif|webp|bmp)$", RegexOptions.IgnoreCase);   // what may come with songs

        static bool IsSong(GameBanana.Mod mod)
        {
            var names = mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents).Select(c => c.Substring(c.LastIndexOf('/') + 1)).ToList();
            // songs and nothing else of the game's (user 2026-10-16: HazelNutAzazel's "Super Street Fighter IV Restoration"
            // has BGM banks but also the menus' pictures and the intro, and showed as MUSIC): a readme or a picture may come along
            return names.Any(n => SongFile.IsMatch(n)) && names.All(n => SongFile.IsMatch(n) || SongExtra.IsMatch(n) || n == "") &&
                   !HasStage(mod) && !SkinImport.Named(names).Any();
        }

        // an effects mod: a fighter's effects files and no costume or stage (Skins > Other/Misc on GameBanana)
        static bool IsEffects(GameBanana.Mod mod)
        {
            var contents = mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents).ToList();
            return contents.Any(Effects.IsFile) && !SkinImport.Named(contents).Any() && !HasStage(mod);
        }

        static bool HasStage(GameBanana.Mod mod)
        {
            return mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents).Any(c => StageFile(c) && c.Substring(c.LastIndexOf('/') + 1).StartsWith("STG_", StringComparison.OrdinalIgnoreCase));
        }

        static string Kept() { return GameBanana.ReadAt == DateTime.MinValue ? "as EX More Stuff came with it" : "from " + GameBanana.ReadAt.ToString("d MMM yyyy"); }

        List<Entry> Merge()
        {
            if (mods == null) return new List<Entry>();
            bool hide = Settings.HideNsfw;   // 18+ mods left out when the player hides them (the About tab)
            return mods.Where(m => (m.IsSkin || HasStage(m) || IsSong(m)) && !(hide && m.IsNsfw)).Select(m =>
            {
                var contents = m.Files.SelectMany(f => f.Contents).ToList();
                var effects = Effects.FightersIn(contents);   // a fighter's effects (user 2026-10-18)
                var e = new Entry { Mod = m, Stage = HasStage(m), Fighters = new HashSet<string>(SkinImport.Named(contents).Select(c => c.Item1).Concat(effects)) };
                e.Words = (m.Name + " " + string.Join(" ", e.Fighters.Select(ExMoreStuff.Fighters.Name)) + (e.Stage ? " stage" : "") + (IsSong(m) ? " music song" : "") +
                           (effects.Count > 0 ? " effects" : "")).ToLowerInvariant();
                return e;
            }).ToList();
        }

        // the cards to show: what Stages / Character picks and the search finds (every word; a modder by their whole name),
        // in the chosen order
        List<Entry> Shown()
        {
            string query = search.Text.Trim().ToLowerInvariant();
            string[] words = Regex.Split(query, @"\s+").Where(w => w != "").ToArray();
            var shown = entries.Where(e => pick == null || (pick == "packs" ? false : pick == "stages" ? e.Stage : pick == "music" ? IsSong(e.Mod) : pick == "costumes" ? e.Fighters.Count > 0 : pick == "updates" ? outdated.Contains(e.Key) :
                                               pick == CreatorPicker.FavoritesKey ? Creators.IsFavorite(e.Mod) : pick == "notifications" ? newIds.Contains(e.Mod.Id) :
                                               CreatorPicked ? Creators.Key(e.Mod) == pick.Substring(8) :
                                               CollectionPicked ? PickedCollection != null && PickedCollection.Mods.Contains(e.Mod.Id) : e.Fighters.Contains(pick)))
                               .Where(e => !notInstalledOnly || !installedKeys.Contains(e.Key))
                               .Where(e => words.All(w => e.Words.Contains(w)) || (query != "" && Squeezed(e.Mod.Author) == Squeezed(query)));
            if (pick == "notifications") return shown.OrderByDescending(e => e.Date).ToList();   // what's new: newest first
            if (byDownloads) return (mostFirst ? shown.OrderByDescending(e => e.Mod.Downloads) : shown.OrderBy(e => e.Mod.Downloads)).ToList();
            return (newestFirst ? shown.OrderByDescending(e => e.Date) : shown.OrderBy(e => e.Date)).ToList();
        }

        // which installed mods have a newer version on GameBanana; the tab's dot is told when that number changes
        Dictionary<string, InGame> Recount()
        {
            var installed = Installed();
            installedKeys = new HashSet<string>(installed.Keys);
            outdated = new HashSet<string>(entries.Where(e =>
            {
                InGame g;
                // an update only once GameBanana has passed its new files (a file it's still checking isn't one yet)
                return installed.TryGetValue(e.Key, out g) && Wanted(e.Mod).Count > 0 && !g.Versions.All(v => v == "" || v == Signature(e.Mod));
            }).Select(e => e.Key));
            // a collection installed from (one of its mods in): its mods not in yet are new too, for the purple dot (user 2026-10-14)
            var listedIds = new HashSet<long>(entries.Select(e => e.Mod.Id));
            int collectionNew = GameBanana.Collections.Where(c => c.Mods.Any(id => installed.ContainsKey("gamebanana:" + id + ":")))
                .SelectMany(c => c.Mods).Distinct().Count(id => listedIds.Contains(id) && !installed.ContainsKey("gamebanana:" + id + ":"));
            if (outdated.Count + collectionNew != updatesTold)
            {
                updatesTold = outdated.Count + collectionNew;
                var told = UpdatesChanged;
                if (told != null) told(updatesTold);
            }
            news = Creators.New(entries.Select(e => e.Mod), m => installed.ContainsKey("gamebanana:" + m.Id + ":"), DateTime.Now);
            newIds = new HashSet<long>(news.Select(m => m.Id));
            bool unseen = Creators.Unseen(news);
            if (unseen != newTold)
            {
                newTold = unseen;
                var tell = NewFromFavorites;
                if (tell != null) tell(unseen);
            }
            return installed;
        }

        static string Squeezed(string text) { return Regex.Replace((text ?? "").ToLowerInvariant(), @"\s+", ""); }

        // which mods are in (installed or switched off): what they became, by card
        sealed class InGame
        {
            public readonly List<string> Into = new List<string>();      // "Blanka 08", "stage C72"
            public readonly List<string> Versions = new List<string>();  // the GameBanana files each was built from (Version)
            public readonly List<Installed> Records = new List<Installed>(); // those in the game now (for their size warnings)
        }

        Dictionary<string, InGame> Installed()
        {
            var result = new Dictionary<string, InGame>();
            if (!Game.IsGameFolder(game)) return result;
            var inGame = Installer.Load(game);
            foreach (Installed r in inGame.Concat(Installer.Removed(game)))
            {
                Match m = Regex.Match(r.Item.Id ?? "", @"^(gamebanana:[^:]+:)");
                if (!m.Success) continue;
                string key = m.Groups[1].Value;
                if (!result.ContainsKey(key)) result[key] = new InGame();
                // only whose it is, in caps: the character, or STAGE (user 2026-10-12: no slot, no code, "just say the name")
                string where = r.Item.IsStage ? "STAGE" : ExMoreStuff.Fighters.Name(r.Item.Fighter).ToUpperInvariant();
                if (!result[key].Into.Contains(where)) result[key].Into.Add(where);
                result[key].Versions.Add(r.Item.Version ?? "");
                if (inGame.Contains(r)) result[key].Records.Add(r);
            }
            foreach (SongMods.Record s in SongMods.Load())   // song mods from GameBanana (Mods\Music)
            {
                if (!s.Key.StartsWith("gamebanana:")) continue;
                if (!result.ContainsKey(s.Key)) result[s.Key] = new InGame();
                if (!result[s.Key].Into.Contains("MUSIC")) result[s.Key].Into.Add("MUSIC");
                result[s.Key].Versions.Add(s.Version ?? "");
            }
            foreach (Override o in Overrides.Load(game))   // override stages from GameBanana
            {
                Match m = Regex.Match(o.Id ?? "", @"^(gamebanana:[^:]+:)");
                if (!m.Success) continue;
                string key = m.Groups[1].Value;
                if (!result.ContainsKey(key)) result[key] = new InGame();
                string replaced = Stages.Name(o.Target).ToUpperInvariant();   // an override: the stage it plays as
                if (!result[key].Into.Contains(replaced)) result[key].Into.Add(replaced);
                result[key].Versions.Add(o.Version ?? "");
            }
            foreach (Effect fx in Effects.Load(game))   // effects from GameBanana
            {
                Match m = Regex.Match(fx.Id ?? "", @"^(gamebanana:[^:]+:)");
                if (!m.Success) continue;
                string key = m.Groups[1].Value;
                if (!result.ContainsKey(key)) result[key] = new InGame();
                string what = ExMoreStuff.Fighters.Name(fx.Fighter).ToUpperInvariant() + " EFFECTS";
                if (!result[key].Into.Contains(what)) result[key].Into.Add(what);
                result[key].Versions.Add(fx.Version ?? "");
            }
            return result;
        }

        // which of a mod's GameBanana files it's built from (their ids and MD5s): when the modder uploads a new version,
        // this changes, and its card says Update. (Installed before EX More Stuff kept it: "", taken as up to date.)
        static string Signature(GameBanana.Mod mod)
        {
            return string.Join(",", Wanted(mod).Select(f => f.Id + ":" + f.Md5).OrderBy(x => x, StringComparer.Ordinal));
        }

        // a mod's files to install: checked ones with costume or stage files in, one per version (zip over rar and 7z)
        static List<GameBanana.ModFile> Wanted(GameBanana.Mod mod)
        {
            return mod.Files.Where(f => f.Ready && (SkinImport.Named(f.Contents).Any() || f.Contents.Any(StageFile) || f.Contents.Any(Effects.IsFile) ||
                                                   f.Contents.Any(c => SongFile.IsMatch(c.Substring(c.LastIndexOf('/') + 1)))))
                .GroupBy(f => f.Base, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First()).ToList();
        }

        // whose a mod is, from the names in its files, in caps (user 2026-10-12: no slot or code, "just say the name of
        // the character or stage"): "SAKURA", "STAGE", an override's "FESTIVAL AT THE OLD TEMPLE"; null: nothing to install
        string Where(GameBanana.Mod mod)
        {
            if (IsSong(mod)) return "MUSIC";   // a song mod: for any stage or fighter, on the Music tab
            string replaces = Overrides.Target(Wanted(mod).SelectMany(f => f.Contents));
            if (replaces != null) return Stages.Name(replaces).ToUpperInvariant();   // an override: the stage it plays as
            var parts = SkinImport.Named(Wanted(mod).SelectMany(f => f.Contents)).Select(c => ExMoreStuff.Fighters.Name(c.Item1).ToUpperInvariant()).Distinct().ToList();
            if (HasStage(mod)) parts.Add("STAGE");
            parts.AddRange(Effects.FightersIn(Wanted(mod).SelectMany(f => f.Contents)).Select(f => ExMoreStuff.Fighters.Name(f).ToUpperInvariant() + " EFFECTS"));
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // the top of the page: rebuilt only when the width changes; the sort buttons show their way and which is in use
        void LayOutTop(int width)
        {
            if (heading == null || topWidth != width)
            {
                if (heading != null) { heading.Dispose(); note.Dispose(); }
                topWidth = width;
                heading = new Heading("Browse Mods", width) { Height = 30, Margin = new Padding(6, 0, 6, 2) };   // compact: no empty space above it (user 2026-10-14)
                // the user's own words, exactly (2026-10-13: "Exactly this nothing more")
                note = ShadowLabel.Note("Players who use the same stages and costumes will see each other's mods in rollback games.", width);   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                tools.Width = width - 16;
                status.Width = width - 16;
            }
            byDate.Text = newestFirst ? "Newest" : "Oldest";
            byCount.Text = mostFirst ? "Most Downloaded" : "Least Downloaded";
            byDate.Primary = !byDownloads;
            byCount.Primary = byDownloads;
            foreach (FlatButton b in new[] { byDate, byCount }) { b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 30; b.Invalidate(); }
            stagesButton.Text = "Stages";
            characterButton.Text = (ExMoreStuff.Fighters.IsCode(pick ?? "") ? ExMoreStuff.Fighters.Name(pick) : "Costumes") + "  ▾";
            musicButton.Primary = pick == "music";
            notInstalledButton.Primary = notInstalledOnly;
            updatesButton.Text = "Updates (" + outdated.Count + ")";
            string creator = CreatorPicked ? CreatorName(pick.Substring(8)) : null;
            if (creator != null && creator.Length > 18) creator = creator.Substring(0, 17) + "…";
            creatorsButton.Text = (pick == CreatorPicker.FavoritesKey ? "Favorites" : creator ?? "Creators") + "  ▾";
            creatorsButton.Primary = pick == CreatorPicker.FavoritesKey || CreatorPicked;
            notificationsButton.Text = "♥  Notifications" + (news.Count > 0 ? " (" + news.Count + ")" : "");
            notificationsButton.Primary = pick == "notifications";
            updatesButton.Primary = pick == "updates";
            // something to see: Notifications pink, Updates purple (user 2026-10-14); orange while it's the one shown
            notificationsButton.Fill = news.Count > 0 && pick != "notifications" ? Theme.Notify : (Color?)null;
            updatesButton.Fill = outdated.Count > 0 && pick != "updates" ? Theme.Update : (Color?)null;
            updatesButton.Visible = outdated.Count > 0 || chaining;
            updateAll.Text = chaining ? (stopChain ? "Stopping..." : "Stop") : "Update all";
            updateAll.Primary = !chaining;
            stagesButton.Primary = pick == "stages";
            characterButton.Primary = ExMoreStuff.Fighters.IsCode(pick ?? "") || pick == "costumes";
            // Mod Packs: shown once there's one (that keeps the rule, with mods on the list); lit on its page and in a pack
            bool packsShown = GameBanana.Collections.Any(c => entries.Any(e => c.Mods.Contains(e.Mod.Id)));
            collectionsButton.Visible = packsShown;
            collectionsButton.Text = "Mod Packs";
            collectionsButton.Primary = pick == "packs" || CollectionPicked;
            collectionsButton.Fill = collectionsButton.Primary ? (Color?)null : Theme.Packs;   // soft green; orange while it's the one shown
            foreach (FlatButton b in new[] { stagesButton, characterButton, musicButton, notInstalledButton, creatorsButton, collectionsButton, notificationsButton, updatesButton, updateAll }) { b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 30; b.Invalidate(); }
            refreshButton.Width = TextRenderer.MeasureText("Refreshing...", refreshButton.Font).Width + 30;   // its widest: nothing moves while it works
            // two rows, each centered (user 2026-10-16): Refresh, the search, Costumes, Stages, Music, Mod Packs; then Newest,
            // Most Downloaded, Creators, Updates, Notifications. A row too wide for the page goes on over another line.
            int y = 2;
            foreach (Control[] row in new[]
            {
                new Control[] { refreshButton, search, characterButton, stagesButton, musicButton, notInstalledButton, collectionsButton },
                new Control[] { byDate, byCount, creatorsButton, updatesButton, notificationsButton },
            })
            {
                var line = new List<Control>();
                Action center = () =>
                {
                    int x = (tools.Width - (line.Sum(c => c.Width) + 6 * (line.Count - 1))) / 2;
                    foreach (Control c in line) { c.Location = new Point(Math.Max(0, x), y + (c is FlatButton ? 0 : (36 - c.Height) / 2)); x += c.Width + 6; }
                    y += 42;
                    line.Clear();
                };
                foreach (Control c in row.Where(c => c == updatesButton ? outdated.Count > 0 || chaining : c != collectionsButton || packsShown))
                {
                    if (line.Count > 0 && line.Sum(b => b.Width + 6) + c.Width > tools.Width) center();
                    line.Add(c);
                }
                if (line.Count > 0) center();
            }
            tools.Height = y - 4;
        }

        void Fill()
        {
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            stale = false;
            var installed = Recount();
            if (pick == "notifications" && Creators.Unseen(news)) { Creators.Seen(news); installed = Recount(); }   // opened: the tab's heart goes
            if (pick == "updates" && outdated.Count == 0 && !chaining) pick = null;
            LayOutTop(width);
            var top = pick == "updates" || (chaining && !collectionChain && !notifyChain) ? new Control[] { heading, note, tools, updateAll, status } : new Control[] { heading, note, tools, status };
            // Notifications: Install all over its cards while some aren't in (Stop while it goes)
            int notified = pick == "notifications" ? NotifiedTodo().Count : 0;
            if (pick == "notifications" && (notified > 0 || notifyChain))
            {
                notifyAction.Text = notifyChain ? (stopChain ? "Stopping..." : "Stop") : "Install all (" + notified + ")";
                notifyAction.Width = TextRenderer.MeasureText(notifyAction.Text, notifyAction.Font).Width + 36;
                notifyAction.Invalidate();
                top = new Control[] { heading, note, tools, notifyAction, status };
            }
            if (Array.IndexOf(top, notifyAction) < 0 && notifyAction.Parent == list) list.Controls.Remove(notifyAction);
            // a picked collection: what it is, and Install all / New Mods / All installed
            var collection = PickedCollection;
            if (CollectionPicked && collection == null) pick = null;   // gone from GameBanana's list
            if (collection != null)
            {
                int count = entries.Count(e => collection.Mods.Contains(e.Mod.Id));
                if (collectionNote != null) collectionNote.Dispose();
                collectionNote = ShadowLabel.Note(collection.Name + " by " + collection.Owner + ": " + count + (count == 1 ? " mod." : " mods."), width);
                PackButton(collectionAction, collection, installed);
                top = new Control[] { heading, note, tools, collectionNote, collectionAction, status };
            }
            if (Array.IndexOf(top, updateAll) < 0 && updateAll.Parent == list) list.Controls.Remove(updateAll);
            if (Array.IndexOf(top, collectionAction) < 0 && collectionAction.Parent == list) list.Controls.Remove(collectionAction);
            var cards = new List<Control>();
            var shown = Shown();
            if (pointKey != null)
            {
                int at = shown.FindIndex(e => e.Key == pointKey);
                if (at >= shownCount) shownCount = at + 1;
            }
            ModCard pointed = null;
            foreach (var entry in shown.Take(shownCount))
            {
                InGame inGame;
                installed.TryGetValue(entry.Key, out inGame);
                bool current = inGame != null && !outdated.Contains(entry.Key);
                // a skin that changes its fighter's moves too: not installed, and the card says why (user 2026-10-13)
                string moves = inGame == null && !SkinImport.AllowMoveMods ? SkinImport.ChangesMoves(Wanted(entry.Mod).SelectMany(f => f.Contents)) : null;
                // pulled for now (the catalog's list, user 2026-10-22): not installed, and the card says why
                string pulledWhy = inGame == null ? Catalog.PulledWhy(entry.Mod.Id) : null;
                if (pulledWhy != null) moves = "";
                // a new file GameBanana is still checking (user 2026-10-13: an update showed greyed out): the card says so
                bool checking = moves == null && entry.Mod.Files.Any(f => f.Waiting);
                var card = new ModCard(entry.Mod.Name, entry.Mod.Author, entry.Mod.Page, entry.Mod.Downloads.ToString("N0") + (entry.Mod.Downloads == 1 ? " download" : " downloads"),
                                       moves != null ? null : Where(entry.Mod), inGame != null ? string.Join(", ", inGame.Into) : null, inGame != null && !current)
                           { Margin = new Padding(6), Why = pulledWhy != null ? "Switched off for now" : moves != null ? "Compatibility issue" : checking ? (inGame != null ? string.Join(", ", inGame.Into) + "   ·   " : "") +
                                                                                                                  "GameBanana is checking a new file" : null,
                             WhyDetail = pulledWhy != null ? char.ToUpperInvariant(pulledWhy[0]) + pulledWhy.Substring(1) + "."
                                       : moves != null ? char.ToUpperInvariant(moves[0]) + moves.Substring(1) + " in every costume of that fighter, and online that's a desync. " +
                                                         "Its model may need them, so as a costume it would crash."
                                       : checking ? "GameBanana checks every new file for viruses before EX More Stuff offers it.\nIt usually takes a few minutes: press Refresh then." : null };
                var mod = entry.Mod;
                card.ShowSection(IsEffects(mod) ? "effects" : mod.Category);   // its GameBanana section's icon, top right (effects: their own, user 2026-10-18)
                card.FileSize = StoragePanel.Megabytes(Wanted(mod).Sum(f => f.Size));   // what it downloads (user 2026-10-16)
                GameBanana.Support support;
                if (GameBanana.Supports.TryGetValue(mod.AuthorId, out support)) card.ShowSupport(support, mod.Author, tips);   // their Ko-fi / Patreon
                card.Install.Click += async (s, e) => await Install(mod);
                if (card.Uninstall != null)
                {
                    string key = entry.Key;
                    card.Uninstall.Click += async (s, e) => { if (!working && UninstallMod != null && await UninstallMod(key, mod.Name)) Fill(); };
                }
                card.Creator = Creators.Key(mod);
                card.Heart.On = Creators.IsFavorite(mod);
                card.Heart.Click += (s, e) => ToggleFavorite(mod, card.Heart);
                card.AuthorClicked += (s, e) => { quiet = true; search.Text = ""; quiet = false; pick = "creator:" + Creators.Key(mod); Refilter(); list.AutoScrollPosition = Point.Empty; };
                tips.SetToolTip(card.Page, "Open its page on GameBanana");
                card.Enable(!working);
                if (checking && inGame == null) card.NotReady();
                if (inGame != null)
                {
                    var warnings = inGame.Records.Select(r => Weight.InGame(game, r.Item, Weight.Name(r))).Where(w => w != null).ToList();
                    if (warnings.Count > 0) card.Warning.SetMessage(string.Join("\n", warnings));
                }
                cards.Add(card);
                if (entry.Key == pointKey) pointed = card;
                var ignored = Thumbnail(CardPicture(entry.Mod), card);
                // a sound's audio preview: the card's ▶ (user 2026-10-17: "update EXMS to play the preview file")
                if (!string.IsNullOrEmpty(mod.Preview))
                {
                    card.PreviewUrl = mod.Preview;
                    card.ShowListen(mod.Preview == previewPlaying);
                    card.Listen.Click += async (s, e) => await PlayPreview(mod.Preview);
                    tips.SetToolTip(card.Listen, "Hear its preview");
                }
            }
            if (pick == "packs") cards.AddRange(PackCards(installed));   // the Mod Packs page: a card each
            else if (shown.Count == 0 && (loaded || entries.Count > 0) && pick == "notifications" && search.Text.Trim() == "")
                cards.Add(ShadowLabel.Note("Nothing new from your favorite creators in the last 6 months.", width));
            else if (shown.Count == 0 && (loaded || entries.Count > 0) && notInstalledOnly && search.Text.Trim() == "")   // Not Installed: nothing left
                cards.Add(ShadowLabel.Note(pick == null ? "Everything here is installed." : "All the mods for " + Picked() + " are installed.", width));
            else if (shown.Count == 0 && (loaded || entries.Count > 0))
                cards.Add(ShadowLabel.Note(search.Text.Trim() != "" ? "No mods match \"" + search.Text.Trim() + "\"" + (pick != null ? " in " + Picked() : "") + "." : "No mods for " + Picked() + " yet.", width));
            if (shown.Count > shownCount)
            {
                var more = new FlatButton("Show more (" + (shown.Count - shownCount) + " more)") { Margin = new Padding(8, 10, 8, 16) };
                more.Click += (s, e) => { shownCount += PageSize; Fill(); };
                cards.Add(more);
            }
            list.SuspendLayout();
            // the cards go; the top stays where it is (so a search box being typed in keeps its place)
            foreach (Control c in list.Controls.Cast<Control>().Where(c => Array.IndexOf(top, c) < 0).ToList()) { list.Controls.Remove(c); c.Dispose(); }
            for (int i = 0; i < top.Length; i++)
            {
                if (top[i].Parent != list) list.Controls.Add(top[i]);
                list.Controls.SetChildIndex(top[i], i);
                list.SetFlowBreak(top[i], true);
            }
            foreach (Control c in cards)
            {
                list.Controls.Add(c);
                if (!(c is ModCard)) { list.SetFlowBreak(c, true); int i = list.Controls.GetChildIndex(c); if (i > 0) list.SetFlowBreak(list.Controls[i - 1], true); }
            }
            list.ResumeLayout();
            if (pointed != null)
            {
                pointKey = null;
                Action light = () => { if (!pointed.IsDisposed) { list.ScrollControlIntoView(pointed); pointed.Flash(); } };
                if (IsHandleCreated) BeginInvoke(light); else light();
            }
            else if (pointKey != null && loaded && !loading)
            {
                pointKey = null;
                Say("That mod isn't listed on GameBanana any more", true);
            }
            // a file in its first 2 minutes (GameBanana.ModFile.Settling): the cards made again once it's ready, so it's
            // offered without a Refresh
            long settles = entries.SelectMany(e => e.Mod.Files).Where(f => f.Clean && f.SettlesIn > 0).Select(f => f.SettlesIn).DefaultIfEmpty(0).Min();
            settled.Stop();
            if (settles > 0) { settled.Interval = (int)Math.Min(int.MaxValue, settles * 1000 + 1000); settled.Start(); }
        }

        readonly Timer settled = new Timer();

        // pictures a few at a time, a failed one tried twice more a little later (user 2026-10-12: cards stayed black when
        // GameBanana's picture server dropped some of a whole page's pictures asked for at once)
        static readonly System.Threading.SemaphoreSlim fetching = new System.Threading.SemaphoreSlim(6);

        const int CardPictureSide = 640;   // a card's picture at twice the size it's shown, for high-DPI screens

        static async Task<Image> Picture(string url)
        {
            byte[] bytes = null;
            for (int attempt = 0; bytes == null && attempt < 3; attempt++)
            {
                if (attempt > 0) await Task.Delay(attempt * 1500);
                await fetching.WaitAsync();
                try { bytes = url.StartsWith("zip:", StringComparison.Ordinal) ? await GameBanana.ZipPicture(url.Substring(4)) : await GameBanana.Bytes(url); }
                catch (Exception) { }
                finally { fetching.Release(); }
            }
            if (bytes == null) return null;
            try { return await Task.Run(() => Pictures.Smaller(Pictures.Read(bytes), CardPictureSide)); }   // kept at card size
            catch (Exception) { return null; }   // not a picture EX More Stuff can read
        }

        async Task Thumbnail(string url, ModCard card)
        {
            if (string.IsNullOrEmpty(url)) return;
            Task<Image> image;
            if (!thumbnails.TryGetValue(url, out image)) thumbnails[url] = image = Picture(url);
            Image picture = await image;
            // not had: asked for again when the cards are next made (a filter, Show more, Refresh, the tab opened again)
            if (picture == null) { Task<Image> kept; if (thumbnails.TryGetValue(url, out kept) && kept == image) thumbnails.Remove(url); }
            if (card.IsDisposed) return;
            card.Picture = picture;
            card.Invalidate();
        }

        string CantInstall()
        {
            string blocked = Blocked != null ? Blocked() : null;
            if (blocked == null && Game.IsRunning()) blocked = "close the game first";
            return blocked;
        }

        // download each version -> unpack -> its costumes (versions as colors) each in its code; its stages each in a code of
        // their own
        async Task<bool> Install(GameBanana.Mod mod)
        {
            bool song = IsSong(mod);   // a song mod: nothing goes in the game, so it may be open, and no PC check
            string blocked = song ? (Blocked != null ? Blocked() : null) : CantInstall();
            if (blocked != null) { Say("Can't install it now: " + blocked, true); return false; }
            string pulled = Catalog.PulledWhy(mod.Id);   // pulled for now (the catalog's list)
            if (pulled != null) { Say(mod.Name + " is switched off for now: " + pulled, true); return false; }
            string tooNew = Overrides.TooNew(Wanted(mod).SelectMany(f => f.Contents));   // made for a newer EX More Stuff
            if (tooNew != null) { Say(mod.Name + " " + tooNew, true); return false; }
            string moves = SkinImport.ChangesMoves(Wanted(mod).SelectMany(f => f.Contents));
            if (moves != null && !SkinImport.AllowMoveMods) { Say(mod.Name + " can't be installed: " + moves + ", for every costume (online that's a desync), and its model may need them", true); return false; }
            if (BeforeInstall != null && !song) await BeforeInstall();
            bool isOverride = Overrides.Target(Wanted(mod).SelectMany(f => f.Contents)) != null;   // an override stage: no seat to make room in
            if (!isOverride && !await MakeRoom(mod)) return false;
            SetWorking(true);
            string g = game, patch = Game.PatchFolder(game), done = null;
            bool replaced = false;
            var before = Weight.Keys(g);
            try
            {
                var files = Wanted(mod);
                if (files.Count == 0) throw new InvalidDataException("it has no costume or stage files GameBanana has checked");
                Directory.CreateDirectory(patch);
                var everything = new List<KeyValuePair<string, byte[]>>();
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    string download = Path.Combine(patch, "ex_more_stuff.gamebanana" + Path.GetExtension(file.Name));
                    string which = files.Count > 1 ? " (" + (i + 1) + " of " + files.Count + ")" : "";
                    var progress = new Progress<long>(bytes => Say("Downloading " + mod.Name + which + "   " + StoragePanel.Megabytes(bytes) + " of " + StoragePanel.Megabytes(file.Size)));
                    try
                    {
                        await GameBanana.Download(file, download, progress);
                        Say("Unpacking " + mod.Name + which + "...");
                        string work = Path.Combine(patch, "ex_more_stuff.unpack");
                        var unpacked = await Task.Run(() => GameBanana.Unpack(download, work));
                        everything.AddRange(unpacked.Select(f => new KeyValuePair<string, byte[]>(file.Base + "/" + f.Key, f.Value)));
                    }
                    finally { try { File.Delete(download); } catch (Exception) { } }
                }
                // a song mod: its music files kept in Mods\Music for the Music tab (nothing goes in the game until a song is picked there)
                if (IsSong(mod))
                {
                    var banks = everything.Where(f => SongFile.IsMatch(Path.GetFileName(f.Key))).ToList();
                    if (banks.Count == 0) throw new InvalidDataException("it has no music files EX More Stuff can use");
                    Say("Putting " + mod.Name + " in...");
                    await Task.Run(() => SongMods.Put("gamebanana:" + mod.Id + ":", mod.Name, mod.Author, Signature(mod), banks));
                    done = mod.Name + " is in: pick it for any stage or fighter on the Music tab";
                }
                // an override stage (override_KOR.txt in it): in its game stage's place on this PC, no code of its own
                else if (Overrides.Target(everything.Select(f => f.Key)) != null)
                {
                    byte[] shot = null;
                    if (!string.IsNullOrEmpty(mod.Picture)) try { shot = await GameBanana.Bytes(mod.Picture); } catch (Exception) { }
                    Say("Putting " + mod.Name + " in...");
                    string target = await Task.Run(() => Overrides.Install(g, everything, "gamebanana:" + mod.Id + ":override", mod.Name, mod.Author, Signature(mod), shot));
                    done = mod.Name + " is in: it plays in place of " + Stages.Name(target) + " (only you see it, online too; the Stages page puts the game's own back)";
                }
                else
                {
                    var costumes = SkinImport.Find(everything);
                    var stageFiles = everything.Where(f => StageFile(f.Key) || Overrides.IsCodeMarker(f.Key)).ToList();   // code_<CODE>.txt: its own code
                    // a color pack (new colors 30-99 for a costume, no model: user 2026-10-17, the Dee Jay white pants upload):
                    // in as Add Mod From File puts colors in, each color its own
                    var effectFiles = everything.Where(f => Effects.IsFile(f.Key)).ToList();   // a fighter's effects (user 2026-10-18)
                    var colorFiles = costumes.Count == 0 && stageFiles.Count == 0 && ModFile.FindColors(everything).Count > 0
                        ? everything.Where(f => ModFile.FindColors(new[] { f }).Count > 0 || Regex.IsMatch(Path.GetFileName(f.Key), @"^[A-Za-z0-9]{3}_\d\d_\d\d\.(obj\.emm|png)$", RegexOptions.IgnoreCase)).ToList()
                        : new List<KeyValuePair<string, byte[]>>();
                    if (costumes.Count == 0 && stageFiles.Count == 0 && colorFiles.Count == 0 && effectFiles.Count == 0) throw new InvalidDataException("it has no costume or stage files EX More Stuff can use");
                    byte[] picture = null;
                    if (!string.IsNullOrEmpty(mod.Picture)) try { picture = await GameBanana.Bytes(mod.Picture); } catch (Exception) { }
                    var into = new List<string>();
                    var problems = new List<string>();
                    bool personal = false, firstModel = false;
                    var records = Installer.Load(g).Concat(Installer.Removed(g)).ToList();
                    foreach (var costume in costumes)
                    {
                        string id = "gamebanana:" + mod.Id + ":" + costume.Fighter + costume.Number.ToString("D2");
                        Installed again = records.FirstOrDefault(r => r.Item.Id == id);
                        int slot = again != null ? again.Item.Slot : SharedCodes.Slot(mod, costume.Fighter, costume.Number, holders);
                        if (slot == 0) { slot = SkinImport.FreeSlot(g, costume.Fighter); personal = true; }
                        if (slot == 0) throw new InvalidOperationException(SkinImport.NoRoom(costume.Fighter));
                        Installed other = records.FirstOrDefault(r => r.Item.IsCostume && r.Item.Fighter == costume.Fighter && r.Item.Slot == slot && r.Item.Id != id);
                        if (other != null)
                            throw new InvalidOperationException("your " + Fighters.Name(other.Item.Fighter) + " " + other.Item.TitleNamed(other.Shown ?? other.Item.Name) +
                                                                " is in the code this skin shares with everyone; give yours another code in Advanced > Package codes first");
                        string name = Regex.Replace(mod.Name, @"[^\w\s\-\.\(\)]", "").Trim();
                        string zip = Path.Combine(AppFolders.FighterPackages(costume.Fighter), name + " - " + costume.Fighter + " " + slot.ToString("D2") + ".zip");
                        var item = new Item
                        {
                            Id = id, Type = "costume", Fighter = costume.Fighter, Slot = slot, Code = "", Name = mod.Name, Author = mod.Author, Version = Signature(mod),
                            Description = mod.Name + " by " + mod.Author + " on GameBanana (" + mod.Page + "): " + costume.Describe +
                                          (costume.Versions > 1 ? ", its " + costume.Versions + " versions as colors" : ""),
                            Picture = "", Download = "", Sha256 = "",
                        };
                        Say("Putting " + mod.Name + " in as " + Fighters.Name(costume.Fighter) + " " + slot.ToString("D2") + "...");
                        int s = slot;
                        string said = null, warning = null;
                        await Task.Run(() =>
                        {
                            // built beside it first: an older version's zip stays as it is if this one isn't wanted
                            string fresh = zip.Substring(0, zip.Length - 4) + " (new).zip";
                            try
                            {
                                SkinImport.Build(g, costume, s, picture ?? new byte[0], fresh);   // a GameBanana install: never the mod's own card picture
                                InstallCheck.Decide(g, fresh, true, out said, out warning);   // the costume check (EX More Stuff's own zip, fixed in it)
                                File.Copy(fresh, zip, true);
                                Installer.InstallPackage(g, item, zip, "file");
                                InstallCheck.SetWarning(item.Key, warning);
                                if (warning != null) Installer.Remove(g, item.Key);   // it can't be fixed: switched off and locked
                            }
                            finally { try { File.Delete(fresh); } catch (Exception) { } }
                        });
                        string seat = Fighters.Name(costume.Fighter) + " " + slot.ToString("D2");
                        if (warning != null) { problems.Add(seat + " is switched off: it has problems that can't be fixed (see " + InstallCheck.ListButton + ")"); continue; }
                        into.Add(seat + (said != null ? " (" + said + ")" : ""));
                        firstModel |= costume.ModelsDiffer;
                    }
                    if (moves != null) problems.Add("its move files (test switch: " + moves + ")");
                    if (stageFiles.Count > 0)
                    {
                        Action<string> say = text => BeginInvoke((Action)(() => Say(text)));
                        var result = await Task.Run(() => ModFile.InstallFiles(g, stageFiles, "gamebanana:" + mod.Id, mod.Name, mod.Author, " on GameBanana (" + mod.Page + ")", picture, say, Signature(mod),
                                                                                  code => SharedCodes.StageCode(mod, code)));
                        into.AddRange(result.Into);
                        problems.AddRange(result.Problems);
                        personal |= result.Into.Count > 0 && !result.Shared;
                    }
                    if (colorFiles.Count > 0)
                    {
                        Action<string> say = text => BeginInvoke((Action)(() => Say(text)));
                        var result = await Task.Run(() => ModFile.InstallFiles(g, colorFiles, "gamebanana:" + mod.Id, mod.Name, mod.Author, " on GameBanana (" + mod.Page + ")", picture, say, Signature(mod),
                                                                                  code => SharedCodes.StageCode(mod, code)));
                        into.AddRange(result.Into);
                        problems.AddRange(result.Problems);
                    }
                    if (effectFiles.Count > 0)
                    {
                        Say("Putting " + mod.Name + " in...");
                        var fx = await Task.Run(() => Effects.Install(g, effectFiles, "gamebanana:" + mod.Id, mod.Name, mod.Author, Signature(mod), picture));
                        into.AddRange(fx.Select(f => ExMoreStuff.Fighters.Name(f) + "'s effects"));
                        personal = true;   // only on this PC, online too
                    }
                    if (into.Count == 0) throw new InvalidOperationException(string.Join("; ", problems));
                    // its old version's leftovers out (only when all of it went in)
                    if (problems.Count == 0) { string version = Signature(mod); await Task.Run(() => PackagesPanel.DropLeftovers(g, "gamebanana:" + mod.Id + ":", version)); }
                    done = mod.Name + " is in: " + string.Join(", ", into) + (personal ? " (only you see " + (into.Count > 1 ? "those" : "it") + ")" : ", the same for everyone who has it") +
                           (firstModel ? ". Its versions have models of their own; the first one's is used" : "") + (problems.Count > 0 ? ". Not put in: " + string.Join("; ", problems) : "") +
                           Notes(g, before);
                }
            }
            catch (GameBanana.FileReplaced ex) { Say(mod.Name + " wasn't installed: " + ex.Message + ". Refreshing...", true); replaced = true; }
            catch (Exception ex) { Say(mod.Name + " wasn't installed: " + ex.Message, true); }
            finally { try { Finished(done); } finally { SetWorking(false); } }
            if (done != null) Say(done);
            if (replaced) ReadAgain();   // its card then shows the new file (Install), or that GameBanana is still checking it
            return done != null;
        }

        // Before anything downloads: a seat this mod shares with everyone (a costume slot, a stage code) that one of the
        // player's own packages is in now. The player is asked to move theirs to a free seat (84 down to 80, then 40 down, then 79 down; or
        // a personal U code for a stage); Package codes' move does it (its zip rewritten, its name kept). False: not installed.
        async Task<bool> MakeRoom(GameBanana.Mod mod)
        {
            string g = game, ours = "gamebanana:" + mod.Id + ":";
            var seats = SkinImport.Named(Wanted(mod).SelectMany(f => f.Contents))
                .Select(c => new { Fighter = c.Item1, Number = c.Item2, Slot = SharedCodes.Slot(mod, c.Item1, c.Item2, holders) })
                .Where(c => c.Slot > 0).ToList();
            var stageCodes = StageCodes(mod).Select(c => SharedCodes.StageCode(mod, c)).Where(c => c != null).Distinct().ToList();
            foreach (var seat in seats)
            {
                var records = Installer.Load(g).Concat(Installer.Removed(g)).ToList();
                string id = ours + seat.Fighter + seat.Number.ToString("D2");
                if (records.Any(r => r.Item.Id == id)) continue;   // it's in already: an update
                Installed other = records.FirstOrDefault(r => r.Item.IsCostume && r.Item.Fighter == seat.Fighter && r.Item.Slot == seat.Slot && r.Item.Id != id);
                if (other == null) continue;
                int to = SkinImport.FreeSlot(g, seat.Fighter);
                if (!await MoveYours(mod, other, Fighters.Name(seat.Fighter) + " " + seat.Slot.ToString("D2"), to > 0 ? to.ToString() : null, to,
                                to > 0 ? Fighters.Name(seat.Fighter) + " " + to.ToString("D2") : null)) return false;
            }
            foreach (string code in stageCodes)
            {
                var records = Installer.Load(g).Concat(Installer.Removed(g)).ToList();
                if (records.Any(r => (r.Item.Id ?? "").StartsWith(ours + "STG_") && r.Item.Code == code)) continue;   // it's in already
                Installed other = records.FirstOrDefault(r => r.Item.IsStage && r.Item.Code == code && !(r.Item.Id ?? "").StartsWith(ours));
                if (other == null) continue;
                string to = ModFile.FreeStageCode(g);
                if (!await MoveYours(mod, other, "Stage " + code, to, 0, to != null ? "stage " + to : null)) return false;
            }
            return true;
        }

        async Task<bool> MoveYours(GameBanana.Mod mod, Installed other, string seat, string code, int slot, string target)
        {
            // its name as the player knows it ("Monster Hunter"); without one, its slot or code
            string named = !string.IsNullOrEmpty(other.Shown) ? other.Shown : other.Item.Name;
            string yours = (other.Item.IsStage ? "Stage" : "Costume") + " \"" + (!string.IsNullOrEmpty(named) ? named : other.Item.TitleNamed(null)) + "\"";   // Costume "Monster Hunter"
            if (target == null) { Say(seat + " is where " + mod.Name + " goes for everyone, and your " + yours + " is there: there's no free seat to move yours to; uninstall one of yours to make room", true); return false; }
            string problem = CodeChange.Problem(game, other, code, slot);
            if (problem != null) { Say(seat + " is where " + mod.Name + " goes for everyone, and your " + yours + " is there; it can't move: " + problem, true); return false; }
            if (MessageBox.Show(FindForm(), seat + " is where " + mod.Name + " goes for everyone. Your " + yours + " is there now.\n\nMove yours to " + target + " and install?",
                    "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
            {
                Say(mod.Name + " wasn't installed: your " + yours + " stays in " + seat);
                return false;
            }
            Say("Moving your " + yours + " to " + target + "...");
            string g = game;
            SetWorking(true);
            try { await Task.Run(() => CodeChange.Apply(g, other, code, slot)); }
            catch (Exception ex) { Say("Your " + yours + " didn't move: " + ex.Message, true); return false; }
            finally { SetWorking(false); }
            var changed = Changed;
            if (changed != null) changed();
            return true;
        }

        // Update all: the installed mods with a newer version, one at a time, each downloaded and put in before the next
        // starts, 2 seconds apart (easy on the PC and on GameBanana); pressed again (Stop), it ends after the one going
        async Task UpdateAll()
        {
            if (chaining) { stopChain = true; if (heading != null) LayOutTop(topWidth); return; }
            var todo = entries.Where(e => outdated.Contains(e.Key)).Select(e => e.Mod).ToList();
            if (todo.Count == 0) return;
            chaining = true;
            stopChain = false;
            SetWorking(true);
            int done = 0;
            var failed = new List<string>();
            foreach (var mod in todo)
            {
                if (stopChain) break;
                if (await Install(mod)) done++; else failed.Add(mod.Name);
                if (stopChain || mod == todo.Last()) break;
                Say("Updated " + done + " of " + todo.Count + "; the next in 2 seconds...");
                await Task.Delay(2000);
            }
            chaining = false;
            stopChain = false;
            SetWorking(false);
            Fill();
            int left = outdated.Count;
            Say((done > 0 ? "Updated " + done + " mod" + (done > 1 ? "s" : "") : "Nothing was updated") +
                (failed.Count > 0 ? "; not updated: " + string.Join(", ", failed) : "") + (left > 0 && failed.Count == 0 ? "; " + left + " still to update" : ""), failed.Count > 0);
        }

        // A pack's button (its card's, and above its mods; user 2026-10-14): "Install pack" when none of its mods is in;
        // "UPDATE" over "N New Mods" (purple) when some are and others aren't, or have an update; "All installed" (greyed)
        // when there's nothing to get; "Stop" while it installs
        void PackButton(FlatButton button, GameBanana.Collection pack, Dictionary<string, InGame> installed)
        {
            var members = entries.Where(e => pack.Mods.Contains(e.Mod.Id)).ToList();
            var missing = members.Where(e => !installed.ContainsKey(e.Key)).ToList();
            var todo = missing.Concat(members.Where(e => outdated.Contains(e.Key))).ToList();
            bool anyIn = missing.Count < members.Count, going = collectionChain && chainPack == pack.Id;
            button.Sub = null;
            button.Fill = null;
            button.Primary = true;
            if (going) button.Text = stopChain ? "Stopping..." : "Stop";
            else if (!anyIn) button.Text = "Install pack";
            else if (todo.Count > 0) { button.Text = "UPDATE"; button.Sub = todo.Count + (todo.Count == 1 ? " New Mod" : " New Mods"); button.Fill = Theme.Update; }
            else { button.Text = "All installed"; button.Primary = false; }
            button.Enabled = going || (!chaining && todo.Count > 0);
            button.Size = new Size(Math.Max(TextRenderer.MeasureText(button.Text, button.Font).Width, button.Sub != null ? TextRenderer.MeasureText(button.Sub, Theme.Font(8.5f)).Width : 0) + 36,
                                   button.Sub != null ? 44 : 36);
            tips.SetToolTip(button, anyIn && todo.Count > 0 && !going ? "New in this pack or updated: " + string.Join(", ", todo.Select(e => e.Mod.Name)) : null);
            button.Invalidate();
        }

        // The Mod Packs page: a card per pack whose mods are on the list, its picture its 4 newest mods' (PackCard)
        List<Control> PackCards(Dictionary<string, InGame> installed)
        {
            var cards = new List<Control>();
            foreach (var pack in GameBanana.Collections)
            {
                var members = entries.Where(e => pack.Mods.Contains(e.Mod.Id)).ToList();
                if (members.Count == 0) continue;
                var p = pack;
                var card = new PackCard(pack.Name, pack.Owner, members.Count) { Margin = new Padding(6) };
                card.Page.Page = pack.Page;
                tips.SetToolTip(card.Page, "Open its page on GameBanana");
                PackButton(card.Action, pack, installed);
                card.Action.Location = new Point(12, card.Height - card.Action.Height - 12);
                card.Action.Click += async (s, e) => await InstallCollection(p);
                card.Opened += (s, e) => { pick = "collection:" + p.Id; Refilter(); list.AutoScrollPosition = Point.Empty; };   // its mods
                // its maker: a favorite with the heart, their mods with the gold name, as on a mod card (one of the pack's is theirs)
                var theirs = members[0].Mod;
                card.Heart.On = Creators.IsFavorite(theirs);
                card.Heart.Click += (s, e) => ToggleFavorite(theirs, card.Heart);
                card.OwnerClicked += (s, e) => { quiet = true; search.Text = ""; quiet = false; pick = "creator:" + Creators.Key(theirs); Refilter(); list.AutoScrollPosition = Point.Empty; };
                GameBanana.Support support;
                if (GameBanana.Supports.TryGetValue(pack.OwnerId, out support)) card.ShowSupport(support, pack.Owner, tips);
                var ignored = PackPicture(members.OrderByDescending(e => e.Date).Take(4).Select(e => CardPicture(e.Mod)).ToList(), card);   // a song's from its zip (user 2026-10-17: the BGM pack's picture was black)
                cards.Add(card);
            }
            return cards;
        }

        // a pack card's picture: its 4 newest mods' pictures, two by two, a yellow cross between them (user 2026-10-14)
        // a mod's card picture: GameBanana's, or with none there (its Sounds section takes none) the one in its zip, read from
        // GameBanana ("zip:" + its address; user 2026-10-17)
        string CardPicture(GameBanana.Mod mod)
        {
            if (!string.IsNullOrEmpty(mod.Thumbnail)) return mod.Thumbnail;
            GameBanana.ModFile zipped = Wanted(mod).FirstOrDefault(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            return zipped != null ? "zip:" + zipped.Download : null;
        }

        async Task PackPicture(List<string> urls, PackCard card)
        {
            var pictures = new List<Image>();
            foreach (string url in urls)
            {
                if (string.IsNullOrEmpty(url)) { pictures.Add(null); continue; }
                Task<Image> image;
                if (!thumbnails.TryGetValue(url, out image)) thumbnails[url] = image = Picture(url);
                pictures.Add(await image);
            }
            if (card.IsDisposed) return;
            int w = card.Width - 3, h = 140;
            var whole = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(whole))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Theme.Well);
                for (int i = 0; i < 4; i++)
                {
                    var cell = new RectangleF(i % 2 * w / 2f, i / 2 * h / 2f, w / 2f, h / 2f);
                    Image picture = i < pictures.Count ? pictures[i] : null;
                    if (picture != null) Theme.Fit(g, picture, cell, false);
                }
                using (var yellow = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(yellow, w / 2f - 1.5f, 0, 3, h);
                    g.FillRectangle(yellow, 0, h / 2f - 1.5f, w, 3);
                }
            }
            if (card.Picture != null) card.Picture.Dispose();
            card.Picture = whole;
            card.Invalidate();
        }

        // Install pack / UPDATE (user 2026-10-14: "install all, queue them like update"): a pack's mods not in yet and those
        // with an update, one at a time as Update all does (2 seconds apart); pressed again (Stop), it ends after the one going
        bool collectionChain;
        long chainPack;   // the pack being installed
        async Task InstallCollection(GameBanana.Collection collection)
        {
            if (chaining) { if (collectionChain) { stopChain = true; Fill(); } return; }
            if (collection == null) return;
            chainPack = collection.Id;
            var installed = Installed();
            var todo = entries.Where(e => collection.Mods.Contains(e.Mod.Id) && (!installed.ContainsKey(e.Key) || outdated.Contains(e.Key))).Select(e => e.Mod).ToList();
            if (todo.Count == 0) return;
            chaining = collectionChain = true;
            stopChain = false;
            SetWorking(true);
            Fill();
            int done = 0;
            var failed = new List<string>();
            for (int i = 0; i < todo.Count && !stopChain; i++)
            {
                Say("Installing " + (i + 1) + " of " + todo.Count + ": " + todo[i].Name + "...");
                if (await Install(todo[i])) done++; else failed.Add(todo[i].Name);
                if (stopChain || i == todo.Count - 1) break;
                await Task.Delay(2000);
            }
            chaining = collectionChain = false;
            stopChain = false;
            SetWorking(false);
            Fill();
            Say("Installed " + done + " of " + todo.Count + (failed.Count > 0 ? "; not installed: " + string.Join(", ", failed) : ""), failed.Count > 0);
        }

        // Notifications' mods not in yet that have files to install (the ones Install all puts in)
        List<GameBanana.Mod> NotifiedTodo()
        {
            var installed = Installed();
            return entries.Where(e => newIds.Contains(e.Mod.Id) && !installed.ContainsKey(e.Key) && Wanted(e.Mod).Count > 0).OrderByDescending(e => e.Date).Select(e => e.Mod).ToList();
        }

        // Install all on Notifications: one at a time as a pack's (2 seconds apart); pressed again (Stop), it ends after the one going
        async Task InstallNotified()
        {
            if (chaining) { if (notifyChain) { stopChain = true; Fill(); } return; }
            var todo = NotifiedTodo();
            if (todo.Count == 0) return;
            chaining = notifyChain = true;
            stopChain = false;
            SetWorking(true);
            Fill();
            int done = 0;
            var failed = new List<string>();
            for (int i = 0; i < todo.Count && !stopChain; i++)
            {
                Say("Installing " + (i + 1) + " of " + todo.Count + ": " + todo[i].Name + "...");
                if (await Install(todo[i])) done++; else failed.Add(todo[i].Name);
                if (stopChain || i == todo.Count - 1) break;
                await Task.Delay(2000);
            }
            chaining = notifyChain = false;
            stopChain = false;
            SetWorking(false);
            Fill();
            Say("Installed " + done + " of " + todo.Count + (failed.Count > 0 ? "; not installed: " + string.Join(", ", failed) : ""), failed.Count > 0);
        }

        string Picked()
        {
            return pick == "stages" ? "stages" : pick == "music" ? "music" : pick == "costumes" ? "costumes" : pick == "updates" ? "updates" : pick == CreatorPicker.FavoritesKey ? "your favorite creators" :
                   pick == "notifications" ? "Notifications" : CreatorPicked ? CreatorName(pick.Substring(8)) :
                   pick == "packs" ? "Mod Packs" : CollectionPicked ? (PickedCollection != null ? PickedCollection.Name : "this pack") : ExMoreStuff.Fighters.Name(pick);
        }

        // a creator's name, by key (the latest the list has)
        string CreatorName(string key)
        {
            return entries.Where(e => Creators.Key(e.Mod) == key).Select(e => e.Mod.Author).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "a creator";
        }

        /// <summary>The page from the start (its tab clicked): every mod, no filter or search, scrolled to the top.</summary>
        public void ShowAll()
        {
            pick = null;
            quiet = true;
            search.Text = "";
            quiet = false;
            if (!loaded) return;   // it shows once the list is in
            Refilter();
            list.AutoScrollPosition = Point.Empty;
        }

        /// <summary>One creator's mods, favorite or not (their gold name clicked on a costume or stage card; user
        /// 2026-10-12): by a mod of theirs, or their name when it isn't listed.</summary>
        public void ShowCreatorOf(long modId, string author)
        {
            quiet = true;
            search.Text = "";
            quiet = false;
            if (!loaded) { creatorOf = Tuple.Create(modId, author); if (!loading) { var ignored = Load(); } return; }   // it shows once the list is in
            PickCreator(modId, author);
            Refilter();
            list.AutoScrollPosition = Point.Empty;
        }

        void PickCreator(long modId, string author)
        {
            GameBanana.Mod mod = entries.Select(e => e.Mod).FirstOrDefault(m => m.Id == modId) ??
                                 entries.Select(e => e.Mod).FirstOrDefault(m => string.Equals(m.Author, author, StringComparison.OrdinalIgnoreCase)) ??
                                 new GameBanana.Mod { Author = author };
            pick = "creator:" + Creators.Key(mod);
        }

        /// <summary>Opens Notifications: new mods from the favorite creators (the tab's heart goes).</summary>
        public void ShowNotifications()
        {
            pick = "notifications";
            if (!loaded && !loading) { var ignored = Load(); return; }   // it shows once the list is in
            Refilter();
        }

        // the Creators list: Favorites, then the favorite creators, then everyone else alphabetically, with their mods
        CreatorPicker CreatorList()
        {
            var rows = entries.GroupBy(e => Creators.Key(e.Mod)).Select(g => new CreatorPicker.Row
            {
                Key = g.Key, Count = g.Count(), Favorite = Creators.IsFavorite(g.First().Mod),
                Name = g.Select(e => e.Mod.Author).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "?",
                Picture = g.Select(e => e.Mod.AuthorPicture).FirstOrDefault(OwnPicture),
            }).OrderBy(r => r.Favorite ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var picker = new CreatorPicker(rows, entries.Count(e => Creators.IsFavorite(e.Mod)))
            {
                Selected = pick == CreatorPicker.FavoritesKey ? pick : CreatorPicked ? pick.Substring(8) : null,
            };
            var waiting = new HashSet<string>();
            picker.Picture = address =>
            {
                Task<Image> image;
                if (!thumbnails.TryGetValue(address, out image)) thumbnails[address] = image = Picture(address);
                if (image.IsCompleted) return image.Status == TaskStatus.RanToCompletion ? image.Result : null;
                if (waiting.Add(address)) image.ContinueWith(t => { try { if (!picker.IsDisposed) picker.BeginInvoke((Action)picker.Invalidate); } catch (Exception) { } });
                return null;
            };
            picker.Hearted = row =>
            {
                var first = entries.FirstOrDefault(e => Creators.Key(e.Mod) == row.Key);
                if (first == null) return row.Favorite;
                bool on = Creators.Toggle(first.Mod);
                FavoriteChanged(row.Key, on);
                return on;
            };
            picker.HeartedAt += (row, area) => Toast.ShowAbove(picker, area, row.Favorite ? "Favorited this creator" : "Removed from favorites", this);
            return picker;
        }

        // a creator's own profile picture: not GameBanana's default (near black) or the one for banned accounts
        static bool OwnPicture(string address)
        {
            return !string.IsNullOrEmpty(address) && address.IndexOf("/defaults/avatar", StringComparison.OrdinalIgnoreCase) < 0 &&
                   address.IndexOf("/av/banned", StringComparison.OrdinalIgnoreCase) < 0;
        }

        // a card's heart: the creator a favorite or not, said in a small message above that heart
        void ToggleFavorite(GameBanana.Mod mod, HeartButton heart)
        {
            bool on = Creators.Toggle(mod);
            Toast.ShowAbove(heart, heart.ClientRectangle, on ? "Favorited this creator" : "Removed from favorites", this);
            FavoriteChanged(Creators.Key(mod), on);
        }

        // every card of that creator shows it; Favorites and Notifications are shown again (their mods come or go)
        /// <summary>A creator made a favorite (or not) elsewhere (a costume card's heart): this page follows.</summary>
        public void CreatorChanged(GameBanana.Mod mod, bool on) { FavoriteChanged(Creators.Key(mod), on); }

        void FavoriteChanged(string key, bool on)
        {
            foreach (ModCard card in list.Controls.OfType<ModCard>().Where(c => c.Creator == key)) card.Heart.On = on;
            Recount();
            if (pick == CreatorPicker.FavoritesKey || pick == "notifications") Fill();
            else if (heading != null) LayOutTop(topWidth);
        }

        /// <summary>Installs a mod file (Advanced > Add Mod From File for anything that isn't an EX More Stuff package, or
        /// a file dropped here): a GameBanana file points to its card (its shared code), anything else goes in as it is,
        /// in personal slots and codes.</summary>
        /// <summary>Installs a mod file (Add Mod From File, or dropped here). Returns null when it went in, else why not.</summary>
        public async Task<string> InstallFile(string path)
        {
            string blocked = CantInstall();
            if (blocked != null) { Say("Can't install it now: " + blocked, true); return "can't install it now: " + blocked; }
            if (BeforeInstall != null) await BeforeInstall();
            SetWorking(true);
            string g = game, done = null, problem = null;
            var before = Weight.Keys(g);
            try
            {
                string md5 = await Task.Run(() => ModFile.Md5(path));
                GameBanana.Mod same = mods == null ? null : mods.FirstOrDefault(m => m.Files.Any(f => f.Md5 == md5));
                if (same != null)
                {
                    Highlight("gamebanana:" + same.Id + ":");
                    bool installed = Installer.Load(g).Concat(Installer.Removed(g)).Any(r => r.Item.Id.StartsWith("gamebanana:" + same.Id + ":"));
                    problem = installed ? "it's " + same.Name + " from GameBanana, already installed"
                                        : "it's " + same.Name + " from GameBanana: press Install on its card (Browse Mods) so it goes in its shared code";
                    Say("That file is " + same.Name + " from GameBanana: " + (installed ? "it's already installed" : "press Install on its card (below) so it goes in its shared code"));
                    return problem;
                }
                Action<string> say = text => BeginInvoke((Action)(() => { Say(text); if (Progress != null) Progress(text); }));
                var result = await Task.Run(() => ModFile.Install(g, path, null, null, "", null, null, say, check: true));   // the costume check (InstallCheck)
                if (result.Into.Count == 0) throw new InvalidOperationException(string.Join("; ", result.Problems));
                done = ModFile.NameFrom(path) + " is in: " + string.Join(", ", result.Into) + " (only you see it)" +
                       (result.Problems.Count > 0 ? ". Not put in: " + string.Join("; ", result.Problems) : "") + Notes(g, before);
            }
            catch (Exception ex) { problem = ex.Message; Say("It wasn't installed: " + ex.Message, true); }
            finally { try { Finished(done); } finally { SetWorking(false); } }
            if (done != null) Say(done);
            LastDone = done;
            return problem;
        }

        // Add Mod From File (the window, from whatever tab it's on: user 2026-10-14, it used to switch to Browse Mods): the
        // install's progress, and what it put in (LastDone), for the window's status line
        public Action<string> Progress;
        public string LastDone { get; private set; }

        // the window's footer: an install's steps (downloading, unpacking, putting in) show there while it works (user
        // 2026-10-16: "make the download status appear here"); what went wrong stays on this page
        public Action<string> FooterSays;

        // GameBanana's audio previews (a sound's mp3): one plays at a time, at the Music tab's starting volume; its card's
        // button shows ■ while it plays and ▶ again when it ends or another starts
        readonly WavePlayer previewPlayer = new WavePlayer { Volume = 0.33f };
        readonly Dictionary<string, short[]> previews = new Dictionary<string, short[]>();
        readonly Timer previewEnded = new Timer { Interval = 250 };
        string previewPlaying;

        async Task PlayPreview(string url)
        {
            bool same = previewPlaying == url && previewPlayer.Playing;
            previewPlayer.Stop();
            previewPlaying = null;
            ShowPreviewButtons();
            if (same) return;   // pressed again while it played: stopped
            short[] pcm;
            if (!previews.TryGetValue(url, out pcm))
            {
                try
                {
                    byte[] mp3 = await GameBanana.Bytes(url);
                    pcm = await Task.Run(() =>
                    {
                        // Windows' decoders read files: a copy in EX More Stuff's folder for a moment
                        string temp = Path.Combine(AppFolders.Root, "opening", "preview.mp3");
                        Directory.CreateDirectory(Path.GetDirectoryName(temp));
                        File.WriteAllBytes(temp, mp3);
                        try { return AudioFile.Load(temp).Pcm; }
                        finally { try { Directory.Delete(Path.GetDirectoryName(temp), true); } catch (Exception) { } }
                    });
                    previews[url] = pcm;
                }
                catch (Exception ex) { Say("Its preview couldn't be played: " + ex.Message, true); return; }
            }
            try { previewPlayer.Play(pcm, 0, false, 0, pcm.Length / 2); }
            catch (Exception ex) { Say("Its preview couldn't be played: " + ex.Message, true); return; }
            previewPlaying = url;
            ShowPreviewButtons();
            previewEnded.Start();
        }

        void ShowPreviewButtons()
        {
            // only the cards that have a preview (user 2026-10-17: "remove the play button from non music cards")
            foreach (ModCard card in list.Controls.OfType<ModCard>().Where(c => c.PreviewUrl != null)) card.ShowListen(card.PreviewUrl == previewPlaying);
        }

        // the window's uninstall of a GameBanana mod (its key, its name): true when it went (user 2026-10-16: "each card
        // should have an uninstall button in the browser too")
        public Func<string, string, Task<bool>> UninstallMod;

        // the size warnings for what an install put in (Weight), as the end of its message: they inform, nothing is stopped
        static string Notes(string game, ICollection<string> before)
        {
            var notes = Weight.New(game, before);
            return notes.Count > 0 ? ". Note: " + string.Join(" ", notes) : "";
        }

        // an install done: the cards made again while the page still counts as working (so they come greyed), and only
        // then the buttons back (SetWorking(false)), each as it should be (user 2026-10-16: when an install finished, every
        // card's button, Installed ones too, lit orange for a moment); its message is said after
        void Finished(string done)
        {
            if (done == null) return;
            var changed = Changed;
            if (changed != null) changed();
            Fill();
        }

        void SetWorking(bool on)
        {
            bool busy = on || chaining;
            working = busy;
            if (on && FooterSays != null && Progress == null) status.Text = "";   // its steps show in the footer now
            UseWaitCursor = on;
            foreach (Control c in list.Controls)
            {
                var card = c as ModCard;
                if (card != null) card.Enable(!busy);
                else if (c is FlatButton) c.Enabled = !busy || (c == updateAll && chaining) || (c == notifyAction && notifyChain);
                else foreach (FlatButton b in c.Controls.OfType<FlatButton>()) b.Enabled = !busy;
            }
            search.Enabled = !busy;
            if (heading != null) LayOutTop(topWidth);
            var handler = Working;
            if (handler != null) handler(busy);
        }

        void Say(string text, bool problem = false)
        {
            if (working && !problem && FooterSays != null && Progress == null) { FooterSays(text); return; }
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
            if (problem) BugReport.Noted(text);
        }
    }

    // One mod on the Browse Mods tab: its picture, name, maker (in gold, showing their mods), downloads, where it goes,
    // Install and a tiny blue icon opening its page; once it's in, where it went and "Installed", or Update when its
    // GameBanana files have changed since.
    class ModCard : Clear
    {
        public Image Picture;
        readonly string title, extra, goes, into;
        /// <summary>How much it downloads, under the downloads ("-": not known).</summary>
        public string FileSize;
        readonly bool outdated;
        public readonly FlatButton Install;
        public readonly HeartButton Heart = new HeartButton();
        public readonly WarningMark Warning = new WarningMark();   // a size warning for what it put in (Weight)
        Image section;   // its GameBanana section's icon, top right (Skins, Stages, a music section), or null

        // the section icons (user 2026-10-16: "an icon in the top right corner for the type of section it came from on game
        // banana"; their own art, res\icons): a section named for music, sound or audio gets the note
        static readonly Dictionary<string, Image> sectionIcons = new Dictionary<string, Image>();

        static Image SectionIcon(string category)
        {
            string c = (category ?? "").ToLowerInvariant();
            string name = c == "skins" ? "skins" : c == "stages" ? "stages" : c == "effects" ? "effects" : c.Contains("music") || c.Contains("sound") || c.Contains("audio") ? "music" : null;
            if (name == null) return null;
            Image icon;
            if (!sectionIcons.TryGetValue(name, out icon))
            {
                try { icon = Theme.Resource("icons." + name + ".png"); } catch (Exception) { icon = null; }
                sectionIcons[name] = icon;
            }
            return icon;
        }

        /// <summary>Its GameBanana section: its icon top right; the size warning, when there is one, just left of it.</summary>
        public void ShowSection(string category)
        {
            section = SectionIcon(category);
            Warning.Location = new Point(Width - 38 - (section != null ? 40 : 0), 12);
            Invalidate();
        }
        public readonly PageButton Page = new PageButton();   // its page on GameBanana
        public string Creator;   // Creators.Key of its mod
        public string Why;       // why it can't be installed, in place of where it goes: "Compatibility issue" (user 2026-10-13)
        public string WhyDetail; // the reason, shown while the pointer is on that line
        // the card's own (made the first time it's needed), gone with it: a shared one kept pointing at a card the list
        // had thrown away (Refresh making the cards again under the pointer) and the program stopped with "Cannot access
        // a disposed object" (user 2026-10-14)
        ToolTip whyTip;
        bool whyShown;
        Rectangle WhyArea { get { return new Rectangle(12, 210, Width - 24, 22); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = Why != null && WhyDetail != null && WhyArea.Contains(e.Location);
            if (over == whyShown) return;
            whyShown = over;
            if (whyTip == null) whyTip = new ToolTip();
            if (over) whyTip.Show(WhyDetail, this, WhyArea.Left, WhyArea.Bottom + 4); else whyTip.Hide(this);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (whyShown) { whyShown = false; whyTip.Hide(this); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && whyTip != null) { whyTip.Dispose(); whyTip = null; }
            base.Dispose(disposing);
        }
        public event EventHandler AuthorClicked;   // the creator's gold name: their mods are shown
        readonly GoldName author;
        int flash;

        /// <summary>The creator's Ko-fi and Patreon (GameBanana.Supports), left of the page icon (user 2026-10-14).</summary>
        public void ShowSupport(GameBanana.Support support, string creator, ToolTip tips)
        {
            int right = Page.Left - 2;
            foreach (var button in SupportButton.For(support))
            {
                button.Location = new Point(right - button.Width, Page.Top);
                right = button.Left - 2;
                tips.SetToolTip(button, "Support " + creator + " on " + (button.Kofi ? "Ko-fi" : "Patreon"));
                Controls.Add(button);
            }
        }

        public ModCard(string title, string authorName, string page, string extra, string goes, string into, bool outdated)
        {
            this.title = title;
            this.outdated = outdated;
            this.extra = extra;
            this.goes = goes;
            this.into = into;
            Size = new Size(286, 292);
            Warning.Location = new Point(Width - 38, 12);
            Controls.Add(Warning);
            author = new GoldName(string.IsNullOrEmpty(authorName) ? "its maker" : authorName);
            author.Click += (s, e) => { var handler = AuthorClicked; if (handler != null) handler(this, e); };
            int by = TextRenderer.MeasureText("by ", Theme.Font(9f)).Width - 4;
            // between "by" and the gold name: the heart that makes the creator a favorite (or not)
            Heart.Location = new Point(12 + by - 2, 182);
            Controls.Add(Heart);
            author.Location = new Point(Heart.Right + 1, 181);
            author.Width = Math.Min(author.Width, Width - 24 - by - 62);
            Controls.Add(author);
            Install = new FlatButton(into == null ? "Install" : outdated ? "Update" : "Installed", into == null || outdated);
            if (into != null && outdated) Install.Fill = Theme.Update;   // purple, as the tab's update dot (user 2026-10-13)
            Install.Location = new Point(12, Height - Install.Height - 12);
            Controls.Add(Install);
            if (into != null)   // installed: Uninstall beside it (user 2026-10-16)
            {
                Uninstall = new FlatButton("Uninstall") { Location = new Point(Install.Right + 6, Install.Top) };
                Controls.Add(Uninstall);
            }
            Page.Page = page;
            Page.Location = new Point(Width - 12 - Page.Width, Install.Top + (Install.Height - Page.Height) / 2 + 4);   // a little low (user 2026-10-16)
            Controls.Add(Page);
        }

        public readonly FlatButton Uninstall;   // on an installed mod's card only

        // a sound's preview: ▶ on its picture's lower left (■ while it plays)
        public readonly FlatButton Listen = new FlatButton("\u25B6") { Size = new Size(40, 34), Visible = false };
        public string PreviewUrl;

        public void ShowListen(bool playing)
        {
            Listen.Text = playing ? "\u25A0" : "\u25B6";
            Listen.Primary = playing;
            if (!Listen.Visible) { Listen.Location = new Point(10, 100); Controls.Add(Listen); Listen.Visible = true; Listen.BringToFront(); }
            Listen.Invalidate();
        }

        public void Enable(bool on)
        {
            if (Uninstall != null) Uninstall.Enabled = on;
            Install.Enabled = on && goes != null && (into == null || outdated) && !notReady;
        }

        bool notReady;

        /// <summary>Not installed, and its file still checking (or in its first 2 minutes): the button says so, greyed
        /// (user 2026-10-15: "say Not Ready").</summary>
        public void NotReady()
        {
            notReady = true;
            Install.Text = "Not Ready";
            Install.Width = Math.Max(Install.Width, TextRenderer.MeasureText(Install.Text, Install.Font).Width + 36);
            Install.Enabled = false;
        }

        // a short glow around the card it pointed to
        public void Flash()
        {
            var timer = new Timer { Interval = 60 };
            flash = 30;
            timer.Tick += (s, e) => { if (--flash <= 0 || IsDisposed) { timer.Dispose(); flash = 0; } if (!IsDisposed) Invalidate(); };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                g.SetClip(path);
                var picture = new RectangleF(1, 1, Width - 3, 140);
                using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
                if (Picture != null) Theme.Fit(g, Picture, picture, false);
                g.ResetClip();
                if (section != null) g.DrawImage(section, new Rectangle(Width - 41, 5, 34, 34));   // its GameBanana section, in the corner
                Color edge = flash > 0 && flash % 6 < 3 ? Theme.AccentHover : into != null ? Color.FromArgb(150, Theme.Accent) : Theme.Line;
                using (var pen = new Pen(edge, flash > 0 ? 2f : 1f)) g.DrawPath(pen, path);
            }
            Theme.Draw(g, title, Theme.Bold(10f), Theme.Text, new Rectangle(12, 146, Width - 24, 38), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, "by", Theme.Font(9f), Theme.Muted, new Rectangle(12, 182, 30, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (extra != "")
                Theme.Draw(g, "·   " + extra, Theme.Font(8.5f), Theme.Muted, new Rectangle(author.Right + 6, 182, Width - author.Right - 18, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(FileSize) && FileSize != "-")   // the file's size under the downloads (user 2026-10-16)
            {
                int at = author.Right + 6 + TextRenderer.MeasureText(g, "·   ", Theme.Font(8.5f), Size.Empty, TextFormatFlags.NoPadding).Width;
                Theme.Draw(g, FileSize, Theme.Font(8.5f), Theme.Muted, new Rectangle(at, 200, Width - at - 12, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            string where = Why ?? into ?? goes ?? "Nothing EX More Stuff can install";   // just whose it is (user: no "Goes in"); a reason first
            Theme.Draw(g, where, Theme.Font(9f), Why != null ? Theme.Warning : into != null ? Theme.GoodText : goes != null ? Theme.Text : Theme.Muted, new Rectangle(12, 216, Width - 24, 30),
                       TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
    // A Mod Pack on the Mod Packs page (user 2026-10-14: "each pack gets its own card"): its 4 newest mods' pictures as one
    // (CommunityPanel.PackPicture), its name, "by" its maker in gold and how many mods; its button (Install pack, UPDATE,
    // All installed) at the bottom left, its GameBanana page and its maker's Ko-fi / Patreon at the bottom right. A click on
    // the picture or the name shows its mods (Opened).
    class PackCard : Clear
    {
        public Image Picture;
        public readonly FlatButton Action = new FlatButton("Install pack", true);
        public readonly PageButton Page = new PageButton();
        public readonly HeartButton Heart = new HeartButton();   // its maker a favorite, as on mod cards (user 2026-10-14: "the card is missing the heart")
        public event EventHandler Opened, OwnerClicked;           // the picture or name: its mods; the gold name: its maker's mods
        readonly string title, count;
        readonly GoldName owner;
        bool hover;

        public PackCard(string name, string ownerName, int mods)
        {
            title = name;
            count = mods + (mods == 1 ? " mod" : " mods");
            Size = new Size(286, 292);
            // "by", the heart, the maker's name in gold: as a mod card has them
            Heart.Location = new Point(12 + TextRenderer.MeasureText("by ", Theme.Font(9f)).Width - 4 - 2, 182);
            Controls.Add(Heart);
            owner = new GoldName(string.IsNullOrEmpty(ownerName) ? "its maker" : ownerName) { Location = new Point(Heart.Right + 1, 181) };
            owner.Width = Math.Min(owner.Width, Width - 24 - owner.Left - 60);
            owner.Click += (s, e) => { var handler = OwnerClicked; if (handler != null) handler(this, e); };
            Controls.Add(owner);
            Action.Location = new Point(12, Height - Action.Height - 12);
            Controls.Add(Action);
            Page.Location = new Point(Width - 12 - Page.Width, Height - 12 - 36 + (36 - Page.Height) / 2 + 4);   // a little low (user 2026-10-16)
            Controls.Add(Page);
        }

        /// <summary>Its maker's Ko-fi and Patreon, left of the page icon.</summary>
        public void ShowSupport(GameBanana.Support support, string creator, ToolTip tips)
        {
            int right = Page.Left - 2;
            foreach (var button in SupportButton.For(support))
            {
                button.Location = new Point(right - button.Width, Page.Top);
                right = button.Left - 2;
                tips.SetToolTip(button, "Support " + creator + " on " + (button.Kofi ? "Ko-fi" : "Patreon"));
                Controls.Add(button);
            }
        }

        bool OnOpener(Point p) { return p.Y < 180; }   // the picture and the name

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = OnOpener(e.Location);
            Cursor = over ? Cursors.Hand : Cursors.Default;
            if (over != hover) { hover = over; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover) { hover = false; Invalidate(); } }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (OnOpener(e.Location)) { var handler = Opened; if (handler != null) handler(this, e); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Picture != null) { Picture.Dispose(); Picture = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                g.SetClip(path);
                var picture = new RectangleF(1, 1, Width - 3, 140);
                using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
                if (Picture != null) g.DrawImage(Picture, picture);
                g.ResetClip();
                using (var pen = new Pen(hover ? Theme.AccentHover : Theme.Line)) g.DrawPath(pen, path);
            }
            Theme.Draw(g, title, Theme.Bold(10f), hover ? Theme.AccentHover : Theme.Text, new Rectangle(12, 146, Width - 24, 38),
                       TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, "by", Theme.Font(9f), Theme.Muted, new Rectangle(12, 182, 30, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "·   " + count, Theme.Font(8.5f), Theme.Muted, new Rectangle(owner.Right + 6, 182, Width - owner.Right - 18, 22),
                       TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
