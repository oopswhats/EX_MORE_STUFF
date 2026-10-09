using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Package codes: the player's own costume and stage packages (Add from file), in the game or switched
    // off, each with its stage code or costume slot to change (CodeChange): the zip itself is updated, ready to share.
    class PackagesPanel : Clear
    {
        public event Action Changed;     // EX More Stuff's lists need reading again
        public Func<string> Blocked;
        public event Action<bool> Working;   // a move or a delete started or ended (the rest of the window waits)

        void Busy(bool on)
        {
            Enabled = !on;
            UseWaitCursor = on;
            var working = Working;
            if (working != null) working(on);
        }     // why codes can't change right now, or null

        string game;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        readonly Label status = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Font(9.5f), Height = 22, AutoEllipsis = true };
        static readonly Dictionary<string, Image> pictures = new Dictionary<string, Image>();   // by zip and its time

        public PackagesPanel() { Controls.Add(list); }

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

        // laid out again at its real width once it's shown (it's first filled while hidden)
        int filledWidth;
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (game != null && Visible && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        void Fill()
        {
            stale = false;
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            var controls = new List<Control>
            {
                new Heading("Package codes", width),
                ShadowLabel.Note("Your own costume, color and stage packages (added from a zip). Change a stage's code, a costume's slot or a new color's number (30-99) here: " +
                     "its zip is updated too, ready to share. Stage codes are three capital letters or digits that aren't the game's " +
                     "(C01-C99 are the catalog's); costume slots are " + Catalog.FirstCustomCostume + "-" + Catalog.LastSlot + ". Share yours in " + Catalog.FirstCustomCostume + "-" +
                     Catalog.LastShared + "; " + (Catalog.LastShared + 1) + "-" + Catalog.LastSlot + " aren't free, nor are stage codes of one A or T with two digits (A12, 1A2, 12A, T12 ...).", width),
            };
            status.Width = width - 16;
            controls.Add(status);
            var own = Game.IsGameFolder(game)
                ? Installer.Load(game).Select(r => Tuple.Create(r, true)).Concat(Installer.Removed(game).Select(r => Tuple.Create(r, false)))
                    .Where(t => t.Item1.Source == "file").OrderBy(t => t.Item1.Item.IsStage).ThenBy(t => t.Item1.Item.Fighter).ThenBy(t => t.Item1.Item.Code).ThenBy(t => t.Item1.Item.Slot).ThenBy(t => t.Item1.Item.Color).ToList()
                : new List<Tuple<Installed, bool>>();
            if (own.Count == 0) controls.Add(ShadowLabel.Note("No packages of your own yet: add one with Advanced > Add Mod From File.", width));
            foreach (var t in own) controls.Add(Row(t.Item1, t.Item2, width));
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != status) c.Dispose();   // the status line stays
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        Control Row(Installed record, bool inGame, int width)
        {
            Item item = record.Item;
            var row = new PackageRow
            {
                Size = new Size(width, 96),
                Margin = new Padding(6, 4, 6, 4),
                Title = item.IsStage ? item.TitleNamed(record.Shown ?? item.Name) : Fighters.Name(item.Fighter) + " " + item.TitleNamed(record.Shown ?? item.Name),
                Detail = (inGame ? "In the game" : "Switched off") + "   ·   " + record.Package,
                Kind = item.IsStage ? "Stage" : item.IsColor ? "Color" : "Costume",
            };
            // its picture and whether its zip still holds it: read from the zip in the background (big zips, cold disk)
            string detail = row.Detail;
            Task.Run(() => Tuple.Create(Picture(record), File.Exists(record.Package) && !Installer.PackageHolds(record.Package, item))).ContinueWith(t =>
            {
                if (t.IsFaulted || row.IsDisposed) return;
                row.Picture = t.Result.Item1;
                if (t.Result.Item2) row.Detail = (inGame ? "In the game" : "Switched off") + "   ·   its zip holds another slot or code now   ·   " + record.Package;
                row.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            row.Code.MaxLength = item.IsStage ? 3 : 2;
            row.Code.Text = item.IsStage ? item.Code : item.IsColor ? item.Color.ToString() : item.Slot.ToString();
            row.Change.Click += async (s, e) => await Change(record, row);
            row.Remove.Click += async (s, e) => await Delete(record, row);
            return row;
        }

        // Gone for good (here, and from the trash can on its card): out of the game and off the lists (a stage's
        // Music-page songs given back too); its zip to the Recycle Bin, unless it isn't where it was or another package
        // still uses it. Other changes waiting for Apply stay waiting.
        public static bool RecyclesZip(string game, Installed record)
        {
            bool shared = Installer.Load(game).Concat(Installer.Removed(game))
                .Any(r => r.Item.Key != record.Item.Key && string.Equals(r.Package, record.Package, StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrEmpty(record.Package) && File.Exists(record.Package) && !shared;
        }

        // the game stages a stage stands in for (the Stages page's replace): deleting it gives them their own back
        static List<string> StandingIn(string game, Item item)
        {
            return item.IsStage ? Replacements.Load(game).Where(r => r.Source == item.Code).Select(r => r.Stage).ToList() : new List<string>();
        }

        public static string DeleteQuestion(string game, Installed record, string title)
        {
            Item item = record.Item;
            bool inGame = Installer.Load(game).Any(r => r.Item.Key == item.Key), recycle = RecyclesZip(game, record);
            bool kept = !recycle && !string.IsNullOrEmpty(record.Package);
            var standing = StandingIn(game, item).Select(Stages.Name).ToList();
            return "Delete " + title + " for good?\n\n" +
                   (inGame ? "It's taken out of the game and off EX More Stuff's lists" : "It's taken off EX More Stuff's lists") +
                   (item.IsStage ? ", with any songs you put on " + item.Code + " on the Music page" : "") + ".\n" +
                   (standing.Count > 0 ? string.Join(" and ", standing) + (standing.Count > 1 ? ", which play as it now, get their own stages back.\n"
                                                                                           : ", which plays as it now, gets its own stage back.\n") : "") +
                   (recycle ? "Its zip (" + Path.GetFileName(record.Package) + ") goes to the Recycle Bin." :
                    kept && File.Exists(record.Package) ? "Its zip stays: another package uses it." : kept ? "Its zip isn't where it was, so nothing else is touched." : "");
        }

        /// <summary>The deleting itself, off the window's thread (the zip goes after, with RecycleZip). A note on songs, or null.</summary>
        public static string PurgeForGood(string game, Installed record)
        {
            var notes = new List<string>();
            foreach (string stage in StandingIn(game, record.Item))
            {
                string kept = Replacements.Restore(game, stage);
                if (kept != null) notes.Add(Stages.Name(stage) + ": " + kept);
            }
            Installer.Purge(game, record.Item.Key);
            string songs = record.Item.IsStage ? MusicBank.RemoveCode(game, record.Item.Code) : null;
            if (songs != null) notes.Add(songs);
            RoundMod.RemoveIfUnused(game);
            return notes.Count > 0 ? string.Join("\n", notes) : null;
        }

        public static void RecycleZip(string path)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        async Task Delete(Installed record, PackageRow row)
        {
            if (Game.IsRunning()) { Say("Can't delete it: close the game first", true); return; }
            if (MessageBox.Show(FindForm(), DeleteQuestion(game, record, row.Title), "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;
            bool recycle = RecyclesZip(game, record);
            Busy(true);
            string g = game, note = null;
            try
            {
                note = await Task.Run(() => PurgeForGood(g, record));
                if (recycle) RecycleZip(record.Package);
            }
            catch (Exception ex) { Say("It wasn't all deleted: " + ex.Message, true); return; }
            finally
            {
                Busy(false);
                var changed = Changed;
                if (changed != null) changed();
                Fill();
            }
            Say(row.Title + " is deleted" + (recycle ? "; its zip is in the Recycle Bin" : "") + (note != null ? ". " + note : ""), note != null);
        }

        static Image Picture(Installed record)
        {
            if (string.IsNullOrEmpty(record.Package) || !File.Exists(record.Package)) return null;
            string key = record.Package + "|" + File.GetLastWriteTimeUtc(record.Package).Ticks;
            Image image;
            lock (pictures) if (pictures.TryGetValue(key, out image)) return image;
            try
            {
                byte[] bytes = Installer.PackagePicture(record.Package, record.Item);
                image = bytes == null ? null : Image.FromStream(new MemoryStream(bytes));
            }
            catch (Exception) { image = null; }
            lock (pictures) pictures[key] = image;
            return image;
        }

        async Task Change(Installed record, PackageRow row)
        {
            Item item = record.Item;
            string code = row.Code.Text.Trim().ToUpperInvariant();
            int slot = 0;
            if (!item.IsStage && !int.TryParse(code, out slot)) { Say(item.IsColor ? "A color is a number" : "A costume slot is a number", true); return; }
            string blocked = Blocked != null ? Blocked() : null;
            string problem = blocked ?? CodeChange.Problem(game, record, code, slot);
            if (problem != null) { Say("Can't change it: " + problem, true); return; }
            string from = item.IsStage ? item.Code : item.IsColor ? "color " + item.Color : "slot " + item.Slot,
                   to = item.IsStage ? code : item.IsColor ? "color " + slot : "slot " + slot;
            // the game's, taken on GameBanana, or not free: a warning, the player's call
            string warning = Seats.Warning(new Item { Id = item.Id, Type = item.Type, Fighter = item.Fighter, Slot = item.IsStage ? 0 : slot, Code = item.IsStage ? code : "" });
            string notFree = warning != null ? warning + ". Use it anyway?\n\n" : "";
            if (MessageBox.Show(FindForm(), notFree + "Move " + row.Title + " from " + from + " to " + to + "?\n\nIts zip is rewritten with the new " +
                    (item.IsStage ? "code" : item.IsColor ? "number" : "slot") + " (" + Path.GetFileName(record.Package) + ")" +
                    (Installer.Load(game).Any(r => r.Item.Key == item.Key) ? ", and it's put in the game again under it." : "."),
                    "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            Busy(true);
            Say("Moving " + row.Title + " to " + to + "...");
            string g = game, note = null;
            try { note = await Task.Run(() => CodeChange.Apply(g, record, code, slot)); }
            catch (Exception ex) { Say("It didn't move: " + ex.Message, true); return; }
            finally { Busy(false); }
            var changed = Changed;
            if (changed != null) changed();
            Fill();
            Say(row.Title + " is now " + to + (note != null ? ". " + note : ""), note != null);
        }

        void Say(string text, bool problem = false)
        {
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
        }
    }

    // One package on the Package codes page: its picture, name, where it is, and its code to change.
    class PackageRow : Clear
    {
        public Image Picture;
        public string Title, Detail;
        public string Kind = "Costume";   // Costume, Color or Stage
        bool Stage { get { return Kind == "Stage"; } }
        public readonly TextBox Code = new TextBox { BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Bold(11f),
                                                     CharacterCasing = CharacterCasing.Upper, TextAlign = HorizontalAlignment.Center };
        public readonly FlatButton Change = new FlatButton("Change"), Remove = new FlatButton("Delete");

        public PackageRow()
        {
            Controls.Add(Code);
            Controls.Add(Change);
            Controls.Add(Remove);
            // a stage code: letters and digits, 3; a costume slot: digits, 2
            Code.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                bool ok = Stage ? char.IsLetterOrDigit(e.KeyChar) && e.KeyChar < 128 : char.IsDigit(e.KeyChar);
                if (!ok) e.Handled = true;
            };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Remove.Location = new Point(Width - Remove.Width - 14, (Height - Remove.Height) / 2);
            Change.Location = new Point(Remove.Left - Change.Width - 8, (Height - Change.Height) / 2);
            Code.Bounds = new Rectangle(Change.Left - 74, (Height - 28) / 2, 64, 28);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            var picture = new RectangleF(10, 10, 136, Height - 20);
            using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, picture);
            if (Picture != null) Theme.Fit(g, Picture, picture, false);
            int x = 160, right = Math.Max(x + 40, Code.Left - 66);
            Theme.Draw(g, Title, Theme.Bold(11f), Theme.Text, new Rectangle(x, 14, right - x, 24), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Kind, Theme.Font(9f), Theme.Accent, new Rectangle(x, 40, right - x, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(x, 60, right - x, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
            Theme.Draw(g, Stage ? "Code" : Kind == "Color" ? "Color" : "Slot", Theme.Font(9f), Theme.Muted, new Rectangle(Code.Left - 60, Code.Top, 54, Code.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
