// MANAGE MUSIC captured (Newest, then A-Z) from the player's own songs, read only: nothing is installed or removed.
// Usage: ManageMusicShot <data folder> <game folder> <out folder> (build_test.bat ManageMusicShot)
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class ManageMusicShot
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppFolders.Root = args[0];
            AppFolders.NoMigrate = true;
            Directory.CreateDirectory(args[2]);
            var tab = new MusicTab { Dock = DockStyle.Fill };
            var form = new Form { Width = 1100, Height = 900, BackColor = Color.FromArgb(24, 24, 28), StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
            form.Controls.Add(tab);
            typeof(MusicTab).GetField("view", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 2);
            form.Shown += (s, e) =>
            {
                tab.SetGame(args[1]);
                Application.DoEvents();
                Save(form, Path.Combine(args[2], "newest.png"));
                typeof(MusicTab).GetMethod("SortBy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tab, new object[] { true });
                Application.DoEvents();
                Save(form, Path.Combine(args[2], "a-z.png"));
                form.Close();
            };
            Application.Run(form);
        }

        static void Save(Form form, string path)
        {
            using (var b = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(b, new Rectangle(Point.Empty, form.ClientSize));
                b.Save(path, ImageFormat.Png);
            }
        }
    }
}
