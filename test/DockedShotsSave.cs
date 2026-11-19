// A docked stage's card with its preview pictures and Save pictures beside them (user 2026-11-18: "save the preview images
// next to the zip ... so i can use the images on gamebanana"), at the window's default width: captured, and where the button
// sits. Usage: DockedShotsSave <out .png> (build_test.bat DockedShotsSave)
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class DockedShotsSave
    {
        [STAThread]
        static void Main(string[] args)
        {
            var form = new Form { Width = 1000, Height = 400, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0), BackColor = Color.FromArgb(24, 24, 28) };
            int width = 1000 - 40 - SystemInformation.VerticalScrollBarWidth - 16;   // about as Advanced lays it out
            var row = new DockedRow { Size = new Size(width - 16, DockedRow.RowHeight + DockedRow.ShotsHeight), Location = new Point(8, 8), Title = "Starlit Café", Kind = "Stage", FilePath = @"X:\packages\Starlit Café.zip", CanPreview = true, CanGetCode = true };
            row.ShowFile.Text = row.FilePath;
            form.Controls.Add(row);
            form.Shown += (s, e) =>
            {
                var shots = new Image[3];
                for (int i = 0; i < 3; i++) { var b = new Bitmap(352, 198); using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(60 + 50 * i, 90, 140)); shots[i] = b; }
                row.SetShots(shots);
                Application.DoEvents();
                Console.WriteLine("card " + row.Width + " wide; Save pictures at " + row.SavePictures.Bounds + ", visible " + row.SavePictures.Visible + ", fits " + (row.SavePictures.Right <= row.Width - 8));
                using (var b = new Bitmap(row.Width, row.Height)) { row.DrawToBitmap(b, new Rectangle(Point.Empty, row.Size)); b.Save(args[0], ImageFormat.Png); }
                form.Close();
            };
            Application.Run(form);
        }
    }
}
