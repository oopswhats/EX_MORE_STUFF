// HCA (CRI's High Compression Audio, a DCT codec like AAC) decoded to floats: what Street Fighter X Tekken's music
// is stored in (SfxtMusic). Ported to C# from vgmstream's clHCA decoder (src/coding/libs/clhca.c; its tables in
// HcaTables.cs, from clhca_data.h): the original decompilation and C++ decoder by nyaga
// (github.com/Nyagamon/HCADecoder), ported to C by kode54, cleaned up and re-reverse engineered for HCA v3 by bnnm
// using Thealexbarney's VGAudio as reference. Keyless files only (SFxT's are; a key-encrypted one, ciph 56, is refused).
//
// vgmstream's license (github.com/vgmstream/vgmstream, COPYING):
//   Copyright (c) 2008-2025 Adam Gashlin, Fastelbja, Ronny Elfert, bnnm, Christopher Snowhill, NicknineTheEagle,
//   bxaimc, Thealexbarney, CyberBotX, EdnessP, et al
//
//   Permission to use, copy, modify, and distribute this software for any purpose with or without fee is hereby
//   granted, provided that the above copyright notice and this permission notice appear in all copies.
//
//   THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING
//   ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL,
//   DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
//   WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE
//   USE OR PERFORMANCE OF THIS SOFTWARE.
using System;
using System.IO;

namespace ExMoreStuff
{
    // One decoded HCA file: each channel's samples (encoder delay and padding taken off) and its loop, if it has one.
    sealed class HcaAudio
    {
        public int Channels, SampleRate;
        public float[][] Samples;              // [channel][sample], -1..1
        public int LoopStart = -1, LoopEnd = -1;   // in samples, end exclusive; -1: no loop
        public int Length { get { return Samples.Length == 0 ? 0 : Samples[0].Length; } }
    }

    static class Hca
    {
        const int Subframes = 8, SubframeSamples = 128, FrameSamples = Subframes * SubframeSamples, MdctBits = 7;
        const uint Mask = 0x7F7F7F7F;   // chunk names in an encrypted header
        const int V101 = 0x0101, V102 = 0x0102, V103 = 0x0103, V200 = 0x0200, V300 = 0x0300;
        enum Kind { Discrete, StereoPrimary, StereoSecondary }

        static float[] Floats(uint[] bits)
        {
            var f = new float[bits.Length];
            for (int i = 0; i < bits.Length; i++) f[i] = BitConverter.ToSingle(BitConverter.GetBytes(bits[i]), 0);
            return f;
        }

        static readonly float[] ScalingTable = Floats(HcaTables.hcadequantizer_scaling_table_float_hex);
        static readonly float[] RangeTable = Floats(HcaTables.hcadequantizer_range_table_float_hex);
        static readonly float[] ScaleConversion = Floats(HcaTables.hcadecoder_scale_conversion_table_hex);
        static readonly float[] IntensityRatio = Floats(HcaTables.hcadecoder_intensity_ratio_table_hex);
        static readonly float[] Window = Floats(HcaTables.hcaimdct_window_float_hex);
        static readonly float[][] SinTables = Array.ConvertAll(HcaTables.sin_tables_hex, Floats);
        static readonly float[][] CosTables = Array.ConvertAll(HcaTables.cos_tables_hex, Floats);

        sealed class Channel
        {
            public Kind Type;
            public int CodedCount;
            public readonly byte[] Intensity = new byte[Subframes];
            public readonly byte[] Scalefactors = new byte[SubframeSamples];
            public readonly byte[] Resolution = new byte[SubframeSamples];
            public readonly byte[] Noises = new byte[SubframeSamples];
            public int NoiseCount, ValidCount;
            public readonly float[] Gain = new float[SubframeSamples];
            public readonly float[][] Spectra = NewBlocks();
            public readonly float[] Temp = new float[SubframeSamples];
            public readonly float[] ImdctPrevious = new float[SubframeSamples];
            public readonly float[][] Wave = NewBlocks();

