using System;

namespace ExMoreStuff
{
    static partial class Rar
    {
        // RAR 2.9's PPMd blocks (user 2026-11-01, Button Mashin' Mastah: monkeygigabuster's DeviantArt stages, "needs Windows'
        // own tar" on Linux): Dmitry Shkarin's PPMd variant H with RAR's range coder, as unrar's model.cpp/suballoc.cpp and
        // 7-Zip's Ppmd7.c (public domain) describe it. The whole model lives in one byte array in 12-byte units, as in C:
        //   a state (6 bytes):    symbol, freq, successor (4)
        //   a context (12 bytes): number of states (2), freq sum (2), states (4), suffix (4); with one state, that state
        //                         sits in place of the freq sum and states (at +2)
        //   a free block:         stamp (2), units (2), next (4), prev (4); in a free list only its first 4 bytes: the next
        sealed class Ppmd
        {
            const int MaxO = 64, PeriodBits = 7, BinScale = 1 << 14, MaxFreq = 124, UnitSize = 12, NumIndexes = 38, Align = 4;
            const uint Top = 1 << 24, Bot = 1 << 15;
            static readonly ushort[] InitBinEsc = { 0x3CDD, 0x1F3F, 0x59BF, 0x48F3, 0x64A1, 0x5ABC, 0x6632, 0x6051 };
            static readonly byte[] ExpEscape = { 25, 14, 9, 7, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 2 };
            static readonly byte[] Indx2Units = new byte[NumIndexes], Units2Indx = new byte[128];
            static readonly byte[] NS2Indx = new byte[256], NS2BSIndx = new byte[256], HB2Flag = new byte[256];

            static Ppmd()
            {
                for (int i = 0, k = 0; i < NumIndexes; i++)
                {
                    int step = i >= 12 ? 4 : (i >> 2) + 1;
                    do { Units2Indx[k++] = (byte)i; } while (--step != 0);
                    Indx2Units[i] = (byte)k;
                }
                NS2BSIndx[0] = 0;
                NS2BSIndx[1] = 2;
                for (int i = 2; i < 11; i++) NS2BSIndx[i] = 4;
                for (int i = 11; i < 256; i++) NS2BSIndx[i] = 6;
                for (int i = 0; i < 3; i++) NS2Indx[i] = (byte)i;
                for (int i = 3, m = 3, k = 1; i < 256; i++)
                {
                    NS2Indx[i] = (byte)m;
                    if (--k == 0) k = ++m - 2;
                }
                for (int i = 0x40; i < 256; i++) HB2Flag[i] = 8;
            }

            byte[] mem;
            int size, text, unitsStart, loUnit, hiUnit, glueCount;
            readonly int[] freeList = new int[NumIndexes];
            int minContext, maxContext, foundState, orderFall, initEsc, prevSuccess, maxOrder, hiBitsFlag, runLength, initRL, numMasked;
            readonly ushort[,] binSumm = new ushort[128, 64];
            const int DummySee = 25 * 16;   // the SEE contexts [25][16], then the dummy one
            readonly ushort[] seeSumm = new ushort[DummySee + 1];
            readonly byte[] seeShift = new byte[DummySee + 1], seeCount = new byte[DummySee + 1];
            readonly int[] charMask = new int[256];   // == escCount: masked for this symbol
            int escCount;
            readonly int[] ps = new int[256];
            uint low, code, range;
            public BitInput In;

            int U16(int at) { return mem[at] | mem[at + 1] << 8; }
            void SetU16(int at, int v) { mem[at] = (byte)v; mem[at + 1] = (byte)(v >> 8); }
            int U32(int at) { return mem[at] | mem[at + 1] << 8 | mem[at + 2] << 16 | mem[at + 3] << 24; }
            void SetU32(int at, int v) { mem[at] = (byte)v; mem[at + 1] = (byte)(v >> 8); mem[at + 2] = (byte)(v >> 16); mem[at + 3] = (byte)(v >> 24); }

