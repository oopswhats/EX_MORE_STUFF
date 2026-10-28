using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    // The costume check as a costume comes into EX More Stuff (user 2026-10-22: "can you create a solid validator on
    // install"; docs\EXMS_COSTUME_CHECK_ON_INSTALL.md): Browse Mods' Install and Update (1-Click too), Add Mod From File,
    // Fix a folder of mods and docking. Nothing is asked (user 2026-10-27): a FAULTY costume is fixed when it can be, else
    // it goes in switched off and can't be switched on (players were desyncing with ones taken as they were); its
    // warning shows on its card and in the footer's list. Costumes that came in before are checked once each.
    static class InstallCheck
    {
        // where a player sees the locked costumes, as messages name it (the footer's button, MainForm)
        public const string ListButton = "Warnings, next to Apply changes";
        // in every message about a costume switched off by the check (Fix a folder of mods counts those apart)
        public const string CantBeFixed = "problems that can't be fixed";

        // what makes a costume FAULTY: the check's faults, and locator bones that differ from costume 01, the stand-in
        static List<CostumeCheck.Finding> Faults(CostumeCheck.Result r)
        {
            return r.Findings.Where(f => CostumeCheck.Faults.Contains(f.Kind) ||
                                         ((f.Kind == "DESYNC" || f.Kind == "DESYNC?" || f.Kind == "IK") && f.Bones > 0 && f.Text.StartsWith("vs stand-in 01"))).ToList();
        }

        static string Fighter(CostumeCheck.Result r)
        {
            string code = r.Name.Length >= 3 ? r.Name.Substring(0, 3) : r.Name;
            return Fighters.Name(code);
        }

        // a costume's faults, in a player's words
        static IEnumerable<string> Plain(CostumeCheck.Result r)
        {
            var faults = Faults(r);
            string fighter = Fighter(r);
            var moved = faults.Where(f => f.Kind == "DESYNC" || f.Kind == "DESYNC?").ToList();
            int bones = moved.Sum(f => f.Bones);
            if (moved.Count > 0)
                yield return (bones == 1 ? "1 of " + fighter + "'s bones is" : bones + " of " + fighter + "'s bones are") + " moved in this costume. That changes where hits land, so online matches " +
                             "against players without it go out of sync." + (moved.All(f => f.Fixable) ? " Fixing puts them back where " + fighter + "'s are: it looks the same " +
                             "standing still, but its arms and neck may bend a little differently." : "");
            if (faults.Any(f => f.Kind == "IK"))
                yield return "Its arms and legs bend differently from " + fighter + "'s, so online matches against players without it may go out of sync.";
            if (faults.Any(f => f.Kind == "BROKEN")) yield return "Its model is damaged and might crash the game.";
            else if (faults.Any(f => f.Kind == "PALETTE" || f.Kind == "SUBMESHES")) yield return "Its model is built in a way the game can't take and might crash the game.";
            if (faults.Any(f => f.Kind == "FILES")) yield return "It's missing files the game loads with every costume. Fixing takes them from " + fighter + "'s first costume.";
            if (faults.Any(f => f.Kind == "EMM>CAP")) yield return "Some of its colors have more materials than its model takes, so they show without textures.";
            // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
            if (faults.Any(f => f.Kind == "COLORS")) yield return "It doesn't have all ten colors, so rollback games won't offer it.";
            if (faults.Any(f => f.Kind == "SLOT")) yield return "Its costume number isn't 08-99, so rollback games won't offer it.";   // ROLLBACK-NAME
            if (faults.Any(f => f.Kind == "UNREAD")) yield return "It couldn't be read, so it wasn't checked.";
        }

        static string Describe(List<CostumeCheck.Result> faulty)
        {
            return string.Join("\n\n", faulty.Select(r => (faulty.Count > 1 && r.Name.Length == 6 ? Fighter(r) + " " + r.Name.Substring(4) + ":\n" : "") + string.Join("\n\n", Plain(r))));
        }

        // what a fix did, for the message
        static string Fixed(List<CostumeCheck.Result> faulty)
        {
            var said = new List<string>();
            foreach (var r in faulty)
            {
                var faults = Faults(r);
                if (faults.Any(f => f.Kind == "DESYNC" || f.Kind == "DESYNC?")) said.Add("its bones put back where " + Fighter(r) + "'s are");
                if (faults.Any(f => f.Kind == "FILES")) said.Add("its missing files taken from " + Fighter(r) + "'s first costume");
            }
            return "fixed: " + string.Join("; ", said.Distinct());
        }

        /// <summary>What goes in for the costumes in `zip` (user 2026-10-27: "disable them. not allow users to use them anyway",
        /// players were desyncing with costumes taken as they were): nothing is asked. A FAULTY costume is fixed when every
        /// fault can be; `own`: the zip is EX More Stuff's own copy, so the fix goes into it, else the fixed copy goes beside
        /// it ("&lt;name&gt; - fixed.zip") and the zip stays as it is. Returns the zip or the fixed copy. `said`: what a fix
        /// did, else null. `warning`: why it can't be used (it can't be fixed, or its fix failed), in a player's words, else
        /// null: the caller switches it off and keeps it locked (SetWarning).</summary>
        public static string Decide(string game, string zip, bool own, out string said, out string warning)
        {
            said = null;
            warning = null;
            List<CostumeCheck.Result> faulty;
            try { faulty = CostumeCheck.Check(game, CostumeCheck.FromZip(zip)).Where(r => r.Verdict == CostumeCheck.Verdict.Faulty).ToList(); }
            catch (Exception) { return zip; }   // the check itself failed: not held against the costume
            if (faulty.Count == 0) return zip;
            if (!faulty.All(r => Faults(r).All(f => f.Fixable)))
            {
                warning = Describe(faulty);
                return zip;
            }
            string folder = Path.GetDirectoryName(Path.GetFullPath(zip));
            string target = AppFolders.PlaceFor(folder, Path.GetFileNameWithoutExtension(zip) + " - fixed.zip", null);
            try
            {
                CostumeCheck.Fix(game, CostumeCheck.FromZip(zip), new List<string>(), target);
                if (own)
                {
                    File.Copy(target, zip, true);
                    File.Delete(target);
                    target = zip;
                }
                said = Fixed(faulty);
                return target;
            }
            catch (Exception ex)
            {
                try { if (target != zip && File.Exists(target)) File.Delete(target); } catch (Exception) { }   // a half-made copy
                warning = "It couldn't be fixed (" + ex.Message + ").\n\n" + Describe(faulty);
                return zip;
            }
        }

        // ---- a costume that can't be fixed: switched off and locked, its warning on its card and in the footer's list ----

        static readonly object warningsLock = new object();

        static string WarningsFile { get { return AppFolders.DataFile("costume_warnings.txt"); } }

        // "item key<TAB>warning" lines, the warning's line breaks as \n
        static Dictionary<string, string> Warnings()
        {
            var warnings = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(WarningsFile))
                    foreach (string line in File.ReadAllLines(WarningsFile))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab > 0) warnings[line.Substring(0, tab)] = line.Substring(tab + 1).Replace("\\n", "\n");
                    }
            }
            catch (Exception) { }   // unreadable: none locked
            return warnings;
        }

        /// <summary>Why a costume (its Item.Key) can't be used, or null.</summary>
        public static string Warning(string itemKey)
        {
            string w;
            lock (warningsLock) return Warnings().TryGetValue(itemKey, out w) ? w : null;
        }

        /// <summary>Every locked costume's key and warning.</summary>
        public static Dictionary<string, string> AllWarnings()
        {
            lock (warningsLock) return Warnings();
        }

        /// <summary>Locks a costume with its warning, or unlocks it (null: a version of it that's fine or fixed came in).</summary>
        public static void SetWarning(string itemKey, string warning)
        {
            lock (warningsLock)
            {
                try
                {
                    var warnings = Warnings();
                    if (warning == null && !warnings.Remove(itemKey)) return;
                    if (warning != null) warnings[itemKey] = warning;
                    File.WriteAllLines(WarningsFile, warnings.Select(w => w.Key + "\t" + w.Value.Replace("\r", "").Replace("\n", "\\n")));
                }
                catch (Exception) { }   // not kept: it isn't locked
            }
        }

        // ---- the costumes that came in before this check, checked once each (user 2026-10-27) ----

        static string CheckedFile { get { return AppFolders.DataFile("costume_checked.txt"); } }

        // a package as checked: its key, size and time, so a new version of it is checked again
        static string Stamp(Installed r)
        {
            var f = new FileInfo(r.Package);
            return r.Item.Key + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks;
        }

        static HashSet<string> Checked()
        {
            try { return File.Exists(CheckedFile) ? new HashSet<string>(File.ReadAllLines(CheckedFile), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal); }
            catch (Exception) { return new HashSet<string>(StringComparer.Ordinal); }
        }

        /// <summary>The player's own costume packages (in the game or switched off) not checked yet in this version.</summary>
        public static List<Installed> Unchecked(string game)
        {
            var done = Checked();
            return Installer.Load(game).Concat(Installer.Removed(game))
                .Where(r => r.Item.IsCostume && r.Source == "file" && r.Package != null && File.Exists(r.Package) && Warning(r.Item.Key) == null)
                .Where(r => { try { return !done.Contains(Stamp(r)); } catch (Exception) { return false; } })
                .GroupBy(r => r.Item.Key).Select(g => g.First()).ToList();
        }

        static void MarkChecked(Item item, string package)
        {
            try { File.AppendAllLines(CheckedFile, new[] { Stamp(new Installed { Item = item, Package = package }) }); }
            catch (Exception) { }   // checked again next time
        }

        /// <summary>Whether a costume that came in before the check is FAULTY: only reads its package. One that's fine (or
        /// can't be read) isn't looked at again.</summary>
        public static bool Faulty(string game, Installed r)
        {
            bool bad;
            try { bad = CostumeCheck.Check(game, CostumeCheck.FromZip(r.Package)).Any(x => x.Verdict == CostumeCheck.Verdict.Faulty); }
            catch (Exception) { bad = false; }   // the check itself failed: not held against the costume
            if (!bad) MarkChecked(r.Item, r.Package);
            return bad;
        }

        /// <summary>One costume that came in before the check: fixed in its package (and in the game, if it's in), or locked
        /// and taken out of the game when it can't be. The game must be closed. Returns its warning when it's locked now.</summary>
        public static string CheckInstalled(string game, Installed r)
        {
            string said, warning;
            string use = Decide(game, r.Package, !r.Theirs, out said, out warning);
            bool inGame = Installer.Load(game).Any(x => x.Item.Key == r.Item.Key);
            if (warning != null)
            {
                SetWarning(r.Item.Key, warning);
                if (inGame)
                {
                    Installer.Remove(game, r.Item.Key);
                    foreach (var s in CostumeSwaps.Load(game).Where(s => s.Fighter == r.Item.Fighter && s.Source == r.Item.Slot).ToList()) CostumeSwaps.Restore(game, s.Fighter, s.Costume);
                }
            }
            else if (said != null && inGame) Installer.InstallPackage(game, r.Item, use, "file", use == r.Package && r.Theirs);   // its fixed files into the game
            MarkChecked(r.Item, use);
            return warning;
        }
    }
}
