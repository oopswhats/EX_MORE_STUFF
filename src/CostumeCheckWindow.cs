using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // The costume check's results (CostumeCheck; user 2026-10-23: "add it to ex in a logical way (validator/fixer)"): a row
    // per costume that needs a fix, is worth a look or is fine (a filter each), what's wrong said plainly with the check's
    // own line under it, Fix a copy where something can be fixed (the copy goes where the player says; the costume stays
    // as it is) and the folder it's in. Save report... writes the whole check as text, to look into or send to its maker.
    // Only the results are kept: Fix reads the costume's folder or zip again.
    class CostumeCheckWindow : Form
    {
        readonly string game;
        readonly List<CostumeCheck.Result> results;
        readonly List<FlatButton> fixButtons = new List<FlatButton>();
        bool fixing;   // a fixed copy being made: one at a time, and the window stays until it's done
        readonly FlowLayoutPanel rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Base, Padding = new Padding(20, 4, 20, 20) };
        readonly FlatButton faulty, look, fine;
        readonly Label status = new Label { AutoSize = false, Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Base, ForeColor = Theme.Muted, Font = Theme.Font(9.5f), Padding = new Padding(22, 4, 0, 0) };

        static int Group(CostumeCheck.Result r) { return r.Verdict == CostumeCheck.Verdict.Faulty ? 0 : r.Verdict == CostumeCheck.Verdict.Ok ? 2 : 1; }

        // each kind of finding in a player's words. The stand-in: what a player without the costume sees instead, the
        // fighter's costume 1 (2 or 3 in a mirror match)
        static readonly Dictionary<string, string> Plain = new Dictionary<string, string>
        {
            { "DESYNC", "Online, a player without this costume desyncs: bones the fight uses sit differently from the costume they see instead (the stand-in below)." },
            { "DESYNC?", "It may desync online: bones the fight uses differ from the stand-in below, where the game's IK may set them anyway." },
            { "IK", "The IK records of bones the fight uses differ from the stand-in's." },
            { "EFFECTS", "Only effect bones differ from the stand-in: effects show a little elsewhere. Not a fault." },
            { "WHITE", "Parts draw white: a material the model uses is missing from these colors." },
            { "EMM>CAP", "A color draws untextured: it has more materials than the model takes." },
            { "FILES", "Files the game loads for every costume are missing." },
            { "COLORS", "Colors are missing: rollback games only offer a costume with all ten." },   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
            { "SLOT", "Its costume number is outside 08-99: rollback games don't offer it." },
            { "STOCK", "The game's costumes 1-3 weren't found, so its bones weren't checked." },
            { "BROKEN", "The model file is damaged: something in it points past its end. The game closes when it loads it." },
            { "PALETTE", "Too many bones move one piece of the model (more than 24): the game closes when it loads it." },
            { "SUBMESHES", "A part of the model is split into too many pieces (more than 31): the game closes when it loads it." },
            { "UNREAD", "Its files couldn't be read, so it wasn't checked." },
        };

        public CostumeCheckWindow(string game, string title, List<CostumeCheck.Result> results)
        {
            this.game = game; this.results = results;
            Text = title;
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            ClientSize = new Size(1100, 760);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Base };
            faulty = new FlatButton("Need a fix · " + results.Count(r => Group(r) == 0));
            look = new FlatButton("Worth a look · " + results.Count(r => Group(r) == 1));
            fine = new FlatButton("Fine · " + results.Count(r => Group(r) == 2));
            int x = 22;
            foreach (FlatButton b in new[] { faulty, look, fine }) { b.Location = new Point(x, 16); x = b.Right + 8; top.Controls.Add(b); }
            faulty.Click += (s, e) => ShowGroup(0);
            look.Click += (s, e) => ShowGroup(1);
            fine.Click += (s, e) => ShowGroup(2);
            var report = new FlatButton("Save report (.txt)...") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            report.Location = new Point(ClientSize.Width - report.Width - 22, 16);
            report.Click += (s, e) => SaveReport();
            top.Controls.Add(report);
            status.Text = results.Count + (results.Count == 1 ? " costume" : " costumes") + " checked";
            Controls.Add(rows);
            Controls.Add(status);
            Controls.Add(top);
            rows.Resize += (s, e) => { foreach (Control c in rows.Controls) c.Width = RowWidth(); };
            ShowGroup(results.Any(r => Group(r) == 0) ? 0 : results.Any(r => Group(r) == 1) ? 1 : 2);
        }

        int RowWidth() { return Math.Max(400, Math.Min(1040, rows.ClientSize.Width - rows.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth)); }

        void ShowGroup(int group)
        {
            faulty.Primary = group == 0; look.Primary = group == 1; fine.Primary = group == 2;
            faulty.Invalidate(); look.Invalidate(); fine.Invalidate();
            rows.SuspendLayout();
            foreach (Control c in rows.Controls.Cast<Control>().ToList()) c.Dispose();
            fixButtons.Clear();
            var list = results.Where(r => Group(r) == group).OrderBy(r => r.Name).ToList();
            if (list.Count == 0)
                rows.Controls.Add(new Label { Text = "None.", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Font(10f), Margin = new Padding(2, 12, 0, 0) });
            foreach (var r in list) rows.Controls.Add(Row(r));
            rows.ResumeLayout();
        }

        string Title(CostumeCheck.Result r)
        {
            if (r.Name.Length != 6 || r.Name[3] != '_') return r.Name;   // a zip that couldn't be read
            string fighter = r.Name.Substring(0, 3);
            return (Fighters.IsCode(fighter) ? Fighters.Name(fighter) + " · " : "") + "costume " + r.Name.Substring(4);
        }

        Control Row(CostumeCheck.Result r)
        {
            int width = RowWidth();
            var row = new Panel { Width = width, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 10) };
            const int buttons = 170;
            int textWidth = width - buttons - 48, y = 14;
            Func<string, Font, Color, int, Label> add = (text, font, color, left) =>
            {
                int h = TextRenderer.MeasureText(text, font, new Size(textWidth - left, 0), TextFormatFlags.WordBreak).Height;
                var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false, Bounds = new Rectangle(16 + left, y, textWidth - left, h) };
                row.Controls.Add(l);
                y += h + 4;
                return l;
            };
            add(Title(r) + "     " + r.Name + "  ·  " + Path.GetFileName(r.Where.TrimEnd('\\', '/')), Theme.Bold(10.5f), Theme.Text, 0);
            if (r.Findings.Count == 0) add("Nothing wrong found: plays the same as costume 1 online, every color draws.", Theme.Font(9.5f), Theme.GoodText, 0);
            foreach (var f in r.Findings)
            {
                y += 2;
                var tag = new Label { Text = f.Kind, Font = Theme.Bold(8f), ForeColor = Theme.OnAccent, BackColor = f.Kind == "EFFECTS" ? Theme.Muted : Theme.Warning, AutoSize = true, Location = new Point(16, y + 1) };
                row.Controls.Add(tag);
                string plain;
                add(Plain.TryGetValue(f.Kind, out plain) ? plain : f.Kind, Theme.Font(9.5f), Theme.Text, 78);
                add(f.Text + (f.Fixable ? "" : "   (not fixed here)"), Theme.Font(8.5f), Theme.Muted, 78);
            }
            row.Height = Math.Max(y + 10, 104);

            int bx = width - buttons - 16, by = 14;
            if (r.Findings.Any(f => f.Fixable))
            {
                var fix = new FlatButton("Fix a copy...", true) { Bounds = new Rectangle(bx, by, buttons, 36), Enabled = !fixing };
                fix.Click += async (s, e) => await Fix(r);
                row.Controls.Add(fix);
                fixButtons.Add(fix);
                by += 44;
            }
            var folder = new FlatButton("Show in folder") { Bounds = new Rectangle(bx, by, buttons, 36) };
            folder.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", File.Exists(r.Where) ? "/select,\"" + r.Where + "\"" : "\"" + r.Where + "\""); } catch (Exception) { }
            };
            row.Controls.Add(folder);
            return row;
        }

        // the costume's files, read again, fixed into a copy where the player says (never over the costume or into the
        // game's folder), checked again
        async Task Fix(CostumeCheck.Result r)
        {
            if (fixing) return;
            CostumeCheck.Source source;
            try { source = CostumeCheck.Only(CostumeCheck.Open(r.Where), r.Name); }
            catch (Exception ex) { status.ForeColor = Theme.Warning; status.Text = r.Name + " couldn't be read again: " + ex.Message; return; }
            string target;
            using (var dialog = new SaveFileDialog { Filter = "Zip|*.zip", FileName = r.Name + " - fixed.zip", Title = "Where the fixed copy of " + r.Name + " goes",
                                                     InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                target = dialog.FileName;
            }
            string why = CostumeCheck.TargetProblem(game, source, target);
            if (why != null)
            {
                MessageBox.Show(this, "The fixed copy can't go there: " + why, "EX More Stuff", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            fixing = true;
            foreach (FlatButton b in fixButtons) b.Enabled = false;
            status.ForeColor = Theme.Muted;
            status.Text = "Fixing " + r.Name + "...";
            var done = new List<string>();
            List<CostumeCheck.Result> after = null;
            string problem = null;
            try
            {
                await Task.Run(() =>
                {
                    CostumeCheck.Fix(game, source, done, target);
                    after = CostumeCheck.Find(target).SelectMany(s => CostumeCheck.Check(game, s)).ToList();
                });
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { fixing = false; }
            if (IsDisposed) return;
            foreach (FlatButton b in fixButtons) b.Enabled = true;
            if (problem != null) { status.ForeColor = Theme.Warning; status.Text = r.Name + " couldn't be fixed: " + problem; return; }
            var now = after.FirstOrDefault();
            string verdict = now == null ? "" : Group(now) == 0 ? ", but it still needs a fix: " + string.Join(", ", now.Findings.Select(f => f.Kind).Distinct()) : Group(now) == 1 ? ", now worth a look only" : ", now fine";
            status.ForeColor = now != null && Group(now) == 0 ? Theme.Warning : Theme.GoodText;
            status.Text = "Fixed copy saved: " + Path.GetFileName(target) + verdict + ". Put it in with Advanced > Add Mod From File.";
            MessageBox.Show(this, "The fixed copy of " + r.Name + " is in " + target + verdict + ".\n\n" + string.Join("\n", done.Select(d => "• " + d)) +
                            "\n\nThe costume itself is as it was. Put the copy in with Advanced > Add Mod From File.", "EX More Stuff", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void SaveReport()
        {
            using (var dialog = new SaveFileDialog { Filter = "Text|*.txt", FileName = "costume check " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ".txt", Title = "Save the report" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, CostumeCheck.Report(game, results));
                    status.ForeColor = Theme.Muted;
                    status.Text = "Report saved: " + dialog.FileName;
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + dialog.FileName + "\"");
                }
                catch (Exception ex) { status.ForeColor = Theme.Warning; status.Text = "It couldn't be saved: " + ex.Message; }
            }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        // the window stays while a copy is made (the costume rows' Check waits behind it)
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (fixing && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }
}
