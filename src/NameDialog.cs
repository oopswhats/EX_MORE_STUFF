using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Naming an installed costume or stage as EMBER's menus show it.
    class NameDialog : Form
    {
        readonly TextBox box;

        public string Value { get { return box.Text; } }

        public NameDialog(string what, string current)
        {
            Text = "Rename";
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            ClientSize = new Size(440, 214);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var title = new Label { Text = "Rename " + what, AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, Font = Theme.Bold(13f), ForeColor = Theme.Text, Bounds = new Rectangle(22, 18, 396, 30) };
            var about = new Label
            {
                Text = "The name Ember's menus show once you apply the changes and start the game. Only you see it. " +
                       "English letters, numbers, spaces and _ - . only. Leave it empty for \"Custom\" and the number.",
                AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), ForeColor = Theme.Muted, Bounds = new Rectangle(22, 52, 396, 54),
            };
            box = new TextBox { Text = Installer.CleanName(current), MaxLength = Installer.NameBytes, Bounds = new Rectangle(24, 114, 392, 26), BackColor = Theme.Well, ForeColor = Theme.Text,
                                BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10.5f) };
            var save = new FlatButton("Save", true);
            var cancel = new FlatButton("Cancel");
            save.Location = new Point(ClientSize.Width - save.Width - 24, 160);
            cancel.Location = new Point(save.Left - cancel.Width - 10, 160);
            save.Click += (s, e) => { DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.AddRange(new Control[] { title, about, box, cancel, save });
            Shown += (s, e) => { box.Focus(); box.SelectAll(); };
            // Other characters can't be typed, and are taken out of pasted text.
            box.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !Installer.NameCharacter(e.KeyChar)) e.Handled = true; };
            box.TextChanged += (s, e) =>
            {
                if (box.Text.All(Installer.NameCharacter)) return;
                int caret = box.SelectionStart - box.Text.Take(box.SelectionStart).Count(c => !Installer.NameCharacter(c));
                box.Text = new string(box.Text.Where(Installer.NameCharacter).ToArray());
                box.SelectionStart = Math.Max(0, Math.Min(caret, box.Text.Length));
            };
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            if (keys == Keys.Enter) { DialogResult = DialogResult.OK; return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }
}
