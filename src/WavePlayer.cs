// Plays a song from memory through Windows' waveOut (nothing to install), from any point, optionally looping as the
// game will: on reaching the loop end it carries on at the loop start, sample for sample. Position is the frame now
// heard, for the waveform's playhead.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ExMoreStuff
{
    sealed class WavePlayer : IDisposable
    {
        const int Buffers = 4, BufferFrames = 4410;          // 4 x 100 ms
        IntPtr device = IntPtr.Zero;
        Thread thread;
        volatile bool stop;
        volatile int generation;   // which playback is current: an older one still finishing stops by itself
        short[] pcm;
        int loopStart, loopEnd;
        bool looping;
        long startFrame;                                     // frame the playback began at (for Position)
        readonly object sync = new object();

        public bool Playing { get { return thread != null && thread.IsAlive; } }

        /// <summary>The song frame now being heard, or -1.</summary>
        public long Position
        {
            get
            {
                if (!Playing || device == IntPtr.Zero) return -1;
                var t = new MMTIME { wType = 2 };             // TIME_SAMPLES
                if (waveOutGetPosition(device, ref t, Marshal.SizeOf(typeof(MMTIME))) != 0) return -1;
                long played = t.u;
                lock (sync) return Map(played);
            }
        }

        // frames played so far -> the song frame, following the loop jumps
        long Map(long played)
        {
            long f = startFrame + played;
            if (!looping || f < loopEnd) return f;
            long len = Math.Max(1, loopEnd - loopStart);
            return loopStart + (f - loopEnd) % len;
        }

        /// <summary>Plays `song` from `from`; with loop, [loopStart, loopEnd) repeats as in the game.</summary>
        public void Play(short[] song, long from, bool loop, int loopStartFrame, int loopEndFrame)
        {
            Stop();
            pcm = song;
            looping = loop;
            loopStart = loopStartFrame;
            loopEnd = Math.Min(loopEndFrame, song.Length / 2);
            startFrame = Math.Max(0, Math.Min(from, song.Length / 2 - 1));
            var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 2, nSamplesPerSec = Song.Rate, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = Song.Rate * 4 };
            int r = waveOutOpen(out device, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) { device = IntPtr.Zero; throw new InvalidOperationException("Windows couldn't open the sound output (" + r + ")"); }
            stop = false;
            IntPtr dev = device;   // each playback closes only its own device, even if a slow one ends after the next starts
            int gen = ++generation;
            thread = new Thread(() => Run(dev, gen)) { IsBackground = true, Name = "WavePlayer" };
            thread.Start();
        }

        void Run(IntPtr device, int gen)
        {
            var headers = new IntPtr[Buffers];
            var data = new IntPtr[Buffers];
            int hsize = Marshal.SizeOf(typeof(WAVEHDR));
            long next = startFrame;
            bool ended = false;
            try
            {
                for (int i = 0; i < Buffers; i++)
                {
                    headers[i] = Marshal.AllocHGlobal(hsize);
                    data[i] = Marshal.AllocHGlobal(BufferFrames * 4);
                    var h = new WAVEHDR { lpData = data[i] };
                    Marshal.StructureToPtr(h, headers[i], false);
                }
                int queued = 0, k = 0;
                while (!stop && gen == generation)
                {
                    // fill and queue every free buffer
                    var h = (WAVEHDR)Marshal.PtrToStructure(headers[k], typeof(WAVEHDR));
                    bool free = queued < Buffers || (h.dwFlags & 1) != 0;   // WHDR_DONE
                    if (!free) { Thread.Sleep(5); continue; }
                    if (queued >= Buffers) waveOutUnprepareHeader(device, headers[k], hsize);
                    if (ended)
                    {
                        // drain: wait for every queued buffer to finish
                        bool all = true;
                        for (int i = 0; i < Buffers; i++)
                        {
                            var hi = (WAVEHDR)Marshal.PtrToStructure(headers[i], typeof(WAVEHDR));
                            if ((hi.dwFlags & 1) == 0 && (hi.dwFlags & 2) != 0) all = false;    // prepared, not done
                        }
                        if (all) break;
                        Thread.Sleep(10);
                        continue;
                    }
                    int n = Fill(data[k], ref next, out ended);
                    if (n == 0) { ended = true; continue; }
                    h = new WAVEHDR { lpData = data[k], dwBufferLength = (uint)(n * 4) };
                    Marshal.StructureToPtr(h, headers[k], false);
                    waveOutPrepareHeader(device, headers[k], hsize);
                    waveOutWrite(device, headers[k], hsize);
                    queued++;
                    k = (k + 1) % Buffers;
                }
            }
            finally
            {
                if (device != IntPtr.Zero)
                {
                    waveOutReset(device);
                    for (int i = 0; i < Buffers; i++) if (headers[i] != IntPtr.Zero) waveOutUnprepareHeader(device, headers[i], hsize);
                    waveOutClose(device);
                    if (this.device == device) this.device = IntPtr.Zero;
                }
                for (int i = 0; i < Buffers; i++)
                {
                    if (headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(headers[i]);
                    if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]);
                }
            }
        }

        // the next frames, jumping from the loop end to the loop start when looping
        int Fill(IntPtr dest, ref long next, out bool ended)
        {
            ended = false;
            var chunk = new short[BufferFrames * 2];
            int n = 0, total = pcm.Length / 2;
            while (n < BufferFrames)
            {
                long end = looping ? loopEnd : total;
                if (next >= end)
                {
                    if (!looping || loopEnd - loopStart < 1) { ended = n == 0; break; }
                    next = loopStart;
                }
                int take = (int)Math.Min(BufferFrames - n, end - next);
                Array.Copy(pcm, next * 2, chunk, n * 2, take * 2);
                n += take;
                next += take;
            }
            float volume = Volume;
            if (volume != 1f)
                for (int i = 0; i < n * 2; i++) chunk[i] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(chunk[i] * volume)));
            Marshal.Copy(chunk, 0, dest, n * 2);
            return n;
        }

        /// <summary>How loud it plays (1 = as the samples are), taking effect within a buffer.</summary>
        public volatile float Volume = 1f;

        public void Stop()
        {
            stop = true;
            if (thread != null) { thread.Join(1000); thread = null; }
        }

        public void Dispose() { Stop(); }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [StructLayout(LayoutKind.Explicit)]
        struct MMTIME { [FieldOffset(0)] public uint wType; [FieldOffset(4)] public uint u; [FieldOffset(8)] public uint pad; }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr device, int id, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr device);
        [DllImport("winmm.dll")] static extern int waveOutGetPosition(IntPtr device, ref MMTIME time, int size);
    }
}
