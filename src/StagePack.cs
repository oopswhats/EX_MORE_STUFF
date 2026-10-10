using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    // A stage's two files (STG_<code>.emz with its models and scripts, STG_<code>.tex.emz with its textures) and how
    // one stage becomes another (tools/stage_recode.py, in C#). The game finds a stage by its code: the file names,
    // the entries inside them (TRN_SetupObj.lua, TRN_SetupParam.lua, TRN_CUBE.emb, TRN_BAS.emo ...) and the names
    // its Lua scripts load. So every <old>_ entry is renamed <new>_ and the old code is replaced in the scripts
    // (compiled Lua; all codes have three characters, so the bytecode stays valid). The rest is copied unchanged.
    static class StagePack
    {
        public static readonly string[] Suffixes = { ".emz", ".tex.emz" };

        // Where the game looks for stage files, first match wins (as for every other game file).
        public static readonly string[] Roots = { "patch_ae2_tu3", "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" };

        public static string RelativePath(string code, string suffix) { return Path.Combine("battle", "stage", "STG_" + code + suffix); }

        // The file the game loads for a stage, skipping the patch folder when `skipPatch` (a file there that EX More
        // Stuff wrote itself is not the stage's own).
        public static string GameFile(string game, string code, string suffix, bool skipPatch)
        {
            foreach (string root in Roots.Skip(skipPatch ? 1 : 0))
            {
                string path = Path.Combine(game, root, RelativePath(code, suffix));
                if (File.Exists(path)) return path;
            }
            return null;
        }

        // A stage file under another code. The container keeps its header and alignment; a compressed (#EMZ) file
        // is compressed again.
        public static byte[] Recode(byte[] file, string from, string to)
        {
            if (from.Length != 3 || to.Length != 3) throw new ArgumentException("stage codes are three characters");
            byte[] inner = GameArt.Unpack(file);
            if (inner.Length < 32 || Encoding.ASCII.GetString(inner, 0, 4) != "#EMB") throw new InvalidDataException("not a stage file");
            var entries = GameArt.ReadContainer(inner);
            int align = Alignment(inner, entries);
            byte[] old = Encoding.ASCII.GetBytes(from), now = Encoding.ASCII.GetBytes(to);
            var recoded = new List<KeyValuePair<string, byte[]>>();
            foreach (var entry in entries)
            {
                string name = entry.Key;
                byte[] data = entry.Value;
                if (name.StartsWith(from + "_", StringComparison.OrdinalIgnoreCase)) name = to + name.Substring(from.Length);
                if (name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) data = Replace(data, old, now);
                recoded.Add(new KeyValuePair<string, byte[]>(name, data));
            }
            byte[] body = Build(inner, recoded, align);
            return Encoding.ASCII.GetString(file, 0, 4) == "#EMZ" ? Compress(body) : body;
        }

        // The alignment the container was written with: the first that puts every entry and the name block where
        // they are.
        static int Alignment(byte[] d, List<KeyValuePair<string, byte[]>> entries)
        {
            int count = BitConverter.ToInt32(d, 12), table = BitConverter.ToInt32(d, 24), names = BitConverter.ToInt32(d, 28);
            foreach (int align in new[] { 16, 32, 64, 128, 256, 1024, 2048 })
            {
                long at = 32 + (names != 0 ? 12 : 8) * count;
                bool fits = true;
                for (int i = 0; i < count && fits; i++)
                {
                    at += (align - at % align) % align;
                    int entry = table + i * 8;
                    fits = BitConverter.ToInt32(d, entry) + entry == at;
                    at += entries[i].Value.Length;
                }
                if (fits && names != 0) fits = BitConverter.ToInt32(d, names) == at + (align - at % align) % align;
                if (fits) return align;
            }
            return 64;
        }

        // An #EMB container (the inside of an .emz): the first 12 header bytes kept, the entry count, the (offset,
        // length) list, name pointers, then each entry and the name block on an `align` boundary.
        static byte[] Build(byte[] original, List<KeyValuePair<string, byte[]>> entries, int align)
        {
            int n = entries.Count;
            using (var buffer = new MemoryStream())
            using (var w = new BinaryWriter(buffer))
            {
                w.Write(original, 0, 12);
                w.Write(n); w.Write(0L); w.Write(32); w.Write(32 + 8 * n);
                w.Write(new byte[12 * n]);
                var offsets = new long[n];
                for (int i = 0; i < n; i++)
                {
                    Pad(w, align);
                    offsets[i] = buffer.Position;
                    w.Write(entries[i].Value);
                }
                Pad(w, align);
                var nameAt = new long[n];
                for (int i = 0; i < n; i++)
                {
                    nameAt[i] = buffer.Position;
                    w.Write(Encoding.ASCII.GetBytes(entries[i].Key));
                    w.Write((byte)0);
                }
                for (int i = 0; i < n; i++)
                {
                    buffer.Position = 32 + 8 * i;
                    w.Write((int)(offsets[i] - (32 + 8 * i)));
                    w.Write(entries[i].Value.Length);
                    buffer.Position = 32 + 8 * n + 4 * i;
                    w.Write((int)nameAt[i]);
                }
                w.Flush();
                return buffer.ToArray();
            }
        }

        static void Pad(BinaryWriter w, int align)
        {
            long gap = (align - w.BaseStream.Position % align) % align;
            if (gap > 0) w.Write(new byte[gap]);
        }

        // #EMZ: magic, CRC-32 of the inflated data, inflated size, header size 16, then raw deflate.
        static byte[] Compress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                var w = new BinaryWriter(output);
                w.Write(Encoding.ASCII.GetBytes("#EMZ"));
                w.Write(Crc32(data));
                w.Write(data.Length);
                w.Write(16);
                w.Flush();
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }

        static uint[] crcTable;

        static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[i] = c;
                }
                crcTable = table;
            }
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        static int IndexOf(byte[] data, byte[] find, int start = 0)
        {
            for (int i = start; i <= data.Length - find.Length; i++)
            {
                int k = 0;
                while (k < find.Length && data[i + k] == find[k]) k++;
                if (k == find.Length) return i;
            }
            return -1;
        }

        // The code where it starts a name ("TRN_BAS.emo", "@TRN_SetupObj.lua", or a string of its own), not inside
        // one: "TRN_INDTRAVELER0" holds "IND", which is part of Training Stage's traveller, not Exciting Street
        // Scene's code.
        public static bool StartsName(byte[] data, int at, int length)
        {
            bool before = at == 0 || !char.IsLetterOrDigit((char)data[at - 1]);
            int next = at + length < data.Length ? data[at + length] : 0;
            return before && (next == '_' || next == 0);
        }

        static byte[] Replace(byte[] data, byte[] find, byte[] with)
        {
            byte[] copy = null;
            for (int at = IndexOf(data, find); at >= 0; at = IndexOf(data, find, at + find.Length))
            {
                if (!StartsName(data, at, find.Length)) continue;
                if (copy == null) copy = (byte[])data.Clone();
                Buffer.BlockCopy(with, 0, copy, at, with.Length);
            }
            return copy ?? data;
        }
    }
}