            int NumStats(int c) { return U16(c); }
            int SummFreq(int c) { return U16(c + 2); }
            int Stats(int c) { return U32(c + 4); }
            int Suffix(int c) { return U32(c + 8); }
            int Successor(int s) { return U32(s + 2); }
            void SetSuccessor(int s, int v) { SetU32(s + 2, v); }

            void CopyState(int to, int from) { Buffer.BlockCopy(mem, from, mem, to, 6); }

            void SwapStates(int a, int b)
            {
                for (int i = 0; i < 6; i++) { byte t = mem[a + i]; mem[a + i] = mem[b + i]; mem[b + i] = t; }
            }

            int GetChar() { return In.GetChar(); }

            public void Free() { mem = null; size = 0; }

            /// <summary>A PPMd block's start (its flags byte, model size, escape character) and the range coder's first
            /// bytes; false: the block can't be read (no model to go on with).</summary>
            public bool DecodeInit(BitInput inp, ref int escChar)
            {
                In = inp;
                int flags = GetChar();
                bool reset = (flags & 0x20) != 0;
                int maxMB = 0;
                if (reset) maxMB = GetChar();
                else if (mem == null) return false;
                if ((flags & 0x40) != 0) escChar = GetChar();
                low = code = 0;
                range = 0xFFFFFFFF;
                for (int i = 0; i < 4; i++) code = code << 8 | (uint)GetChar();
                if (reset)
                {
                    int order = (flags & 0x1F) + 1;
                    if (order > 16) order = 16 + (order - 16) * 3;
                    if (order == 1) { Free(); return false; }
                    int bytes = (maxMB + 1) << 20;
                    if (mem == null || size != bytes) { mem = new byte[Align + bytes + UnitSize]; size = bytes; }
                    maxOrder = order;
                    RestartModel();
                    seeShift[DummySee] = PeriodBits;
                    seeSumm[DummySee] = 0;
                    seeCount[DummySee] = 64;
                }
                return minContext != 0;
            }

            void RestartModel()
            {
                Array.Clear(freeList, 0, NumIndexes);
                text = Align;
                hiUnit = text + size;
                loUnit = unitsStart = hiUnit - size / 8 / UnitSize * 7 * UnitSize;
                glueCount = 0;
                orderFall = maxOrder;
                runLength = initRL = -(maxOrder < 12 ? maxOrder : 12) - 1;
                prevSuccess = 0;
                minContext = maxContext = hiUnit -= UnitSize;
                SetU32(minContext + 8, 0);
                SetU16(minContext, 256);
                SetU16(minContext + 2, 257);
                foundState = loUnit;
                SetU32(minContext + 4, loUnit);
                for (int i = 0; i < 256; i++)
                {
                    int s = loUnit + i * 6;
                    mem[s] = (byte)i;
                    mem[s + 1] = 1;
                    SetSuccessor(s, 0);
                }
                loUnit += 256 / 2 * UnitSize;
                for (int i = 0; i < 128; i++)
                    for (int k = 0; k < 8; k++)
                        for (int m = 0; m < 64; m += 8) binSumm[i, k + m] = (ushort)(BinScale - InitBinEsc[k] / (i + 2));
                for (int i = 0; i < 25; i++)
                    for (int k = 0; k < 16; k++)
                    {
                        seeShift[i * 16 + k] = PeriodBits - 4;
                        seeSumm[i * 16 + k] = (ushort)((5 * i + 10) << (PeriodBits - 4));
                        seeCount[i * 16 + k] = 4;
                    }
            }

            // ---- the memory allocator (units of 12 bytes; free lists by size index)

            void InsertNode(int node, int indx) { SetU32(node, freeList[indx]); freeList[indx] = node; }

            int RemoveNode(int indx)
            {
                int node = freeList[indx];
                freeList[indx] = U32(node);
                return node;
            }

