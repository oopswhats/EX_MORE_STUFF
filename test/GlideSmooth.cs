// Page Down on Browse Mods glides as pictures of the cards (ScrollList's view; user 2026-11-19: "not smooth it jumps a
// lot", for a video): real mod cards in a ScrollList over art, Page Down pressed; how long before it moves, how many
// different places it shows on the way and how long a step takes, and whether what's on screen half way and at the
// end is what the list itself paints there. The window sits on screen at 1% opacity (Windows doesn't paint windows off
// the screen), read back from Windows.
// Usage: GlideSmooth <out folder> (build_test.bat GlideSmooth)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class GlideSmooth
    {
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr to, int x, int y, int w, int h, IntPtr from, int fx, int fy, int op);

        class ArtForm : Form
        {
            readonly Bitmap art;
            public ArtForm()
            {
                art = new Bitmap(1000, 700, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(art))
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, 1000, 700), Color.FromArgb(150, 90, 40), Color.FromArgb(30, 60, 140), 45f))
                    g.FillRectangle(b, 0, 0, 1000, 700);
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                RectangleF clip = e.Graphics.ClipBounds;
                Theme.DrawPart(e.Graphics, art, Rectangle.FromLTRB((int)Math.Floor(clip.Left), (int)Math.Floor(clip.Top), (int)Math.Ceiling(clip.Right), (int)Math.Ceiling(clip.Bottom)));
            }
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        static int ShownY(ScrollList list) { return (int)typeof(ScrollList).GetProperty("ShownY", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(list); }
        static bool Gliding(ScrollList list) { return ((Timer)typeof(ScrollList).GetField("glide", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(list)).Enabled; }

        [STAThread]
        static void Main(string[] args)
        {
            var pic = new Bitmap(640, 360);
            using (var g = Graphics.FromImage(pic)) using (var b = new LinearGradientBrush(new Rectangle(0, 0, 640, 360), Color.SteelBlue, Color.DarkOrange, 30f)) g.FillRectangle(b, 0, 0, 640, 360);
            var form = new ArtForm { ClientSize = new Size(1000, 700), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), ShowInTaskbar = false, Opacity = 0.01 };
            var panel = new Clear { Dock = DockStyle.Fill };
            var list = new ScrollList { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            panel.Controls.Add(list);
            // the top of Browse Mods: a note, and tools with the search box (a Windows box) and buttons
            var note = ShadowLabel.Note("Mods from GameBanana for Ultra Street Fighter IV.", 900);
            list.Controls.Add(note); list.SetFlowBreak(note, true);
            var tools = new Clear { Size = new Size(900, 56) };
            tools.Controls.Add(new TextBox { Location = new Point(10, 14), Width = 200, Text = "search mods", BackColor = Color.FromArgb(20, 20, 24), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle });
            tools.Controls.Add(new FlatButton("Costumes") { Location = new Point(230, 10) });
            tools.Controls.Add(new FlatButton("Stages", true) { Location = new Point(360, 10) });
            list.Controls.Add(tools); list.SetFlowBreak(tools, true);
            for (int i = 0; i < 60; i++)
            {
                var card = new ModCard("Mod number " + i, "Somebody", "https://gamebanana.com", (100 + i) + " downloads", "RYU", i % 3 == 0 ? "Installed as RYU 81" : null, false) { Picture = pic, Margin = new Padding(6) };
                card.FileSize = "12 MB";
                list.Controls.Add(card);
            }
            form.Shown += (s, e) =>
            {
                Application.DoEvents();
                {   // the view's first picture, at the top (the note and search box in it), against the list
                    var f = BindingFlags.NonPublic | BindingFlags.Instance;
                    Bitmap real = Shot(form);
                    typeof(ScrollList).GetMethod("StartView", f).Invoke(list, new object[] { 0, 700 });
                    Bitmap shown = Shot(form);
                    typeof(ScrollList).GetMethod("EndView", f).Invoke(list, null);
                    Console.WriteLine("first picture at the top, differs from the list itself: " + Differ(shown, real));
                    shown.Save(System.IO.Path.Combine(args[0], "top view.png"), ImageFormat.Png);
                    real.Save(System.IO.Path.Combine(args[0], "top list.png"), ImageFormat.Png);
                }
                foreach (bool warmed in new[] { false, true })
                {
                    if (warmed) { var wait = System.Diagnostics.Stopwatch.StartNew(); while (wait.ElapsedMilliseconds < 2500) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }
                    var shotsNow = (System.Collections.IDictionary)typeof(ScrollList).GetField("shots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(list);
                    Console.WriteLine("pictures ready: " + shotsNow.Count);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var steps = new System.Collections.Generic.List<double>(); int lastY = -1;
                    int from = ShownY(list);
                    list.Page(1);
                    double start = clock.Elapsed.TotalMilliseconds;
                    var places = new HashSet<int>();
                    Bitmap half = null; int halfY = -1;
                    int to = -1;
                    while (Gliding(list))
                    {
                        Application.DoEvents();
                        int y = ShownY(list);
                        places.Add(y);
                        if (y != lastY) { steps.Add(clock.Elapsed.TotalMilliseconds); lastY = y; }
                        if (half == null && y > from + 250) { half = Shot(form); halfY = y; }

                    }
                    double took = clock.Elapsed.TotalMilliseconds;
                    var gaps = new System.Collections.Generic.List<double>(); for (int k = 1; k < steps.Count; k++) gaps.Add(steps[k] - steps[k - 1]); gaps.Sort();
                    if (gaps.Count > 0) Console.WriteLine("   between steps: least " + gaps[0].ToString("0.0") + " ms, middle " + gaps[gaps.Count / 2].ToString("0.0") + ", most " + gaps[gaps.Count - 1].ToString("0.0") + "; first step at " + steps[0].ToString("0") + " ms");
                    to = ShownY(list);
                    Application.DoEvents();
                    Bitmap end = Shot(form);
                    Console.WriteLine((warmed ? "after a rest" : "at once") + ": " + start.ToString("0") + " ms before it moves, " + places.Count + " places shown in " + took.ToString("0") + " ms (" + (took / Math.Max(1, places.Count)).ToString("0.0") + " ms a step), " + from + " to " + to);
                    // what the list itself paints there
                    list.ScrollTo(to); Application.DoEvents();
                    Console.WriteLine("   at the end, differs from the list itself: " + Differ(end, Shot(form)));
                    if (half != null)
                    {
                        list.ScrollTo(halfY); Application.DoEvents();
                        Bitmap real = Shot(form);
                        Console.WriteLine("   half way (" + halfY + "), differs from the list itself: " + Differ(half, real));
                        half.Save(System.IO.Path.Combine(args[0], (warmed ? "warm" : "cold") + " half view.png"), ImageFormat.Png);
                        real.Save(System.IO.Path.Combine(args[0], (warmed ? "warm" : "cold") + " half list.png"), ImageFormat.Png);
                        list.ScrollTo(to); Application.DoEvents();
                    }
                }
                // a step of the view by itself, timed
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                // does making a card's picture mark anything in it to be painted again (which drops the picture)?
                int marked = 0; var who = new System.Collections.Generic.List<string>();
                Action<Control> hook = null;
                hook = c => { c.Invalidated += (s2, e2) => { marked++; if (who.Count < 6) who.Add(s2.GetType().Name); }; foreach (Control k in c.Controls) hook(k); };
                var first = list.Controls[0];
                hook(first);
                typeof(ScrollList).GetMethod("Shoot", flags).Invoke(list, new object[] { first });
                Console.WriteLine("making a card's picture marked " + marked + " to paint again: " + string.Join(", ", who));
                var shotsField = (System.Collections.IDictionary)typeof(ScrollList).GetField("shots", flags).GetValue(list);
                Console.WriteLine("pictures kept now: " + shotsField.Count);
                int at = ShownY(list);
                if ((bool)typeof(ScrollList).GetMethod("StartView", flags).Invoke(list, new object[] { at - 700, at + 1400 }))
                {
                    var showAt = typeof(ScrollList).GetMethod("ShowAt", flags);
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    for (int k = 1; k <= 40; k++) showAt.Invoke(list, new object[] { at - k * 10 });
                    Console.WriteLine("a step of the view: " + (timer.Elapsed.TotalMilliseconds / 40).ToString("0.0") + " ms");
                    typeof(ScrollList).GetMethod("EndView", flags).Invoke(list, null);
                }
                form.Close();
            };
            Application.Run(form);
        }

        static string Differ(Bitmap a, Bitmap b)
        {
            long off = 0, n = 0; int most = 0;
            for (int y = 0; y < a.Height; y += 2) for (int x = 0; x < a.Width; x += 2)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                int d = Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)));
                if (d > 24) off++;
                most = Math.Max(most, d);
                n++;
            }
            return (off * 100.0 / n).ToString("0.00") + "% of pixels (largest difference " + most + ")";
        }

        static Bitmap Shot(Form form)
        {
            var b = new Bitmap(form.Width, form.Height);
            using (var g = Graphics.FromImage(b)) { IntPtr to = g.GetHdc(), from = GetWindowDC(form.Handle); BitBlt(to, 0, 0, b.Width, b.Height, from, 0, 0, 0x00CC0020); ReleaseDC(form.Handle, from); g.ReleaseHdc(to); }
            return b;
        }
    }
}
