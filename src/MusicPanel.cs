// The Music page: pick where a song goes (a stage's theme, a fighter's theme, a custom stage's), open a song (WAV,
// MP3, FLAC, M4A, WMA, or a game theme's .csb), set where it loops on the waveform (the green line placed by hand, the
// orange one stepped to the next or previous spot whose music matches it),
// hear the loop's seam, and put it in the game. Unlike costumes and stages this acts at once: "Put in the game"
// writes the file, "Give back the game's music" takes it away again.
// A stage's music has three layers the game plays together and fades between (main; Ultra, when both players have
// half their Ultra gauge; low health, when the timer is at 15 or someone's health is low): a song goes on one, and
// the main song's options say what the other two play. Rounds 2 and 3 are Tom's Round BGM mod's files
// (BGM_<stage>2.csb, BGM_<stage>3.csb; it plays them when dinput8.dll is next to SSFIV.exe).
// Every song put in the game, or saved with Save, is kept in Music\ beside the program (MusicBank.Keep): the song file
// as it was opened, its loop and settings, and the game file it made, named TRN_3_MAIN_Jackson; opening that song
// again brings everything back. The footer's folder button opens Music\ while this page is open.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        string game;
        MusicSlot slot;                 // the row picked (round 1)
        string docked, dockedName;      // a docked stage's row: its zip and its name (songs go into the zip, DockedMusic)
        string slotLabel;               // the row's name when it has one of its own ("Mishima Estate Roof (U01)"), else null

        // what a docked file holds for `file` from the Music page; null when it can't be read (gone, open elsewhere)
        static InstalledSong DockedSong(string zip, string file)
        {
            try { return DockedMusic.Song(zip, file); } catch (Exception) { return null; }
        }

        static byte[] DockedBank(string zip, string file)
        {
            try { return DockedMusic.Bank(zip, file); } catch (Exception) { return null; }
        }
        Song song;
        string songFile;                // the song file opened (kept with the song), or null: a game file (.csb)
        double songLoudness; int songPeak = 1;   // the open song's, worked out once
        Dictionary<string, InstalledSong> installed = new Dictionary<string, InstalledSong>(StringComparer.OrdinalIgnoreCase);
        string listProblem;             // the songs list can't be read: shown, and nothing is put in or given back
        readonly List<SlotRow> rows = new List<SlotRow>();
        // How loud the game's music is, generally (test\VolumeScan.cs over every stage and fighter theme): each song
        // itself about -11.5 LUFS, played by its bank about 8.5 dB down, so heard at about -20 LUFS on a stage's main
        // and Ultra layers and on fighter themes, and 0.7 under that on low health (the medians; banks range from
        // -26 to -17). "Match game volume" brings a song itself to TypicalSong and sets its bank's level for each layer
        // to be heard at TypicalHeard, whatever slot it goes on (a slot's own music may be quieter or louder).
        internal static readonly double[] TypicalHeard = { -20.0, -20.0, -20.7 };
        internal const double TypicalSong = -11.5;
        short[] preview;                // the song as it would go in (seam smoothed, volume set), for playback
        string previewKey;
        double volumeDb;

        readonly ScrollList list = new ScrollList { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(4, 4, 0, 4) };
        readonly Label title = MakeLabel(Theme.Bold(14f), Theme.Text), now = MakeLabel(Theme.Font(9f), Theme.Muted), songLabel = MakeLabel(Theme.Bold(9.5f), Theme.Text);
        readonly FlatButton openSong = new FlatButton("Open a song..."), openGame = new FlatButton("Open what the game plays here"), openSfxt = new FlatButton("From SFxT...");
        readonly WaveformView wave = new WaveformView();
        readonly SeamView seam = new SeamView();
        readonly FlatButton play = Small("Play"), stop = Small("Stop"), testLoop = Small("Test the loop"),
                            whole = Small("Show all"), atStart = Small("Start"), atEnd = Small("End");
        readonly Toggle loop = new Toggle("Loop") { Checked = true, Width = 84 }, bassPreview = new Toggle("Bass cut") { Width = 104 },
                        gameLevel = new Toggle("Hear it as in game") { Checked = true, Width = 170 };
        readonly Label clock = MakeLabel(Theme.Font(9f), Theme.Muted);
        readonly Label startLabel = MakeLabel(Theme.Bold(9f), Color.FromArgb(70, 210, 120)), endLabel = MakeLabel(Theme.Bold(9f), Theme.Accent);
        readonly TextBox startBox = DarkBox(92), endBox = DarkBox(92);
        readonly FlatButton startHere = Small("At cursor"), endHere = Small("At cursor");
        // no loop found by itself any more (user 2026-10-16: "remove the automatic looping thing. instead the user puts the
        // green slider where they want the loop to be, then the orange slider can be ticked to find next closest match")
        readonly FlatButton prevMatch = Small("Previous match"), nextMatch = Small("Next match"), fit = Small("Fit the end");
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
        readonly FlatButton install = new FlatButton("Put in the game", true), restore = new FlatButton("Give back the game's music"), save = new FlatButton("Save");
        // user 2026-10-16: a zip to upload (the song-mod format the Music tab and Browse Mods read), and the loop as an MP3
        readonly FlatButton exportBanana = new FlatButton("Export for GameBanana"), exportLoop = new FlatButton("Export loop");
        readonly Label status = MakeLabel(Theme.Bold(9f), Theme.Text);
        readonly WavePlayer player = new WavePlayer();
        // how loud it plays here only (user 2026-10-16: "a volume slider the same one from the music tab", then "just name it
        // Preview Volume"): the Music tab's slider, 33% to begin with
        readonly Slider listen = new Slider { Value = 0.33 };
        readonly Label listenLabel = MakeLabel(Theme.Bold(9f), Theme.Text);
        readonly Timer timer = new Timer { Interval = 30 };
        readonly ToolTip tips = new ToolTip();

        public MusicPanel()
        {
            Controls.Add(list);
            clock.TextAlign = ContentAlignment.MiddleRight;
            Controls.AddRange(new Control[]
            {
                title, now, openSfxt, openSong, openGame, songLabel, clock, wave, play, stop, testLoop, loop, bassPreview, gameLevel, whole, atStart, atEnd,
                startLabel, startBox, startHere, endLabel, endBox, endHere, lengthLabel, prevMatch, nextMatch, fit, matchLabel, seam,
                snap, loud, smooth, volumeLabel, quieter, volumeBox, louder, volumeNote,
                roundLabel, round, layerLabel, layer, roundNote, ultraLabel, ultraMode, lowLabel, lowMode, layerNote,
                install, restore, save, exportBanana, exportLoop, status,
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
            openSfxt.Click += async (s, e) => await OpenSfxt();
            openGame.Click += async (s, e) => await OpenGameTheme();
            play.Click += (s, e) => Play(Math.Max(wave.CursorFrame, wave.SongStart), loop.Checked);   // never in the part left out
            stop.Click += (s, e) => player.Stop();
            testLoop.Click += (s, e) => Play(Math.Max(wave.LoopStart, wave.LoopEnd - 4 * Song.Rate), true);
            whole.Click += (s, e) => wave.ShowAll();
            atStart.Click += (s, e) => wave.ShowAround(wave.LoopStart, 0.5);
            atEnd.Click += (s, e) => wave.ShowAround(wave.LoopEnd, 0.5);
            startHere.Click += (s, e) => { wave.LoopStart = (int)wave.CursorFrame; LoopChanged(); };
            endHere.Click += (s, e) => { wave.LoopEnd = (int)wave.CursorFrame; LoopChanged(); };
            prevMatch.Click += async (s, e) => await Match(false);
            nextMatch.Click += async (s, e) => await Match(true);
            fit.Click += (s, e) => FitEnd();
            snap.CheckedChanged += (s, e) => wave.Snap = snap.Checked;
            smooth.CheckedChanged += (s, e) => LoopChanged();
            loud.CheckedChanged += (s, e) => LoopChanged();
            bassPreview.CheckedChanged += (s, e) => { if (player.Playing) Play(Math.Max(0, player.Position), loop.Checked); };
            gameLevel.CheckedChanged += (s, e) => player.Volume = Loudness();
            listen.Moved += () => player.Volume = Loudness();
            listenLabel.Text = "Preview Volume";
            listenLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.AddRange(new Control[] { listen, listenLabel });
            // the buttons show the value in the box even while it has the focus (user 2026-10-16: emptied, it stayed empty)
            quieter.Click += (s, e) => { SetVolume(volumeDb - 1); ShowVolumeBox(); };
            louder.Click += (s, e) => { SetVolume(volumeDb + 1); ShowVolumeBox(); };
            volumeBox.Leave += (s, e) => TypedVolume();
            volumeBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { TypedVolume(); e.SuppressKeyPress = true; } };
            round.Changed += () => { player.Stop(); ModesFromRecord(); UpdateEditor(); LoopChanged(); };
            layer.Changed += () => { player.Stop(); UpdateEditor(); LoopChanged(); };
            install.Click += async (s, e) => await Install();
            restore.Click += (s, e) => Restore();
            save.Click += async (s, e) => await Save();
            exportBanana.Click += async (s, e) => await ExportForGameBanana();
            exportLoop.Click += async (s, e) => await ExportLoop();
            foreach (var box in new[] { startBox, endBox })
            {
                var b = box;
                b.Leave += (s, e) => TypedTime(b);
                b.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { TypedTime(b); e.SuppressKeyPress = true; } };
            }
            wave.Changed += LoopChanged;
            wave.CursorMoved += () => { if (player.Playing) Play(wave.CursorFrame, loop.Checked); ShowClock(); };
            // the playhead and clock follow the song only while it plays (user 2026-10-22: nothing ticking when idle)
            timer.Tick += (s, e) =>
            {
                bool playing = player.Playing;
                wave.Playhead = playing ? player.Position : -1;
                ShowClock();
                if (!playing) timer.Stop();   // its last step drawn: stopped
            };

            tips.SetToolTip(openSong, "A WAV, MP3, FLAC, M4A or WMA file, or a game music file (.csb). A song from your songs folder opens with its loop and settings");
            tips.SetToolTip(openSfxt, "Music from your Street Fighter X Tekken: its stages' themes and the rest, each with its own loop (read from the game, never changed)");
            tips.SetToolTip(openGame, "What this stage or fighter plays now (the round and layer picked), with its loop");
            tips.SetToolTip(testLoop, "Plays the last 4 seconds before the loop end and the jump back to the loop start, over and over");
            tips.SetToolTip(bassPreview, "Plays the song with its bass (and a little of its mids) cut, as the low-health layer's \"Bass cut\" puts it in");
            tips.SetToolTip(loud, "Makes the song as loud in the game as the game's music generally is (measured over every stage and fighter theme, as ears hear it), " +
                                  "whatever stage or fighter it goes on. Off: it plays at the level this slot's own music is played at");
            tips.SetToolTip(gameLevel, "Plays at the level the game plays this music, from its music file's mix (about 9 dB down for a stage's main layer), " +
                "so it sounds as it will in a match; your in-game music volume setting lowers it further");
            tips.SetToolTip(whole, "Zooms out to the whole song");
            tips.SetToolTip(atStart, "Zooms in on the loop start");
            tips.SetToolTip(atEnd, "Zooms in on the loop end");
            tips.SetToolTip(nextMatch, "Moves the loop end (orange) to the next spot after it where the music matches what follows the loop start (green)");
            tips.SetToolTip(prevMatch, "Moves the loop end (orange) back to the spot before it where the music matches what follows the loop start (green)");
            tips.SetToolTip(fit, "Moves the loop end a little (50 ms at most) so the music after it lines up with the loop start");
            tips.SetToolTip(snap, "A dragged loop marker goes to the nearest point (a few pixels at most) where the sound crosses zero, so the seam doesn't click. Hold Shift while dragging to place it freely");
            tips.SetToolTip(volumeBox, "Louder or quieter, in dB (6 dB is about twice as loud), on top of matching the game's volume");
            tips.SetToolTip(smooth, "Blends the last 30 ms of the loop into what plays just before the loop start, so the jump can't be heard");
            tips.SetToolTip(round, "Rounds 2 and 3 play with Tom's Round BGM mod (dinput8.dll next to SSFIV.exe); without a round 3 song, round 3 plays round 2's");
            tips.SetToolTip(layer, "Main: the stage's theme. Ultra: when both players have half their Ultra gauge. Low health: when the timer is at 15 or someone's health is low");
            tips.SetToolTip(ultraMode, "This song: no change at Ultra.  Its own: a song put on the Ultra layer.  The game's: the stage's own Ultra music");
            tips.SetToolTip(lowMode, "This song: no change at low health.  Bass cut: this song with its bass turned down, in step.  Its own: a song put on the low-health layer.  The game's: the stage's own");
            tips.SetToolTip(exportBanana, "Makes a zip to upload to GameBanana as a song mod: the game file this song makes for where it goes, as BGM_TRN.csb. " +
                                          "Players get it on Browse Mods and pick it for any stage or fighter on the Music tab");
            tips.SetToolTip(exportLoop, "Saves just the loop as an MP3 (from the green line to the orange one), for your own listening");
            tips.SetToolTip(save, "Keeps the song in your songs folder without putting it in the game: the song file, its loop and settings, and the game file it makes, named like TRN_3_MAIN_Jackson");
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
            mixGains.Clear();
            LoadInstalled();
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            rows.Clear();
            int width = ListWidth - 46;
            // docked stages first (user 2026-10-14): their songs go into the docked file, not the game (DockedMusic)
            var dockedStages = Game.IsGameFolder(game) ? Docked.Load().Where(r => r.Item.IsStage).ToList() : new List<Installed>();
            if (dockedStages.Count > 0) list.Controls.Add(new Heading("Docked", width) { Font = Theme.Bold(11f), Height = 34 });
            foreach (var d in dockedStages)
                AddRow(new MusicSlot { Code = d.Item.Code }, width, d.Package,
                       Regex.Replace(d.Item.Name, @"\s*-\s*" + Regex.Escape(d.Item.Code) + "$", "", RegexOptions.IgnoreCase) + " (" + d.Item.Code + ")");
            list.Controls.Add(new Heading("Stages", width) { Font = Theme.Bold(11f), Height = 34 });
            foreach (var code in Stages.Codes)
                AddRow(new MusicSlot { Code = code }, width);
            // the custom stages installed (and any with a song of ours), after a yellow line
            var custom = (Game.IsGameFolder(game) ? Installer.Load(game).Where(r => r.Item.IsStage).Select(r => r.Item.Code) : Enumerable.Empty<string>())
                .Concat(installed.Keys.Select(MusicSlot.FromFile).Where(s => !s.Fighter && Stages.IsCustomCode(s.Code)).Select(s => s.Code))
                .Distinct().OrderBy(c => c).ToList();
            if (custom.Count > 0) list.Controls.Add(new Rule { Width = width, Height = 14, Margin = new Padding(2, 2, 2, 2) });
            // by name (user 2026-10-14: "Custom stage U01" didn't say which stage): "Mishima Estate Roof (U01)", its name in the
            // menus or the mod's; a code with a song of ours and no stage stays "Custom stage U05"
            var names = Game.IsGameFolder(game)
                ? Installer.Load(game).Concat(Installer.Removed(game)).Where(r => r.Item.IsStage).GroupBy(r => r.Item.Code)
                           .ToDictionary(g => g.Key, g => g.First().Shown ?? g.First().Item.Name)
                : new Dictionary<string, string>();
            foreach (var code in custom)
            {
                string name;
                AddRow(new MusicSlot { Code = code }, width, null, names.TryGetValue(code, out name) && !string.IsNullOrWhiteSpace(name) ? name + " (" + code + ")" : null);
            }
            list.Controls.Add(new Heading("Fighters", width) { Font = Theme.Bold(11f), Height = 34 });
            foreach (int i in Fighters.DisplayOrder)
                AddRow(new MusicSlot { Code = Fighters.Codes[i], Fighter = true }, width);
            list.ResumeLayout();
            var again = slot == null ? null : rows.FirstOrDefault(r => r.Slot.File == slot.File && r.Docked == docked);
            Select(again ?? rows.FirstOrDefault());
        }

        void AddRow(MusicSlot s, int width, string dockedZip = null, string label = null)
        {
            // only slots the game has a bank for (or, for a custom stage, its stand-in stage)
            if (game != null && MusicBank.GameFile(game, s.TemplateFile) == null) return;
            var row = new SlotRow { Slot = s, Docked = dockedZip, Label = label, Width = width, Height = 26, Margin = new Padding(2, 1, 2, 1) };
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
                if (row.Docked != null ? (mine = DockedSong(row.Docked, row.Slot.InRound(r).File)) != null : installed.TryGetValue(row.Slot.InRound(r).File, out mine))
                    songs.Add((r > 1 ? "round " + r + ": " : "") + (mine.Layers[0] ?? mine.Song));
            }
            row.Song = songs.Count == 0 ? null : string.Join(", ", songs);
            string named = row.Label ?? row.Slot.Name;
            tips.SetToolTip(row, row.Song == null ? named : named + ": " + row.Song);
            row.Invalidate();
        }

        void Select(SlotRow row)
        {
            player.Stop();
            foreach (var r in rows) r.Selected = r == row;
            slot = row == null ? null : row.Slot;
            docked = row == null ? null : row.Docked;
            slotLabel = row == null ? null : row.Label;
            dockedName = docked == null ? null : Regex.Replace(row.Label, @"\s*\(" + row.Slot.Code + @"\)$", "");
            ModesFromRecord();
            UpdateEditor();
            if (song != null) LoopChanged();
        }

        // the other layers' choices as the song put here left them
        void ModesFromRecord()
        {
            InstalledSong mine = null;
            if (Target == null || (docked != null ? (mine = DockedSong(docked, Target.File)) == null : !installed.TryGetValue(Target.File, out mine))) { ultraMode.Selected = 0; lowMode.Selected = 0; return; }
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
                Filter = "Songs and game music (*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.csb;*.zip)|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.csb;*.zip|All files (*.*)|*.*",
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
            }
            // a zip (user 2026-10-16: "the exact type of file we export cant be imported", Export for GameBanana's zip of
            // BGM_*.csb): the song in it, picked from a menu when it holds more than one
            string zipped = null, zipFile = null;
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                List<string> inside;
                try
                {
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
                        inside = zip.Entries.Where(e => Regex.IsMatch(e.Name, @"\.(wav|mp3|flac|m4a|aac|wma|csb)$", RegexOptions.IgnoreCase)).Select(e => e.FullName).ToList();
                }
                catch (Exception ex) { Say(Path.GetFileName(path) + " couldn't be opened: " + ex.Message, true); return; }
                if (inside.Count == 0) { Say("There's no song in " + Path.GetFileName(path), true); return; }
                zipFile = path;
                zipped = inside.Count == 1 ? inside[0] : await PickFromZip(inside);
                if (zipped == null) return;
                path = zipped;
            }
            Song loaded = null;
            double? stored = null;   // a bank of ours: the Volume it was exported with
            int fileUltra = -1, fileLow = -1;   // ... and what its Ultra and Low health play
            bool bank = Path.GetExtension(path).Equals(".csb", StringComparison.OrdinalIgnoreCase);
            if (!await Run("Opening " + Path.GetFileName(path), () =>
            {
                if (zipFile != null)
                {
                    byte[] data;
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(zipFile))
                    using (Stream s = zip.GetEntry(zipped).Open())
                    using (var copy = new MemoryStream()) { s.CopyTo(copy); data = copy.ToArray(); }
                    if (bank)
                    {
                        int zls;
                        // named for the zip (Export for GameBanana names it for the song; the BGM_TRN inside is only the slot it
                        // was made for, user 2026-10-16: "it shows the wrong name. it shows the stage name")
                        string named = Regex.IsMatch(Path.GetFileName(zipped), @"^BGM_", RegexOptions.IgnoreCase) ? Path.GetFileNameWithoutExtension(zipFile).Trim() : Path.GetFileNameWithoutExtension(zipped);
                        loaded = MusicBank.Decode(data, 0, named, out zls);
                        loaded.LoopStart = zls;
                        loaded.LoopEnd = loaded.Frames;
                        loaded.GameLoop = true;
                        stored = StoredVolume(data, Path.GetFileName(zipped), loaded.Pcm);
                        LayersOf(data, Path.GetFileName(zipped), loaded.Pcm, out fileUltra, out fileLow);
                        return;
                    }
                    // an audio file: read from a copy in EX More Stuff's own folder (Windows' decoders want a file), gone after
                    string temp = Path.Combine(AppFolders.Root, "opening", Path.GetFileName(zipped));
                    Directory.CreateDirectory(Path.GetDirectoryName(temp));
                    File.WriteAllBytes(temp, data);
                    try { loaded = AudioFile.Load(temp); }
                    finally { try { Directory.Delete(Path.GetDirectoryName(temp), true); } catch (Exception) { } }
                    if (loaded.Frames < Song.Rate) throw new InvalidDataException(Path.GetFileName(zipped) + " is shorter than a second");
                    return;
                }
                if (bank)
                {
                    int ls;
                    byte[] data = File.ReadAllBytes(path);
                    loaded = MusicBank.Decode(data, 0, Path.GetFileNameWithoutExtension(path), out ls);
                    loaded.LoopStart = ls;
                    loaded.LoopEnd = loaded.Frames;
                    loaded.GameLoop = true;
                    stored = StoredVolume(data, Path.GetFileName(path), loaded.Pcm);
                    LayersOf(data, Path.GetFileName(path), loaded.Pcm, out fileUltra, out fileLow);
                }
                else
                {
                    loaded = AudioFile.Load(path);
                    if (loaded.Frames < Song.Rate) throw new InvalidDataException(Path.GetFileName(path) + " is shorter than a second");
                    // a song from the songs folder: its loop and settings kept beside it
                    int kls, kle; string kept;
                    if (loaded.LoopStart < 0 && MusicBank.ReadKept(path, out kls, out kle, out kept) && kle <= loaded.Frames)
                    { loaded.LoopStart = kls; loaded.LoopEnd = kle; loaded.Settings = kept; }
                }
            })) return;
            // a new song starts at volume 0 (user 2026-10-16: "it should always reset to 0"); one saved with its settings gets its own
            // back, and a bank of ours the Volume it was exported with (matched, as it was made)
            // Ultra and Low health start on This song (user 2026-10-17: "nothing there should be ticked by default, unless this song can
            // be default"; the slot's song's own choices used to stay on a new one); a bank of ours or saved settings set them below
            ultraMode.Selected = 0;
            lowMode.Selected = 0;
            SetVolume(stored ?? 0);
            if (stored != null) loud.Checked = true;
            if (fileUltra >= 0) ultraMode.Selected = fileUltra;
            if (fileLow >= 0) lowMode.Selected = fileLow;
            ShowVolumeBox();
            if (loaded.Settings != null) ApplySettings(loaded.Settings);
            songFile = bank || zipFile != null ? null : path;
            // its own loop (a game theme's, SFxT's, a song saved with one), else the whole song until the green line is placed
            SetSong(loaded, Math.Max(0, loaded.LoopStart), loaded.LoopEnd > 0 ? loaded.LoopEnd : loaded.Frames);
            if (bank) Say("Opened " + loaded.Name + " with the game's own loop" + (stored != null ? " and the volume it was made with (" + stored.Value.ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture) + ")" : ""));
            else if (loaded.LoopStart < 0 && loaded.Frames > MusicBank.LongestSong) Say("Opened " + loaded.Name + ": songs in the game can be at most 10 minutes long", true);
            else if (loaded.LoopStart < 0) Say("Opened " + loaded.Name + ": put the green line where the loop starts, then Next match for its end");
            else Say("Opened " + loaded.Name + " with its saved loop" + (loaded.Settings != null ? " and settings" : ""));
        }

        // The Volume a bank of ours was exported with, read back (user 2026-10-16: "can you make it so on import it knows my DB
        // change"): matched, its bank plays the song at TypicalHeard + Volume, so it's the bank's level against the song's own
        // loudness (one file alone, nothing compared with others). Null when it plays at the game's own level for its slot
        // (the game's file, or one made without matching) or the result isn't one EX More Stuff would make. A step near
        // a half is taken as that half (the song's loudness after packing is off by about 0.1).
        double? StoredVolume(byte[] bankBytes, string fileName, short[] pcm)
        {
            try
            {
                double level = MusicBank.MixGain(bankBytes, 0);
                string own = game != null ? MusicBank.GameFile(game, fileName) : null;
                if (own != null && Math.Abs(MusicBank.MixGain(File.ReadAllBytes(own), 0) - level) < 0.002) return null;
                double v = 20 * Math.Log10(level) - TypicalHeard[0] + MusicBank.Loudness(pcm);
                if (double.IsNaN(v) || Math.Abs(v) > 24) return null;
                double half = Math.Round(v * 2) / 2;
                v = Math.Abs(v - half) <= 0.15 ? half : Math.Round(v, 1);
                return v == 0 ? 0 : v;
            }
            catch (Exception) { return null; }
        }

        // What a bank's Ultra (cue 1) and Low health (cue 2) play, read from the file (user 2026-10-16: the switches still
        // showed Bass cut on songs made without it): the song's own sound -> This song; the low health one as long as it
        // with far less low end -> Bass cut; the slot's sound in the game's own file -> The game's; else Its own. -1: not known.
        void LayersOf(byte[] data, string fileName, short[] main, out int ultra, out int low)
        {
            ultra = low = -1;
            byte[] own = null;
            try { string g = game != null ? MusicBank.GameFile(game, fileName) : null; if (g != null) own = File.ReadAllBytes(g); }
            catch (Exception) { }
            Func<byte[], int, short[]> sound = (b, cue) =>
            {
                try { int x; return MusicBank.Decode(b, cue, "", out x).Pcm; }
                catch (Exception) { return null; }
            };
            Func<short[], short[], bool> same = (a, b) => a != null && b != null && a.Length == b.Length && a.SequenceEqual(b);
            for (int cue = 1; cue <= 2; cue++)
            {
                short[] pcm = sound(data, cue);
                if (pcm == null) continue;
                int pick;
                if (same(pcm, main)) pick = 0;
                else if (own != null && same(pcm, sound(own, cue))) pick = cue == 1 ? 2 : 3;
                else if (cue == 2 && Math.Abs(pcm.Length - main.Length) < Song.Rate && LowEnd(pcm) < LowEnd(main) / 3) pick = 1;
                else pick = cue == 1 ? 1 : 2;
                if (cue == 1) ultra = pick; else low = pick;
            }
        }

        // the share of a sound's energy under about 150 Hz
        static double LowEnd(short[] pcm)
        {
            double a = Math.Exp(-2 * Math.PI * 150 / Song.Rate), y = 0, lowSum = 0, all = 0;
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                double x = (pcm[i] + pcm[i + 1]) / 2.0;
                y = (1 - a) * x + a * y;
                lowSum += y * y;
                all += x * x;
            }
            return all > 0 ? lowSum / all : 0;
        }

        // the song to open from a zip holding several (its files by their paths inside), or null when none was picked
        Task<string> PickFromZip(List<string> inside)
        {
            var picked = new TaskCompletionSource<string>();
            ContextMenuStrip menu = DarkMenu.Create();
            foreach (string name in inside.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                string n = name;
                menu.Items.Add(new ToolStripMenuItem(n, null, (s, e) => picked.TrySetResult(n)));
            }
            menu.Closed += (s, e) => BeginInvoke((Action)(() => { picked.TrySetResult(null); menu.Dispose(); }));
            menu.Show(Cursor.Position);
            return picked.Task;
        }

        // Street Fighter X Tekken's music (the player's own SFxT, read only): a menu of its banks, the stages first, each
        // with its songs (cues); the one picked opens like any song, with its own loop
        async Task OpenSfxt()
        {
            string folder = SfxtMusic.Find();
            if (folder == null)
                using (var dialog = new FolderBrowserDialog { Description = "Choose your Street Fighter X Tekken folder (the one with \"resource\" and \"stream\" in it)." })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (!SfxtMusic.IsInstall(dialog.SelectedPath)) { Say("That isn't Street Fighter X Tekken's folder (it has no resource\\CMN\\battle\\sound\\bgm)", true); return; }
                    folder = dialog.SelectedPath;
                }
            ExMoreStuff.Settings.SfxtFolder = folder;
            List<SfxtMusic.Bank> banks = null;
            if (!await Run("Reading Street Fighter X Tekken's music", () => banks = SfxtMusic.Banks(folder))) return;
            if (banks.Count == 0) { Say("No music found in " + folder, true); return; }
            ContextMenuStrip menu = DarkMenu.Create();
            Action<ToolStripItemCollection, SfxtMusic.Bank, string> add = (items, bank, text) =>
            {
                if (bank.Cues.Count == 1)
                {
                    items.Add(new ToolStripMenuItem(text, null, async (s, e) => await OpenSfxtSong(bank, 0)));
                    return;
                }
                var item = new ToolStripMenuItem(text);
                DarkMenu.Style(item.DropDown);
                for (int c = 0; c < bank.Cues.Count; c++)
                {
                    int cue = c;
                    item.DropDownItems.Add(new ToolStripMenuItem(SfxtMusic.CueName(bank.Cues[c]), null, async (s, e) => await OpenSfxtSong(bank, cue)));
                }
                items.Add(item);
            };
            menu.Items.Add(DarkMenu.Header("Stages"));
            foreach (var bank in banks.Where(b => b.Stage).OrderBy(b => b.Name == b.Code ? "~" + b.Code : b.Name, StringComparer.OrdinalIgnoreCase))
                add(menu.Items, bank, bank.Name == bank.Code ? "Stage " + bank.Code : bank.Name);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(DarkMenu.Header("Other music"));
            foreach (var bank in banks.Where(b => !b.Stage))
            {
                string cue = bank.Cues.Count == 1 ? bank.Cues[0] : null;
                add(menu.Items, bank, bank.Code + (cue != null && cue != bank.Code && cue != "BGM_" + bank.Code ? "  \u00b7  " + cue : ""));
            }
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(openSfxt, new Point(0, openSfxt.Height));
        }

        async Task OpenSfxtSong(SfxtMusic.Bank bank, int cue)
        {
            Song loaded = null;
            string name = bank.Name + " " + SfxtMusic.CueName(bank.Cues[cue]);
            if (!await Run("Opening " + name + " from Street Fighter X Tekken", () => loaded = SfxtMusic.Load(bank, cue))) return;
            songFile = null;
            bool looped = loaded.LoopStart >= 0;
            SetVolume(0); ultraMode.Selected = 0; lowMode.Selected = 0; ShowVolumeBox();   // a new song: volume 0, Ultra and Low health This song
            SetSong(loaded, Math.Max(0, loaded.LoopStart), looped ? loaded.LoopEnd : loaded.Frames);
            Say("Opened " + name + " from Street Fighter X Tekken" + (looped ? " with its own loop" : ""));
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
            songFile = null;
            loaded.LoopStart = ls;
            loaded.LoopEnd = loaded.Frames;
            loaded.GameLoop = true;
            SetVolume(0); ultraMode.Selected = 0; lowMode.Selected = 0; ShowVolumeBox();   // a new song: volume 0, Ultra and Low health This song
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
            wave.SongStart = pendingSongStart;   // a saved song's own (ApplySettings), else its beginning
            pendingSongStart = 0;
            LoopChanged();
            UpdateEditor();
        }

        // -- settings (saved with a WAV) -----------------------------------------------------------------------------
        int pendingSongStart;   // a saved song's start, put on the waveform once it has the song

        string Settings()
        {
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "format", 1 }, { "volumeDb", volumeDb }, { "matchVolume", loud.Checked }, { "smoothSeam", smooth.Checked },
                { "ultra", UltraModes[ultraMode.Selected] }, { "lowHealth", LowModes[lowMode.Selected] }, { "songStart", wave.SongStart },
            });
        }

        // The song as it goes in: from its start (the part before it left out, user 2026-10-21: "if i have a song with a
        // particularly boring intro i cant skip it"), its loop moved with it, a few milliseconds faded in so it doesn't click
        short[] FromStart(short[] pcm, ref int ls, ref int le)
        {
            int start = Math.Min(wave.SongStart, ls);
            if (start <= 0) return pcm;
            var cut = new short[pcm.Length - start * 2];
            Array.Copy(pcm, start * 2, cut, 0, cut.Length);
            int fade = Math.Min(Song.Rate * 5 / 1000, cut.Length / 2);
            for (int i = 0; i < fade; i++) for (int c = 0; c < 2; c++) cut[i * 2 + c] = (short)(cut[i * 2 + c] * i / fade);
            ls -= start;
            le -= start;
            return cut;
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
                if (j.TryGetValue("songStart", out v)) pendingSongStart = Math.Max(0, Convert.ToInt32(v, CultureInfo.InvariantCulture));
            }
            catch (Exception) { }     // settings from a newer or broken file: the song still opens
        }

        // -- the loop --------------------------------------------------------------------------------------------------
        // the loop end stepped to the next (or previous) spot whose music matches what follows the loop start
        async Task Match(bool forward)
        {
            if (song == null) return;
            int start = wave.LoopStart, from = wave.LoopEnd, end = -1;
            if (!await Run("Looking for a match", () => end = LoopFinder.MatchEnd(song.Pcm, start, from, forward))) return;
            if (end < 0)
            {
                Say(forward ? "No match after the orange line: move the green line, or the orange one back" : "No match before the orange line", true);
                return;
            }
            player.Stop();
            wave.LoopEnd = end;
            LoopChanged();
            Say(string.Format("Loop end at {0}, matching the loop start. Press Test the loop to hear the seam", WaveformView.Time(end / (double)Song.Rate)));
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
            lengthLabel.Text = ls <= wave.SongStart ? "No intro: it loops from the start   Loop " + WaveformView.Time((le - ls) / (double)Song.Rate)
                             : "Intro " + WaveformView.Time((ls - wave.SongStart) / (double)Song.Rate) + "   Loop " + WaveformView.Time((le - ls) / (double)Song.Rate);
            var pcm = Preview(false);
            if (song.GameLoop && ls == song.LoopStart && le == song.LoopEnd)
            {
                // seamless as the game has it; ending at the song's end, there's nothing after it for the measure to compare
                matchLabel.Text = "The game's own loop";
                matchLabel.ForeColor = Theme.GoodText;
            }
            else
            {
                double match = LoopFinder.SeamMatch(pcm, ls, le);
                matchLabel.Text = string.Format("Seam match {0:P0}", match);
                matchLabel.ForeColor = match >= 0.9 ? Theme.GoodText : match >= 0.6 ? Theme.Text : Theme.Warning;
            }
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

        void ShowVolumeBox() { volumeBox.Text = volumeDb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture); }

        void TypedVolume()
        {
            double v;
            if (double.TryParse(volumeBox.Text.Trim().Replace(',', '.').Replace("dB", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) SetVolume(v);
            volumeBox.Text = volumeDb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        }

        // "In game": the level the game plays the layer heard (the low-health one while Bass cut is on) at, from the
        // slot's music file's mix (read once per bank and layer); 1 with the switch off
        readonly Dictionary<string, double> mixGains = new Dictionary<string, double>();
        // what it plays at here: as in the game (or not), times the listening slider
        float Loudness() { return (float)(PlaybackGain() * listen.Value); }

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
            var levels = Levels();
            if (levels == null || song == null) return g;
            // matched: the level it will go in at (the bass cut is 2 LU under the song), no louder than the bank can play
            double raw = songLoudness + 20 * Math.Log10(Gain()) - (cue == 2 && bassPreview.Checked ? 2 : 0);
            string maxKey = key + "|max";
            double max;
            if (!mixGains.TryGetValue(maxKey, out max))
            {
                try { max = MusicBank.MaxLevel(File.ReadAllBytes(path), cue); }
                catch (Exception) { max = 1; }
                mixGains[maxKey] = max;
            }
            return Math.Min(levels(cue, raw), max);
        }

        // the song's own gain: matched, to the game's songs' own loudness (the volume goes on its bank's level instead);
        // else the volume itself
        double Gain()
        {
            return loud.Checked ? MusicBank.MatchGain(songLoudness, TypicalSong) : Math.Pow(10, volumeDb / 20);
        }

        // matched: the level its bank should play each layer at, for that layer's sound of loudness `raw` (LUFS) to be
        // heard as the game's music generally is, the volume on top; null when not matched (the slot's own levels)
        Func<int, double, double> Levels() { return loud.Checked ? LevelsFor(volumeDb) : null; }

        internal static Func<int, double, double> LevelsFor(double volume)
        {
            return (layer, raw) => raw <= -99 ? 1 : Math.Pow(10, (TypicalHeard[Math.Min(layer, 2)] + volume - raw) / 20);
        }

        // the gain, and how much the limiter holds the loudest peaks down (a lot squashes the song: turn it down)
        void ShowVolume()
        {
            if (song == null) { volumeNote.Text = ""; return; }
            double gain = Gain(), over = 20 * Math.Log10(songPeak * gain / MusicBank.Ceiling);
            // the total heard (user 2026-10-16: "the number on the right should update so that it shows the current total"):
            // matched, the Volume goes on the bank's level for the layer rather than on the song itself, so it's shown on top
            double total = loud.Checked ? gain * Math.Pow(10, volumeDb / 20) : gain;
            volumeNote.Text = string.Format("x{0:0.00}", total);
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
            player.Volume = Loudness();
            try { player.Play(Preview(bassPreview.Checked), from, looping, wave.LoopStart, wave.LoopEnd); timer.Start(); }
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
        // `levels` (from Levels): each layer playing this song is set to be heard at the game's general loudness; null:
        // those layers play at the game's own levels for the slot. Layers playing the game's sound always get its level.
        internal static byte[] BuildFor(string game, Dictionary<string, InstalledSong> have, MusicSlot target, int layerIndex, short[] pcm, int ls, int le,
                               string name, int ultra, int low, Func<int, double, double> levels, out InstalledSong about,
                               byte[] start = null, InstalledSong startSong = null)
        {
            byte[] gameBank = File.ReadAllBytes(MusicBank.GameFile(game, target.TemplateFile));
            byte[] bank;
            string[] layers;
            if (start != null)   // a docked file's music there (DockedMusic): built on it, its layers' songs as noted
            {
                // music made on another stage's bank than the stand-in's: that stage's is the one naming each layer's
                // sound; none of the game's: built on the stand-in's afresh
                if (!MusicBank.HasSounds(start, gameBank))
                {
                    string on = MusicBank.BuiltOn(game, start);
                    if (on != null) gameBank = File.ReadAllBytes(on);
                    else { start = gameBank; startSong = null; }
                }
                bank = start;
                layers = startSong != null ? (string[])startSong.Layers.Clone() : new string[3];
            }
            else
            {
                InstalledSong mine;
                have.TryGetValue(target.File, out mine);
                string ourFile = MusicBank.PatchFile(game, target.File);
                // built on our song there only while it's still the file we wrote
                if (mine != null && !(File.Exists(ourFile) && Installer.Sha256(ourFile) == mine.Sha256)) mine = null;
                bank = mine != null ? File.ReadAllBytes(ourFile) : gameBank;
                layers = mine != null ? (string[])mine.Layers.Clone() : new string[3];
            }
            int cues = MusicBank.CueCount(gameBank);
            bank = MusicBank.Build(bank, gameBank, layerIndex, pcm, ls, le);
            layers[layerIndex] = name;
            double raw = levels != null ? MusicBank.Loudness(pcm) : 0;
            Func<byte[], int, double, byte[]> level = (b, layer, loudness) =>
                levels != null ? MusicBank.SetLevel(b, layer, Math.Min(levels(layer, loudness), MusicBank.MaxLevel(b, layer))) : MusicBank.GameLevel(b, gameBank, layer);
            bank = level(bank, layerIndex, raw);
            if (layerIndex == 0 && cues >= 3)
            {
                if (ultra == 0) { bank = MusicBank.Link(bank, gameBank, 1, 0, false); layers[1] = name; bank = level(bank, 1, raw); }
                else bank = MusicBank.Link(bank, gameBank, 1, 1, ultra == 2);
                if (ultra == 2) { layers[1] = null; bank = MusicBank.GameLevel(bank, gameBank, 1); }
                if (low == 0) { bank = MusicBank.Link(bank, gameBank, 2, 0, false); layers[2] = name; bank = level(bank, 2, raw); }
                else if (low == 1)
                {
                    short[] cut = MusicBank.LowHealthVersion(pcm);
                    bank = MusicBank.Build(bank, gameBank, 2, cut, ls, le);
                    layers[2] = name + BassCutNote;
                    bank = level(bank, 2, levels != null ? MusicBank.Loudness(cut) : 0);
                }
                else bank = MusicBank.Link(bank, gameBank, 2, 2, low == 3);
                if (low == 3) { layers[2] = null; bank = MusicBank.GameLevel(bank, gameBank, 2); }
            }
            about = new InstalledSong { Song = layers[0] ?? name, LoopStart = ls, LoopEnd = le, Layers = layers };
            return bank;
        }

        async Task Install()
        {
            var target = Target;
            if (song == null || target == null) return;
            if (docked != null) { await PutInDocked(target); return; }
            if (Game.IsRunning()) { Say("Close the game first, then put the song in", true); return; }
            if (MusicBank.GameFile(game, target.TemplateFile) == null) { Say("The game has no music to build " + target.Name + " on", true); return; }
            string owner = MusicBank.PackageOwning(game, target);
            if (owner != null && MessageBox.Show(FindForm(), target.File + " is the music of " + owner + ", from its package.\n\nPut this song in anyway? " +
                    "If the stage is updated, switched off or deleted, this song comes off again (it stays in your songs folder).",
                    "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            player.Stop();
            var pcm = Preview(false);
            int ls = wave.LoopStart, le = wave.LoopEnd, layerIndex = Layer, ultra = ultraMode.Selected, low = lowMode.Selected;
            int keptLs = ls, keptLe = le;   // the whole song's, kept with it
            pcm = FromStart(pcm, ref ls, ref le);
            string name = song.Name, g = game, roundProblem = null, source = songFile, settings = Settings(), keepProblem = null;
            var have = installed;
            var levels = Levels();
            if (!await Run("Putting " + name + " in the game", () =>
            {
                InstalledSong about;
                var bank = BuildFor(g, have, target, layerIndex, pcm, ls, le, name, ultra, low, levels, out about);
                MusicBank.Install(g, target, bank, about);
                if (target.Round > 1) roundProblem = RoundMod.Ensure(g);   // Tom's Round BGM mod comes with round 2/3 music
                // and kept in the songs folder, so giving the game's music back never loses it
                try { MusicBank.Keep(target, layerIndex, name, bank, source, keptLs, keptLe, settings); }
                catch (Exception ex) { keepProblem = ex.Message; }
            })) return;
            Reload();
            string where = (slotLabel ?? target.Name) + (target.Round > 1 ? ", round " + target.Round : "") + (layerIndex > 0 ? ", " + MusicBank.LayerNames[layerIndex].ToLowerInvariant() + " layer" : "");
            if (roundProblem != null) Say(name + " is in for " + where + ", but " + roundProblem, true);
            else if (keepProblem != null) Say(name + " now plays for " + where + ", but it couldn't be kept in your songs folder: " + keepProblem, true);
            else Say(name + " now plays for " + where + ". Start the game to hear it"); 
        }

        // A docked stage's song: into its docked file (its zip, as BGM_<code>.csb), built on the music already in it, never
        // into the game (so the game may be open); kept in the songs folder as Put in the game keeps it.
        async Task PutInDocked(MusicSlot target)
        {
            if (MusicBank.GameFile(game, target.TemplateFile) == null) { Say("The game has no music to build " + target.Name + " on", true); return; }
            player.Stop();
            var pcm = Preview(false);
            int ls = wave.LoopStart, le = wave.LoopEnd, layerIndex = Layer, ultra = ultraMode.Selected, low = lowMode.Selected;
            int keptLs = ls, keptLe = le;
            pcm = FromStart(pcm, ref ls, ref le);
            string name = song.Name, g = game, zip = docked, source = songFile, settings = Settings(), keepProblem = null;
            var levels = Levels();
            if (!await Run("Putting " + name + " in the docked file", () =>
            {
                InstalledSong about;
                var bank = BuildFor(g, null, target, layerIndex, pcm, ls, le, name, ultra, low, levels, out about,
                                    DockedBank(zip, target.File) ?? File.ReadAllBytes(MusicBank.GameFile(g, target.TemplateFile)), DockedSong(zip, target.File));
                DockedMusic.Put(zip, target.File, bank, about);
                try { MusicBank.Keep(target, layerIndex, name, bank, source, keptLs, keptLe, settings); }
                catch (Exception ex) { keepProblem = ex.Message; }
            })) return;
            Reload();
            string where = dockedName + (target.Round > 1 ? ", round " + target.Round : "") + (layerIndex > 0 ? ", " + MusicBank.LayerNames[layerIndex].ToLowerInvariant() + " layer" : "");
            if (keepProblem != null) Say(name + " is in the docked file for " + where + ", but it couldn't be kept in your songs folder: " + keepProblem, true);
            else Say(name + " is in the docked file for " + where);
        }

        // Save: kept in the songs folder (overwriting) as Put in the game keeps it, without putting it in
        // the song as a song mod to upload (user 2026-10-16: "export csb in a manner for users to upload music"): a zip of
        // the game file it makes for the slot, built as Put in the game builds it but on the slot's own music (not on a
        // song of ours there), BGM_<code>.csb inside, which the Music tab and Browse Mods read as a song
        async Task ExportForGameBanana()
        {
            var target = Target;
            if (song == null || target == null) return;
            if (docked != null || MusicBank.GameFile(game, target.TemplateFile) == null) { Say("Pick one of the game's stages or fighters for it first", true); return; }
            string path;
            using (var ask = new SaveFileDialog { Filter = "Zip (*.zip)|*.zip", FileName = Clean(song.Name) + ".zip", Title = "Export for GameBanana" })
            {
                if (ask.ShowDialog(FindForm()) != DialogResult.OK) return;
                path = ask.FileName;
            }
            // a picture for its card (user 2026-10-17: "make it ask for an image": GameBanana's Sounds section takes none, so
            // Browse Mods shows the one in the zip); none picked: the zip goes without
            string picture = null;
            using (var pick = new OpenFileDialog { Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = "A picture for its card (a wide one fits best, such as 1280 x 720)" })
                if (pick.ShowDialog(FindForm()) == DialogResult.OK) picture = pick.FileName;
            player.Stop();
            var pcm = Preview(false);
            int ls = wave.LoopStart, le = wave.LoopEnd, layerIndex = Layer, ultra = ultraMode.Selected, low = lowMode.Selected;
            pcm = FromStart(pcm, ref ls, ref le);
            string name = song.Name, g = game, preview = Path.ChangeExtension(path, ".mp3");
            var levels = Levels();
            if (!await Run("Exporting " + name, () =>
            {
                InstalledSong about;
                byte[] bank = BuildFor(g, new Dictionary<string, InstalledSong>(), target, layerIndex, pcm, ls, le, name, ultra, low, levels, out about);
                if (File.Exists(path)) File.Delete(path);
                using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
                {
                    using (Stream s = zip.CreateEntry(target.InRound(1).File).Open()) s.Write(bank, 0, bank.Length);   // round 1's name: a song, whatever round it was made on
                    if (picture != null) System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, picture, Path.GetFileName(picture));
                }
                // the preview GameBanana's Sounds section asks for (user 2026-10-17: "export the 5 second mp3 next to the zip")
                MediaFoundation.EncodeMp3(preview, PreviewClip(pcm));
            })) return;
            Say("Exported " + Path.GetFileName(path) + (picture != null ? " with its picture" : "") + " and its 5-second preview " + Path.GetFileName(preview) + ": upload them to GameBanana as they are");
        }

        // 5 seconds of a song from 30 seconds in (the Music tab's listening spot; the end's last 5 seconds when it's shorter),
        // faded in and out over 40 ms so it doesn't click
        static short[] PreviewClip(short[] pcm)
        {
            int frames = pcm.Length / 2, length = Math.Min(5 * Song.Rate, frames), at = Math.Max(0, Math.Min(30 * Song.Rate, frames - length));
            var clip = new short[length * 2];
            Array.Copy(pcm, at * 2, clip, 0, clip.Length);
            int fade = Math.Min(Song.Rate * 40 / 1000, length / 2);
            for (int i = 0; i < fade; i++)
            {
                double g = i / (double)fade;
                for (int c = 0; c < 2; c++)
                {
                    clip[i * 2 + c] = (short)(clip[i * 2 + c] * g);
                    clip[(length - 1 - i) * 2 + c] = (short)(clip[(length - 1 - i) * 2 + c] * g);
                }
            }
            return clip;
        }

        // just the loop as an MP3 (user 2026-10-16: "the exported mp3 should just be the loop"), as it would go in
        // (seam smoothed, volume set), through Windows' own MP3 encoder
        async Task ExportLoop()
        {
            if (song == null) return;
            string path;
            using (var ask = new SaveFileDialog { Filter = "MP3 (*.mp3)|*.mp3", FileName = Clean(song.Name) + " (loop).mp3", Title = "Export loop" })
            {
                if (ask.ShowDialog(FindForm()) != DialogResult.OK) return;
                path = ask.FileName;
            }
            player.Stop();
            var pcm = Preview(false);
            int ls = wave.LoopStart, le = Math.Min(wave.LoopEnd, pcm.Length / 2);
            if (le - ls < Song.Rate) { Say("The loop must be at least a second long", true); return; }
            var loopPart = new short[(le - ls) * 2];
            Array.Copy(pcm, ls * 2, loopPart, 0, loopPart.Length);
            if (!await Run("Exporting the loop", () => MediaFoundation.EncodeMp3(path, loopPart))) return;
            Say("Exported the loop to " + Path.GetFileName(path));
        }

        static string Clean(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        async Task Save()
        {
            var target = Target;
            if (song == null || target == null) return;
            if (MusicBank.GameFile(game, target.TemplateFile) == null) { Say("The game has no music to build " + target.Name + " on", true); return; }
            player.Stop();
            var pcm = Preview(false);
            int ls = wave.LoopStart, le = wave.LoopEnd, layerIndex = Layer, ultra = ultraMode.Selected, low = lowMode.Selected;
            int keptLs = ls, keptLe = le;
            pcm = FromStart(pcm, ref ls, ref le);
            string settings = Settings(), name = song.Name, g = game, source = songFile, kept = null, zip = docked;
            var have = installed;
            var levels = Levels();
            if (!await Run("Saving " + name, () =>
            {
                InstalledSong about;
                byte[] start = zip == null ? null : DockedBank(zip, target.File) ?? File.ReadAllBytes(MusicBank.GameFile(g, target.TemplateFile));   // a docked stage's: on its file's music
                kept = MusicBank.Keep(target, layerIndex, name, BuildFor(g, have, target, layerIndex, pcm, ls, le, name, ultra, low, levels, out about,
                                                                      start, zip == null ? null : DockedSong(zip, target.File)), source, keptLs, keptLe, settings);
            })) return;
            Say("Saved as " + kept + " in your songs folder (the Music folder button at the bottom)");
            Toast.Show(this, "Songs saved to disk");   // always "songs" (user)
            var saved = Saved;
            if (saved != null) saved();   // under Your songs on the Music tab: only yours, nothing shared
        }

        void Restore()
        {
            var target = Target;
            if (target == null) return;
            if (docked != null)   // out of the docked file
            {
                try
                {
                    DockedMusic.TakeOut(docked, target.File);
                    Reload();
                    Say(dockedName + (target.Round > 1 ? " round " + target.Round + "'s" : "'s") + " music is out of the docked file");
                }
                catch (Exception ex) { Say(ex.Message, true); }
                return;
            }
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
            title.Text = slot == null ? "Music" : slotLabel ?? slot.Name;
            InstalledSong mine = null;
            bool inDocked = false;   // a docked stage's file has music for this round
            if (t != null && docked != null) { mine = DockedSong(docked, t.File); inDocked = mine != null || DockedBank(docked, t.File) != null; }
            else if (t != null) installed.TryGetValue(t.File, out mine);
            if (t == null) now.Text = "";
            else if (docked != null)
                now.Text = mine != null ? "In the docked file: your \"" + (mine.Layers[0] ?? mine.Song) + "\"  (" + t.File + ")"
                         : inDocked ? "In the docked file: its own music  (" + t.File + ")"
                         : "Not in the docked file yet: a song put here is built on " + Stages.Name(t.TemplateCode) + "'s music  (" + t.File + ")";
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
            foreach (var c in new Control[] { play, stop, testLoop, bassPreview, whole, atStart, atEnd, startBox, endBox, startHere, endHere, prevMatch, nextMatch, fit, save, exportBanana, exportLoop })
                c.Enabled = has;
            install.Enabled = has && t != null && listProblem == null;
            install.Text = Layer > 0 ? "Put on the " + MusicBank.LayerNames[Layer] + " layer" : docked != null ? "Put in the docked file" : "Put in the game";
            restore.Text = docked != null ? "Take it out of the docked file" : "Give back the game's music";
            restore.Enabled = docked != null ? inDocked : mine != null && listProblem == null;
            foreach (var b in new[] { install, restore }) b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 36;   // the docked wording is longer
            openGame.Enabled = t != null;
            if (!has) { startBox.Text = endBox.Text = lengthLabel.Text = matchLabel.Text = volumeNote.Text = ""; seam.Show(null, 0, 0); }
            player.Volume = Loudness();   // the slot, layer or Bass cut may have changed
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
        public event Action Saved;           // a song saved to your songs (the Music tab lists it at once, user 2026-10-17)

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
            list.Bounds = new Rectangle(6, 6, ListWidth - 12, h - 12);

            int x = ListWidth + Gap + 14, right = w - 14, ew = right - x;
            title.Bounds = new Rectangle(x, 8, ew - 520, 28);
            now.Bounds = new Rectangle(x, 36, ew - 520, 18);
            openGame.Location = new Point(right - openGame.Width, 10);
            openSong.Location = new Point(openGame.Left - openSong.Width - 6, 10);
            openSfxt.Location = new Point(openSong.Left - openSfxt.Width - 6, 10);
            title.Width = now.Width = Math.Max(40, openSfxt.Left - x - 10);
            clock.Bounds = new Rectangle(right - 170, 60, 170, 20);
            gameLevel.Location = new Point(clock.Left - gameLevel.Width - 10, 57);
            songLabel.Bounds = new Rectangle(x, 60, gameLevel.Left - x - 10, 20);

            // Preview Volume on a line of its own
            listen.Bounds = new Rectangle(right - 140, 84, 140, 20);
            listenLabel.Bounds = new Rectangle(listen.Left - 120, 82, 116, 24);
            const int below = 322;   // (40 of it: the export buttons' line, user 2026-10-16)
            int wh = Math.Max(120, h - 110 - below);
            wave.Bounds = new Rectangle(x, 108, ew, wh);
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
            prevMatch.Location = new Point(x, y);
            nextMatch.Location = new Point(prevMatch.Right + 6, y);
            fit.Location = new Point(nextMatch.Right + 6, y);
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
            // the export buttons on a line of their own, above the others (the row had no room for them)
            exportBanana.Location = new Point(right - exportBanana.Width, y);
            exportLoop.Location = new Point(exportBanana.Left - exportLoop.Width - 8, y);
            y += 40;
            // Put in the game / Give back the game's music: gone (user 2026-10-16: the Music tab puts songs in now); a docked
            // stage keeps them, as Put in / Take it out of the docked file (the zip to upload)
            install.Visible = restore.Visible = docked != null;
            install.Location = new Point(right - install.Width, y);
            restore.Location = new Point(install.Left - restore.Width - 8, y);
            save.Location = new Point(docked != null ? restore.Left - save.Width - 8 : right - save.Width, y);
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

    // The line between the game's stages and the custom ones in the Music page's list: yellow.
    class Rule : Clear
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(Color.FromArgb(255, 205, 60), 2f)) e.Graphics.DrawLine(pen, 6, Height / 2, Width - 6, Height / 2);
        }
    }

    // A stage or fighter in the Music page's list: its name, and a green dot when one of your songs plays there.
    class SlotRow : Clear
    {
        public MusicSlot Slot;
        public string Song;
        public string Docked, Label;   // a docked stage's: its zip, and its name in the list ("Urban War Zone (U05)")
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
            string name = Label ?? (Slot.Fighter ? Fighters.Name(Slot.Code) : Slot.Name);
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
