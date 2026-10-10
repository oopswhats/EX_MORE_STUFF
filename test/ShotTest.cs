// Opens EX More Stuff's window from a built exe and saves captures of its pages (roster, one fighter's costumes,
// stages top and scrolled, Advanced and its pages, Community, About, the replace dialog), without clicking. Usage: ShotTest <EXMoreStuff.exe> <out folder> [fighter code]
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
        // as Program.Main does (this opens the window without it): GitHub and GameBanana need TLS 1.2
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Assembly program = Assembly.LoadFrom(args[0]);
        folder = args[1];
        Directory.CreateDirectory(folder);
        string fighter = args.Length > 2 ? args[2] : "RYU";
        // the program's own folder (Mods\, settings.json) is the exe's, not this test's; and only a made-up game (an empty
        // SSFIV.exe): on a real one the window would take the player's packages into this Mods\ when it opens
        program.GetType("ExMoreStuff.AppFolders").GetProperty("Root").SetValue(null, Path.GetDirectoryName(Path.GetFullPath(args[0])));
        string game = (string)program.GetType("ExMoreStuff.Settings").GetProperty("GameFolder").GetValue(null);
        string exe = game == null ? null : Path.Combine(game, "SSFIV.exe");
        if (exe == null || !File.Exists(exe) || new FileInfo(exe).Length > 0)
        {
            Console.Error.WriteLine("settings.json beside the exe must point at a made-up game folder (an empty SSFIV.exe), not " + (game ?? "nothing"));
            Environment.Exit(2);
        }
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
                case 4: Save("stages_scrolled"); Call("ShowTab", Field("advancedTab")); break;
                case 5: Save("advanced"); SetField("openTool", "music"); Call("ShowTab", Field("advancedTab")); break;
                case 6: Save("music"); SetField("openTool", "packages"); Call("ShowTab", Field("advancedTab")); break;
                case 7: Save("packages"); SetField("openTool", "storage"); Call("ShowTab", Field("advancedTab")); break;
                case 8: Save("storage"); SetField("openTool", "freecodes"); Call("ShowTab", Field("advancedTab")); break;
                case 9:
                {
                    Save("freecodes");
                    var page9 = Field("freeCodesPage");
                    ((Control)Inner(page9, "slot")).Text = "87";
                    ((Control)Inner(page9, "stage")).Text = "B01";
                    break;
                }
                case 10: Save("freecodes_check"); SetField("openTool", "oldmods"); Call("ShowTab", Field("advancedTab")); break;
                case 11: Save("oldmods"); SetField("openTool", null); Call("ShowTab", Field("communityTab")); break;
                case 15:
                    Save("community");
                    var community = (ScrollableControl)Inner(Field("communityPage"), "list");
                    community.AutoScrollPosition = new Point(0, 3020);
                    community.Invalidate(true);
                    break;
                case 16:
                    Save("community_scrolled");
                    ((Control)Inner(Field("communityPage"), "search")).Text = "akuma";   // the search waits 0.3 s for more typing
                    break;
                case 17:
                {
                    Save("community_search");
                    var page15 = Field("communityPage");
                    ((Control)Inner(page15, "search")).Text = "";
                    // the Character list, drawn on its own (it drops down in a window of its own), then Blanka picked
                    var pickerType = page15.GetType().Assembly.GetType("ExMoreStuff.CharacterPicker");
                    var picker = (Control)Activator.CreateInstance(pickerType, true);
                    pickerType.GetField("Portrait").SetValue(picker, page15.GetType().GetField("Portrait").GetValue(page15));
                    pickerType.GetField("Selected").SetValue(picker, "RYU");
                    var shot = new Bitmap(picker.Width, picker.Height);
                    picker.DrawToBitmap(shot, new Rectangle(0, 0, picker.Width, picker.Height));
                    shot.Save(Path.Combine(folder, "character_picker.png"), ImageFormat.Png);
                    page15.GetType().GetField("pick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(page15, "BLK");
                    page15.GetType().GetMethod("Fill", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page15, null);
                    break;
                }
                case 18:
                    Save("community_blanka");
                    // from an installed mod's "Browse Mods" link: True Ryu's card, lit up
                    var page = Field("communityPage");
                    page.GetType().GetMethod("Highlight").Invoke(page, new object[] { "gamebanana:251565:" });
                    break;
                case 19:
                {
                    Save("community_highlight");
                    // the Updates list (with Update all), when installed mods have newer versions
                    var page17 = Field("communityPage");
                    page17.GetType().GetField("pick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(page17, "updates");
                    page17.GetType().GetMethod("Fill", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page17, null);
                    break;
                }
                case 20: Save("community_updates"); Call("ShowTab", Field("aboutTab")); break;
                case 21:
                    Save("about");
                    Call("ShowTab", Field("stagesTab"));
                    // The replace dialog is modal: opened from a posted call, the timer keeps ticking inside it.
                    form.BeginInvoke(new Action(() => Call("ChooseReplacement", "DET")));
                    break;
                case 23: Save(Dialog(), "replace_open"); Pick("TRN"); break;
                case 25: Save(Dialog(), "replace_trn"); Pick("VIE"); break;
                case 27: Save(Dialog(), "replace_vie"); Dialog().DialogResult = DialogResult.Cancel; break;
                case 28: timer.Stop(); form.Close(); break;
            }
        };
        form.Shown += (s, e) => timer.Start();
        Application.Run(form);
    }

    static object Field(string name)
    {
        return form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    static object Inner(object owner, string name)
    {
        return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    }

    static void SetField(string name, object value)
    {
        form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
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
