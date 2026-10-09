// CRI ADX (type 3, 4-bit ADPCM): the game's music streams (two per cue, intro and loop, inside an AAX table).
// A frame is 32 samples of one channel in 18 bytes: a big-endian scale, then 32 signed nibbles; each sample =
// nibble * scale + (coef1 * hist1 + coef2 * hist2) >> 12, with coefficients from the high-pass cutoff (500 Hz) and the
// sample rate. Frames of the channels alternate. The game's streams (version 4) have a 40-byte header and end with
// an 18-byte end block (scale 0x8001, then 0x000E).
using System;
using System.IO;

namespace ExMoreStuff
{
    sealed class AdxInfo
    {
        public int Channels, SampleRate, Samples, HighPass = 500, Version = 4, HeaderSize = 40;
        // version 4 header 0x18: each channel's starting history, twice (h1, h2): CRI's encoder starts every stream
        // from its first sample, so the first coded value is 0 (checked on the game's banks: test\AdxHist.cs)
        public short[] History = new short[4];
    }

    static class Adx
    {
        public const int FrameSamples = 32, FrameBytes = 18;

        static void Coefficients(int cutoff, int rate, out int c1, out int c2)
        {
            double z = Math.Cos(2.0 * Math.PI * cutoff / rate);
            double a = Math.Sqrt(2.0) - z, b = Math.Sqrt(2.0) - 1.0;
            double c = (a - Math.Sqrt((a + b) * (a - b))) / b;
            c1 = (int)(c * 8192);
            c2 = (int)(c * c * -4096);
        }

        public static bool IsAdx(byte[] b) { return b != null && b.Length >= 32 && b[0] == 0x80 && b[1] == 0x00; }

        public static AdxInfo ReadHeader(byte[] b)
        {
            if (!IsAdx(b)) throw new InvalidDataException("not an ADX stream");
            int copyOff = b[2] << 8 | b[3];
            if (b[4] != 3) throw new InvalidDataException("ADX encoding " + b[4] + " isn't supported (only type 3)");
            if (b[5] != FrameBytes || b[6] != 4) throw new InvalidDataException("unexpected ADX frame layout");
            var i = new AdxInfo
            {
                HeaderSize = copyOff + 4,
                Channels = b[7],
                SampleRate = b[8] << 24 | b[9] << 16 | b[10] << 8 | b[11],
                Samples = b[12] << 24 | b[13] << 16 | b[14] << 8 | b[15],
                HighPass = b[16] << 8 | b[17],
                Version = b[18],
            };
            if (i.Version == 4 && i.HeaderSize >= 0x20)
                for (int k = 0; k < 4; k++) i.History[k] = (short)(b[0x18 + 2 * k] << 8 | b[0x19 + 2 * k]);
            return i;
        }

        /// <summary>Interleaved 16-bit samples of a stream (all its channels).</summary>
        public static short[] Decode(byte[] b, out AdxInfo info)
        {
            info = ReadHeader(b);
            int[,] start = null;
            if (info.Version == 4 && info.Channels <= 2)
            {
                start = new int[info.Channels, 2];
                for (int c = 0; c < info.Channels; c++) { start[c, 0] = info.History[2 * c]; start[c, 1] = info.History[2 * c + 1]; }
            }
            return DecodeFrom(b, info, start);
        }

        /// <summary>Decode with each channel's starting history (two samples before the first), or none.</summary>
        public static short[] DecodeFrom(byte[] b, AdxInfo info, int[,] start)
        {
            int ch = info.Channels, n = info.Samples;
            int c1, c2;
            Coefficients(info.HighPass, info.SampleRate, out c1, out c2);
            var o = new short[(long)n * ch];
            var h1 = new int[ch];
            var h2 = new int[ch];
            if (start != null)
                for (int c = 0; c < ch; c++) { h1[c] = start[c, 0]; h2[c] = start[c, 1]; }
            int frames = (n + FrameSamples - 1) / FrameSamples;
            int p = info.HeaderSize;
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < ch; c++, p += FrameBytes)
                {
                    if (p + FrameBytes > b.Length) return o;
                    int scale = b[p] << 8 | b[p + 1];
                    if ((scale & 0x8000) != 0) return o;      // end block
                    for (int s = 0; s < FrameSamples; s++)
                    {
                        int idx = f * FrameSamples + s;
                        int nib = b[p + 2 + s / 2];
                        nib = (s & 1) == 0 ? nib >> 4 : nib & 15;
                        if (nib >= 8) nib -= 16;
                        int v = nib * scale + ((c1 * h1[c] + c2 * h2[c]) >> 12);
                        v = v < -32768 ? -32768 : v > 32767 ? 32767 : v;
                        h2[c] = h1[c];
                        h1[c] = v;
                        if (idx < n) o[(long)idx * ch + c] = (short)v;
                    }
                }
            return o;
        }

