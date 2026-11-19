// The Music tab with a docked stage (user 2026-11-18: "files are docked but i cant change it in the music tab"): its card
// under "Docked" at the top, then a song of yours put in its docked file (MusicLibrary.UseDocked, as Pick does), a
// fighter's theme built onto its stand-in for round 2, heard (DockedNow), and taken out again. On copies only.
// Usage: DockedMusicTab <scratch folder> <game folder> <docked zip> <a song of yours (.csb)> (build_test.bat DockedMusicTab)
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class DockedMusicTab
    {
        [STAThread]
        static void Main(string[] args)
        {
            string scratch = args[0], game = args[1];
            Directory.CreateDirectory(Path.Combine(scratch, "Music"));
            string zip = Path.Combine(scratch, Path.GetFileName(args[2])), song = Path.Combine(scratch, "Music", Path.GetFileName(args[3]));
            File.Copy(args[2], zip, true);
            File.Copy(args[3], song, true);
            AppFolders.Root = scratch;
            AppFolders.NoMigrate = true;
            Docked.Add(zip);
            var d = Docked.Load().First();
            var slot = new MusicSlot { Code = d.Item.Code };
            Console.WriteLine("docked: " + d.Item.Name + " (" + d.Item.Code + "), stand-in " + slot.TemplateCode);
            var form = new Form { Width = 1200, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0), BackColor = Color.FromArgb(24, 24, 28) };
            var tab = new MusicTab { Dock = DockStyle.Fill };
            form.Controls.Add(tab);
            form.Shown += (s, e) =>
            {
                try
                {
                    tab.SetGame(game);
                    Application.DoEvents();
                    Cards(tab, "before");
                    Save(tab, Path.Combine(scratch, "before.png"));
                    var songs = MusicLibrary.Songs(game);
                    var yours = songs.First(x => x.Key == "yours:" + Path.GetFileName(song));
                    var theme = songs.First(x => x.Key == "fighter:RYU");
                    MusicLibrary.UseDocked(game, d.Package, slot, yours);
                    MusicLibrary.UseDocked(game, d.Package, slot.InRound(2), theme);
                    foreach (int r in new[] { 1, 2, 3 })
                    {
                        var put = DockedMusic.Song(d.Package, slot.InRound(r).File);
                        Console.WriteLine("round " + r + " (" + slot.InRound(r).File + "): " + (put == null ? "none" : put.Song + " [" + string.Join(" | ", put.Layers) + "]"));
                    }
                    int loop;
                    short[] heard = MusicLibrary.Decode(MusicLibrary.DockedNow(game, d.Package, slot, "now"), out loop);
                    Console.WriteLine("heard: " + heard.Length / 2 / Song.Rate + " s, loops at " + loop / Song.Rate + " s");
                    tab.SetGame(game);
                    Application.DoEvents();
                    Cards(tab, "after");
                    Save(tab, Path.Combine(scratch, "after.png"));
                    foreach (int r in new[] { 1, 2, 3 }) MusicLibrary.TakeOutDocked(d.Package, slot.InRound(r));
                    Console.WriteLine("taken out: " + string.Join(", ", new[] { 1, 2, 3 }.Select(r => slot.InRound(r).File + " " + (DockedMusic.Has(d.Package, slot.InRound(r).File) ? "still there" : "gone"))));
                    using (var z = System.IO.Compression.ZipFile.OpenRead(d.Package)) Console.WriteLine("zip now: " + string.Join(", ", z.Entries.Select(x => x.Name)));
                }
                catch (Exception ex) { Console.WriteLine("FAILED: " + ex); }
                form.Close();
            };
            Application.Run(form);
        }

        static void Cards(MusicTab tab, string when)
        {
            var list = (Control)typeof(MusicTab).GetField("list", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tab);
            Console.WriteLine(when + ":");
            int n = 0;
            foreach (Control c in list.Controls)
            {
                if (c is Heading) Console.WriteLine("  == " + c.Text);
                var card = c as SlotCard;
                if (card != null && n++ < 3) Console.WriteLine("  " + card.Caption + ": " + card.Song + (card.Changed ? " (changed)" : ""));
            }
        }

        static void Save(Control c, string path)
        {
            using (var b = new Bitmap(c.ClientSize.Width, c.ClientSize.Height))
            {
                c.DrawToBitmap(b, new Rectangle(Point.Empty, c.ClientSize));
                b.Save(path, ImageFormat.Png);
            }
        }
    }
}
