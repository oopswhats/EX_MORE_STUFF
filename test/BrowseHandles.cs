// Browse Mods scrolled to the end again and again (cards added 9 at a time), then made again as an install does, counting
// the window (USER) and GDI handles the program holds (user 2026-11-18: EX More Stuff crashed, "Error creating window
// handle" in Windows' log). Reads GameBanana's list; installs nothing.
// Usage: BrowseHandles <data folder (a scratch copy)> <game folder> (build_test.bat BrowseHandles)
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class BrowseHandles
    {
        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process, int flags);

        static string Handles()
        {
            IntPtr me = System.Diagnostics.Process.GetCurrentProcess().Handle;
            return "USER " + GetGuiResources(me, 1) + ", GDI " + GetGuiResources(me, 0);
        }

        [STAThread]
        static void Main(string[] args)
        {
            AppFolders.Root = args[0];
            AppFolders.NoMigrate = true;
            var panel = new CommunityPanel { Dock = DockStyle.Fill };
            var form = new Form { Width = 1000, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
            form.Controls.Add(panel);
            var list = (ScrollList)typeof(CommunityPanel).GetField("list", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            var loaded = typeof(CommunityPanel).GetField("loaded", BindingFlags.Instance | BindingFlags.NonPublic);
            var fill = typeof(CommunityPanel).GetMethod("Fill", BindingFlags.Instance | BindingFlags.NonPublic);
            form.Shown += async (s, e) =>
            {
                panel.SetGame(args[1]);
                panel.CheckForUpdates();
                for (int i = 0; i < 600 && !(bool)loaded.GetValue(panel); i++) await System.Threading.Tasks.Task.Delay(100);
                fill.Invoke(panel, null);
                Console.WriteLine("loaded: " + list.Controls.OfType<Control>().Count(c => c.GetType().Name == "ModCard") + " cards, " + Handles());
                for (int round = 0; round < 80; round++)
                {
                    list.ScrollTo(int.MaxValue);
                    Application.DoEvents();
                    if (round % 10 == 9) Console.WriteLine("scrolled " + (round + 1) + ": " + list.Controls.OfType<Control>().Count(c => c.GetType().Name == "ModCard") + " cards, " + Handles());
                }
                for (int k = 0; k < 5; k++)
                {
                    fill.Invoke(panel, null);   // as after an install
                    Application.DoEvents();
                    Console.WriteLine("made again " + (k + 1) + ": " + list.Controls.OfType<Control>().Count(c => c.GetType().Name == "ModCard") + " cards, " + Handles());
                }
                form.Close();
            };
            Application.Run(form);
        }
    }
}
