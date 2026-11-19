// The full-size picture view (FullPicture) over a filled list, captured: loading, with a picture, zoomed in at a point, zoomed
// back out, then its page's pictures given (SetAll): next, and previous round to the last; then a real mod's picture list
// read from GameBanana (GameBanana.PictureList).
// Usage: FullPictureShot <out folder> <mod number> <picture> <picture> ... (build_test.bat FullPictureShot)
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    class Wheelable : FullPicture
    {
        public Wheelable(string name, string first, Func<string, Task<Image>> load) : base(name, first, load) { }
        public void Wheel(Point at, int delta) { OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, at.X, at.Y, delta)); }
        public void Key(Keys key) { OnKeyDown(new KeyEventArgs(key)); }
    }

    static class FullPictureShot
    {
        [STAThread]
        static void Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;   // as Program does
            string outFolder = args[0];
            long modId = long.Parse(args[1]);
            var files = args.Skip(2).ToList();
            Directory.CreateDirectory(outFolder);
            var form = new Form { Width = 1100, Height = 800, BackColor = Color.FromArgb(24, 24, 28), StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
            var host = new Panel { Dock = DockStyle.Fill };
            var list = new ScrollList { Dock = DockStyle.Fill };
            for (int i = 0; i < 9; i++) list.Controls.Add(new Label { Text = "card " + i, Size = new Size(286, 292), BackColor = Color.SteelBlue, Margin = new Padding(6) });
            host.Controls.Add(list);
            form.Controls.Add(host);
            var slow = new TaskCompletionSource<Image>();   // the first picture held back, to see "Loading..."
            Func<string, Task<Image>> load = path => path == files[0] && !slow.Task.IsCompleted ? slow.Task : Task.FromResult(Pictures.Read(File.ReadAllBytes(path)));
            form.Shown += async (s, e) =>
            {
                var view = new Wheelable("Test Mod", files[0], load);
                host.Controls.Add(view);
                view.BringToFront();
                Application.DoEvents();
                Save(view, Path.Combine(outFolder, "loading.png"));
                slow.SetResult(Pictures.Read(File.ReadAllBytes(files[0])));
                await Task.Delay(50);
                Application.DoEvents();
                Save(view, Path.Combine(outFolder, "shown.png"));
                for (int k = 0; k < 4; k++) view.Wheel(new Point(250, 300), 120);
                Save(view, Path.Combine(outFolder, "zoomed.png"));
                for (int k = 0; k < 20; k++) view.Wheel(new Point(250, 300), -120);
                view.SetAll(files);
                Application.DoEvents();
                Console.WriteLine("given " + files.Count + ": " + view.Count + " pictures, showing " + (view.Shown + 1));
                Save(view, Path.Combine(outFolder, "several.png"));
                view.Key(Keys.Right);
                await Task.Delay(50);
                Application.DoEvents();
                Console.WriteLine("Right: showing " + (view.Shown + 1));
                Save(view, Path.Combine(outFolder, "next.png"));
                view.Key(Keys.Left); view.Key(Keys.Left);
                await Task.Delay(50);
                Application.DoEvents();
                Console.WriteLine("Left twice: showing " + (view.Shown + 1) + " (round to the last)");
                Save(view, Path.Combine(outFolder, "last.png"));
                bool closed = false;
                view.Closed += (a, b) => closed = true;
                view.Close();
                Application.DoEvents();
                Console.WriteLine("closed: " + closed + ", still there: " + host.Controls.Contains(view));
                var pictures = await GameBanana.PictureList(new GameBanana.Mod { Id = modId });
                Console.WriteLine("mod " + modId + "'s page: " + (pictures == null ? "not read" : pictures.Count + " pictures"));
                if (pictures != null) foreach (string p in pictures) Console.WriteLine("  " + p);
                form.Close();
            };
            Application.Run(form);
        }

        // DrawToBitmap draws stacked children in the wrong order, so the view is captured on its own
        static void Save(Control form, string path)
        {
            using (var b = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(b, new Rectangle(Point.Empty, form.ClientSize));
                b.Save(path, ImageFormat.Png);
            }
        }
    }
}
