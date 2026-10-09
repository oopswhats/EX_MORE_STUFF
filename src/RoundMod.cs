using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // Tom's Round BGM mod (res\roundbgm, credited on the About page): a dinput8.dll next to SSFIV.exe that switches a
    // stage's music to BGM_<stage>2.csb in round 2 and BGM_<stage>3.csb from round 3. EX More Stuff puts it in when
    // it installs round 2 or 3 music (a song on the Music page, or a stage package's) and takes it out when none of
    // its round music is left. It never touches a dinput8.dll it didn't put there: another mod's stays, and so does
    // Tom's when the player put it in.
    static class RoundMod
    {
        const string Dll = "dinput8.dll", Log = "USF4_Round_BGM.log", Marker = "ex_more_stuff_dinput8.txt";
        // the copy built in (v0.3: round banks follow the bank the game loads, so EMBER's custom stages get theirs),
        // and the earlier ones EX More Stuff may have put in (upgraded, and taken out like the current one)
        const string Sha256 = "d6c4852074f3709bbbd494b7b6187a87af37f56103ec2e2998d27604170446c0";
        static readonly string[] Ours = { Sha256, "44ffb0fe89e26cd53fd3d9464c7698f1cc6ea698e8090c70fd6cc2eb37da24da" /* v0.2 */ };

        public enum State { Missing, Tom, Other }

        static string DllPath(string game) { return Path.Combine(game, Dll); }
        static string MarkerPath(string game) { return Path.Combine(Game.PatchFolder(game), Marker); }

        /// <summary>What dinput8.dll is next to SSFIV.exe: none, Tom's mod (any version), or another mod's.</summary>
        public static State Check(string game)
        {
            string path = DllPath(game);
            if (!File.Exists(path)) return State.Missing;
            try
            {
                if (Installer.Sha256(path) == Sha256) return State.Tom;
                return System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)).Contains("Round BGM") ? State.Tom : State.Other;
            }
            catch (IOException) { return State.Other; }
        }

        /// <summary>A round 2 or 3 music file: BGM_<stage>2.csb or BGM_<stage>3.csb (stage codes are 3 characters).</summary>
        public static bool IsRoundFile(string file)
        {
            return Regex.IsMatch(Path.GetFileName(file), @"^BGM_[A-Z0-9]{3}[23]\.csb$", RegexOptions.IgnoreCase);
        }

        /// <summary>Puts the mod in when EX More Stuff has round music installed and no dinput8.dll is there yet.
        /// Returns a note when another mod's dinput8.dll is in the way, or null.</summary>
        public static string Ensure(string game)
        {
            if (!UsedBy(game)) return null;
            switch (Check(game))
            {
                case State.Tom:
                    // an older copy EX More Stuff put in is brought up to the built-in one; the player's own stays
                    if (!File.Exists(MarkerPath(game)) || Installer.Sha256(DllPath(game)) == Sha256 || !Ours.Contains(Installer.Sha256(DllPath(game)))) return null;
                    break;
                case State.Other:
                    return "another mod's dinput8.dll is next to SSFIV.exe, so Tom's Round BGM mod wasn't added: rounds 2 and 3 play round 1's music";
            }
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("roundbgm.dll"))
            using (var data = new MemoryStream())
            {
                if (s == null) throw new InvalidOperationException("this copy of EX More Stuff was built without Tom's Round BGM mod");
                s.CopyTo(data);
                File.WriteAllBytes(DllPath(game), data.ToArray());
            }
            Directory.CreateDirectory(Game.PatchFolder(game));
            File.WriteAllText(MarkerPath(game), "dinput8.dll next to SSFIV.exe: Tom's Round BGM mod, put in by EX More Stuff for round 2 and 3 music\r\n");
            return null;
        }

        /// <summary>Takes the mod out again (and its log) when EX More Stuff put it in and none of its round music is
        /// left, as long as the file is still the one it put there.</summary>
        public static void RemoveIfUnused(string game)
        {
            if (!File.Exists(MarkerPath(game)) || UsedBy(game)) return;
            string path = DllPath(game);
            if (File.Exists(path) && !Ours.Contains(Installer.Sha256(path))) { File.Delete(MarkerPath(game)); return; }   // replaced since: not ours any more
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(Path.Combine(game, Log))) File.Delete(Path.Combine(game, Log));
            File.Delete(MarkerPath(game));
        }

        // round music EX More Stuff installed: songs from the Music page, or files of an installed package
        static bool UsedBy(string game)
        {
            bool songs;
            try { songs = MusicBank.Load(game).Keys.Any(IsRoundFile); }
            catch (InvalidDataException) { songs = true; }   // an unreadable song list: keep the mod rather than guess
            return songs || Installer.Load(game).Any(r => r.Files.Any(IsRoundFile));
        }
    }
}
