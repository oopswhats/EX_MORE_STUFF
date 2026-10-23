using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;

namespace ExMoreStuff
{
    // EX More Stuff's own steps without its window (user 2026-10-18, for the costume agent preparing legacy skins for
    // GameBanana: it can't click a window). Local builds only for now.
    //   --dock "<mod file>"                  Add Mod From File, docked for edits (7z, rar, zip; a zip given is copied first,
    //                                        never converted in place)
    //   --free-codes <FIGHTER>               the shared slots nobody holds (Advanced > Free codes)
    //   --set-code "<docked zip>" <slot>     the docked package moved to another slot (or stage code), its zip rewritten
    //   --color-pictures "<docked zip>" [1,2,5-10]   color pictures into the zip (drawn by EX More Stuff; the game runs
    //                                        only for a costume that can't be drawn; refused while it's open)
    //   --check-costumes [<folder or zip> ...] [--report "<file.txt>"]   the costume check (CostumeCheck): what breaks in
    //                                        the game or desyncs online; default the installed costumes, as the window's
    //                                        Check installed costumes (patch_ae2_tu3 but the game's own 01-07, and the
    //                                        docked ones). The report, a text file to look into, with --report
    //   --fix-costume "<folder or zip>" ["<copy>"]   a fixed copy at <copy> (a folder, or a zip when it ends .zip), else
    //                                        beside it ("... - fixed"; refused when that's in the game's folder): locators
    //                                        set to the stand-in's, materials put right, missing parts from the game;
    //                                        then checked again. Never over the costume itself or into the game's folder
    // and before the command:
    //   --data "<folder>"    EX More Stuff's own files (docked list, GameBanana list, built zips) there, not beside the
    //                        program, so these runs don't show in the player's EX More Stuff
    //   --game "<folder>"    the game folder (default: the one EX More Stuff's settings name)
    //   --result "<file>"    the answer written there too (a window program's printing may not reach a command line)
    // The answer: key=value lines. Exit code 0 when it worked; 1 with "error=<why>" (also on stderr) when it didn't; 2 for
    // a command line it doesn't understand.
    static class Cli
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int process);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);

        sealed class Usage : Exception { public Usage(string why) : base(why) { } }

        public static int Run(string[] args)
        {
            if (GetStdHandle(-11) == IntPtr.Zero) AttachConsole(-1);   // started from a console without redirection: print there
            var answer = new List<string>();
            string resultFile = null;
            int code;
            try
            {
                string data = null, game = null, command = null, reportFile = null;
                var rest = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    Func<string> next = () => { if (i + 1 >= args.Length) throw new Usage(a + " needs a value"); return args[++i]; };
                    if (a == "--data") data = next();
                    else if (a == "--game") game = next();
                    else if (a == "--result") resultFile = next();
                    else if (a == "--report") reportFile = next();
                    else if (command == null && a.StartsWith("--")) command = a;
                    else rest.Add(a);
                }
                if (command == null) throw new Usage("no command (--dock, --free-codes, --set-code, --color-pictures, --check-costumes, --fix-costume)");
                game = game ?? Settings.GameFolder;   // the player's settings, read before --data moves EX More Stuff's files
                if (data != null)
                {
                    Directory.CreateDirectory(data);
                    AppFolders.Root = Path.GetFullPath(data);
                    AppFolders.NoMigrate = true;   // nothing copied in from an older EX More Stuff's folder
                }
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Action<string> say = text => Console.Error.WriteLine(text);   // progress: stderr, the answer stays clean
                switch (command)
                {
                    case "--dock": Need(rest, 1, "--dock \"<mod file>\""); DockFile(NeedGame(game), rest[0], say, answer); break;
                    case "--free-codes": Need(rest, 1, "--free-codes <FIGHTER>"); FreeCodes(rest[0], answer); break;
                    case "--set-code": Need(rest, 2, "--set-code \"<docked zip>\" <slot or stage code>"); SetCode(rest[0], rest[1], answer); break;
                    case "--color-pictures":
                        if (rest.Count < 1) throw new Usage("--color-pictures \"<docked zip>\" [colors, e.g. 1,2,5-10]");
                        ColorPictures(NeedGame(game), rest[0], rest.Count > 1 ? rest[1] : null, say, answer);
                        break;
                    case "--check-costumes": CheckCostumes(NeedGame(game), rest, reportFile, answer); break;
                    case "--fix-costume":
                        Need(rest, 1, "--fix-costume \"<folder or zip>\" [\"<fixed copy's folder or .zip>\"]");
                        FixCostume(NeedGame(game), rest[0], rest.Count > 1 ? rest[1] : null, reportFile, answer);
                        break;
                    default: throw new Usage("unknown command " + command);
                }
                answer.Insert(0, "ok");
                code = 0;
            }
            catch (Usage ex) { answer.Clear(); answer.Add("error=" + ex.Message); code = 2; }
            catch (Exception ex) { answer.Add("error=" + ex.Message); code = 1; }
            foreach (string line in answer) Console.WriteLine(line);
            if (code != 0) Console.Error.WriteLine(answer.Last().Substring("error=".Length));
            if (resultFile != null) try { File.WriteAllLines(resultFile, answer); } catch (Exception) { }
            Console.Out.Flush();
            return code;
        }

        static void Need(List<string> rest, int count, string usage) { if (rest.Count < count) throw new Usage("usage: " + usage); }

        static string NeedGame(string game)
        {
            if (!Game.IsGameFolder(game)) throw new InvalidOperationException("no game folder: EX More Stuff's settings don't name one; give it with --game");
            return game;
        }

        static void Package(List<string> answer, Installed r)
        {
            answer.Add("package=" + r.Package);
            answer.Add("kind=" + (r.Item.IsStage ? "stage" : r.Item.IsColor ? "color" : "costume"));
            if (!r.Item.IsStage) answer.Add("fighter=" + r.Item.Fighter);
            answer.Add(r.Item.IsStage ? "code=" + r.Item.Code : "slot=" + r.Item.Slot.ToString("D2"));
            if (r.Item.IsColor) answer.Add("color=" + r.Item.Color);
        }

        // Add Mod From File, docked: the packages it made
        static void DockFile(string game, string path, Action<string> say, List<string> answer)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("there's no file at " + path);
            // a zip given is copied first: docking makes a plain zip the package in place, and this one is the player's
            string source = path;
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string folder = Path.Combine(AppFolders.Root, "Docked");
                Directory.CreateDirectory(folder);
                source = AppFolders.PlaceFor(folder, Path.GetFileName(path), path);
                if (!File.Exists(source)) File.Copy(path, source);
            }
            var before = new HashSet<string>(Docked.Load().Select(r => r.Package + "|" + r.Item.Key), StringComparer.OrdinalIgnoreCase);
            var problems = new List<string>();
            if (Installer.Package(source) != null) Docked.Add(source);
            else problems.AddRange(ModFile.Dock(game, source, say).Problems);
            var made = Docked.Load().Where(r => !before.Contains(r.Package + "|" + r.Item.Key)).ToList();
            foreach (string p in problems) answer.Add("problem=" + p);
            if (made.Count == 0) throw new InvalidOperationException(problems.Count > 0 ? string.Join("; ", problems) : "nothing could be docked (docked already?)");
            foreach (Installed r in made) Package(answer, r);
        }

        // the costume check: a line per costume (name=verdict), the report written with --report
        static void CheckCostumes(string game, List<string> paths, string reportFile, List<string> answer)
        {
            var sources = paths.Count == 0 ? CostumeCheck.Installed(game) : paths.Where(p => File.Exists(p) || Directory.Exists(p)).SelectMany(CostumeCheck.Find).ToList();
            var results = sources.SelectMany(s => CostumeCheck.Check(game, s)).ToList();
            foreach (var r in results) answer.Add(r.Name + "=" + r.Verdict.ToString().ToUpperInvariant() + " " + r.Where);
            answer.Add("faulty=" + results.Count(r => r.Verdict == CostumeCheck.Verdict.Faulty));
            if (reportFile != null) { File.WriteAllText(reportFile, CostumeCheck.Report(game, results)); answer.Add("report=" + Path.GetFullPath(reportFile)); }
        }

        // the fixed copy where it's asked for, or beside the costume; never over it or into the game's folder
        static void FixCostume(string game, string path, string target, string reportFile, List<string> answer)
        {
            var sources = CostumeCheck.Find(Path.GetFullPath(path));
            if (sources.Count != 1) throw new InvalidOperationException(sources.Count == 0 ? "no costume (CHR_NN.obj.emo) at " + path : "more than one folder or zip at " + path + ": give one");
            if (target == null)
            {
                target = CostumeCheck.DefaultTarget(sources[0].Where);
                if (CostumeCheck.InGame(game, target))
                    throw new InvalidOperationException("the fixed copy would go into the game's folder (" + target + "): give where it goes, --fix-costume \"<folder or zip>\" \"<fixed copy's folder or .zip>\"");
            }
            var done = new List<string>();
            string fixedCopy = CostumeCheck.Fix(game, sources[0], done, target);
            foreach (string d in done) answer.Add("done=" + d);
            answer.Add("fixed=" + fixedCopy);
            var results = CostumeCheck.Find(fixedCopy).SelectMany(s => CostumeCheck.Check(game, s)).ToList();
            foreach (var r in results) answer.Add(r.Name + "=" + r.Verdict.ToString().ToUpperInvariant());
            if (reportFile != null) { File.WriteAllText(reportFile, CostumeCheck.Report(game, results)); answer.Add("report=" + Path.GetFullPath(reportFile)); }
        }

        // Advanced > Free codes: the shared slots no GameBanana mod holds
        static void FreeCodes(string fighter, List<string> answer)
        {
            fighter = fighter.Trim().ToUpperInvariant();
            if (!Fighters.IsCode(fighter)) throw new InvalidOperationException(fighter + " isn't a fighter's code (RYU, KEN, AGL ...)");
            SharedCodes.Holders(GameBanana.All().GetAwaiter().GetResult());
            var free = Enumerable.Range(Catalog.FirstCustomCostume, Catalog.LastShared - Catalog.FirstCustomCostume + 1)
                .Where(s => Seats.Costume(fighter, s).Kind == Seats.Kind.Free).ToList();
            answer.Add("fighter=" + fighter);
            answer.Add("free=" + string.Join(",", free.Select(s => s.ToString("D2"))));
            answer.Add("list=" + (!GameBanana.Stale ? "fresh" : "stale, from " + (GameBanana.ReadAt == DateTime.MinValue ? "the copy EX More Stuff came with" : GameBanana.ReadAt.ToString("yyyy-MM-dd"))));
        }

        static Installed DockedRecord(string zip)
        {
            zip = Path.GetFullPath(zip);
            var found = Docked.Load().Where(r => string.Equals(Path.GetFullPath(r.Package), zip, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0) throw new InvalidOperationException(zip + " isn't docked here (dock it with --dock first, with the same --data)");
            if (found.Count > 1) throw new InvalidOperationException("it's a color pack's zip: one of its colors at a time isn't supported here");
            return found[0];
        }

        // the docked card's code change
        static void SetCode(string zip, string to, List<string> answer)
        {
            Installed r = DockedRecord(zip);
            string code = r.Item.IsStage ? to.Trim().ToUpperInvariant() : null;
            int slot = 0;
            if (!r.Item.IsStage && !int.TryParse(to, out slot)) throw new InvalidOperationException(r.Item.IsColor ? "a color is a number" : "a costume slot is a number");
            string problem = CodeChange.DockedProblem(r, code, slot);
            if (problem != null) throw new InvalidOperationException(problem);
            string warning = Seats.Warning(new Item { Id = r.Item.Id, Type = r.Item.Type, Fighter = r.Item.Fighter, Slot = r.Item.IsStage ? 0 : slot, Code = r.Item.IsStage ? code : "" });
            CodeChange.MoveDocked(r, code, slot);
            if (warning != null) answer.Add("warning=" + warning);
            Package(answer, DockedRecord(r.Package));
        }

        // the docked card's Color pictures: drawn (the game runs once a color only for a costume that can't be), the
        // pictures into the zip; colors 1-10 (a package holds no 11 or 12)
        static void ColorPictures(string game, string zip, string which, Action<string> say, List<string> answer)
        {
            Installed r = DockedRecord(zip);
            if (!r.Item.IsCostume) throw new InvalidOperationException("only a costume's color pictures can be made here");
            string left = PerfTest.Recover(game);   // a test or picture run that stopped halfway: what it changed goes back first
            if (left != null) throw new InvalidOperationException(left);
            var colors = Colors(which);
            var made = PerfTest.ColorPictures(game, r.Item, colors, say, false, r.Package);
            try { PerfTest.PutPictures(game, r.Item, r, made); }
            finally { foreach (var b in made.Values) b.Dispose(); }
            Package(answer, r);
            answer.Add("pictures=" + string.Join(",", made.Keys));
        }

        // "1,2,5-10" (none: all ten)
        static List<int> Colors(string which)
        {
            if (string.IsNullOrWhiteSpace(which)) return Enumerable.Range(1, PerfTest.ColorCount).ToList();
            var colors = new List<int>();
            foreach (string part in which.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] ends = part.Trim().Split('-');
                int a, b;
                if (!int.TryParse(ends[0], out a) || !int.TryParse(ends[ends.Length - 1], out b) || a < 1 || b > PerfTest.ColorCount || a > b)
                    throw new Usage("colors are 1-" + PerfTest.ColorCount + ", e.g. 1,2,5-10");
                for (int c = a; c <= b; c++) if (!colors.Contains(c)) colors.Add(c);
            }
            return colors;
        }
    }
}