            void SplitBlock(int ptr, int oldIndx, int newIndx)
            {
                int nu = Indx2Units[oldIndx] - Indx2Units[newIndx];
                ptr += Indx2Units[newIndx] * UnitSize;
                int i = Units2Indx[nu - 1];
                if (Indx2Units[i] != nu)
                {
                    int k = Indx2Units[--i];
                    InsertNode(ptr + k * UnitSize, nu - k - 1);
                }
                InsertNode(ptr, i);
            }

            // the free blocks in one list, neighbors joined, then back into the free lists
            void GlueFreeBlocks()
            {
                int head = Align + size, n = head;
                glueCount = 255;
                for (int i = 0; i < NumIndexes; i++)
                {
                    int nu = Indx2Units[i], next = freeList[i];
                    freeList[i] = 0;
                    while (next != 0)
                    {
                        int node = next;
                        SetU32(node + 4, n);
                        SetU32(n + 8, node);
                        n = node;
                        next = U32(node);
                        SetU16(node, 0);
                        SetU16(node + 2, nu);
                    }
                }
                SetU16(head, 1);
                SetU32(head + 4, n);
                SetU32(n + 8, head);
                if (loUnit != hiUnit) SetU16(loUnit, 1);
                while (n != head)
                {
                    int node = n, nu = U16(node + 2);
                    while (true)
                    {
                        int node2 = node + nu * UnitSize;
                        nu += U16(node2 + 2);
                        if (U16(node2) != 0 || nu >= 0x10000) break;
                        int prev2 = U32(node2 + 8), next2 = U32(node2 + 4);
                        SetU32(prev2 + 4, next2);
                        SetU32(next2 + 8, prev2);
                        SetU16(node + 2, nu);
                    }
                    n = U32(node + 4);
                }
                for (n = U32(head + 4); n != head;)
                {
                    int node = n, next = U32(node + 4), nu = U16(node + 2);
                    for (; nu > 128; nu -= 128, node += 128 * UnitSize) InsertNode(node, NumIndexes - 1);
                    int i = Units2Indx[nu - 1];
                    if (Indx2Units[i] != nu)
                    {
                        int k = Indx2Units[--i];
                        InsertNode(node + k * UnitSize, nu - k - 1);
                    }
                    InsertNode(node, i);
                    n = next;
                }
            }

            int AllocUnitsRare(int indx)
            {
                if (glueCount == 0)
                {
                    GlueFreeBlocks();
                    if (freeList[indx] != 0) return RemoveNode(indx);
                }
                int i = indx;
                do
                {
                    if (++i == NumIndexes)
                    {
                        int numBytes = Indx2Units[indx] * UnitSize;
                        glueCount--;
                        return unitsStart - text > numBytes ? (unitsStart -= numBytes) : 0;
                    }
                } while (freeList[i] == 0);
                int block = RemoveNode(i);
                SplitBlock(block, i, indx);
                return block;
            }

            int AllocUnits(int indx)
            {
                if (freeList[indx] != 0) return RemoveNode(indx);
                int numBytes = Indx2Units[indx] * UnitSize;
                if (numBytes <= hiUnit - loUnit)
                {
                    int block = loUnit;
                    loUnit += numBytes;
                    return block;
                }
                return AllocUnitsRare(indx);
            }

            int ShrinkUnits(int oldPtr, int oldNU, int newNU)
            {
                int i0 = Units2Indx[oldNU - 1], i1 = Units2Indx[newNU - 1];
                if (i0 == i1) return oldPtr;
                if (freeList[i1] != 0)
                {
                    int ptr = RemoveNode(i1);
                    Buffer.BlockCopy(mem, oldPtr, mem, ptr, newNU * UnitSize);
                    InsertNode(oldPtr, i0);
                    return ptr;
                }
                SplitBlock(oldPtr, i0, i1);
                return oldPtr;
            }

            // ---- the model

