using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // A player's own package moved to another stage code (C71 -> D05), costume slot (JHA_71 -> JHA_72) or color number
    // (RYU_01_30 -> RYU_01_31): its zip is
    // rewritten in place (files renamed; a stage's STG_ files re-coded inside, as for replaced game stages), so it's
    // ready to share as it is, and what EX More Stuff has of it follows: in the game, it's put in again under the new
    // code (its name kept); switched off, its listing changes; songs put on the stage on the Music page move with it.
    static class CodeChange
    {
        /// <summary>Why `record` can't move to `code` (a stage) or `slot` (a costume's slot, a color's number), or null.</summary>
        public static string Problem(string game, Installed record, string code, int slot)
        {
            Item item = record.Item;
            if (record.Source != "file" || string.IsNullOrEmpty(record.Package)) return "only your own packages (added from a zip) can change their code";
            if (!File.Exists(record.Package)) return "its zip isn't at " + record.Package + " any more";
            Item moved = Moved(item, code, slot);
            if (item.IsStage)
            {
                if (!Regex.IsMatch(code ?? "", "^[A-Z0-9]{3}$")) return "a stage code is three capital letters or digits";
                if (!Stages.IsCustomCode(code)) return code + " is one of the game's own stage codes";
                if (code == item.Code) return "that's its code already";
            }
            else if (item.IsColor)
            {
                if (slot < Catalog.FirstCustomColor || slot > Catalog.LastColor) return "a new color is " + Catalog.FirstCustomColor + " to " + Catalog.LastColor;
                if (slot == item.Color) return "that's its color already";
            }
            else
            {
                if (slot < Catalog.FirstCustomCostume || slot > Catalog.LastSlot) return "a costume slot is " + Catalog.FirstCustomCostume + " to " + Catalog.LastSlot;
                if (slot == item.Slot) return "that's its slot already";
            }
            var taken = Installer.Load(game).Concat(Installer.Removed(game)).FirstOrDefault(r => r.Item.Key == moved.Key);
            if (taken != null) return (item.IsStage ? code : (item.IsColor ? "color " : "slot ") + slot) + " is already used by " + taken.Item.TitleNamed(taken.Shown ?? taken.Item.Name);
            string folder = Game.FolderFor(game, moved);
            if (File.Exists(Path.Combine(folder, moved.Prefix + (item.IsStage ? ".emz" : item.IsColor ? ".col.emb" : ".obj.emo"))))
                return "the game folder already has " + moved.Prefix + " files that EX More Stuff didn't put there";
            if (item.IsStage)
                foreach (string round in new[] { "", "2", "3" })
                {
                    string file = "BGM_" + code + round + ".csb";
                    if (File.Exists(MusicBank.PatchFile(game, file))) return "the game folder already has " + file;
                }
            if (Game.IsRunning()) return "close the game first";
            return null;
        }

        /// <summary>Moves the package (check Problem first). Returns notes on songs that couldn't move, or null.</summary>
        public static string Apply(string game, Installed record, string code, int slot)
        {
            Item item = record.Item, moved = Moved(item, code, slot);
            string was = record.Package + ".was";   // the zip as it was, until everything has moved
            Rewrite(record.Package, item, moved, was);
            try { Installer.Moved(game, record, moved); }
            catch
            {
                File.Replace(was, record.Package, null);
                throw;
            }
            File.Delete(was);
            return item.IsStage ? MusicBank.MoveCode(game, item.Code, code) : null;
        }

        static Item Moved(Item item, string code, int slot)
        {
            Item moved = Item.FromJson(item.ToJson());
            if (item.IsStage) moved.Code = code; else if (item.IsColor) moved.Color = slot; else moved.Slot = slot;
            return moved;
        }

        // the zip with every file of `from` renamed to `to` (a stage's STG_*.emz re-coded inside); written whole
        // beside it, then put in its place, the old one kept as `was`
        static void Rewrite(string zipPath, Item from, Item to, string was)
        {
            var files = new List<KeyValuePair<string, byte[]>>();
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    using (Stream s = e.Open())
                    using (var data = new MemoryStream())
                    {
                        s.CopyTo(data);
                        files.Add(new KeyValuePair<string, byte[]>(e.FullName, data.ToArray()));
                    }
                }
            string temp = zipPath + ".tmp";
            using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var file in files)
                {
                    string name = Rename(file.Key, from, to);
                    byte[] data = file.Value;
                    if (from.IsStage && name.StartsWith("STG_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".emz", StringComparison.OrdinalIgnoreCase))
                        data = StagePack.Recode(data, from.Code, to.Code);
                    using (Stream s = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) s.Write(data, 0, data.Length);
                }
            if (File.Exists(was)) File.Delete(was);
            File.Replace(temp, zipPath, was);
        }

        // STG_C71.emz -> STG_D05.emz, BGM_C712.csb -> BGM_D052.csb, JHA_71_03.col.emb -> JHA_72_03.col.emb; anything
        // else stays as it is
        static string Rename(string name, Item from, Item to)
        {
            if (from.IsStage)
            {
                foreach (string kind in new[] { "STG_", "BGM_" })
                    if (name.StartsWith(kind + from.Code, StringComparison.OrdinalIgnoreCase))
                        return kind + to.Code + name.Substring(kind.Length + from.Code.Length);
                return name;
            }
            return name.StartsWith(from.Prefix, StringComparison.OrdinalIgnoreCase) ? to.Prefix + name.Substring(from.Prefix.Length) : name;
        }
    }
}
