using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Disk space: the room what EX More Stuff put on this PC takes, biggest first. Each costume and stage
    // (in the game or switched off), each replaced game stage, each song from the Music page and Tom's mod, with its
    // files in the game, its zip (the player's own packages) and its backups (files it moved aside to put its own in).
    class StoragePanel : Clear
    {
        string game;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        int filledWidth;

        public StoragePanel() { Controls.Add(list); }

        bool stale;   // the lists changed while the page was hidden: filled when it's shown

        public void SetGame(string folder)
        {
            game = folder;
            if (Visible) Fill(); else stale = true;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && stale) Fill();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (game != null && Visible && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        // one line of the page: what it is, and its bytes in the game, in its zip and in backups
        sealed class Use
        {
            public string Title, Kind;
            public long InGame, Zip, Backup;
            public long Total { get { return InGame + Zip + Backup; } }
        }

        static long FileSize(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (Exception) { return 0; }
        }

        static string StageName(string code)
        {
            return Array.IndexOf(Stages.Codes, code) >= 0 ? Stages.Name(code) : "Custom stage " + code;
        }

        List<Use> Gather(out long otherBackups)
        {
            var uses = new List<Use>();
            otherBackups = 0;
            if (!Game.IsGameFolder(game)) return uses;
            string patch = Game.PatchFolder(game), backups = Path.Combine(patch, "ex_more_stuff_backup");
            Func<Installed, string> title = r => (r.Item.IsStage ? "" : Fighters.Name(r.Item.Fighter) + " ") + r.Item.TitleNamed(r.Shown ?? r.Item.Name);
            foreach (Installed r in Installer.Load(game))
                uses.Add(new Use
                {
                    Title = title(r), Kind = Kind(r.Item) + (r.Source == "file" ? ", your own package" : ", from the catalog"),
                    InGame = r.Files.Sum(f => FileSize(Path.Combine(patch, f))),
                    Zip = r.Source == "file" && !string.IsNullOrEmpty(r.Package) ? FileSize(r.Package) : 0,
                });
            foreach (Installed r in Installer.Removed(game))
                uses.Add(new Use { Title = title(r), Kind = Kind(r.Item) + ", switched off", Zip = string.IsNullOrEmpty(r.Package) ? 0 : FileSize(r.Package) });
            long counted = 0;
            foreach (Replacement r in Replacements.Load(game))
            {
                long backup = r.Backups.Sum(b => FileSize(Path.Combine(backups, b)));
                counted += backup;
                uses.Add(new Use
                {
                    Title = StageName(r.Stage) + " replaced by " + StageName(r.Source), Kind = "Replaced game stage",
                    InGame = r.Files.Keys.Sum(f => FileSize(Path.Combine(patch, f))), Backup = backup,
                });
            }
            try
            {
                foreach (InstalledSong s in MusicBank.Load(game).Values)
                {
                    var slot = MusicSlot.FromFile(s.File);
                    long backup = s.Backup != null ? FileSize(Path.Combine(backups, s.Backup)) : 0;
                    counted += backup;
                    uses.Add(new Use
                    {
                        Title = slot.Name + (slot.Round > 1 ? ", round " + slot.Round : "") + ": " + s.Song, Kind = "Song (Music page)",
                        InGame = FileSize(MusicBank.PatchFile(game, s.File)), Backup = backup,
                    });
                }
            }
            catch (InvalidDataException) { }   // an unreadable song list: the Music page says so
            if (File.Exists(Path.Combine(patch, "ex_more_stuff_dinput8.txt")))
                uses.Add(new Use { Title = "Tom's Round BGM mod", Kind = "For round 2 and 3 music", InGame = FileSize(Path.Combine(game, "dinput8.dll")) });
            // anything else in the backup folder (left from earlier versions, or files nothing lists any more)
            try
            {
                if (Directory.Exists(backups))
                    otherBackups = Math.Max(0, Directory.EnumerateFiles(backups, "*", SearchOption.AllDirectories).Sum(f => FileSize(f)) - counted);
            }
            catch (Exception) { }
            if (otherBackups > 0) uses.Add(new Use { Title = "Other backups", Kind = "Files in ex_more_stuff_backup nothing lists any more", Backup = otherBackups });
            return uses.OrderByDescending(u => u.Total).ToList();
        }

        // a summary figure: none is "0 MB" there (the rows use "-")
        static string Total(long bytes) { return bytes <= 0 ? "0 MB" : Megabytes(bytes); }

        public static string Megabytes(long bytes)
        {
            if (bytes <= 0) return "-";
            if (bytes < 100 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return (bytes / 1048576.0).ToString(bytes < 10 * 1048576 ? "0.0" : "0") + " MB";
        }

        void Fill()
        {
            stale = false;
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            long other;
            var uses = Gather(out other);
            long largest = uses.Count > 0 ? uses[0].Total : 1;
            var controls = new List<Control>
            {
                new Heading("Disk space", width),
                ShadowLabel.Note("What each costume, stage and song takes, biggest first.", width),
                Line("Altogether " + Total(uses.Sum(u => u.Total)), width, Theme.Bold(12.5f), Theme.Text),
            };
            if (uses.Count == 0) controls.Add(ShadowLabel.Note("Nothing yet.", width));
            foreach (Use u in uses)
                controls.Add(new StorageRow
                {
                    Size = new Size(width, 64), Margin = new Padding(6, 3, 6, 3),
                    Title = u.Title, Kind = u.Kind, Bytes = u.Total, Share = u.Total / (double)Math.Max(1, largest),
                });
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        static string Kind(Item item) { return item.IsStage ? "Stage" : item.IsColor ? "Color" : "Costume"; }

        static ShadowLabel Line(string text, int width, Font font, Color color)
        {
            var line = new ShadowLabel { Text = text, ForeColor = color, Font = font, Margin = new Padding(8, 4, 8, 6) };
            line.Size = new Size(width - 16, TextRenderer.MeasureText(text, font, new Size(width - 18, 0), TextFormatFlags.WordBreak).Height + 6);
            return line;
        }
    }

    // One line of the Disk space page: name, kind, a bar of its share of the biggest, and its size.
    class StorageRow : Clear
    {
        public string Title, Kind;
        public long Bytes;
        public double Share;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 9))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            int right = Width - 124;
            Theme.Draw(g, Title, Theme.Bold(10f), Theme.Text, new Rectangle(14, 8, right - 24, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Kind, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 30, right - 24, 16), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var track = new RectangleF(14, Height - 13, right - 24, 4);
            using (var back = new SolidBrush(Theme.Well)) g.FillRectangle(back, track);
            using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, track.X, track.Y, (float)(track.Width * Math.Max(0.01, Math.Min(1, Share))), track.Height);
            Theme.Draw(g, StoragePanel.Megabytes(Bytes), Theme.Bold(11f), Theme.Text, new Rectangle(right, 0, 110, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