            static float[][] NewBlocks()
            {
                var b = new float[Subframes][];
                for (int i = 0; i < Subframes; i++) b[i] = new float[SubframeSamples];
                return b;
            }
        }

        sealed class Bits
        {
            readonly byte[] data;
            readonly int start, size;   // size in bits
            public int Bit;

            public Bits(byte[] data, int start, int bytes) { this.data = data; this.start = start; size = bytes * 8; }

            public uint Peek(int count)
            {
                if (count == 0 || Bit + count > size) return 0;   // past the end reads as 0, as the lib does
                ulong v = 0;
                int first = start + (Bit >> 3), rem = Bit & 7, need = (rem + count + 7) >> 3;
                for (int i = 0; i < need; i++) v = (v << 8) | data[first + i];
                v >>= need * 8 - rem - count;
                return (uint)(v & ((1ul << count) - 1));
            }

            public uint Read(int count) { uint v = Peek(count); Bit += count; return v; }
            public void Skip(int count) { Bit += count; }
        }

        static ushort Crc16(byte[] data, int start, int size)
        {
            ushort sum = 0;
            for (int i = 0; i < size; i++) sum = (ushort)((sum << 8) ^ HcaTables.hcacommon_crc_mask_table[(sum >> 8) ^ data[start + i]]);
            return sum;
        }

