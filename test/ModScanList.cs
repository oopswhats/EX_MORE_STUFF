// Lists what Advanced > Old mods finds in a game folder (reads only). Usage: ModScanList <game folder>   (build_test.bat ModScanList)
using System;
using System.Linq;

namespace ExMoreStuff
{
    static class ModScanList
    {
        static void Main(string[] args)
        {
            foreach (var f in ModScan.Scan(args[0]))
                Console.WriteLine("{0,-8} {1,-62} {2,3} files  {3:yyyy-MM-dd}  e.g. {4}", f.Kind, f.What, f.Files.Count, f.Newest, f.Files.First());
        }
    }
}
