using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExMoreStuff
{
    // RAR5 archives ("Rar!\x1a\x07\x01\x00") read by EX More Stuff itself (user 2026-10-29: RedWarriorP's Seth colossal titan and
    // Juri cosplay spinela are RAR5, and a player's Windows had no tar to fall back on: "needs Windows' own tar"). RAR 5.0's
    // unpacking (Huffman-coded LZ with its filters: delta, E8, E8E9, ARM) and stored files, solid or not, each file checked
    // against its CRC. Anything else (RAR 7's larger dictionaries, passwords, volumes) is NotSupportedException, and the caller
    // tries Windows' tar instead. Written from the format as unrar's public source and libarchive (BSD licence) describe it;
    // nothing in an archive is run.
    static class Rar5
    {
        static readonly byte[] Signature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };

        /// <summary>A RAR5 archive.</summary>
        public static bool Is(string path)
        {
            try
            {
                using (var s = File.OpenRead(path))
                {
                    var head = new byte[8];
                    if (s.Read(head, 0, 8) < 8) return false;
                    for (int i = 0; i < Signature.Length; i++) if (head[i] != Signature[i]) return false;
                    return true;
                }
            }
            catch (IOException) { return false; }
        }

        sealed class Entry
        {
            public string Name;
            public long DataAt, PackSize, UnpSize;
            public uint Crc;
            public bool HasCrc, Solid, Directory, Link;
            public int Version, Method;
        }

        /// <summary>The files `keep` wants (by their path inside, with '/'), in the archive's order.</summary>
        public static List<KeyValuePair<string, byte[]>> Read(string path, Func<string, bool> keep)
        {
            byte[] data = File.ReadAllBytes(path);
            var entries = Entries(data);
            var result = new List<KeyValuePair<string, byte[]>>();
            Unpacker unpacker = null;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.Directory || e.Link) continue;
                bool wanted = keep(e.Name) && e.UnpSize <= 512L * 1024 * 1024;
                byte[] bytes;
                if (e.PackSize > int.MaxValue || e.DataAt + e.PackSize > data.Length) throw new NotSupportedException("too big or damaged");
                if (e.Method == 0)   // stored
                {
                    if (!wanted) continue;
                    if (e.PackSize != e.UnpSize) throw new NotSupportedException("a damaged stored file");
                    bytes = new byte[e.UnpSize];
                    Buffer.BlockCopy(data, (int)e.DataAt, bytes, 0, bytes.Length);
                }
                else
                {
                    if (e.Version != 0) throw new NotSupportedException("RAR 7 packing");
                    // in a solid archive every file goes through the unpacker in turn (each continues from the one before)
                    if (!wanted && !NextIsSolid(entries, i)) continue;
                    if (e.UnpSize > int.MaxValue) throw new NotSupportedException("too big");
                    if (unpacker == null || !e.Solid) unpacker = new Unpacker();
                    bytes = unpacker.Unpack(data, (int)e.DataAt, (int)e.PackSize, (int)e.UnpSize);
                    if (!wanted) continue;
                }
                if (e.HasCrc && Rar.Crc32(bytes) != e.Crc) throw new NotSupportedException("its CRC doesn't match (" + e.Name + ")");
                result.Add(new KeyValuePair<string, byte[]>(e.Name, bytes));
            }
            return result;
        }

        static bool NextIsSolid(List<Entry> entries, int i)
        {
            for (int j = i + 1; j < entries.Count; j++) if (!entries[j].Directory && !entries[j].Link) return entries[j].Solid && entries[j].Method != 0;
            return false;
        }

        // RAR5's variable-length number: 7 bits a byte, lowest first, the top bit set while more follow
        static ulong VInt(byte[] d, ref long at, long end)
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (at >= end) throw new NotSupportedException("a damaged header");
                byte b = d[at++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
            }
            throw new NotSupportedException("a damaged header");
        }

        static uint U32(byte[] d, long at) { return (uint)(d[at] | d[at + 1] << 8 | d[at + 2] << 16 | d[at + 3] << 24); }

        static List<Entry> Entries(byte[] d)
        {
            var list = new List<Entry>();
            long pos = Signature.Length;
            while (pos + 7 <= d.Length)
            {
                long at = pos + 4;   // past the header's CRC
                long size = (long)VInt(d, ref at, d.Length);
                long start = at, end = at + size;
                if (size < 2 || end > d.Length) break;
                int type = (int)VInt(d, ref at, end);
                ulong flags = VInt(d, ref at, end);
                long extra = (flags & 1) != 0 ? (long)VInt(d, ref at, end) : 0;
                long dataSize = (flags & 2) != 0 ? (long)VInt(d, ref at, end) : 0;
                if ((flags & 0x18) != 0) throw new NotSupportedException("a file split over parts");
                if (type == 1)   // the archive's own header
                {
                    if ((VInt(d, ref at, end) & 1) != 0) throw new NotSupportedException("a multi-part archive");
                }
                else if (type == 4) throw new NotSupportedException("a password");   // its headers are encrypted
                else if (type == 2)   // a file
                {
                    ulong fileFlags = VInt(d, ref at, end);
                    var e = new Entry { DataAt = end, PackSize = dataSize, Directory = (fileFlags & 1) != 0 };
                    e.UnpSize = (long)VInt(d, ref at, end);
                    VInt(d, ref at, end);   // attributes
                    if ((fileFlags & 2) != 0) at += 4;   // modified time
                    if ((fileFlags & 4) != 0) { if (at + 4 > end) break; e.Crc = U32(d, at); e.HasCrc = true; at += 4; }
                    ulong compression = VInt(d, ref at, end);
                    e.Version = (int)(compression & 0x3F);
                    e.Solid = (compression & 0x40) != 0;
                    e.Method = (int)((compression >> 7) & 7);
                    VInt(d, ref at, end);   // the system it was packed on
                    int nameSize = (int)VInt(d, ref at, end);
                    if (at + nameSize > end) break;
                    e.Name = Encoding.UTF8.GetString(d, (int)at, nameSize).Replace('\\', '/');
                    // the extra records: a password, or a link (no data of its own)
                    long record = end - extra;
                    while (extra > 0 && record < end)
                    {
                        long recordSize = (long)VInt(d, ref record, end);
                        long next = record + recordSize;
                        int recordType = (int)VInt(d, ref record, end);
                        if (recordType == 1) throw new NotSupportedException("a password");
                        if (recordType == 5) e.Link = true;
                        if (recordSize <= 0 || next > end) break;
                        record = next;
                    }
                    if ((fileFlags & 8) != 0) throw new NotSupportedException("a file of unknown size");
                    list.Add(e);
                }
                else if (type == 5) break;   // the end
                pos = end + dataSize;
            }
            return list;
        }

        // A canonical Huffman code from its code lengths (0: unused), decoded by looking up the next 15 bits.
        sealed class Huffman
        {
            readonly short[] symbols = new short[1 << 15];
            readonly byte[] lengths = new byte[1 << 15];

            public Huffman(byte[] table, int start, int count)
            {
                var counts = new int[16];
                for (int i = 0; i < count; i++) counts[table[start + i]]++;
                counts[0] = 0;
                var next = new int[16];
                int code = 0;
                for (int len = 1; len < 16; len++) { code = (code + counts[len - 1]) << 1; next[len] = code; }
                for (int i = 0; i < symbols.Length; i++) symbols[i] = -1;
                for (int i = 0; i < count; i++)
                {
                    int len = table[start + i];
                    if (len == 0) continue;
                    int c = next[len]++;
                    if (c >= 1 << len) throw new NotSupportedException("damaged packed data");
                    int from = c << (15 - len), to = (c + 1) << (15 - len);
                    for (int k = from; k < to; k++) { symbols[k] = (short)i; lengths[k] = (byte)len; }
                }
            }

            public int Decode(Bits bits)
            {
                int key = (int)bits.Peek(15), symbol = symbols[key];
                if (symbol < 0) throw new NotSupportedException("damaged packed data");
                bits.Skip(lengths[key]);
                return symbol;
            }
        }

        // Bits read from the front of a byte array, the first bit the highest; past its end, zeros.
        sealed class Bits
        {
            readonly byte[] buf;
            readonly long end;
            public long Position;   // in bits

            public Bits(byte[] buf, int start, int length) { this.buf = buf; Position = (long)start * 8; end = (long)start + length; }

            public long Byte { get { return Position >> 3; } }

            public uint Peek(int n)   // up to 32
            {
                long b = Position >> 3;
                ulong window = 0;
                for (int i = 0; i < 5; i++) window = (window << 8) | (b + i < end ? buf[b + i] : (byte)0);
                return (uint)((window >> (40 - (int)(Position & 7) - n)) & ((1UL << n) - 1));
            }

            public void Skip(int n) { Position += n; }

            public uint Take(int n) { uint v = n == 0 ? 0 : Peek(n); Position += n; return v; }

            public void Align() { Position = (Position + 7) & ~7L; }
        }

        sealed class Filter
        {
            public int Type, Channels;
            public long Start, Length;   // in the unpacked stream
        }

        // RAR 5.0's unpacker (unrar's Unpack5): its state lives on from one file to the next in a solid archive. Everything
        // unpacked is kept (a mod's archive is small), so a distance always reaches back into what came before.
        sealed class Unpacker
        {
            const int NC = 306, DC = 64, LDC = 16, RC = 44, TableSize = NC + DC + LDC + RC, BC = 20;
            byte[] window = new byte[1 << 20];
            long done;   // bytes unpacked so far (all files of a solid run)
            readonly long[] oldDist = { -1, -1, -1, -1 };
            int lastLength;
            Huffman ld, dd, ldd, rd;

            void Put(byte b)
            {
                if (done == window.Length) Grow(done + 1);
                window[done++] = b;
            }

            void Grow(long need)
            {
                long size = window.Length;
                while (size < need) size *= 2;
                if (size > int.MaxValue) throw new NotSupportedException("too big");
                Array.Resize(ref window, (int)size);
            }

            void Copy(int length, long distance)
            {
                if (distance <= 0 || distance > done) throw new NotSupportedException("damaged packed data");
                if (done + length > window.Length) Grow(done + length);
                long from = done - distance;
                for (int i = 0; i < length; i++) window[done + i] = window[from + i];
                done += length;
            }

            public byte[] Unpack(byte[] data, int start, int packSize, int unpSize)
            {
                long fileStart = done;
                var filters = new List<Filter>();
                var bits = new Bits(data, start, packSize);
                long blockEnd;   // in bits
                bool last;
                ReadBlock(bits, out blockEnd, out last);
                if (ld == null) throw new NotSupportedException("damaged packed data");
                while (true)
                {
                    while (bits.Position >= blockEnd)
                    {
                        if (last) goto finished;
                        ReadBlock(bits, out blockEnd, out last);
                    }
                    if (bits.Byte >= (long)start + packSize) throw new NotSupportedException("its packed data ended early");
                    int main = ld.Decode(bits);
                    if (main < 256) { Put((byte)main); continue; }
                    if (main >= 262)
                    {
                        int length = Length(bits, main - 262);
                        int distSlot = dd.Decode(bits), dBits;
                        long distance = 1;
                        if (distSlot < 4) { dBits = 0; distance += distSlot; }
                        else { dBits = distSlot / 2 - 1; distance += (long)(2 | (distSlot & 1)) << dBits; }
                        if (dBits > 0)
                        {
                            if (dBits >= 4)
                            {
                                if (dBits > 4) distance += (long)bits.Take(dBits - 4) << 4;
                                distance += ldd.Decode(bits);
                            }
                            else distance += bits.Take(dBits);
                        }
                        if (distance > 0x100) { length++; if (distance > 0x2000) { length++; if (distance > 0x40000) length++; } }
                        oldDist[3] = oldDist[2]; oldDist[2] = oldDist[1]; oldDist[1] = oldDist[0]; oldDist[0] = distance;
                        lastLength = length;
                        Copy(length, distance);
                        continue;
                    }
                    if (main == 256)
                    {
                        var f = new Filter();
                        long blockStart = FilterNumber(bits);
                        f.Length = FilterNumber(bits);
                        f.Type = (int)bits.Take(3);
                        if (f.Type == 0) f.Channels = (int)bits.Take(5) + 1;
                        f.Start = done + blockStart;
                        if (f.Length > 0 && f.Length <= 0x400000) filters.Add(f);
                        continue;
                    }
                    if (main == 257)
                    {
                        if (lastLength != 0) Copy(lastLength, oldDist[0]);
                        continue;
                    }
                    // 258-261: one of the last four distances again
                    int n = main - 258;
                    long again = oldDist[n];
                    for (int k = n; k > 0; k--) oldDist[k] = oldDist[k - 1];
                    oldDist[0] = again;
                    int repeat = Length(bits, rd.Decode(bits));
                    lastLength = repeat;
                    Copy(repeat, again);
                }
            finished:
                if (done - fileStart < unpSize) throw new NotSupportedException("its packed data ended early");
                var output = new byte[unpSize];
                Buffer.BlockCopy(window, (int)fileStart, output, 0, unpSize);
                foreach (Filter f in filters) Apply(f, output, f.Start - fileStart);
                return output;
            }

            static int Length(Bits bits, int slot)
            {
                int lBits, length = 2;
                if (slot < 8) { lBits = 0; length += slot; }
                else { lBits = slot / 4 - 1; length += (4 | (slot & 3)) << lBits; }
                if (lBits > 0) length += (int)bits.Take(lBits);
                return length;
            }

            static long FilterNumber(Bits bits)
            {
                int count = (int)bits.Take(2) + 1;
                long value = 0;
                for (int i = 0; i < count; i++) value += (long)bits.Take(8) << (i * 8);
                return value;
            }

            // a block's header (byte aligned: its flags, a check, its size) and its Huffman tables when it has them
            void ReadBlock(Bits bits, out long blockEnd, out bool last)
            {
                bits.Align();
                int flags = (int)bits.Take(8), saved = (int)bits.Take(8);
                int byteCount = ((flags >> 3) & 3) + 1;
                if (byteCount == 4) throw new NotSupportedException("damaged packed data");
                int size = 0;
                for (int i = 0; i < byteCount; i++) size += (int)bits.Take(8) << (i * 8);
                if ((byte)(0x5A ^ flags ^ size ^ (size >> 8) ^ (size >> 16)) != saved) throw new NotSupportedException("damaged packed data");
                long blockStart = bits.Byte;
                blockEnd = (blockStart + size - 1) * 8 + (flags & 7) + 1;
                last = (flags & 0x40) != 0;
                if ((flags & 0x80) != 0) ReadTables(bits);
            }

            void ReadTables(Bits bits)
            {
                var bitLength = new byte[BC];
                for (int i = 0; i < BC; i++)
                {
                    int length = (int)bits.Take(4);
                    if (length == 15)
                    {
                        int zeros = (int)bits.Take(4);
                        if (zeros == 0) bitLength[i] = 15;
                        else { zeros += 2; while (zeros-- > 0 && i < BC) bitLength[i++] = 0; i--; }
                    }
                    else bitLength[i] = (byte)length;
                }
                var bd = new Huffman(bitLength, 0, BC);
                var table = new byte[TableSize];
                for (int i = 0; i < TableSize;)
                {
                    int number = bd.Decode(bits);
                    if (number < 16) table[i++] = (byte)number;
                    else
                    {
                        int n = number % 2 == 0 ? (int)bits.Take(3) + 3 : (int)bits.Take(7) + 11;
                        if (number < 18)
                        {
                            if (i == 0) throw new NotSupportedException("damaged packed data");
                            while (n-- > 0 && i < TableSize) { table[i] = table[i - 1]; i++; }
                        }
                        else while (n-- > 0 && i < TableSize) table[i++] = 0;
                    }
                }
                ld = new Huffman(table, 0, NC);
                dd = new Huffman(table, NC, DC);
                ldd = new Huffman(table, NC + DC, LDC);
                rd = new Huffman(table, NC + DC + LDC, RC);
            }

            // a filter on the file's bytes from `at` (unrar's ApplyFilter; `at` is also the block's place in the file)
            static void Apply(Filter f, byte[] output, long at)
            {
                if (at < 0 || at >= output.Length) return;
                int length = (int)Math.Min(f.Length, output.Length - at), o = (int)at;
                switch (f.Type)
                {
                    case 0:   // delta: each channel's bytes stored one after another
                    {
                        var source = new byte[length];
                        Buffer.BlockCopy(output, o, source, 0, length);
                        int src = 0;
                        for (int channel = 0; channel < f.Channels; channel++)
                        {
                            byte previous = 0;
                            for (int dest = channel; dest < length; dest += f.Channels) output[o + dest] = previous -= source[src++];
                        }
                        break;
                    }
                    case 1:   // E8 and E8E9: x86 calls and jumps back to relative addresses
                    case 2:
                    {
                        const uint fileSize = 0x1000000;
                        byte second = f.Type == 2 ? (byte)0xE9 : (byte)0xE8;
                        for (int pos = 0; pos + 4 < length;)
                        {
                            byte b = output[o + pos++];
                            if (b != 0xE8 && b != second) continue;
                            uint offset = (uint)((pos + at) % fileSize), addr = U32(output, o + pos);
                            if ((addr & 0x80000000) != 0) { if (((addr + offset) & 0x80000000) == 0) Put4(output, o + pos, addr + fileSize); }
                            else if (((addr - fileSize) & 0x80000000) != 0) Put4(output, o + pos, addr - offset);
                            pos += 4;
                        }
                        break;
                    }
                    case 3:   // ARM: BL instructions back to relative addresses
                        for (int pos = 0; pos + 3 < length; pos += 4)
                        {
                            int d = o + pos;
                            if (output[d + 3] != 0xEB) continue;
                            uint offset = (uint)(output[d] | output[d + 1] << 8 | output[d + 2] << 16) - (uint)((at + pos) / 4);
                            output[d] = (byte)offset; output[d + 1] = (byte)(offset >> 8); output[d + 2] = (byte)(offset >> 16);
                        }
                        break;
                    default: throw new NotSupportedException("a filter that isn't one of RAR's standard ones");
                }
            }

            static void Put4(byte[] d, int at, uint v) { d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); d[at + 2] = (byte)(v >> 16); d[at + 3] = (byte)(v >> 24); }
        }
    }
}