        /// <summary>A version-4 stream as the game's banks hold them (40-byte header, end block), started as CRI's
        /// encoder does: each channel's history seeded with its first sample, written in the header.</summary>
        public static byte[] Encode(short[] pcm, int channels, int rate)
        {
            int n = pcm.Length / channels;
            var startHistory = new int[channels, 2];
            var header0x18 = new short[4];
            for (int c = 0; c < channels && c < 2; c++)
            {
                short first = n > 0 ? pcm[c] : (short)0;
                startHistory[c, 0] = startHistory[c, 1] = first;
                header0x18[2 * c] = header0x18[2 * c + 1] = first;
            }
            int c1, c2;
            Coefficients(500, rate, out c1, out c2);
            int frames = (n + FrameSamples - 1) / FrameSamples;
            var o = new byte[40 + frames * channels * FrameBytes + FrameBytes];
            o[0] = 0x80; o[1] = 0x00; o[2] = 0x00; o[3] = 0x24;                // copyright string at 0x22, data at 40
            o[4] = 3; o[5] = FrameBytes; o[6] = 4; o[7] = (byte)channels;
            Put32(o, 8, rate);
            Put32(o, 12, n);
            o[16] = 500 >> 8; o[17] = 500 & 255; o[18] = 4; o[19] = 0;
            for (int k = 0; k < 4; k++) { o[0x18 + 2 * k] = (byte)(header0x18[k] >> 8); o[0x19 + 2 * k] = (byte)header0x18[k]; }
            var cri = System.Text.Encoding.ASCII.GetBytes("(c)CRI");
            Buffer.BlockCopy(cri, 0, o, 0x22, 6);
            var h1 = new int[channels];
            var h2 = new int[channels];
            for (int c = 0; c < channels && c < 2; c++) { h1[c] = startHistory[c, 0]; h2[c] = startHistory[c, 1]; }
            int p = 40;
            var block = new int[FrameSamples];
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < channels; c++, p += FrameBytes)
                {
                    for (int s = 0; s < FrameSamples; s++)
                    {
                        long idx = (long)(f * FrameSamples + s);
                        block[s] = idx < n ? pcm[idx * channels + c] : 0;
                    }
                    EncodeFrame(block, c1, c2, ref h1[c], ref h2[c], o, p);
                }
            // end block: scale 0x8001, 0x000E (the bytes that follow), zeros
            o[p] = 0x80; o[p + 1] = 0x01; o[p + 2] = 0x00; o[p + 3] = 0x0E;
            return o;
        }

        // one frame: the scale from the open-loop prediction error, then closed loop (predicting from what the decoder
        // will have), trying a few scales around it and keeping the one with the least error
        static void EncodeFrame(int[] x, int c1, int c2, ref int h1, ref int h2, byte[] o, int p)
        {
            int peak = 0, e1 = h1, e2 = h2;
            for (int s = 0; s < FrameSamples; s++)
            {
                int pred = (c1 * e1 + c2 * e2) >> 12;
                int d = Math.Abs(x[s] - pred);
                if (d > peak) peak = d;
                e2 = e1; e1 = x[s];
            }
            int baseScale = Math.Max(1, (peak + 6) / 7);
            long bestErr = long.MaxValue;
            int bestScale = baseScale, bh1 = h1, bh2 = h2;
            var nibs = new int[FrameSamples];
            var best = new int[FrameSamples];
            foreach (int scale in new[] { baseScale, baseScale + baseScale / 8 + 1, baseScale + baseScale / 4 + 1, Math.Max(1, baseScale - baseScale / 8) })
            {
                if (scale > 0x7FFF) continue;
                int a1 = h1, a2 = h2;
                long err = 0;
                for (int s = 0; s < FrameSamples; s++)
                {
                    int pred = (c1 * a1 + c2 * a2) >> 12;
                    int q = (int)Math.Round((x[s] - pred) / (double)scale);
                    q = q < -8 ? -8 : q > 7 ? 7 : q;
                    int v = q * scale + pred;
                    v = v < -32768 ? -32768 : v > 32767 ? 32767 : v;
                    err += (long)(x[s] - v) * (x[s] - v);
                    nibs[s] = q;
                    a2 = a1; a1 = v;
                }
                if (err < bestErr) { bestErr = err; bestScale = scale; bh1 = a1; bh2 = a2; Array.Copy(nibs, best, FrameSamples); }
            }
            o[p] = (byte)(bestScale >> 8); o[p + 1] = (byte)bestScale;
            for (int s = 0; s < FrameSamples; s += 2)
                o[p + 2 + s / 2] = (byte)((best[s] & 15) << 4 | (best[s + 1] & 15));
            h1 = bh1; h2 = bh2;
        }

        static void Put32(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    }
}
