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
    // card names its maker in gold, and the name opens the mod's page. A skin installs in one click into its shared code
    // (SharedCodes), built with this PC's game files; a stage into a stage code of its own (ModFile), with its music.
    class CommunityPanel : Clear
    {
        public event Action Changed;          // EX More Stuff's lists need reading again
        public event Action<bool> Working;    // an install started or ended (the rest of the window waits)
        public Func<string> Blocked;          // why nothing can install right now, or null
        public event Action<int> UpdatesChanged;   // how many installed mods have a newer version on GameBanana (the tab's dot)

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
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill, AllowDrop = true };
        static readonly Dictionary<string, Task<Image>> thumbnails = new Dictionary<string, Task<Image>>();
        readonly ToolTip tips = new ToolTip { InitialDelay = 1000 };

        // the top of the page stays while the cards change, so the search box keeps its place as you type
        Heading heading;
        ShadowLabel note;
        readonly Clear tools = new Clear { Height = 40, Margin = new Padding(8, 2, 8, 2) };
        readonly TextBox search = new TextBox { Width = 220, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10.5f) };
        readonly FlatButton stagesButton = new FlatButton("Stages"), characterButton = new FlatButton("Character  ▾");
        readonly FlatButton byDate = new FlatButton("Newest", true), byCount = new FlatButton("Most Downloaded");
        readonly FlatButton updatesButton = new FlatButton("Updates"), updateAll = new FlatButton("Update all", true) { Margin = new Padding(8, 4, 8, 6) };
        HashSet<string> outdated = new HashSet<string>();   // installed mods with a newer version (their keys)
        int updatesTold = -1;
        bool chaining, stopChain;   // Update all running, and asked to stop after the one being updated
        string pick;   // what's shown: everything (null), "stages", "updates", or a fighter's code
        ToolStripDropDown picking;   // the Character list while it's open
        int pickerClosedAt;
        bool closedOnButton;
        public Func<string, Image> Portrait;   // the roster's portraits, for the Character list
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, ForeColor = Theme.Text, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4) };
        readonly Timer typing = new Timer { Interval = 300 };

        public CommunityPanel()
        {
            Controls.Add(list);
            AllowDrop = true;
            foreach (Control target in new Control[] { this, list })
            {
                target.DragEnter += (s, e) => e.Effect = Dropped(e) != null && !working ? DragDropEffects.Copy : DragDropEffects.None;
                target.DragDrop += async (s, e) => { string file = Dropped(e); if (file != null) await InstallFile(file); };
            }
            // Stages: only stages (again: everything); Character: the list of fighters to pick one from
            stagesButton.Click += (s, e) => { pick = pick == "stages" ? null : "stages"; shownCount = PageSize; Fill(); };
            // Character: back to all characters, and the list opens; again while it's open: it closes. (A click on the button
            // first closes the list as a click outside it, so a close that just happened there is that click's doing.)
            characterButton.Click += (s, e) =>
            {
                if (picking != null) { picking.Close(); return; }
                if (closedOnButton && Environment.TickCount - pickerClosedAt < 400) { closedOnButton = false; return; }
                if (pick != null) { pick = null; shownCount = PageSize; Fill(); }
                picking = CharacterPicker.Show(characterButton, null, Portrait, code => entries.Count(m => m.Fighters.Contains(code)),
                                               code => { pick = code; shownCount = PageSize; Fill(); });
                picking.Closed += (s2, e2) =>
                {
                    picking = null;
                    pickerClosedAt = Environment.TickCount;
                    closedOnButton = characterButton.ClientRectangle.Contains(characterButton.PointToClient(Cursor.Position));
                };
            };
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search mods");   // EM_SETCUEBANNER: the grey hint
            search.TextChanged += (s, e) => { if (!quiet) { typing.Stop(); typing.Start(); } };
            typing.Tick += (s, e) => { typing.Stop(); shownCount = PageSize; Fill(); };
            // two sort buttons: the other one switches to it; the one in use turns around (Newest <-> Oldest, Most <-> Least Downloaded)
            byDate.Click += (s, e) => { if (byDownloads) byDownloads = false; else newestFirst = !newestFirst; shownCount = PageSize; Fill(); };
            byCount.Click += (s, e) => { if (!byDownloads) byDownloads = true; else mostFirst = !mostFirst; shownCount = PageSize; Fill(); };
            // Updates: only the installed mods with a newer version (again: everything); Update all: each in turn
            updatesButton.Click += (s, e) => { pick = pick == "updates" ? null : "updates"; shownCount = PageSize; Fill(); };
            updateAll.Click += async (s, e) => await UpdateAll();
            tools.Controls.AddRange(new Control[] { search, stagesButton, characterButton, updatesButton, byDate, byCount });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        static string Dropped(DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            return files != null && files.Length == 1 && Regex.IsMatch(files[0], @"\.(zip|rar|7z)$", RegexOptions.IgnoreCase) ? files[0] : null;
        }

        bool stale;   // the lists changed while the page was hidden: its cards are built when it's shown

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
            if (Visible && !loaded && !loading) { var ignored = Load(); }
            else if (Visible && stale) Fill();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (game != null && Visible && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        async Task Load()
        {
            loading = true;
            Say("Reading GameBanana...");
            try { mods = await GameBanana.All(); holders = SharedCodes.Holders(mods); problem = null; }
            catch (Exception ex) { mods = null; problem = ex.Message; }
            loading = false;
            loaded = mods != null;
            entries = Merge();
            // read at start for the updates dot: the cards wait until the page is opened
            if (Visible) Fill(); else { stale = true; Recount(); }
            if (problem != null) Say("GameBanana isn't answering right now: check your internet connection, then try again later", true);
            else if (GameBanana.Stale) Say("GameBanana isn't answering right now, so this is its list " + Kept() + ". Installing needs it back", true);
            else if (status.ForeColor != Theme.Warning) Say("");
        }

        // GameBanana's skins, and its mods with stage files (STG_xxx.emz) in them
        static readonly Regex StageName = new Regex(@"^(STG_[A-Za-z0-9]{3}(\.tex)?\.emz|BGM_[A-Za-z0-9]{3}[23]?\.csb)$", RegexOptions.IgnoreCase);

        static bool StageFile(string path) { return StageName.IsMatch(path.Substring(path.LastIndexOf('/') + 1)); }

        // the stages a mod's files are made from (TRN, or codes of their own)
        static IEnumerable<string> StageCodes(GameBanana.Mod mod)
        {
            return mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents).Select(c => c.Substring(c.LastIndexOf('/') + 1))
                .Where(n => n.StartsWith("STG_", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".emz", StringComparison.OrdinalIgnoreCase) && n.Length >= 11)
                .Select(n => n.Substring(4, 3).ToUpperInvariant()).Distinct();
        }

        static bool HasStage(GameBanana.Mod mod)
        {
            return mod.Files.Where(f => f.Clean).SelectMany(f => f.Contents).Any(c => StageFile(c) && c.Substring(c.LastIndexOf('/') + 1).StartsWith("STG_", StringComparison.OrdinalIgnoreCase));
        }

        static string Kept() { return GameBanana.ReadAt == DateTime.MinValue ? "as EX More Stuff came with it" : "from " + GameBanana.ReadAt.ToString("d MMM yyyy"); }

        List<Entry> Merge()
        {
            if (mods == null) return new List<Entry>();
            return mods.Where(m => m.IsSkin || HasStage(m)).Select(m =>
            {
                var e = new Entry { Mod = m, Stage = HasStage(m), Fighters = new HashSet<string>(SkinImport.Named(m.Files.SelectMany(f => f.Contents)).Select(c => c.Item1)) };
                e.Words = (m.Name + " " + string.Join(" ", e.Fighters.Select(ExMoreStuff.Fighters.Name)) + (e.Stage ? " stage" : "")).ToLowerInvariant();
                return e;
            }).ToList();
        }

        // the cards to show: what Stages / Character picks and the search finds (every word; a modder by their whole name),
        // in the chosen order
        List<Entry> Shown()
        {
            string query = search.Text.Trim().ToLowerInvariant();
            string[] words = Regex.Split(query, @"\s+").Where(w => w != "").ToArray();
            var shown = entries.Where(e => pick == null || (pick == "stages" ? e.Stage : pick == "updates" ? outdated.Contains(e.Key) : e.Fighters.Contains(pick)))
                               .Where(e => words.All(w => e.Words.Contains(w)) || (query != "" && Squeezed(e.Mod.Author) == Squeezed(query)));
            if (byDownloads) return (mostFirst ? shown.OrderByDescending(e => e.Mod.Downloads) : shown.OrderBy(e => e.Mod.Downloads)).ToList();
            return (newestFirst ? shown.OrderByDescending(e => e.Date) : shown.OrderBy(e => e.Date)).ToList();
        }

        // which installed mods have a newer version on GameBanana; the tab's dot is told when that number changes
        Dictionary<string, InGame> Recount()
        {
            var installed = Installed();
            outdated = new HashSet<string>(entries.Where(e =>
            {
                InGame g;
                return installed.TryGetValue(e.Key, out g) && !g.Versions.All(v => v == "" || v == Signature(e.Mod));
            }).Select(e => e.Key));
            if (outdated.Count != updatesTold)
            {
                updatesTold = outdated.Count;
                var told = UpdatesChanged;
                if (told != null) told(outdated.Count);
            }
            return installed;
        }

        static string Squeezed(string text) { return Regex.Replace((text ?? "").ToLowerInvariant(), @"\s+", ""); }

        // which mods are in (installed or switched off): what they became, by card
        sealed class InGame
        {
            public readonly List<string> Into = new List<string>();      // "Blanka 08", "stage C72"
            public readonly List<string> Versions = new List<string>();  // the GameBanana files each was built from (Version)
        }

        Dictionary<string, InGame> Installed()
        {
            var result = new Dictionary<string, InGame>();
            if (!Game.IsGameFolder(game)) return result;
            foreach (Installed r in Installer.Load(game).Concat(Installer.Removed(game)))
            {
                Match m = Regex.Match(r.Item.Id ?? "", @"^(gamebanana:[^:]+:)");
                if (!m.Success) continue;
                string key = m.Groups[1].Value;
                if (!result.ContainsKey(key)) result[key] = new InGame();
                result[key].Into.Add(r.Item.IsStage ? "stage " + r.Item.Code : ExMoreStuff.Fighters.Name(r.Item.Fighter) + " " + r.Item.Slot.ToString("D2") +
                                     (r.Item.IsColor ? " color " + r.Item.Color : ""));
                result[key].Versions.Add(r.Item.Version ?? "");
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
            return mod.Files.Where(f => f.Clean && (SkinImport.Named(f.Contents).Any() || f.Contents.Any(StageFile)))
                .GroupBy(f => f.Base, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First()).ToList();
        }

        // where a mod goes, from the names in its files: "Blanka 08", "Blanka (only you see it)", "a stage of its own ..."
        string Where(GameBanana.Mod mod)
        {
            var costumes = SkinImport.Named(Wanted(mod).SelectMany(f => f.Contents)).ToList();
            var parts = costumes.Select(c =>
            {
                int slot = SharedCodes.Slot(mod, c.Item1, c.Item2, holders);
                return ExMoreStuff.Fighters.Name(c.Item1) + (slot > 0 ? " " + slot.ToString("D2") : " (only you see it)");
            }).Distinct().ToList();
            if (HasStage(mod))
            {
                string stageCode = StageCodes(mod).Select(c => SharedCodes.StageCode(mod, c)).FirstOrDefault(c => c != null);
                parts.Add(stageCode != null ? "stage " + stageCode : "a stage of its own (only you see it)");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // the top of the page: rebuilt only when the width changes; the sort buttons show their way and which is in use
        void LayOutTop(int width)
        {
            if (heading == null || topWidth != width)
            {
                if (heading != null) { heading.Dispose(); note.Dispose(); }
                topWidth = width;
                heading = new Heading("Browse Mods", width);
                note = ShadowLabel.Note("USF4's skins and stages on GameBanana, made and shared for free by its modders. Install puts one in as a costume or stage of its own, " +
                            "built with your own game's files, so the game's own stay as they are. A skin goes in a code that's the same for everyone who has it, so " +
                            "players see it on each other. Click a modder's name to visit the mod's page and thank them.", width);
                tools.Width = width - 16;
                status.Width = width - 16;
            }
            byDate.Text = newestFirst ? "Newest" : "Oldest";
            byCount.Text = mostFirst ? "Most Downloaded" : "Least Downloaded";
            byDate.Primary = !byDownloads;
            byCount.Primary = byDownloads;
            foreach (FlatButton b in new[] { byDate, byCount }) { b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 36; b.Invalidate(); }
            search.Location = new Point(0, (tools.Height - search.Height) / 2);
            stagesButton.Text = "Stages";
            characterButton.Text = (ExMoreStuff.Fighters.IsCode(pick ?? "") ? ExMoreStuff.Fighters.Name(pick) : "Character") + "  ▾";
            updatesButton.Text = "Updates (" + outdated.Count + ")";
            updatesButton.Primary = pick == "updates";
            updatesButton.Visible = outdated.Count > 0 || chaining;
            updateAll.Text = chaining ? (stopChain ? "Stopping..." : "Stop") : "Update all";
            updateAll.Primary = !chaining;
            stagesButton.Primary = pick == "stages";
            characterButton.Primary = ExMoreStuff.Fighters.IsCode(pick ?? "");
            foreach (FlatButton b in new[] { stagesButton, characterButton, updatesButton, updateAll }) { b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 36; b.Invalidate(); }
            stagesButton.Location = new Point(search.Right + 12, 2);
            characterButton.Location = new Point(stagesButton.Right + 6, 2);
            updatesButton.Location = new Point(characterButton.Right + 6, 2);
            byDate.Location = new Point((updatesButton.Visible ? updatesButton : characterButton).Right + 24, 2);
            byCount.Location = new Point(byDate.Right + 6, 2);
        }

        void Fill()
        {
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            stale = false;
            var installed = Recount();
            if (pick == "updates" && outdated.Count == 0 && !chaining) pick = null;
            LayOutTop(width);
            var top = pick == "updates" || chaining ? new Control[] { heading, note, tools, updateAll, status } : new Control[] { heading, note, tools, status };
            if (Array.IndexOf(top, updateAll) < 0 && updateAll.Parent == list) list.Controls.Remove(updateAll);
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
                var card = new ModCard(entry.Mod.Name, entry.Mod.Author, entry.Mod.Page, entry.Mod.Downloads.ToString("N0") + " downloads", Where(entry.Mod),
                                       inGame != null ? string.Join(", ", inGame.Into) : null, inGame != null && !current) { Margin = new Padding(6) };
                var mod = entry.Mod;
                card.Install.Click += async (s, e) => await Install(mod);
                card.Enable(!working);
                cards.Add(card);
                if (entry.Key == pointKey) pointed = card;
                var ignored = Thumbnail(entry.Mod.Thumbnail, card);
            }
            if (shown.Count == 0 && (loaded || entries.Count > 0))
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
        }

        static async Task<Image> Picture(string url)
        {
            try { byte[] bytes = await GameBanana.Bytes(url); return await Task.Run(() => Image.FromStream(new MemoryStream(bytes))); }
            catch (Exception) { return null; }
        }

        async Task Thumbnail(string url, ModCard card)
        {
            if (string.IsNullOrEmpty(url)) return;
            Task<Image> image;
            if (!thumbnails.TryGetValue(url, out image)) thumbnails[url] = image = Picture(url);
            Image picture = await image;
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
            string blocked = CantInstall();
            if (blocked != null) { Say("Can't install it now: " + blocked, true); return false; }
            if (!await MakeRoom(mod)) return false;
            SetWorking(true);
            string g = game, patch = Game.PatchFolder(game), done = null;
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
                var costumes = SkinImport.Find(everything);
                var stageFiles = everything.Where(f => StageFile(f.Key)).ToList();
                if (costumes.Count == 0 && stageFiles.Count == 0) throw new InvalidDataException("it has no costume or stage files EX More Stuff can use");
                byte[] picture = null;
                if (!string.IsNullOrEmpty(mod.Picture)) try { picture = await GameBanana.Bytes(mod.Picture); } catch (Exception) { }
                var into = new List<string>();
                bool personal = false, firstModel = false;
                var records = Installer.Load(g).Concat(Installer.Removed(g)).ToList();
                foreach (var costume in costumes)
                {
                    string id = "gamebanana:" + mod.Id + ":" + costume.Fighter + costume.Number.ToString("D2");
                    Installed again = records.FirstOrDefault(r => r.Item.Id == id);
                    int slot = again != null ? again.Item.Slot : SharedCodes.Slot(mod, costume.Fighter, costume.Number, holders);
                    if (slot == 0) { slot = SkinImport.FreeSlot(g, costume.Fighter); personal = true; }
                    if (slot == 0) throw new InvalidOperationException(Fighters.Name(costume.Fighter) + " has no free slot left in " + Catalog.FirstCustomCostume + "-" + Catalog.LastShared);
                    Installed other = records.FirstOrDefault(r => r.Item.IsCostume && r.Item.Fighter == costume.Fighter && r.Item.Slot == slot && r.Item.Id != id);
                    if (other != null)
                        throw new InvalidOperationException("your " + Fighters.Name(other.Item.Fighter) + " " + other.Item.TitleNamed(other.Shown ?? other.Item.Name) +
                                                            " is in the code this skin shares with everyone; give yours another code in Advanced > Package codes first");
                    string name = Regex.Replace(mod.Name, @"[^\w\s\-\.\(\)]", "").Trim();
                    string zip = Path.Combine(patch, "ex_more_stuff_mods", name + " - " + costume.Fighter + " " + slot.ToString("D2") + ".zip");
                    var item = new Item
                    {
                        Id = id, Type = "costume", Fighter = costume.Fighter, Slot = slot, Code = "", Name = mod.Name, Author = mod.Author, Version = Signature(mod),
                        Description = mod.Name + " by " + mod.Author + " on GameBanana (" + mod.Page + "): " + costume.Describe +
                                      (costume.Versions > 1 ? ", its " + costume.Versions + " versions as colors" : ""),
                        Picture = "", Download = "", Sha256 = "",
                    };
                    Say("Putting " + mod.Name + " in as " + Fighters.Name(costume.Fighter) + " " + slot.ToString("D2") + "...");
                    int s = slot;
                    await Task.Run(() =>
                    {
                        SkinImport.Build(g, costume, s, picture, zip);
                        Installer.InstallPackage(g, item, zip, "file");
                    });
                    into.Add(Fighters.Name(costume.Fighter) + " " + slot.ToString("D2"));
                    firstModel |= costume.ModelsDiffer;
                }
                var problems = new List<string>();
                if (stageFiles.Count > 0)
                {
                    Action<string> say = text => BeginInvoke((Action)(() => Say(text)));
                    var result = await Task.Run(() => ModFile.InstallFiles(g, stageFiles, "gamebanana:" + mod.Id, mod.Name, mod.Author, " on GameBanana (" + mod.Page + ")", picture, say, Signature(mod),
                                                                              code => SharedCodes.StageCode(mod, code)));
                    into.AddRange(result.Into);
                    problems.AddRange(result.Problems);
                    personal |= result.Into.Count > 0 && !result.Shared;
                }
                if (into.Count == 0) throw new InvalidOperationException(string.Join("; ", problems));
                done = mod.Name + " is in: " + string.Join(", ", into) + (personal ? " (only you see " + (into.Count > 1 ? "those" : "it") + ")" : ", the same for everyone who has it") +
                       (firstModel ? ". Its versions have models of their own; the first one's is used" : "") + (problems.Count > 0 ? ". Not put in: " + string.Join("; ", problems) : "");
            }
            catch (Exception ex) { Say(mod.Name + " wasn't installed: " + ex.Message, true); }
            finally { SetWorking(false); }
            Finished(done);
            return done != null;
        }

        // Before anything downloads: a seat this mod shares with everyone (a costume slot, a stage code) that one of the
        // player's own packages is in now. The player is asked to move theirs to a free seat (counting down from 84, or
        // C80-C99 for a stage); Package codes' move does it (its zip rewritten, its name kept). False: not installed.
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
            if (target == null) { Say(seat + " is where " + mod.Name + " goes for everyone, and your " + yours + " is there: there's no free seat to move yours to", true); return false; }
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

        string Picked() { return pick == "stages" ? "stages" : pick == "updates" ? "updates" : ExMoreStuff.Fighters.Name(pick); }

        /// <summary>Installs a mod file (Advanced > Add Mod From File for anything that isn't an EX More Stuff package, or
        /// a file dropped here): a GameBanana file points to its card (its shared code), anything else goes in as it is,
        /// in personal slots and codes.</summary>
        public async Task InstallFile(string path)
        {
            string blocked = CantInstall();
            if (blocked != null) { Say("Can't install it now: " + blocked, true); return; }
            SetWorking(true);
            string g = game, done = null;
            try
            {
                string md5 = await Task.Run(() => ModFile.Md5(path));
                GameBanana.Mod same = mods == null ? null : mods.FirstOrDefault(m => m.Files.Any(f => f.Md5 == md5));
                if (same != null)
                {
                    Highlight("gamebanana:" + same.Id + ":");
                    Say("That file is " + same.Name + " from GameBanana: press Install on its card (below) so it goes in its shared code");
                    return;
                }
                Action<string> say = text => BeginInvoke((Action)(() => Say(text)));
                var result = await Task.Run(() => ModFile.Install(g, path, null, null, "", null, null, say));
                if (result.Into.Count == 0) throw new InvalidOperationException(string.Join("; ", result.Problems));
                done = ModFile.NameFrom(path) + " is in: " + string.Join(", ", result.Into) + " (only you see it)" +
                       (result.Problems.Count > 0 ? ". Not put in: " + string.Join("; ", result.Problems) : "");
            }
            catch (Exception ex) { Say("It wasn't installed: " + ex.Message, true); }
            finally { SetWorking(false); }
            Finished(done);
        }

        void Finished(string done)
        {
            if (done == null) return;
            var changed = Changed;
            if (changed != null) changed();
            Fill();
            Say(done);
        }

        void SetWorking(bool on)
        {
            bool busy = on || chaining;
            working = busy;
            UseWaitCursor = on;
            foreach (Control c in list.Controls)
            {
                var card = c as ModCard;
                if (card != null) card.Enable(!busy);
                else if (c is FlatButton) c.Enabled = !busy || (c == updateAll && chaining);
                else foreach (FlatButton b in c.Controls.OfType<FlatButton>()) b.Enabled = !busy;
            }
            search.Enabled = !busy;
            if (heading != null) LayOutTop(topWidth);
            var handler = Working;
            if (handler != null) handler(busy);
        }

        void Say(string text, bool problem = false)
        {
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
        }
    }

    // One mod on the Browse Mods tab: its picture, name, maker (in gold, opening its page), downloads, where it goes, and
    // Install; once it's in, where it went and "Installed", or Update when its GameBanana files have changed since.
    class ModCard : Clear
    {
        public Image Picture;
        readonly string title, extra, goes, into;
        readonly bool outdated;
        public readonly FlatButton Install;
        readonly GoldName author;
        int flash;

        public ModCard(string title, string authorName, string page, string extra, string goes, string into, bool outdated)
        {
            this.title = title;
            this.outdated = outdated;
            this.extra = extra;
            this.goes = goes;
            this.into = into;
            Size = new Size(286, 292);
            author = new GoldName(string.IsNullOrEmpty(authorName) ? "its maker" : authorName) { Link = page };
            int by = TextRenderer.MeasureText("by ", Theme.Font(9f)).Width - 4;
            author.Location = new Point(12 + by, 181);
            author.Width = Math.Min(author.Width, Width - 24 - by - 40);
            Controls.Add(author);
            Install = new FlatButton(into == null ? "Install" : outdated ? "Update" : "Installed", into == null || outdated);
            Install.Location = new Point(12, Height - Install.Height - 12);
            Controls.Add(Install);
        }

        public void Enable(bool on)
        {
            Install.Enabled = on && goes != null && (into == null || outdated);
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
                Color edge = flash > 0 && flash % 6 < 3 ? Theme.AccentHover : into != null ? Color.FromArgb(150, Theme.Accent) : Theme.Line;
                using (var pen = new Pen(edge, flash > 0 ? 2f : 1f)) g.DrawPath(pen, path);
            }
            Theme.Draw(g, title, Theme.Bold(10f), Theme.Text, new Rectangle(12, 146, Width - 24, 38), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, "by", Theme.Font(9f), Theme.Muted, new Rectangle(12, 182, 30, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (extra != "")
                Theme.Draw(g, "·   " + extra, Theme.Font(8.5f), Theme.Muted, new Rectangle(author.Right + 6, 182, Width - author.Right - 18, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            string where = into != null ? "In: " + into : goes != null ? "Goes in: " + goes : "Nothing EX More Stuff can install";
            Theme.Draw(g, where, Theme.Font(9f), into != null ? Theme.GoodText : goes != null ? Theme.Text : Theme.Muted, new Rectangle(12, 210, Width - 24, 34),
                       TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