            int CreateSuccessors(bool skip)
            {
                int c = minContext, upBranch = Successor(foundState), symbol = mem[foundState], n = 0;
                if (!skip) ps[n++] = foundState;
                while (Suffix(c) != 0)
                {
                    c = Suffix(c);
                    int s;
                    if (NumStats(c) != 1) for (s = Stats(c); mem[s] != symbol; s += 6) { }
                    else s = c + 2;
                    int successor = Successor(s);
                    if (successor != upBranch)
                    {
                        c = successor;
                        if (n == 0) return c;
                        break;
                    }
                    if (n >= MaxO) return 0;
                    ps[n++] = s;
                }
                int upSymbol = mem[upBranch], upFreq;
                if (NumStats(c) == 1) upFreq = mem[c + 3];
                else
                {
                    if (c <= text) return 0;
                    int s;
                    for (s = Stats(c); mem[s] != upSymbol; s += 6) { }
                    uint cf = (uint)mem[s + 1] - 1, s0 = (uint)(SummFreq(c) - NumStats(c)) - cf;
                    if (2 * cf <= s0) upFreq = 1 + (5 * cf > s0 ? 1 : 0);
                    else
                    {
                        if (s0 == 0) throw new NotSupportedException("damaged packed data");
                        upFreq = 1 + (int)((2 * cf + 3 * s0 - 1) / (2 * s0));
                    }
                }
                do
                {
                    int c1;
                    if (hiUnit != loUnit) c1 = hiUnit -= UnitSize;
                    else if (freeList[0] != 0) c1 = RemoveNode(0);
                    else
                    {
                        c1 = AllocUnitsRare(0);
                        if (c1 == 0) return 0;
                    }
                    SetU16(c1, 1);
                    mem[c1 + 2] = (byte)upSymbol;
                    mem[c1 + 3] = (byte)upFreq;
                    SetU32(c1 + 4, upBranch + 1);
                    SetU32(c1 + 8, c);
                    SetSuccessor(ps[--n], c1);
                    c = c1;
                } while (n != 0);
                return c;
            }

