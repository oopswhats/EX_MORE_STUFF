// How smoothly EX More Stuff's window opens: from a built exe, the window shown and a 15 ms ticker watching the UI
// thread for 8 s; every gap over 40 ms (a stall the eye can catch) is listed with when it happened. Usage:
// HitchTest <EXMoreStuff.exe>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class HitchTest
{
    [STAThread]
    static void Main(string[] args)
    {
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var clock = Stopwatch.StartNew();
        Assembly program = Assembly.LoadFrom(args[0]);
        var form = (Form)Activator.CreateInstance(program.GetType("ExMoreStuff.MainForm"), true);
        Console.WriteLine("window made: " + clock.ElapsedMilliseconds + " ms");
        var stalls = new List<string>();
        long last = 0, shownAt = -1, seenAt = -1, fullAt = -1;
        var ticker = new Timer { Interval = 15 };
        ticker.Tick += (s, e) =>
        {
            long now = clock.ElapsedMilliseconds;
            if (last > 0 && now - last > 40) stalls.Add("  at " + (last - shownAt) + " ms after showing: " + (now - last) + " ms frozen");
            if (seenAt < 0 && form.Opacity > 0) seenAt = now - shownAt;
            if (fullAt < 0 && form.Opacity >= 1) fullAt = now - shownAt;
            last = now;
            if (now - shownAt > 8000) { ticker.Stop(); form.Close(); }
        };
        form.Shown += (s, e) => { shownAt = clock.ElapsedMilliseconds; Console.WriteLine("shown: " + shownAt + " ms"); last = shownAt; ticker.Start(); };
        // while the window is frozen, what it's doing: its thread's stack, every 80 ms of a freeze (first 8 s)
        var ui = System.Threading.Thread.CurrentThread;
        var samples = new List<string>();
        new System.Threading.Thread(() =>
        {
            while (clock.ElapsedMilliseconds < 9000)
            {
                System.Threading.Thread.Sleep(80);
                if (shownAt < 0 || clock.ElapsedMilliseconds - last < 80) continue;
#pragma warning disable 618
                try
                {
                    ui.Suspend();
                    var stack = new StackTrace(ui, false);
                    ui.Resume();
                    var frames = new List<string>();
                    foreach (var frame in stack.GetFrames() ?? new StackFrame[0])
                    {
                        var m = frame.GetMethod();
                        if (m != null && m.DeclaringType != null && m.DeclaringType.Namespace == "ExMoreStuff") frames.Add(m.DeclaringType.Name + "." + m.Name);
                    }
                    string top = stack.FrameCount > 0 && stack.GetFrame(0).GetMethod() != null ? stack.GetFrame(0).GetMethod().DeclaringType + "." + stack.GetFrame(0).GetMethod().Name : "?";
                    samples.Add("  " + (clock.ElapsedMilliseconds - shownAt) + " ms: " + string.Join(" < ", frames.Take(6)) + "   [top: " + top + "]");
                }
                catch (Exception ex) { try { ui.Resume(); } catch (Exception) { } samples.Add("  (no sample: " + ex.GetType().Name + ")"); }
#pragma warning restore 618
            }
        }) { IsBackground = true }.Start();
        // after things settle, each step of a reload timed on its own
        var timing = new Timer { Interval = 5000 };
        timing.Tick += (s, e) =>
        {
            timing.Stop();
            ticker.Stop();
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Func<string, object> field = name => form.GetType().GetField(name, any).GetValue(form);
            string game = (string)field("game");
            var steps = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("BuildRoster", () => form.GetType().GetMethod("BuildRoster", any).Invoke(form, null)),
                new KeyValuePair<string, Action>("BuildStages", () => form.GetType().GetMethod("BuildStages", any).Invoke(form, null)),
                new KeyValuePair<string, Action>("packages SetGame", () => { var p = field("packagesPage"); p.GetType().GetMethod("SetGame").Invoke(p, new object[] { game }); }),
                new KeyValuePair<string, Action>("storage SetGame", () => { var p = field("storagePage"); p.GetType().GetMethod("SetGame").Invoke(p, new object[] { game }); }),
                new KeyValuePair<string, Action>("browse SetGame", () => { var p = field("communityPage"); p.GetType().GetMethod("SetGame").Invoke(p, new object[] { game }); }),
                new KeyValuePair<string, Action>("music SetGame", () => { var p = field("musicPage"); p.GetType().GetMethod("SetGame").Invoke(p, new object[] { game }); }),
                new KeyValuePair<string, Action>("Reload (all)", () => form.GetType().GetMethod("Reload", any).Invoke(form, null)),
                new KeyValuePair<string, Action>("paint the window", () => { form.Refresh(); }),
            };
            foreach (var step in steps)
            {
                var watch = Stopwatch.StartNew();
                step.Value();
                Console.WriteLine("  " + step.Key + ": " + watch.ElapsedMilliseconds + " ms");
            }
            form.Close();
        };
        form.Shown += (s, e) => timing.Start();
        Application.Run(form);
        Console.WriteLine("starts to show " + seenAt + " ms after Shown, fully shown at " + fullAt + " ms");
        Console.WriteLine(stalls.Count + " stall(s) over 40 ms:");
        foreach (string stall in stalls) Console.WriteLine(stall);
        Console.WriteLine("what it was doing:");
        foreach (string sample in samples) Console.WriteLine(sample);
    }
}
