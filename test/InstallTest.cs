// Music install on a stand-in game folder: a modded file already in patch_ae2_tu3 is moved aside and comes back;
// layers: the main song shared by Ultra, low health as a bass cut (same length, in step); a song of its own on Ultra
// kept when the main song is put in again; round 2's file; a WAV saved with its loop and settings read back.
// Usage: InstallTest <game> <stand-in folder> <song file> <second song file>
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class InstallTest
    {
        static string game;

        static void Main(string[] args)
        {
            game = args[0];
            string fake = args[1];
            var slot = new MusicSlot { Code = "TRN" };
            string bgm = Path.Combine(fake, "resource", MusicBank.Folder);
            Directory.CreateDirectory(bgm);
            File.Copy(MusicBank.GameFile(game, slot.File), Path.Combine(bgm, slot.File), true);
            string patch = MusicBank.PatchFile(fake, slot.File);
            Directory.CreateDirectory(Path.GetDirectoryName(patch));
            File.WriteAllBytes(patch, new byte[] { 1, 2, 3 });               // someone else's mod
            var gameBank = File.ReadAllBytes(MusicBank.GameFile(fake, slot.TemplateFile));

            var song = AudioFile.Load(args[2]);
            var found = LoopFinder.Find(song.Pcm);
            int ls = found.Start, le = found.End;
            // main song: Ultra plays it too, low health its bass cut
            var bank = MusicBank.Build(gameBank, gameBank, 0, song.Pcm, ls, le);
            bank = MusicBank.Link(bank, gameBank, 1, 0, false);
            bank = MusicBank.Build(bank, gameBank, 2, MusicBank.LowHealthVersion(song.Pcm), ls, le);
            MusicBank.Install(fake, slot, bank, new InstalledSong { Song = song.Name, LoopStart = ls, LoopEnd = le, Layers = new[] { song.Name, song.Name, song.Name + " (bass cut)" } });
            Console.WriteLine("game bank {0:F1} MB, ours {1:F1} MB; backup kept: {2}", gameBank.Length / 1e6, bank.Length / 1e6,
                File.Exists(Path.Combine(Game.PatchFolder(fake), "ex_more_stuff_backup", MusicBank.Folder, slot.File)));
            Layers("main + same Ultra + bass-cut low health", File.ReadAllBytes(patch));

            // a song of its own on Ultra, built on ours
            var other = AudioFile.Load(args[3]);
            var f2 = LoopFinder.Find(other.Pcm);
            bank = MusicBank.Build(File.ReadAllBytes(patch), gameBank, 1, other.Pcm, f2.Start, f2.End);
            MusicBank.Install(fake, slot, bank, new InstalledSong { Song = song.Name, LoopStart = ls, LoopEnd = le, Layers = new[] { song.Name, other.Name, song.Name + " (bass cut)" } });
            Layers("Ultra given its own song", File.ReadAllBytes(patch));

            // the main song again, Ultra "its own", low health "the game's"
            bank = MusicBank.Build(File.ReadAllBytes(patch), gameBank, 0, song.Pcm, ls, le);
            bank = MusicBank.Link(bank, gameBank, 1, 1, false);
            bank = MusicBank.Link(bank, gameBank, 2, 2, true);
            Layers("main again, Ultra its own, low health the game's", bank);

            Console.WriteLine("round 2 file: {0}; parsed back: {1} round {2}", slot.InRound(2).File, MusicSlot.FromFile(slot.InRound(2).File).Code, MusicSlot.FromFile(slot.InRound(2).File).Round);
            string problem = MusicBank.Remove(fake, slot);
            Console.WriteLine("removed: problem {0}; the other mod is back: {1}; record cleared: {2}", problem ?? "none",
                File.Exists(patch) && File.ReadAllBytes(patch).SequenceEqual(new byte[] { 1, 2, 3 }), !MusicBank.Load(fake).ContainsKey(slot.File));

            string wav = Path.Combine(fake, "saved.wav");
            AudioFile.WriteLoopedWav(wav, song.Pcm, ls, le, "{\"volumeDb\":-3}");
            var back = AudioFile.Load(wav);
            Console.WriteLine("saved WAV: loop {0}..{1} (want {2}..{3}), same audio {4}, settings {5}", back.LoopStart, back.LoopEnd, ls, le, back.Pcm.SequenceEqual(song.Pcm), back.Settings);
        }

        // each layer: length, loop start, and whether it is the game's, the same as main, or something else
        static void Layers(string what, byte[] bank)
        {
            Console.WriteLine(what + ":");
            var gameBank = File.ReadAllBytes(MusicBank.GameFile(game, "BGM_TRN.csb"));
            Song main = null;
            for (int c = 0; c < 3; c++)
            {
                int ls, gls;
                var s = MusicBank.Decode(bank, c, "", out ls);
                var g = MusicBank.Decode(gameBank, c, "", out gls);
                if (c == 0) main = s;
                string same = s.Pcm.SequenceEqual(g.Pcm) ? "the game's" : c > 0 && s.Pcm.SequenceEqual(main.Pcm) ? "same as main" : "its own";
                double low = c == 0 ? 0 : LowEnergy(s.Pcm) / Math.Max(1, LowEnergy(main.Pcm));
                Console.WriteLine("  {0}: {1} frames, loop from {2}, {3}{4}", MusicBank.LayerNames[c], s.Frames, ls, same,
                    c > 0 && s.Frames == main.Frames && same == "its own" ? string.Format(", bass vs main {0:F2}", low) : "");
            }
        }

        // energy under ~150 Hz (a one-pole low-pass of the mid channel)
        static double LowEnergy(short[] pcm)
        {
            double lp = 0, e = 0, k = 1 - Math.Exp(-2 * Math.PI * 150 / 44100.0);
            for (int i = 0; i < pcm.Length; i += 2) { lp += k * ((pcm[i] + pcm[i + 1]) * 0.5 - lp); e += lp * lp; }
            return e;
        }
    }
}
