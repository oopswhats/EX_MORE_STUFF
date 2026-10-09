// The Music page: pick where a song goes (a stage's theme, a fighter's theme, a custom stage's), open a song (WAV,
// MP3, FLAC, M4A, WMA, or a game theme's .csb), set where it loops on the waveform (or let the finder suggest it),
// hear the loop's seam, and put it in the game. Unlike costumes and stages this acts at once: "Put in the game"
// writes the file, "Give back the game's music" takes it away again.
// A stage's music has three layers the game plays together and fades between (main; Ultra, when both players have
// half their Ultra gauge; low health, when the timer is at 15 or someone's health is low): a song goes on one, and
// the main song's options say what the other two play. Rounds 2 and 3 are Tom's Round BGM mod's files
// (BGM_<stage>2.csb, BGM_<stage>3.csb; it plays them when dinput8.dll is next to SSFIV.exe).
// "Save..." keeps a song with its loop and settings as a WAV (or the game file it makes); opening that WAV again
// brings everything back.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ExMoreStuff
{
    class MusicPanel : Clear
    {
        const int ListWidth = 214, Gap = 12;
        static readonly string[] UltraModes = { "This song", "Its own", "The game's" };
        static readonly string[] LowModes = { "This song", "Bass cut", "Its own", "The game's" };
        const string BassCutNote = " (bass cut)";

        // Songs longer than this aren't searched for a loop when they open (the search grows with the square of the
        // length: about 20 s at 10 minutes); Find the loop still does it.
        const int AutoFindLimit = 8 * 60 * Song.Rate;

        string game;
        MusicSlot slot;                 // the row picked (round 1)
        Song song;
        double songLoudness; int songPeak = 1;   // the open song's, worked out once
        Dictionary<string, InstalledSong> installed = new Dictionary<string, InstalledSong>(StringComparer.OrdinalIgnoreCase);
        string listProblem;             // the songs list can't be read: shown, and nothing is put in or given back
        readonly List<SlotRow> rows = new List<SlotRow>();
        readonly Dictionary<string, double> loudness = new Dictionary<string, double>();   // the game's layers' loudness
        short[] preview;                // the song as it would go in (seam smoothed, volume set), for playback
        string previewKey;
        double volumeDb;

        readonly ScrollList list = new ScrollList { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(4, 4, 0, 4) };
        readonly TextBox customBox = DarkBox(64);
        readonly FlatButton addCustom = Small("Add");
        readonly Label title = MakeLabel(Theme.Bold(14f), Theme.Text), now = MakeLabel(Theme.Font(9f), Theme.Muted), songLabel = MakeLabel(Theme.Bold(9.5f), Theme.Text);
        readonly FlatButton openSong = new FlatButton("Open a song..."), openGame = new FlatButton("Open what the game plays here");
        readonly WaveformView wave = new WaveformView();
        readonly SeamView seam = new SeamView();
        readonly FlatButton play = Small("Play"), stop = Small("Stop"), testLoop = Small("Test the loop"),
                            whole = Small("Show all"), atStart = Small("Start"), atEnd = Small("End");
        readonly Toggle loop = new Toggle("Loop") { Checked = true, Width = 84 }, bassPreview = new Toggle("Bass cut") { Width = 104 },
                        gameLevel = new Toggle("Hear it as in game") { Checked = true, Width = 170 };
        readonly Label clock = MakeLabel(Theme.Font(9f), Theme.Muted), customLabel = MakeLabel(Theme.Font(8.5f), Theme.Muted);
        readonly Label startLabel = MakeLabel(Theme.Bold(9f), Color.FromArgb(70, 210, 120)), endLabel = MakeLabel(Theme.Bold(9f), Theme.Accent);
        readonly TextBox startBox = DarkBox(92), endBox = DarkBox(92);
        readonly FlatButton startHere = Small("At cursor"), endHere = Small("At cursor");
        readonly FlatButton find = Small("Find the loop"), fit = Small("Fit the end");
        readonly Label lengthLabel = MakeLabel(Theme.Font(9f), Theme.Muted), matchLabel = MakeLabel(Theme.Bold(9f), Theme.Text);
        readonly Toggle snap = new Toggle("Snap to zero") { Checked = true, Width = 124 },
                        loud = new Toggle("Match game volume") { Checked = true, Width = 164 },
                        smooth = new Toggle("Smooth the seam") { Checked = true, Width = 150 };
        readonly Label volumeLabel = MakeLabel(Theme.Bold(9f), Theme.Text), volumeNote = MakeLabel(Theme.Font(8.5f), Theme.Muted);
        readonly FlatButton quieter = Small("-"), louder = Small("+");
        readonly TextBox volumeBox = DarkBox(52);
        readonly Label roundLabel = MakeLabel(Theme.Bold(9f), Theme.Text), layerLabel = MakeLabel(Theme.Bold(9f), Theme.Text), roundNote = MakeLabel(Theme.Font(8.5f), Theme.Muted);
        readonly Choice round = new Choice("1", "2", "3"), layer = new Choice(MusicBank.LayerNames);
        readonly Label ultraLabel = MakeLabel(Theme.Bold(9f), Theme.Text), lowLabel = MakeLabel(Theme.Bold(9f), Theme.Text), layerNote = MakeLabel(Theme.Font(8.5f), Theme.Muted);
        readonly Choice ultraMode = new Choice(UltraModes), lowMode = new Choice(LowModes);
        readonly FlatButton install = new FlatButton("Put in the game", true), restore = new FlatButton("Give back the game's music"), save = new FlatButton("Save...");
        readonly Label status = MakeLabel(Theme.Bold(9f), Theme.Text);
        readonly WavePlayer player = new WavePlayer();
        readonly Timer timer = new Timer { Interval = 30 };
        readonly ToolTip tips = new ToolTip();

        public MusicPanel()
        {
            Controls.Add(list);
            Controls.AddRange(new Control[] { customLabel, customBox, addCustom });
            customLabel.Text = "A custom stage, by its code:";
            clock.TextAlign = ContentAlignment.MiddleRight;
            Controls.AddRange(new Control[]
            {
                title, now, openSong, openGame, songLabel, clock, wave, play, stop, testLoop, loop, bassPreview, gameLevel, whole, atStart, atEnd,
                startLabel, startBox, startHere, endLabel, endBox, endHere, lengthLabel, find, fit, matchLabel, seam,
                snap, loud, smooth, volumeLabel, quieter, volumeBox, louder, volumeNote,
                roundLabel, round, layerLabel, layer, roundNote, ultraLabel, ultraMode, lowLabel, lowMode, layerNote,
                install, restore, save, status,
            });
            startLabel.Text = "Start";
            endLabel.Text = "End";
            volumeLabel.Text = "Volume";
            roundLabel.Text = "Round";
            layerLabel.Text = "Layer";
            ultraLabel.Text = "Ultra plays";
            lowLabel.Text = "Low health plays";
            quieter.Width = louder.Width = 30;
            volumeBox.Text = "0.0";

            openSong.Click += async (s, e) => await OpenSong();
            openGame.Click += async (s, e) => await OpenGameTheme();
            play.Click += (s, e) => Play(wave.CursorFrame, loop.Checked);
            stop.Click += (s, e) => player.Stop();
            testLoop.Click += (s, e) => Play(Math.Max(wave.LoopStart, wave.LoopEnd - 4 * Song.Rate), true);
            whole.Click += (s, e) => wave.ShowAll();
            atStart.Click += (s, e) => wave.ShowAround(wave.LoopStart, 0.5);
            atEnd.Click += (s, e) => wave.ShowAround(wave.LoopEnd, 0.5);
            startHere.Click += (s, e) => { wave.LoopStart = (int)wave.CursorFrame; LoopChanged(); };
            endHere.Click += (s, e) => { wave.LoopEnd = (int)wave.CursorFrame; LoopChanged(); };
            find.Click += async (s, e) => await FindLoop();
            fit.Click += (s, e) => FitEnd();
            snap.CheckedChanged += (s, e) => wave.Snap = snap.Checked;
            smooth.CheckedChanged += (s, e) => LoopChanged();
            loud.CheckedChanged += (s, e) => LoopChanged();
            bassPreview.CheckedChanged += (s, e) => { if (player.Playing) Play(Math.Max(0, player.Position), loop.Checked); };
            gameLevel.CheckedChanged += (s, e) => player.Volume = (float)PlaybackGain();
            quieter.Click += (s, e) => SetVolume(volumeDb - 1);
            louder.Click += (s, e) => SetVolume(volumeDb + 1);
            volumeBox.Leave += (s, e) => TypedVolume();
            volumeBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { TypedVolume(); e.SuppressKeyPress = true; } };
            round.Changed += () => { player.Stop(); ModesFromRecord(); UpdateEditor(); LoopChanged(); };
            layer.Changed += () => { player.Stop(); UpdateEditor(); LoopChanged(); };
            install.Click += async (s, e) => await Install();
            restore.Click += (s, e) => Restore();
            save.Click += async (s, e) => await Save();
            addCustom.Click += (s, e) => AddCustom();
            customBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddCustom(); e.SuppressKeyPress = true; } };
            foreach (var box in new[] { startBox, endBox })
            {
                var b = box;
                b.Leave += (s, e) => TypedTime(b);
                b.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { TypedTime(b); e.SuppressKeyPress = true; } };
            }
            wave.Changed += LoopChanged;
            wave.CursorMoved += () => { if (player.Playing) Play(wave.CursorFrame, loop.Checked); ShowClock(); };
            timer.Tick += (s, e) => { wave.Playhead = player.Playing ? player.Position : -1; ShowClock(); };
            timer.Start();

            tips.SetToolTip(openSong, "A WAV, MP3, FLAC, M4A or WMA file, or a game theme (.csb). A WAV saved here keeps its loop and settings");
            tips.SetToolTip(openGame, "What this stage or fighter plays now (the round and layer picked), with its loop");
            tips.SetToolTip(testLoop, "Plays the last 4 seconds before the loop end and the jump back to the loop start, over and over");
            tips.SetToolTip(bassPreview, "Plays the song with its bass (and a little of its mids) cut, as the low-health layer's \"Bass cut\" puts it in");
            tips.SetToolTip(loud, "Makes the song sound as loud as the game's own music here (measured as ears hear it, in LUFS); a limiter keeps its peaks from clipping");
            tips.SetToolTip(gameLevel, "Plays at the level the game plays this music, from its music file's mix (about 9 dB down for a stage's main layer), " +
                "so it sounds as it will in a match; your in-game music volume setting lowers it further");
            tips.SetToolTip(whole, "Zooms out to the whole song");
            tips.SetToolTip(atStart, "Zooms in on the loop start");
            tips.SetToolTip(atEnd, "Zooms in on the loop end");
            tips.SetToolTip(find, "Looks for the longest part of the song that comes back, and loops on it");
            tips.SetToolTip(fit, "Moves the loop end a little (50 ms at most) so the music after it lines up with the loop start");
            tips.SetToolTip(snap, "A dragged loop marker goes to the nearest point (a few pixels at most) where the sound crosses zero, so the seam doesn't click. Hold Shift while dragging to place it freely");
            tips.SetToolTip(volumeBox, "Louder or quieter, in dB (6 dB is about twice as loud), on top of matching the game's volume");
            tips.SetToolTip(smooth, "Blends the last 30 ms of the loop into what plays just before the loop start, so the jump can't be heard");
            tips.SetToolTip(round, "Rounds 2 and 3 play with Tom's Round BGM mod (dinput8.dll next to SSFIV.exe); without a round 3 song, round 3 plays round 2's");
            tips.SetToolTip(layer, "Main: the stage's theme. Ultra: when both players have half their Ultra gauge. Low health: when the timer is at 15 or someone's health is low");
            tips.SetToolTip(ultraMode, "This song: no change at Ultra.  Its own: a song put on the Ultra layer.  The game's: the stage's own Ultra music");
            tips.SetToolTip(lowMode, "This song: no change at low health.  Bass cut: this song with its bass turned down, in step.  Its own: a song put on the low-health layer.  The game's: the stage's own");
            tips.SetToolTip(save, "Keeps the song with its loop and these settings as a WAV, to open again later; or saves the game file it makes (.csb)");
            tips.SetToolTip(wave, "Wheel: zoom.  Right or middle drag: scroll.  Click: play cursor.  Drag the green and orange lines: loop start and end (Shift: no snapping).");
            UpdateEditor();
        }

        static Label MakeLabel(Font font, Color colour)
        {
            return new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = colour, Font = font, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        }

        static FlatButton Small(string text)
        {
            var b = new FlatButton(text) { Font = Theme.Bold(8.5f) };
            b.Size = new Size(TextRenderer.MeasureText(text, b.Font).Width + 24, 30);
            return b;
        }

        static TextBox DarkBox(int width)
        {
            return new TextBox { Width = width, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(9.5f) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Stop(); player.Dispose(); }
            base.Dispose(disposing);
        }

        public void StopPlaying() { player.Stop(); }

        // where a song goes now: the row's slot in the round picked
        MusicSlot Target { get { return slot == null ? null : slot.InRound(slot.Fighter ? 1 : round.Selected + 1); } }
        int Layer { get { return slot == null || slot.Fighter ? 0 : layer.Selected; } }

        // -- the list of slots -----------------------------------------------------------------------------------------
        public void SetGame(string folder)
        {
            game = folder;
            player.Stop();
            loudness.Clear();
            mixGains.Clear();
            LoadInstalled();
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            rows.Clear();
            int width = ListWidth - 46;
            list.Controls.Add(new Heading("Stages", width) { Font = Theme.Bold(11f), Height = 34 });
            foreach (var code in Stages.Codes)
                AddRow(new MusicSlot { Code = code }, width);
            var custom = installed.Keys.Select(MusicSlot.FromFile).Where(s => !s.Fighter && Stages.IsCustomCode(s.Code))
                .Select(s => s.Code).Distinct().OrderBy(c => c).ToList();
            if (custom.Count > 0) list.Controls.Add(new Heading("Custom stages", width) { Font = Theme.Bold(11f), Height = 34 });
            foreach (var code in custom) AddRow(new MusicSlot { Code = code }, width);
            list.Controls.Add(new Heading("Fighters", width) { Font = Theme.Bold(11f), Height = 34 });
            foreach (int i in Fighters.DisplayOrder)
                AddRow(new MusicSlot { Code = Fighters.Codes[i], Fighter = true }, width);
            list.ResumeLayout();
            var again = slot == null ? null : rows.FirstOrDefault(r => r.Slot.File == slot.File);
            Select(again ?? rows.FirstOrDefault());
        }

        void AddRow(MusicSlot s, int width)
        {
            // only slots the game has a bank for (or, for a custom stage, its stand-in stage)
            if (game != null && MusicBank.GameFile(game, s.TemplateFile) == null) return;
            var row = new SlotRow { Slot = s, Width = width, Height = 26, Margin = new Padding(2, 1, 2, 1) };
            row.Click += (o, e) => Select(row);
            rows.Add(row);
            list.Controls.Add(row);
            Mark(row);
        }

        // the row's green dot: one of your songs plays there (any round)
        void Mark(SlotRow row)
        {
            var songs = new List<string>();
            for (int r = 1; r <= (row.Slot.Fighter ? 1 : 3); r++)
            {
                InstalledSong mine;
                if (installed.TryGetValue(row.Slot.InRound(r).File, out mine))
                    songs.Add((r > 1 ? "round " + r + ": " : "") + (mine.Layers[0] ?? mine.Song));
            }
            row.Song = songs.Count == 0 ? null : string.Join(", ", songs);
            tips.SetToolTip(row, row.Song == null ? row.Slot.Name : row.Slot.Name + ": " + row.Song);
            row.Invalidate();
        }

        void AddCustom()
        {
            string code = customBox.Text.Trim().ToUpperInvariant();
            if (!Stages.IsCustomCode(code)) { Say("A custom stage code is 3 letters or digits that no game stage uses (like C12)", true); return; }
            var row = rows.FirstOrDefault(r => r.Slot.Code == code && !r.Slot.Fighter);
            if (row == null)
            {
                int width = ListWidth - 46, at = list.Controls.IndexOf(rows.First(r => r.Slot.Fighter)) - 1;
                AddRow(new MusicSlot { Code = code }, width);
                row = rows.Last();
                list.Controls.SetChildIndex(row, Math.Max(0, at));
            }
            customBox.Text = "";
            Select(row);
            list.ScrollControlIntoView(row);
        }

        void Select(SlotRow row)
        {
            player.Stop();
            foreach (var r in rows) r.Selected = r == row;
            slot = row == null ? null : row.Slot;
            ModesFromRecord();
            UpdateEditor();
            if (song != null) LoopChanged();
        }

        // the other layers' choices as the song put here left them
        void ModesFromRecord()
        {
            InstalledSong mine;
            if (Target == null || !installed.TryGetValue(Target.File, out mine)) { ultraMode.Selected = 0; lowMode.Selected = 0; return; }
            string main = mine.Layers[0];
            ultraMode.Selected = mine.Layers[1] == null ? (main == null ? 0 : 2) : mine.Layers[1] == main ? 0 : 1;
            lowMode.Selected = mine.Layers[2] == null ? (main == null ? 0 : 3) : mine.Layers[2] == main ? 0
                             : main != null && mine.Layers[2] == main + BassCutNote ? 1 : 2;
        }

        // -- opening songs ---------------------------------------------------------------------------------------------
        async Task OpenSong()
        {
            string path;
            using (var dialog = new OpenFileDialog
            {
                Title = "Open a song",
                Filter = "Songs and game music (*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.csb)|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.csb|All files (*.*)|*.*",
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
            }
            Song loaded = null;
            LoopSuggestion found = null;
            bool bank = Path.GetExtension(path).Equals(".csb", StringComparison.OrdinalIgnoreCase);
            if (!await Run("Opening " + Path.GetFileName(path), () =>
            {
                if (bank)
                {
                    int ls;
                    loaded = MusicBank.Decode(File.ReadAllBytes(path), 0, Path.GetFileNameWithoutExtension(path), out ls);
                    loaded.LoopStart = ls;
                    loaded.LoopEnd = loaded.Frames;
                }
                else
                {
                    loaded = AudioFile.Load(path);
                    if (loaded.Frames < Song.Rate) throw new InvalidDataException(Path.GetFileName(path) + " is shorter than a second");
                    if (loaded.LoopStart < 0 && loaded.Frames <= AutoFindLimit) found = LoopFinder.Find(loaded.Pcm);
                }
            })) return;
            if (loaded.Settings != null) ApplySettings(loaded.Settings);
            SetSong(loaded, found != null ? found.Start : Math.Max(0, loaded.LoopStart), found != null ? found.End : loaded.LoopEnd > 0 ? loaded.LoopEnd : loaded.Frames);
            if (found != null) SayFound(found);
            else if (bank) Say("Opened " + loaded.Name + " with the game's own loop");
            else if (loaded.LoopStart < 0 && loaded.Frames > MusicBank.LongestSong) Say("Opened " + loaded.Name + ": songs in the game can be at most 10 minutes long", true);
            else if (loaded.LoopStart < 0) Say("Opened " + loaded.Name + ", a long song: press Find the loop to look for its loop (it takes a while)");
            else Say("Opened " + loaded.Name + " with its saved loop" + (loaded.Settings != null ? " and settings" : ""));
        }

        // the file the game plays for the target, round fallbacks as Tom's mod does them (3 -> 2 -> 1)
        string PlayedFile(out bool ours)
        {
            var t = Target;
            for (int r = t.Round; r >= 1; r--)
            {
                string p = MusicBank.PatchFile(game, t.InRound(r).File);
                if (File.Exists(p)) { ours = true; return p; }
            }
            ours = false;
            return MusicBank.GameFile(game, t.TemplateFile);
        }

        async Task OpenGameTheme()
        {
            if (slot == null) return;
            bool ours;
            string path = PlayedFile(out ours);
            if (path == null) { Say("The game has no music for " + slot.Name, true); return; }
            string name = slot.Name + (Target.Round > 1 ? " round " + Target.Round : "") + (Layer > 0 ? " " + MusicBank.LayerNames[Layer].ToLowerInvariant() : "");
            Song loaded = null;
            int ls = 0, cue = Layer;
            if (!await Run("Opening " + name, () => loaded = MusicBank.Decode(File.ReadAllBytes(path), cue, name, out ls))) return;
            SetSong(loaded, ls, loaded.Frames);
            Say("This is what the game plays for " + name + (ours ? " now (from patch_ae2_tu3)" : ""));
        }

        void SetSong(Song s, int ls, int le)
        {
            player.Stop();
            song = s;
            preview = leveled = null;
            songLoudness = MusicBank.Loudness(s.Pcm);
            songPeak = 1;
            foreach (short v in s.Pcm) { int a = Math.Abs((int)v); if (a > songPeak) songPeak = a; }
            wave.SetSong(s.Pcm, ls, le);
            LoopChanged();
            UpdateEditor();
        }

        // -- settings (saved with a WAV) -----------------------------------------------------------------------------
        string Settings()
        {
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "format", 1 }, { "volumeDb", volumeDb }, { "matchVolume", loud.Checked }, { "smoothSeam", smooth.Checked },
                { "ultra", UltraModes[ultraMode.Selected] }, { "lowHealth", LowModes[lowMode.Selected] },
            });
        }

        void ApplySettings(string json)
        {
            try
            {
                var j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object v;
                if (j.TryGetValue("volumeDb", out v)) SetVolume(Convert.ToDouble(v, CultureInfo.InvariantCulture));
                if (j.TryGetValue("matchVolume", out v)) loud.Checked = Convert.ToBoolean(v);
                if (j.TryGetValue("smoothSeam", out v)) smooth.Checked = Convert.ToBoolean(v);
                if (j.TryGetValue("ultra", out v) && Array.IndexOf(UltraModes, v) >= 0) ultraMode.Selected = Array.IndexOf(UltraModes, v);
                if (j.TryGetValue("lowHealth", out v) && Array.IndexOf(LowModes, v) >= 0) lowMode.Selected = Array.IndexOf(LowModes, v);
            }
            catch (Exception) { }     // settings from a newer or broken file: the song still opens
        }

        // -- the loop --------------------------------------------------------------------------------------------------
        async Task FindLoop()
        {
            if (song == null) return;
            LoopSuggestion found = null;
            if (!await Run("Finding the loop", () => found = LoopFinder.Find(song.Pcm))) return;
            player.Stop();
            wave.LoopEnd = song.Frames;
            wave.LoopStart = found.Start;
            wave.LoopEnd = found.End;
            LoopChanged();
            SayFound(found);
        }

        void SayFound(LoopSuggestion found)
        {
            if (found.Repeat)
                Say(string.Format("Loop found: {0} to {1}. Press Test the loop to hear the seam", WaveformView.Time(found.Start / (double)Song.Rate),
                    WaveformView.Time(found.End / (double)Song.Rate)));
            else
                Say("Nothing in this song comes back, so it loops whole. Set the loop by hand, or keep Smooth the seam on");
        }

        void FitEnd()
        {
            if (song == null) return;
            player.Stop();
            try { wave.LoopEnd = LoopFinder.FitEnd(song.Pcm, wave.LoopStart, wave.LoopEnd, Song.Rate / 20); }
            catch (Exception ex) { Say("The loop end couldn't be fitted: " + ex.Message, true); return; }
            LoopChanged();
            Say("Loop end fitted to the loop start");
        }

        void TypedTime(TextBox box)
        {
            if (song == null) return;
            double seconds;
            if (!ParseTime(box.Text, out seconds)) { LoopChanged(); return; }
            int frame = (int)Math.Round(seconds * Song.Rate);
            if (box == startBox) wave.LoopStart = frame; else wave.LoopEnd = frame;
            LoopChanged();
        }

        // a frame as m:ss.sssss: 5 decimals tell every sample apart (one is 0.0000227 s)
        static string Exact(int frame)
        {
            double s = frame / (double)Song.Rate;
            int m = (int)(s / 60);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.00000}", m, s - m * 60);
        }

        // "1:23.456", "83.456" or "83,456"
        static bool ParseTime(string text, out double seconds)
        {
            seconds = 0;
            text = text.Trim().Replace(',', '.');
            var parts = text.Split(':');
            double total = 0;
            foreach (var part in parts)
            {
                double v;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 0) return false;
                total = total * 60 + v;
            }
            seconds = total;
            return parts.Length <= 3;
        }

        void LoopChanged()
        {
            if (song == null) { UpdateEditor(); return; }
            int ls = wave.LoopStart, le = wave.LoopEnd;
            if (!startBox.Focused) startBox.Text = Exact(ls);
            if (!endBox.Focused) endBox.Text = Exact(le);
            lengthLabel.Text = "Intro " + WaveformView.Time(ls / (double)Song.Rate) + "   Loop " + WaveformView.Time((le - ls) / (double)Song.Rate);
            var pcm = Preview(false);
            double match = LoopFinder.SeamMatch(pcm, ls, le);
            matchLabel.Text = string.Format("Seam match {0:P0}", match);
            matchLabel.ForeColor = match >= 0.9 ? Theme.GoodText : match >= 0.6 ? Theme.Text : Theme.Warning;
            seam.Show(pcm, ls, le);
            ShowVolume();
        }

        // -- volume ----------------------------------------------------------------------------------------------------
        void SetVolume(double db)
        {
            volumeDb = Math.Max(-30, Math.Min(20, Math.Round(db * 2) / 2));
            if (!volumeBox.Focused) volumeBox.Text = volumeDb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
            if (song != null) LoopChanged();
        }

        void TypedVolume()
        {
            double v;
            if (double.TryParse(volumeBox.Text.Trim().Replace(',', '.').Replace("dB", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) SetVolume(v);
            volumeBox.Text = volumeDb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        }

        // "In game": the level the game plays the layer heard (the low-health one while Bass cut is on) at, from the
        // slot's music file's mix (read once per bank and layer); 1 with the switch off
        readonly Dictionary<string, double> mixGains = new Dictionary<string, double>();
        double PlaybackGain()
        {
            var t = Target;
            string path = !gameLevel.Checked || t == null || game == null ? null : MusicBank.GameFile(game, t.TemplateFile);
            if (path == null) return 1;
            int cue = !t.Fighter && bassPreview.Checked ? 2 : Layer;
            string key = path + "|" + cue;
            double g;
            if (!mixGains.TryGetValue(key, out g))
            {
                try { g = MusicBank.MixGain(File.ReadAllBytes(path), cue); }
                catch (Exception) { g = 1; }
                mixGains[key] = g;
            }
            return g;
        }

        // how loud the game's own music for the target's layer sounds, in LUFS (read once per bank; -100 unknown)
        double GameLoudness()
        {
            var t = Target;
            string path = t == null || game == null ? null : MusicBank.GameFile(game, t.TemplateFile);
            if (path == null) return -100;
            string key = path + "|" + Layer;
            double l;
            if (!loudness.TryGetValue(key, out l))
            {
                try { int x; l = MusicBank.Loudness(MusicBank.Decode(File.ReadAllBytes(path), Layer, "", out x).Pcm); }
                catch (Exception) { l = -100; }
                loudness[key] = l;
            }
            return l;
        }

        double Gain()
        {
            double gain = Math.Pow(10, volumeDb / 20);
            if (loud.Checked) gain *= MusicBank.MatchGain(songLoudness, GameLoudness());
            return gain;
        }

        // the gain, and how much the limiter holds the loudest peaks down (a lot squashes the song: turn it down)
        void ShowVolume()
        {
            if (song == null) { volumeNote.Text = ""; return; }
            double gain = Gain(), over = 20 * Math.Log10(songPeak * gain / MusicBank.Ceiling);
            volumeNote.Text = string.Format("x{0:0.00}", gain);
            volumeNote.ForeColor = over > 6 ? Theme.Warning : Theme.Muted;
            tips.SetToolTip(volumeNote, over <= 0.05 ? "Its volume, times the song's own" : string.Format(
                "Its loudest peaks are held down {0:0.0} dB so they don't clip{1}", over, over > 6 ? ": that's a lot, so turn it down a little for a cleaner sound" : ""));
        }

        // the song as it will go in: at its volume (peaks limited), seam smoothed when that's on; `bass` with the bass
        // cut (made again only when something changes; the volume step only when the gain does)
        short[] leveled;
        string leveledKey;
        short[] Preview(bool bass)
        {
            double gain = Gain();
            string key = wave.LoopStart + "|" + wave.LoopEnd + "|" + smooth.Checked + "|" + gain.ToString("R") + "|" + bass;
            if (preview != null && previewKey == key) return preview;
            if (leveled == null || leveledKey != gain.ToString("R"))
            {
                leveled = MusicBank.Level(song.Pcm, gain);
                leveledKey = gain.ToString("R");
            }
            var pcm = smooth.Checked ? LoopFinder.SmoothSeam(leveled, wave.LoopStart, wave.LoopEnd, Song.Rate * 3 / 100) : leveled;
            if (bass) pcm = MusicBank.LowHealthVersion(pcm);
            preview = pcm;
            previewKey = key;
            return pcm;
        }

        // -- playing ---------------------------------------------------------------------------------------------------
        void Play(long from, bool looping)
        {
            if (song == null) return;
            player.Volume = (float)PlaybackGain();
            try { player.Play(Preview(bassPreview.Checked), from, looping, wave.LoopStart, wave.LoopEnd); }
            catch (Exception ex) { Say(ex.Message, true); }
        }

        void ShowClock()
        {
            if (song == null) { clock.Text = ""; return; }
            long at = player.Playing ? Math.Max(0, player.Position) : wave.CursorFrame;
            clock.Text = WaveformView.Time(at / (double)Song.Rate) + " / " + WaveformView.Time(song.Seconds);
        }

        // -- the game --------------------------------------------------------------------------------------------------
        // the game file the song makes for the target (built on ours when one of our songs is already there, so the
        // other layers keep theirs), and the record of what each layer plays
        // (`game` and `have` are passed in: this runs away from the window, which may change them meanwhile)
        static byte[] BuildFor(string game, Dictionary<string, InstalledSong> have, MusicSlot target, int layerIndex, short[] pcm, int ls, int le,
                               string name, int ultra, int low, out InstalledSong about)
        {
            byte[] gameBank = File.ReadAllBytes(MusicBank.GameFile(game, target.TemplateFile));
            InstalledSong mine;
            have.TryGetValue(target.File, out mine);
            string ourFile = MusicBank.PatchFile(game, target.File);
            // built on our song there only while it's still the file we wrote
            if (mine != null && !(File.Exists(ourFile) && Installer.Sha256(ourFile) == mine.Sha256)) mine = null;
            byte[] bank = mine != null ? File.ReadAllBytes(ourFile) : gameBank;
            var layers = mine != null ? (string[])mine.Layers.Clone() : new string[3];
            int cues = MusicBank.CueCount(gameBank);
            bank = MusicBank.Build(bank, gameBank, layerIndex, pcm, ls, le);
            layers[layerIndex] = name;
            if (layerIndex == 0 && cues >= 3)
            {
                if (ultra == 0) { bank = MusicBank.Link(bank, gameBank, 1, 0, false); layers[1] = name; }
                else bank = MusicBank.Link(bank, gameBank, 1, 1, ultra == 2);
                if (ultra == 2) layers[1] = null;
                if (low == 0) { bank = MusicBank.Link(bank, gameBank, 2, 0, false); layers[2] = name; }
                else if (low == 1) { bank = MusicBank.Build(bank, gameBank, 2, MusicBank.LowHealthVersion(pcm), ls, le); layers[2] = name + BassCutNote; }
                else bank = MusicBank.Link(bank, gameBank, 2, 2, low == 3);
                if (low == 3) layers[2] = null;
            }
            about = new InstalledSong { Song = layers[0] ?? name, LoopStart = ls, LoopEnd = le, Layers = layers };
            return bank;
        }

        async Task Install()
        {
            var target = Target;
            if (song == null || target == null) return;
            if (Game.IsRunning()) { Say("Close the game first, then put the song in", true); return; }
            if (MusicBank.GameFile(game, target.TemplateFile) == null) { Say("The game has no music to build " + target.Name + " on", true); return; }
            string owner = MusicBank.PackageOwning(game, target);
            if (owner != null) { Say(target.File + " is the music of " + owner + ": save the song as .csb and put it in that package", true); return; }
            player.Stop();
            var pcm = Preview(false);
            int ls = wave.LoopStart, le = wave.LoopEnd, layerIndex = Layer, ultra = ultraMode.Selected, low = lowMode.Selected;
            string name = song.Name, g = game, roundProblem = null;
            var have = installed;
            if (!await Run("Putting " + name + " in the game", () =>
            {
                InstalledSong about;
                var bank = BuildFor(g, have, target, layerIndex, pcm, ls, le, name, ultra, low, out about);
                MusicBank.Install(g, target, bank, about);
                if (target.Round > 1) roundProblem = RoundMod.Ensure(g);   // Tom's Round BGM mod comes with round 2/3 music
            })) return;
            Reload();
            string where = target.Name + (target.Round > 1 ? ", round " + target.Round : "") + (layerIndex > 0 ? ", " + MusicBank.LayerNames[layerIndex].ToLowerInvariant() + " layer" : "");
            if (roundProblem != null) Say(name + " is in for " + where + ", but " + roundProblem, true);
            else Say(name + " now plays for " + where + ". Start the game to hear it");
        }

        async Task Save()
        {
            if (song == null) return;
            var target = Target;
            string path;
            int kind;
            using (var dialog = new SaveFileDialog
            {
                Title = "Save the song",
                Filter = "Song with its loop and settings (*.wav)|*.wav" + (target != null ? "|Game music file for " + target.Name + " (*.csb)|*.csb" : ""),
                FileName = song.Name,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
                kind = dialog.FilterIndex;
            }
            player.Stop();
            short[] raw = song.Pcm;
            int ls = wave.LoopStart, le = wave.LoopEnd, layerIndex = Layer, ultra = ultraMode.Selected, low = lowMode.Selected;
            string settings = Settings(), name = song.Name;
            if (kind == 1)
            {
                if (!await Run("Saving " + Path.GetFileName(path), () => AudioFile.WriteLoopedWav(path, raw, ls, le, settings))) return;
                Say("Saved " + Path.GetFileName(path) + " with its loop. Open it again any time to put it in");
            }
            else
            {
                var pcm = Preview(false);
                string g = game;
                var have = installed;
                if (!await Run("Saving " + Path.GetFileName(path), () =>
                {
                    InstalledSong about;
                    File.WriteAllBytes(path, BuildFor(g, have, target, layerIndex, pcm, ls, le, name, ultra, low, out about));
                })) return;
                Say("Saved " + Path.GetFileName(path) + ": in the game it goes in battle\\sound\\bgm as " + target.File);
            }
        }

        void Restore()
        {
            var target = Target;
            if (target == null) return;
            if (Game.IsRunning()) { Say("Close the game first", true); return; }
            try
            {
                string problem = MusicBank.Remove(game, target);
                RoundMod.RemoveIfUnused(game);   // out again when no round 2/3 music of ours is left
                Reload();
                Say(problem ?? target.Name + (target.Round > 1 ? " round " + target.Round : "") + " plays the game's own music again");
            }
            catch (Exception ex) { Say(ex.Message, true); }
        }

        void Reload()
        {
            LoadInstalled();
            foreach (var r in rows) Mark(r);
            ModesFromRecord();
            UpdateEditor();
        }

        // the songs put in; an unreadable list is shown (MusicBank refuses to put in or give back until it's fixed)
        void LoadInstalled()
        {
            installed = new Dictionary<string, InstalledSong>(StringComparer.OrdinalIgnoreCase);
            listProblem = null;
            if (!Game.IsGameFolder(game)) return;
            try { installed = MusicBank.Load(game); }
            catch (Exception ex) { listProblem = ex.Message; Say(listProblem, true); }
        }

        // -- the editor's state ----------------------------------------------------------------------------------------
        void UpdateEditor()
        {
            var t = Target;
            bool stage = slot != null && !slot.Fighter;
            title.Text = slot == null ? "Music" : slot.Name;
            InstalledSong mine = null;
            if (t != null) installed.TryGetValue(t.File, out mine);
            if (t == null) now.Text = "";
            else if (mine != null)
            {
                string layers = !stage ? "" : "   Ultra: " + Describe(mine.Layers[1], mine.Layers[0]) + ",  low health: " + Describe(mine.Layers[2], mine.Layers[0]);
                now.Text = "In the game: " + (mine.Layers[0] != null ? "your \"" + mine.Layers[0] + "\"" : "the game's theme") + layers + "  (" + t.File + ")";
            }
            else if (game != null && File.Exists(MusicBank.PatchFile(game, t.File))) now.Text = "In the game: a modded " + t.File + " in patch_ae2_tu3";
            else if (t.Round > 1) now.Text = "Round " + t.Round + " plays " + (t.Round == 3 && installed.ContainsKey(t.InRound(2).File) ? "round 2's" : "round 1's") + " music  (" + t.File + ")";
            else if (slot.Fighter || !Stages.IsCustomCode(slot.Code)) now.Text = "In the game: the game's own music  (" + t.File + ")";
            else now.Text = "A custom stage: a song put here is built on " + Stages.Name(slot.TemplateCode) + "'s music  (" + t.File + ")";
            tips.SetToolTip(now, now.Text);
            songLabel.Text = song == null ? "No song open" : song.Name + "   ·   " + WaveformView.Time(song.Seconds, false);

            foreach (var c in new Control[] { roundLabel, round, layerLabel, layer, roundNote, bassPreview }) c.Visible = stage;
            bool mainLayer = stage && layer.Selected == 0;
            foreach (var c in new Control[] { ultraLabel, ultraMode, lowLabel, lowMode }) c.Visible = mainLayer;
            layerNote.Visible = !mainLayer;
            layerNote.Text = slot == null ? "" : slot.Fighter ? "A fighter's theme has one layer, and plays every round"
                           : "Puts this song on the " + MusicBank.LayerNames[layer.Selected].ToLowerInvariant() + " layer only; the main song and the other layer stay as they are";
            if (stage && round.Selected > 0)
            {
                // Tom's Round BGM mod plays rounds 2 and 3; EX More Stuff puts it in with the first round 2/3 song
                var mod = game == null ? RoundMod.State.Missing : RoundMod.Check(game);
                roundNote.Text = mod == RoundMod.State.Other ? "Another mod's dinput8.dll is in the way of Tom's Round BGM mod" : "Plays with Tom's Round BGM mod";
                roundNote.ForeColor = mod == RoundMod.State.Other ? Theme.Warning : Theme.Muted;
            }
            else roundNote.Text = "";

            bool has = song != null;
            foreach (var c in new Control[] { play, stop, testLoop, bassPreview, whole, atStart, atEnd, startBox, endBox, startHere, endHere, find, fit, save })
                c.Enabled = has;
            install.Enabled = has && t != null && listProblem == null;
            install.Text = Layer > 0 ? "Put on the " + MusicBank.LayerNames[Layer] + " layer" : "Put in the game";
            restore.Enabled = mine != null && listProblem == null;
            openGame.Enabled = t != null;
            if (!has) { startBox.Text = endBox.Text = lengthLabel.Text = matchLabel.Text = volumeNote.Text = ""; seam.Show(null, 0, 0); }
            player.Volume = (float)PlaybackGain();   // the slot, layer or Bass cut may have changed
            ShowClock();
            LayOut();
        }

        static string Describe(string layerSong, string main)
        {
            if (layerSong == null) return "the game's";
            if (layerSong == main) return "same song";
            if (main != null && layerSong == main + BassCutNote) return "bass cut";
            return "\"" + layerSong + "\"";
        }

        void Say(string text, bool problem = false)
        {
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
            tips.SetToolTip(status, text);
        }

        // runs slow work away from the window, which stays drawn but can't be used meanwhile
        public event Action<bool> Working;   // slow work started or ended (the rest of the window waits)

        async Task<bool> Run(string what, Action work)
        {
            Say(what + "...");
            Enabled = false;
            Cursor = Cursors.WaitCursor;
            var working = Working;
            if (working != null) working(true);
            try { await Task.Run(work); return true; }
            catch (Exception ex) { Say(ex.Message, true); return false; }
            finally
            {
                Enabled = true;
                Cursor = Cursors.Default;
                UpdateEditor();
                if (working != null) working(false);
            }
        }

        // -- layout ----------------------------------------------------------------------------------------------------
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayOut();
        }

        void LayOut()
        {
            int w = Width, h = Height;
            if (w < 400 || h < 300) return;
            list.Bounds = new Rectangle(6, 6, ListWidth - 12, h - 80);
            customLabel.Bounds = new Rectangle(12, h - 70, ListWidth - 24, 20);
            customBox.Location = new Point(12, h - 38);
            addCustom.Location = new Point(customBox.Right + 6, h - 40);
            tips.SetToolTip(customBox, "A custom stage's code (like C12), to give it a song");

            int x = ListWidth + Gap + 14, right = w - 14, ew = right - x;
            title.Bounds = new Rectangle(x, 8, ew - 420, 28);
            now.Bounds = new Rectangle(x, 36, ew - 420, 18);
            openGame.Location = new Point(right - openGame.Width, 10);
            openSong.Location = new Point(openGame.Left - openSong.Width - 8, 10);
            clock.Bounds = new Rectangle(right - 170, 60, 170, 20);
            gameLevel.Location = new Point(clock.Left - gameLevel.Width - 10, 57);
            songLabel.Bounds = new Rectangle(x, 60, gameLevel.Left - x - 10, 20);

            const int below = 282;
            int wh = Math.Max(120, h - 86 - below);
            wave.Bounds = new Rectangle(x, 84, ew, wh);
            int y = wave.Bottom + 8;
            play.Location = new Point(x, y);
            stop.Location = new Point(play.Right + 6, y);
            testLoop.Location = new Point(stop.Right + 6, y);
            loop.Location = new Point(testLoop.Right + 12, y + 3);
            bassPreview.Location = new Point(loop.Right + 4, y + 3);
            atEnd.Location = new Point(right - atEnd.Width, y);
            atStart.Location = new Point(atEnd.Left - atStart.Width - 6, y);
            whole.Location = new Point(atStart.Left - whole.Width - 6, y);

            int seamWidth = Math.Min(300, ew - 472);
            seam.Bounds = new Rectangle(right - seamWidth, y + 40, seamWidth, 72);
            y += 40;
            startLabel.Bounds = new Rectangle(x, y, 44, 26);
            startBox.Location = new Point(startLabel.Right, y + 2);
            startHere.Location = new Point(startBox.Right + 6, y);
            endLabel.Bounds = new Rectangle(startHere.Right + 14, y, 38, 26);
            endBox.Location = new Point(endLabel.Right, y + 2);
            endHere.Location = new Point(endBox.Right + 6, y);
            y += 38;
            find.Location = new Point(x, y);
            fit.Location = new Point(find.Right + 6, y);
            matchLabel.Bounds = new Rectangle(fit.Right + 14, y, 140, 30);
            lengthLabel.Bounds = new Rectangle(x, y + 32, seam.Left - x - 10, 18);
            y += 54;
            snap.Location = new Point(x, y);
            loud.Location = new Point(snap.Right + 10, y);
            smooth.Location = new Point(loud.Right + 10, y);
            volumeLabel.Bounds = new Rectangle(smooth.Right + 10, y, 52, 24);
            quieter.Location = new Point(volumeLabel.Right, y - 3);
            volumeBox.Location = new Point(quieter.Right + 4, y);
            louder.Location = new Point(volumeBox.Right + 4, y - 3);
            volumeNote.Bounds = new Rectangle(louder.Right + 6, y, Math.Max(10, right - louder.Right - 6), 24);
            y += 34;
            roundLabel.Bounds = new Rectangle(x, y, 46, 28);
            round.Location = new Point(roundLabel.Right, y);
            layerLabel.Bounds = new Rectangle(round.Right + 18, y, 42, 28);
            layer.Location = new Point(layerLabel.Right, y);
            roundNote.Bounds = new Rectangle(layer.Right + 14, y, Math.Max(10, right - layer.Right - 14), 28);
            y += 34;
            ultraLabel.Bounds = new Rectangle(x, y, 70, 28);
            ultraMode.Location = new Point(ultraLabel.Right, y);
            lowLabel.Bounds = new Rectangle(ultraMode.Right + 14, y, 104, 28);
            lowMode.Location = new Point(lowLabel.Right, y);
            layerNote.Bounds = new Rectangle(x, y, ew, 28);
            y += 38;
            install.Location = new Point(right - install.Width, y);
            restore.Location = new Point(install.Left - restore.Width - 8, y);
            save.Location = new Point(restore.Left - save.Width - 8, y);
            status.Bounds = new Rectangle(x, y, save.Left - x - 12, 36);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, ListWidth - 1, Height - 1.5f), 10))
            using (var fill = new SolidBrush(Theme.Tile))
            using (var pen = new Pen(Theme.Line))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (var path = Theme.Rounded(new RectangleF(ListWidth + Gap + 0.5f, 0.5f, Width - ListWidth - Gap - 1.5f, Height - 1.5f), 10))
            using (var fill = new SolidBrush(Theme.Card))
            using (var pen = new Pen(Theme.Line))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }
    }

    // A stage or fighter in the Music page's list: its name, and a green dot when one of your songs plays there.
    class SlotRow : Clear
    {
        public MusicSlot Slot;
        public string Song;
        bool selected, hover;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public SlotRow() { Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            if (selected || hover)
                using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6))
                using (var fill = new SolidBrush(selected ? Theme.ButtonHover : Theme.TileHover))
                    g.FillPath(fill, path);
            if (selected) using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 0, 5, 3, Height - 10);
            string name = Slot.Fighter ? Fighters.Name(Slot.Code) : Slot.Name;
            int noteWidth = Song == null ? 0 : 14;
            Theme.Draw(g, name, Theme.Bold(9f), selected ? Theme.Text : Theme.Muted, new Rectangle(10, 0, Width - 14 - noteWidth, Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (Song != null) using (var dot = new SolidBrush(Theme.Badge)) g.FillEllipse(dot, Width - 16, Height / 2 - 4, 8, 8);
        }
    }

    // A row of options of which one is picked (segmented buttons): the picked one orange.
    class Choice : Clear
    {
        readonly string[] options;
        readonly int[] edges;
        int selected, hover = -1;
        public event Action Changed;

        public Choice(params string[] options)
        {
            this.options = options;
            Font = Theme.Bold(8.5f);
            Cursor = Cursors.Hand;
            edges = new int[options.Length + 1];
            for (int i = 0; i < options.Length; i++) edges[i + 1] = edges[i] + Math.Max(30, TextRenderer.MeasureText(options[i], Font).Width + 14);
            Size = new Size(edges[options.Length] + 1, 28);
        }

        public int Selected
        {
            get { return selected; }
            set { value = Math.Max(0, Math.Min(options.Length - 1, value)); if (selected == value) return; selected = value; Invalidate(); if (Changed != null) Changed(); }
        }

        int At(int x) { for (int i = 0; i < options.Length; i++) if (x >= edges[i] && x < edges[i + 1]) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.X); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int i = At(e.X); if (i >= 0 && Enabled) Selected = i; base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, 7))
            {
                using (var fill = new SolidBrush(Theme.Button)) g.FillPath(fill, path);
                g.SetClip(path);
                for (int i = 0; i < options.Length; i++)
                {
                    var cell = new Rectangle(edges[i], 0, edges[i + 1] - edges[i], Height);
                    if (i == selected) using (var fill = new SolidBrush(Enabled ? Theme.Accent : Theme.Off)) g.FillRectangle(fill, cell);
                    else if (i == hover) using (var fill = new SolidBrush(Theme.ButtonHover)) g.FillRectangle(fill, cell);
                    if (i > 0 && i != selected && i - 1 != selected) using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, cell.Left, 5, cell.Left, Height - 6);
                    Theme.Draw(g, options[i], Font, i == selected ? Theme.OnAccent : Theme.Text, cell, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                g.ResetClip();
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
        }
    }
}
