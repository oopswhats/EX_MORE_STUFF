// Captures the Music page with Mishima Estate's Level 1 opened from Street Fighter X Tekken, from a built exe whose
// settings.json points at a made-up game (as ShotTest). Only the window itself is captured (PrintWindow), never the
// screen. Usage: SfxtShot <EXMoreStuff.exe> <out folder>
using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

static class SfxtShot
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    static void Main(string[] args)
    {
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Assembly program = Assembly.LoadFrom(args[0]);
        string folder = args[1];
        Directory.CreateDirectory(folder);
        program.GetType("ExMoreStuff.AppFolders").GetProperty("Root").SetValue(null, Path.GetDirectoryName(Path.GetFullPath(args[0])));
        string game = (string)program.GetType("ExMoreStuff.Settings").GetProperty("GameFolder").GetValue(null);
        if (game == null || !File.Exists(Path.Combine(game, "SSFIV.exe")) || new FileInfo(Path.Combine(game, "SSFIV.exe")).Length > 0)
        {
            Console.Error.WriteLine("settings.json beside the exe must point at a made-up game folder (an empty SSFIV.exe)");
            Environment.Exit(2);
        }
        var form = (Form)Activator.CreateInstance(program.GetType("ExMoreStuff.MainForm"), true);
        Func<string, object> field = name => form.GetType().GetField(name, All).GetValue(form);
        object music = field("musicPage");
        int step = 0;
        var timer = new Timer { Interval = 1500 };
        timer.Tick += async (s, e) =>
        {
            switch (step++)
            {
                case 1:
                    form.GetType().GetField("openTool", All).SetValue(form, "music");
                    form.GetType().GetMethod("ShowTab", All).Invoke(form, new[] { field("advancedTab") });
                    break;
                case 2:
                    timer.Stop();
                    // Mishima Estate, Level 1
                    Type sfxt = program.GetType("ExMoreStuff.SfxtMusic");
                    string install = (string)sfxt.GetMethod("Find").Invoke(null, null);
                    var banks = (IList)sfxt.GetMethod("Banks").Invoke(null, new object[] { install });
                    object msr = null;
                    foreach (object b in banks) if ((string)b.GetType().GetField("Code").GetValue(b) == "MSR") msr = b;
                    await (Task)music.GetType().GetMethod("OpenSfxtSong", All).Invoke(music, new[] { msr, (object)0 });
                    await Task.Delay(800);
                    Capture(form, Path.Combine(folder, "sfxt_song.png"));
                    form.Close();
                    break;
            }
        };
        form.Shown += (s, e) => timer.Start();
        Application.Run(form);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    static void Capture(Form form, string path)
    {
        form.Refresh();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(form.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
