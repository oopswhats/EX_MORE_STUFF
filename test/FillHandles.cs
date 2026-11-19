// The pages' Fill (MainForm.Fill) built again and again, as each reload does (user 2026-11-18: a crash at 10,000 window
// handles): the window handles held stay level, the old cards freed. Usage: FillHandles (build_test.bat FillHandles)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class FillHandles
    {
        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process, int flags);
        static int Handles() { return GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 1); }

        [STAThread]
        static void Main(string[] args)
        {
            var fill = typeof(MainForm).GetMethod("Fill", BindingFlags.Static | BindingFlags.NonPublic);
            var form = new Form { Width = 1200, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            form.Controls.Add(list);
            var tips = new ToolTip();
            form.Shown += (s, e) =>
            {
                int start = Handles();
                for (int round = 1; round <= 30; round++)
                {
                    var cards = new List<Control> { new Heading("Custom stages", 1100) };
                    for (int i = 0; i < 40; i++)
                    {
                        var card = new ItemCard { Title = "Stage " + i, Margin = new Padding(6) };
                        card.ShowCreator("someone", "https://gamebanana.com", false, new GameBanana.Support { Kofi = "https://ko-fi.com/someone" }, tips);
                        cards.Add(card);
                    }
                    if (args.Length > 0) { list.Controls.Clear(); foreach (Control c in cards) list.Controls.Add(c); }   // "old": as before the fix
                    else fill.Invoke(null, new object[] { list, cards });
                    Application.DoEvents();   // the old ones freed just after
                    if (round == 1 || round % 10 == 0) Console.WriteLine("after " + round + " builds: " + (Handles() - start) + " window handles more than at start");
                }
                form.Close();
            };
            Application.Run(form);
        }
    }
}
