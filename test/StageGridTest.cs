// Renders the StageGrid (the program's own control) to a PNG to check its look. Usage: StageGridTest <game> <out.png>
using System.Drawing;
using System.Drawing.Imaging;

namespace ExMoreStuff
{
    static class StageGridTest
    {
        static void Main(string[] args)
        {
            using (var grid = new StageGrid(930))
            using (var bitmap = new Bitmap(grid.Width, grid.Height))
            {
                grid.DrawToBitmap(bitmap, new Rectangle(0, 0, grid.Width, grid.Height));
                bitmap.Save(args[1], ImageFormat.Png);
            }
        }
    }
}
