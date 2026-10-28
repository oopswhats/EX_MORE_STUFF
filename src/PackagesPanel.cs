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

        // a search over the packages (user 2026-10-26: "need a search function for the package codes section"): every word
        // in its name, fighter, kind, code or zip; the rows that don't match are hidden, so typing goes on in place
        readonly TextBox search = new TextBox { Width = 220, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10.5f), Margin = new Padding(8, 2, 8, 6) };
        readonly Timer typing = new Timer { Interval = 300 };
        readonly Dictionary<Control, string> searched = new Dictionary<Control, string>();   // row -> what the search looks in
        Control noMatch;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public PackagesPanel()
        {
            Controls.Add(list);
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search packages");   // EM_SETCUEBANNER: the grey hint
            search.TextChanged += (s, e) => { typing.Stop(); typing.Start(); };
            typing.Tick += (s, e) => { typing.Stop(); ShowMatches(); };
        }

        // the rows every word of the search is in; the others hidden
        void ShowMatches()
        {
            string[] words = search.Text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            list.SuspendLayout();
            int shown = 0;
            foreach (var row in searched)
            {
                bool match = words.All(w => row.Value.Contains(w));
                row.Key.Visible = match;
                if (match) shown++;
            }
            if (noMatch != null)
            {
                noMatch.Visible = searched.Count > 0 && shown == 0;
                noMatch.Text = "No packages match \"" + search.Text.Trim() + "\".";
            }
            list.ResumeLayout();
        }

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
                     "its zip is updated too, ready to share. A color pack moves together: type its first number. Stage codes are three capital letters or digits that aren't the game's " +
                     "(C01-C99 are the catalog's); costume slots are " + Catalog.FirstCustomCostume + "-" + Catalog.LastSlot + ". Share yours in " + Catalog.FirstCustomCostume + "-" +
                     Catalog.LastShared + "; " + Catalog.FirstPersonal + "-" + Catalog.LastPersonal + " and stage codes U12, 1U2, 12U ... are kept for your own mods; " +
                     (Catalog.LastPersonal + 1) + "-" + Catalog.LastSlot + " aren't free, nor are stage codes of one A or T with two digits (A12, 1A2, 12A, T12 ...).", width),
            };
            controls.Add(search);
            status.Width = width - 16;
            controls.Add(status);
            searched.Clear();
            var own = Game.IsGameFolder(game)
                ? Installer.Load(game).Select(r => Tuple.Create(r, true)).Concat(Installer.Removed(game).Select(r => Tuple.Create(r, false)))
                    .Where(t => t.Item1.Source == "file").OrderBy(t => t.Item1.Item.IsStage).ThenBy(t => t.Item1.Item.Fighter).ThenBy(t => t.Item1.Item.Code).ThenBy(t => t.Item1.Item.Slot).ThenBy(t => t.Item1.Item.Color).ToList()
                : new List<Tuple<Installed, bool>>();
            if (own.Count == 0) controls.Add(ShadowLabel.Note("No packages of your own yet: add one with Advanced > Add Mod From File.", width));
            // a color pack's colors (one zip): one row (user 2026-10-18: "could become very messy")
            foreach (var group in own.GroupBy(t => t.Item1.Item.IsColor && !string.IsNullOrEmpty(t.Item1.Package) ? "pack|" + Path.GetFullPath(t.Item1.Package).ToLowerInvariant() : "one|" + t.Item1.Item.Key))
            {
                var members = group.ToList();
                Control row = members.Count > 1 ? PackRow(members, width) : Row(members[0].Item1, members[0].Item2, width);
                searched[row] = string.Join(" ", members.Select(t =>
                {
                    Item item = t.Item1.Item;
                    return Weight.Name(t.Item1) + " " + Fighters.Name(item.Fighter) + " " + item.Fighter + " " + (item.IsStage ? "stage " + item.Code : (item.IsColor ? "color " + item.Color : "costume") + " " + item.Slot) +
                           " " + Path.GetFileName(t.Item1.Package);
                })).ToLowerInvariant();
                controls.Add(row);
            }
            noMatch = ShadowLabel.Note("", width);
            controls.Add(noMatch);
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != status && c != search) c.Dispose();   // the status line and search box stay
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
            ShowMatches();
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
            return !string.IsNullOrEmpty(record.Package) && File.Exists(record.Package) && !shared && !record.Theirs;
        }

        // the game stages a stage stands in for (the Stages page's replace): deleting it gives them their own back
        static List<string> StandingIn(string game, Item item)
        {
            return item.IsStage ? Replacements.Load(game).Where(r => r.Source == item.Code).Select(r => r.Stage).ToList() : new List<string>();
        }

        // the game costumes a custom costume stands in for (the Costumes page's replace): deleting it gives them their own back
        static List<int> CostumesStandingIn(string game, Item item)
        {
            return item.IsCostume ? CostumeSwaps.Load(game).Where(s => s.Fighter == item.Fighter && s.Source == item.Slot).Select(s => s.Costume).ToList() : new List<int>();
        }

        /// <summary>Asks whether to delete a package of the player's (DeleteDialog). Deleting takes EX More Stuff's copy of its
        /// zip to the Recycle Bin too (RecyclesZip; user 2026-10-14: keeping it "is just polluting the user's PC"): the
        /// player's own file, or the download, is still there.</summary>
        public static DeleteDialog.Choice AskDelete(IWin32Window owner, string game, Installed record, string title)
        {
            using (var dialog = new DeleteDialog(title, DeleteQuestion(game, record)))
            {
                dialog.ShowDialog(owner);
                return dialog.Picked;
            }
        }

        // what deleting does (user 2026-10-14: say it's EX More Stuff's copy that goes, not the file they added)
        public static string DeleteQuestion(string game, Installed record)
        {
            Item item = record.Item;
            var costumes = CostumesStandingIn(game, item).Select(CostumeGrid.CostumeName).ToList();
            bool inGame = Installer.Load(game).Any(r => r.Item.Key == item.Key);
            bool downloaded = record.Source == "catalog" || (item.Id ?? "").StartsWith("gamebanana:", StringComparison.OrdinalIgnoreCase);
            var standing = StandingIn(game, item).Select(Stages.Name).ToList();
            return (inGame ? "It's taken out of the game and out of EX More Stuff." : "It's taken out of EX More Stuff.") +
                   (item.IsStage ? " Songs you put on " + item.Code + " go with it." : "") + "\n" +
                   (standing.Count > 0 ? string.Join(" and ", standing) + (standing.Count > 1 ? ", which play as it now, get their own stages back.\n"
                                                                                           : ", which plays as it now, gets its own stage back.\n") : "") +
                   (costumes.Count > 0 ? Fighters.Name(item.Fighter) + "'s " + string.Join(" and ", costumes) + (costumes.Count > 1 ? ", which play as it now, get their own costumes back.\n"
                                                                                                                                  : ", which plays as it now, gets its own costume back.\n") : "") +
                   "\n" + (downloaded ? "You can download it again from Browse Mods." : "The original mod file you added is untouched.");
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
            foreach (int costume in CostumesStandingIn(game, record.Item))
            {
                string kept = CostumeSwaps.Restore(game, record.Item.Fighter, costume);
                if (kept != null) notes.Add(Fighters.Name(record.Item.Fighter) + "'s " + CostumeGrid.CostumeName(costume) + ": " + kept);
            }
            Installer.Purge(game, record.Item.Key);
            string songs = record.Item.IsStage ? MusicBank.RemoveCode(game, record.Item.Code) : null;
            if (songs != null) notes.Add(songs);
            RoundMod.RemoveIfUnused(game);
            return notes.Count > 0 ? string.Join("\n", notes) : null;
        }

        /// <summary>An update's leftovers (user 2026-10-20: Tom's stages kept saying Update after updating): the packages a
        /// mod (`key`, "gamebanana:123:") put in that its new version (`version`) didn't put in again (its zip renamed on
        /// GameBanana gives its parts other ids, so they went in beside the old ones, under other codes): out for good, their
        /// zips to the Recycle Bin when nothing else uses them. Returns how many.</summary>
        public static int DropLeftovers(string game, string key, string version)
        {
            int count = 0;
            foreach (Installed r in Installer.Load(game).Concat(Installer.Removed(game))
                         .Where(r => (r.Item.Id ?? "").StartsWith(key, StringComparison.OrdinalIgnoreCase) && r.Item.Version != version).ToList())
            {
                bool recycle = RecyclesZip(game, r);
                PurgeForGood(game, r);
                if (recycle) try { RecycleZip(r.Package); } catch (Exception) { }
                count++;
            }
            return count;
        }

        public static void RecycleZip(string path)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        async Task Delete(Installed record, PackageRow row)
        {
            if (Game.IsRunning()) { Say("Can't delete it: close the game first", true); return; }
            var choice = AskDelete(FindForm(), game, record, row.Title);
            if (choice == DeleteDialog.Choice.Cancel) return;
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
            Say(row.Title + " is deleted" + (note != null ? ". " + note : ""), note != null);
        }

        // A color pack's row: its name, Colors 30-43, Change moves them all (a new first number, the spacing kept), Delete
        // takes them all
        Control PackRow(List<Tuple<Installed, bool>> members, int width)
        {
            var records = members.Select(t => t.Item1).OrderBy(r => r.Item.Color).ToList();
            Installed first = records[0];
            int inGame = members.Count(t => t.Item2);
            var row = new PackageRow
            {
                Size = new Size(width, 96),
                Margin = new Padding(6, 4, 6, 4),
                Title = first.Shown ?? first.Item.Name,
                Detail = (inGame == records.Count ? "In the game" : inGame == 0 ? "Switched off" : inGame + " of " + records.Count + " in the game") + "   ·   " + first.Package,
                Kind = "Colors",
            };
            Task.Run(() => Picture(first)).ContinueWith(t =>
            {
                if (t.IsFaulted || row.IsDisposed) return;
                row.Picture = t.Result;
                row.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            row.Code.MaxLength = 2;
            row.Code.Text = records[0].Item.Color + "-" + records[records.Count - 1].Item.Color;
            row.Change.Click += async (s, e) => await ChangePack(records, row);
            row.Remove.Click += async (s, e) => await DeletePack(records, row);
            return row;
        }

        // why a pack's colors can't move to start at `start`, or null: each keeps its distance from the first
        string PackProblem(List<Installed> records, int start)
        {
            int shift = start - records[0].Item.Color;
            if (shift == 0) return "that's its first number already";
            int lowest = Catalog.FirstColor(records[0].Item.Slot);
            if (start < lowest || records[records.Count - 1].Item.Color + shift > Catalog.LastColor)
                return "its colors would go past " + lowest + "-" + Catalog.LastColor;
            if (records.Any(r => r.Source != "file" || string.IsNullOrEmpty(r.Package) || !File.Exists(r.Package))) return "its zip isn't where it was";
            var own = new HashSet<int>(records.Select(r => r.Item.Color));
            Item one = records[0].Item;
            var others = Installer.Load(game).Concat(Installer.Removed(game)).Where(r => r.Item.IsColor && r.Item.Fighter == one.Fighter && r.Item.Slot == one.Slot && !records.Any(x => x.Item.Key == r.Item.Key)).ToList();
            string folder = Game.FolderFor(game, one);
            foreach (Installed r in records)
            {
                int to = r.Item.Color + shift;
                Installed taken = others.FirstOrDefault(o => o.Item.Color == to);
                if (taken != null) return "color " + to + " is already used by " + taken.Item.TitleNamed(taken.Shown ?? taken.Item.Name);
                if (!own.Contains(to) && File.Exists(Path.Combine(folder, one.Fighter + "_" + one.Slot.ToString("D2") + "_" + to.ToString("D2") + ".col.emb")))
                    return "the game folder already has color " + to + " files that EX More Stuff didn't put there";
            }
            if (Game.IsRunning()) return "close the game first";
            return null;
        }

        async Task ChangePack(List<Installed> records, PackageRow row)
        {
            string text = row.Code.Text.Trim();
            if (text.Contains("-")) text = text.Substring(0, text.IndexOf('-'));
            int start;
            if (!int.TryParse(text, out start)) { Say("A color is a number", true); return; }
            string blocked = Blocked != null ? Blocked() : null;
            string problem = blocked ?? PackProblem(records, start);
            if (problem != null) { Say("Can't change it: " + problem, true); return; }
            int shift = start - records[0].Item.Color, last = records[records.Count - 1].Item.Color;
            string from = "colors " + records[0].Item.Color + "-" + last, to = "colors " + start + "-" + (last + shift);
            if (MessageBox.Show(FindForm(), "Move " + row.Title + " from " + from + " to " + to + "?\n\nIts zip is rewritten with the new numbers (" + Path.GetFileName(records[0].Package) + ")" +
                    (Installer.Load(game).Any(r => records.Any(x => x.Item.Key == r.Item.Key)) ? ", and they're put in the game again under them." : "."),
                    "EX More Stuff", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            Busy(true);
            Say("Moving " + row.Title + " to " + to + "...");
            string g = game, note = null;
            try
            {
                // moving up: the highest first, down: the lowest first, so each lands on a number that's free by then
                var order = shift > 0 ? records.OrderByDescending(r => r.Item.Color).ToList() : records.OrderBy(r => r.Item.Color).ToList();
                await Task.Run(() =>
                {
                    foreach (Installed r in order)
                    {
                        Installed now = Installer.Load(g).Concat(Installer.Removed(g)).First(x => x.Item.Key == r.Item.Key);
                        note = CodeChange.Apply(g, now, null, r.Item.Color + shift) ?? note;
                    }
                });
            }
            catch (Exception ex) { Say("It didn't all move: " + ex.Message, true); Fill(); return; }
            finally { Busy(false); }
            var changed = Changed;
            if (changed != null) changed();
            Fill();
            Say(row.Title + " is now " + to + (note != null ? ". " + note : ""), note != null);
        }

        async Task DeletePack(List<Installed> records, PackageRow row)
        {
            if (Game.IsRunning()) { Say("Can't delete it: close the game first", true); return; }
            var choice = AskDelete(FindForm(), game, records[0], row.Title + " (" + records.Count + " colors)");
            if (choice == DeleteDialog.Choice.Cancel) return;
            // its zip goes when nothing outside the pack uses it
            var keys = new HashSet<string>(records.Select(r => r.Item.Key));
            bool shared = Installer.Load(game).Concat(Installer.Removed(game)).Any(r => !keys.Contains(r.Item.Key) && string.Equals(r.Package, records[0].Package, StringComparison.OrdinalIgnoreCase));
            bool recycle = !shared && !records[0].Theirs && File.Exists(records[0].Package);
            Busy(true);
            string g = game;
            var notes = new List<string>();
            try
            {
                await Task.Run(() => { foreach (Installed r in records) { string n = PurgeForGood(g, r); if (n != null) notes.Add(n); } });
                if (recycle) RecycleZip(records[0].Package);
            }
            catch (Exception ex) { Say("It wasn't all deleted: " + ex.Message, true); return; }
            finally
            {
                Busy(false);
                var changed = Changed;
                if (changed != null) changed();
                Fill();
            }
            Say(row.Title + " is deleted" + (notes.Count > 0 ? ". " + string.Join(" ", notes.Distinct()) : ""), notes.Count > 0);
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
                image = bytes == null ? null : Pictures.Read(bytes);
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
            if (problem) BugReport.Noted(text);
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
            Theme.Draw(g, Stage ? "Code" : Kind == "Color" || Kind == "Colors" ? "Color" : "Slot", Theme.Font(9f), Theme.Muted, new Rectangle(Code.Left - 60, Code.Top, 54, Code.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