            void UpdateModel()
            {
                int fs = foundState, fsSymbol = mem[fs], fsFreq = mem[fs + 1], fSuccessor = Successor(fs), c;
                if (fsFreq < MaxFreq / 4 && Suffix(minContext) != 0)
                {
                    c = Suffix(minContext);
                    if (NumStats(c) == 1)
                    {
                        if (mem[c + 3] < 32) mem[c + 3]++;
                    }
                    else
                    {
                        int s = Stats(c);
                        if (mem[s] != fsSymbol)
                        {
                            do { s += 6; } while (mem[s] != fsSymbol);
                            if (mem[s + 1] >= mem[s - 5]) { SwapStates(s, s - 6); s -= 6; }
                        }
                        if (mem[s + 1] < MaxFreq - 9)
                        {
                            mem[s + 1] += 2;
                            SetU16(c + 2, SummFreq(c) + 2);
                        }
                    }
                }
                if (orderFall == 0)
                {
                    minContext = maxContext = CreateSuccessors(true);
                    if (minContext == 0) { RestartModel(); return; }
                    SetSuccessor(foundState, minContext);
                    return;
                }
                mem[text++] = (byte)fsSymbol;
                int successor = text;
                if (text >= unitsStart) { RestartModel(); return; }
                if (fSuccessor != 0)
                {
                    if (fSuccessor <= successor)
                    {
                        int cs = CreateSuccessors(false);
                        if (cs == 0) { RestartModel(); return; }
                        fSuccessor = cs;
                    }
                    if (--orderFall == 0)
                    {
                        successor = fSuccessor;
                        if (maxContext != minContext) text--;
                    }
                }
                else
                {
                    SetSuccessor(foundState, successor);
                    fSuccessor = minContext;
                }
                int ns = NumStats(minContext);
                uint s0 = (uint)(SummFreq(minContext) - ns - (fsFreq - 1));
                for (c = maxContext; c != minContext; c = Suffix(c))
                {
                    int ns1 = NumStats(c);
                    if (ns1 != 1)
                    {
                        if ((ns1 & 1) == 0)   // a full block: one unit more
                        {
                            int oldNU = ns1 >> 1, i = Units2Indx[oldNU - 1];
                            if (i != Units2Indx[oldNU])
                            {
                                int ptr = AllocUnits(i + 1);
                                if (ptr == 0) { RestartModel(); return; }
                                int oldPtr = Stats(c);
                                Buffer.BlockCopy(mem, oldPtr, mem, ptr, oldNU * UnitSize);
                                InsertNode(oldPtr, i);
                                SetU32(c + 4, ptr);
                            }
                        }
                        int sum = SummFreq(c);
                        SetU16(c + 2, sum + (2 * ns1 < ns ? 1 : 0) + 2 * ((4 * ns1 <= ns ? 1 : 0) & (sum <= 8 * ns1 ? 1 : 0)));
                    }
                    else
                    {
                        int s = AllocUnits(0);
                        if (s == 0) { RestartModel(); return; }
                        CopyState(s, c + 2);
                        SetU32(c + 4, s);
                        int f = mem[s + 1];
                        mem[s + 1] = (byte)(f < MaxFreq / 4 - 1 ? f * 2 : MaxFreq - 4);
                        SetU16(c + 2, mem[s + 1] + initEsc + (ns > 3 ? 1 : 0));
                    }
                    uint cf = (uint)(2 * fsFreq * (SummFreq(c) + 6)), sf = s0 + (uint)SummFreq(c);
                    if (cf < 6 * sf)
                    {
                        cf = 1 + (cf > sf ? 1u : 0) + (cf >= 4 * sf ? 1u : 0);
                        SetU16(c + 2, SummFreq(c) + 3);
                    }
                    else
                    {
                        cf = 4 + (cf >= 9 * sf ? 1u : 0) + (cf >= 12 * sf ? 1u : 0) + (cf >= 15 * sf ? 1u : 0);
                        SetU16(c + 2, SummFreq(c) + (int)cf);
                    }
                    int added = Stats(c) + ns1 * 6;
                    SetSuccessor(added, successor);
                    mem[added] = (byte)fsSymbol;
                    mem[added + 1] = (byte)cf;
                    SetU16(c, ns1 + 1);
                }
                maxContext = minContext = fSuccessor;
            }

            void Rescale()
            {
                int mc = minContext, stats = Stats(mc), s = foundState;
                var tmp = new byte[6];
                if (s != stats)   // the found state to the front
                {
                    Buffer.BlockCopy(mem, s, tmp, 0, 6);
                    do { CopyState(s, s - 6); s -= 6; } while (s != stats);
                    Buffer.BlockCopy(tmp, 0, mem, s, 6);
                }
                uint escFreq = (uint)(SummFreq(mc) - mem[s + 1]);
                int adder = orderFall != 0 ? 1 : 0;
                mem[s + 1] = (byte)((mem[s + 1] + 4 + adder) >> 1);
                uint sumFreq = mem[s + 1];
                int i = NumStats(mc) - 1;
                do
                {
                    s += 6;
                    escFreq -= mem[s + 1];
                    mem[s + 1] = (byte)((mem[s + 1] + adder) >> 1);
                    sumFreq += mem[s + 1];
                    if (mem[s + 1] > mem[s - 5])   // kept in order, highest first
                    {
                        int s1 = s;
                        Buffer.BlockCopy(mem, s1, tmp, 0, 6);
                        do { CopyState(s1, s1 - 6); s1 -= 6; } while (s1 != stats && tmp[1] > mem[s1 - 5]);
                        Buffer.BlockCopy(tmp, 0, mem, s1, 6);
                    }
                } while (--i != 0);
                if (mem[s + 1] == 0)   // states down to 0 go
                {
                    int numStats = NumStats(mc);
                    i = 0;
                    do { i++; s -= 6; } while (mem[s + 1] == 0);
                    escFreq += (uint)i;
                    SetU16(mc, numStats - i);
                    if (numStats - i == 1)
                    {
                        Buffer.BlockCopy(mem, stats, tmp, 0, 6);
                        do { tmp[1] = (byte)(tmp[1] - (tmp[1] >> 1)); escFreq >>= 1; } while (escFreq > 1);
                        InsertNode(stats, Units2Indx[((numStats + 1) >> 1) - 1]);
                        foundState = mc + 2;
                        Buffer.BlockCopy(tmp, 0, mem, foundState, 6);
                        return;
                    }
                    int n0 = (numStats + 1) >> 1, n1 = (numStats - i + 1) >> 1;
                    if (n0 != n1) SetU32(mc + 4, ShrinkUnits(stats, n0, n1));
                }
                SetU16(mc + 2, (int)(sumFreq + escFreq - (escFreq >> 1)));
                foundState = Stats(mc);
            }

