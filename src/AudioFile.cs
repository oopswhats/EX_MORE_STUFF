// Songs for the Music page: any WAV, and MP3 / FLAC / M4A (AAC) / WMA through Windows' own Media Foundation
// decoders (nothing to install or ship), made 44.1 kHz stereo 16-bit as the game's music is. OGG isn't decoded by
// Windows: convert it first.
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ExMoreStuff
{
    /// <summary>A song in memory: 44.1 kHz, stereo, interleaved 16-bit.</summary>
    sealed class Song
    {
        public const int Rate = 44100, Channels = 2;
        public short[] Pcm;
        public string Name;
        public int LoopStart = -1, LoopEnd = -1;   // from the file (a WAV's loop, a game theme's), or -1
        public string Settings;                     // EX More Stuff's own, saved with a WAV
        public bool GameLoop;                       // the loop is the game's own (a game theme's, SFxT's): seamless as it is
        public int Frames { get { return Pcm.Length / Channels; } }
        public double Seconds { get { return Frames / (double)Rate; } }
    }

    static class AudioFile
    {

        public static Song Load(string path)
        {
            float[] samples;
            int rate, channels;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ogg" || ext == ".oga") throw new NotSupportedException("Windows can't decode OGG files: convert it to WAV, MP3 or FLAC first");
            byte[] wav = ext == ".wav" ? File.ReadAllBytes(path) : null;
            if (wav != null && TryReadWav(wav, out samples, out rate, out channels)) { }
            else
            {
                // Windows' own decoders (Media Foundation); only the "N" editions of Windows come without them
                try { MediaFoundation.Decode(path, out samples, out rate, out channels); }
                catch (DllNotFoundException)
                {
                    throw new NotSupportedException("This Windows has no media decoders (a Windows \"N\" edition): install Microsoft's free " +
                                                    "Media Feature Pack, or use a WAV file");
                }
            }
            var song = new Song { Name = Path.GetFileNameWithoutExtension(path), Pcm = ToGameFormat(samples, rate, channels) };
            if (wav != null)
            {
                int ls, le;
                ReadWavExtras(wav, out ls, out le, out song.Settings);
                if (ls >= 0 && le > ls && le <= song.Frames) { song.LoopStart = ls; song.LoopEnd = le; }
            }
            return song;
        }

        /// <summary>Any rate and channel count to 44.1 kHz stereo 16-bit: mono doubled, more than two channels folded
        /// down, other rates resampled (windowed sinc).</summary>
        public static short[] ToGameFormat(float[] x, int rate, int channels)
        {
            int frames = x.Length / channels;
            var l = new float[frames];
            var r = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                if (channels == 1) { l[i] = r[i] = x[i]; continue; }
                float a = x[i * channels], b = x[i * channels + 1];
                for (int c = 2; c < channels; c++) { a += 0.5f * x[i * channels + c]; b += 0.5f * x[i * channels + c]; }
                l[i] = a; r[i] = b;
            }
            if (rate != Song.Rate) { l = Resample(l, rate, Song.Rate); r = Resample(r, rate, Song.Rate); }
            var o = new short[l.Length * 2];
            for (int i = 0; i < l.Length; i++) { o[2 * i] = Clip(l[i]); o[2 * i + 1] = Clip(r[i]); }
            return o;
        }

        static short Clip(float v)
        {
            int s = (int)Math.Round(v * 32768f);     // the scale 16-bit files are read at, so they come back exactly
            return (short)(s < -32768 ? -32768 : s > 32767 ? 32767 : s);
        }

        // band-limited resampling: a Blackman-windowed sinc, 32 taps each side, cut off just under the lower Nyquist;
        // the kernel tabulated at 1/256-sample steps (a 4-minute 48 kHz song converts in about a second)
        static float[] Resample(float[] x, int from, int to)
        {
            const int taps = 32, steps = 256;
            long n = (long)x.Length * to / from;
            var o = new float[n];
            double step = from / (double)to, cutoff = Math.Min(1.0, to / (double)from) * 0.97;
            var kernel = new float[taps * steps + 1];
            for (int i = 0; i <= taps * steps; i++)
            {
                double d = i / (double)steps, arg = Math.PI * d * cutoff;
                double sinc = d < 1e-9 ? 1.0 : Math.Sin(arg) / arg;
                double w = 0.42 + 0.5 * Math.Cos(Math.PI * d / taps) + 0.08 * Math.Cos(2 * Math.PI * d / taps);
                kernel[i] = (float)(sinc * w);
            }
            for (long i = 0; i < n; i++)
            {
                double t = i * step;
                long c = (long)Math.Floor(t);
                double frac = t - c;
                float sum = 0, wsum = 0;
                for (int k = -taps + 1; k <= taps; k++)
                {
                    long idx = c + k;
                    double d = Math.Abs(frac - k) * steps;
                    int di = (int)d;
                    if (di >= taps * steps) continue;
                    float kv = kernel[di] + (kernel[di + 1] - kernel[di]) * (float)(d - di);
                    wsum += kv;
                    if (idx >= 0 && idx < x.Length) sum += x[idx] * kv;
                }
                o[i] = wsum != 0 ? sum / wsum : 0;
            }
            return o;
        }

        /// <summary>A RIFF WAVE file: PCM 8/16/24/32-bit or 32-bit float, any rate and channel count.</summary>
        public static bool TryReadWav(byte[] b, out float[] samples, out int rate, out int channels)
        {
            samples = null; rate = 0; channels = 0;
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A') return false;
            int format = 0, bits = 0, p = 12;
            while (p + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
                // sizes read unsigned and kept inside the file (pipe-written WAVs say 0xFFFFFFFF), so p always moves on
                int len = (int)Math.Min(BitConverter.ToUInt32(b, p + 4), (uint)(b.Length - (p + 8)));
                int body = p + 8;
                if (id == "fmt ")
                {
                    format = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xFFFE && len >= 40) format = BitConverter.ToUInt16(b, body + 24);   // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data" && channels > 0)
                {
                    len = Math.Min(len, b.Length - body);
                    int bytes = bits / 8;
                    if (bytes == 0 || (format != 1 && format != 3)) return false;
                    int count = len / bytes;
                    samples = new float[count - count % channels];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        int o = body + i * bytes;
                        if (format == 3 && bits == 32) samples[i] = BitConverter.ToSingle(b, o);
                        else if (bits == 8) samples[i] = (b[o] - 128) / 128f;
                        else if (bits == 16) samples[i] = BitConverter.ToInt16(b, o) / 32768f;
                        else if (bits == 24) samples[i] = ((b[o] << 8 | b[o + 1] << 16 | b[o + 2] << 24) >> 8) / 8388608f;
                        else if (bits == 32) samples[i] = BitConverter.ToInt32(b, o) / 2147483648f;
                        else return false;
                    }
                    return true;
                }
                p = body + len + (len & 1);
            }
            return false;
        }

        /// <summary>The song for low health: its bass turned well down (a low shelf, -24 dB under 300 Hz) and its mids a
        /// little (-4 dB around 1 kHz). Timing is kept sample for sample, so it can play in step with the song.</summary>
        public static short[] BassCut(short[] pcm)
        {
            var shelf = LowShelf(300, -24);
            var mid = Peaking(1000, -4, 0.8);
            var o = new short[pcm.Length];
            for (int c = 0; c < 2; c++)
            {
                var s1 = new double[4];
                var s2 = new double[4];
                for (int i = c; i < pcm.Length; i += 2)
                {
                    double y = Biquad(mid, s2, Biquad(shelf, s1, pcm[i]));
                    o[i] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(y)));
                }
            }
            return o;
        }

        // RBJ cookbook biquads as b0 b1 b2 a1 a2 (a0 = 1); state: x1 x2 y1 y2
        static double[] LowShelf(double hz, double db)
        {
            double a = Math.Pow(10, db / 40), w = 2 * Math.PI * hz / Song.Rate, cos = Math.Cos(w), alpha = Math.Sin(w) / 2 * Math.Sqrt(2);
            double sq = 2 * Math.Sqrt(a) * alpha, a0 = (a + 1) + (a - 1) * cos + sq;
            return new[]
            {
                a * ((a + 1) - (a - 1) * cos + sq) / a0, 2 * a * ((a - 1) - (a + 1) * cos) / a0, a * ((a + 1) - (a - 1) * cos - sq) / a0,
                -2 * ((a - 1) + (a + 1) * cos) / a0, ((a + 1) + (a - 1) * cos - sq) / a0,
            };
        }

        static double[] Peaking(double hz, double db, double q)
        {
            double a = Math.Pow(10, db / 40), w = 2 * Math.PI * hz / Song.Rate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha / a;
            return new[] { (1 + alpha * a) / a0, -2 * cos / a0, (1 - alpha * a) / a0, -2 * cos / a0, (1 - alpha / a) / a0 };
        }

        static double Biquad(double[] k, double[] s, double x)
        {
            double y = k[0] * x + k[1] * s[0] + k[2] * s[1] - k[3] * s[2] - k[4] * s[3];
            s[1] = s[0]; s[0] = x; s[3] = s[2]; s[2] = y;
            return y;
        }

        /// <summary>A WAV's loop (from its "smpl" chunk, as samplers and many audio tools write it; the end made
        /// exclusive, both scaled to 44.1 kHz) and EX More Stuff's own settings (an "exms" chunk), when it has them.</summary>
        public static void ReadWavExtras(byte[] b, out int loopStart, out int loopEnd, out string settings)
        {
            loopStart = loopEnd = -1;
            settings = null;
            if (b.Length < 12 || b[0] != 'R' || b[8] != 'W') return;
            int rate = Song.Rate, p = 12;
            while (p + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
                int len = BitConverter.ToInt32(b, p + 4), body = p + 8;
                if (len < 0 || body + len > b.Length) break;
                if (id == "fmt ") rate = Math.Max(1, BitConverter.ToInt32(b, body + 4));
                else if (id == "smpl" && len >= 60 && BitConverter.ToInt32(b, body + 28) > 0)
                {
                    long s = BitConverter.ToUInt32(b, body + 36 + 8), e = BitConverter.ToUInt32(b, body + 36 + 12) + 1L;
                    loopStart = (int)(s * Song.Rate / rate);
                    loopEnd = (int)(e * Song.Rate / rate);
                }
                else if (id == "exms") settings = System.Text.Encoding.UTF8.GetString(b, body, len);
                p = body + len + (len & 1);
            }
        }

        /// <summary>A 44.1 kHz stereo WAV with its loop in a "smpl" chunk (end inclusive, as the format has it) and
        /// EX More Stuff's settings in an "exms" chunk (other programs skip chunks they don't know).</summary>
        public static void WriteLoopedWav(string path, short[] pcm, int loopStart, int loopEnd, string settings)
        {
            byte[] extra = settings == null ? new byte[0] : System.Text.Encoding.UTF8.GetBytes(settings);
            int bytes = pcm.Length * 2, pad = extra.Length & 1;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(4 + 24 + 8 + bytes + 8 + 60 + 8 + extra.Length + pad);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Song.Rate); w.Write(Song.Rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                var buffer = new byte[bytes];
                Buffer.BlockCopy(pcm, 0, buffer, 0, bytes);
                w.Write(buffer);
                w.Write(new[] { 's', 'm', 'p', 'l' }); w.Write(60);
                w.Write(0); w.Write(0); w.Write(1000000000 / Song.Rate); w.Write(60); w.Write(0); w.Write(0); w.Write(0); w.Write(1); w.Write(0);
                w.Write(0); w.Write(0); w.Write(loopStart); w.Write(loopEnd - 1); w.Write(0); w.Write(0);
                w.Write(new[] { 'e', 'x', 'm', 's' }); w.Write(extra.Length); w.Write(extra);
                if (pad > 0) w.Write((byte)0);
            }
        }

        public static void WriteWav(string path, short[] pcm, int channels, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = pcm.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2);
                w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (var s in pcm) w.Write(s);
            }
        }
    }

    // Windows' Media Foundation source reader, asked for 16-bit PCM at the file's own rate and channels.
    static class MediaFoundation
    {
        public static void Decode(string path, out float[] samples, out int rate, out int channels)
        {
            Check(MFStartup(0x20070, 0), "start Media Foundation");
            IMFSourceReader reader = null;
            try
            {
                int hr = MFCreateSourceReaderFromURL(Path.GetFullPath(path), IntPtr.Zero, out reader);
                if (hr < 0) throw new InvalidDataException("Windows can't read this file (" + Path.GetExtension(path) + ", error 0x" + hr.ToString("X8") + ")");
                Check(reader.SetStreamSelection(AllStreams, false), "select streams");
                Check(reader.SetStreamSelection(FirstAudio, true), "select the audio");
                IMFMediaType want;
                Check(MFCreateMediaType(out want), "make a media type");
                Check(want.SetGUID(MT_MAJOR_TYPE, MediaTypeAudio), "set audio");
                Check(want.SetGUID(MT_SUBTYPE, AudioFormatPCM), "set PCM");
                Check(want.SetUINT32(MT_AUDIO_BITS_PER_SAMPLE, 16), "set 16-bit");
                Check(reader.SetCurrentMediaType(FirstAudio, IntPtr.Zero, want), "ask for PCM");
                Marshal.ReleaseComObject(want);
                IMFMediaType got;
                Check(reader.GetCurrentMediaType(FirstAudio, out got), "read the format");
                uint ch, sr;
                Check(got.GetUINT32(MT_AUDIO_NUM_CHANNELS, out ch), "channels");
                Check(got.GetUINT32(MT_AUDIO_SAMPLES_PER_SECOND, out sr), "sample rate");
                Marshal.ReleaseComObject(got);
                channels = (int)ch;
                rate = (int)sr;
                var data = new MemoryStream();
                while (true)
                {
                    uint stream, flags;
                    long time;
                    IMFSample sample;
                    Check(reader.ReadSample(FirstAudio, 0, out stream, out flags, out time, out sample), "decode");
                    if (sample != null)
                    {
                        IMFMediaBuffer buffer;
                        Check(sample.ConvertToContiguousBuffer(out buffer), "read a buffer");
                        IntPtr p;
                        uint max, len;
                        Check(buffer.Lock(out p, out max, out len), "lock a buffer");
                        var chunk = new byte[len];
                        Marshal.Copy(p, chunk, 0, (int)len);
                        buffer.Unlock();
                        data.Write(chunk, 0, chunk.Length);
                        Marshal.ReleaseComObject(buffer);
                        Marshal.ReleaseComObject(sample);
                    }
                    if ((flags & ReaderError) != 0) throw new InvalidDataException("Windows couldn't decode all of this file");
                    if ((flags & (EndOfStream | TypeChanged)) != 0) break;
                }
                var bytes = data.ToArray();
                samples = new float[bytes.Length / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            }
            finally
            {
                if (reader != null) Marshal.ReleaseComObject(reader);
                MFShutdown();
            }
        }

        static void Check(int hr, string what) { if (hr < 0) throw new InvalidDataException("Media Foundation couldn't " + what + " (0x" + hr.ToString("X8") + ")"); }

        const uint FirstAudio = 0xFFFFFFFD, AllStreams = 0xFFFFFFFE, ReaderError = 0x1, EndOfStream = 0x2, TypeChanged = 0x20;
        static readonly Guid MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static readonly Guid MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static readonly Guid MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        static readonly Guid MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        static readonly Guid MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        static readonly Guid MediaTypeAudio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        static readonly Guid AudioFormatPCM = new Guid("00000001-0000-0010-8000-00AA00389B71");

        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] static extern int MFShutdown();
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);

        // The interfaces below list every method in vtable order; only the ones called have real signatures.
        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaType
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare();
            [PreserveSig] int GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, out uint value);
            void GetUINT64(); void GetDouble(); void GetGUID(); void GetStringLength(); void GetString();
            void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
            void SetItem(); void DeleteItem(); void DeleteAllItems();
            [PreserveSig] int SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, uint value);
            void SetUINT64(); void SetDouble();
            [PreserveSig] int SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
            void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount();
            void GetItemByIndex(); void CopyAllItems();
            void GetMajorType(); void IsCompressedFormat(); void IsEqual(); void GetRepresentation(); void FreeRepresentation();
        }

        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSourceReader
        {
            void GetStreamSelection();
            [PreserveSig] int SetStreamSelection(uint stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
            void GetNativeMediaType();
            [PreserveSig] int GetCurrentMediaType(uint stream, out IMFMediaType type);
            [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, IMFMediaType type);
            void SetCurrentPosition();
            [PreserveSig] int ReadSample(uint stream, uint control, out uint actualStream, out uint flags, out long timestamp, out IMFSample sample);
            void Flush(); void GetServiceForStream(); void GetPresentationAttribute();
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSample
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
            void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
            void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
            void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID(); void SetString();
            void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex();
            void CopyAllItems();
            void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime(); void SetSampleTime(); void GetSampleDuration();
            void SetSampleDuration(); void GetBufferCount(); void GetBufferByIndex();
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            void AddBuffer(); void RemoveBufferByIndex(); void RemoveAllBuffers(); void GetTotalLength(); void CopyToBuffer();
        }

        [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
            [PreserveSig] int Unlock();
            void GetCurrentLength(); void SetCurrentLength(); void GetMaxLength();
        }
    }
}
