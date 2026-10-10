// Shows the Music page's "Songs saved to disk" pop-up (Toast) over a plain window from a built exe and saves a capture of
// the screen there half a second later. Usage: ToastShot <EXMoreStuff.exe> <capture.png>
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

static class ToastShot
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Assembly program = Assembly.LoadFrom(args[0]);
        var form = new Form { Size = new Size(1000, 800), StartPosition = FormStartPosition.CenterScreen, BackColor = Color.FromArgb(15, 16, 20), Text = "Toast test" };
        var timer = new Timer { Interval = 500 };
        int step = 0;
        timer.Tick += (s, e) =>
        {
            if (step++ == 0)
            {
                program.GetType("ExMoreStuff.Toast").GetMethod("Show", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { form, "Songs saved to disk" });
                return;
            }
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap)) g.CopyFromScreen(form.Location, Point.Empty, form.Size);
                bitmap.Save(args[1], ImageFormat.Png);
            }
            timer.Stop();
            form.Close();
        };
        form.Shown += (s, e) => timer.Start();
        Application.Run(form);
    }
}
