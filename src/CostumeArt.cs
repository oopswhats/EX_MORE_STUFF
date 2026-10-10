using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace ExMoreStuff
{
    // The game's costumes as pictures, in every color: SF4 Ember's selection art (photographs credited in its
    // assets\selection\README.md; game imagery Capcom), made small by test\EmberArt.cs into res\costumes, built into the
    // program: one JPEG sheet per costume (<CODE>_<NN>.jpg, a cell per color, color 1 first) and index.txt (which colors
    // each sheet has). A sheet is cut into its colors the first time one is wanted.
    static class CostumeArt
    {
        public const int CellWidth = 96, CellHeight = 144;
        static Dictionary<string, string> index;
        static readonly Dictionary<string, Bitmap[]> costumes = new Dictionary<string, Bitmap[]>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();

        static Dictionary<string, string> Index()
        {
            if (index != null) return index;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (Stream s = typeof(CostumeArt).Assembly.GetManifestResourceStream("costumes.index.txt"))
                if (s != null)
                    using (var r = new StreamReader(s))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            string[] parts = line.Split(' ');
                            if (parts.Length == 2) result[parts[0]] = parts[1];
                        }
                    }
            return index = result;
        }

        static string Name(string fighter, int costume) { return fighter + "_" + costume.ToString("D2"); }

        /// <summary>How many colors the costume's pictures go up to (0: none for it).</summary>
        public static int Colors(string fighter, int costume)
        {
            lock (gate) { string mask; return fighter != null && Index().TryGetValue(Name(fighter, costume), out mask) ? mask.Length : 0; }
        }

        /// <summary>One color of one of the game's costumes (color 1 = its first), or null when there's no picture of it.</summary>
        public static Image Picture(string fighter, int costume, int color)
        {
            if (fighter == null) return null;
            lock (gate)
            {
                string name = Name(fighter, costume), mask;
                if (!Index().TryGetValue(name, out mask) || color < 1 || color > mask.Length || mask[color - 1] != '1') return null;
                Bitmap[] cells;
                if (!costumes.TryGetValue(name, out cells))
                {
                    cells = new Bitmap[mask.Length];
                    using (Stream s = typeof(CostumeArt).Assembly.GetManifestResourceStream("costumes." + name + ".jpg"))
                        if (s != null)
                            using (var sheet = new Bitmap(s))
                                for (int c = 0; c < mask.Length && (c + 1) * CellWidth <= sheet.Width; c++)
                                {
                                    if (mask[c] != '1') continue;
                                    // drawn into a bitmap of its own (a clone could still need the sheet's stream later)
                                    cells[c] = new Bitmap(CellWidth, CellHeight);
                                    using (Graphics g = Graphics.FromImage(cells[c]))
                                        g.DrawImage(sheet, new Rectangle(0, 0, CellWidth, CellHeight), new Rectangle(c * CellWidth, 0, CellWidth, CellHeight), GraphicsUnit.Pixel);
                                }
                    costumes[name] = cells;
                }
                return cells[color - 1];
            }
        }
    }
}