            // ---- decoding

            uint GetCurrentCount(uint scale) { return (code - low) / (range /= scale); }

            void Decode(uint lowCount, uint highCount)
            {
                low += range * lowCount;
                range *= highCount - lowCount;
            }

            void Normalize()
            {
                while (true)
                {
                    if ((low ^ (low + range)) >= Top)
                    {
                        if (range >= Bot) return;
                        range = (0u - low) & (Bot - 1);
                    }
                    code = code << 8 | (uint)GetChar();
                    range <<= 8;
                    low <<= 8;
                }
            }

            int MakeEscFreq(int diff, out uint scale)
            {
                int ns = NumStats(minContext);
                if (ns == 256) { scale = 1; return DummySee; }
                int see = NS2Indx[diff - 1] * 16 + (diff < NumStats(Suffix(minContext)) - ns ? 1 : 0)
                          + 2 * (SummFreq(minContext) < 11 * ns ? 1 : 0) + 4 * (numMasked > diff ? 1 : 0) + hiBitsFlag;
                uint r = (uint)(seeSumm[see] >> seeShift[see]);
                seeSumm[see] = (ushort)(seeSumm[see] - r);
                scale = r + (r == 0 ? 1u : 0);
                return see;
            }

            // in a context of several states: false = damaged
            bool DecodeSymbol1()
            {
                int p = Stats(minContext);
                uint scale = (uint)SummFreq(minContext), count = GetCurrentCount(scale);
                if (count >= scale) return false;
                uint hiCnt = mem[p + 1];
                if (count < hiCnt)
                {
                    Decode(0, hiCnt);
                    prevSuccess = 2 * hiCnt > scale ? 1 : 0;
                    runLength += prevSuccess;
                    foundState = p;
                    mem[p + 1] = (byte)(hiCnt + 4);
                    SetU16(minContext + 2, (int)scale + 4);
                    if (hiCnt + 4 > MaxFreq) Rescale();
                    return true;
                }
                if (foundState == 0) return false;
                prevSuccess = 0;
                int i = NumStats(minContext) - 1;
                while ((hiCnt += mem[(p += 6) + 1]) <= count)
                    if (--i == 0)   // none of them: an escape
                    {
                        hiBitsFlag = HB2Flag[mem[foundState]];
                        Decode(hiCnt, scale);
                        charMask[mem[p]] = escCount;
                        numMasked = NumStats(minContext);
                        foundState = 0;
                        for (i = numMasked - 1; i > 0; i--) charMask[mem[p -= 6]] = escCount;
                        return true;
                    }
                Decode(hiCnt - mem[p + 1], hiCnt);
                foundState = p;
                mem[p + 1] += 4;
                SetU16(minContext + 2, (int)scale + 4);
                if (mem[p + 1] > mem[p - 5])
                {
                    SwapStates(p, p - 6);
                    foundState = p -= 6;
                    if (mem[p + 1] > MaxFreq) Rescale();
                }
                return true;
            }

