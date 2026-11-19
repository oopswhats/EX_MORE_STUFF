using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // The Music tab (user 2026-10-15: "its just like the stages panel, you pick a stage and pick a song"; their mockup):
    // STAGES or CHARACTERS at the top, then a horizontal card each, two to a row: its picture and name, the song it plays
    // (in our orange when it's been changed), ▶ to hear 5 seconds of it. The stages as on the Stages tab (custom ones,
    // then the game's), the 44 fighters as on the Costumes tab. A click opens SongDialog. It acts at once, as Advanced >
    // Music does (the game must be closed); Advanced > Music, the song editor, stays as it is.
    class MusicTab : Clear
    {
        string game;
        static int view;   // what's shown: 0 STAGES, 1 CHARACTERS, 2 MANAGE MUSIC (kept while EX More Stuff is open)
        static bool fighters { get { return view == 1; } }
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4) };
        readonly BigToggle stagesButton = new BigToggle("STAGES"), fightersButton = new BigToggle("CHARACTERS"), manageButton = new BigToggle("MANAGE MUSIC");
        // STAGES and CHARACTERS on the left, MANAGE MUSIC on the far right (user 2026-10-16)
        readonly Clear toggles = new Clear { Height = 54, Margin = new Padding(6, 4, 6, 10) };
        // MANAGE MUSIC's order (user 2026-11-17: "a sorting toggle newest/a-z. no need for oldest"), kept while EX More Stuff is open
        static bool byName;
        readonly FlatButton newestButton = new FlatButton("Newest"), nameButton = new FlatButton("A-Z");
        readonly Clear sortRow = new Clear { Height = 34, Margin = new Padding(6, 0, 6, 6) };
        static readonly Dictionary<string, Image> portraits = new Dictionary<string, Image>();
        // the player at the bottom (user 2026-10-16: "a second bottom bar ... a scroll bar for the track, play and pause,
        // volume. default start volume here at 33%"): this tab's own, nothing shared with Advanced > Music
        readonly WavePlayer player = new WavePlayer();
        readonly PlayerBar bar = new PlayerBar { Dock = DockStyle.Bottom };
        readonly Timer ticking = new Timer { Interval = 100 };
        short[] track;              // the song in the bar, decoded; null: none yet
        int trackLoop;              // where it loops back to (the game's loop: it plays on as in the game)
        long pausedAt = -1;         // paused: the frame it stopped at
        string trackKey;            // which card's song it is (its slot's file)
        SlotCard trackCard;
        int filledWidth;
        bool stale = true;

        public event Action<bool> Working;   // a song going in (the rest of the window waits)
        public event Action Changed;         // songs changed (Advanced > Music and Disk space read them again)

        public MusicTab()
        {
            Controls.Add(list);
            Controls.Add(bar);
            toggles.Controls.AddRange(new Control[] { stagesButton, fightersButton, manageButton });
            stagesButton.Click += (s, e) => ShowView(0);
            fightersButton.Click += (s, e) => ShowView(1);
            manageButton.Click += (s, e) => ShowView(2);
            sortRow.Controls.AddRange(new Control[] { newestButton, nameButton });
            newestButton.Click += (s, e) => SortBy(false);
            nameButton.Click += (s, e) => SortBy(true);
            player.Volume = (float)bar.Volume.Value;
            bar.Volume.Moved += () => player.Volume = (float)bar.Volume.Value;
            bar.Toggle.Click += (s, e) => Toggle();
            bar.Seek.Moved += () => { if (track != null) bar.ShowTime((long)(bar.Seek.Value * Frames), Frames); };
            bar.Seek.Done += () => SeekTo((long)(bar.Seek.Value * Frames));
            ticking.Tick += (s, e) => Tick();
        }

        long Frames { get { return track == null ? 0 : track.Length / 2; } }

        // the bar follows the song (not while its knob is held)
        void Tick()
        {
            bool playing = track != null && player.Playing && pausedAt < 0;
            if (track != null)
            {
                long at = playing ? player.Position : pausedAt;
                if (at < 0) at = 0;
                if (!bar.Seek.Held) { bar.Seek.Value = Frames > 0 ? (double)at / Frames : 0; bar.ShowTime(at, Frames); }
            }
            bar.Playing = playing;
            if (trackCard != null && !trackCard.IsDisposed) trackCard.Playing = playing;
            if (!playing) ticking.Stop();
        }

        void Toggle()
        {
            if (track == null) return;
            if (player.Playing && pausedAt < 0) { pausedAt = Math.Max(0, player.Position); player.Stop(); }
            else PlayFrom(pausedAt >= 0 ? pausedAt : 0);
            Tick();
        }

        void SeekTo(long frame)
        {
            if (track == null) return;
            frame = Math.Max(0, Math.Min(Frames - 1, frame));
            if (player.Playing && pausedAt < 0) PlayFrom(frame); else pausedAt = frame;
            Tick();
        }

        void PlayFrom(long frame)
        {
            try { player.Play(track, frame, true, trackLoop, (int)Frames); pausedAt = -1; ticking.Start(); }
            catch (Exception ex) { Say(ex.Message, true); }
        }

        // paused where it is (leaving the tab, picking a song)
        void StopAll()
        {
            if (player.Playing && pausedAt < 0) pausedAt = Math.Max(0, player.Position);
            player.Stop();
            Tick();
        }

        void ShowView(int which)
        {
            if (view == which) return;
            view = which;
            Fill();
            list.AutoScrollPosition = Point.Empty;
        }

        void SortBy(bool name)
        {
            if (byName == name) return;
            byName = name;
            Fill();
            list.AutoScrollPosition = Point.Empty;
        }

        public void SetGame(string folder)
        {
            game = folder;
            if (Visible) Fill(); else stale = true;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && stale) Fill();
            if (!Visible) StopAll();   // leaving the tab pauses it
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // (also when it was built before the window measured it: at the 400 floor, 4 cards a row, user 2026-10-16)
            if (game != null && Visible && list.ClientSize.Width > 200 && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        // a card each: (the slot, its name, its picture or null while it loads, a docked stage's zip or null)
        List<Tuple<MusicSlot, string, Func<Image>, string>> Slots()
        {
            var slots = new List<Tuple<MusicSlot, string, Func<Image>, string>>();
            if (fighters)
            {
                foreach (int id in Fighters.DisplayOrder)
                {
                    string code = Fighters.Codes[id];
                    slots.Add(Tuple.Create(new MusicSlot { Code = code, Fighter = true }, Fighters.Names[id], (Func<Image>)(() => { Image p; return portraits.TryGetValue(code, out p) ? p : null; }), (string)null));
                }
                return slots;
            }
            // docked stages first, as on Advanced > Music (user 2026-11-18: "files are docked but i cant change it in the music
            // tab"): a song picked goes into the docked file (DockedMusic), not the game
            if (Game.IsGameFolder(game))
                foreach (Installed d in Docked.Load().Where(r => r.Item.IsStage))
                {
                    var slot = new MusicSlot { Code = d.Item.Code };
                    if (MusicBank.GameFile(game, slot.TemplateFile) == null) continue;   // no stand-in music to build on
                    Image picture = Picture(d);
                    string name = Regex.Replace(d.Item.Name, @"\s*-\s*" + Regex.Escape(d.Item.Code) + "$", "", RegexOptions.IgnoreCase);   // as installed ones show (a code didn't fit)
                    slots.Add(Tuple.Create(slot, name, (Func<Image>)(() => picture), d.Package));
                }
            // the game's (its stage select's order), then GameBanana's, then the player's own (user 2026-10-16: "vanilla
            // stages > custom banana stages > user insert stages")
            foreach (string code in StageGrid.Grid.Where(c => c != null))
            {
                string c = code;
                slots.Add(Tuple.Create(new MusicSlot { Code = c }, Stages.Name(c), (Func<Image>)(() => { try { return Stages.Picture(c); } catch (Exception) { return null; } }), (string)null));
            }
            if (Game.IsGameFolder(game))
                foreach (Installed r in Installer.Load(game).Where(r => r.Item.IsStage).OrderBy(r => (r.Item.Id ?? "").StartsWith("gamebanana:") ? 0 : 1).ThenBy(r => r.Item.Code))
                {
                    Installed record = r;
                    Image picture = Picture(record);
                    slots.Add(Tuple.Create(new MusicSlot { Code = record.Item.Code }, record.Shown ?? record.Item.Name, (Func<Image>)(() => picture), (string)null));
                }
            return slots;
        }

        // a card's song in the bar: its slot's file (a docked stage's: its zip's too, as the same code may be installed)
        static string TrackKey(MusicSlot slot, string docked) { return docked == null ? slot.File : "docked:" + docked + "|" + slot.File; }

        // the song a docked stage's round plays from its docked file, put there from the Music tab or Advanced > Music; null: none
        static string DockedPlaying(string zip, MusicSlot slot)
        {
            try { InstalledSong s = DockedMusic.Song(zip, slot.File); return s == null ? null : s.Layers[0] ?? s.Song; }
            catch (Exception) { return null; }
        }

        void Fill()
        {
            stale = false;
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            Dictionary<string, InstalledSong> installed;
            try { installed = Game.IsGameFolder(game) ? MusicBank.Load(game) : new Dictionary<string, InstalledSong>(); }
            catch (Exception ex) { installed = new Dictionary<string, InstalledSong>(); Say(ex.Message, true); }
            stagesButton.Selected = view == 0;
            fightersButton.Selected = view == 1;
            manageButton.Selected = view == 2;
            toggles.Width = width - 12;
            stagesButton.Location = Point.Empty;
            fightersButton.Location = new Point(stagesButton.Right + 8, 0);
            manageButton.Location = new Point(toggles.Width - manageButton.Width, 0);
            foreach (var b in new[] { stagesButton, fightersButton, manageButton }) b.Invalidate();
            status.Width = width - 16;
            var controls = new List<Control>
            {
                new Heading("Music", width),
                ShadowLabel.Note("Pick a stage or a fighter, then the song it plays. Songs come from the game's stages and fighters, the stages you installed, and song mods.", width),
                toggles,
                status,
            };
            var cards = new List<SlotCard>();
            int cardWidth = (width - 2 * 12) / 2;
            if (view == 2) controls.AddRange(ManageRows(width));
            else
            {
                var all = Slots();
                bool anyDocked = all.Any(s => s.Item4 != null), stagesShown = false;
                if (anyDocked) controls.Add(new Heading("Docked", width) { Font = Theme.Bold(11f), Height = 34 });
                foreach (var s in all)
                {
                    MusicSlot slot = s.Item1;
                    string name = s.Item2, docked = s.Item4, playing = docked != null ? DockedPlaying(docked, slot) : MusicLibrary.Playing(installed, slot);
                    if (anyDocked && docked == null && !stagesShown) { controls.Add(new Heading("Stages", width) { Font = Theme.Bold(11f), Height = 34 }); stagesShown = true; }
                    var card = new SlotCard
                    {
                        Size = new Size(cardWidth, 84), Margin = new Padding(6, 4, 6, 4), Picture = s.Item3(), Caption = name, Fighter = slot.Fighter,
                        Song = playing ?? MusicLibrary.OwnName(slot, name), Changed = playing != null,
                    };
                    card.Clicked += () => Pick(slot, name, docked);
                    card.Play.Click += (o, e) => Listen(slot, name, card, docked);
                    if (trackKey == TrackKey(slot, docked)) { trackCard = card; card.Playing = player.Playing && pausedAt < 0; }
                    cards.Add(card);
                    controls.Add(card);
                }
            }
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != status && c != toggles && c != sortRow) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); if (!(c is SlotCard)) list.SetFlowBreak(c, true); }
            list.ResumeLayout();
            if (fighters) LoadPortraits(cards);
        }

        // MANAGE MUSIC (user 2026-10-16: "lets you uninstall songs youve created in the advanced tab and also music you
        // downloaded"): a row each, its name, where it's from and Uninstall
        List<Control> ManageRows(int width)
        {
            var rows = new List<Control>();
            var owned = Game.IsGameFolder(game) ? MusicLibrary.Owned(game) : new List<MusicLibrary.Owner>();
            if (owned.Count == 0)
            {
                rows.Add(ShadowLabel.Note("No songs of your own yet: songs you make on Advanced > Music and song mods you install from Browse Mods show here.", width));
                return rows;
            }
            newestButton.Primary = !byName;
            nameButton.Primary = byName;
            int x = 0;
            foreach (FlatButton b in new[] { newestButton, nameButton })
            {
                b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 30;
                b.Location = new Point(x, 0);
                x = b.Right + 8;
                b.Invalidate();
            }
            sortRow.Width = width - 12;
            rows.Add(sortRow);
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            foreach (var o in byName ? owned.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList() : owned.OrderByDescending(o => o.Added).ToList())
            {
                MusicLibrary.Owner song = o;
                var row = new ManageRow { Size = new Size(width - 12, 56), Margin = new Padding(6, 3, 6, 3), Title = song.Name,
                                       Detail = song.From + (song.Added > DateTime.MinValue ? "  ·  Added " + song.Added.ToString("MMM d, yyyy", culture) + ", " + song.Added.ToString("t", culture) : "") };
                row.LoadThumbnail(song.PictureZip);
                row.Uninstall.Click += (s, e) => Uninstall(song);
                rows.Add(row);
            }
            return rows;
        }

        void Uninstall(MusicLibrary.Owner song)
        {
            if (MessageBox.Show(FindForm(), "Uninstall " + song.Name + "? Its files go to the Recycle Bin. A stage or fighter playing it now keeps it until you pick another song.",
                                "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { MusicLibrary.Uninstall(song); Say(song.Name + " is uninstalled"); }
            catch (Exception ex) { Say(song.Name + " wasn't uninstalled: " + ex.Message, true); }
            int scrolled = -list.AutoScrollPosition.Y;
            Fill();
            list.AutoScrollPosition = new Point(0, scrolled);
            var changed = Changed;
            if (changed != null) changed();
        }

        static Image Picture(Installed record)
        {
            try
            {
                byte[] png = Installer.PackagePicture(record.Package, record.Item);
                return png != null ? Pictures.Read(png) : null;
            }
            catch (Exception) { return null; }
        }

        async void LoadPortraits(List<SlotCard> cards)
        {
            string folder = game;
            foreach (int id in Fighters.DisplayOrder)
            {
                string code = Fighters.Codes[id];
                Image image;
                if (!portraits.TryGetValue(code, out image))
                {
                    try { image = await Task.Run(() => (Image)GameArt.FighterPortrait(folder, code, 300)); } catch (Exception) { image = null; }
                    if (image != null) portraits[code] = image;
                }
                var card = cards.FirstOrDefault(c => c.Caption == Fighters.Name(code));
                if (card == null || card.IsDisposed) return;
                card.Picture = image;
                card.Invalidate();
            }
        }

        // a card's ▶: its song in the bar, from 30 seconds in (user: "listen to a part of it 30 seconds in"); pressed again,
        // paused, then on from there (user 2026-10-16: "if i press it again its just play again")
        async void Listen(MusicSlot slot, string name, SlotCard card, string docked)
        {
            string key = TrackKey(slot, docked);
            if (trackKey == key && track != null) { trackCard = card; Toggle(); return; }
            var song = docked != null ? MusicLibrary.DockedNow(game, docked, slot, card.Song) : MusicLibrary.Now(game, slot, card.Song);
            if (song == null) return;
            StopAll();
            if (trackCard != null && !trackCard.IsDisposed) trackCard.Playing = false;
            bar.Show(card.Song, name);
            short[] pcm;
            int loop = 0;
            try { pcm = await Task.Run(() => MusicLibrary.Decode(song, out loop)); }
            catch (Exception ex) { Say("It can't be played: " + ex.Message, true); return; }
            if (card.IsDisposed || !Visible) return;
            track = pcm;
            trackLoop = loop;
            trackKey = key;
            trackCard = card;
            PlayFrom(Frames > 40 * Song.Rate ? 30 * Song.Rate : 0);
            Tick();
        }

        // a card clicked: its songs, and the one picked goes in
        // (a docked stage's, `docked` its zip: into the docked file, so the game may be open)
        async void Pick(MusicSlot slot, string name, string docked)
        {
            StopAll();
            List<MusicLibrary.Entry> songs;
            Dictionary<string, InstalledSong> installed;
            try { songs = MusicLibrary.Songs(game); installed = MusicBank.Load(game); }
            catch (Exception ex) { Say(ex.Message, true); return; }
            // what each round plays (a fighter's theme: every round, round 1's)
            var playing = new string[3];
            for (int r = 1; r <= (slot.Fighter ? 1 : 3); r++) playing[r - 1] = docked != null ? DockedPlaying(docked, slot.InRound(r)) : MusicLibrary.Playing(installed, slot.InRound(r));
            MusicLibrary.Entry[] picks;
            bool own;
            using (var ask = new SongDialog(name, slot, songs, playing, (float)bar.Volume.Value))
            {
                if (ask.ShowDialog(FindForm()) != DialogResult.OK) return;
                picks = ask.Picks;
                own = ask.Own;
            }
            if (docked == null && Game.IsRunning()) { Say("Close the game first", true); return; }
            // one song for every round: round 1's file, no round 2 or 3 of its own (the game plays it all match)
            bool every = !own && picks[0] != null && (slot.Fighter || (picks[1] == picks[0] && picks[2] == picks[0]));
            // its own song picked for round 1: its own music back, rather than a copy of it (a docked stage's: the same code
            // installed is another file, its song put in whole)
            string ownKey = slot.Fighter ? "fighter:" + slot.Code : Stages.IsCustomCode(slot.Code) ? "custom:" + slot.Code : "stage:" + slot.Code;
            bool ownFirst = docked == null && picks[0] != null && picks[0].Key == ownKey;
            string g = game, problem = null;
            Busy(true);
            Say(own ? "Giving " + name + " its own music..." : "Putting the music on " + name + "...");
            try
            {
                problem = await Task.Run(() =>
                {
                    if (docked != null)
                    {
                        Action<int> takeOut = r => MusicLibrary.TakeOutDocked(docked, slot.InRound(r));
                        if (own) { takeOut(1); takeOut(2); takeOut(3); }
                        else if (every) { MusicLibrary.UseDocked(g, docked, slot, picks[0]); takeOut(2); takeOut(3); }
                        else for (int r = 1; r <= 3; r++) if (picks[r - 1] != null) MusicLibrary.UseDocked(g, docked, slot.InRound(r), picks[r - 1]);
                        return (string)null;
                    }
                    var notes = new List<string>();
                    Action<int> off = r => { string n = MusicLibrary.Own(g, slot.InRound(r)); if (n != null) notes.Add(n); };
                    if (own) { off(1); if (!slot.Fighter) { off(2); off(3); } }
                    else if (every) { if (ownFirst) off(1); else MusicLibrary.Use(g, slot, picks[0]); if (!slot.Fighter) { off(2); off(3); } }
                    else
                    {
                        // a song for each round picked; a round picked for none stays as it is
                        if (picks[0] != null) { if (ownFirst) off(1); else MusicLibrary.Use(g, slot, picks[0]); }
                        for (int r = 2; r <= 3; r++) if (picks[r - 1] != null) MusicLibrary.Use(g, slot.InRound(r), picks[r - 1]);
                        if (picks[1] != null || picks[2] != null) { string mod = RoundMod.Ensure(g); if (mod != null) notes.Add(mod); }   // Tom's Round BGM mod plays rounds 2 and 3
                    }
                    RoundMod.RemoveIfUnused(g);   // no round 2 or 3 song left: Tom's mod goes too
                    return notes.Count > 0 ? string.Join("; ", notes) : null;
                });
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { Busy(false); }
            if (trackKey == TrackKey(slot, docked)) { track = null; trackKey = null; bar.Show(null, null); Tick(); }   // its song changed: out of the bar
            int scrolled = -list.AutoScrollPosition.Y;
            Fill();
            list.AutoScrollPosition = new Point(0, scrolled);
            var changed = Changed;
            if (changed != null) changed();
            if (problem != null) Say(problem, true);
            else if (docked != null && own) Say(name + "'s music is out of the docked file");   // as Advanced > Music says it
            else if (docked != null && every) Say(picks[0].Name + " is in the docked file for " + name);
            else if (docked != null) Say(string.Join(", ", Enumerable.Range(1, 3).Where(r => picks[r - 1] != null).Select(r => picks[r - 1].Name + " for round " + r)) + " in the docked file for " + name);
            else if (own) Say(name + " plays its own music again");
            else if (every) Say(name + " plays " + picks[0].Name + " now");
            else Say(name + " plays " + string.Join(", ", Enumerable.Range(1, 3).Where(r => picks[r - 1] != null).Select(r => picks[r - 1].Name + " in round " + r)) + " now");
        }

        void Busy(bool on)
        {
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

    // The Music tab's player (user 2026-10-16): ▶/❚❚, the song and whose it is, the track to drag through with its time,
    // and the volume, 33% to begin with each time EX More Stuff opens.
    class PlayerBar : Clear
    {
        public readonly FlatButton Toggle = new FlatButton("▶");
        public readonly Slider Seek = new Slider(), Volume = new Slider { Value = 0.33 };
        readonly Label title = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Bold(10f), AutoEllipsis = true };
        readonly Label of = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(8.5f), AutoEllipsis = true };
        readonly Label time = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(8.5f), TextAlign = ContentAlignment.MiddleRight };
        readonly Label speaker = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = new Font("Segoe MDL2 Assets", 12f), Text = "", TextAlign = ContentAlignment.MiddleCenter };   // Windows' own volume sign
        // named as on Advanced > Music (user 2026-10-16: "in the music tab it needs to say preview volume also")
        readonly Label volumeName = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Bold(9f), Text = "Preview Volume", TextAlign = ContentAlignment.MiddleRight };
        bool playing;

        public PlayerBar()
        {
            Height = 64;
            Toggle.Size = new Size(48, 44);
            Toggle.Font = Theme.Bold(14f);
            Controls.AddRange(new Control[] { Toggle, title, of, Seek, time, volumeName, speaker, Volume });
            Show(null, null);
        }

        public bool Playing
        {
            get { return playing; }
            set { if (playing == value) return; playing = value; Toggle.Text = value ? "❚❚" : "▶"; Toggle.Invalidate(); }
        }

        /// <summary>The song in the bar (null: none yet).</summary>
        public void Show(string song, string whose)
        {
            title.Text = song ?? "Nothing playing";
            of.Text = song == null ? "Press ▶ on a card to hear it" : whose;
            Toggle.Enabled = song != null;
            Seek.Enabled = song != null;
            ShowTime(0, 0);
        }

        public void ShowTime(long frame, long frames)
        {
            time.Text = frames <= 0 ? "" : Clock(frame) + " / " + Clock(frames);
        }

        static string Clock(long frames) { long s = frames / Song.Rate; return s / 60 + ":" + (s % 60).ToString("00"); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Toggle.Location = new Point(12, (Height - Toggle.Height) / 2);
            int left = Toggle.Right + 12, volume = 120, right = Width - 12 - volume - 30, named = 110;
            title.Bounds = new Rectangle(left, 8, 230, 22);
            of.Bounds = new Rectangle(left, 30, 230, 18);
            int seekLeft = title.Right + 12;
            volumeName.Bounds = new Rectangle(right - named, 0, named, Height);
            time.Bounds = new Rectangle(volumeName.Left - 90, 0, 84, Height);
            Seek.Bounds = new Rectangle(seekLeft, (Height - 24) / 2, Math.Max(40, time.Left - seekLeft - 8), 24);
            speaker.Bounds = new Rectangle(right, 0, 30, Height);
            Volume.Bounds = new Rectangle(speaker.Right, (Height - 24) / 2, volume, 24);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var fill = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(fill, ClientRectangle);
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }

    // A track to drag along (the song's place, the volume): our orange up to the knob. Moved while it's dragged, Done
    // when it's let go.
    class Slider : Clear
    {
        double value;
        public bool Held { get; private set; }
        public event Action Moved, Done;

        public Slider() { Cursor = Cursors.Hand; }

        public double Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        void At(int x)
        {
            Value = (x - 8) / (double)Math.Max(1, Width - 16);
            if (Moved != null) Moved();
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (!Enabled || e.Button != MouseButtons.Left) return; Held = true; At(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Held) At(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (!Held) return; Held = false; At(e.X); if (Done != null) Done(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            float y = Height / 2f, x0 = 8, x1 = Width - 8, knob = x0 + (float)(value * (x1 - x0));
            using (var back = new Pen(Theme.Well, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(back, x0, y, x1, y);
            if (Enabled)
            {
                using (var done = new Pen(Theme.Accent, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(done, x0, y, knob, y);
                using (var dot = new SolidBrush(Theme.Accent)) g.FillEllipse(dot, knob - 7, y - 7, 14, 14);
            }
        }
    }

    // One song of the player's on MANAGE MUSIC: its name, where it's from, Uninstall.
    class ManageRow : Clear
    {
        public string Title, Detail;
        public readonly FlatButton Uninstall = new FlatButton("Uninstall");

        public ManageRow() { Controls.Add(Uninstall); }

        System.Drawing.Image thumbnail;   // its zip's picture (MusicLibrary.Thumbnail), or none

        public async void LoadThumbnail(string zip)
        {
            if (zip == null) return;
            var made = await Task.Run(() => MusicLibrary.Thumbnail(zip));
            if (IsDisposed || made == null) return;
            thumbnail = made;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Uninstall.Location = new Point(Width - Uninstall.Width - 12, (Height - Uninstall.Height) / 2); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 9))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            int right = Uninstall.Left - 12, left = 14;
            if (thumbnail != null) { SongRow.DrawThumbnail(g, thumbnail, new Rectangle(10, (Height - 40) / 2, 71, 40)); left = 92; }
            Theme.Draw(g, Title, Theme.Bold(10.5f), Theme.Text, new Rectangle(left, 6, right - left, 24), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(left, 30, right - left, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // STAGES / CHARACTERS: big, the one shown in our orange (the user's mockup)
    class BigToggle : Clear
    {
        public bool Selected;
        bool hover;

        public BigToggle(string text)
        {
            Text = text;
            Font = Theme.Bold(22f);
            Cursor = Cursors.Hand;
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 28, 54);
            Margin = new Padding(0, 0, 8, 0);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6))
            using (var fill = new SolidBrush(Selected ? Theme.Accent : hover ? Theme.ButtonHover : Theme.Button)) g.FillPath(fill, path);
            Theme.Draw(g, Text, Font, Selected ? Theme.OnAccent : Theme.Text, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // One stage or fighter on the Music tab: its picture with its name under it, the song it plays (our orange when
    // changed), ▶ to hear it. A click anywhere but ▶ opens its songs.
    class SlotCard : Clear
    {
        public Image Picture;
        public string Caption, Song;
        public bool Changed, Fighter;
        public readonly FlatButton Play = new FlatButton("▶");
        bool playing;

        /// <summary>Its song playing in the bar: ❚❚ (pause) on its button, else ▶.</summary>
        public bool Playing
        {
            get { return playing; }
            set { if (playing == value) return; playing = value; Play.Text = value ? "❚❚" : "▶"; Play.Invalidate(); }
        }
        public event Action Clicked;
        bool hover;
        const int PictureWidth = 150;

        public SlotCard()
        {
            Cursor = Cursors.Hand;
            Play.Size = new Size(48, 48);
            Play.Font = Theme.Bold(14f);
            Controls.Add(Play);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Play.Location = new Point(Width - Play.Width - 14, (Height - Play.Height) / 2); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Clicked != null) Clicked(); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 9))
            {
                using (var fill = new SolidBrush(hover ? Theme.TileHover : Theme.Card)) g.FillPath(fill, path);
                g.SetClip(path);
                var picture = new Rectangle(1, 1, PictureWidth, Height - 2);
                using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
                if (Picture != null)
                {
                    if (Fighter)
                    {
                        // a fighter's portrait from the top (its face), as wide as the space
                        float scale = (float)picture.Width / Picture.Width;
                        g.DrawImage(Picture, new RectangleF(picture.X, picture.Y, picture.Width, Picture.Height * scale));
                    }
                    else g.DrawImage(Picture, picture);
                }
                var caption = new Rectangle(picture.X, picture.Bottom - 18, picture.Width, 18);
                using (var shade = new SolidBrush(Color.FromArgb(215, Theme.Well))) g.FillRectangle(shade, caption);
                Theme.Draw(g, Caption, Theme.Font(7.5f), Theme.Text, new Rectangle(caption.X + 4, caption.Y, caption.Width - 8, caption.Height),
                           TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                g.ResetClip();
                using (var pen = new Pen(hover ? Theme.Accent : Theme.Line, hover ? 2f : 1f)) g.DrawPath(pen, path);
            }
            int left = PictureWidth + 16, right = Play.Left - 10;
            Theme.Draw(g, Song, Theme.Bold(13f), Changed ? Theme.Accent : Theme.Text, new Rectangle(left, 0, right - left, Height),
                       TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    // The songs for a stage or fighter, in groups first (user 2026-10-15: "group these before showing lists": SF4 songs,
    // SF4 character themes, then songs by creators): a button each, and the group picked lists its songs, each with ▶ (5
    // seconds of it, from 30 seconds in). A stage's songs have Round 1, 2 and 3 boxes (user 2026-10-16, "let me do this":
    // a song for each round): a box gives that round to its song (taken from any other), a song clicked takes all three;
    // a round picked for nothing stays as it is. A fighter's theme plays every round: no boxes. "Use this song" (one song
    // for every round) or "Use these songs", "Its own music", Cancel.
    class SongDialog : Form
    {
        /// <summary>The song picked for each round (1 to 3), null where none; a fighter's: round 1's.</summary>
        public MusicLibrary.Entry[] Picks { get; private set; }
        public bool Own { get; private set; }   // its own music back, every round

        readonly List<MusicLibrary.Entry> songs;
        readonly string[] playing;   // the song set on each round (1 to 3), or null
        readonly bool stage;
        readonly Label now = new Label { AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), AutoEllipsis = true };
        readonly FlowLayoutPanel groups = new FlowLayoutPanel { BackColor = Color.Transparent, WrapContents = true };
        readonly ScrollList list = new ScrollList { FlowDirection = FlowDirection.TopDown, WrapContents = false };
        readonly FlatButton use = new FlatButton("Use this song", true), own = new FlatButton("Its own music"), cancel = new FlatButton("Cancel");
        readonly Label status = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = Theme.Font(9f) };
        // what each round will play, above the buttons (user 2026-10-16: "show which songs play for the three rounds")
        static readonly Font Underlined = new Font(Theme.Font(9.5f), FontStyle.Underline);   // a summary line pointed at
        // a line each, a click takes you to its song and plays it (user 2026-10-16: "take you to its selection and start playing it")
        readonly Label[] summary = Enumerable.Range(0, 3).Select(i => new Label
        {
            AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Font(9.5f), AutoEllipsis = true, Cursor = Cursors.Hand,
        }).ToArray();
        readonly WavePlayer player = new WavePlayer();
        readonly Timer stopAfter = new Timer { Interval = 5200 };
        readonly List<SongRow> rows = new List<SongRow>();
        MusicLibrary.Entry[] start;   // the picks it opened with (what plays now): Use waits for a change
        string group;
        string folderCreator, folderCollection;   // Songs by creators: the folder open (null: the creators; then a creator's collections)
        const int Wide = 640, Pad = 18;
        // taller (user 2026-10-16: "expand the window vertically"), within the screen
        static int Tall { get { return Math.Min(780, Screen.PrimaryScreen.WorkingArea.Height - 60); } }

        public SongDialog(string name, MusicSlot slot, List<MusicLibrary.Entry> songs, string[] playing, float volume)
        {
            this.songs = songs;
            this.playing = playing;
            stage = !slot.Fighter;
            // what plays now, picked to begin with (user 2026-10-16: "it didnt remember my round 1,2,3 settings"): each round's
            // song; round 1 without one its own; round 2 without one plays round 1's, round 3 round 2's (Tom's Round BGM mod)
            string ownKey = slot.Fighter ? "fighter:" + slot.Code : Stages.IsCustomCode(slot.Code) ? "custom:" + slot.Code : "stage:" + slot.Code;
            Func<string, MusicLibrary.Entry> named = n => n == null ? null : songs.FirstOrDefault(e => e.Name == n);
            Picks = new MusicLibrary.Entry[3];
            Picks[0] = playing[0] != null ? named(playing[0]) : songs.FirstOrDefault(e => e.Key == ownKey);   // a song no longer listed (a song mod gone): none picked, it stays
            if (stage) { Picks[1] = named(playing[1]) ?? Picks[0]; Picks[2] = named(playing[2]) ?? Picks[1]; }
            start = (MusicLibrary.Entry[])Picks.Clone();
            player.Volume = volume;   // the Music tab's bar's
            Text = "Music for " + name;
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Wide, Tall);
            var heading = new Label { Text = "Music for " + name, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Bold(13f), ForeColor = Theme.Text, Bounds = new Rectangle(Pad, 14, Wide - 2 * Pad, 30) };
            string first = playing[0] ?? MusicLibrary.OwnName(slot, name);
            now.Text = "Plays now: " + first + (playing[1] != null ? "  ·  Round 2: " + playing[1] : "") + (playing[2] != null ? "  ·  Round 3: " + playing[2] : "");
            now.ForeColor = playing.Any(p => p != null) ? Theme.Accent : Theme.GoodText;
            now.Bounds = new Rectangle(Pad, 44, Wide - 2 * Pad, 20);
            foreach (string g in songs.Select(s => s.Group).Distinct())
            {
                string each = g;
                var button = new FlatButton(g) { Margin = new Padding(0, 0, 8, 8), Tag = g };
                button.Width = TextRenderer.MeasureText(g, button.Font).Width + 30;
                button.Click += (s, e) => { if (each == MusicLibrary.CreatorSongs) folderCreator = folderCollection = null; ShowGroup(each); };   // its folders from the top
                groups.Controls.Add(button);
            }
            groups.Bounds = new Rectangle(Pad, 70, Wide - 2 * Pad, 40);
            // as tall as its rows (user 2026-10-17: a second row of groups, Your songs among them, was hidden under the list)
            groups.Height = groups.GetPreferredSize(new Size(groups.Width, 0)).Height;
            int summaryHeight = stage ? 62 : 22;
            list.Bounds = new Rectangle(Pad, groups.Bottom + 6, Wide - 2 * Pad, Tall - groups.Bottom - 6 - 76 - summaryHeight);
            for (int i = 0; i < 3; i++)
            {
                int round = i;
                summary[i].Bounds = new Rectangle(Pad, list.Bottom + 6 + i * 20, Wide - 2 * Pad, 20);
                summary[i].Visible = stage || i == 0;
                summary[i].Click += (s, e) => GoTo(Picks[round]);
                summary[i].MouseEnter += (s, e) => { if (Picks[round] != null) summary[round].Font = Underlined; };
                summary[i].MouseLeave += (s, e) => summary[round].Font = Theme.Font(9.5f);
            }
            status.Bounds = new Rectangle(Pad, Tall - 62, Wide - 2 * Pad, 18);
            use.Location = new Point(Pad, Tall - 42);
            use.Click += (s, e) => DialogResult = DialogResult.OK;
            own.Enabled = playing.Any(p => p != null);
            own.Click += (s, e) => { Own = true; Picks = new MusicLibrary.Entry[3]; DialogResult = DialogResult.OK; };
            cancel.Location = new Point(Wide - Pad - cancel.Width, Tall - 42);
            own.Location = new Point(cancel.Left - 10 - own.Width, Tall - 42);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            stopAfter.Tick += (s, e) => { status.Text = ""; Quiet(); };
            Controls.AddRange(new Control[] { heading, now, groups, list, status, use, own, cancel });
            Controls.AddRange(summary);
            // the group of the song playing there, else the slot's own kind's
            var current = playing[0] != null ? songs.FirstOrDefault(s => s.Name == playing[0]) : null;
            if (current != null) { folderCreator = current.Creator; folderCollection = current.Collection; }
            ShowGroup(current != null ? current.Group : slot.Fighter ? MusicLibrary.FighterThemes : MusicLibrary.StageSongs);
        }

        // a song for a round (1 to 3), or for every round (0)
        void Give(MusicLibrary.Entry song, int round)
        {
            if (round == 0 || !stage) { Picks[0] = song; Picks[1] = stage ? song : null; Picks[2] = stage ? song : null; }
            else Picks[round - 1] = Picks[round - 1] == song ? null : song;   // its box again: that round unpicked
            ShowPicks();
        }

        void ShowPicks()
        {
            foreach (var r in rows)
            {
                for (int n = 0; n < 3; n++) r.Lit[n] = Picks[n] == r.Song;
                r.Invalidate();
            }
            bool any = Picks.Any(p => p != null), one = Picks[0] != null && (!stage || (Picks[1] == Picks[0] && Picks[2] == Picks[0]));
            use.Enabled = any && !Picks.SequenceEqual(start);   // a change to put in
            Func<int, string> round = n => Picks[n - 1] != null ? Picks[n - 1].Name : "stays as it is";
            if (!stage) summary[0].Text = "Every round: " + round(1);
            else for (int n = 1; n <= 3; n++) summary[n - 1].Text = "Round " + n + ":  " + round(n);
            use.Text = !any || one ? "Use this song" : "Use these songs";
            use.Width = TextRenderer.MeasureText(use.Text, use.Font).Width + 36;
        }

        void ShowGroup(string g)
        {
            group = g;
            foreach (FlatButton b in groups.Controls) { b.Fill = (string)b.Tag == g ? Theme.Accent : (Color?)null; b.Invalidate(); }
            Quiet();
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            rows.Clear();
            var inGroup = songs.Where(s => s.Group == g).ToList();
            if (g != MusicLibrary.CreatorSongs) foreach (var song in inGroup) AddSongRow(song);
            else if (folderCreator == null)   // the creators, a folder each
                foreach (var creator in inGroup.GroupBy(s => s.Creator ?? "").OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string who = creator.Key;
                    AddFolder(who, creator.Count(), false, () => { folderCreator = who; folderCollection = null; ShowGroup(g); });
                }
            else
            {
                var theirs = inGroup.Where(s => (s.Creator ?? "") == folderCreator).ToList();
                if (folderCollection == null)   // a creator's collections, a folder each, then their songs in none
                {
                    AddFolder("Creators", 0, true, () => { folderCreator = null; ShowGroup(g); });
                    foreach (var pack in theirs.Where(s => s.Collection != null).GroupBy(s => s.Collection).OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        string named = pack.Key;
                        AddFolder(named, pack.Count(), false, () => { folderCollection = named; ShowGroup(g); });
                    }
                    foreach (var song in theirs.Where(s => s.Collection == null)) AddSongRow(song);
                }
                else
                {
                    AddFolder(folderCreator, 0, true, () => { folderCollection = null; ShowGroup(g); });
                    foreach (var song in theirs.Where(s => s.Collection == folderCollection)) AddSongRow(song);
                }
            }
            list.ResumeLayout();
            list.AutoScrollPosition = Point.Empty;
            ShowPicks();
        }

        void AddSongRow(MusicLibrary.Entry song)
        {
            var row = new SongRow(song, stage) { Width = list.Width - SystemInformation.VerticalScrollBarWidth - 8, Height = 44, Margin = new Padding(0, 0, 0, 4), Now = playing[0] != null && song.Name == playing[0] };
            row.Picked += () => Give(row.Song, 0);              // the song: every round
            row.RoundPicked += n => Give(row.Song, n);          // a round's box: that round
            row.Play.Click += (s, e) => { if (heard == row) Quiet(); else Hear(row); };
            rows.Add(row);
            list.Controls.Add(row);
        }

        // a folder in Songs by creators (a creator, a collection), or the way back up (`back`)
        void AddFolder(string name, int count, bool back, Action open)
        {
            var row = new FolderRow(name, count, back) { Width = list.Width - SystemInformation.VerticalScrollBarWidth - 8, Height = back ? 34 : 44, Margin = new Padding(0, 0, 0, 4) };
            row.Click += (s, e) => open();
            list.Controls.Add(row);
        }

        // a round's song in the list: its group (and folder) shown, its row scrolled to and its 5 seconds played
        void GoTo(MusicLibrary.Entry song)
        {
            if (song == null) return;
            if (song.Group == MusicLibrary.CreatorSongs && (folderCreator != song.Creator || folderCollection != song.Collection))
            {
                folderCreator = song.Creator;
                folderCollection = song.Collection;
                ShowGroup(song.Group);
            }
            else if (group != song.Group) ShowGroup(song.Group);
            var row = rows.FirstOrDefault(r => r.Song == song);
            if (row == null) return;
            list.ScrollControlIntoView(row);
            Hear(row);
        }

        SongRow heard;   // the row whose song plays (its ▶ shows ❚❚)

        // 5 seconds of it, from 30 seconds in; its button again stops it (user 2026-10-16: "if i press it again its just play again")
        async void Hear(SongRow row)
        {
            Quiet();
            heard = row;
            row.Playing = true;
            status.Text = "Opening " + row.Song.Name + "...";
            short[] part;
            try { part = await Task.Run(() => MusicLibrary.Taste(row.Song, 5)); }
            catch (Exception ex) { status.Text = "It can't be played: " + ex.Message; Quiet(); return; }
            if (IsDisposed || heard != row) return;
            try { player.Play(part, 0, false, 0, part.Length / 2); }
            catch (Exception ex) { status.Text = ex.Message; Quiet(); return; }
            status.Text = "Playing 5 seconds of " + row.Song.Name;
            stopAfter.Start();
        }

        void Quiet()
        {
            stopAfter.Stop();
            player.Stop();
            if (heard != null && !heard.IsDisposed) heard.Playing = false;
            heard = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Quiet();
            base.OnFormClosed(e);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }

    // One song in SongDialog: ▶, its name, more about it under it; "Plays now" on round 1's now; for a stage, its Round 1
    // / 2 / 3 boxes on the right (the user's mockup), lit for the rounds it's picked for; picked for any, its outline lit.
    // A folder on Songs by creators (a creator, one of their collections) with how many songs it holds, or the way back up
    // to the one above
    class FolderRow : Clear
    {
        static readonly Font Glyph = new Font("Segoe MDL2 Assets", 12f);
        readonly string name;
        readonly int count;
        readonly bool back;
        bool hover;

        public FolderRow(string name, int count, bool back)
        {
            this.name = name;
            this.count = count;
            this.back = back;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 8))
            {
                using (var fill = new SolidBrush(hover ? Theme.TileHover : back ? Theme.Base : Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            if (back) Theme.Draw(g, "\uE72B", Glyph, Theme.Muted, new Rectangle(12, 0, 28, Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            else
            {
                // a folder: its tab, then its body
                float x = 14, y = Height / 2f - 9;
                using (var fill = new SolidBrush(Theme.Accent))
                {
                    using (var tab = Theme.Rounded(new RectangleF(x, y, 11, 6), 1.5f)) g.FillPath(fill, tab);
                    using (var body = Theme.Rounded(new RectangleF(x, y + 3, 24, 16), 2.5f)) g.FillPath(fill, body);
                }
            }
            Theme.Draw(g, name, back ? Theme.Font(9.5f) : Theme.Bold(10f), back ? Theme.Muted : Theme.Text, new Rectangle(48, 0, Width - 48 - 110, Height),
                       TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!back) Theme.Draw(g, count == 1 ? "1 song" : count + " songs", Theme.Font(9f), Theme.Muted, new Rectangle(Width - 120, 0, 106, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    class SongRow : Clear
    {
        public readonly MusicLibrary.Entry Song;
        public readonly FlatButton Play = new FlatButton("▶");
        public bool Now;
        public readonly bool[] Lit = new bool[3];
        public event Action Picked;
        public event Action<int> RoundPicked;
        readonly bool rounds;
        bool hover, playing;
        const int Box = 40, BoxGap = 4;

        public bool Playing
        {
            get { return playing; }
            set { if (playing == value) return; playing = value; Play.Text = value ? "❚❚" : "▶"; Play.Invalidate(); }
        }

        public SongRow(MusicLibrary.Entry song, bool rounds)
        {
            Song = song;
            this.rounds = rounds;
            Cursor = Cursors.Hand;
            Play.Width = 40;
            Play.Location = new Point(8, 6);
            Controls.Add(Play);
            LoadThumbnail(song.PictureZip);
        }

        System.Drawing.Image thumbnail;   // its zip's picture (MusicLibrary.Thumbnail), or none

        async void LoadThumbnail(string zip)
        {
            if (zip == null) return;
            var made = await Task.Run(() => MusicLibrary.Thumbnail(zip));
            if (IsDisposed || made == null) return;
            thumbnail = made;
            Invalidate();
        }

        /// <summary>A thumbnail in rounded corners.</summary>
        public static void DrawThumbnail(Graphics g, System.Drawing.Image picture, Rectangle at)
        {
            var mode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var path = Theme.Rounded(at, 4))
            {
                var clip = g.Clip;
                g.SetClip(path);
                g.DrawImage(picture, at);
                g.Clip = clip;
            }
            g.InterpolationMode = mode;
        }

        Rectangle BoxAt(int n) { return new Rectangle(Width - 10 - (4 - n) * (Box + BoxGap) + BoxGap, (Height - Box) / 2, Box, Box); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Play.Location = new Point(8, (Height - Play.Height) / 2); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int box = rounds ? Enumerable.Range(1, 3).FirstOrDefault(n => BoxAt(n).Contains(e.Location)) : 0;
                if (box > 0) { if (RoundPicked != null) RoundPicked(box); }
                else if (Picked != null) Picked();
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            bool picked = Lit.Any(l => l);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 8))
            {
                using (var fill = new SolidBrush(hover ? Theme.TileHover : Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(picked ? Theme.Accent : Theme.Line, picked ? 2f : 1f)) g.DrawPath(pen, path);
            }
            int boxes = rounds ? 3 * (Box + BoxGap) + 6 : 0;
            int left = Play.Right + 12, right = Width - boxes - (Now ? 100 : 12);
            if (thumbnail != null) { DrawThumbnail(g, thumbnail, new Rectangle(left - 2, (Height - 34) / 2, 60, 34)); left += 66; }
            bool two = !string.IsNullOrEmpty(Song.From);
            Theme.Draw(g, Song.Name, Theme.Bold(10f), Theme.Text, two ? new Rectangle(left, 3, right - left, 22) : new Rectangle(left, 0, right - left, Height),
                       TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (two) Theme.Draw(g, Song.From, Theme.Font(8.5f), Theme.Muted, new Rectangle(left, 22, right - left, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Now) Theme.Draw(g, "Plays now", Theme.Bold(8.5f), Theme.Accent, new Rectangle(Width - boxes - 100, 0, 88, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            if (!rounds) return;
            for (int n = 1; n <= 3; n++)
            {
                bool lit = Lit[n - 1];
                var r = BoxAt(n);
                using (var fill = new SolidBrush(lit ? Theme.Accent : Theme.Well)) g.FillRectangle(fill, r);
                using (var pen = new Pen(lit ? Theme.Accent : Theme.Text, 1.5f)) g.DrawRectangle(pen, r);
                Color ink = lit ? Theme.OnAccent : Theme.Text;
                Theme.Draw(g, "Round", Theme.Bold(6.5f), ink, new Rectangle(r.X, r.Y + 1, r.Width, 12), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Theme.Draw(g, n.ToString(), Theme.Bold(13f), ink, new Rectangle(r.X, r.Y + 11, r.Width, r.Height - 12), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
