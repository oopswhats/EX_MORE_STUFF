// Ko-fi / Patreon as profiles keep them (GameBanana.SupportLink; user 2026-11-18: Tom90's Contact Info has just "tom90"),
// then the support links read from real profiles. Usage: SupportLinks <member number> ... (build_test.bat SupportLinks)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ExMoreStuff
{
    static class SupportLinks
    {
        static void Main(string[] args)
        {
            foreach (string v in new[] { "tom90", " tom90 ", "https://Ko-fi.com/tom90", "http://www.ko-fi.com/tom90/", "ko-fi.com/tom90", "@tom90",
                                         "https://ko-fi.com/tom90/shop", "https://evil.com/tom90", "https://ko-fi.com.evil.com/tom90", "tom 90", "", null })
                Console.WriteLine("ko-fi   [" + v + "] -> " + (GameBanana.SupportLink(v, "ko-fi.com") ?? "(none)"));
            foreach (string v in new[] { "someone", "https://www.patreon.com/someone", "https://patreon.com/c/someone", "https://ko-fi.com/someone" })
                Console.WriteLine("patreon [" + v + "] -> " + (GameBanana.SupportLink(v, "patreon.com") ?? "(none)"));
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;   // as Program does
            var mods = args.Select(a => new GameBanana.Mod { AuthorId = long.Parse(a) }).ToList();
            var read = (Task<Dictionary<long, GameBanana.Support>>)typeof(GameBanana).GetMethod("ReadSupport", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { mods });
            foreach (string a in args)
            {
                GameBanana.Support s;
                Console.WriteLine("member " + a + ": " + (read.Result.TryGetValue(long.Parse(a), out s) ? "Ko-fi " + (s.Kofi ?? "-") + ", Patreon " + (s.Patreon ?? "-") : "none"));
            }
        }
    }
}