            // in a context of one state
            void DecodeBinSymbol()
            {
                int rs = minContext + 2;
                hiBitsFlag = HB2Flag[mem[foundState]];
                int i = mem[rs + 1] - 1;
                int j = prevSuccess + NS2BSIndx[NumStats(Suffix(minContext)) - 1] + hiBitsFlag + 2 * HB2Flag[mem[rs]] + ((runLength >> 26) & 0x20);
                uint bs = binSumm[i, j];
                if ((code - low) / (range >>= 14) < bs)
                {
                    foundState = rs;
                    if (mem[rs + 1] < 128) mem[rs + 1]++;
                    Decode(0, bs);
                    binSumm[i, j] = (ushort)(bs + 128 - ((bs + 32) >> PeriodBits));
                    prevSuccess = 1;
                    runLength++;
                }
                else
                {
                    Decode(bs, BinScale);
                    binSumm[i, j] = (ushort)(bs - ((bs + 32) >> PeriodBits));
                    initEsc = ExpEscape[binSumm[i, j] >> 10];
                    numMasked = 1;
                    charMask[mem[rs]] = escCount;
                    prevSuccess = 0;
                    foundState = 0;
                }
            }

            // in a shorter context, the symbols already masked left out: false = damaged
            bool DecodeSymbol2()
            {
                int i = NumStats(minContext) - numMasked, n = 0;
                uint scale;
                int see = MakeEscFreq(i, out scale);
                int p = Stats(minContext) - 6;
                uint hiCnt = 0;
                do
                {
                    do { p += 6; } while (charMask[mem[p]] == escCount);
                    hiCnt += mem[p + 1];
                    ps[n++] = p;
                } while (--i != 0);
                scale += hiCnt;
                uint count = GetCurrentCount(scale);
                if (count >= scale) return false;
                if (count < hiCnt)
                {
                    int k = 0;
                    p = ps[0];
                    hiCnt = 0;
                    while ((hiCnt += mem[p + 1]) <= count) p = ps[++k];
                    Decode(hiCnt - mem[p + 1], hiCnt);
                    if (seeShift[see] < PeriodBits && --seeCount[see] == 0)
                    {
                        seeSumm[see] = (ushort)(seeSumm[see] << 1);
                        seeCount[see] = (byte)(3 << seeShift[see]++);
                    }
                    foundState = p;
                    mem[p + 1] += 4;
                    SetU16(minContext + 2, SummFreq(minContext) + 4);
                    if (mem[p + 1] > MaxFreq) Rescale();
                    runLength = initRL;
                }
                else
                {
                    Decode(hiCnt, scale);
                    for (int k = 0; k < n; k++) charMask[mem[ps[k]]] = escCount;
                    seeSumm[see] = (ushort)(seeSumm[see] + scale);
                    numMasked = NumStats(minContext);
                }
                return true;
            }

            bool Valid(int at) { return at > text && at <= Align + size; }

            /// <summary>The next byte, or -1: damaged (or an escape from the shortest context).</summary>
            public int DecodeChar()
            {
                if (mem == null || !Valid(minContext)) return -1;
                escCount++;   // a fresh mask for this symbol
                if (NumStats(minContext) != 1)
                {
                    if (!Valid(Stats(minContext)) || !DecodeSymbol1()) return -1;
                }
                else DecodeBinSymbol();
                while (foundState == 0)
                {
                    Normalize();
                    do
                    {
                        orderFall++;
                        minContext = Suffix(minContext);
                        if (!Valid(minContext)) return -1;
                    } while (NumStats(minContext) == numMasked);
                    if (!DecodeSymbol2()) return -1;
                }
                int symbol = mem[foundState];
                if (orderFall == 0 && Successor(foundState) > text) minContext = maxContext = Successor(foundState);
                else UpdateModel();
                Normalize();
                return symbol;
            }
        }
    }
}
