// The lists' own scroll bar (ListSlider over ScrollList): it shows when the list scrolls, dragging the thumb, clicking
// the track and the wheel scroll the list, and scrolling the list moves the thumb. Usage: SliderTest
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class SliderTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Call(Control c, string method, EventArgs e)
        {
            c.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, new object[] { e });
        }

        [STAThread]
        static void Main()
        {
            var form = new Form { ClientSize = new Size(400, 300), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
            var list = new ScrollList { Dock = DockStyle.Fill };
            form.Controls.Add(list);
            for (int i = 0; i < 60; i++) list.Controls.Add(new Panel { Size = new Size(300, 40), BackColor = Color.Gray });
            form.Show();
            Application.DoEvents();
            var slider = form.Controls.OfType<ListSlider>().SingleOrDefault();
            Check(slider != null && slider.Visible && slider.Right == list.Right && slider.Height == list.Height, "our bar lies over the list's right edge");
            Check(list.VerticalScroll.Value == 0, "the list starts at the top");

            Call(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 8, 20, 0));
            Call(slider, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 8, 20 + 100, 0));
            Call(slider, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 8, 120, 0));
            int dragged = list.VerticalScroll.Value;
            Check(dragged > 0, "dragging the thumb 100 pixels down scrolls the list (to " + dragged + ")");

            Call(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 8, slider.Height - 5, 0));
            Call(slider, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 8, slider.Height - 5, 0));
            for (var glide = System.Diagnostics.Stopwatch.StartNew(); glide.ElapsedMilliseconds < 700; ) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }   // a page glides there too (2026-11-19)
            Check(list.VerticalScroll.Value > dragged, "clicking the track below it pages down (" + list.VerticalScroll.Value + ")");

            int before = list.VerticalScroll.Value;
            Call(slider, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 8, 50, 120));
            for (var glide = System.Diagnostics.Stopwatch.StartNew(); glide.ElapsedMilliseconds < 500; ) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }   // the wheel glides there
            Check(list.VerticalScroll.Value < before, "the wheel over it scrolls up (" + list.VerticalScroll.Value + ")");

            list.ScrollTo(int.MaxValue);
            Application.DoEvents();
            Check(list.VerticalScroll.Value == list.VerticalScroll.Maximum - list.VerticalScroll.LargeChange + 1, "scrolled to the end, as far as it goes");

            list.Controls.Clear();
            list.Controls.Add(new Panel { Size = new Size(300, 40) });
            Application.DoEvents();
            Check(!slider.Visible, "gone when the list doesn't scroll");
            form.Close();
            Console.WriteLine(failures == 0 ? "All passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
