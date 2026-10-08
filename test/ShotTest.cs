// Opens EX More Stuff's window from a built exe and saves captures of its pages (roster, one fighter's costumes,
// stages top and scrolled), without clicking. Usage: ShotTest <EXMoreStuff.exe> <out folder> [fighter code]
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class ShotTest
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    static Form form;
    static string folder;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Assembly program = Assembly.LoadFrom(args[0]);
        folder = args[1];
        string fighter = args.Length > 2 ? args[2] : "RYU";
        form = (Form)Activator.CreateInstance(program.GetType("ExMoreStuff.MainForm"), true);
        int step = 0;
        var timer = new Timer { Interval = 1500 };
        timer.Tick += (s, e) =>
        {
            switch (step++)
            {
                case 1: Save("roster"); Call("ShowCostumes", fighter); break;
                case 2: Save("costumes"); Call("ShowRoster"); Call("ShowTab", Field("stagesTab")); break;
                case 3:
                    Save("stages");
                    var list = (ScrollableControl)Field("stagesPage");
                    list.AutoScrollPosition = new Point(0, 600);
                    list.Invalidate(true);
                    break;
                case 4: Save("stages_scrolled"); Call("ShowTab", Field("aboutTab")); break;
                case 5:
                    Save("about");
                    Call("ShowTab", Field("stagesTab"));
                    // The replace dialog is modal: opened from a posted call, the timer keeps ticking inside it.
                    form.BeginInvoke(new Action(() => Call("ChooseReplacement", "DET")));
                    break;
                case 7: Save(Dialog(), "replace_open"); Pick("TRN"); break;
                case 9: Save(Dialog(), "replace_trn"); Pick("VIE"); break;
                case 11: Save(Dialog(), "replace_vie"); Dialog().DialogResult = DialogResult.Cancel; break;
                case 12: timer.Stop(); form.Close(); break;
            }
        };
        form.Shown += (s, e) => timer.Start();
        Application.Run(form);
    }

    static object Field(string name)
    {
        return form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    static void Call(string name, params object[] args)
    {
        form.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
    }

    static Form Dialog()
    {
        foreach (Form open in Application.OpenForms) if (open != form) return open;
        return form;
    }

    static void Pick(string code)
    {
        Form dialog = Dialog();
        dialog.GetType().GetMethod("Pick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { code });
    }

    static void Save(string name) { Save(form, name); }

    static void Save(Form window, string name)
    {
        window.Refresh();
        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(window.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
        }
    }
}