        /// <summary>Decodes a whole HCA file.</summary>
        public static HcaAudio Decode(byte[] file)
        {
            if (file == null || file.Length < 8) throw new InvalidDataException("not an HCA file");
            var br = new Bits(file, 0, Math.Min(file.Length, 0x10000));
            if ((br.Peek(32) & Mask) != 0x48434100) throw new InvalidDataException("not an HCA file");   // "HCA\0"
            br.Skip(32);
            int version = (int)br.Read(16), headerSize = (int)br.Read(16);
            if (version != V101 && version != V102 && version != V103 && version != V200 && version != V300) throw new InvalidDataException("HCA version " + version.ToString("X") + " isn't known");
            if (file.Length < headerSize || Crc16(file, 0, headerSize) != 0) throw new InvalidDataException("HCA header is damaged");
            int size = headerSize - 8;
            br = new Bits(file, 0, headerSize) { Bit = 64 };

            int channels, sampleRate, frameCount, encoderDelay, encoderPadding;
            if (size >= 0x10 && (br.Peek(32) & Mask) == 0x666D7400)   // "fmt\0"
            {
                br.Skip(32);
                channels = (int)br.Read(8); sampleRate = (int)br.Read(24); frameCount = (int)br.Read(32);
                encoderDelay = (int)br.Read(16); encoderPadding = (int)br.Read(16);
                if (channels < 1 || channels > 16 || frameCount == 0 || sampleRate < 1) throw new InvalidDataException("HCA format is damaged");
                size -= 0x10;
            }
            else throw new InvalidDataException("HCA has no format");

            int frameSize, minResolution, maxResolution, trackCount, channelConfig, totalBands, baseBands, stereoBands, bandsPerHfrGroup, msStereo, stereoType = 0;
            if (size >= 0x10 && (br.Peek(32) & Mask) == 0x636F6D70)   // "comp"
            {
                br.Skip(32);
                frameSize = (int)br.Read(16); minResolution = (int)br.Read(8); maxResolution = (int)br.Read(8);
                trackCount = (int)br.Read(8); channelConfig = (int)br.Read(8); totalBands = (int)br.Read(8);
                baseBands = (int)br.Read(8); stereoBands = (int)br.Read(8); bandsPerHfrGroup = (int)br.Read(8);
                msStereo = (int)br.Read(8); br.Read(8);
                size -= 0x10;
            }
            else if (size >= 0x0C && (br.Peek(32) & Mask) == 0x64656300)   // "dec\0"
            {
                br.Skip(32);
                frameSize = (int)br.Read(16); minResolution = (int)br.Read(8); maxResolution = (int)br.Read(8);
                totalBands = (int)br.Read(8) + 1; baseBands = (int)br.Read(8) + 1;
                trackCount = (int)br.Read(4); channelConfig = (int)br.Read(4); stereoType = (int)br.Read(8);
                if (stereoType == 0) baseBands = totalBands;
                stereoBands = totalBands - baseBands;
                bandsPerHfrGroup = 0; msStereo = 0;
                size -= 0x0C;
            }
            else throw new InvalidDataException("HCA has no compression info");

            if (size >= 0x08 && (br.Peek(32) & Mask) == 0x76627200)   // "vbr\0": not supported by the lib either way
            {
                br.Skip(32);
                br.Read(16); br.Read(16);
                if (frameSize != 0) throw new InvalidDataException("HCA variable bit rate isn't supported");
                size -= 0x08;
            }

            int athType;
            if (size >= 0x06 && (br.Peek(32) & Mask) == 0x61746800)   // "ath\0"
            {
                br.Skip(32);
                athType = (int)br.Read(16);
            }
            else athType = version < V200 ? 1 : 0;

            var audio = new HcaAudio { Channels = channels, SampleRate = sampleRate };
            if (size >= 0x10 && (br.Peek(32) & Mask) == 0x6C6F6F70)   // "loop"
            {
                br.Skip(32);
                int startFrame = (int)br.Read(32), endFrame = (int)br.Read(32), startDelay = (int)br.Read(16), endPadding = (int)br.Read(16);
                if (startFrame > endFrame || endFrame >= frameCount) throw new InvalidDataException("HCA loop is damaged");
                audio.LoopStart = startFrame * FrameSamples - encoderDelay + startDelay;
                audio.LoopEnd = endFrame * FrameSamples - encoderDelay + (FrameSamples - endPadding);
                size -= 0x10;
            }

            int cipher = 0;
            if (size >= 0x06 && (br.Peek(32) & Mask) == 0x63697068)   // "ciph"
            {
                br.Skip(32);
                cipher = (int)br.Read(16);
                if (cipher == 56) throw new InvalidDataException("this HCA file is encrypted with a key");
                if (cipher != 0 && cipher != 1) throw new InvalidDataException("HCA encryption " + cipher + " isn't known");
            }

            if (frameSize < 8 || frameSize > 0xFFFF) throw new InvalidDataException("HCA frame size is damaged");
            if (version <= V200 ? minResolution != 1 || maxResolution != 15 : minResolution > maxResolution || maxResolution > 15)
                throw new InvalidDataException("HCA resolution is damaged");
            if (trackCount == 0) trackCount = 1;
            if (trackCount > channels) throw new InvalidDataException("HCA tracks are damaged");
            if (totalBands > SubframeSamples || totalBands == 0 || baseBands + stereoBands > totalBands || baseBands + stereoBands == 0 ||
                stereoBands > baseBands || bandsPerHfrGroup > SubframeSamples)
                throw new InvalidDataException("HCA bands are damaged");
            if (msStereo != 0) throw new InvalidDataException("HCA mid/side stereo isn't supported");
            int hfrGroups = bandsPerHfrGroup < 1 ? 0 : (totalBands - baseBands - stereoBands + bandsPerHfrGroup - 1) / bandsPerHfrGroup;

            // channels: their types (pairs share stereo bands) and coded counts
            var types = new Kind[channels];
            int perTrack = channels / trackCount;
            if (stereoBands > 0 && perTrack > 1)
                for (int t = 0; t < trackCount; t++)
                {
                    int b = t * perTrack;
                    Kind[] layout;
                    switch (perTrack)
                    {
                        case 2: layout = new[] { Kind.StereoPrimary, Kind.StereoSecondary }; break;
                        case 3: layout = new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete }; break;
                        case 4: layout = channelConfig == 0 ? new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.StereoPrimary, Kind.StereoSecondary }
                                                            : new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete, Kind.Discrete }; break;
                        case 5: layout = channelConfig <= 2 ? new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete, Kind.StereoPrimary, Kind.StereoSecondary }
                                                            : new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete, Kind.Discrete, Kind.Discrete }; break;
                        case 6: layout = new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete, Kind.Discrete, Kind.StereoPrimary, Kind.StereoSecondary }; break;
                        case 7: layout = new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete, Kind.Discrete, Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete }; break;
                        case 8: layout = new[] { Kind.StereoPrimary, Kind.StereoSecondary, Kind.Discrete, Kind.Discrete, Kind.StereoPrimary, Kind.StereoSecondary, Kind.StereoPrimary, Kind.StereoSecondary }; break;
                        default: layout = new Kind[perTrack]; break;
                    }
                    for (int i = 0; i < perTrack; i++) types[b + i] = layout[i];
                }
            var ch = new Channel[channels];
            for (int i = 0; i < channels; i++)
                ch[i] = new Channel { Type = types[i], CodedCount = baseBands + (types[i] == Kind.StereoSecondary ? 0 : stereoBands) };

            var ath = new byte[SubframeSamples];
            if (athType == 1)
            {
                uint acc = 0;
                for (int i = 0; i < SubframeSamples; i++)
                {
                    acc += (uint)sampleRate;
                    uint index = acc >> 13;
                    if (index >= 654) { for (int j = i; j < SubframeSamples; j++) ath[j] = 0xFF; break; }
                    ath[i] = HcaTables.ath_base_curve[index];
                }
            }
            else if (athType != 0) throw new InvalidDataException("HCA ATH " + athType + " isn't known");

            var cipherTable = new byte[256];
            for (int i = 0; i < 256; i++) cipherTable[i] = (byte)i;
            if (cipher == 1)
            {
                int v = 0;
                for (int i = 1; i < 255; i++)
                {
                    v = (v * 13 + 11) & 0xFF;
                    if (v == 0 || v == 0xFF) v = (v * 13 + 11) & 0xFF;
                    cipherTable[i] = (byte)v;
                }
            }

            // the frames
            if ((long)headerSize + (long)frameCount * frameSize > file.Length) throw new InvalidDataException("HCA file is cut short");
            int total = frameCount * FrameSamples;
            var output = new float[channels][];
            for (int c = 0; c < channels; c++) output[c] = new float[total];
            var frame = new byte[frameSize];
            uint random = 1;
            for (int f = 0; f < frameCount; f++)
            {
                Buffer.BlockCopy(file, headerSize + f * frameSize, frame, 0, frameSize);
                var fb = new Bits(frame, 0, frameSize);
                if (fb.Read(16) != 0xFFFF) throw new InvalidDataException("HCA frame " + f + " is damaged");
                if (Crc16(frame, 0, frameSize) != 0) throw new InvalidDataException("HCA frame " + f + " is damaged");
                if (cipher != 0) for (int i = 0; i < frameSize; i++) frame[i] = cipherTable[frame[i]];

                uint acceptableNoise = fb.Read(9), evaluationBoundary = fb.Read(7);
                uint packedNoise = unchecked((acceptableNoise << 8) - evaluationBoundary);
                foreach (Channel c in ch)
                {
                    UnpackScalefactors(c, fb, hfrGroups, version);
                    UnpackIntensity(c, fb, hfrGroups, version);
                    CalculateResolution(c, packedNoise, ath, minResolution, maxResolution);
                    for (int i = 0; i < c.CodedCount; i++) c.Gain[i] = ScalingTable[c.Scalefactors[i]] * RangeTable[c.Resolution[i]];
                }
                for (int sf = 0; sf < Subframes; sf++)
                    foreach (Channel c in ch) Dequantize(c, fb, sf);

                for (int sf = 0; sf < Subframes; sf++)
                {
                    int hfrChannels = (channelConfig & 0x80) != 0 ? 1 : channels;   // ambisonics
                    for (int c = 0; c < hfrChannels; c++)
                    {
                        ReconstructNoise(ch[c], minResolution, msStereo, ref random, sf);
                        ReconstructHighFrequency(ch[c], hfrGroups, bandsPerHfrGroup, stereoBands, baseBands, totalBands, version, sf);
                    }
                    if (stereoBands > 0)
                        for (int c = 0; c < channels - 1; c++) IntensityStereo(ch[c], ch[c + 1], sf, baseBands, totalBands);
                    foreach (Channel c in ch) Imdct(c, sf);
                }
                for (int c = 0; c < channels; c++)
                    for (int sf = 0; sf < Subframes; sf++)
                        Array.Copy(ch[c].Wave[sf], 0, output[c], f * FrameSamples + sf * SubframeSamples, SubframeSamples);
            }

            int length = Math.Max(0, total - encoderDelay - encoderPadding);
            audio.Samples = new float[channels][];
            for (int c = 0; c < channels; c++)
            {
                audio.Samples[c] = new float[length];
                Array.Copy(output[c], Math.Min(encoderDelay, total), audio.Samples[c], 0, Math.Min(length, total - Math.Min(encoderDelay, total)));
            }
            if (audio.LoopEnd > length) audio.LoopEnd = length;
            if (audio.LoopStart < 0 || audio.LoopStart >= audio.LoopEnd) audio.LoopStart = audio.LoopEnd = -1;
            return audio;
        }

        // scale indexes to normalize the dequantized coefficients
        static void UnpackScalefactors(Channel ch, Bits br, int hfrGroups, int version)
        {
            int cs = ch.CodedCount;
            int hs = ch.Type == Kind.StereoSecondary || hfrGroups == 0 || version <= V200 ? 0 : hfrGroups;
            int count = cs + hs;
            int deltaBits = (int)br.Read(3);
            if (deltaBits >= 6)
            {
                for (int i = 0; i < count; i++) ch.Scalefactors[i] = (byte)br.Read(6);
            }
            else if (deltaBits > 0)
            {
                int expected = (1 << deltaBits) - 1;
                int value = (int)br.Read(6);
                ch.Scalefactors[0] = (byte)value;
                for (int i = 1; i < count; i++)
                {
                    int delta = (int)br.Read(deltaBits);
                    if (delta == expected) value = (int)br.Read(6);
                    else
                    {
                        int test = value + (delta - (expected >> 1));
                        if (test < 0 || test >= 64) throw new InvalidDataException("HCA scale is damaged");
                        value = (value - (expected >> 1) + delta) & 0x3F;
                    }
                    ch.Scalefactors[i] = (byte)value;
                }
            }
            else
                Array.Clear(ch.Scalefactors, 0, SubframeSamples);
            for (int i = hs; i > 0; i--) ch.Scalefactors[SubframeSamples - 1 - hs + i] = ch.Scalefactors[cs - 1 + i];
        }

        // joint stereo intensity (the second channel of a pair), or v2.0 high-frequency scales (the others)
        static void UnpackIntensity(Channel ch, Bits br, int hfrGroups, int version)
        {
            if (ch.Type == Kind.StereoSecondary)
            {
                if (version <= V200)
                {
                    int value = (int)br.Peek(4);
                    ch.Intensity[0] = (byte)value;
                    if (value < 15)
                    {
                        br.Skip(4);
                        for (int i = 1; i < Subframes; i++) ch.Intensity[i] = (byte)br.Read(4);
                    }
                }
                else
                {
                    int value = (int)br.Peek(4);
                    if (value < 15)
                    {
                        br.Skip(4);
                        int deltaBits = (int)br.Read(2);
                        ch.Intensity[0] = (byte)value;
                        if (deltaBits == 3)
                            for (int i = 1; i < Subframes; i++) ch.Intensity[i] = (byte)br.Read(4);
                        else
                        {
                            int max = (2 << deltaBits) - 1, bits = deltaBits + 1;
                            for (int i = 1; i < Subframes; i++)
                            {
                                int delta = (int)br.Read(bits);
                                if (delta == max) value = (int)br.Read(4);
                                else
                                {
                                    value = (value - (max >> 1) + delta) & 0xFF;
                                    if (value > 15) throw new InvalidDataException("HCA intensity is damaged");
                                }
                                ch.Intensity[i] = (byte)value;
                            }
                        }
                    }
                    else
                    {
                        br.Skip(4);
                        for (int i = 0; i < Subframes; i++) ch.Intensity[i] = 7;
                    }
                }
            }
            else if (version <= V200)
                for (int i = 0; i < hfrGroups; i++) ch.Scalefactors[SubframeSamples - hfrGroups + i] = (byte)br.Read(6);
        }

        // the resolution (range of values) of each coded coefficient
        static void CalculateResolution(Channel ch, uint packedNoise, byte[] ath, int minResolution, int maxResolution)
        {
            int noise = 0, valid = 0;
            for (int i = 0; i < ch.CodedCount; i++)
            {
                int resolution = 0, scalefactor = ch.Scalefactors[i];
                if (scalefactor > 0)
                {
                    int noiseLevel = ath[i] + (int)((packedNoise + (uint)i) >> 8);
                    int curve = noiseLevel + 1 - ((5 * scalefactor) >> 1);
                    resolution = curve < 0 ? 15 : curve <= 65 ? HcaTables.hcadecoder_invert_table[curve] : 0;
                    if (resolution > maxResolution) resolution = maxResolution;
                    else if (resolution < minResolution) resolution = minResolution;
                    if (resolution < 1) ch.Noises[noise++] = (byte)i;
                    else ch.Noises[SubframeSamples - 1 - valid++] = (byte)i;
                }
                ch.Resolution[i] = (byte)resolution;
            }
            ch.NoiseCount = noise;
            ch.ValidCount = valid;
            Array.Clear(ch.Resolution, ch.CodedCount, SubframeSamples - ch.CodedCount);
        }

        static void Dequantize(Channel ch, Bits br, int sf)
        {
            float[] spectra = ch.Spectra[sf];
            for (int i = 0; i < ch.CodedCount; i++)
            {
                int resolution = ch.Resolution[i], bits = HcaTables.hcatbdecoder_max_bit_table[resolution];
                uint code = br.Read(bits);
                float qc;
                if (resolution > 7)
                {
                    int signedCode = ((code & 1) != 0 ? -1 : 1) * (int)(code >> 1);   // sign in the lowest bit
                    if (signedCode == 0) br.Skip(-1);                                     // zero has no sign bit
                    qc = signedCode;
                }
                else
                {
                    int index = (resolution << 4) + (int)code;
                    br.Skip(HcaTables.hcatbdecoder_read_bit_table[index] - bits);
                    qc = HcaTables.hcatbdecoder_read_val_table[index];
                }
                spectra[i] = ch.Gain[i] * qc;
            }
            Array.Clear(spectra, ch.CodedCount, SubframeSamples - ch.CodedCount);
        }

        // resolution 0 coefficients: pseudo-random noise from the coded ones
        static void ReconstructNoise(Channel ch, int minResolution, int msStereo, ref uint random, int sf)
        {
            if (minResolution > 0 || ch.ValidCount == 0 || ch.NoiseCount == 0) return;
            if (msStereo != 0 && ch.Type != Kind.StereoPrimary) return;
            float[] spectra = ch.Spectra[sf];
            for (int i = 0; i < ch.NoiseCount; i++)
            {
                random = unchecked(0x343FD * random + 0x269EC3);
                uint randomOut = (random >> 16) & 0x7FFF;
                int randomIndex = SubframeSamples - ch.ValidCount + (int)((randomOut * (uint)ch.ValidCount) >> 15);
                int target = ch.Noises[i], source = ch.Noises[randomIndex];
                int sc = ch.Scalefactors[target] - ch.Scalefactors[source] + 62;
                if (sc < 0) sc = 0;
                spectra[target] = ScaleConversion[sc] * spectra[source];
            }
        }

        // the high bands that weren't coded, made from the lower ones
        static void ReconstructHighFrequency(Channel ch, int hfrGroups, int bandsPerGroup, int stereoBands, int baseBands, int totalBands, int version, int sf)
        {
            if (bandsPerGroup == 0 || ch.Type == Kind.StereoSecondary) return;
            float[] spectra = ch.Spectra[sf];
            int start = stereoBands + baseBands, high = start, low = start - 1, hfrScales = SubframeSamples - hfrGroups;
            int limit = version <= V200 ? hfrGroups : hfrGroups >> 1;
            for (int group = 0; group < hfrGroups; group++)
            {
                int adjust = group < limit ? -1 : 1;
                if (high >= totalBands || low < 0) break;
                for (int i = 0; i < bandsPerGroup; i++)
                {
                    if (high >= totalBands || low < 0) break;
                    int sc = ch.Scalefactors[hfrScales + group] - ch.Scalefactors[low] + 63;
                    if (sc < 0) sc = 0;
                    spectra[high] = ScaleConversion[sc] * spectra[low];
                    high++;
                    low += adjust;
                }
            }
            if (high > 0) spectra[high - 1] = 0f;
        }

        static void IntensityStereo(Channel left, Channel right, int sf, int baseBands, int totalBands)
        {
            if (left.Type != Kind.StereoPrimary) return;
            float ratioL = IntensityRatio[right.Intensity[sf]], ratioR = 2.0f - ratioL;
            float[] l = left.Spectra[sf], r = right.Spectra[sf];
            for (int band = baseBands; band < totalBands; band++)
            {
                float coef = l[band];
                l[band] = coef * ratioL;
                r[band] = coef * ratioR;
            }
        }

        // DCT-IV of the spectra, then the windowed overlap with the previous subframe
        static void Imdct(Channel ch, int sf)
        {
            const int size = SubframeSamples, half = SubframeSamples / 2;
            float[] spectra = ch.Spectra[sf];
            float[] src = spectra, dst = ch.Temp;
            int count1 = 1, count2 = half;
            for (int i = 0; i < MdctBits; i++)   // pre-rotation butterflies
            {
                int s = 0, d1 = 0, d2 = count2;
                for (int j = 0; j < count1; j++)
                {
                    for (int k = 0; k < count2; k++)
                    {
                        float a = src[s++], b = src[s++];
                        dst[d1++] = a + b;
                        dst[d2++] = a - b;
                    }
                    d1 += count2;
                    d2 += count2;
                }
                float[] swap = src; src = dst; dst = swap;
                count1 <<= 1;
                count2 >>= 1;
            }
            src = ch.Temp; dst = spectra;
            count1 = half; count2 = 1;
            for (int i = 0; i < MdctBits; i++)   // twiddles
            {
                float[] sin = SinTables[i], cos = CosTables[i];
                int t = 0, d1 = 0, d2 = count2 * 2 - 1, s1 = 0, s2 = count2;
                for (int j = 0; j < count1; j++)
                {
                    for (int k = 0; k < count2; k++)
                    {
                        float a = src[s1++], b = src[s2++], sn = sin[t], cs = cos[t];
                        t++;
                        dst[d1++] = a * sn - b * cs;
                        dst[d2--] = a * cs + b * sn;
                    }
                    s1 += count2;
                    s2 += count2;
                    d1 += count2;
                    d2 += count2 * 3;
                }
                float[] swap = src; src = dst; dst = swap;
                count1 >>= 1;
                count2 <<= 1;
            }
            float[] dct = spectra, prev = ch.ImdctPrevious, wave = ch.Wave[sf];
            for (int i = 0; i < half; i++)
            {
                wave[i] = Window[i] * dct[i + half] + prev[i];
                wave[i + half] = Window[i + half] * dct[size - 1 - i] - prev[i + half];
                prev[i] = Window[size - 1 - i] * dct[half - i - 1];
                prev[i + half] = Window[half - i - 1] * dct[i];
            }
        }
    }
}
