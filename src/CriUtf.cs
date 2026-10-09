// CRI @UTF tables: the container of the game's sound banks (.csb: TBLCSB with nested CUE / SYNTH / SOUND_ELEMENT
// tables; each sound element an AAX table of ADX streams). Read and written so that an unchanged table comes out byte
// for byte as the game's (checked against every bank of the game: test\MusicScan.cs).
//
// Layout (big-endian): "@UTF", u32 size (bytes after these 8), then from +8: u32 rows offset, u32 strings offset,
// u32 data offset, u32 table-name string offset (offsets from +8), u16 column count, u16 row length, u32 row count;
// the columns (u8 flag + u32 name; flag high nibble 0x10 zero, 0x30 constant (value follows), 0x50 per row; low
// nibble the type); the rows; the strings ("<NULL>", the table name, the column names, then the values in row order,
// each once); padding to 8; the data blobs, each at a multiple of 8; padding to 8.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExMoreStuff
{
    sealed class UtfColumn
    {
        public string Name;
        public byte Storage;      // 0x10 zero, 0x30 constant, 0x50 per row
        public byte Type;         // 0/1 u8/s8, 2/3 u16/s16, 4/5 u32/s32, 6/7 u64/s64, 8 float, 0xA string, 0xB data
        public object Constant;   // for 0x30
    }

    sealed class UtfTable
    {
        public string Name;
        public List<UtfColumn> Columns = new List<UtfColumn>();
        public List<object[]> Rows = new List<object[]>();   // per row, a value for every column (zero/constant too)

        public const byte Zero = 0x10, Const = 0x30, PerRow = 0x50;
        public const byte TString = 0xA, TData = 0xB;

        static int Size(byte type)
        {
            switch (type)
            {
                case 0: case 1: return 1;
                case 2: case 3: return 2;
                case 4: case 5: case 8: case TString: return 4;
                case 6: case 7: case TData: return 8;
            }
            throw new InvalidDataException("unknown @UTF column type " + type);
        }

        public int Column(string name)
        {
            for (int i = 0; i < Columns.Count; i++) if (Columns[i].Name == name) return i;
            return -1;
        }

        public object Get(int row, string column)
        {
            int c = Column(column);
            if (c < 0) throw new InvalidDataException("no column " + column + " in table " + Name);
            return Rows[row][c];
        }

        public void Set(int row, string column, object value)
        {
            int c = Column(column);
            if (c < 0) throw new InvalidDataException("no column " + column + " in table " + Name);
            // a column stored once for all rows becomes per row when one row's value changes
            if (Columns[c].Storage == Const && !Equals(Columns[c].Constant, value)) Columns[c].Storage = PerRow;
            else if (Columns[c].Storage == Zero && !IsZero(value)) throw new InvalidDataException("column " + column + " of " + Name + " holds no values");
            Rows[row][c] = value;
        }

        // what a column stored as "zero" stands for: nothing, 0, an empty string or no data
        static bool IsZero(object v)
        {
            if (v == null) return true;
            if (v is string) return ((string)v).Length == 0;
            if (v is byte[]) return ((byte[])v).Length == 0;
            if (v is float || v is double) return Convert.ToDouble(v) == 0;
            return v is IConvertible && Convert.ToDecimal(v) == 0;
        }

        // -- reading ------------------------------------------------------------------------------------------------
        static uint U32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        static ushort U16(byte[] b, int o) { return (ushort)(b[o] << 8 | b[o + 1]); }

        public static bool IsUtf(byte[] b)
        {
            return b != null && b.Length >= 32 && b[0] == '@' && b[1] == 'U' && b[2] == 'T' && b[3] == 'F';
        }

        public static UtfTable Read(byte[] b)
        {
            if (!IsUtf(b)) throw new InvalidDataException("not an @UTF table");
            int rowsOff = (int)U32(b, 8) + 8, strOff = (int)U32(b, 12) + 8, dataOff = (int)U32(b, 16) + 8;
            int nameOff = (int)U32(b, 20), ncol = U16(b, 24), nrows = (int)U32(b, 28);
            Func<int, string> str = o =>
            {
                int s = strOff + o, e = s;
                while (e < b.Length && b[e] != 0) e++;
                return Encoding.ASCII.GetString(b, s, e - s);
            };
            var t = new UtfTable { Name = str(nameOff) };
            int p = 32;
            for (int c = 0; c < ncol; c++)
            {
                byte flag = b[p];
                var col = new UtfColumn { Name = str((int)U32(b, p + 1)), Storage = (byte)(flag & 0xF0), Type = (byte)(flag & 0x0F) };
                p += 5;
                if (col.Storage == Const)
                {
                    col.Constant = ReadValue(b, p, col.Type, str, dataOff);
                    p += Size(col.Type);
                }
                else if (col.Storage != Zero && col.Storage != PerRow)
                    throw new InvalidDataException("unknown @UTF column storage " + col.Storage);
                t.Columns.Add(col);
            }
            for (int r = 0; r < nrows; r++)
            {
                var row = new object[ncol];
                int q = rowsOff + r * U16(b, 26);
                for (int c = 0; c < ncol; c++)
                {
                    var col = t.Columns[c];
                    if (col.Storage == Zero) row[c] = ZeroValue(col.Type);
                    else if (col.Storage == Const) row[c] = col.Constant;
                    else { row[c] = ReadValue(b, q, col.Type, str, dataOff); q += Size(col.Type); }
                }
                t.Rows.Add(row);
            }
            return t;
        }

        static object ZeroValue(byte type)
        {
            switch (type)
            {
                case 0: return (byte)0;
                case 1: return (sbyte)0;
                case 2: return (ushort)0;
                case 3: return (short)0;
                case 4: return 0u;
                case 5: return 0;
                case 6: return 0ul;
                case 7: return 0L;
                case 8: return 0f;
                case TString: return null;
                case TData: return new byte[0];
            }
            return null;
        }

        static object ReadValue(byte[] b, int o, byte type, Func<int, string> str, int dataOff)
        {
            switch (type)
            {
                case 0: return b[o];
                case 1: return (sbyte)b[o];
                case 2: return U16(b, o);
                case 3: return (short)U16(b, o);
                case 4: return U32(b, o);
                case 5: return (int)U32(b, o);
                case 6: return (ulong)U32(b, o) << 32 | U32(b, o + 4);
                case 7: return (long)((ulong)U32(b, o) << 32 | U32(b, o + 4));
                case 8: return BitConverter.ToSingle(new[] { b[o + 3], b[o + 2], b[o + 1], b[o] }, 0);
                case TString: return str((int)U32(b, o));
                case TData:
                    {
                        int off = (int)U32(b, o), len = (int)U32(b, o + 4);
                        var d = new byte[len];
                        Buffer.BlockCopy(b, dataOff + off, d, 0, len);
                        return d;
                    }
            }
            throw new InvalidDataException("unknown @UTF column type " + type);
        }

        // -- writing ------------------------------------------------------------------------------------------------
        public byte[] Write()
        {
            // strings: "<NULL>", the table name, column names, then values (constants where their column is, row
            // values in row order), each once
            var strings = new List<string>();
            var index = new Dictionary<string, int>();
            int strLen = 0;
            Func<string, int> intern = s =>
            {
                if (s == null) s = "<NULL>";
                int at;
                if (index.TryGetValue(s, out at)) return at;
                at = strLen;
                index[s] = at;
                strings.Add(s);
                strLen += Encoding.ASCII.GetByteCount(s) + 1;
                return at;
            };
            intern("<NULL>");
            int nameOff = intern(Name);
            var colName = new int[Columns.Count];
            for (int c = 0; c < Columns.Count; c++) colName[c] = intern(Columns[c].Name);
            // column area (constants' strings interned in column order), then rows
            var cols = new MemoryStream();
            var blobs = new List<byte[]>();
            var blobAt = new Dictionary<byte[], int>();
            Action<MemoryStream, byte, object> put = null;
            put = (ms, type, v) => WriteValue(ms, type, v, intern, blobs);
            for (int c = 0; c < Columns.Count; c++)
            {
                var col = Columns[c];
                cols.WriteByte((byte)(col.Storage | col.Type));
                PutU32(cols, (uint)colName[c]);
                if (col.Storage == Const) put(cols, col.Type, col.Constant);
            }
            int rowLen = 0;
            foreach (var col in Columns) if (col.Storage == PerRow) rowLen += Size(col.Type);
            var rows = new MemoryStream();
            foreach (var row in Rows)
                for (int c = 0; c < Columns.Count; c++)
                    if (Columns[c].Storage == PerRow) put(rows, Columns[c].Type, row[c]);
            // assemble: header (32), columns, rows, strings (+ pad to 8), data blobs (each at a multiple of 8)
            int rowsAt = 32 + (int)cols.Length;
            int strAt = rowsAt + (int)rows.Length;
            int dataAt = Align8(strAt + strLen);
            var data = new MemoryStream();
            var offsets = new List<int>();
            foreach (var blob in blobs)
            {
                if (blob.Length == 0) { offsets.Add(0); continue; }
                while ((dataAt + data.Length) % 8 != 0) data.WriteByte(0);
                offsets.Add((int)data.Length);
                data.Write(blob, 0, blob.Length);
            }
            int total = Align8(dataAt + (int)data.Length);
            var o = new byte[total];
            o[0] = (byte)'@'; o[1] = (byte)'U'; o[2] = (byte)'T'; o[3] = (byte)'F';
            SetU32(o, 4, (uint)(total - 8));
            SetU32(o, 8, (uint)(rowsAt - 8));
            SetU32(o, 12, (uint)(strAt - 8));
            SetU32(o, 16, (uint)(dataAt - 8));
            SetU32(o, 20, (uint)nameOff);
            o[24] = (byte)(Columns.Count >> 8); o[25] = (byte)Columns.Count;
            o[26] = (byte)(rowLen >> 8); o[27] = (byte)rowLen;
            SetU32(o, 28, (uint)Rows.Count);
            var colBytes = cols.ToArray();
            var rowBytes = rows.ToArray();
            // the blobs' offsets were written as placeholders (their index): fix them now
            FixBlobs(colBytes, Columns, true, offsets);
            FixBlobs(rowBytes, Columns, false, offsets);
            Buffer.BlockCopy(colBytes, 0, o, 32, colBytes.Length);
            Buffer.BlockCopy(rowBytes, 0, o, rowsAt, rowBytes.Length);
            int sp = strAt;
            foreach (var s in strings)
            {
                var sb = Encoding.ASCII.GetBytes(s);
                Buffer.BlockCopy(sb, 0, o, sp, sb.Length);
                sp += sb.Length + 1;
            }
            var db = data.ToArray();
            Buffer.BlockCopy(db, 0, o, dataAt, db.Length);
            return o;
        }

        static int Align8(int v) { return (v + 7) & ~7; }

        // data values are written as (blob index, length) and patched to (offset, length) once the data area is laid out
        void FixBlobs(byte[] area, List<UtfColumn> columns, bool columnArea, List<int> offsets)
        {
            int p = 0;
            if (columnArea)
            {
                foreach (var col in columns)
                {
                    p += 5;
                    if (col.Storage != Const) continue;
                    if (col.Type == TData) Patch(area, p, offsets);
                    p += Size(col.Type);
                }
                return;
            }
            foreach (var row in Rows)
                foreach (var col in columns)
                {
                    if (col.Storage != PerRow) continue;
                    if (col.Type == TData) Patch(area, p, offsets);
                    p += Size(col.Type);
                }
        }

        static void Patch(byte[] a, int p, List<int> offsets)
        {
            int i = (int)U32(a, p);
            SetU32(a, p, (uint)offsets[i]);
        }

        static void WriteValue(MemoryStream ms, byte type, object v, Func<string, int> intern, List<byte[]> blobs)
        {
            switch (type)
            {
                case 0: ms.WriteByte(Convert.ToByte(v)); return;
                case 1: ms.WriteByte((byte)Convert.ToSByte(v)); return;
                case 2: PutU16(ms, Convert.ToUInt16(v)); return;
                case 3: PutU16(ms, (ushort)Convert.ToInt16(v)); return;
                case 4: PutU32(ms, Convert.ToUInt32(v)); return;
                case 5: PutU32(ms, (uint)Convert.ToInt32(v)); return;
                case 6: { ulong x = Convert.ToUInt64(v); PutU32(ms, (uint)(x >> 32)); PutU32(ms, (uint)x); return; }
                case 7: { ulong x = (ulong)Convert.ToInt64(v); PutU32(ms, (uint)(x >> 32)); PutU32(ms, (uint)x); return; }
                case 8: { var f = BitConverter.GetBytes(Convert.ToSingle(v)); for (int i = 3; i >= 0; i--) ms.WriteByte(f[i]); return; }
                case TString: PutU32(ms, (uint)intern(v as string)); return;
                case TData:
                    {
                        var d = v as byte[] ?? new byte[0];
                        PutU32(ms, (uint)blobs.Count);
                        blobs.Add(d);
                        PutU32(ms, (uint)d.Length);
                        return;
                    }
            }
            throw new InvalidDataException("unknown @UTF column type " + type);
        }

        static void PutU16(Stream s, ushort v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void PutU32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void SetU32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    }
}
