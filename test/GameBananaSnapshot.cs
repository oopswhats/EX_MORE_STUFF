// Refreshes the GameBanana list built into EX More Stuff (res\gamebanana.json): read from GameBanana now, written as
// EX More Stuff keeps it. Run before a release. Usage: GameBananaSnapshot <res\gamebanana.json>
using System;
using System.Linq;
using System.Net;

namespace ExMoreStuff
{
    static class GameBananaSnapshot
    {
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var mods = GameBanana.All().Result;
            if (GameBanana.Stale) { Console.WriteLine("GameBanana couldn't be reached: nothing written"); Environment.Exit(1); }
            GameBanana.Save(mods, args[0]);
            Console.WriteLine(mods.Count + " mods, " + mods.Sum(m => m.Files.Count) + " files -> " + args[0] + " (" + new System.IO.FileInfo(args[0]).Length + " bytes)");
        }
    }
}
