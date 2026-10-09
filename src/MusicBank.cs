// The game's music banks (docs\SF4_MUSIC.md in the modding project): battle\sound\bgm\BGM_<stage>.csb (3 cues: 0 the
// stage's theme, 1 and 2 short pieces), BGM_<fighter>_2CH.csb (1 cue). The game plays cues by number. A cue's track
// (SYNTH, syntype 1) links waveforms (syntype 0: the front pair and a copy 30 ms later on the rear) that name a
// SOUND_ELEMENT, whose data is an AAX table: row 0 the intro (played once), row 1 the loop (forever), each an ADX.
// A new song for a slot is built on the slot's own bank from the game's folders (its cue count, synths and volumes
// stay): only the main cue's sound element changes. It goes to patch_ae2_tu3\battle\sound\bgm, which the game reads
// first; a file already there is moved to ex_more_stuff_backup and comes back when the song is taken off (as stage
// replacements do).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    /// <summary>Where a song can go: a game stage's theme, a fighter's theme, or a custom stage's (by code); for a
    /// stage, also which round (rounds 2 and 3 are Tom's Round BGM mod's BGM_<stage>2.csb and BGM_<stage>3.csb).</summary>
    sealed class MusicSlot
    {
        public string Code;        // RVR, RYU, C12
        public bool Fighter;
        public int Round = 1;      // stages: 1, 2 or 3 (3 = round 3 and later)
        public string File { get { return Fighter ? "BGM_" + Code + "_2CH.csb" : "BGM_" + Code + (Round > 1 ? Round.ToString() : "") + ".csb"; } }
        public string Name
        {
            get
            {
                if (Fighter) return Fighters.Name(Code) + "'s theme";
                return Array.IndexOf(Stages.Codes, Code) >= 0 ? Stages.Name(Code) : "Custom stage " + Code;
            }
        }
        public MusicSlot InRound(int round) { return new MusicSlot { Code = Code, Fighter = Fighter, Round = Fighter ? 1 : round }; }

        /// <summary>The slot a bank file is for: BGM_RYU_2CH.csb a fighter's, BGM_RVR.csb a stage's, BGM_RVR2.csb
        /// a stage's round 2 (stage codes are 3 characters).</summary>
        public static MusicSlot FromFile(string file)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
            if (name.StartsWith("BGM_")) name = name.Substring(4);
            if (name.EndsWith("_2CH")) return new MusicSlot { Code = name.Substring(0, name.Length - 4), Fighter = true };
            int round = name.Length == 4 && (name[3] == '2' || name[3] == '3') ? name[3] - '0' : 1;
            return new MusicSlot { Code = round > 1 ? name.Substring(0, 3) : name, Round = round };
        }

        // the bank a new song is built on: the slot's own (round 1's for rounds 2 and 3); a custom stage's, its
        // stand-in stage's
        public string TemplateCode { get { return Fighter || Array.IndexOf(Stages.Codes, Code) >= 0 ? Code : Stages.FallbackCode(Code); } }
        public string TemplateFile { get { return Fighter ? File : "BGM_" + TemplateCode + ".csb"; } }
    }

    sealed class InstalledSong
    {
        public string File, Song, Sha256;
        public int LoopStart, LoopEnd;
        public string Backup;      // the patch folder's file moved aside, or null
        public string[] Layers = new string[3];   // stages: the song on each layer (main, Ultra, low health); null = the game's
    }

    static class MusicBank
    {
        const string ListName = "ex_more_stuff_music.json", BackupFolder = "ex_more_stuff_backup";
        public const string Folder = @"battle\sound\bgm";
        static readonly string[] GameRoots = { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" };

        /// <summary>The game's own bank (its updates and DLC folders, not patch_ae2_tu3 where songs are put).</summary>
        public static string GameFile(string game, string file)
        {
            foreach (var root in GameRoots)
            {
                string p = Path.Combine(game, root, Folder, file);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static string PatchFile(string game, string file) { return Path.Combine(Game.PatchFolder(game), Folder, file); }

        // -- reading a bank ------------------------------------------------------------------------------------------
        static int Row(UtfTable t, string column, string value)
        {
            for (int i = 0; i < t.Rows.Count; i++) if ((string)t.Get(i, column) == value) return i;
            return -1;
        }

        static UtfTable Sub(UtfTable csb, string name) { return UtfTable.Read((byte[])csb.Get(Row(csb, "name", name), "utf")); }

        /// <summary>The SOUND_ELEMENT row cue `cue` plays: cue -> its synth -> (a track's first link) -> waveform.</summary>
        public static int ElementOf(UtfTable cues, UtfTable synths, UtfTable elements, int cue)
        {
            string synth = (string)cues.Get(cue, "synth");
            for (int guard = 0; guard < 8; guard++)
            {
                int s = Row(synths, "synname", synth);
                if (s < 0) break;
                string link = ((string)synths.Get(s, "lnkname") ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (link == null) break;
                int e = Row(elements, "name", link);
                if (e >= 0) return e;
                synth = link;
            }
            throw new InvalidDataException("cue " + cue + " has no sound");
        }

        public static int CueCount(byte[] bank) { return Sub(UtfTable.Read(bank), "CUE").Rows.Count; }

        /// <summary>How loud the game plays a cue's sound, from the bank's synths: the track's volume (of 1000) times
        /// its loudest waveform's volume (of 1000) and front send (dry0/dry1, of 255). A stage's main theme: 1000 ×
        /// 400 × 229 ≈ 0.36 (-9 dB); its Ultra and low-health layers differ a little. 1 when it can't be read.</summary>
        public static double MixGain(byte[] bank, int cue)
        {
            try
            {
                var csb = UtfTable.Read(bank);
                var cues = Sub(csb, "CUE");
                var synths = Sub(csb, "SYNTH");
                if (cue >= cues.Rows.Count) return 1;
                Func<int, string, double> number = (row, column) => synths.Column(column) >= 0 && synths.Get(row, column) != null ? Convert.ToDouble(synths.Get(row, column)) : -1;
                int track = Row(synths, "synname", (string)cues.Get(cue, "synth"));
                if (track < 0) return 1;
                double trackVolume = number(track, "volume") >= 0 ? number(track, "volume") / 1000 : 1, loudest = 0;
                foreach (string link in ((string)synths.Get(track, "lnkname") ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int w = Row(synths, "synname", link);
                    if (w < 0) continue;
                    double volume = number(w, "volume") >= 0 ? number(w, "volume") / 1000 : 1;
                    double send = Math.Max(Math.Max(number(w, "dry0"), number(w, "dry1")), 0) / 255;
                    loudest = Math.Max(loudest, volume * send);
                }
                return loudest > 0 ? trackVolume * loudest : 1;
            }
            catch (Exception) { return 1; }
        }

        /// <summary>A cue's song: intro then loop, decoded; LoopStart = the intro's length, LoopEnd = the end.</summary>
        public static Song Decode(byte[] bank, int cue, string name, out int loopStart)
        {
            var csb = UtfTable.Read(bank);
            var elements = Sub(csb, "SOUND_ELEMENT");
            int e = ElementOf(Sub(csb, "CUE"), Sub(csb, "SYNTH"), elements, cue);
            var aax = UtfTable.Read((byte[])elements.Get(e, "data"));
            AdxInfo ii, li;
            var intro = Adx.Decode((byte[])aax.Get(0, "data"), out ii);
            var loop = aax.Rows.Count > 1 ? Adx.Decode((byte[])aax.Get(1, "data"), out li) : new short[0];
            var pcm = ToStereo(intro, ii.Channels).Concat(ToStereo(loop, ii.Channels)).ToArray();
            if (ii.SampleRate != Song.Rate)
                pcm = AudioFile.ToGameFormat(pcm.Select(s => s / 32768f).ToArray(), ii.SampleRate, 2);
            loopStart = (int)((long)ii.Samples * Song.Rate / ii.SampleRate);
            return new Song { Pcm = pcm, Name = name };
        }

        static short[] ToStereo(short[] x, int ch)
        {
            if (ch == 2) return x;
            int n = x.Length / ch;
            var o = new short[n * 2];
            for (int i = 0; i < n; i++) o[2 * i] = o[2 * i + 1] = x[i * ch];
            return o;
        }

        /// <summary>How loud music sounds, in LUFS (ITU-R BS.1770: K-weighted, so deep bass counts less and the
        /// presence range more, as the ear hears it; 400 ms blocks, gated at -70 LUFS and 10 LU under the average so
        /// silences don't count). -100 for silence.</summary>
        public static double Loudness(short[] pcm)
        {
            int frames = pcm.Length / 2;
            // the K filter: a +4 dB shelf above ~1.7 kHz, then a high-pass at 38 Hz (BS.1770's, made for 44.1 kHz)
            var shelf = KFilter(1681.974450955533, 3.999843853973347, 0.7071752369554196, false);
            var high = KFilter(38.13547087602444, 0, 0.5003270373238773, true);
            var power = new double[frames];
            for (int c = 0; c < 2; c++)
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0, z1 = 0, z2 = 0;
                for (int i = 0; i < frames; i++)
                {
                    double x = pcm[2 * i + c] / 32768.0;
                    double y = shelf[0] * x + shelf[1] * x1 + shelf[2] * x2 - shelf[3] * y1 - shelf[4] * y2;
                    x2 = x1; x1 = x;
                    double z = high[0] * y + high[1] * y1 + high[2] * y2 - high[3] * z1 - high[4] * z2;
                    y2 = y1; y1 = y; z2 = z1; z1 = z;
                    power[i] += z * z;
                }
            }
            int block = Song.Rate * 4 / 10, hop = Song.Rate / 10;
            var blocks = new List<double>();
            double sum = 0;
            for (int i = 0; i < Math.Min(block, frames); i++) sum += power[i];
            for (int s = 0; s + block <= frames; s += hop)
            {
                blocks.Add(sum / block);
                for (int i = s; i < s + hop && i + block < frames; i++) sum += power[i + block] - power[i];
            }
            Func<double, double> lufs = p => -0.691 + 10 * Math.Log10(Math.Max(p, 1e-15));
            var heard = blocks.Where(p => lufs(p) > -70).ToList();
            if (heard.Count == 0) return -100;
            double gate = lufs(heard.Average()) - 10;
            var kept = heard.Where(p => lufs(p) > gate).ToList();
            return lufs(kept.Average());
        }

        // BS.1770's two K-weighting stages, recomputed for 44.1 kHz: b0 b1 b2 a1 a2
        static double[] KFilter(double hz, double db, double q, bool highPass)
        {
            double k = Math.Tan(Math.PI * hz / Song.Rate), a0 = 1 + k / q + k * k;
            if (highPass) return new[] { 1 / a0, -2 / a0, 1 / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0 };
            double vh = Math.Pow(10, db / 20), vb = Math.Pow(vh, 0.4996667741545416);
            return new[] { (vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0 };
        }

        // -- building a bank -----------------------------------------------------------------------------------------
        /// <summary>A stage bank's layers: the game starts all three cues together and fades between them (the
        /// main theme; Ultra, when both players have half their Ultra gauge; low health, when the timer is at 15 or
        /// someone's health is low). A fighter's bank has the main one only.</summary>
        public static readonly string[] LayerNames = { "Main", "Ultra", "Low health" };

        // The game's own loops are 6 s or longer and its banks a few MB; a whole bank is loaded into the 32-bit game.
        public const int LongestSong = 10 * 60 * Song.Rate, ShortestLoop = Song.Rate;

        /// <summary>`bank` with layer `layer` playing `pcm` (44.1 kHz stereo): [0, loopStart) once, then [loopStart,
        /// loopEnd) forever. `game` is the game's own bank for the slot, which names each layer's own sound element.
        /// The other layers keep what `bank` has for them. Synths and volumes stay the bank's.</summary>
        public static byte[] Build(byte[] bank, byte[] game, int layer, short[] pcm, int loopStart, int loopEnd)
        {
            int frames = pcm.Length / 2;
            if (loopEnd <= 0 || loopEnd > frames) loopEnd = frames;
            if (frames > LongestSong) throw new ArgumentException("songs can be at most 10 minutes long");
            if (loopStart < 0 || loopEnd - loopStart < ShortestLoop) throw new ArgumentException("the loop must be at least a second long");
            var gameCsb = UtfTable.Read(game);
            var gameElements = Sub(gameCsb, "SOUND_ELEMENT");
            var csb = UtfTable.Read(bank);
            var cues = Sub(csb, "CUE");
            if (layer >= cues.Rows.Count) throw new ArgumentException("this music has no " + LayerNames[layer] + " layer");
            string own = (string)gameElements.Get(ElementOf(Sub(gameCsb, "CUE"), Sub(gameCsb, "SYNTH"), gameElements, layer), "name");
            int synRow = Row(csb, "name", "SYNTH"), sdlRow = Row(csb, "name", "SOUND_ELEMENT");
            var synths = UtfTable.Read((byte[])csb.Get(synRow, "utf"));
            var elements = UtfTable.Read((byte[])csb.Get(sdlRow, "utf"));
            int e = Row(elements, "name", own);
            if (e < 0) throw new InvalidDataException("the bank lacks the game's sound " + own);
            // the layer plays its own sound element again (it may have shared another layer's)
            Relink(cues, synths, elements, layer, own);

            var aax = UtfTable.Read((byte[])elements.Get(e, "data"));
            var intro = new short[loopStart * 2];
            var loop = new short[(loopEnd - loopStart) * 2];
            Array.Copy(pcm, 0, intro, 0, intro.Length);
            Array.Copy(pcm, loopStart * 2, loop, 0, loop.Length);
            // a song that loops from its very start has no intro: the game's AAX always has both rows, so the intro
            // is one ADX frame of the loop's start (32 samples, 0.7 ms) and the loop starts after it
            if (intro.Length == 0)
            {
                intro = loop.Take(Adx.FrameSamples * 2).ToArray();
                loop = loop.Skip(Adx.FrameSamples * 2).Concat(intro).ToArray();
            }
            aax.Set(0, "data", Adx.Encode(intro, 2, Song.Rate));
            aax.Set(0, "lpflg", (byte)0);
            if (aax.Rows.Count < 2) aax.Rows.Add((object[])aax.Rows[0].Clone());
            aax.Set(1, "data", Adx.Encode(loop, 2, Song.Rate));
            aax.Set(1, "lpflg", (byte)1);
            elements.Set(e, "data", aax.Write());
            if (elements.Column("nsmpl") >= 0) elements.Set(e, "nsmpl", (uint)(intro.Length / 2 + loop.Length / 2));
            if (elements.Column("sfreq") >= 0) elements.Set(e, "sfreq", (uint)Song.Rate);
            if (elements.Column("nch") >= 0) elements.Set(e, "nch", (byte)2);
            csb.Set(synRow, "utf", synths.Write());
            csb.Set(sdlRow, "utf", elements.Write());
            return csb.Write();
        }

        // points every waveform under cue `cue` at sound element `element` (a synth's links name either more synths
        // or sound elements)
        static void Relink(UtfTable cues, UtfTable synths, UtfTable elements, int cue, string element)
        {
            var todo = new Queue<string>();
            var seen = new HashSet<string>();
            todo.Enqueue((string)cues.Get(cue, "synth"));
            while (todo.Count > 0)
            {
                string name = todo.Dequeue();
                if (name == null || !seen.Add(name)) continue;
                int s = Row(synths, "synname", name);
                if (s < 0) continue;
                string links = (string)synths.Get(s, "lnkname") ?? "";
                var parts = links.Split('\n');
                bool changed = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) continue;
                    if (Row(elements, "name", parts[i]) >= 0) { if (parts[i] != element) { parts[i] = element; changed = true; } }
                    else todo.Enqueue(parts[i]);
                }
                if (changed) synths.Set(s, "lnkname", string.Join("\n", parts));
            }
        }

        /// <summary>`bank` with layer `layer` playing what layer `from` plays (its own sound when they're the same);
        /// with `gameSound`, that sound is first put back to the game's (`game` being the game's own bank).</summary>
        public static byte[] Link(byte[] bank, byte[] game, int layer, int from, bool gameSound)
        {
            var gameCsb = UtfTable.Read(game);
            var gameElements = Sub(gameCsb, "SOUND_ELEMENT");
            var csb = UtfTable.Read(bank);
            var cues = Sub(csb, "CUE");
            if (layer >= cues.Rows.Count || from >= cues.Rows.Count) return bank;
            int ge = ElementOf(Sub(gameCsb, "CUE"), Sub(gameCsb, "SYNTH"), gameElements, from);
            string own = (string)gameElements.Get(ge, "name");
            int synRow = Row(csb, "name", "SYNTH"), sdlRow = Row(csb, "name", "SOUND_ELEMENT");
            var synths = UtfTable.Read((byte[])csb.Get(synRow, "utf"));
            var elements = UtfTable.Read((byte[])csb.Get(sdlRow, "utf"));
            int e = Row(elements, "name", own);
            if (e < 0) throw new InvalidDataException("the bank lacks the game's sound " + own);
            if (gameSound)
                for (int c = 0; c < elements.Columns.Count; c++)
                {
                    string column = elements.Columns[c].Name;
                    if (gameElements.Column(column) >= 0) elements.Set(e, column, gameElements.Get(ge, column));
                }
            Relink(cues, synths, elements, from, own);
            Relink(cues, synths, elements, layer, own);
            csb.Set(synRow, "utf", synths.Write());
            csb.Set(sdlRow, "utf", elements.Write());
            return csb.Write();
        }

        /// <summary>Gain bringing `pcm`'s loudness to `target`, kept under clipping (peaks at most -0.5 dB).</summary>
        /// <summary>The gain bringing a song as loud as `target` sounds (both in LUFS, from Loudness).</summary>
        public static double MatchGain(double song, double target)
        {
            if (song <= -99 || target <= -99) return 1.0;
            return Math.Pow(10, (target - song) / 20);
        }

        /// <summary>The song for the low-health layer: bass and a little of the mids cut (AudioFile.BassCut), then
        /// brought back up to 2 LU under the song (the game's own low-health music is 1-2 LU under its main), peaks
        /// limited. It stays sample for sample in step with the song.</summary>
        public static short[] LowHealthVersion(short[] pcm)
        {
            var cut = AudioFile.BassCut(pcm);
            double lost = Loudness(pcm) - Loudness(cut);
            return lost > 2 ? Level(cut, Math.Pow(10, (lost - 2) / 20)) : cut;
        }

        /// <summary>The highest a sample may go: -0.5 dBFS.</summary>
        public const double Ceiling = 0.944 * 32767;

        /// <summary>`pcm` × `gain` with its peaks held under Ceiling by a limiter instead of clipping: it looks 5 ms
        /// ahead, turns down only as much as a peak needs, eases back over about 150 ms, and turns both channels down
        /// together so nothing moves in the stereo image.</summary>
        public static short[] Level(short[] pcm, double gain)
        {
            int n = pcm.Length / 2;
            var need = new float[n];      // the most gain each frame can take
            bool limits = false;
            for (int i = 0; i < n; i++)
            {
                double peak = Math.Max(Math.Abs((int)pcm[2 * i]), Math.Abs((int)pcm[2 * i + 1])) * gain;
                need[i] = peak > Ceiling ? (float)(Ceiling / peak) : 1f;
                limits |= need[i] < 1f;
            }
            if (!limits) return Scale(pcm, gain);
            int look = Song.Rate / 200;
            // the lowest need over each frame's next `look` frames (a sliding minimum, in order)
            var ahead = new float[n];
            var queue = new int[n];
            int head = 0, tail = 0;
            for (int j = 0; j < n + look; j++)
            {
                if (j < n)
                {
                    while (tail > head && need[queue[tail - 1]] >= need[j]) tail--;
                    queue[tail++] = j;
                }
                int i = j - look;
                if (i < 0) continue;
                while (queue[head] < i) head++;
                ahead[i] = need[queue[head]];
            }
            // easing back up after a peak, never above what's needed
            double release = 1 - Math.Exp(-1.0 / (0.15 * Song.Rate)), g = 1;
            for (int i = 0; i < n; i++)
            {
                g = Math.Min(ahead[i], g + (1 - g) * release);
                ahead[i] = (float)g;
            }
            // averaged over the look-ahead, so the gain goes down smoothly before a peak (every frame averaged into
            // frame i's gain looked ahead past i, so none of them is above what frame i needs)
            var o = new short[pcm.Length];
            double window = 0;
            for (int i = 0; i < n; i++)
            {
                window += ahead[i];
                if (i > look) window -= ahead[i - look - 1];
                double env = window / Math.Min(i + 1, look + 1) * gain;
                for (int c = 0; c < 2; c++)
                {
                    double v = pcm[2 * i + c] * env;
                    o[2 * i + c] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(v)));
                }
            }
            return o;
        }

        public static short[] Scale(short[] pcm, double gain)
        {
            if (Math.Abs(gain - 1.0) < 1e-6) return pcm;
            var o = new short[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
            {
                int v = (int)Math.Round(pcm[i] * gain);
                o[i] = (short)(v < -32768 ? -32768 : v > 32767 ? 32767 : v);
            }
            return o;
        }

        // -- installing ----------------------------------------------------------------------------------------------
        static string ListPath(string game) { return Path.Combine(Game.PatchFolder(game), ListName); }

        /// <summary>The songs put in. An unreadable list throws (InvalidDataException), so nothing is installed or
        /// removed, and the list is never written over, until it's fixed.</summary>
        public static Dictionary<string, InstalledSong> Load(string game)
        {
            var result = new Dictionary<string, InstalledSong>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(ListPath(game))) return result;
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(ListPath(game)));
                object list;
                if (root == null || !root.TryGetValue("songs", out list) || !(list is System.Collections.ArrayList)) return result;
                foreach (var j in ((System.Collections.ArrayList)list).OfType<Dictionary<string, object>>())
                {
                    var s = new InstalledSong
                    {
                        File = Convert.ToString(j["file"]), Song = Convert.ToString(j["song"]), Sha256 = Convert.ToString(j["sha256"]),
                        LoopStart = Convert.ToInt32(j["loopStart"]), LoopEnd = Convert.ToInt32(j["loopEnd"]),
                        Backup = j.ContainsKey("backup") && j["backup"] != null ? Convert.ToString(j["backup"]) : null,
                        Layers = j.ContainsKey("layers") && j["layers"] is System.Collections.ArrayList
                            ? ((System.Collections.ArrayList)j["layers"]).Cast<object>().Select(o => o == null ? null : Convert.ToString(o)).Concat(new string[3]).Take(3).ToArray()
                            : new[] { Convert.ToString(j["song"]), null, null },
                    };
                    result[s.File] = s;
                }
            }
            catch (Exception ex) when (!(ex is IOException))
            {
                throw new InvalidDataException(ListName + " in the patch folder can't be read (" + ex.Message + "), so songs can't be put in or given back until it's fixed or deleted");
            }
            return result;
        }

        static void Save(string game, Dictionary<string, InstalledSong> list)
        {
            if (list.Count == 0) { if (File.Exists(ListPath(game))) File.Delete(ListPath(game)); return; }
            var entries = list.Values.Select(s => new Dictionary<string, object>
            {
                { "file", s.File }, { "song", s.Song }, { "sha256", s.Sha256 }, { "loopStart", s.LoopStart }, { "loopEnd", s.LoopEnd },
                { "backup", s.Backup }, { "layers", s.Layers },
            }).ToList();
            Directory.CreateDirectory(Game.PatchFolder(game));
            WriteWhole(ListPath(game), System.Text.Encoding.UTF8.GetBytes(
                new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "format", 1 }, { "songs", entries } })));
        }

        // A file written whole or not at all: a full disk or a crash never leaves half a file where the game reads it.
        static void WriteWhole(string path, byte[] data)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>The stage package EX More Stuff installed that brought this music file (a custom stage's own
        /// BGM_<code>.csb), or null: a song can't go there, or removing or updating the stage would take it.</summary>
        public static string PackageOwning(string game, MusicSlot slot)
        {
            string relative = Path.Combine(Folder, slot.File);
            var record = Installer.Load(game).FirstOrDefault(r => r.Files.Any(f => string.Equals(f, relative, StringComparison.OrdinalIgnoreCase)));
            return record == null ? null : record.Item.TitleNamed(record.Shown ?? record.Item.Name);
        }

        // -- the songs library: Music\ beside the program ---------------------------------------------------------
        // Every song put in the game or saved is kept there, named for where it goes: <code>_<round>_<layer>_<song>, like
        // TRN_3_MAIN_Jackson.mp3 (the song as it was opened), .json (its loop and settings) and .csb (the game file it
        // made). A music mod with .csb files named that way goes in with Add Mod From File.
        public static readonly string[] LayerTags = { "MAIN", "ULTRA", "LOWHP" };
        static readonly Regex Tagged = new Regex(@"^([A-Za-z0-9]{3})_([123])_(MAIN|ULTRA|LOWHP)_(.+)$", RegexOptions.IgnoreCase);

        /// <summary>The library name for a song on `slot`'s `layer`: TRN_3_MAIN_Jackson (a song already named that way
        /// keeps only its own part).</summary>
        public static string LibraryName(MusicSlot slot, int layer, string song)
        {
            Match m = Tagged.Match(song ?? "");
            string name = m.Success ? m.Groups[4].Value : song;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            if (name == "") name = "song";
            return slot.Code + "_" + slot.Round + "_" + LayerTags[Math.Max(0, Math.Min(2, layer))] + "_" + name;
        }

        /// <summary>Keeps a song in the library (overwriting): the game file it made, and, when it came from a song file,
        /// that file as it was and its loop and settings. Returns the name it was kept under.</summary>
        public static string Keep(MusicSlot slot, int layer, string song, byte[] bank, string source, int loopStart, int loopEnd, string settings)
        {
            Directory.CreateDirectory(AppFolders.Music);
            string name = LibraryName(slot, layer, song), stem = Path.Combine(AppFolders.Music, name);
            File.WriteAllBytes(stem + ".csb", bank);
            if (source != null && File.Exists(source) && !source.EndsWith(".csb", StringComparison.OrdinalIgnoreCase))
            {
                string copy = stem + Path.GetExtension(source).ToLowerInvariant();
                if (!string.Equals(Path.GetFullPath(copy), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) File.Copy(source, copy, true);
                File.WriteAllText(stem + ".json", new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    { "format", 1 }, { "loopStart", loopStart }, { "loopEnd", loopEnd }, { "settings", settings },
                }));
            }
            return name;
        }

        /// <summary>A song file's loop and settings kept beside it (TRN_3_MAIN_Jackson.json), or false.</summary>
        public static bool ReadKept(string songFile, out int loopStart, out int loopEnd, out string settings)
        {
            loopStart = loopEnd = -1;
            settings = null;
            string json = Path.ChangeExtension(songFile, ".json");
            if (!File.Exists(json)) return false;
            try
            {
                var j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(json));
                loopStart = Convert.ToInt32(j["loopStart"]);
                loopEnd = Convert.ToInt32(j["loopEnd"]);
                settings = j.ContainsKey("settings") && j["settings"] != null ? Convert.ToString(j["settings"]) : null;
                return loopEnd > loopStart && loopStart >= 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Where a library-named game file goes: TRN_3_MAIN_Jackson.csb -> stage TRN, round 3 (its layer and
        /// song name); RYU_1_MAIN_x.csb -> Ryu's theme. Null when the name isn't one.</summary>
        public static MusicSlot TaggedSlot(string fileName, out int layer, out string song)
        {
            layer = 0;
            song = null;
            Match m = Tagged.Match(Path.GetFileNameWithoutExtension(fileName ?? ""));
            if (!m.Success) return null;
            string code = m.Groups[1].Value.ToUpperInvariant();
            layer = Array.IndexOf(LayerTags, m.Groups[3].Value.ToUpperInvariant());
            song = m.Groups[4].Value;
            int round = int.Parse(m.Groups[2].Value);
            if (Fighters.IsCode(code)) return round == 1 ? new MusicSlot { Code = code, Fighter = true } : null;
            if (Array.IndexOf(Stages.Codes, code) < 0 && !Stages.IsCustomCode(code)) return null;
            return new MusicSlot { Code = code, Round = round };
        }

        /// <summary>Puts a built bank in the patch folder for `slot` (replacing an earlier song of ours there).</summary>
        public static void Install(string game, MusicSlot slot, byte[] bank, InstalledSong about)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            string owner = PackageOwning(game, slot);
            if (owner != null)
                throw new InvalidOperationException(slot.File + " is the music of " + owner + ", installed from its package; save the song as .csb and put it in that package instead");
            var list = Load(game);
            if (list.ContainsKey(slot.File))
            {
                string problem = Remove(game, slot);
                if (problem != null) throw new IOException(problem);
            }
            list = Load(game);
            string path = PatchFile(game, slot.File), relative = Path.Combine(Folder, slot.File), temp = path + ".tmp";
            string to = Path.Combine(Game.PatchFolder(game), BackupFolder, relative), backup = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(temp, bank);   // a full disk fails here, before anything in the game folder changes
            try
            {
                if (File.Exists(path))
                {
                    if (File.Exists(to)) throw new IOException("an older copy of " + slot.File + " is already in " + BackupFolder + "; move it back or delete it first");
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Move(path, to);
                    backup = relative;
                }
                File.Move(temp, path);
            }
            catch
            {
                if (backup != null && !File.Exists(path) && File.Exists(to)) File.Move(to, path);
                throw;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            about.File = slot.File;
            about.Sha256 = Installer.Sha256(path);
            about.Backup = backup;
            list[slot.File] = about;
            Save(game, list);
        }

        /// <summary>Songs put on a custom stage (BGM_<code>.csb, round 2, round 3) given back when its package is deleted.
        /// Returns what couldn't be done, or null.</summary>
        public static string RemoveCode(string game, string code)
        {
            var problems = new List<string>();
            foreach (string file in Load(game).Keys.Where(f => System.Text.RegularExpressions.Regex.IsMatch(f, "^BGM_" + code + "[23]?\\.csb$",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList())
            {
                string problem = Remove(game, MusicSlot.FromFile(file));
                if (problem != null) problems.Add(problem);
            }
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>Songs put on a custom stage (BGM_<code>.csb, round 2, round 3) moved to its new code when its package
        /// changes code. Returns which stayed (moved aside a file, or changed since), or null.</summary>
        public static string MoveCode(string game, string from, string to)
        {
            var list = Load(game);
            var stayed = new List<string>();
            foreach (string file in list.Keys.ToList())
            {
                var m = System.Text.RegularExpressions.Regex.Match(file, "^BGM_" + from + "([23]?)\\.csb$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                InstalledSong song = list[file];
                string now = "BGM_" + to + m.Groups[1].Value + ".csb", path = PatchFile(game, file), target = PatchFile(game, now);
                if (song.Backup != null || !File.Exists(path) || File.Exists(target) || Installer.Sha256(path) != song.Sha256) { stayed.Add(file); continue; }
                File.Move(path, target);
                list.Remove(file);
                song.File = now;
                list[now] = song;
            }
            Save(game, list);
            return stayed.Count == 0 ? null : string.Join(", ", stayed) + " stayed under " + from + ": put " + (stayed.Count > 1 ? "them" : "it") + " in again on the Music page";
        }

        /// <summary>Takes our song off a slot: the file goes (unless something else replaced it since), a file we moved
        /// aside comes back. Returns what couldn't be done, or null.</summary>
        public static string Remove(string game, MusicSlot slot)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var list = Load(game);
            InstalledSong s;
            if (!list.TryGetValue(slot.File, out s)) return null;
            string path = PatchFile(game, slot.File), problem = null;
            if (File.Exists(path))
            {
                if (Installer.Sha256(path) == s.Sha256) File.Delete(path);
                else problem = slot.File + " changed since EX More Stuff wrote it, so it was left as it is" +
                               (s.Backup != null ? "; the file it replaced is still in " + BackupFolder : "");
            }
            if (s.Backup != null)
            {
                string from = Path.Combine(Game.PatchFolder(game), BackupFolder, s.Backup);
                if (File.Exists(from) && !File.Exists(path)) File.Move(from, path);
            }
            list.Remove(slot.File);
            Save(game, list);
            return problem;
        }
    }
}
