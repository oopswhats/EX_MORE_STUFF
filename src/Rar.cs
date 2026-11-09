using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExMoreStuff
{
    // Old .rar archives (RAR 1.5 to 4.x, "Rar!\x1a\x07\x00") read by EX More Stuff itself (user 2026-10-16: Windows 10's tar can't
    // undo the filters older RARs use, "Parsing filters is unsupported", so kenji490's Nightwolf for T.Hawk didn't install).
    // RAR 2.9's unpacking (Huffman-coded LZ with its standard filters: E8, E8E9, delta, RGB, audio; PPMd blocks, RarPpmd.cs)
    // and stored files, solid or not, each file checked against its CRC. Anything else (the RAR 1.5/2.0 methods, passwords,
    // volumes, filters that aren't the standard ones) is NotSupportedException, and the caller tries Windows' tar instead.
    // Written from the format as unrar's public source and libarchive (BSD licence) describe it; nothing in an archive is run.
    static partial class Rar
    {
        static readonly byte[] Signature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };

        /// <summary>An old-style RAR (not RAR5, which Windows' tar reads).</summary>
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
            public int Version, Method;
            public bool Solid, Directory;
        }

        /// <summary>The files `keep` wants (by their path inside, with '/'), in the archive's order.</summary>
        public static List<KeyValuePair<string, byte[]>> Read(string path, Func<string, bool> keep)
        {
            byte[] data = File.ReadAllBytes(path);
            var entries = Entries(data);
            var result = new List<KeyValuePair<string, byte[]>>();
            Unpacker unpacker = null;
            foreach (Entry e in entries)
            {
                if (e.Directory) continue;
                bool wanted = keep(e.Name) && e.UnpSize <= 512L * 1024 * 1024;
                byte[] bytes;
                if (e.Method == 0x30)   // stored
                {
                    if (!wanted) continue;
                    if (e.PackSize != e.UnpSize || e.DataAt + e.PackSize > data.Length) throw new NotSupportedException("a damaged stored file");
                    bytes = new byte[e.UnpSize];
                    Buffer.BlockCopy(data, (int)e.DataAt, bytes, 0, bytes.Length);
                }
                else
                {
                    if (e.Version != 29 && e.Version != 36) throw new NotSupportedException("RAR " + e.Version / 10 + "." + e.Version % 10 + " packing");
                    // in a solid archive every file goes through the unpacker in turn (each continues from the one before)
                    if (!wanted && !e.Solid && !NextIsSolid(entries, e)) continue;
                    if (e.UnpSize > int.MaxValue || e.DataAt + e.PackSize > data.Length) throw new NotSupportedException("too big or damaged");
                    if (unpacker == null || !e.Solid) unpacker = unpacker ?? new Unpacker();
                    bytes = unpacker.Unpack(data, (int)e.DataAt, (int)e.PackSize, (int)e.UnpSize, e.Solid);
                    if (!wanted) continue;
                }
                if (Crc32(bytes) != e.Crc) throw new NotSupportedException("its CRC doesn't match (" + e.Name + ")");
                result.Add(new KeyValuePair<string, byte[]>(e.Name, bytes));
            }
            return result;
        }

        static bool NextIsSolid(List<Entry> entries, Entry e)
        {
            int i = entries.IndexOf(e);
            for (int j = i + 1; j < entries.Count; j++) if (!entries[j].Directory) return entries[j].Solid && entries[j].Method != 0x30;
            return false;
        }

        static int U16(byte[] d, long at) { return d[at] | d[at + 1] << 8; }
        static uint U32(byte[] d, long at) { return (uint)(d[at] | d[at + 1] << 8 | d[at + 2] << 16 | d[at + 3] << 24); }

        static List<Entry> Entries(byte[] d)
        {
            var list = new List<Entry>();
            long pos = Signature.Length;
            while (pos + 7 <= d.Length)
            {
                int type = d[pos + 2], flags = U16(d, pos + 3), size = U16(d, pos + 5);
                if (size < 7 || pos + size > d.Length) break;
                long add = 0;
                if (type == 0x74 || type == 0x7A || (flags & 0x8000) != 0)
                {
                    if (size < 11) break;
                    add = U32(d, pos + 7);
                    if ((type == 0x74 || type == 0x7A) && (flags & 0x100) != 0 && size >= 40) add |= (long)U32(d, pos + 32) << 32;
                }
                if (type == 0x73)   // the archive's own header
                {
                    if ((flags & 0x0080) != 0) throw new NotSupportedException("a password");
                    if ((flags & 0x0001) != 0) throw new NotSupportedException("a multi-part archive");
                }
                else if (type == 0x74)   // a file
                {
                    if (size < 32) break;
                    if ((flags & 0x0004) != 0) throw new NotSupportedException("a password");
                    if ((flags & 0x0003) != 0) throw new NotSupportedException("a file split over parts");
                    var e = new Entry
                    {
                        PackSize = add,
                        UnpSize = U32(d, pos + 11) | ((flags & 0x100) != 0 ? (long)U32(d, pos + 36) << 32 : 0),
                        Crc = U32(d, pos + 16),
                        Version = d[pos + 24],
                        Method = d[pos + 25],
                        Solid = (flags & 0x0010) != 0,
                        Directory = (flags & 0x00E0) == 0x00E0,
                        DataAt = pos + size,
                    };
                    int nameSize = U16(d, pos + 26), nameAt = (int)pos + ((flags & 0x100) != 0 ? 40 : 32);
                    if (nameAt + nameSize > pos + size) break;
                    e.Name = Name(d, nameAt, nameSize, (flags & 0x0200) != 0).Replace('\\', '/');
                    list.Add(e);
                }
                else if (type == 0x7B) break;   // the end
                pos += size + add;
            }
            return list;
        }

        // a file's name: plain (the archiver's code page; ASCII in practice), UTF-8, or RAR's own Unicode packing after a 0
        static string Name(byte[] d, int at, int size, bool unicode)
        {
            int zero = Array.IndexOf(d, (byte)0, at, size);
            if (!unicode) return Encoding.GetEncoding(28591).GetString(d, at, size);
            if (zero < 0) return Encoding.UTF8.GetString(d, at, size);
            int plain = zero - at, encPos = zero + 1, encEnd = at + size;
            var name = new StringBuilder();
            if (encPos >= encEnd) return Encoding.GetEncoding(28591).GetString(d, at, plain);
            int high = d[encPos++], flags = 0, flagBits = 0;
            while (encPos < encEnd)
            {
                if (flagBits == 0) { flags = d[encPos++]; flagBits = 8; if (encPos > encEnd) break; }
                switch (flags >> 6)
                {
                    case 0: if (encPos >= encEnd) break; name.Append((char)d[encPos++]); break;
                    case 1: if (encPos >= encEnd) break; name.Append((char)(d[encPos++] + (high << 8))); break;
                    case 2: if (encPos + 1 >= encEnd) { encPos = encEnd; break; } name.Append((char)(d[encPos] | d[encPos + 1] << 8)); encPos += 2; break;
                    default:
                        if (encPos >= encEnd) break;
                        int length = d[encPos++];
                        if ((length & 0x80) != 0)
                        {
                            if (encPos >= encEnd) break;
                            int correction = d[encPos++];
                            for (length = (length & 0x7F) + 2; length > 0 && name.Length < plain; length--)
                                name.Append((char)(((d[at + name.Length] + correction) & 0xFF) + (high << 8)));
                        }
                        else
                            for (length += 2; length > 0 && name.Length < plain; length--) name.Append((char)d[at + name.Length]);
                        break;
                }
                flags = (flags << 2) & 0xFF;
                flagBits -= 2;
            }
            return name.ToString();
        }

        static uint[] crcTable;

        public static uint Crc32(byte[] data, int start = 0, int length = -1)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                crcTable = t;
            }
            if (length < 0) length = data.Length - start;
            uint crc = 0xFFFFFFFF;
            for (int i = start; i < start + length; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        // A canonical Huffman code from its code lengths (0: unused), decoded a bit at a time.
        sealed class Huffman
        {
            readonly int[] counts = new int[16];
            readonly int[] symbols;

            public Huffman(byte[] lengths, int start, int count)
            {
                symbols = new int[count];
                for (int i = 0; i < count; i++) counts[lengths[start + i] & 0xF]++;
                counts[0] = 0;
                var offsets = new int[16];
                for (int len = 1; len < 16; len++) offsets[len] = offsets[len - 1] + counts[len - 1];
                for (int i = 0; i < count; i++)
                {
                    int len = lengths[start + i] & 0xF;
                    if (len != 0) symbols[offsets[len]++] = i;
                }
            }

            public int Decode(Unpacker u)
            {
                int code = 0, first = 0, index = 0;
                for (int len = 1; len < 16; len++)
                {
                    code |= u.Bit();
                    int count = counts[len];
                    if (code - first < count) return symbols[index + code - first];
                    index += count;
                    first += count;
                    first <<= 1;
                    code <<= 1;
                }
                throw new NotSupportedException("damaged packed data");
            }
        }

        // A filter program the archive sent (one of RAR's standard ones, known by its code's CRC and length), and a use of it
        // on a block of the window.
        enum FilterType { E8, E8E9, Delta, Rgb, Audio }

        sealed class StackFilter
        {
            public FilterType Type;
            public uint BlockStart, BlockLength;
            public bool NextWindow;
            public readonly uint[] InitR = new uint[7];
        }

        // Bits read from the front of a byte array, the first bit the highest (RAR's order); past its end, zeros.
        sealed class BitInput
        {
            readonly byte[] buf;
            readonly int end;
            public int Addr, Bit;

            public BitInput(byte[] buf, int start, int end) { this.buf = buf; Addr = start; this.end = end; }

            int Byte(int i) { return i < end ? buf[i] : 0; }

            public uint GetBits() { return (uint)((Byte(Addr) << 16 | Byte(Addr + 1) << 8 | Byte(Addr + 2)) >> (8 - Bit)) & 0xFFFF; }

            public void AddBits(int n) { n += Bit; Addr += n >> 3; Bit = n & 7; }

            // a whole byte (PPMd reads bytes: its blocks start on one)
            public int GetChar() { return Byte(Addr++); }

            // a number in RarVM's own variable-length form
            public uint ReadData()
            {
                uint data = GetBits();
                switch (data & 0xC000)
                {
                    case 0: AddBits(6); return (data >> 10) & 0xF;
                    case 0x4000:
                        if ((data & 0x3C00) == 0) { data = 0xFFFFFF00 | ((data >> 2) & 0xFF); AddBits(14); }
                        else { data = (data >> 6) & 0xFF; AddBits(10); }
                        return data;
                    case 0x8000: AddBits(2); data = GetBits(); AddBits(16); return data;
                    default: AddBits(2); data = GetBits() << 16; AddBits(16); data |= GetBits(); AddBits(16); return data;
                }
            }
        }

        // RAR 2.9's unpacker (unrar's Unpack29): its state lives on from one file to the next in a solid archive.
        sealed class Unpacker
        {
            const int WinSize = 0x400000, WinMask = WinSize - 1;
            const int NC = 299, DC = 60, LDC = 17, RC = 28, TableSize = NC + DC + LDC + RC;
            const int VmMemSize = 0x40000;

            static readonly int[] LDecode = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224 };
            static readonly int[] LBits = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5 };
            static readonly uint[] DDecode =
            {
                0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096, 6144, 8192,
                12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072, 196608, 262144, 327680, 393216, 458752, 524288, 589824, 655360,
                720896, 786432, 851968, 917504, 983040, 1048576, 1310720, 1572864, 1835008, 2097152, 2359296, 2621440, 2883584, 3145728,
                3407872, 3670016, 3932160,
            };
            static readonly int[] DBits =
            {
                0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16,
                16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18,
            };
            static readonly uint[] SDDecode = { 0, 4, 8, 16, 32, 64, 128, 192 };
            static readonly int[] SDBits = { 2, 2, 3, 4, 5, 6, 6, 6 };

            readonly byte[] window = new byte[WinSize];
            int unpPtr, wrPtr;
            BitInput inp;
            int inEnd;

            readonly byte[] oldTable = new byte[TableSize];
            Huffman md, dd, ldd, rd;
            bool tablesRead;
            Ppmd ppm;                     // made at the first PPMd block; its model lives on as unrar's does
            bool ppmBlock;
            int ppmEscChar = 2;
            readonly uint[] oldDist = new uint[4];
            uint lastDist;
            int lastLength, lowDistRepCount;
            uint prevLowDist;

            readonly List<FilterType> filters = new List<FilterType>();   // the programs sent so far
            readonly List<uint> oldFilterLengths = new List<uint>();
            int lastFilter;
            readonly List<StackFilter> stack = new List<StackFilter>();
            readonly byte[] vm = new byte[VmMemSize + 4];

            byte[] output;
            long written;
            int outLength;

            public int Bit()
            {
                int b = (inp.Addr < inEnd ? inp_buf[inp.Addr] : 0) >> (7 - inp.Bit) & 1;
                if (++inp.Bit == 8) { inp.Bit = 0; inp.Addr++; }
                return b;
            }

            byte[] inp_buf;

            public byte[] Unpack(byte[] data, int start, int length, int unpSize, bool solid)
            {
                if (!solid)
                {
                    tablesRead = false;
                    Array.Clear(oldTable, 0, oldTable.Length);
                    Array.Clear(oldDist, 0, 4);
                    lastDist = 0;
                    lastLength = 0;
                    unpPtr = wrPtr = 0;
                    Array.Clear(window, 0, window.Length);
                    filters.Clear();
                    oldFilterLengths.Clear();
                    lastFilter = 0;
                    ppmBlock = false;
                    ppmEscChar = 2;
                }
                stack.Clear();
                inp_buf = data;
                inp = new BitInput(data, start, start + length);
                if (ppm != null) ppm.In = inp;   // a solid file can go on in the PPMd block the one before ended in
                inEnd = start + length;
                output = new byte[unpSize];
                outLength = unpSize;
                written = 0;
                if (!tablesRead) ReadTables();
                while (true)
                {
                    unpPtr &= WinMask;
                    if (inp.Addr > inEnd) break;   // its data ran out (the CRC check then says so)
                    if (((wrPtr - unpPtr) & WinMask) < 260 && wrPtr != unpPtr)
                    {
                        WriteBuf();
                        if (written > outLength) break;
                    }
                    if (ppmBlock)   // unrar's Unpack29, BLOCK_PPM
                    {
                        int ch = ppm.DecodeChar();
                        if (ch == -1) { PpmDamaged(); break; }
                        if (ch == ppmEscChar)
                        {
                            int next = ppm.DecodeChar();
                            if (next == -1) { PpmDamaged(); break; }
                            if (next == 0) { ReadTables(); continue; }   // back to LZ
                            if (next == 2) break;                        // this file's end
                            if (next == 3) { if (!ReadVMCodePpm()) break; continue; }
                            if (next == 4)   // a match: distance in 3 bytes, length in 1
                            {
                                uint distance = 0;
                                int len = 0;
                                bool failed = false;
                                for (int i = 0; i < 4 && !failed; i++)
                                {
                                    int c = ppm.DecodeChar();
                                    if (c == -1) failed = true;
                                    else if (i == 3) len = c;
                                    else distance = (distance << 8) + (uint)c;
                                }
                                if (failed) { PpmDamaged(); break; }
                                CopyString(len + 32, distance + 2);
                                continue;
                            }
                            if (next == 5)   // a run of the last byte
                            {
                                int len = ppm.DecodeChar();
                                if (len == -1) { PpmDamaged(); break; }
                                CopyString(len + 4, 1);
                                continue;
                            }
                            // 1: the escape character itself
                        }
                        window[unpPtr++] = (byte)ch;
                        continue;
                    }
                    int number = md.Decode(this);
                    if (number < 256) { window[unpPtr++] = (byte)number; continue; }
                    if (number >= 271)
                    {
                        number -= 271;
                        if (number >= LDecode.Length) throw new NotSupportedException("damaged packed data");
                        int len = LDecode[number] + 3;
                        if (LBits[number] > 0) { len += (int)(inp.GetBits() >> (16 - LBits[number])); inp.AddBits(LBits[number]); }
                        int distNumber = dd.Decode(this);
                        if (distNumber >= DDecode.Length) throw new NotSupportedException("damaged packed data");
                        uint distance = DDecode[distNumber] + 1;
                        int bits = DBits[distNumber];
                        if (bits > 0)
                        {
                            if (distNumber > 9)
                            {
                                if (bits > 4) { distance += (inp.GetBits() >> (20 - bits)) << 4; inp.AddBits(bits - 4); }
                                if (lowDistRepCount > 0) { lowDistRepCount--; distance += prevLowDist; }
                                else
                                {
                                    int lowDist = ldd.Decode(this);
                                    if (lowDist == 16) { lowDistRepCount = 15; distance += prevLowDist; }
                                    else { distance += (uint)lowDist; prevLowDist = (uint)lowDist; }
                                }
                            }
                            else { distance += inp.GetBits() >> (16 - bits); inp.AddBits(bits); }
                        }
                        if (distance >= 0x2000) { len++; if (distance >= 0x40000) len++; }
                        InsertOldDist(distance);
                        lastLength = len;
                        lastDist = distance;
                        CopyString(len, distance);
                        continue;
                    }
                    if (number == 256) { if (!ReadEndOfBlock()) break; continue; }
                    if (number == 257) { if (!ReadVMCode()) break; continue; }
                    if (number == 258) { if (lastLength != 0) CopyString(lastLength, lastDist); continue; }
                    if (number < 263)
                    {
                        int distNum = number - 259;
                        uint distance = oldDist[distNum];
                        for (int i = distNum; i > 0; i--) oldDist[i] = oldDist[i - 1];
                        oldDist[0] = distance;
                        int lengthNumber = rd.Decode(this);
                        if (lengthNumber >= LDecode.Length) throw new NotSupportedException("damaged packed data");
                        int len = LDecode[lengthNumber] + 2;
                        if (LBits[lengthNumber] > 0) { len += (int)(inp.GetBits() >> (16 - LBits[lengthNumber])); inp.AddBits(LBits[lengthNumber]); }
                        lastLength = len;
                        lastDist = distance;
                        CopyString(len, distance);
                        continue;
                    }
                    {
                        number -= 263;
                        uint distance = SDDecode[number] + 1;
                        if (SDBits[number] > 0) { distance += inp.GetBits() >> (16 - SDBits[number]); inp.AddBits(SDBits[number]); }
                        InsertOldDist(distance);
                        lastLength = 2;
                        lastDist = distance;
                        CopyString(2, distance);
                    }
                }
                WriteBuf();
                if (written < outLength) throw new NotSupportedException("its packed data ended early");
                return output;
            }

            void InsertOldDist(uint distance)
            {
                oldDist[3] = oldDist[2];
                oldDist[2] = oldDist[1];
                oldDist[1] = oldDist[0];
                oldDist[0] = distance;
            }

            void CopyString(int length, uint distance)
            {
                int src = (int)((unpPtr - distance) & WinMask);
                for (int i = 0; i < length; i++)
                {
                    window[unpPtr] = window[(src + i) & WinMask];
                    unpPtr = (unpPtr + 1) & WinMask;
                }
            }

            void ReadTables()
            {
                inp.AddBits((8 - inp.Bit) & 7);   // to the next byte
                uint bitField = inp.GetBits();
                if ((bitField & 0x8000) != 0)   // a PPMd block
                {
                    ppmBlock = true;
                    if (ppm == null) ppm = new Ppmd();
                    if (!ppm.DecodeInit(inp, ref ppmEscChar)) throw new NotSupportedException("damaged packed data");
                    return;
                }
                ppmBlock = false;
                prevLowDist = 0;
                lowDistRepCount = 0;
                if ((bitField & 0x4000) == 0) Array.Clear(oldTable, 0, oldTable.Length);
                inp.AddBits(2);
                var bitLength = new byte[20];
                for (int i = 0; i < 20; i++)
                {
                    int length = (int)(inp.GetBits() >> 12);
                    inp.AddBits(4);
                    if (length == 15)
                    {
                        int zeroCount = (int)(inp.GetBits() >> 12);
                        inp.AddBits(4);
                        if (zeroCount == 0) bitLength[i] = 15;
                        else
                        {
                            zeroCount += 2;
                            while (zeroCount-- > 0 && i < bitLength.Length) bitLength[i++] = 0;
                            i--;
                        }
                    }
                    else bitLength[i] = (byte)length;
                }
                var bd = new Huffman(bitLength, 0, 20);
                var table = new byte[TableSize];
                for (int i = 0; i < TableSize;)
                {
                    int number = bd.Decode(this);
                    if (number < 16) { table[i] = (byte)((number + oldTable[i]) & 0xF); i++; }
                    else if (number < 18)
                    {
                        int n;
                        if (number == 16) { n = (int)(inp.GetBits() >> 13) + 3; inp.AddBits(3); }
                        else { n = (int)(inp.GetBits() >> 9) + 11; inp.AddBits(7); }
                        if (i == 0) throw new NotSupportedException("damaged packed data");
                        while (n-- > 0 && i < TableSize) { table[i] = table[i - 1]; i++; }
                    }
                    else
                    {
                        int n;
                        if (number == 18) { n = (int)(inp.GetBits() >> 13) + 3; inp.AddBits(3); }
                        else { n = (int)(inp.GetBits() >> 9) + 11; inp.AddBits(7); }
                        while (n-- > 0 && i < TableSize) table[i++] = 0;
                    }
                }
                tablesRead = true;
                md = new Huffman(table, 0, NC);
                dd = new Huffman(table, NC, DC);
                ldd = new Huffman(table, NC + DC, LDC);
                rd = new Huffman(table, NC + DC + LDC, RC);
                Buffer.BlockCopy(table, 0, oldTable, 0, TableSize);
            }

            // false: this file's data ends here
            bool ReadEndOfBlock()
            {
                uint bitField = inp.GetBits();
                bool newTable, newFile = false;
                if ((bitField & 0x8000) != 0) { newTable = true; inp.AddBits(1); }
                else { newFile = true; newTable = (bitField & 0x4000) != 0; inp.AddBits(2); }
                tablesRead = !newTable;
                if (newFile) return false;
                ReadTables();
                return true;
            }

            bool ReadVMCode()
            {
                uint firstByte = inp.GetBits() >> 8;
                inp.AddBits(8);
                int length = (int)(firstByte & 7) + 1;
                if (length == 7) { length = (int)(inp.GetBits() >> 8) + 7; inp.AddBits(8); }
                else if (length == 8) { length = (int)inp.GetBits(); inp.AddBits(16); }
                if (length == 0) return false;
                var code = new byte[length];
                for (int i = 0; i < length; i++) { code[i] = (byte)(inp.GetBits() >> 8); inp.AddBits(8); }
                AddVMCode(firstByte, code);
                return true;
            }

            // damaged PPMd data: its model goes, LZ from here (unrar's PPM.CleanUp)
            void PpmDamaged()
            {
                ppm.Free();
                ppmBlock = false;
            }

            bool ReadVMCodePpm()
            {
                int firstByte = ppm.DecodeChar();
                if (firstByte == -1) { PpmDamaged(); return false; }
                int length = (firstByte & 7) + 1;
                if (length == 7)
                {
                    int b1 = ppm.DecodeChar();
                    if (b1 == -1) { PpmDamaged(); return false; }
                    length = b1 + 7;
                }
                else if (length == 8)
                {
                    int b1 = ppm.DecodeChar(), b2 = b1 == -1 ? -1 : ppm.DecodeChar();
                    if (b2 == -1) { PpmDamaged(); return false; }
                    length = b1 * 256 + b2;
                }
                if (length == 0) return false;
                var code = new byte[length];
                for (int i = 0; i < length; i++)
                {
                    int ch = ppm.DecodeChar();
                    if (ch == -1) { PpmDamaged(); return false; }
                    code[i] = (byte)ch;
                }
                AddVMCode((uint)firstByte, code);
                return true;
            }

            static FilterType Identify(byte[] program)
            {
                uint crc = Crc32(program);
                if (program.Length == 53 && crc == 0xAD576887) return FilterType.E8;
                if (program.Length == 57 && crc == 0x3CD7E57E) return FilterType.E8E9;
                if (program.Length == 29 && crc == 0x0E06077D) return FilterType.Delta;
                if (program.Length == 149 && crc == 0x1C2C5DC8) return FilterType.Rgb;
                if (program.Length == 216 && crc == 0xBC85E701) return FilterType.Audio;
                throw new NotSupportedException("a filter that isn't one of RAR's standard ones");
            }

            void AddVMCode(uint firstByte, byte[] code)
            {
                var vmInp = new BitInput(code, 0, code.Length);
                int filtPos;
                if ((firstByte & 0x80) != 0)
                {
                    filtPos = (int)vmInp.ReadData();
                    if (filtPos == 0) { filters.Clear(); oldFilterLengths.Clear(); stack.Clear(); }
                    else filtPos--;
                }
                else filtPos = lastFilter;
                if (filtPos > filters.Count || filtPos > oldFilterLengths.Count) throw new NotSupportedException("damaged filter data");
                lastFilter = filtPos;
                bool newFilter = filtPos == filters.Count;
                var f = new StackFilter();
                if (newFilter)
                {
                    if (filtPos > 8192) throw new NotSupportedException("too many filters");
                    filters.Add(FilterType.E8);   // its type is set below, from its program
                    oldFilterLengths.Add(0);
                }
                stack.RemoveAll(s => s == null);
                if (stack.Count > 8192) throw new NotSupportedException("too many filters");
                stack.Add(f);
                uint blockStart = vmInp.ReadData();
                if ((firstByte & 0x40) != 0) blockStart += 258;
                f.BlockStart = (uint)((blockStart + unpPtr) & WinMask);
                if ((firstByte & 0x20) != 0) { f.BlockLength = vmInp.ReadData(); oldFilterLengths[filtPos] = f.BlockLength; }
                else f.BlockLength = filtPos < oldFilterLengths.Count ? oldFilterLengths[filtPos] : 0;
                f.NextWindow = wrPtr != unpPtr && ((wrPtr - unpPtr) & WinMask) <= blockStart;
                f.InitR[4] = f.BlockLength;
                if ((firstByte & 0x10) != 0)
                {
                    uint initMask = vmInp.GetBits() >> 9;
                    vmInp.AddBits(7);
                    for (int i = 0; i < 7; i++) if ((initMask & (1u << i)) != 0) f.InitR[i] = vmInp.ReadData();
                }
                if (newFilter)
                {
                    uint size = vmInp.ReadData();
                    if (size >= 0x10000 || size == 0 || vmInp.Addr + size > code.Length) throw new NotSupportedException("damaged filter data");
                    var program = new byte[size];
                    for (int i = 0; i < size; i++) { program[i] = (byte)(vmInp.GetBits() >> 8); vmInp.AddBits(8); }
                    filters[filtPos] = Identify(program);
                }
                f.Type = filters[filtPos];
                // (global data may follow: the standard filters don't use it)
            }

            void WriteArea(int start, int end)
            {
                // wrapped past the window's end: its two parts (user 2026-10-18: Cammy Nilin's rar, "Offset and length were
                // out of bounds", copied the whole span from `start` instead)
                if (end < start)
                {
                    WriteData(window, start, WinSize - start);
                    WriteData(window, 0, end);
                }
                else if (end > start) WriteData(window, start, end - start);
            }

            void WriteData(byte[] data, int at, int size)
            {
                if (written >= outLength) { written += size; return; }
                int n = (int)Math.Min(size, outLength - written);
                Buffer.BlockCopy(data, at, output, (int)written, n);
                written += size;
            }

            // the window up to unpPtr out, through the filters that cover parts of it (unrar's UnpWriteBuf30)
            void WriteBuf()
            {
                int writtenBorder = wrPtr;
                int writeSize = (unpPtr - writtenBorder) & WinMask;
                for (int i = 0; i < stack.Count; i++)
                {
                    StackFilter flt = stack[i];
                    if (flt == null) continue;
                    if (flt.NextWindow) { flt.NextWindow = false; continue; }
                    int blockStart = (int)flt.BlockStart;
                    uint blockLength = flt.BlockLength;
                    if (((blockStart - writtenBorder) & WinMask) < writeSize)
                    {
                        if (writtenBorder != blockStart)
                        {
                            WriteArea(writtenBorder, blockStart);
                            writtenBorder = blockStart;
                            writeSize = (unpPtr - writtenBorder) & WinMask;
                        }
                        if (blockLength <= writeSize)
                        {
                            int blockEnd = (int)((blockStart + blockLength) & WinMask);
                            if (blockLength > VmMemSize) throw new NotSupportedException("damaged filter data");
                            if (blockStart < blockEnd || blockEnd == 0) Buffer.BlockCopy(window, blockStart, vm, 0, (int)blockLength);
                            else
                            {
                                int firstPart = WinSize - blockStart;
                                Buffer.BlockCopy(window, blockStart, vm, 0, firstPart);
                                Buffer.BlockCopy(window, 0, vm, firstPart, blockEnd);
                            }
                            int at, size;
                            Execute(flt, out at, out size);
                            stack[i] = null;
                            while (i + 1 < stack.Count)
                            {
                                StackFilter next = stack[i + 1];
                                if (next == null || next.BlockStart != blockStart || next.BlockLength != size || next.NextWindow) break;
                                // several filters on the same block, one after the other
                                Buffer.BlockCopy(vm, at, vm, 0, size);
                                Execute(next, out at, out size);
                                i++;
                                stack[i] = null;
                            }
                            WriteData(vm, at, size);
                            writtenBorder = blockEnd;
                            writeSize = (unpPtr - writtenBorder) & WinMask;
                        }
                        else
                        {
                            // this filter's block isn't all unpacked yet: it's done next time
                            for (int j = i; j < stack.Count; j++) if (stack[j] != null && stack[j].NextWindow) stack[j].NextWindow = false;
                            wrPtr = writtenBorder;
                            return;
                        }
                    }
                }
                WriteArea(writtenBorder, unpPtr);
                wrPtr = unpPtr;
            }

            static uint Get4(byte[] b, int at) { return (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24); }
            static void Put4(byte[] b, int at, uint v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }

            // one of RAR's standard filters on the block in vm; where its result is (at, size)
            void Execute(StackFilter f, out int at, out int size)
            {
                uint dataSize = f.InitR[4];
                switch (f.Type)
                {
                    case FilterType.E8:
                    case FilterType.E8E9:
                    {
                        if (dataSize > VmMemSize || dataSize < 4) throw new NotSupportedException("damaged filter data");
                        uint fileOffset = (uint)written;
                        const uint fileSize = 0x1000000;
                        byte cmp2 = f.Type == FilterType.E8E9 ? (byte)0xE9 : (byte)0xE8;
                        for (uint curPos = 0; curPos < dataSize - 4;)
                        {
                            byte cur = vm[curPos++];
                            if (cur == 0xE8 || cur == cmp2)
                            {
                                uint offset = curPos + fileOffset;
                                uint addr = Get4(vm, (int)curPos);
                                if ((addr & 0x80000000) != 0) { if (((addr + offset) & 0x80000000) == 0) Put4(vm, (int)curPos, addr + fileSize); }
                                else if (((addr - fileSize) & 0x80000000) != 0) Put4(vm, (int)curPos, addr - offset);
                                curPos += 4;
                            }
                        }
                        at = 0;
                        size = (int)dataSize;
                        return;
                    }
                    case FilterType.Delta:
                    {
                        uint channels = f.InitR[0];
                        if (dataSize > VmMemSize / 2 || channels > 1024 || channels == 0) throw new NotSupportedException("damaged filter data");
                        int srcPos = 0;
                        for (uint ch = 0; ch < channels; ch++)
                        {
                            byte prev = 0;
                            for (uint dest = dataSize + ch; dest < dataSize * 2; dest += channels) vm[dest] = prev = (byte)(prev - vm[srcPos++]);
                        }
                        at = (int)dataSize;
                        size = (int)dataSize;
                        return;
                    }
                    case FilterType.Rgb:
                    {
                        uint width = f.InitR[0] - 3, posR = f.InitR[1];
                        if (dataSize > VmMemSize / 2 || dataSize < 3 || width > dataSize || posR > 2) throw new NotSupportedException("damaged filter data");
                        int src = 0, dest = (int)dataSize;
                        for (int ch = 0; ch < 3; ch++)
                        {
                            uint prev = 0;
                            for (int i = ch; i < dataSize; i += 3)
                            {
                                uint predicted;
                                if (i >= width + 3)
                                {
                                    int upper = dest + i - (int)width;
                                    uint upperByte = vm[upper], upperLeft = vm[upper - 3];
                                    predicted = prev + upperByte - upperLeft;
                                    int pa = Math.Abs((int)(predicted - prev)), pb = Math.Abs((int)(predicted - upperByte)), pc = Math.Abs((int)(predicted - upperLeft));
                                    if (pa <= pb && pa <= pc) predicted = prev;
                                    else if (pb <= pc) predicted = upperByte;
                                    else predicted = upperLeft;
                                }
                                else predicted = prev;
                                prev = (byte)(predicted - vm[src++]);
                                vm[dest + i] = (byte)prev;
                            }
                        }
                        for (int i = (int)posR; i < dataSize - 2; i += 3)
                        {
                            byte g = vm[dest + i + 1];
                            vm[dest + i] += g;
                            vm[dest + i + 2] += g;
                        }
                        at = dest;
                        size = (int)dataSize;
                        return;
                    }
                    default:   // audio
                    {
                        uint channels = f.InitR[0];
                        if (dataSize > VmMemSize / 2 || channels > 128 || channels == 0) throw new NotSupportedException("damaged filter data");
                        int src = 0, dest = (int)dataSize;
                        for (uint ch = 0; ch < channels; ch++)
                        {
                            uint prevByte = 0, prevDelta = 0;
                            var dif = new uint[7];
                            int d1 = 0, d2 = 0, d3, k1 = 0, k2 = 0, k3 = 0;
                            for (uint i = ch, byteCount = 0; i < dataSize; i += channels, byteCount++)
                            {
                                d3 = d2;
                                d2 = (int)prevDelta - d1;
                                d1 = (int)prevDelta;
                                uint predicted = (uint)(8 * (int)prevByte + k1 * d1 + k2 * d2 + k3 * d3);
                                predicted = (predicted >> 3) & 0xFF;
                                uint cur = vm[src++];
                                predicted -= cur;
                                vm[dest + i] = (byte)predicted;
                                prevDelta = (uint)(sbyte)(predicted - prevByte);
                                prevByte = (byte)predicted;
                                int d = (sbyte)cur << 3;
                                dif[0] += (uint)Math.Abs(d);
                                dif[1] += (uint)Math.Abs(d - d1);
                                dif[2] += (uint)Math.Abs(d + d1);
                                dif[3] += (uint)Math.Abs(d - d2);
                                dif[4] += (uint)Math.Abs(d + d2);
                                dif[5] += (uint)Math.Abs(d - d3);
                                dif[6] += (uint)Math.Abs(d + d3);
                                if ((byteCount & 0x1F) == 0)
                                {
                                    uint minDif = dif[0], numMinDif = 0;
                                    dif[0] = 0;
                                    for (uint j = 1; j < 7; j++)
                                    {
                                        if (dif[j] < minDif) { minDif = dif[j]; numMinDif = j; }
                                        dif[j] = 0;
                                    }
                                    switch (numMinDif)
                                    {
                                        case 1: if (k1 >= -16) k1--; break;
                                        case 2: if (k1 < 16) k1++; break;
                                        case 3: if (k2 >= -16) k2--; break;
                                        case 4: if (k2 < 16) k2++; break;
                                        case 5: if (k3 >= -16) k3--; break;
                                        case 6: if (k3 < 16) k3++; break;
                                    }
                                }
                            }
                        }
                        at = dest;
                        size = (int)dataSize;
                        return;
                    }
                }
            }
        }
    }
}
