using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // The performance test (user 2026-10-11; docs\STAGE_PERFORMANCE_PLAN.md in the modding project): the game's own
    // benchmark run with the fight EX More Stuff asks for. EX More Stuff puts its helper (res\helper\dinput8.dll, from
    // helper\exms_helper.cpp) next to SSFIV.exe for the run only; it boots the game straight into the benchmark with
    // that fight, writes the benchmark's numbers and closes the game.
    // - The PC check (user: "where the user's PC stands"), once, the first time the player installs or applies
    //   anything: Hugo vs Poison on Blast Furnace, the game's heaviest stage (Weight.BiggestStage).
    // - A custom stage (the player's choice: its card's green circle or "!"): the same fight with the custom stage in
    //   Blast Furnace's place (Replacements, as the Stages page does it), compared with the PC check.
    // - A custom costume: its fighter on both sides (user) on Training Stage, the custom costume in the Original's place
    //   (CostumeSwaps), compared with the same fight in the Original (once per fighter).
    // Anything past its warning line that fails its test (doesn't load, or doesn't keep full speed) is switched off and
    // can't be switched on again on this PC (user: "should not be loadable": it would crash the game for its owner and
    // break the match for P2), whatever line the player set by hand (Weight.Lockable); a new version of it may be tried.
    // Each test also moves the automatic warning lines (Weight.FromPcCheck, Weight.Learn). The comparison runs are kept per PC and graphics
    // settings. Everything is put back after a run: the swap, the player's own swap or replacement in that place,
    // another dinput8.dll that was there (Tom's Round BGM mod), config.ini; at the next start too if it stopped halfway.
    static class PerfTest
    {
        public class Result
        {
            public double Average, Lowest, Baseline;
            // from the game's speed only (a player 2026-10-15: "the benchmark's own numbers weren't caught": their SSFIV.exe
            // differs where the helper catches the benchmark): 60 / speed, the speed never under 1, so 60 means 60 or more
            public bool Estimated;
            public string Against;   // what the comparison ran on: "Blast Furnace", "Ryu's Original costume"
            public bool FullSpeed { get { return Average >= FullSpeedFps; } }
            /// <summary>How much slower than the game's own, in percent (0 or less: as fast or faster).</summary>
            public double Slower { get { return Baseline > 0 ? (Baseline - Average) * 100 / Baseline : 0; } }
        }

        /// <summary>The game didn't get through the test with it (it crashed, closed or never finished): it didn't load.</summary>
        public class LoadFailed : Exception { public LoadFailed(string message) : base(message) { } }

        const double FullSpeedFps = 59;          // the game plays at 60: an average at it or above keeps full speed
        const string Helper = "dinput8.dll", Request = "exms_benchmark.txt", Aside = "dinput8.dll.ex_more_stuff_test";
        const string PendingName = "ex_more_stuff_test.json";
        static readonly TimeSpan Longest = TimeSpan.FromMinutes(6);
        // the PC check's fight (user: Blast Furnace, the heaviest, then Hugo vs Poison), also a custom stage's
        const string Heaviest = "BFU";
        static readonly string[] PcFighters = { "HUG", "PSN" };
        static readonly int[] PcFight = { Array.IndexOf(Fighters.Codes, PcFighters[0]), Array.IndexOf(Fighters.Codes, PcFighters[1]), StageId(Heaviest) };

        // the game's stage IDs: the place in Stages.Codes, with 22 and 23 left out (unused) from Pit Stop 109 on
        static int StageId(string code) { int i = Array.IndexOf(Stages.Codes, code); return i < 22 ? i : i + 2; }

        // for test\PerfTestTest.cs: another config.ini, and no need for Steam (its made-up game doesn't use it)
        public static string ConfigFile = null;
        public static bool NeedSteam = true;

        static string ConfigPath
        {
            get { return ConfigFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CAPCOM", "SUPERSTREETFIGHTERIV", "config.ini"); }
        }

        static string ResultsFile { get { return AppFolders.DataFile("performance.json"); } }
        static string PendingPath(string game) { return Path.Combine(Game.PatchFolder(game), PendingName); }

        /// <summary>Why a test can't run now, or null.</summary>
        public static string Blocked()
        {
            if (Game.IsRunning()) return "Street Fighter IV is running: close it first";
            if (NeedSteam && Process.GetProcessesByName("steam").Length == 0) return "Steam isn't running: the game needs it to start";
            return null;
        }

        /// <summary>The PC check's result for the game's graphics settings now (the average), or null before it ran.</summary>
        public static double? PcResult()
        {
            double average;
            return Baselines().TryGetValue(PcKey(), out average) ? average : (double?)null;
        }

        static string PcKey() { return "pc:" + GraphicsKey(); }

        /// <summary>The PC check: Hugo vs Poison on the game's own Blast Furnace (off the window's thread, about 12 s).</summary>
        public static Result CheckPc(string game, Action<string> say)
        {
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            string key = PcKey();   // the settings as the player has them (config.ini comes back after)
            try
            {
                Begin(game, Heaviest, null);
                say("Checking how this PC runs the game: the game opens for a few seconds and closes by itself...");
                Result r;
                try { r = Benchmark(game, PcFight, DevMode.Shot("PC check - Blast Furnace, Hugo vs Poison")); }
                catch (LoadFailed ex) { throw new InvalidOperationException("the game's own Blast Furnace didn't finish its test: " + ex.Message); }
                SaveBaseline(key, r.Average, r.Estimated);
                if (!r.Estimated || !r.FullSpeed) Weight.FromPcCheck(r.Average);   // where the stages' warning line starts on this PC (user); "60 or more" says too little
                return r;
            }
            finally { Recover(game); }
        }

        /// <summary>Tests an installed custom stage or costume (off the window's thread: about 12 seconds a run; a stage is
        /// compared with the PC check, run first if it hasn't been; a costume runs once, no comparison: user 2026-10-13,
        /// "dont run the test twice ... just run the costume test"). Throws LoadFailed when the game didn't get through it.
        /// `flagged`: past its warning line (Weight.Lockable), so failing locks it (Locked). The automatic warning line learns
        /// from it.</summary>
        public static Result Run(string game, Item item, bool flagged, Action<string> say)
        {
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            string fighter = item.IsCostume ? item.Fighter : null;
            int id = fighter != null ? Array.IndexOf(Fighters.Codes, fighter) : 0;
            int[] fight = item.IsStage ? PcFight : new[] { id, id, 0 };   // a costume: its fighter on both sides, Training Stage
            string against = item.IsStage ? Stages.Name(Heaviest) : null;   // a costume: nothing to compare with
            string pcKey = PcKey();   // the settings as the player has them, read before a run changes config.ini
            double times = Weight.TimesInGame(game, item);   // its files as installed, before the test swaps anything
            try
            {
                Begin(game, item.IsStage ? Heaviest : null, fighter);
                double baseline = 0;
                if (item.IsStage && !Baselines().TryGetValue(pcKey, out baseline))
                {
                    say("Checking how this PC runs the game's own Blast Furnace first (once)...");
                    Result pc;
                    try { pc = Benchmark(game, fight, DevMode.Shot(against + " (comparison)")); baseline = pc.Average; }
                    catch (LoadFailed ex) { throw new InvalidOperationException("the game's own " + against + " didn't finish its test: " + ex.Message); }
                    SaveBaseline(pcKey, baseline, pc.Estimated);
                    if (!pc.Estimated || !pc.FullSpeed) Weight.FromPcCheck(baseline);   // that was the PC check
                }
                say("Putting it in place of " + (item.IsStage ? against : Fighters.Name(fighter) + "'s Original costume") + " for the test...");
                if (item.IsStage) Replacements.Replace(game, Heaviest, item.Code);
                else CostumeSwaps.Replace(game, fighter, 1, item.Slot);
                say("Testing it: the game opens for a few seconds and closes by itself...");
                Result result;
                try { result = Benchmark(game, fight, DevMode.Shot(Shown(item))); }
                catch (LoadFailed ex)
                {
                    Weight.Learn(item.IsStage, times, false);
                    if (flagged) Lock(item, "it didn't load in the game's test on this PC (" + ex.Message + ")");
                    throw;
                }
                result.Baseline = baseline;
                result.Against = against;
                SaveResult(item.Key, result);
                Weight.Learn(item.IsStage, times, result.FullSpeed);
                if (flagged && !result.FullSpeed) Lock(item, "it ran at " + result.Average.ToString("0") + " fps in the game's test on this PC, below full speed (60)");
                return result;
            }
            finally { Recover(game); }
        }

        // What has to come back, written down before anything changes (Recover reads it, also after a crash); then the
        // place the test uses is cleared: another dinput8.dll aside, the player's own replacement or swap there off, and
        // files put in the patch folder by hand for the stage it plays on or the fighter's Original (a port over Blast
        // Furnace, a skin over Ryu's Original: user, "it loaded the wrong scenario") aside, so the game's own load.
        static void Begin(string game, string stage, string fighter)
        {
            if (File.Exists(PendingPath(game))) Recover(game);
            var pending = new Pending { Stage = stage, Fighter = fighter, Began = DateTime.UtcNow.Ticks };
            if (File.Exists(ConfigPath)) pending.Config = Convert.ToBase64String(File.ReadAllBytes(ConfigPath));
            Replacement theirStage = stage != null ? Replacements.Load(game).FirstOrDefault(r => r.Stage == stage) : null;
            pending.TheirStage = theirStage != null ? theirStage.Source : null;
            CostumeSwap theirSwap = fighter != null ? CostumeSwaps.Load(game).FirstOrDefault(s => s.Fighter == fighter && s.Costume == 1) : null;
            pending.TheirSwap = theirSwap != null ? theirSwap.Source : 0;
            pending.Moved = File.Exists(Path.Combine(game, Helper));
            Write(game, pending);
            NoSpeed();
            if (pending.Moved) File.Move(Path.Combine(game, Helper), Path.Combine(game, Aside));   // Tom's Round BGM mod, or another mod's
            if (pending.TheirStage != null) { string note = Replacements.Restore(game, stage); if (note != null) throw new IOException(note); }
            if (pending.TheirSwap != 0) { string note = CostumeSwaps.Restore(game, fighter, 1); if (note != null) throw new IOException(note); }
            // what the run plays: Hugo vs Poison on Blast Furnace, or the costume's fighter on Training Stage
            pending.Aside = HandPlaced(game, stage ?? "TRN", fighter != null ? new[] { fighter } : PcFighters);
            Write(game, pending);
            string patch = Game.PatchFolder(game);
            foreach (string relative in pending.Aside)
            {
                string to = Path.Combine(patch, AsideFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (File.Exists(to)) throw new IOException("an older copy of " + Path.GetFileName(relative) + " is already in " + AsideFolder);
                File.Move(Path.Combine(patch, relative), to);
            }
        }

        // The game starts with the speed config.ini keeps from an earlier run of its own benchmark ([SYSTEM]
        // SystemSpeed=1.00000; Tom 2026-10-15: the game closed after 4 seconds, "the benchmark's own numbers weren't
        // caught"), and the helper takes a speed for the benchmark's end: none for the run (-1, as a game that never ran
        // its benchmark has). config.ini comes back after (Recover: changed since Began).
        static void NoSpeed()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                string[] lines = File.ReadAllLines(ConfigPath);
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].StartsWith("SystemSpeed=", StringComparison.OrdinalIgnoreCase) && lines[i].Trim() != "SystemSpeed=-1.00000")
                    {
                        lines[i] = "SystemSpeed=-1.00000";
                        changed = true;
                    }
                if (changed) File.WriteAllLines(ConfigPath, lines, Encoding.ASCII);
            }
            catch (IOException) { }   // the helper still waits for the fight (it notes the speed only after the benchmark started)
        }

        const string AsideFolder = "ex_more_stuff_test_aside";

        // The patch folder's files for the stage a run plays on and the fighter's Original costume (EX More Stuff's own
        // replacement and swap there are already off): put there by hand, so not the game's own.
        static List<string> HandPlaced(string game, string stage, IEnumerable<string> fighters)
        {
            string patch = Game.PatchFolder(game);
            var found = new List<string>();
            foreach (string suffix in StagePack.Suffixes)
            {
                string relative = StagePack.RelativePath(stage, suffix);
                if (File.Exists(Path.Combine(patch, relative))) found.Add(relative);
            }
            foreach (string fighter in fighters)
            {
                string chara = Path.Combine(patch, "battle", "chara", fighter);
                if (!Directory.Exists(chara)) continue;
                var original = new System.Text.RegularExpressions.Regex("^" + fighter + @"_01(\.(obj\.emo|shd\.emo|nml\.emb|bsr|csb)|_\d\d\.(col\.emb|obj\.emm))$",
                                                                       System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                found.AddRange(Directory.GetFiles(chara).Where(f => original.IsMatch(Path.GetFileName(f))).Select(f => f.Substring(patch.Length + 1)));
            }
            return found;
        }

        /// <summary>Puts back what a test changed (its swap, the player's own replacement or swap in that place, the
        /// dinput8.dll it moved aside, config.ini) if one stopped halfway; nothing when no test was going. Returns a note
        /// when something stayed, or null.</summary>
        public static string Recover(string game)
        {
            Pending pending = Read(game);
            if (pending == null || Game.IsRunning()) return null;
            var notes = new List<string>();
            Action<Action> attempt = step => { try { step(); } catch (Exception ex) { notes.Add(ex.Message); } };
            foreach (string file in new[] { Helper, Request })
                attempt(() => { if (IsOurs(game, file)) File.Delete(Path.Combine(game, file)); });   // only ours goes
            // the test's own swap goes, the files moved aside come back, then the player's own replacement or swap
            if (pending.Stage != null) attempt(() => { string n = Replacements.Restore(game, pending.Stage); if (n != null) notes.Add(n); });
            if (pending.Fighter != null) attempt(() => { string n = CostumeSwaps.Restore(game, pending.Fighter, 1); if (n != null) notes.Add(n); });
            string patch = Game.PatchFolder(game);
            foreach (string relative in pending.Aside ?? new List<string>())
                attempt(() =>
                {
                    string from = Path.Combine(patch, AsideFolder, relative), to = Path.Combine(patch, relative);
                    if (!File.Exists(from)) return;
                    if (File.Exists(to)) throw new IOException(Path.GetFileName(relative) + " is in its place again, so the one moved aside stays in " + AsideFolder);
                    File.Move(from, to);
                });
            attempt(() =>
            {
                string aside = Path.Combine(patch, AsideFolder);
                if (!Directory.Exists(aside)) return;
                foreach (string dir in Directory.GetDirectories(aside, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                if (!Directory.EnumerateFileSystemEntries(aside).Any()) Directory.Delete(aside);
            });
            if (pending.Stage != null && pending.TheirStage != null) attempt(() => Replacements.Replace(game, pending.Stage, pending.TheirStage));
            if (pending.Fighter != null && pending.TheirSwap != 0) attempt(() => CostumeSwaps.Replace(game, pending.Fighter, 1, pending.TheirSwap));
            if (pending.Moved)
                attempt(() => { if (File.Exists(Path.Combine(game, Aside)) && !File.Exists(Path.Combine(game, Helper))) File.Move(Path.Combine(game, Aside), Path.Combine(game, Helper)); });
            // the game's settings as they were, if the test changed them (written since it began)
            if (pending.Config != null && File.Exists(ConfigPath) && File.GetLastWriteTimeUtc(ConfigPath).Ticks > pending.Began)
                attempt(() => File.WriteAllBytes(ConfigPath, Convert.FromBase64String(pending.Config)));
            if (notes.Count == 0) { File.Delete(PendingPath(game)); try { File.Delete(PendingPath(game) + ".bak"); } catch (Exception) { } }   // its copy from before (SafeFile) too
            return notes.Count == 0 ? null : "The last performance test didn't put everything back: " + string.Join("; ", notes);
        }

        // our helper or our request file (the helper's bytes as built in): never another mod's dinput8.dll
        static bool IsOurs(string game, string file)
        {
            string path = Path.Combine(game, file);
            if (!File.Exists(path)) return false;
            if (file == Request) return true;
            using (var sha = SHA256.Create()) return sha.ComputeHash(File.ReadAllBytes(path)).SequenceEqual(sha.ComputeHash(HelperBytes()));
        }

        static byte[] HelperBytes()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("helper.dll"))
            using (var data = new MemoryStream())
            {
                if (s == null) throw new InvalidOperationException("this copy of EX More Stuff was built without its test helper");
                s.CopyTo(data);
                return data.ToArray();
            }
        }

        // One run of the game with the helper and a request (the fight by the game's IDs, and what else the helper is
        // asked for): the result file's lines; LoadFailed when the game didn't get through it.
        static Dictionary<string, string> RunGame(string game, string request)
        {
            string result = AppFolders.DataFile("benchmark_result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(result));
            if (File.Exists(result)) File.Delete(result);
            File.WriteAllBytes(Path.Combine(game, Helper), HelperBytes());
            try
            {
                File.WriteAllText(Path.Combine(game, Request), "result=" + result + "\r\n" + request, new UTF8Encoding(false));
                var start = new ProcessStartInfo(Path.Combine(game, "SSFIV.exe")) { WorkingDirectory = game, UseShellExecute = false };
                start.EnvironmentVariables["SteamAppId"] = "45760";   // as EMBER starts it: straight from the exe, Steam still needs to know the game
                start.EnvironmentVariables["SteamGameId"] = "45760";
                using (Process process = Process.Start(start))
                {
                    // the result written and the game still open 10 seconds on (a run 2026-10-15 hung closing): closed, the
                    // result stands
                    var clock = Stopwatch.StartNew();
                    TimeSpan? written = null;
                    while (!process.WaitForExit(500))
                    {
                        if (written == null && File.Exists(result)) written = clock.Elapsed;
                        bool stuck = written != null && clock.Elapsed - written.Value > TimeSpan.FromSeconds(10);
                        if (!stuck && clock.Elapsed < Longest) continue;
                        try { process.Kill(); process.WaitForExit(10000); } catch (Exception) { }
                        if (stuck) break;
                        throw new LoadFailed("the game didn't finish the test in " + Longest.TotalMinutes + " minutes");
                    }
                }
            }
            finally
            {
                foreach (string file in new[] { Helper, Request })
                    try { if (IsOurs(game, file)) File.Delete(Path.Combine(game, file)); } catch (Exception) { }
            }
            if (!File.Exists(result)) throw new LoadFailed(ClosedEarly);
            var values = File.ReadAllLines(result).Select(l => l.Split(new[] { '=' }, 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            string error;
            // (the benchmark's own numbers not caught, the game's speed there: Benchmark reads that instead)
            if (values.TryGetValue("error", out error) && !values.ContainsKey("speed")) throw new LoadFailed(error);
            return values;
        }

        static string FightLines(int[] fight) { return "p1=" + fight[0] + "\r\np2=" + fight[1] + "\r\nstage=" + fight[2] + "\r\n"; }

        // One run of the benchmark: its numbers.
        static Result Benchmark(string game, int[] fight, string shot)
        {
            var values = RunGame(game, FightLines(fight) + (shot != null ? "shot=" + shot + "\r\n" : ""));   // developer mode: a frame of the fight
            double average, lowest, speed;
            if (!values.ContainsKey("average") || !double.TryParse(values["average"], NumberStyles.Float, CultureInfo.InvariantCulture, out average))
            {
                // the benchmark's own numbers not caught, but it finished: the speed the game set from its average
                if (values.ContainsKey("speed") && double.TryParse(values["speed"], NumberStyles.Float, CultureInfo.InvariantCulture, out speed) && speed >= 1)
                    return new Result { Average = 60 / speed, Estimated = true };
                throw new LoadFailed(values.ContainsKey("error") ? values["error"] : "the test gave no average");
            }
            double.TryParse(values.ContainsKey("lowest") ? values["lowest"] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out lowest);
            return new Result { Average = average, Lowest = lowest };
        }

        // Color pictures (user 2026-10-12, for Ember's character select; Tom: "a render of the character with the
        // costume for each color"): the benchmark's fight in training mode (game mode 3 for its 2 at RVA 0x26B00: both
        // fighters stand idle), the color in its request (RVA 0x26C88: 0 is color 1, for both sides), and the helper
        // saves the layer the game draws the fighters on (no stage, no life bars: transparent around them) 60 frames
        // in. P1 is the costume's fighter wearing it (in the Original's place), P2 Ryu (Ken beside Ryu), cut away.
        const string TrainingMode = "patch=26B00:02>03\r\n";
        // A package's colors: 01-10 (Installer.AllowedNames takes no <prefix>_11.png or _12.png). The game's two styles of
        // color 1 are View...'s only (CostumeRender.Colors).
        public const int ColorCount = 10;
        public const int PictureWidth = 256, PictureHeight = 384;   // as the pattern pictures (ColorVariants), for Ember's gallery

        /// <summary>An installed custom costume in the colors asked for (1-10; all when null), drawn (Drawn) or, when it can't
        /// be, P1 standing idle as the game draws it, cut out (off the window's thread: about 7 seconds of game a color). `say` says which color. A docked
        /// file (`docked`, its zip: not in the game) is unpacked beside the program for the runs and in Ryu's Original's
        /// place from there; the unpacked files go after. useGame(why it couldn't be drawn): asked before the game runs
        /// instead (false: OperationCanceledException); null runs it without asking.</summary>
        public static SortedDictionary<int, Bitmap> ColorPictures(string game, Item item, IList<int> colors, Action<string> say, bool fullSize = false, string docked = null,
                                                                  Func<string, bool> useGame = null)
        {
            colors = colors ?? Enumerable.Range(1, ColorCount).ToList();
            string why;
            var drawn = Drawn(game, item.Fighter + "_" + item.Slot.ToString("D2"), () => docked != null ? ZipCostume(docked, item) : Read(CostumeSwaps.CostumeFiles(game, item.Fighter, item.Slot)),
                              colors.Where(c => c >= 1 && c <= ColorCount).Distinct().OrderBy(c => c).ToList(), say, fullSize, out why);
            if (drawn != null) return drawn;
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            if (useGame != null && !useGame(why)) throw new OperationCanceledException();
            string fighter = item.Fighter;
            int id = Array.IndexOf(Fighters.Codes, fighter), other = id == 0 ? 1 : 0;
            var pictures = new SortedDictionary<int, Bitmap>();
            string unpacked = docked != null ? AppFolders.DataFile("docked_costume") : null;
            try
            {
                Begin(game, null, fighter);
                if (docked != null) CostumeSwaps.ReplaceWith(game, fighter, 1, item.Slot, Unpack(docked, item, unpacked));
                else CostumeSwaps.Replace(game, fighter, 1, item.Slot);
                string shot = AppFolders.DataFile("color_picture.png");
                int done = 0;
                var making = colors.Where(c => c >= 1 && c <= ColorCount).Distinct().OrderBy(c => c).ToList();
                foreach (int color in making)
                {
                    if (color != making[0]) Breather();
                    say("Color " + color + (colors.Count > 1 ? " (" + ++done + " of " + colors.Count + ")" : "") + ": the game opens for a few seconds and closes by itself...");
                    if (File.Exists(shot)) File.Delete(shot);
                    RunGame(game, FightLines(new[] { id, other, 0 }) + TrainingMode + "patch=26C88:00>" + (color - 1).ToString("X2") + "\r\n" +
                                  "shot=" + shot + "\r\nshotat=60\r\nlayer=1\r\nquit=shots\r\n");
                    if (!File.Exists(shot)) throw new LoadFailed("the game saved no picture of color " + color);
                    using (var whole = new Bitmap(new MemoryStream(File.ReadAllBytes(shot)))) pictures[color] = fullSize ? FullSize(whole) : Cutout(whole);
                    File.Delete(shot);
                }
                return pictures;
            }
            catch (Exception)
            {
                foreach (Bitmap p in pictures.Values) p.Dispose();
                throw;
            }
            finally
            {
                Recover(game);
                if (unpacked != null) try { Directory.Delete(unpacked, true); } catch (Exception) { }
            }
        }

        // Color pictures drawn without the game (CostumeRender; user 2026-10-23: "automate the screenshots/images inside the
        // costume packs"): the costume in the stance as SF4 Ember's gallery pictures are made, lit like the training stage,
        // a second or two a color and nothing to start. files: the costume's, by what follows its prefix. Null when it
        // can't be drawn (the reason said, and in `why`): the game makes the pictures then.
        static SortedDictionary<int, Bitmap> Drawn(string game, string stem, Func<Dictionary<string, byte[]>> files, IList<int> colors, Action<string> say, bool fullSize, out string why)
        {
            why = null;
            var pictures = new SortedDictionary<int, Bitmap>();
            try
            {
                var have = files();
                var costume = new CostumeRender.Costume(game, stem, name => { byte[] b; return have.TryGetValue(name.Substring(stem.Length), out b) ? b : null; });
                int done = 0;
                foreach (int color in colors)
                {
                    say("Color " + color + (colors.Count > 1 ? " (" + ++done + " of " + colors.Count + ")" : "") + ": drawing it...");
                    using (Bitmap layer = costume.Draw(color, fullSize ? 600 : 450, fullSize ? 800 : 600))
                    {
                        if (layer == null) throw new InvalidDataException("color " + color + " has no textures");
                        Rectangle crop = DrawnBounds(layer);
                        pictures[color] = fullSize ? FullSizeOf(layer, crop) : CutoutOf(layer, crop);
                    }
                }
                return pictures;
            }
            catch (Exception ex)
            {
                foreach (Bitmap p in pictures.Values) p.Dispose();
                why = ex.Message;
                say("It couldn't be drawn (" + ex.Message + "); the game makes the pictures instead.");
                return null;
            }
        }

        /// <summary>An installed custom costume (or a docked one: `docked`, its zip) ready to draw (CostumeViewer).</summary>
        internal static CostumeRender.Costume Costume(string game, Item item, string docked)
        {
            string stem = item.Fighter + "_" + item.Slot.ToString("D2");
            var have = docked != null ? ZipCostume(docked, item) : Read(CostumeSwaps.CostumeFiles(game, item.Fighter, item.Slot));
            return new CostumeRender.Costume(game, stem, name => { byte[] b; return have.TryGetValue(name.Substring(stem.Length), out b) ? b : null; });
        }

        /// <summary>Color packs drawn: each the costume's files with the pack's as color 1. Null when one can't be (why: the
        /// reason).</summary>
        internal static SortedDictionary<int, Bitmap> DrawnPacks(string game, IList<Installed> packs, Action<string> say, bool fullSize)
        {
            string why;
            return DrawnPacks(game, packs, say, fullSize, out why);
        }

        static SortedDictionary<int, Bitmap> DrawnPacks(string game, IList<Installed> packs, Action<string> say, bool fullSize, out string why)
        {
            why = null;
            var drawn = new SortedDictionary<int, Bitmap>();
            foreach (Installed pack in packs)
            {
                Item item = pack.Item;
                var one = Drawn(game, item.Fighter + "_" + item.Slot.ToString("D2"), () =>
                {
                    var files = Read(CostumeSwaps.CostumeFiles(game, item.Fighter, item.Slot));
                    foreach (var part in UnpackColor(pack.Package, item)) files["_01" + part.Key] = part.Value;
                    return files;
                }, new[] { 1 }, text => say(text.Replace("Color 1", "Color " + item.Color)), fullSize, out why);
                if (one == null) { foreach (Bitmap p in drawn.Values) p.Dispose(); return null; }
                drawn[item.Color] = one[1];
            }
            return drawn;
        }

        static Dictionary<string, byte[]> Read(Dictionary<string, string> paths)
        {
            return paths.ToDictionary(p => p.Key, p => File.ReadAllBytes(p.Value), StringComparer.OrdinalIgnoreCase);
        }

        // everything drawn (one fighter, framed by the renderer)
        static Rectangle DrawnBounds(Bitmap layer)
        {
            int left = layer.Width, top = layer.Height, right = -1, bottom = -1;
            var data = layer.LockBits(new Rectangle(0, 0, layer.Width, layer.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new int[layer.Width];
            for (int y = 0; y < layer.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, layer.Width);
                for (int x = 0; x < layer.Width; x++)
                    if (((row[x] >> 24) & 0xFF) > 16) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            }
            layer.UnlockBits(data);
            if (right < 0) throw new InvalidDataException("nothing was drawn");
            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }

        // Between one color picture's run of the game and the next (user 2026-10-22: "add a 3 second delay to subsequent
        // color creations IF their game has trouble running"): 3 seconds for the PC to settle when its PC check is under
        // SlowPc or under (user: "struggling should be 250 or lower"; a steady PC is ~300: 295-299), or it has none yet.
        const double SlowPc = 250;

        static void Breather()
        {
            double? pc = PcResult();
            if (pc == null || pc.Value <= SlowPc) System.Threading.Thread.Sleep(3000);
        }

        // Color pack pictures (user 2026-10-18: "where do i go to generate the colors for this?"): a new color (30-99)
        // can't be picked in the benchmark's fight, so for each its costume's files (as the game has them) play as P1's
        // Original with the pack's texture as color 1, and color 1 is pictured as ColorPictures does. A run of the game
        // each; Begin and Recover around each put the Original (and a swap of the player's) back.
        public static SortedDictionary<int, Bitmap> PackPictures(string game, IList<Installed> packs, Action<string> say, bool fullSize = false, Func<string, bool> useGame = null)
        {
            string why;
            var drawn = DrawnPacks(game, packs, say, fullSize, out why);
            if (drawn != null) return drawn;
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            if (useGame != null && !useGame(why)) throw new OperationCanceledException();   // as ColorPictures
            var pictures = new SortedDictionary<int, Bitmap>();
            string unpacked = AppFolders.DataFile("color_pack"), shot = AppFolders.DataFile("color_picture.png");
            try
            {
                int done = 0;
                foreach (Installed pack in packs)
                {
                    Item item = pack.Item;
                    if (pack != packs[0]) Breather();
                    say("Color " + item.Color + (packs.Count > 1 ? " (" + ++done + " of " + packs.Count + ")" : "") + ": the game opens for a few seconds and closes by itself...");
                    int id = Array.IndexOf(Fighters.Codes, item.Fighter), other = id == 0 ? 1 : 0;
                    if (File.Exists(shot)) File.Delete(shot);
                    Begin(game, null, item.Fighter);
                    try
                    {
                        var files = CostumeSwaps.CostumeFiles(game, item.Fighter, item.Slot);
                        if (!files.ContainsKey(".obj.emo")) throw new InvalidOperationException(Fighters.Name(item.Fighter) + "'s costume " + item.Slot + " isn't in the game");
                        foreach (var part in UnpackColor(pack.Package, item, unpacked)) files["_01" + part.Key] = part.Value;
                        CostumeSwaps.ReplaceWith(game, item.Fighter, 1, item.Slot, files);
                        RunGame(game, FightLines(new[] { id, other, 0 }) + TrainingMode + "patch=26C88:00>00\r\n" +
                                      "shot=" + shot + "\r\nshotat=60\r\nlayer=1\r\nquit=shots\r\n");
                    }
                    finally { Recover(game); }
                    if (!File.Exists(shot)) throw new LoadFailed("the game saved no picture of color " + item.Color);
                    using (var whole = new Bitmap(new MemoryStream(File.ReadAllBytes(shot)))) pictures[item.Color] = fullSize ? FullSize(whole) : Cutout(whole);
                    File.Delete(shot);
                }
                return pictures;
            }
            catch (Exception)
            {
                foreach (Bitmap p in pictures.Values) p.Dispose();
                throw;
            }
            finally { try { if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true); } catch (Exception) { } }
        }

        // a color pack's texture and its material (<prefix>.col.emb, <prefix>.obj.emm when it has one) in its zip, by what follows
        // the prefix
        static Dictionary<string, byte[]> UnpackColor(string zipPath, Item item)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                foreach (string part in new[] { ".col.emb", ".obj.emm" })
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(item.Prefix + part, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;
                    using (Stream s = entry.Open())
                    using (var to = new MemoryStream()) { s.CopyTo(to); files[part] = to.ToArray(); }
                }
            if (!files.ContainsKey(".col.emb")) throw new InvalidDataException(item.Prefix + ".col.emb isn't in its zip");
            return files;
        }

        // a color pack's texture and its material (<prefix>.col.emb, <prefix>.obj.emm when it has one) out of its zip into
        // `folder` (emptied first), by what follows the prefix
        static Dictionary<string, string> UnpackColor(string zipPath, Item item, string folder)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in UnpackColor(zipPath, item))
            {
                string path = Path.Combine(folder, item.Prefix + part.Key);
                File.WriteAllBytes(path, part.Value);
                files[part.Key] = path;
            }
            return files;
        }

        /// <summary>Color pack pictures into their packages: each zip's <prefix>.png (its picture on the Costumes page and its
        /// card) replaced, then the package into the game again (a docked file: not).</summary>
        public static void PutPackPictures(string game, IList<Installed> packs, IDictionary<int, Bitmap> made)
        {
            // an installed one goes into the game again after: nothing is changed while the game is open
            if (packs.Any(p => p.Source != Docked.Source) && Game.IsRunning()) throw new InvalidOperationException("close the game first");
            foreach (Installed pack in packs)
            {
                Bitmap picture;
                if (!made.TryGetValue(pack.Item.Color, out picture)) continue;
                string entry = pack.Item.Prefix + ".png";
                SafeFile.EditZip(pack.Package, zip =>   // on a copy, swapped in when done
                {
                    foreach (var old in zip.Entries.Where(e => string.Equals(e.Name, entry, StringComparison.OrdinalIgnoreCase)).ToList()) old.Delete();
                    using (Stream s = zip.CreateEntry(entry).Open()) picture.Save(s, ImageFormat.Png);
                });
                if (pack.Source != Docked.Source) Installer.InstallPackage(game, pack.Item, pack.Package, "file");
            }
        }

        // Stage preview (user 2026-10-14: a docked stage seen in the game, "no characters, screen starts on the left, pans
        // to the right and closes"; its pictures shown on its card, each full size on a click): the docked stage in
        // Training Stage's place from its zip, the benchmark in training mode (both stand idle) with the helper's hide=1
        // (no fighters, no overlay) and slide=1 (held at the left wall 1.5 s, carried to the right in about 8 s, the game
        // running as fast as it can so the camera tilts little: user), pictures at the left wall, the middle and the
        // right wall. About 17 s with the game starting. Begin and Recover put Training Stage and the rest back.
        const string PreviewShots = "shotat=14,55,999\r\nhide=1\r\nslide=1\r\nquit=shots\r\n";
        static readonly int[] PreviewFrames = { 14, 55, 999 };

        /// <summary>Where a docked stage's preview pictures are kept: beside the program (data), a folder per zip.</summary>
        public static string PreviewFolder(string zipPath)
        {
            string key;
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(zipPath).ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
            return Path.Combine(AppFolders.DataFile("stage_previews"), key);
        }

        /// <summary>A docked stage's preview pictures (left, middle, right), the ones there are.</summary>
        public static List<string> PreviewPictures(string zipPath)
        {
            string folder = PreviewFolder(zipPath);
            return PreviewFrames.Select(f => Path.Combine(folder, "preview_" + f + ".png")).Where(File.Exists).ToList();
        }

        /// <summary>The preview picture last made the stage's picture (UseAsStagePicture), or null.</summary>
        public static string StagePictureShot(string zipPath)
        {
            string note = Path.Combine(PreviewFolder(zipPath), "picture.txt");
            try { if (File.Exists(note)) { string shot = Path.Combine(PreviewFolder(zipPath), File.ReadAllText(note).Trim()); if (File.Exists(shot)) return shot; } }
            catch (Exception) { }
            return null;
        }

        /// <summary>One of a docked stage's preview pictures as its stage picture (user 2026-10-14: "from here i should be
        /// able to make one of these images the stage thumbnail"): made 640x360, as stage pictures are, into its zip as
        /// STG_<code>.png (the one there replaced), and noted as the one chosen.</summary>
        public static void UseAsStagePicture(string zipPath, Item item, string shot)
        {
            byte[] png;
            using (var full = new Bitmap(new MemoryStream(File.ReadAllBytes(shot))))
            using (var small = new Bitmap(640, 360))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(full, 0, 0, small.Width, small.Height);
                }
                using (var data = new MemoryStream()) { small.Save(data, ImageFormat.Png); png = data.ToArray(); }
            }
            string name = item.Prefix + ".png";   // STG_<code>.png: the first name a package's picture is looked for under
            SafeFile.EditZip(zipPath, zip =>   // on a copy, swapped in when done
            {
                foreach (var old in zip.Entries.Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToList()) old.Delete();
                using (Stream s = zip.CreateEntry(name).Open()) s.Write(png, 0, png.Length);
            });
            File.WriteAllText(Path.Combine(PreviewFolder(zipPath), "picture.txt"), Path.GetFileName(shot));
        }

        /// <summary>A docked stage (its zip: not in the game) previewed in the game; its pictures' paths. Off the window's
        /// thread.</summary>
        public static List<string> StagePreview(string game, Item item, string zipPath)
        {
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            var files = new Dictionary<string, byte[]>();
            byte[] music = null;   // its own music (BGM_<code>.csb), played in Training Stage's place too; none: Training Stage's
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                Func<string, byte[]> read = name =>
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return null;
                    using (Stream s = entry.Open())
                    using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
                };
                foreach (string suffix in StagePack.Suffixes)
                {
                    files[suffix] = read("STG_" + item.Code + suffix);
                    if (files[suffix] == null) throw new InvalidDataException("STG_" + item.Code + suffix + " isn't in its zip");
                }
                music = read("BGM_" + item.Code + ".csb");
            }
            string folder = PreviewFolder(zipPath), shot = Path.Combine(folder, "preview.png");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            try
            {
                Begin(game, "TRN", null);
                Replacements.ReplaceWith(game, "TRN", "docked", item.Code, files, music);
                RunGame(game, FightLines(new[] { 0, 1, StageId("TRN") }) + TrainingMode + "shot=" + shot + "\r\n" + PreviewShots);
            }
            finally { Recover(game); }
            var made = PreviewPictures(zipPath);
            if (made.Count == 0) throw new LoadFailed("the game saved no pictures");
            return made;
        }

        /// <summary>Listen to stage (user 2026-10-15: the green circle's menu): an installed stage with its own music, in
        /// Training Stage's place, in training mode (the benchmark plays no music; training mode does), for ListenSeconds,
        /// then the game closes by itself. Off the window's thread.</summary>
        public static void Listen(string game, Item item)
        {
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            try
            {
                Begin(game, "TRN", null);
                Replacements.Replace(game, "TRN", item.Code, true);   // its BGM_<code>.csb plays as Training Stage's
                // the player closing the game sooner (its X, Alt+F4) leaves no result: that's fine for listening
                try { RunGame(game, FightLines(new[] { 0, 1, StageId("TRN") }) + TrainingMode + "listen=" + ListenSeconds + "\r\n"); }
                catch (LoadFailed ex) when (ex.Message == ClosedEarly) { }
            }
            finally { Recover(game); }
        }

        public const int ListenSeconds = 60;
        const string ClosedEarly = "the game closed before the test was done";

        // a docked costume's files out of its zip into `folder` (emptied first), by what follows its prefix
        static Dictionary<string, string> Unpack(string zipPath, Item item, string folder)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in ZipCostume(zipPath, item))
            {
                string path = Path.Combine(folder, item.Prefix + part.Key);
                File.WriteAllBytes(path, part.Value);
                files[part.Key] = path;
            }
            return files;
        }

        // a costume's files in its zip, by what follows its prefix (".obj.emo", "_01.col.emb" ...)
        static Dictionary<string, byte[]> ZipCostume(string zipPath, Item item)
        {
            var part = new System.Text.RegularExpressions.Regex("^" + item.Prefix + @"(\.(obj\.emo|shd\.emo|nml\.emb|bsr|csb)|_\d\d\.(col\.emb|obj\.emm))$",
                                                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                foreach (var entry in zip.Entries.Where(e => part.IsMatch(e.Name)))
                {
                    string rest = entry.Name.Substring(item.Prefix.Length);
                    if (files.ContainsKey(rest)) continue;
                    using (Stream s = entry.Open())
                    using (var to = new MemoryStream()) { s.CopyTo(to); files[rest] = to.ToArray(); }
                }
            return files;
        }

        /// <summary>Color pictures into a package of the player's: its zip's <prefix>_<CC>.png replaced for the colors made
        /// (the others stay as they are; ready to share), then the package into the game again (a docked file: not, it
        /// stays out of the game).</summary>
        public static void PutPictures(string game, Item item, Installed record, IDictionary<int, Bitmap> made)
        {
            // an installed one goes into the game again after: nothing is changed while the game is open
            if (record.Source != Docked.Source && Game.IsRunning()) throw new InvalidOperationException("close the game first");
            SafeFile.EditZip(record.Package, zip =>   // on a copy, swapped in when done
            {
                foreach (var picture in made)
                {
                    if (!(picture.Key >= 1 && picture.Key <= ColorCount || picture.Key >= 30 && picture.Key <= 99)) continue;   // nothing else can be in a package (Installer.AllowedNames)
                    string entry = item.Prefix + "_" + picture.Key.ToString("D2") + ".png";
                    foreach (var old in zip.Entries.Where(e => string.Equals(e.Name, entry, StringComparison.OrdinalIgnoreCase)).ToList()) old.Delete();
                    using (Stream s = zip.CreateEntry(entry).Open()) picture.Value.Save(s, ImageFormat.Png);
                }
            });
            if (record.Source != Docked.Source) Installer.InstallPackage(game, item, record.Package, "file");
        }

        /// <summary>P1 from the fighters' layer: the first run of columns from the left with anything in them (P2 stands
        /// apart further right; fighters too close: up to the middle), standing at the bottom of a transparent 256 x 384.</summary>
        public static Bitmap Cutout(Bitmap shot)
        {
            using (Bitmap layer = Solid(shot)) return CutoutOf(layer, P1(layer));
        }

        static Bitmap CutoutOf(Bitmap layer, Rectangle crop)
        {
            double scale = Math.Min(PictureWidth * 0.92 / crop.Width, PictureHeight * 0.94 / crop.Height);
            int dw = Math.Max(1, (int)Math.Round(crop.Width * scale)), dh = Math.Max(1, (int)Math.Round(crop.Height * scale));
            var picture = new Bitmap(PictureWidth, PictureHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(picture))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                int bottomMargin = (int)(PictureHeight * 0.03);
                g.DrawImage(layer, new Rectangle((PictureWidth - dw) / 2, PictureHeight - bottomMargin - dh, dw, dh), crop, GraphicsUnit.Pixel);
            }
            return picture;
        }

        /// <summary>P1 at the game's own size (user 2026-10-12: "generate full size"): cut out as it is, with a little
        /// room around, transparent.</summary>
        public static Bitmap FullSize(Bitmap shot)
        {
            using (Bitmap layer = Solid(shot)) return FullSizeOf(layer, P1(layer));
        }

        static Bitmap FullSizeOf(Bitmap layer, Rectangle crop)
        {
            const int room = 12;
            var picture = new Bitmap(crop.Width + room * 2, crop.Height + room * 2, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(picture))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(layer, new Rectangle(room, room, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
            }
            return picture;
        }

        // The fighters' layer with every drawn pixel solid. Some skins' textures leave their alpha empty where they're
        // opaque (user 2026-10-18: Terry Bogard for Ken came out as floating pieces; three of its six textures have no
        // alpha at all): the game ignores it there, but the layer keeps it, so the fighter would be cut away. Where the
        // layer is see-through but its color isn't the background's (the color nearly all its see-through pixels share),
        // the fighter was drawn: made solid. A layer without one background color is left as it is.
        static Bitmap Solid(Bitmap layer)
        {
            int w = layer.Width, h = layer.Height;
            // read as it is (drawing it would go through premultiplied color and lose the color under alpha 0)
            var px = new int[w * h];
            var source = layer.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(source.Scan0 + y * source.Stride, px, y * w, w);
            layer.UnlockBits(source);
            var copy = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = copy.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var counts = new Dictionary<int, int>();
            int clear = 0;
            foreach (int p in px)
            {
                if (((p >> 24) & 0xFF) != 0) continue;
                clear++;
                int rgb = p & 0xFFFFFF, n;
                counts.TryGetValue(rgb, out n);
                counts[rgb] = n + 1;
            }
            if (clear > 0)
            {
                var background = counts.OrderByDescending(c => c.Value).First();
                if (background.Value >= clear * 0.6)
                {
                    int br = (background.Key >> 16) & 0xFF, bg = (background.Key >> 8) & 0xFF, bb = background.Key & 0xFF;
                    for (int i = 0; i < px.Length; i++)
                    {
                        int p = px[i];
                        if (((p >> 24) & 0xFF) > 16) continue;
                        int r = (p >> 16) & 0xFF, gr = (p >> 8) & 0xFF, b = p & 0xFF;
                        if (Math.Abs(r - br) + Math.Abs(gr - bg) + Math.Abs(b - bb) > 24) px[i] = unchecked((int)0xFF000000) | (p & 0xFFFFFF);
                    }
                }
            }
            for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(px, y * w, data.Scan0 + y * data.Stride, w);
            copy.UnlockBits(data);
            return copy;
        }

        // where P1 is in the fighters' layer: of the runs of columns with anything in them (a gap of empty columns apart),
        // the one starting left of the middle with the most drawn in it, so a detached speck left of P1 (Female Shao Kahn's
        // little emblem, user 2026-10-13: it was taken for her) is left out; P2 stands apart further right; fighters too
        // close: up to the middle. As tall as P1.
        static Rectangle P1(Bitmap layer)
        {
            int w = layer.Width, h = layer.Height;
            var px = new int[w * h];
            using (var argb = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(argb)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(layer, 0, 0, w, h); }
                var data = argb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w, w);
                argb.UnlockBits(data);
            }
            Func<int, int, bool> solid = (x, y) => ((px[y * w + x] >> 24) & 0xFF) > 16;
            var mass = new int[w];   // drawn pixels in each column
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (solid(x, y)) mass[x]++;
            var runs = new List<int[]>();   // left, right, drawn pixels
            int start = -1, last = -1, gap = 0, minGap = Math.Max(4, w / 64);
            long drawn = 0;
            for (int x = 0; x <= w; x++)
            {
                if (x < w && mass[x] > 0) { if (start < 0) { start = x; drawn = 0; } last = x; gap = 0; drawn += mass[x]; }
                else if (start >= 0 && (++gap >= minGap || x == w)) { runs.Add(new[] { start, last, (int)Math.Min(drawn, int.MaxValue) }); start = -1; }
            }
            int[] p1 = runs.Where(r => r[0] < w / 2).OrderByDescending(r => r[2]).FirstOrDefault();
            if (p1 == null) throw new InvalidDataException("the picture has no fighter in it");
            int left = p1[0], right = p1[1];
            if (right > w * 0.6) right = Math.Max(left + 1, w / 2);   // no gap (the fighters touch): P1's side of the middle
            int top = h, bottom = -1;
            for (int y = 0; y < h; y++)
                for (int x = left; x <= right; x++)
                    if (solid(x, y)) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); break; }
            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }

        // The game's graphics settings ([DISPLAY] and [GRAPHICS] of config.ini) as a short fingerprint: comparison runs
        // are made again when they change, as the game makes its own benchmark result invalid then.
        static string GraphicsKey()
        {
            if (!File.Exists(ConfigPath)) return "none";
            var kept = new StringBuilder();
            bool inside = false;
            foreach (string line in File.ReadAllLines(ConfigPath))
            {
                string t = line.Trim();
                if (t.StartsWith("[")) inside = t == "[DISPLAY]" || t == "[GRAPHICS]";
                if (inside) kept.AppendLine(t);
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(kept.ToString()))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
        }

        // performance.json beside the program: { "baselines": { key: average }, "results": { item key: {...} },
        // "locked": { item key: { "why", "version", "when" } } }, read once (Locked is asked often) and kept in step.
        static Dictionary<string, object> cache;
        static string cachedFrom;

        static Dictionary<string, object> Load()
        {
            if (cache != null && cachedFrom == ResultsFile) return cache;
            try { cache = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(ResultsFile)); }
            catch (Exception) { cache = null; }
            cache = cache ?? new Dictionary<string, object>();
            cachedFrom = ResultsFile;
            return cache;
        }

        static void Save(Dictionary<string, object> j)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultsFile));
            SafeFile.WriteAllText(ResultsFile, new JavaScriptSerializer().Serialize(j), true);
            cache = j;
            cachedFrom = ResultsFile;
        }

        static Dictionary<string, object> Part(Dictionary<string, object> j, string name)
        {
            object v;
            var part = j.TryGetValue(name, out v) ? v as Dictionary<string, object> : null;
            if (part == null) j[name] = part = new Dictionary<string, object>();
            return part;
        }

        static Dictionary<string, double> Baselines()
        {
            return Part(Load(), "baselines").ToDictionary(p => p.Key, p => Convert.ToDouble(p.Value));
        }

        static void SaveBaseline(string key, double average, bool estimated)
        {
            var j = Load();
            Part(j, "baselines")[key] = Math.Round(average, 2);
            if (estimated) Part(j, "estimated")[key] = true; else Part(j, "estimated").Remove(key);
            Save(j);
        }

        /// <summary>Whether the PC check's result is from the game's speed only (Result.Estimated): 60 there means 60 or more.</summary>
        public static bool PcEstimated() { return Part(Load(), "estimated").ContainsKey(PcKey()); }

        static void SaveResult(string key, Result r)
        {
            var j = Load();
            Part(j, "results")[key] = new Dictionary<string, object>
            {
                { "average", Math.Round(r.Average, 2) }, { "lowest", Math.Round(r.Lowest, 2) }, { "baseline", Math.Round(r.Baseline, 2) },
                { "against", r.Against }, { "when", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) }, { "estimated", r.Estimated },
            };
            Save(j);
        }

        /// <summary>The last test of an item on this PC, or null.</summary>
        public static Result Last(string key)
        {
            object v;
            var r = Part(Load(), "results").TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
            if (r == null) return null;
            try
            {
                return new Result { Average = Convert.ToDouble(r["average"]), Lowest = Convert.ToDouble(r["lowest"]), Baseline = Convert.ToDouble(r["baseline"]),
                                    Against = Convert.ToString(r["against"]), Estimated = r.ContainsKey("estimated") && Convert.ToBoolean(r["estimated"]) };
            }
            catch (Exception) { return null; }
        }

        // an item as the player knows it, for its developer-mode frame: "Stage U01 - Mishima Estate Roof", "Ryu Slot 80 - Manon"
        static string Shown(Item item) { return (item.IsStage ? "" : Fighters.Name(item.Fighter) + " ") + item.Title; }

        // which version of an item failed: a catalog item's fingerprint and version; the same for every copy of a package
        static string Version(Item item) { return (item.Sha256 ?? "") + "|" + (item.Version ?? ""); }

        static void Lock(Item item, string why)
        {
            var j = Load();
            Part(j, "locked")[item.Key] = new Dictionary<string, object>
            {
                { "why", why }, { "version", Version(item) }, { "when", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) },
            };
            Save(j);
        }

        /// <summary>Why this version of an item can't be switched on on this PC (it had a size warning and failed its
        /// test), or null when it can.</summary>
        public static string Locked(Item item)
        {
            object v;
            var r = Part(Load(), "locked").TryGetValue(item.Key, out v) ? v as Dictionary<string, object> : null;
            if (r == null || Convert.ToString(r["version"]) != Version(item)) return null;
            return Convert.ToString(r["why"]);
        }

        /// <summary>A new copy of an item went in (Installer.InstallPackage: an update, a file added again): it may be tried again.</summary>
        public static void Unlock(string key)
        {
            var j = Load();
            if (Part(j, "locked").Remove(key)) Save(j);
        }

        /// <summary>A result in a sentence or two, for the player.</summary>
        public static string Describe(string name, Result r)
        {
            // from the game's speed only: kept full speed (60 or more), or about how fast; nothing to compare (Estimated)
            if (r.Estimated)
                return r.FullSpeed ? name + " kept full speed (60 fps or more) in the game's benchmark. It should play at full speed on this PC."
                                   : name + " ran at about " + r.Average.ToString("0") + " fps in the game's benchmark. It may not keep full speed (60 fps) on this PC.";
            // a costume has nothing to compare with (one run, user 2026-10-13); a stage, the PC check's Blast Furnace
            string compared = r.Baseline <= 0 || string.IsNullOrEmpty(r.Against) ? ""
                            : r.Slower < 5 ? ": about as fast as the game's own " + r.Against + " (" + r.Baseline.ToString("0") + " fps)"
                            : ": " + r.Slower.ToString("0") + "% slower than the game's own " + r.Against + " (" + r.Baseline.ToString("0") + " fps)";
            return name + " ran at " + r.Average.ToString("0") + " fps on average in the game's benchmark" + compared + ". " +
                   (r.FullSpeed ? "It should play at full speed on this PC." : "It may not keep full speed (60 fps) on this PC.");
        }

        sealed class Pending
        {
            public string Stage, Fighter, Config, TheirStage;
            public List<string> Aside;   // hand-placed patch files moved to ex_more_stuff_test_aside for the test
            public int TheirSwap;
            public bool Moved;
            public long Began;   // UTC ticks
        }

        static void Write(string game, Pending p)
        {
            Directory.CreateDirectory(Game.PatchFolder(game));
            SafeFile.WriteAllText(PendingPath(game), new JavaScriptSerializer().Serialize(p), true);
        }

        static Pending Read(string game)
        {
            // the note of a test under way, or the one before it when a crash left it unreadable (so Recover still puts
            // the game's files back)
            try
            {
                string text = SafeFile.ReadList(PendingPath(game), t => { try { new JavaScriptSerializer().Deserialize<Pending>(t); return true; } catch (Exception) { return false; } });
                return text == null ? null : new JavaScriptSerializer().Deserialize<Pending>(text);
            }
            catch (Exception) { return null; }
        }
    }
}
