// Advanced > Music, a docked stage's row, "Open what the game plays here" (user 2026-11-18: a song put in on the Music tab,
// it opened the original stage's music): the docked file's music, not the stand-in's. On a copy of the docked zip.
// Usage: DockedOpenGame <scratch folder> <game folder> <docked zip with music> (build_test.bat DockedOpenGame)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class DockedOpenGame
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        static void Main(string[] args)
        {
            string scratch = args[0], game = args[1];
            Directory.CreateDirectory(scratch);
            string zip = Path.Combine(scratch, Path.GetFileName(args[2]));
            File.Copy(args[2], zip, true);
            AppFolders.Root = scratch;
            AppFolders.NoMigrate = true;
            Docked.Add(zip);
            var form = new Form { Width = 1300, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
            var panel = new MusicPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Shown += async (s, e) =>
            {
                try
                {
                    panel.SetGame(game);
                    var rows = (System.Collections.IList)typeof(MusicPanel).GetField("rows", Private).GetValue(panel);
                    object row = rows.Cast<object>().First(r => r.GetType().GetField("Docked").GetValue(r) != null);
                    typeof(MusicPanel).GetMethod("Select", Private, null, new[] { row.GetType() }, null).Invoke(panel, new[] { row });
                    await (Task)typeof(MusicPanel).GetMethod("OpenGameTheme", Private).Invoke(panel, null);
                    var song = (Song)typeof(MusicPanel).GetField("song", Private).GetValue(panel);
                    var status = (Label)typeof(MusicPanel).GetField("status", Private).GetValue(panel);
                    var slot = new MusicSlot { Code = Docked.Load().First().Item.Code };
                    int ls;
                    int inZip = MusicBank.Decode(DockedMusic.Bank(zip, slot.File), 0, "zip", out ls).Frames;
                    int standIn = MusicBank.Decode(File.ReadAllBytes(MusicBank.GameFile(game, slot.TemplateFile)), 0, "stand-in", out ls).Frames;
                    Console.WriteLine("opened: " + (song == null ? "nothing" : song.Name + ", " + song.Frames / Song.Rate + " s"));
                    Console.WriteLine("said: " + status.Text);
                    Console.WriteLine("docked file's music " + inZip / Song.Rate + " s, stand-in " + slot.TemplateCode + "'s " + standIn / Song.Rate + " s: " +
                                      (song != null && song.Frames == inZip ? "the docked file's" : song != null && song.Frames == standIn ? "THE STAND-IN'S" : "neither"));
                }
                catch (Exception ex) { Console.WriteLine("FAILED: " + ex); }
                form.Close();
            };
            Application.Run(form);
        }
    }
}
