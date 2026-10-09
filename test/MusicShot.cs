// Opens EX More Stuff's window from a built exe on the Music page and saves captures: empty, with a song open and its
// loop found, zoomed on the loop end. Nothing is played or installed.
// Usage: MusicShot <EXMoreStuff.exe> <out folder> <song file>
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class MusicShot
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static Form form;
    static Assembly program;
    static string folder;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        program = Assembly.LoadFrom(args[0]);
        folder = args[1];
        form = (Form)Activator.CreateInstance(program.GetType("ExMoreStuff.MainForm"), true);
        object page = null;
        int step = 0;
        var timer = new Timer { Interval = 1500 };
        timer.Tick += (s, e) =>
        {
            switch (step++)
            {
                case 1:
                    Call(form, "ShowTab", Field(form, "musicTab"));
                    page = Field(form, "musicPage");
                    break;
                case 2:
                    Save("music_empty");
                    var song = program.GetType("ExMoreStuff.AudioFile").GetMethod("Load", Any).Invoke(null, new object[] { args[2] });
                    var pcm = (short[])song.GetType().GetField("Pcm").GetValue(song);
                    var found = program.GetType("ExMoreStuff.LoopFinder").GetMethod("Find", Any).Invoke(null, new object[] { pcm });
                    int ls = (int)found.GetType().GetField("Start").GetValue(found), le = (int)found.GetType().GetField("End").GetValue(found);
                    Call(page, "SetSong", song, ls, le);
                    Call(page, "SayFound", found);
                    break;
                case 3:
                    Save("music_song");
                    var wave = Field(page, "wave");
                    Call(wave, "ShowAround", (long)(int)wave.GetType().GetProperty("LoopEnd").GetValue(wave), 0.05);
                    break;
                case 4: Save("music_zoom"); timer.Stop(); form.Close(); break;
            }
        };
        form.Shown += (s, e) => timer.Start();
        Application.Run(form);
    }

    static object Field(object o, string name) { return o.GetType().GetField(name, Any).GetValue(o); }

    static void Call(object o, string name, params object[] a) { o.GetType().GetMethod(name, Any).Invoke(o, a); }

    static void Save(string name)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(form.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
        }
    }
}
