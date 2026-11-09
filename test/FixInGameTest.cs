// EX More Stuff kept inside the game's folder (Bogart Willey 2026-11-08: ...\Arcade Edition\_Mods\EX More Stuff): a fixable
// costume coming in is fixed in its own package there, not locked because "the fixed copy can't go ... inside the game's
// folder". Usage: FixInGameTest <game folder> <EX More Stuff folder inside it> <a fixable costume's package zip in it>
// (build_test.bat FixInGameTest, with all of src\)
using System;
using System.IO;

namespace ExMoreStuff
{
    static class FixInGameTest
    {
        static void Main(string[] args)
        {
            AppFolders.Root = args[1];
            AppFolders.NoMigrate = true;
            string said, warning;
            string use = InstallCheck.Decide(args[0], args[2], true, out said, out warning);
            Console.WriteLine("warning: " + (warning ?? "none"));
            Console.WriteLine("said: " + (said ?? "none"));
            bool ok = warning == null && said != null && use == args[2] && !File.Exists(Path.ChangeExtension(args[2], null) + " - fixed.zip");
            Console.WriteLine(ok ? "ALL PASSED" : "FAILED");
            Environment.Exit(ok ? 0 : 1);
        }
    }
}
