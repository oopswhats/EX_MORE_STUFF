using System;
using System.Drawing;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Deleting a package of the player's (its card's trash can, Package codes' Delete): out of the game, off EX More Stuff's
    // lists, and EX More Stuff's copy of its zip to the Recycle Bin. One Delete button (user 2026-10-14: "Only remove from
    // EX More Stuff" kept a copy that was "just polluting the user's PC"); the text says the file they added stays.
    class DeleteDialog : Form
    {
        public enum Choice { Cancel, Delete }

        public Choice Picked { get; private set; }

        // `title` names it, `what` says what happens
        public DeleteDialog(string title, string what, string verb = "Delete")   // verb: "Uninstall" from Browse Mods
        {
            Text = verb;
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            const int width = 560;
            string text = what;
            int height = TextRenderer.MeasureText(text, Theme.Font(9.5f), new Size(width - 44, 0), TextFormatFlags.WordBreak).Height + 8;
            ClientSize = new Size(width, 60 + height + 66);

            var heading = new Label { Text = verb + " " + title + "?", AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, Font = Theme.Bold(13f), ForeColor = Theme.Text,
                                      Bounds = new Rectangle(22, 18, width - 44, 30) };
            var about = new Label { Text = text, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, Bounds = new Rectangle(22, 54, width - 44, height) };
            var cancel = new FlatButton("Cancel");
            var delete = new FlatButton(verb, true);
            int top = ClientSize.Height - 54;
            delete.Location = new Point(width - 22 - delete.Width, top);
            cancel.Location = new Point(delete.Left - 10 - cancel.Width, top);
            delete.Click += (s, e) => { Picked = Choice.Delete; DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; };
            Controls.AddRange(new Control[] { heading, about, delete, cancel });
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; return true; }
            // Enter deletes (user 2026-11-17: "let me press enter here to confirm")
            if (keys == Keys.Enter) { Picked = Choice.Delete; DialogResult = DialogResult.OK; return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }
}
