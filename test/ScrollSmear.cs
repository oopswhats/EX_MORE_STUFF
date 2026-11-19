// What a step of a Browse Mods scroll costs to paint (user 2026-11-19: "screen tearing" when scrolling, then Page Up /
// Page Down "not smooth it jumps a lot"): real mod cards (ModCard, 640-pixel pictures, installed and not) in a ScrollList
// in a panel over background art, as the window has them, moved with ScrollList.ScrollTo (drawing held, painted at once);
// the time a step takes with the art drawn whole for each piece painted (as MainForm did) and with only the piece it's for.
// The window sits on screen at 1% opacity (Windows doesn't paint windows off the screen).
// Usage: ScrollSmear (build_test.bat ScrollSmear)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class ScrollSmear
    {
        class ArtForm : Form
        {
            public static bool Clipped;
            readonly Bitmap art;
            public ArtForm()
            {
                art = new Bitmap(1000, 700, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(art))
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, 1000, 700), Color.FromArgb(120, 70, 40), Color.FromArgb(30, 40, 90), 45f))
                    g.FillRectangle(b, 0, 0, 1000, 700);
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (Clipped) Theme.DrawPart(e.Graphics, art, e.ClipRectangle);
                else e.Graphics.DrawImageUnscaled(art, 0, 0);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        [STAThread]
        static void Main(string[] args)
        {
            var pic = new Bitmap(640, 360);
            using (var g = Graphics.FromImage(pic)) using (var b = new LinearGradientBrush(new Rectangle(0, 0, 640, 360), Color.SteelBlue, Color.DarkOrange, 30f)) g.FillRectangle(b, 0, 0, 640, 360);
            foreach (bool clipped in new[] { false, true })
            {
                ArtForm.Clipped = clipped;
                var form = new ArtForm { ClientSize = new Size(1000, 700), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), ShowInTaskbar = false, Opacity = 0.01 };
                var panel = new Clear { Dock = DockStyle.Fill };
                var list = new ScrollList { Dock = DockStyle.Fill };
                form.Controls.Add(panel);
                panel.Controls.Add(list);
                for (int i = 0; i < 60; i++)
                {
                    var card = new ModCard("Mod number " + i, "Somebody", "https://gamebanana.com", (100 + i) + " downloads", "RYU", i % 3 == 0 ? "Installed as RYU 81" : null, false) { Picture = pic, Margin = new Padding(6) };
                    card.FileSize = "12 MB";
                    list.Controls.Add(card);
                }
                form.Shown += (s, e) =>
                {
                    Application.DoEvents();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    int steps = 0;
                    for (int y = 0; y <= 1500; y += 25, steps++) list.ScrollTo(y);
                    Console.WriteLine((clipped ? "art drawn only where painted" : "art drawn whole (as before)") + ": " + (clock.Elapsed.TotalMilliseconds / steps).ToString("0.0") + " ms a step");
                    form.Close();
                };
                Application.Run(form);
            }
        }
    }
}
