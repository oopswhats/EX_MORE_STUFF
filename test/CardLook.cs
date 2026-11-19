// A real Browse Mods card (ModCard: not installed, and installed with Uninstall) over background art, as Windows has it on
// screen (a window at 1% opacity, read back), to check its buttons, heart and name look as before now that they paint
// the card's plain color under them (ISolidBelow; user 2026-11-19: "the scrollbar freezes").
// Usage: CardLook <out .png> (build_test.bat CardLook)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class CardLook
    {
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr to, int x, int y, int w, int h, IntPtr from, int fx, int fy, int op);

        class ArtForm : Form
        {
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(120, 70, 40), Color.FromArgb(30, 40, 90), 45f)) e.Graphics.FillRectangle(b, ClientRectangle);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        [STAThread]
        static void Main(string[] args)
        {
            var form = new ArtForm { ClientSize = new Size(620, 320), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), ShowInTaskbar = false, Opacity = 0.01, FormBorderStyle = FormBorderStyle.None };
            var pic = new Bitmap(220, 124);
            using (var g = Graphics.FromImage(pic)) g.Clear(Color.SteelBlue);
            var a = new ModCard("Seth Skin Venom", "RedWarriorP", "https://gamebanana.com", "201 downloads", "SETH", null, false) { Location = new Point(10, 10), Picture = pic };
            var b = new ModCard("USF4 SF5 Inspired Cammy skin mod", "someone", "https://gamebanana.com", "576 downloads", "CAMMY", "Installed as CAMMY 81", false) { Location = new Point(316, 10), Picture = pic };
            a.FileSize = "9.4 MB"; b.FileSize = "63 MB";
            form.Controls.Add(a); form.Controls.Add(b);
            form.Shown += (s, e) =>
            {
                form.Refresh(); Application.DoEvents();
                var shot = new Bitmap(form.Width, form.Height);
                using (var g = Graphics.FromImage(shot)) { IntPtr to = g.GetHdc(), from = GetWindowDC(form.Handle); BitBlt(to, 0, 0, shot.Width, shot.Height, from, 0, 0, 0x00CC0020); ReleaseDC(form.Handle, from); g.ReleaseHdc(to); }
                shot.Save(args[0], ImageFormat.Png);
                form.Close();
            };
            Application.Run(form);
        }
    }
}
