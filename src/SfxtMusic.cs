using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    // Street Fighter X Tekken's music for the Music page ("From SFxT..."), read from the player's own SFxT, never
    // changed. A bank is a CRI ADX2 cue sheet resource\CMN\battle\sound\bgm\BGM_<X>.acb (@UTF tables) with its audio in
    // stream\CMN\battle\sound\bgm\BGM_<X>.awb, or in the cue sheet's own AwbFile; both are CPKs (an @UTF header and an
    // ID table, the files in ID order from ContentOffset, aligned). A stage's cues (Level1-3, Round1-2) are each a 5.1
    // mix in six mono HCA waveforms, one per child synth, placed by the child's commands (0x000B pan in 0.1 degrees,
    // 0x004D centre / LFE send); they're mixed to stereo as ffmpeg does (centre and each rear side at -3 dB, the LFE
    // left out, scaled so nothing clips) and keep the loop their HCA files carry. docs\EXMS_SFXT_MUSIC.md has the
    // research.
    static class SfxtMusic
    {
        public static readonly string[] StageCodes = { "ANT", "BFU", "DET", "ELV", "HFP", "JUR", "MAD", "MSR", "PDB", "TRN", "UWZ" };
        const string BankFolder = @"resource\CMN\battle\sound\bgm", StreamFolder = @"stream\CMN\battle\sound\bgm";

        public sealed class Bank
        {
            public string Code, Acb, Awb;   // Awb null: the audio is in the cue sheet
            public List<string> Cues = new List<string>();
            public bool Stage { get { return Array.IndexOf(StageCodes, Code) >= 0; } }
            public string Name { get { return SfxtMusic.Name(Code); } }
        }

        public static bool IsInstall(string folder)
        {
            return !string.IsNullOrEmpty(folder) && Directory.Exists(Path.Combine(folder, BankFolder));
        }

        /// <summary>SFxT's folder: the one chosen before, else Street Fighter X Tekken in a Steam library; or null.</summary>
        public static string Find()
        {
            string kept = Settings.SfxtFolder;
            if (IsInstall(kept)) return kept;
            foreach (string library in Game.SteamLibraries())
            {
                string folder = Path.Combine(library, "steamapps", "common", "Street Fighter X Tekken");
                if (IsInstall(folder)) return folder;
            }
            return null;
        }

        /// <summary>A bank's name: the stage's (the SF4 name for the stages USF4 has too), else its code.</summary>
        public static string Name(string code)
        {
            if (Array.IndexOf(Stages.Codes, code) >= 0) return Stages.Name(code);
            if (code == "MSR") return "Mishima Estate";
            return code;
        }

        /// <summary>A cue's name for the menu: "Level1" as "Level 1".</summary>
        public static string CueName(string cue)
        {
            var m = System.Text.RegularExpressions.Regex.Match(cue ?? "", @"^(Level|Round)(\d+)$");
            return m.Success ? m.Groups[1].Value + " " + m.Groups[2].Value : cue;
        }

        public static List<Bank> Banks(string sfxt)
        {
            var banks = new List<Bank>();
            foreach (string acb in Directory.GetFiles(Path.Combine(sfxt, BankFolder), "BGM_*.acb").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string code = Path.GetFileNameWithoutExtension(acb).Substring(4).ToUpperInvariant();
                string awb = Path.Combine(sfxt, StreamFolder, "BGM_" + code + ".awb");
                var bank = new Bank { Code = code, Acb = acb, Awb = File.Exists(awb) ? awb : null };
                try
                {
                    UtfTable sheet = UtfTable.Read(File.ReadAllBytes(acb));
                    UtfTable names = Table(sheet, "CueNameTable"), cues = Table(sheet, "CueTable");
                    for (int i = 0; i < cues.Rows.Count; i++) bank.Cues.Add("Cue " + (i + 1));
                    if (names != null)
                        for (int r = 0; r < names.Rows.Count; r++)
                        {
                            int index = Convert.ToInt32(names.Get(r, "CueIndex"));
                            if (index >= 0 && index < bank.Cues.Count) bank.Cues[index] = Convert.ToString(names.Get(r, "CueName"));
                        }
                }
                catch (Exception) { continue; }   // not a cue sheet this can read
                if (bank.Cues.Count > 0) banks.Add(bank);
            }
            return banks;
        }

        static UtfTable Table(UtfTable sheet, string column)
        {
            if (sheet.Column(column) < 0) return null;
            var data = sheet.Get(0, column) as byte[];
            return data != null && UtfTable.IsUtf(data) ? UtfTable.Read(data) : null;
        }

        static int Int(UtfTable t, int row, string column) { return t.Column(column) < 0 ? 0 : Convert.ToInt32(t.Get(row, column)); }

        static ushort U16(byte[] b, int o) { return (ushort)(b[o] << 8 | b[o + 1]); }

        // the speaker a child synth's commands put it on: FL FR FC LFE BL BR, or null
        static string Speaker(byte[] command)
        {
            for (int i = 0; command != null && i + 3 <= command.Length;)
            {
                int code = U16(command, i), size = command[i + 2];
                if (i + 3 + size > command.Length) break;
                if (code == 0x000B && size == 2)
                {
                    switch ((short)U16(command, i + 3))
                    {
                        case -300: return "FL";
                        case 300: return "FR";
                        case -1200: return "BL";
                        case 1200: return "BR";
                    }
                }
                if (code == 0x004D && size == 4) return U16(command, i + 3) >= U16(command, i + 5) ? "FC" : "LFE";
                i += 3 + size;
            }
            return null;
        }

        /// <summary>One cue of a bank as a song in the game's format (44.1 kHz stereo) with its own loop.</summary>
        public static Song Load(Bank bank, int cue)
        {
            int rate, loopStart, loopEnd;
            float[] stereo = Stereo(bank, cue, out rate, out loopStart, out loopEnd);
            var song = new Song { Pcm = AudioFile.ToGameFormat(stereo, rate, 2), Name = bank.Name + " " + CueName(bank.Cues[cue]) };
            if (loopStart >= 0 && loopEnd > loopStart)
            {
                song.LoopStart = (int)((long)loopStart * Song.Rate / rate);
                song.LoopEnd = Math.Min(song.Frames, (int)((long)loopEnd * Song.Rate / rate));
                song.GameLoop = true;
            }
            return song;
        }

        /// <summary>One cue mixed to stereo at its own rate (interleaved, -1..1), with its loop in samples (or -1).</summary>
        public static float[] Stereo(Bank bank, int cue, out int rate, out int loopStart, out int loopEnd)
        {
            UtfTable sheet = UtfTable.Read(File.ReadAllBytes(bank.Acb));
            UtfTable cues = Table(sheet, "CueTable"), synths = Table(sheet, "SynthTable"), commands = Table(sheet, "CommandTable"), waves = Table(sheet, "WaveformTable");
            if (cues == null || waves == null || cue < 0 || cue >= cues.Rows.Count) throw new InvalidDataException("BGM_" + bank.Code + " has no such song");

            // its waveforms: a synth's children (each a waveform, placed by its commands) or, for anything else, the
            // bank's one waveform
            var parts = new List<KeyValuePair<string, int>>();   // speaker (or null), waveform row
            if (Int(cues, cue, "ReferenceType") == 2 && synths != null)
            {
                int synth = Int(cues, cue, "ReferenceIndex");
                var items = synths.Get(synth, "ReferenceItems") as byte[] ?? new byte[0];
                for (int k = 0; k + 4 <= items.Length; k += 4)
                {
                    int type = U16(items, k), index = U16(items, k + 2);
                    if (type == 1) parts.Add(new KeyValuePair<string, int>(null, index));
                    else if (type == 2 && index < synths.Rows.Count)
                    {
                        var child = synths.Get(index, "ReferenceItems") as byte[] ?? new byte[0];
                        if (child.Length < 4 || U16(child, 0) != 1) continue;
                        byte[] command = null;
                        if (commands != null && synths.Column("CommandIndex") >= 0)
                        {
                            int c = Int(synths, index, "CommandIndex");
                            if (c >= 0 && c < commands.Rows.Count) command = commands.Get(c, "Command") as byte[];
                        }
                        parts.Add(new KeyValuePair<string, int>(Speaker(command), U16(child, 2)));
                    }
                }
            }
            if (parts.Count == 0 && waves.Rows.Count == 1) parts.Add(new KeyValuePair<string, int>(null, 0));
            if (parts.Count == 0) throw new InvalidDataException("BGM_" + bank.Code + "'s " + bank.Cues[cue] + " isn't laid out in a way this can read");

            // decoded and mixed to stereo as they come (one channel in memory at a time)
            float[] left = null, right = null;
            rate = 0; loopStart = -1; loopEnd = -1;
            bool surround = parts.Count == 6 && parts.All(p => p.Key != null);
            const float side = 0.70710677f;
            float norm = surround ? 1f / (1f + side + side) : 1f;   // ffmpeg's normalized 5.1 downmix
            byte[] memory = sheet.Column("AwbFile") >= 0 ? sheet.Get(0, "AwbFile") as byte[] : null;
            foreach (var part in parts)
            {
                if (part.Value >= waves.Rows.Count) throw new InvalidDataException("BGM_" + bank.Code + " points at a waveform it doesn't have");
                if (Int(waves, part.Value, "EncodeType") != 2) throw new InvalidDataException("BGM_" + bank.Code + " isn't HCA audio");
                int id = Int(waves, part.Value, "Id");
                byte[] file = Int(waves, part.Value, "Streaming") != 0 ? CpkFile(bank.Awb, null, id) : CpkFile(null, memory, id);
                HcaAudio audio = Hca.Decode(file);
                if (left == null)
                {
                    rate = audio.SampleRate;
                    left = new float[audio.Length];
                    right = new float[audio.Length];
                    loopStart = audio.LoopStart;
                    loopEnd = audio.LoopEnd;
                }
                int n = Math.Min(left.Length, audio.Length);
                for (int c = 0; c < audio.Channels; c++)
                {
                    float l, r;
                    string speaker = audio.Channels == 1 ? part.Key : c == 0 ? "FL" : c == 1 ? "FR" : null;
                    switch (speaker)
                    {
                        case "FL": l = 1; r = 0; break;
                        case "FR": l = 0; r = 1; break;
                        case "BL": l = side; r = 0; break;
                        case "BR": l = 0; r = side; break;
                        case "FC": l = r = side; break;
                        case "LFE": l = r = 0; break;
                        default: l = r = parts.Count == 1 && audio.Channels == 1 ? 1 : side; break;
                    }
                    float[] s = audio.Samples[c];
                    l *= norm; r *= norm;
                    if (l != 0) for (int i = 0; i < n; i++) left[i] += s[i] * l;
                    if (r != 0) for (int i = 0; i < n; i++) right[i] += s[i] * r;
                }
            }
            var stereo = new float[left.Length * 2];
            for (int i = 0; i < left.Length; i++) { stereo[2 * i] = left[i]; stereo[2 * i + 1] = right[i]; }
            return stereo;
        }

        // one file of a CPK by its ID (from a file on disk, or bytes already read)
        static byte[] CpkFile(string path, byte[] bytes, int id)
        {
            if (path == null && bytes == null) throw new InvalidDataException("the song's audio file is missing");
            using (Stream s = path != null ? (Stream)File.OpenRead(path) : new MemoryStream(bytes, false))
            {
                Func<long, byte[]> utfAt = at =>
                {
                    var head = new byte[8];
                    s.Position = at;
                    if (s.Read(head, 0, 8) != 8 || head[0] != '@' || head[1] != 'U' || head[2] != 'T' || head[3] != 'F') throw new InvalidDataException("the song's audio file is damaged");
                    int size = 8 + (head[4] << 24 | head[5] << 16 | head[6] << 8 | head[7]);
                    var table = new byte[size];
                    s.Position = at;
                    if (s.Read(table, 0, size) != size) throw new InvalidDataException("the song's audio file is cut short");
                    return table;
                };
                UtfTable header = UtfTable.Read(utfAt(16));
                long content = Convert.ToInt64(header.Get(0, "ContentOffset")), itocAt = Convert.ToInt64(header.Get(0, "ItocOffset"));
                int align = Math.Max(1, header.Column("Align") >= 0 ? Convert.ToInt32(header.Get(0, "Align")) : 1);
                UtfTable itoc = UtfTable.Read(utfAt(itocAt + 16));
                var sizes = new SortedDictionary<int, long>();
                foreach (string part in new[] { "DataL", "DataH" })
                {
                    var data = itoc.Column(part) >= 0 ? itoc.Get(0, part) as byte[] : null;
                    if (data == null || !UtfTable.IsUtf(data)) continue;
                    UtfTable list = UtfTable.Read(data);
                    for (int r = 0; r < list.Rows.Count; r++) sizes[Convert.ToInt32(list.Get(r, "ID"))] = Convert.ToInt64(list.Get(r, "FileSize"));
                }
                long offset = content;
                foreach (var file in sizes)
                {
                    if (file.Key == id)
                    {
                        var result = new byte[file.Value];
                        s.Position = offset;
                        if (s.Read(result, 0, result.Length) != result.Length) throw new InvalidDataException("the song's audio file is cut short");
                        return result;
                    }
                    offset += file.Value;
                    offset += (align - offset % align) % align;
                }
                throw new InvalidDataException("the song's audio isn't in its file");
            }
        }
    }
}
