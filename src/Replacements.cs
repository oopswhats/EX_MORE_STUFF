using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // A game stage replaced by another on this PC: the game finds stages by code, so a converted copy of the other
    // stage (StagePack.Recode) is put in the patch folder under the replaced stage's file names. Only this PC sees
    // it, online too; the music and everything else stay the replaced stage's. Nothing is lost: a file already in
    // the patch folder (the game's own update, or a player's port) is moved to ex_more_stuff_backup first and comes
    // back when the replacement is switched off. Ember needs nothing for this.
    class Replacement
    {
        public string Stage, Source;
        public Dictionary<string, string> Files = new Dictionary<string, string>();   // written file -> its SHA-256
        public List<string> Backups = new List<string>();                             // patch files moved aside
    }

    static class Replacements
    {
        const string ListName = "ex_more_stuff_stages.json", BackupFolder = "ex_more_stuff_backup";

        static string ListPath(string game) { return Path.Combine(Game.PatchFolder(game), ListName); }
        static string BackupPath(string game, string relative) { return Path.Combine(Game.PatchFolder(game), BackupFolder, relative); }

        public static List<Replacement> Load(string game)
        {
            var result = new List<Replacement>();
            if (!File.Exists(ListPath(game))) return result;
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(ListPath(game)));
            object list;
            if (root == null || !root.TryGetValue("replaced", out list) || !(list is System.Collections.ArrayList)) return result;
            foreach (var j in ((System.Collections.ArrayList)list).OfType<Dictionary<string, object>>())
            {
                var r = new Replacement { Stage = Convert.ToString(j["stage"]), Source = Convert.ToString(j["source"]) };
                var files = j.ContainsKey("files") ? j["files"] as Dictionary<string, object> : null;
                if (files != null) foreach (var f in files) r.Files[f.Key] = Convert.ToString(f.Value);
                var backups = j.ContainsKey("backups") ? j["backups"] as System.Collections.ArrayList : null;
                if (backups != null) foreach (object b in backups) r.Backups.Add(Convert.ToString(b));
                result.Add(r);
            }
            return result;
        }

        static void Save(string game, List<Replacement> list)
        {
            if (list.Count == 0) { if (File.Exists(ListPath(game))) File.Delete(ListPath(game)); return; }
            var entries = list.Select(r => new Dictionary<string, object>
            {
                { "stage", r.Stage }, { "source", r.Source }, { "files", r.Files }, { "backups", r.Backups },
            }).ToList();
            File.WriteAllText(ListPath(game), new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "format", 1 }, { "replaced", entries } }));
        }

        // The file a stage is copied from: a game stage's own, as the game shipped it (resource\battle\stage, never a
        // port or replacement in the patch folder), a custom stage's as installed. Since the game's own files are
        // never changed, replacements can't build on each other: Pit Stop 109 <-> Training Stage is a clean swap.
        public static string SourceFile(string game, string code, string suffix)
        {
            if (Array.IndexOf(Stages.Codes, code) < 0) return StagePack.GameFile(game, code, suffix, false);
            string path = Path.Combine(game, "resource", StagePack.RelativePath(code, suffix));
            return File.Exists(path) ? path : null;
        }

        // Why `stage` can't play as `source` (or null; with no source, whether the stage can be replaced at all): their
        // files must be there, and the source needs both.
        public static string Problem(string game, string stage, string source, Func<string, string> name)
        {
            foreach (var check in source == null || source == stage ? new[] { stage } : new[] { stage, source })
            {
                string emz = SourceFile(game, check, ".emz");
                if (emz == null) return name(check) + "'s files weren't found.";
                if (check == source && SourceFile(game, check, ".tex.emz") == null) return name(check) + " has no textures file (STG_" + check + ".tex.emz).";
            }
            return null;
        }

        public static void Replace(string game, string stage, string source)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var list = Load(game);
            if (list.Any(r => r.Stage == stage)) throw new InvalidOperationException("it is already replaced");
            // Both files converted before anything is moved.
            var made = new Dictionary<string, byte[]>();
            foreach (string suffix in StagePack.Suffixes)
            {
                string from = SourceFile(game, source, suffix);
                if (from == null) throw new FileNotFoundException("STG_" + source + suffix + " wasn't found");
                made[StagePack.RelativePath(stage, suffix)] = StagePack.Recode(File.ReadAllBytes(from), source, stage);
            }
            var record = new Replacement { Stage = stage, Source = source };
            try
            {
                foreach (var file in made)
                {
                    string path = Path.Combine(Game.PatchFolder(game), file.Key), backup = BackupPath(game, file.Key);
                    if (File.Exists(path))
                    {
                        if (File.Exists(backup)) throw new IOException("an older copy of " + Path.GetFileName(path) + " is already in " + BackupFolder);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        File.Move(path, backup);
                        record.Backups.Add(file.Key);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, file.Value);
                    record.Files[file.Key] = Installer.Sha256(path);
                }
            }
            catch (Exception)
            {
                Undo(game, record);
                throw;
            }
            list.Add(record);
            Save(game, list);
        }

        // Puts the stage back: its converted files go (unless something else has replaced them since), the moved
        // files come back. Returns what couldn't be put back, or null.
        public static string Restore(string game, string stage)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var list = Load(game);
            Replacement record = list.FirstOrDefault(r => r.Stage == stage);
            if (record == null) return null;
            string problem = Undo(game, record);
            list.Remove(record);
            Save(game, list);
            return problem;
        }

        static string Undo(string game, Replacement record)
        {
            var kept = new List<string>();
            foreach (var file in record.Files)
            {
                string path = Path.Combine(Game.PatchFolder(game), file.Key);
                if (!File.Exists(path)) continue;
                if (Installer.Sha256(path) == file.Value) File.Delete(path);
                else kept.Add(file.Key);                      // changed since: someone else's file now, left alone
            }
            foreach (string relative in record.Backups)
            {
                string path = Path.Combine(Game.PatchFolder(game), relative), backup = BackupPath(game, relative);
                if (!File.Exists(backup) || File.Exists(path)) continue;
                File.Move(backup, path);
            }
            string folder = Path.Combine(Game.PatchFolder(game), BackupFolder);
            try
            {
                foreach (string dir in Directory.Exists(folder) ? Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToArray() : new string[0])
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (IOException) { }
            bool backedUp = kept.Any(k => record.Backups.Contains(k) && File.Exists(BackupPath(game, k)));
            return kept.Count == 0 ? null
                : string.Join(", ", kept.Select(Path.GetFileName)) + " changed since EX More Stuff wrote it, so it was left as it is" +
                  (backedUp ? " (the file it replaced is in " + BackupFolder + ")" : "");
        }
    }
}
