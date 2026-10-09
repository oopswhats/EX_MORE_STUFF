// The Music page's waveform: the song's two channels (min/max peaks per pixel from a precomputed pyramid, so drawing
// stays cheap at any zoom), the loop start (green) and loop end (orange) as draggable markers, the play cursor,
// a time ruler. Mouse wheel zooms around the pointer (to single samples), a middle or right drag scrolls, a click
// places the cursor, dragging a marker moves it (snapped to the nearest zero crossing when Snap is on).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ExMoreStuff
{
    class WaveformView : Clear
    {
        short[] pcm;                    // interleaved stereo
        int frames;
        List<short[]> levels;           // peak pyramid: level k = (min, max) per 2^(k+6) frames per channel, interleaved
        double view0, perPixel = 1;     // first frame shown, frames per pixel
        int loopStart, loopEnd;
        long cursor = 0, playhead = -1;
        enum Drag { None, Start, End, Pan, Cursor }
        Drag drag;
        int dragX;
        double dragView0;
        public bool Snap = true;

        public event Action Changed;     // loop markers moved
        public event Action CursorMoved;

        static readonly Color Wave = Color.FromArgb(130, 170, 230), WaveIntro = Color.FromArgb(110, 120, 140),
                              StartColor = Color.FromArgb(70, 210, 120), EndColor = Color.FromArgb(242, 148, 22);

        public WaveformView()
        {
            Cursor = Cursors.Cross;
            TabStop = true;
        }

        public int LoopStart { get { return loopStart; } set { loopStart = Clamp(value, 0, Math.Max(0, loopEnd - 1)); Invalidate(); } }
        public int LoopEnd { get { return loopEnd; } set { loopEnd = Clamp(value, loopStart + 1, frames); Invalidate(); } }
        public long CursorFrame { get { return cursor; } set { cursor = Math.Max(0, Math.Min(value, frames)); Invalidate(); } }
        public long Playhead { get { return playhead; } set { if (playhead == value) return; playhead = value; Follow(); Invalidate(); } }
        public int Frames { get { return frames; } }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        public void SetSong(short[] song, int start, int end)
        {
            pcm = song;
            frames = song == null ? 0 : song.Length / 2;
            loopEnd = Clamp(end <= 0 ? frames : end, 1, Math.Max(1, frames));
            loopStart = Clamp(start, 0, loopEnd - 1);
            cursor = 0;
            playhead = -1;
            BuildLevels();
            ShowAll();
        }

        void BuildLevels()
        {
            levels = new List<short[]>();
            if (pcm == null) return;
            // level 0: (min, max) of each channel per 64 frames
            int block = 64, n = (frames + block - 1) / block;
            var l0 = new short[n * 4];
            for (int b = 0; b < n; b++)
                for (int c = 0; c < 2; c++)
                {
                    short lo = short.MaxValue, hi = short.MinValue;
                    int end = Math.Min(frames, (b + 1) * block);
                    for (int f = b * block; f < end; f++) { short v = pcm[f * 2 + c]; if (v < lo) lo = v; if (v > hi) hi = v; }
                    l0[b * 4 + c * 2] = lo; l0[b * 4 + c * 2 + 1] = hi;
                }
            levels.Add(l0);
            while (levels[levels.Count - 1].Length > 4 * 4)
            {
                var prev = levels[levels.Count - 1];
                int pn = prev.Length / 4, m = (pn + 1) / 2;
                var next = new short[m * 4];
                for (int b = 0; b < m; b++)
                    for (int k = 0; k < 4; k++)
                    {
                        short a = prev[(2 * b) * 4 + k], z = 2 * b + 1 < pn ? prev[(2 * b + 1) * 4 + k] : a;
                        next[b * 4 + k] = (k & 1) == 0 ? Math.Min(a, z) : Math.Max(a, z);
                    }
                levels.Add(next);
            }
        }

        public void ShowAll()
        {
            view0 = 0;
            perPixel = Math.Max(1.0 / 16, frames / (double)Math.Max(1, Width));
            Invalidate();
        }

        /// <summary>Zoom so `seconds` around `frame` fill the view.</summary>
        public void ShowAround(long frame, double seconds)
        {
            perPixel = Math.Max(1.0 / 16, seconds * Song.Rate / Math.Max(1, Width));
            view0 = frame - Width / 2.0 * perPixel;
            ClampView();
            Invalidate();
        }

        void ClampView()
        {
            double maxPer = Math.Max(1.0 / 16, frames / (double)Math.Max(1, Width));
            perPixel = Math.Max(1.0 / 16, Math.Min(perPixel, maxPer));
            view0 = Math.Max(0, Math.Min(view0, frames - Width * perPixel));
        }

        // keep the playhead in view while playing (page by page)
        void Follow()
        {
            if (playhead < 0) return;
            double x = (playhead - view0) / perPixel;
            if (x < 0 || x > Width - 20) { view0 = playhead - 20 * perPixel; ClampView(); }
        }

        int X(double frame) { return (int)Math.Round((frame - view0) / perPixel); }
        long FrameAt(int x) { return (long)Math.Round(view0 + x * perPixel); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ClampView(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (frames == 0) return;
            double at = view0 + e.X * perPixel;
            perPixel *= e.Delta > 0 ? 0.8 : 1.25;
            view0 = at - e.X * perPixel;
            ClampView();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (frames == 0) return;
            dragX = e.X;
            dragView0 = view0;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right) { drag = Drag.Pan; Cursor = Cursors.SizeWE; return; }
            if (Math.Abs(e.X - X(loopStart)) <= 6) drag = Drag.Start;
            else if (Math.Abs(e.X - X(loopEnd)) <= 6) drag = Drag.End;
            else { drag = Drag.Cursor; CursorFrame = FrameAt(e.X); if (CursorMoved != null) CursorMoved(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (frames == 0) return;
            switch (drag)
            {
                case Drag.Pan:
                    view0 = dragView0 - (e.X - dragX) * perPixel;
                    ClampView();
                    Invalidate();
                    return;
                case Drag.Start:
                    LoopStart = DragFrame(e.X);
                    if (Changed != null) Changed();
                    return;
                case Drag.End:
                    LoopEnd = DragFrame(e.X);
                    if (Changed != null) Changed();
                    return;
                case Drag.Cursor:
                    CursorFrame = FrameAt(e.X);
                    if (CursorMoved != null) CursorMoved();
                    return;
            }
            Cursor = Math.Abs(e.X - X(loopStart)) <= 6 || Math.Abs(e.X - X(loopEnd)) <= 6 ? Cursors.SizeWE : Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e) { drag = Drag.None; Cursor = Cursors.Cross; }

        /// <summary>A dragged marker's frame: the nearest point where the sound crosses zero (or is silent), looked for
        /// only a few pixels either side so it lands where it's put at any zoom; none with Snap off or Shift held.</summary>
        int DragFrame(int x)
        {
            int f = Clamp((int)FrameAt(x), 0, frames);
            if (!Snap || pcm == null || (ModifierKeys & Keys.Shift) != 0) return f;
            return SnapFrame(f, (int)Math.Min(Song.Rate / 100, 6 * perPixel));
        }

        /// <summary>The nearest frame (within `reach`) where both channels cross zero or are silent, or the frame itself.</summary>
        public int SnapFrame(int f, int reach)
        {
            f = Clamp(f, 0, frames);
            if (pcm == null || reach < 1) return f;
            int best = f, bestScore = int.MaxValue;
            for (int d = -reach; d <= reach; d++)
            {
                int g = f + d;
                if (g <= 0 || g >= frames) continue;
                int level = Math.Abs(pcm[g * 2]) + Math.Abs(pcm[g * 2 + 1]);
                bool crossing = (pcm[(g - 1) * 2] < 0) != (pcm[g * 2] < 0);
                int score = level + (crossing || level == 0 ? 0 : 4000) + Math.Abs(d) / 4;
                if (score < bestScore) { bestScore = score; best = g; }
            }
            return best;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, ClientRectangle);
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            if (frames == 0)
            {
                Theme.Draw(g, "Open a song or a game theme", Theme.Font(10f), Theme.Muted, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            const int ruler = 20;
            int lane = (Height - ruler) / 2;
            // the loop region tinted
            int xs = X(loopStart), xe = X(loopEnd);
            using (var tint = new SolidBrush(Color.FromArgb(22, 242, 148, 22))) g.FillRectangle(tint, Math.Max(0, xs), ruler, Math.Min(Width, xe) - Math.Max(0, xs), Height - ruler);
            for (int c = 0; c < 2; c++)
            {
                int mid = ruler + lane * c + lane / 2;
                using (var axis = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(axis, 0, mid, Width, mid);
                DrawChannel(g, c, mid, lane / 2 - 3);
            }
            DrawRuler(g, ruler);
            DrawMarker(g, loopStart, StartColor, "loop start");
            DrawMarker(g, loopEnd, EndColor, "loop end");
            int xc = X(cursor);
            using (var pen = new Pen(Color.FromArgb(160, 255, 255, 255)) { DashStyle = DashStyle.Dot }) g.DrawLine(pen, xc, ruler, xc, Height);
            if (playhead >= 0) using (var pen = new Pen(Color.White, 2f)) g.DrawLine(pen, X(playhead), ruler, X(playhead), Height);
        }

        void DrawChannel(Graphics g, int c, int mid, int half)
        {
            using (var loopPen = new Pen(Wave))
            using (var introPen = new Pen(WaveIntro))
            {
                if (perPixel <= 2)
                {
                    // close up: the samples themselves, joined
                    long f0 = Math.Max(0, (long)view0 - 1), f1 = Math.Min(frames - 1, (long)(view0 + Width * perPixel) + 1);
                    PointF prev = PointF.Empty;
                    for (long f = f0; f <= f1; f++)
                    {
                        var p = new PointF((float)((f - view0) / perPixel), mid - pcm[f * 2 + c] * half / 32768f);
                        if (f > f0) g.DrawLine(f < loopStart || f >= loopEnd ? introPen : loopPen, prev, p);
                        if (perPixel < 0.25) g.FillRectangle(Brushes.White, p.X - 1, p.Y - 1, 2, 2);
                        prev = p;
                    }
                    return;
                }
                // far: min/max per pixel from the finest pyramid level that has at most a block per pixel
                int level = 0;
                while (level + 1 < levels.Count && (64 << (level + 1)) <= perPixel) level++;
                var L = levels[level];
                int block = 64 << level;
                for (int x = 0; x < Width; x++)
                {
                    double fa = view0 + x * perPixel, fb = fa + perPixel;
                    if (fa >= frames) break;
                    short lo = short.MaxValue, hi = short.MinValue;
                    if (perPixel < 64)
                    {
                        for (long f = (long)fa; f < (long)Math.Min(fb, frames); f++) { short v = pcm[f * 2 + c]; if (v < lo) lo = v; if (v > hi) hi = v; }
                    }
                    else
                    {
                        int b0 = (int)(fa / block), b1 = Math.Min(L.Length / 4 - 1, (int)Math.Ceiling(fb / block) - 1);
                        for (int b = b0; b <= Math.Max(b0, b1); b++) { lo = Math.Min(lo, L[b * 4 + c * 2]); hi = Math.Max(hi, L[b * 4 + c * 2 + 1]); }
                    }
                    if (lo > hi) continue;
                    g.DrawLine(fa < loopStart || fa >= loopEnd ? introPen : loopPen, x, mid - hi * half / 32768f, x, mid - lo * half / 32768f + 1);
                }
            }
        }

        void DrawRuler(Graphics g, int height)
        {
            using (var band = new SolidBrush(Color.FromArgb(28, 30, 36))) g.FillRectangle(band, 1, 1, Width - 2, height - 1);
            double secondsPerPixel = perPixel / Song.Rate;
            double[] steps = { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60 };
            double step = 60;
            foreach (var s in steps) if (s / secondsPerPixel >= 80) { step = s; break; }
            double t0 = view0 / Song.Rate, t1 = (view0 + Width * perPixel) / Song.Rate;
            Font font = Theme.Font(7.5f);
            using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
                for (double t = Math.Ceiling(t0 / step) * step; t <= t1; t += step)
                {
                    int x = X(t * Song.Rate);
                    g.DrawLine(pen, x, height - 6, x, height);
                    Theme.Draw(g, Time(t, step < 1), font, Theme.Muted, new Rectangle(x + 3, 2, 90, height - 4), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
        }

        void DrawMarker(Graphics g, int frame, Color colour, string label)
        {
            int x = X(frame);
            if (x < -40 || x > Width + 40) return;
            using (var pen = new Pen(colour, 2f)) g.DrawLine(pen, x, 0, x, Height);
            using (var brush = new SolidBrush(colour)) g.FillPolygon(brush, new[] { new Point(x - 6, 0), new Point(x + 6, 0), new Point(x, 9) });
            Theme.Draw(g, label, Theme.Bold(7.5f), colour, new Rectangle(x + 5, Height - 18, 80, 16), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        public static string Time(double seconds, bool fine = true)
        {
            int m = (int)(seconds / 60);
            double s = seconds - m * 60;
            return fine ? string.Format("{0}:{1:00.000}", m, s) : string.Format("{0}:{1:00}", m, Math.Floor(s));
        }
    }
}

namespace ExMoreStuff
{
    // The loop's seam up close: the last 25 ms before the loop end, then the first 25 ms after the loop start (what
    // the game plays at the jump), with what the song itself has after the loop end drawn faintly behind: when the
    // two lines on the right lie on each other, the jump can't be heard.
    class SeamView : Clear
    {
        short[] pcm;
        int start, end;
        const int Span = Song.Rate / 40;

        public void Show(short[] song, int loopStart, int loopEnd) { pcm = song; start = loopStart; end = loopEnd; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, ClientRectangle);
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            Theme.Draw(g, "The seam, up close", Theme.Bold(7.5f), Theme.Muted, new Rectangle(6, 2, Width - 12, 16), TextFormatFlags.Left);
            if (pcm == null || end <= start) return;
            Smooth(g);
            int frames = pcm.Length / 2, mid = Width / 2, top = 18;
            float half = (Height - top - 4) / 4f;
            for (int c = 0; c < 2; c++)
            {
                float axis = top + half + c * 2 * half;
                using (var faint = new Pen(Color.FromArgb(90, 150, 156, 170)))
                    Line(g, faint, axis, half, i => end + i < frames ? pcm[(end + i) * 2 + c] : (short)0, 0, Span, mid);
                using (var before = new Pen(Color.FromArgb(242, 148, 22)))
                    Line(g, before, axis, half, i => end - Span + i >= 0 ? pcm[(end - Span + i) * 2 + c] : (short)0, 0, Span, 0);
                using (var after = new Pen(Color.FromArgb(70, 210, 120)))
                    Line(g, after, axis, half, i => start + i < frames ? pcm[(start + i) * 2 + c] : (short)0, 0, Span, mid);
            }
            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255)) { DashStyle = DashStyle.Dot }) g.DrawLine(pen, mid, top, mid, Height);
        }

        // `count` samples drawn across half the view from x0
        void Line(Graphics g, Pen pen, float axis, float half, Func<int, short> sample, int from, int count, int x0)
        {
            var points = new PointF[count];
            float w = (Width / 2f) / count;
            for (int i = 0; i < count; i++) points[i] = new PointF(x0 + i * w, axis - sample(from + i) * half / 32768f);
            g.DrawLines(pen, points);
        }
    }
}
