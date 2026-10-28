using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // The costume check (FRaccie 2026-10-23): what in a custom costume breaks it in the game or online, read from the
    // files, with a fixer for what can be put right and a text report to look into. The rules come from the decompiled
    // game (Steam 1.05) and the costumes seen so far:
    //  - Online, a player without the costume loads its stand-in, the fighter's costume 1 (2 or 3 in a mirror match,
    //    Ember's ResolveAppearance). Gameplay reads 37 "locator" bones of the loaded model, found by name
    //    (Actor::getLocatorBoneName 0x563260, table 0xa60640): the 24 body hurtboxes run between them
    //    (buildBodyHurtQuads 0x55b2a0, pairs 0x94ffe8), WAIST moves the fighter, LND sets the position, TRS is the
    //    distance, height and close/far. A bone's translation, rotation and scale come from the animation only where it
    //    keys them, else from the model's rest pose (EmModel::calcBoneMatrices 0x6c6ea0); arms and legs get their
    //    rotation from the model's IK records (solveConstraints 0x6c9390). So the costume's rest pose of those bones must
    //    be the stand-in's, bit for bit, or the match desyncs (Samus AGL_09, 2026-10-22).
    //  - An .obj.emm with more materials than its model declares (part table +2, 0 = 256) isn't used at all
    //    (EmModel::bindMaterialSet 0x6c6080): that color draws untextured. A material the model uses that the .obj.emm
    //    lacks draws white (Juri JRI_38, Sakura SKR_34, Ken KEN_08); several long-loved skins have that and play, so it's
    //    worth a look, not a fault.
    //  - The game loads .obj.emo, .shd.emo, .nml.emb, .bsr per costume (Vega also .clw.emo, .msk.emo;
    //    CostumeResourceHolder::load 0x5e5f80); Ember only offers a costume with all ten colors.
    //  - From the older check (2026-10-22): a model part pointing past the end of its file closes the game (GameBanana's
    //    Raiden for M. Bison, a texture count 1 -> 6), as do more than 24 bones in one skin palette and more than 31
    //    submeshes in one part (35 closed it).
    // Only what the check needs is read (the models, the colors' .obj.emm, the other files' names), one costume at a time.
    static class CostumeCheck
    {
        // Actor::getLocatorBoneName's table; findBoneIndex compares without case
        static readonly string[] Gameplay = { "WAIST", "STOMACH", "CHEST", "NECK", "HEAD", "LARM1", "LARM2", "LHANDROT", "LHN",
            "LLEG1", "LLEG2", "LFOOT", "LFN", "RARM1", "RARM2", "RHANDROT", "RHN", "RLEG1", "RLEG2", "RFOOT", "RFN", "LHOPTION",
            "RHOPTION", "TRS", "LND" };
        // only effects and sounds use these, as far as traced
        static readonly string[] Effects = { "FACE_LEYE", "FACE_REYE", "WAN", "HEN", "HEOPTION", "LARMEFF", "RARMEFF", "LLEGEFF", "RLEGEFF" };

        static readonly Regex Model = new Regex(@"^([A-Za-z]{3})_(\d\d)\.obj\.emo$", RegexOptions.IgnoreCase);
        // the mod's own marker files (Installer, CodeChange): they go into a fixed copy with the costume
        static readonly Regex Marker = new Regex(@"^(slot_[A-Za-z]{3}_\d\d|code_.+|needs_exms_.+|override_.+)\.txt$", RegexOptions.IgnoreCase);

        const int PaletteLimit = 24, SubmeshLimit = 31;

        // Look: plays the same online, but something draws wrong (WHITE)
        public enum Verdict { Ok, Mirror, Noise, Look, Faulty }

        public sealed class Finding
        {
            // Kind: DESYNC, DESYNC?, IK, EFFECTS, WHITE, EMM>CAP, FILES, COLORS, SLOT, STOCK, BROKEN, PALETTE, SUBMESHES, UNREAD
            public string Kind, Text;
            public bool Fixable;
            public int Bones;   // DESYNC, DESYNC?: the bones moved or small against the stand-in (InstallCheck says how many)
        }

        public sealed class Result
        {
            public string Name, Where;   // "AGL_09", the folder or zip (read again for a fix)
            public Verdict Verdict;
            public readonly List<Finding> Findings = new List<Finding>();
        }

        // what puts a costume under "Need a fix"
        public static readonly string[] Faults = { "FILES", "EMM>CAP", "COLORS", "SLOT", "BROKEN", "PALETTE", "SUBMESHES", "UNREAD" };

        // ---- reading the files ----

        sealed class Bone
        {
            public string Name;
            public int Parent;   // -1: none
            public float[] Local;   // 16, row vectors
        }

        static int I32(byte[] d, int o) { return BitConverter.ToInt32(d, o); }
        static int U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }

        static string CString(byte[] d, int o)
        {
            int end = o;
            while (end < d.Length && d[end] != 0) end++;
            return Encoding.GetEncoding(28591).GetString(d, o, end - o);
        }

        // a file packed as #EMZ (a 16-byte header, then deflate) as the game reads it; any other as it is
        static byte[] Unwrap(byte[] d)
        {
            if (d == null || d.Length < 16 || Encoding.ASCII.GetString(d, 0, 4) != "#EMZ") return d;
            using (var src = new MemoryStream(d, 16, d.Length - 16))
            using (var z = new DeflateStream(src, CompressionMode.Decompress))
            using (var o = new MemoryStream()) { z.CopyTo(o); return o.ToArray(); }
        }

        /// <summary>A model's skeleton: names, parents, rest matrices (node records of 80 bytes, matrix at +16).</summary>
        static List<Bone> Skeleton(byte[] emo)
        {
            var bones = new List<Bone>();
            int s = I32(emo, 16);
            if (s == 0) return bones;
            int count = U16(emo, s), nodes = s + I32(emo, s + 8), names = s + I32(emo, s + 12);
            for (int i = 0; i < count; i++)
            {
                int o = nodes + i * 80, parent = U16(emo, o);
                var m = new float[16];
                for (int k = 0; k < 16; k++) m[k] = BitConverter.ToSingle(emo, o + 16 + k * 4);
                bones.Add(new Bone { Name = CString(emo, s + I32(emo, names + i * 4)), Parent = parent < count && parent != i ? parent : -1, Local = m });
            }
            return bones;
        }

        /// <summary>How many materials the model takes (part table +2; 0 means 256, EmModelData 0x718ad0).</summary>
        static int MaterialCapacity(byte[] emo)
        {
            int capacity = BitConverter.ToInt16(emo, I32(emo, 0xc) + 2);
            return capacity > 0 ? capacity : 256;
        }

        // Every model record of the model: parts (part table), their EMG blocks, the models in those (layout as the
        // usf4-nesica tools fix_costume.py / render_usf4_costumes.py read it).
        static IEnumerable<int> Models(byte[] emo)
        {
            int pt = I32(emo, 0xc);
            for (int p = 0; p < BitConverter.ToInt16(emo, pt); p++)
            {
                int po = pt + I32(emo, pt + 8 + p * 4);
                for (int g = 0; g < I32(emo, po); g++)
                {
                    int go = po + I32(emo, po + 4 + g * 4);
                    for (int r = 0; r < U16(emo, go + 6); r++) yield return go + I32(emo, go + 8 + r * 4);
                }
            }
        }

        // a model record's submeshes: (record offset, material name offset)
        static IEnumerable<int> Submeshes(byte[] emo, int model)
        {
            int count = U16(emo, model + 0x1a), table = I32(emo, model + 0x1c);
            for (int i = 0; i < count; i++) yield return model + I32(emo, model + table + i * 4) + 0x10;
        }

        static List<string> UsedMaterials(byte[] emo)
        {
            return Models(emo).SelectMany(m => Submeshes(emo, m)).Select(s => CString(emo, s + 6)).Distinct().ToList();
        }

        /// <summary>The material names of an .obj.emm.</summary>
        static List<string> Materials(byte[] emm)
        {
            int t = I32(emm, 0xc);
            return Enumerable.Range(0, I32(emm, t)).Select(i => CString(emm, t + I32(emm, t + 4 + i * 4))).ToList();
        }

        // The game finds materials whatever the case (304 of the stock models' names differ from their .emm only in case:
        // Juri's "skin", Yun's "Shoes"), so every lookup here ignores it.
        static bool Has(List<string> materials, string name) { return materials.Contains(name, StringComparer.OrdinalIgnoreCase); }

        // the fighters' animations read once (only bone names and kinds are kept)
        static readonly Dictionary<string, Dictionary<string, HashSet<int>>> unkeyed = new Dictionary<string, Dictionary<string, HashSet<int>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Upper-case bone name -> the transform kinds (0 translation, 1 rotation, 2 scale) that at least one of the
        /// fighter's animations leaves to the model. Animation files: every .ema of the fighter but the camera's; in the face
        /// file only the bones it animates count (a layer of its own).</summary>
        static Dictionary<string, HashSet<int>> Unkeyed(string game, string fighter)
        {
            lock (unkeyed)
            {
                Dictionary<string, HashSet<int>> known;
                if (unkeyed.TryGetValue(game + "|" + fighter, out known)) return known;
            }
            var loose = new Dictionary<string, HashSet<int>>();
            foreach (string kind in new[] { "obj", "uc1", "uc2", "dm1", "dm2", "fce" })
            {
                string path = SkinImport.GameFile(game, fighter, fighter + "." + kind + ".ema");
                if (path == null) continue;
                byte[] d = File.ReadAllBytes(path);
                int s = I32(d, 12);
                if (s == 0) continue;
                var names = Enumerable.Range(0, U16(d, s)).Select(i => CString(d, s + I32(d, s + I32(d, s + 12) + i * 4)).ToUpperInvariant()).ToList();
                int header = U16(d, 6);
                for (int a = 0; a < U16(d, 16); a++)
                {
                    int o = I32(d, header + a * 4);
                    if (o == 0) continue;
                    var axes = new Dictionary<int, HashSet<int>>();   // bone * 3 + kind -> axes keyed
                    for (int c = 0; c < U16(d, o + 2); c++)
                    {
                        int co = o + I32(d, o + 20 + c * 4);
                        int key = U16(d, co) * 3 + d[co + 2];
                        HashSet<int> set;
                        if (!axes.TryGetValue(key, out set)) axes[key] = set = new HashSet<int>();
                        set.Add(d[co + 3] & 3);
                    }
                    var animated = new HashSet<int>(axes.Keys.Select(k => k / 3));
                    for (int b = 0; b < names.Count; b++)
                    {
                        if (kind == "fce" && !animated.Contains(b)) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            HashSet<int> set;
                            if (axes.TryGetValue(b * 3 + k, out set) && set.Count == 3) continue;
                            HashSet<int> l;
                            if (!loose.TryGetValue(names[b], out l)) loose[names[b]] = l = new HashSet<int>();
                            l.Add(k);
                        }
                    }
                }
            }
            lock (unkeyed) unkeyed[game + "|" + fighter] = loose;
            return loose;
        }

        // ---- what closes the game (the older check's rules, its header-only reading) ----

        sealed class Shape
        {
            public string Broken;   // why the model can't be read as the game reads it, or null
            public int Palette, Submeshes;   // the most bones in one skin palette / submeshes in one part
        }

        static void Need(byte[] d, long offset, long length, string what)
        {
            if (offset < 0 || length < 0 || offset + length > d.Length) throw new InvalidDataException(what);
        }

        /// <summary>Every pointer and count of a model checked against the file's size, as the game follows them, with the
        /// most bones in one skin palette and the most submeshes in one part.</summary>
        static Shape Structure(byte[] d)
        {
            var shape = new Shape();
            try
            {
                Need(d, 0, 0x30, "too short");
                if (Encoding.ASCII.GetString(d, 0, 4) != "#EMO") throw new InvalidDataException("not a model file");
                int s = I32(d, 16), bones = 0;
                if (s != 0)
                {
                    Need(d, s, 0x24, "the skeleton is past the end");
                    bones = U16(d, s);
                    int nodes = I32(d, s + 8), names = I32(d, s + 12), inverse = I32(d, s + 0x1c);
                    Need(d, (long)s + nodes, bones * 80L, "the skeleton's bones run past the end");
                    Need(d, (long)s + names, bones * 4L, "the skeleton's names run past the end");
                    if (inverse != 0) Need(d, (long)s + inverse, bones * 64L, "the skeleton's bind matrices run past the end");
                    for (int i = 0; i < bones; i++)
                    {
                        int parent = BitConverter.ToInt16(d, s + nodes + i * 80);
                        if (parent >= bones) throw new InvalidDataException("bone " + i + " has parent " + parent + " of " + bones);
                        Need(d, (long)s + I32(d, s + names + i * 4), 1, "bone " + i + "'s name is past the end");
                    }
                }
                int pt = I32(d, 0xc);
                Need(d, pt, 8, "its part table is past the end");
                for (int p = 0; p < BitConverter.ToInt16(d, pt); p++)
                {
                    Need(d, pt + 8 + p * 4L, 4, "the part list runs past the end");
                    long po = (long)pt + I32(d, pt + 8 + p * 4);
                    Need(d, po, 4, "part " + p + " is past the end");
                    int groups = I32(d, (int)po), sub = 0;
                    if (groups < 0 || groups > 4096) throw new InvalidDataException("part " + p + " has " + groups + " blocks");
                    for (int g = 0; g < groups; g++)
                    {
                        Need(d, po + 4 + g * 4L, 4, "part " + p + "'s block list runs past the end");
                        long go = po + I32(d, (int)(po + 4 + g * 4));
                        Need(d, go, 8, "part " + p + "'s block " + g + " is past the end");
                        for (int r = 0; r < U16(d, (int)go + 6); r++)
                        {
                            Need(d, go + 8 + r * 4L, 4, "part " + p + "'s model list runs past the end");
                            sub += PartModel(d, go + I32(d, (int)(go + 8 + r * 4)), p, bones, shape);
                        }
                    }
                    shape.Submeshes = Math.Max(shape.Submeshes, sub);
                }
            }
            catch (InvalidDataException e) { shape.Broken = e.Message; }
            return shape;
        }

        // a model record of a part: its texture slots, vertices and submeshes inside the file; returns its submeshes
        static int PartModel(byte[] d, long at, int part, int boneCount, Shape shape)
        {
            string where = "part " + part;
            Need(d, at, 80, where + " is past the end");
            int b = (int)at;
            int textures = I32(d, b + 4), list = I32(d, b + 12), vcount = U16(d, b + 16), stride = U16(d, b + 18);
            int vlist = I32(d, b + 20), subs = U16(d, b + 26), sublist = I32(d, b + 28);
            if (textures < 0 || textures > 4096) throw new InvalidDataException(where + " has " + textures + " texture slots");
            // only the slots its submeshes use: the game reads no other (Raiden Bison's part 12 lists 6, slots 2-5 are junk,
            // only slot 0 is used, and it plays fine; flagged as damaged before, user 2026-10-27)
            var used = new HashSet<int>();
            for (int s = 0; s < subs; s++)
            {
                Need(d, (long)b + sublist + s * 4L, 4, where + "'s submesh list runs past the end");
                long sp = (long)b + I32(d, b + sublist + s * 4);
                Need(d, sp, 54, where + "'s submesh " + s + " is past the end");
                used.Add(U16(d, (int)sp + 16));
            }
            for (int t = 0; t < textures; t++)
            {
                if (!used.Contains(t)) continue;
                Need(d, (long)b + list + t * 4L, 4, where + "'s texture list runs past the end");
                long tp = (long)b + I32(d, b + list + t * 4);
                Need(d, tp, 4, where + "'s texture slot " + t + " is past the end");
                // the layer count is the record's first 16 bits: some exporters leave a number in the next two bytes
                // (Alpha Cammy's 02 00 01 00, read whole as 65538 layers, flagged a model that plays fine; Tom 2026-10-24)
                int layers = U16(d, (int)tp);
                if (layers < 0 || layers > 64) throw new InvalidDataException(where + "'s texture slot " + t + " has " + layers + " layers");
                Need(d, tp + 4, layers * 12L, where + "'s texture slot " + t + " runs past the end");
            }
            Need(d, (long)b + vlist, (long)vcount * stride, where + "'s vertices run past the end");
            for (int s = 0; s < subs; s++)
            {
                Need(d, (long)b + sublist + s * 4L, 4, where + "'s submesh list runs past the end");
                long sp = (long)b + I32(d, b + sublist + s * 4);
                Need(d, sp, 54, where + "'s submesh " + s + " is past the end");
                int material = U16(d, (int)sp + 16), indices = U16(d, (int)sp + 18), bones = U16(d, (int)sp + 20);
                if (material >= textures && textures > 0) throw new InvalidDataException(where + "'s submesh " + s + " uses texture slot " + material + " of " + textures);
                Need(d, sp + 54, indices * 2L + bones * 2L, where + "'s submesh " + s + " runs past the end");
                for (int k = 0; k < bones; k++)
                {
                    int bone = U16(d, (int)sp + 54 + indices * 2 + k * 2);
                    if (boneCount > 0 && bone >= boneCount) throw new InvalidDataException(where + " is weighted to bone " + bone + " of " + boneCount);
                }
                shape.Palette = Math.Max(shape.Palette, bones);
            }
            return subs;
        }

        // ---- the locator rule ----

        // upper-case name -> bone, and every bone on the way from a locator to the root -> the locators it moves
        static Dictionary<string, Bone> ByName(List<Bone> bones)
        {
            var by = new Dictionary<string, Bone>();
            foreach (Bone b in bones) by[b.Name.ToUpperInvariant()] = b;
            return by;
        }

        static string ParentName(List<Bone> bones, Bone b) { return b.Parent < 0 ? null : bones[b.Parent].Name.ToUpperInvariant(); }

        // a bone and its parents up to the root; in a damaged skeleton whose parents run in a circle, up to the repeat
        static IEnumerable<int> Up(List<Bone> bones, int i)
        {
            var seen = new HashSet<int>();
            while (i >= 0 && i < bones.Count && seen.Add(i)) { yield return i; i = bones[i].Parent; }
        }

        static Dictionary<string, HashSet<string>> Chains(List<Bone> bones)
        {
            var by = ByName(bones);
            var above = new Dictionary<string, HashSet<string>>();
            foreach (string locator in Gameplay.Concat(Effects))
            {
                string name = locator;
                var seen = new HashSet<string>();   // names can repeat: a walk by name could go round
                Bone b;
                while (name != null && seen.Add(name) && by.TryGetValue(name, out b))
                {
                    HashSet<string> set;
                    if (!above.TryGetValue(name, out set)) above[name] = set = new HashSet<string>();
                    set.Add(locator);
                    name = ParentName(bones, b);
                }
            }
            return above;
        }

        static float[] Multiply(float[] a, float[] b)
        {
            var r = new float[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    double v = 0;
                    for (int k = 0; k < 4; k++) v += (double)a[i * 4 + k] * b[k * 4 + j];
                    r[i * 4 + j] = (float)v;
                }
            return r;
        }

        // the bone's rest pose times its parents' (row vectors: local x parent x ...)
        static float[] World(List<Bone> bones, int i)
        {
            float[] m = null;
            foreach (int b in Up(bones, i)) m = m == null ? (float[])bones[b].Local.Clone() : Multiply(m, bones[b].Local);
            return m;
        }

        static string Size(double d) { return d >= 1e-3 ? "moved" : d >= 1e-4 ? "small" : "noise"; }

        static string Number(double d) { return d.ToString(d < 1e-4 ? "0.0e0" : "0.000000", System.Globalization.CultureInfo.InvariantCulture); }

        sealed class Difference
        {
            public string Level, Bone, What, Size;
        }

        /// <summary>Where the custom model's locator chains differ from the stand-in model in what the animations leave to
        /// the model. Level DESYNC (gameplay locator, translation or scale, missing, other parent), DESYNC? (a rotation IK may
        /// override), IK (the IK records of gameplay bones differ), EFFECTS (only effect locators).</summary>
        static List<Difference> LocatorDifferences(byte[] custom, byte[] standIn, Dictionary<string, HashSet<int>> loose)
        {
            List<Bone> mine = Skeleton(custom), theirs = Skeleton(standIn);
            Dictionary<string, Bone> myBy = ByName(mine), theirBy = ByName(theirs);
            var need = Chains(theirs);
            // above TRS the bones are named per model: TRS's world rest pose covers them
            var roots = new HashSet<string>();
            Bone trs;
            if (theirBy.TryGetValue("TRS", out trs))
                for (string n = ParentName(theirs, trs); n != null && roots.Add(n); n = ParentName(theirs, theirBy[n])) { }
            var found = new List<Difference>();
            Func<string, bool, string> level = (name, rotation) =>
                !need[name].Overlaps(Gameplay) ? "EFFECTS" : rotation ? "DESYNC?" : "DESYNC";
            foreach (string name in need.Keys.Where(n => !roots.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
            {
                Bone a, b = theirBy[name];
                if (!myBy.TryGetValue(name, out a)) { found.Add(new Difference { Level = level(name, false), Bone = name, What = "missing", Size = "moved" }); continue; }
                if (name == "TRS" || b.Parent < 0)
                {
                    float[] wa = World(mine, mine.IndexOf(a)), wb = World(theirs, theirs.IndexOf(b));
                    double d = Enumerable.Range(0, 16).Max(k => Math.Abs((double)wa[k] - wb[k]));
                    if (d > 0) found.Add(new Difference { Level = level(name, false), Bone = name, What = "world rest pose " + Number(d), Size = Size(d) });
                    continue;
                }
                if (ParentName(mine, a) != ParentName(theirs, b))
                {
                    found.Add(new Difference { Level = level(name, false), Bone = name, What = "parent " + ParentName(mine, a) + ", stand-in " + ParentName(theirs, b), Size = "moved" });
                    continue;
                }
                HashSet<int> kinds;
                loose.TryGetValue(name, out kinds);
                if (kinds == null) continue;
                double[] scaleA = Scales(a.Local), scaleB = Scales(b.Local);
                double translation = Enumerable.Range(12, 3).Max(k => Math.Abs((double)a.Local[k] - b.Local[k]));
                double scale = Enumerable.Range(0, 3).Max(k => Math.Abs(scaleA[k] - scaleB[k]));
                double rotation = Enumerable.Range(0, 3).SelectMany(r => Enumerable.Range(0, 3).Select(c =>
                    Math.Abs(a.Local[r * 4 + c] / Math.Max(scaleA[r], 1e-9) - b.Local[r * 4 + c] / Math.Max(scaleB[r], 1e-9)))).Max();
                if (translation > 0 && kinds.Contains(0)) found.Add(new Difference { Level = level(name, false), Bone = name, What = "translation " + Number(translation), Size = Size(translation) });
                if (rotation > 0 && kinds.Contains(1)) found.Add(new Difference { Level = level(name, true), Bone = name, What = "rotation " + Number(rotation), Size = Size(rotation) });
                if (scale > 0 && kinds.Contains(2)) found.Add(new Difference { Level = level(name, false), Bone = name, What = "scale " + Number(scale), Size = Size(scale) });
            }
            // IK records that set a bone on a gameplay chain (the record's first bone; the jaw's and such only draw)
            var chain = new HashSet<string>(need.Where(n => n.Value.Overlaps(Gameplay)).Select(n => n.Key));
            Func<byte[], List<string>> ik = emo => IkRecords(emo).Where(r => chain.Contains(r.Split(':')[1].Split(',')[0])).ToList();
            if (!ik(custom).SequenceEqual(ik(standIn)))
                found.Add(new Difference { Level = "IK", Bone = "IK", What = "IK records of gameplay bones differ from the stand-in's", Size = "moved" });
            return found;
        }

        static double[] Scales(float[] m)
        {
            return Enumerable.Range(0, 3).Select(r => Math.Sqrt(Enumerable.Range(0, 3).Sum(c => (double)m[r * 4 + c] * m[r * 4 + c]))).ToArray();
        }

        // the skeleton's IK records (skeleton +0x20, count +4) with their bone numbers as names, the rest as bytes
        static List<string> IkRecords(byte[] emo)
        {
            var list = new List<string>();
            int s = I32(emo, 16);
            if (s == 0) return list;
            int count = U16(emo, s + 4), at = I32(emo, s + 0x20);
            if (at == 0) return list;
            var names = Skeleton(emo).Select(b => b.Name.ToUpperInvariant()).ToList();
            int o = s + at;
            for (int i = 0; i < count; i++)
            {
                int method = U16(emo, o), size = U16(emo, o + 2), n = method == 0 ? 5 : 3;
                var text = new StringBuilder(method + ":");
                for (int k = 0; k < n; k++) { int id = U16(emo, o + 6 + k * 2); text.Append(id < names.Count ? names[id] : id.ToString()).Append(','); }
                for (int k = 6 + n * 2; k < size; k++) text.Append(emo[o + k].ToString("x2"));
                list.Add(text.ToString());
                o += size;
            }
            return list;
        }

        // ---- checking ----

        /// <summary>A costume's files by name (folder or zip): the names listed, a file read only when asked for.</summary>
        public sealed class Source
        {
            public string Where;
            public Func<string, byte[]> Read;   // null when there's no such file
            public List<string> Names;
            public bool CustomOnly;   // the installed costumes: 01-07 are the game's own (or EX More Stuff's swaps of them), not checked
            public string Problem;    // a zip that couldn't be opened: why
        }

        public static Source FromFolder(string folder)
        {
            var names = Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
            return new Source { Where = folder, Names = names, Read = n => { string p = Path.Combine(folder, n); return File.Exists(p) ? File.ReadAllBytes(p) : null; } };
        }

        // the zip's file names (in any of its folders, the first of a name) now, each file unpacked when it's read
        public static Source FromZip(string zip)
        {
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var z = ZipFile.OpenRead(zip))
                foreach (var e in z.Entries.Where(e => e.Name != ""))
                    if (!entries.ContainsKey(e.Name)) entries[e.Name] = e.FullName;
            return new Source
            {
                Where = zip, Names = entries.Keys.ToList(),
                Read = n =>
                {
                    string full;
                    if (!entries.TryGetValue(n, out full)) return null;
                    using (var z = ZipFile.OpenRead(zip))
                    {
                        var e = z.GetEntry(full);
                        if (e == null) return null;
                        using (var s = e.Open())
                        using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
                    }
                }
            };
        }

        static Source ZipOrProblem(string zip)
        {
            try { return FromZip(zip); }
            catch (Exception ex) { return new Source { Where = zip, Names = new List<string>(), Read = n => null, Problem = ex.Message }; }
        }

        /// <summary>A folder or zip again (the results window's Fix reads the costume afresh).</summary>
        public static Source Open(string where)
        {
            return File.Exists(where) ? FromZip(where) : FromFolder(where);
        }

        /// <summary>Every costume model (CHR_NN.obj.emo) in a folder (and the folders in it), a zip, or for any other file the
        /// costume it belongs to in its own folder (its CHR_NN, else the folder's costumes; no folders below).</summary>
        public static List<Source> Find(string path)
        {
            var found = new List<Source>();
            if (File.Exists(path))
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) found.Add(ZipOrProblem(path));
                else
                {
                    var folder = FromFolder(Path.GetDirectoryName(Path.GetFullPath(path)));
                    Match m = Regex.Match(Path.GetFileName(path), @"^([A-Za-z]{3}_\d\d)[._]");
                    found.Add(m.Success && folder.Names.Contains(m.Groups[1].Value + ".obj.emo", StringComparer.OrdinalIgnoreCase) ? Only(folder, m.Groups[1].Value) : folder);
                }
            }
            else if (Directory.Exists(path))
            {
                foreach (string dir in new[] { path }.Concat(Directory.GetDirectories(path, "*", SearchOption.AllDirectories)))
                    if (Directory.GetFiles(dir, "*.obj.emo").Any(f => Model.IsMatch(Path.GetFileName(f)))) found.Add(FromFolder(dir));
                foreach (string zip in Directory.GetFiles(path, "*.zip", SearchOption.AllDirectories)) found.Add(ZipOrProblem(zip));
            }
            return found.Where(s => s.Problem != null || s.Names.Any(n => Model.IsMatch(n))).ToList();
        }

        /// <summary>The costumes as the game loads them (patch_ae2_tu3\battle\chara\&lt;CHR&gt;) and the docked ones (out of
        /// the game), the game's own slots 01-07 left out.</summary>
        public static List<Source> Installed(string game)
        {
            var found = new List<Source>();
            string chara = Path.Combine(Game.PatchFolder(game), "battle", "chara");
            if (Directory.Exists(chara)) found.AddRange(Find(chara));
            foreach (string zip in Docked.Load().Where(r => r.Item.IsCostume).Select(r => r.Package).Distinct(StringComparer.OrdinalIgnoreCase))
                if (File.Exists(zip)) found.AddRange(Find(zip));
            foreach (Source s in found) s.CustomOnly = true;
            return found;
        }

        /// <summary>The game's stock model of a costume (1-based), or null.</summary>
        static byte[] Stock(string game, string fighter, int costume)
        {
            string path = SkinImport.GameFile(game, fighter, fighter + "_" + costume.ToString("D2") + ".obj.emo");
            return path == null ? null : File.ReadAllBytes(path);
        }

        public static List<Result> Check(string game, Source source)
        {
            var results = new List<Result>();
            if (source.Problem != null)
            {
                var bad = new Result { Name = Path.GetFileName(source.Where), Where = source.Where, Verdict = Verdict.Faulty };
                bad.Findings.Add(new Finding { Kind = "UNREAD", Text = "the zip couldn't be read: " + source.Problem });
                results.Add(bad);
                return results;
            }
            var stock = new Dictionary<string, byte[][]>();   // fighter -> its costumes 1-3, while this folder or zip is checked
            foreach (string model in source.Names.Where(n => Model.IsMatch(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                Match m = Model.Match(model);
                if (source.CustomOnly && int.Parse(m.Groups[2].Value) < Catalog.FirstCustomCostume) continue;
                var r = new Result { Name = model.Substring(0, 6).ToUpperInvariant(), Where = source.Where };
                // one damaged costume is a finding; the others are still checked
                try { CheckOne(game, source, model, r, stock); }
                catch (Exception ex) { r.Findings.Add(new Finding { Kind = "UNREAD", Text = "it couldn't be read: " + ex.Message }); }
                if (r.Findings.Any(f => Faults.Contains(f.Kind))) r.Verdict = Verdict.Faulty;
                else if (r.Findings.Any(f => f.Kind == "WHITE") && r.Verdict < Verdict.Look) r.Verdict = Verdict.Look;
                results.Add(r);
            }
            return results;
        }

        static void CheckOne(string game, Source source, string model, Result r, Dictionary<string, byte[][]> stock)
        {
            Match m = Model.Match(model);
            string fighter = m.Groups[1].Value.ToUpperInvariant(), stem = model.Substring(0, 6);
            int slot = int.Parse(m.Groups[2].Value);
            Action<string, string, bool> add = (kind, text, fixable) => r.Findings.Add(new Finding { Kind = kind, Text = text, Fixable = fixable });
            // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
            if (slot < Catalog.FirstCustomCostume || slot > Catalog.LastSlot) add("SLOT", "costume " + slot.ToString("D2") + " is outside " + Catalog.FirstCustomCostume.ToString("D2") + ".." + Catalog.LastSlot + ": rollback games don't offer it", false);

            byte[] emo = Unwrap(source.Read(model));
            Shape shape = Structure(emo);
            if (shape.Broken != null) add("BROKEN", "its model is damaged (" + shape.Broken + ")", false);
            else
            {
                if (shape.Palette > PaletteLimit) add("PALETTE", shape.Palette + " bones in one skin palette (the game takes " + PaletteLimit + ")", false);
                if (shape.Submeshes > SubmeshLimit) add("SUBMESHES", shape.Submeshes + " pieces in one part (the game closed with 35; " + SubmeshLimit + " work)", false);

                // the locators, against each stand-in
                var verdicts = new Dictionary<int, List<Difference>>();
                var loose = Unkeyed(game, fighter);
                if (!stock.ContainsKey(fighter)) stock[fighter] = Enumerable.Range(1, 3).Select(c => Stock(game, fighter, c)).ToArray();
                for (int standIn = 1; standIn <= 3; standIn++)
                {
                    byte[] theirs = stock[fighter][standIn - 1];
                    if (theirs != null) verdicts[standIn] = LocatorDifferences(emo, theirs, loose);
                }
                if (verdicts.Count == 0) add("STOCK", "the game's " + fighter + "_01..03.obj.emo weren't found: the locators weren't checked", false);
                foreach (string level in new[] { "DESYNC", "DESYNC?", "IK", "EFFECTS" })
                {
                    var per = verdicts.ToDictionary(v => v.Key, v => v.Value.Where(d => d.Level == level).ToList());
                    if (!per.Values.Any(v => v.Count > 0)) continue;
                    int key = per.ContainsKey(1) && per[1].Count > 0 ? 1 : per.First(v => v.Value.Count > 0).Key;
                    var text = string.Join("; ", new[] { "moved", "small", "noise" }.Select(size => new { size, bones = per[key].Where(d => d.Size == size).ToList() })
                        .Where(x => x.bones.Count > 0).Select(x => x.size + " " + x.bones.Count + ": " + string.Join(", ", x.bones.Select(d => d.Bone + " " + d.What))));
                    string clean = string.Join(",", per.Where(v => v.Value.Count == 0).Select(v => v.Key.ToString("D2")));
                    add(level, "vs stand-in " + key.ToString("D2") + ": " + text + "; clean vs " + (clean == "" ? "none" : clean), (level == "DESYNC" || level == "DESYNC?") && key == 1);
                    r.Findings.Last().Bones = per[key].Count(d => d.Size != "noise");
                }
                List<Difference> first;
                var mine = verdicts.TryGetValue(1, out first) ? first.Where(d => d.Level != "EFFECTS").ToList() : new List<Difference>();
                bool mirror = verdicts.Where(v => v.Key != 1).Any(v => v.Value.Any(d => d.Level != "EFFECTS"));
                r.Verdict = mine.Any(d => d.Size != "noise") ? Verdict.Faulty : mine.Count > 0 ? Verdict.Noise : mirror ? Verdict.Mirror : Verdict.Ok;
            }

            // files and materials (the colors' textures only by name)
            var parts = new List<string> { ".obj.emo", ".shd.emo", ".nml.emb", ".bsr" };
            if (fighter == "BLR") parts.AddRange(new[] { ".clw.emo", ".msk.emo" });
            var lost = parts.Where(p => !source.Names.Contains(stem + p, StringComparer.OrdinalIgnoreCase)).ToList();
            if (lost.Count > 0) add("FILES", "missing " + string.Join(" ", lost.Select(p => stem + p)) + " (the game loads them for every costume)", true);
            int capacity = shape.Broken == null ? MaterialCapacity(emo) : 0;
            var used = shape.Broken == null ? UsedMaterials(emo) : new List<string>();
            var absent = new List<string>();
            var white = new Dictionary<string, List<string>>();   // lacking materials -> colors
            for (int color = 1; color <= 10; color++)
            {
                string name = stem + "_" + color.ToString("D2");
                if (!source.Names.Contains(name + ".obj.emm", StringComparer.OrdinalIgnoreCase) || !source.Names.Contains(name + ".col.emb", StringComparer.OrdinalIgnoreCase))
                {
                    absent.Add(color.ToString("D2"));
                    continue;
                }
                if (shape.Broken != null) continue;
                var have = Materials(Unwrap(source.Read(name + ".obj.emm")));
                if (have.Count > capacity) add("EMM>CAP", name + ".obj.emm has " + have.Count + " materials, the model takes " + capacity + ": the game doesn't use it (untextured)", false);
                var lacking = used.Where(u => !Has(have, u)).ToList();
                if (lacking.Count == 0) continue;
                string key = string.Join(", ", lacking);
                if (!white.ContainsKey(key)) white[key] = new List<string>();
                white[key].Add(color.ToString("D2"));
            }
            foreach (var w in white) add("WHITE", "colors " + string.Join(" ", w.Value) + ": the .obj.emm lacks " + w.Key + " (drawn white)", true);
            // ROLLBACK-NAME
            if (absent.Count > 0) add("COLORS", "missing colors " + string.Join(" ", absent) + ": rollback games only offer a costume with all ten colors", false);
        }

        // ---- fixing ----

        /// <summary>The costume's model with the stand-in's (costume 1) rest pose for every bone on a locator chain (named the
        /// same in both), its inverse bind matrices rebuilt and every skinned vertex moved with its bones, so the mesh sits on
        /// them as before. Returns how many bones changed.</summary>
        public static int Align(byte[] emo, byte[] standIn)
        {
            List<Bone> mine = Skeleton(emo), theirs = Skeleton(standIn);
            var theirBy = ByName(theirs);
            var need = Chains(theirs);
            int s = I32(emo, 16), nodes = s + I32(emo, s + 8), inverseAt = I32(emo, s + 0x1c), inverse = s + inverseAt;
            var oldWorld = Enumerable.Range(0, mine.Count).Select(i => World(mine, i)).ToList();
            int changed = 0;
            foreach (Bone b in mine)
            {
                Bone t;
                if (!need.ContainsKey(b.Name.ToUpperInvariant()) || !theirBy.TryGetValue(b.Name.ToUpperInvariant(), out t)) continue;
                if (!b.Local.SequenceEqual(t.Local)) { b.Local = (float[])t.Local.Clone(); changed++; }
            }
            if (changed == 0) return 0;
            var newWorld = Enumerable.Range(0, mine.Count).Select(i => World(mine, i)).ToList();
            var change = Enumerable.Range(0, mine.Count).Select(i => Multiply(Inverse(oldWorld[i]), newWorld[i])).ToList();
            for (int i = 0; i < mine.Count; i++)
            {
                for (int k = 0; k < 16; k++) Buffer.BlockCopy(BitConverter.GetBytes(mine[i].Local[k]), 0, emo, nodes + i * 80 + 16 + k * 4, 4);
                if (inverseAt == 0) continue;   // no bind matrices: never written over the skeleton's header
                float[] inv = Inverse(newWorld[i]);
                for (int k = 0; k < 16; k++) Buffer.BlockCopy(BitConverter.GetBytes(inv[k]), 0, emo, inverse + i * 64 + k * 4, 4);
            }
            // vertices: position, normal at +12; skinned (size >= 64): 4 bone numbers at +48 into the submesh's node list,
            // 3 weights at +52 (the 4th is 1 - their sum)
            foreach (int model in Models(emo))
            {
                int vCount = U16(emo, model + 0x10), vSize = U16(emo, model + 0x12), vOff = I32(emo, model + 0x14);
                if (vSize < 64) continue;
                var done = new HashSet<int>();
                foreach (int sub in Submeshes(emo, model))
                {
                    int idxCount = U16(emo, sub + 2), nodeCount = U16(emo, sub + 4);
                    var nodeList = Enumerable.Range(0, nodeCount).Select(k => U16(emo, sub + 38 + idxCount * 2 + k * 2)).ToArray();
                    for (int k = 0; k < idxCount; k++)
                    {
                        int v = U16(emo, sub + 38 + k * 2);
                        if (v >= vCount || !done.Add(v)) continue;
                        int o = model + vOff + v * vSize;
                        float[] w = { BitConverter.ToSingle(emo, o + 52), BitConverter.ToSingle(emo, o + 56), BitConverter.ToSingle(emo, o + 60), 0 };
                        w[3] = 1 - w[0] - w[1] - w[2];
                        var mat = new double[16];
                        bool any = false;
                        for (int j = 0; j < 4; j++)
                        {
                            int id = emo[o + 48 + j];
                            if (w[j] == 0 || id >= nodeList.Length || nodeList[id] >= change.Count) continue;
                            for (int q = 0; q < 16; q++) mat[q] += w[j] * change[nodeList[id]][q];
                            any = true;
                        }
                        if (!any) continue;   // none of its bones known: it stays where it was (not at the origin)
                        Transform(emo, o, mat, true);
                        Transform(emo, o + 12, mat, false);
                    }
                }
            }
            return changed;
        }

        static void Transform(byte[] d, int o, double[] m, bool point)
        {
            double x = BitConverter.ToSingle(d, o), y = BitConverter.ToSingle(d, o + 4), z = BitConverter.ToSingle(d, o + 8);
            var r = new double[3];
            for (int c = 0; c < 3; c++) r[c] = x * m[c] + y * m[4 + c] + z * m[8 + c] + (point ? m[12 + c] : 0);
            if (!point) { double len = Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]); if (len > 0) for (int c = 0; c < 3; c++) r[c] /= len; }
            for (int c = 0; c < 3; c++) Buffer.BlockCopy(BitConverter.GetBytes((float)r[c]), 0, d, o + c * 4, 4);
        }

        static float[] Inverse(float[] m)
        {
            var a = new double[4, 8];
            for (int r = 0; r < 4; r++) { for (int c = 0; c < 4; c++) a[r, c] = m[r * 4 + c]; a[r, 4 + r] = 1; }
            for (int c = 0; c < 4; c++)
            {
                int p = c;
                for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                for (int k = 0; k < 8; k++) { double t = a[c, k]; a[c, k] = a[p, k]; a[p, k] = t; }
                double div = a[c, c];
                for (int k = 0; k < 8; k++) a[c, k] /= div;
                for (int r = 0; r < 4; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c];
                    for (int k = 0; k < 8; k++) a[r, k] -= f * a[c, k];
                }
            }
            var inv = new float[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) inv[r * 4 + c] = (float)a[r, 4 + c];
            return inv;
        }

        /// <summary>The missing materials put right, as usf4-nesica's fix_costume.py does: when one of the game's costumes
        /// of the fighter has every material the model uses, its .obj.emm per color replaces the costume's (the model was
        /// built on it, Juri JRI_38); otherwise the model's missing names are renamed to the closest material every color has
        /// (same name in other case, else the most alike, else the one the model uses most; Sakura SKR_34). Never adds a
        /// material (the color's .obj.emm must stay within the model's capacity). Works on copies: `files` gets the changed
        /// files, the bytes it held stay as they were. Returns what it did, or null.</summary>
        public static string FixMaterials(string game, string fighter, string stem, Dictionary<string, byte[]> files)
        {
            string modelName = stem + ".obj.emo";
            byte[] emo = files[modelName];
            var used = UsedMaterials(emo);
            var colors = Enumerable.Range(1, 10).Select(c => stem + "_" + c.ToString("D2") + ".obj.emm").Where(files.ContainsKey).ToList();
            if (colors.Count == 0) return null;
            var have = colors.ToDictionary(c => c, c => Materials(files[c]));
            if (!have.Values.Any(h => used.Any(u => !Has(h, u)))) return null;
            // a color whose .emm has a material the model doesn't use, named like the one it lacks: rename it in that .emm
            var renamed = new List<string>();
            foreach (string c in colors)
            {
                byte[] emm = null;   // copied on its first change
                foreach (string missing in used.Where(u => !Has(have[c], u)).ToList())
                {
                    var spare = Enumerable.Range(0, have[c].Count).Where(i => !Has(used, have[c][i]))
                        .Select(i => new { i, r = Alike(have[c][i].ToLowerInvariant(), missing.ToLowerInvariant()) })
                        .Where(x => x.r >= 0.75).OrderByDescending(x => x.r).FirstOrDefault();   // eyes/eye, not ring/finger
                    if (spare == null) continue;
                    if (emm == null) emm = (byte[])files[c].Clone();
                    int t = I32(emm, 0xc);
                    byte[] bytes = new byte[32];
                    Encoding.GetEncoding(28591).GetBytes(missing, 0, Math.Min(missing.Length, 31), bytes, 0);
                    Buffer.BlockCopy(bytes, 0, emm, t + I32(emm, t + 4 + spare.i * 4), 32);
                    renamed.Add(c.Substring(0, c.Length - 8) + ": " + have[c][spare.i] + " -> " + missing);
                    have[c][spare.i] = missing;
                }
                if (emm != null) files[c] = emm;
            }
            string inEmm = renamed.Count > 0 ? "materials renamed in the colors: " + string.Join(", ", renamed) : null;
            if (!have.Values.Any(h => used.Any(u => !Has(h, u)))) return inEmm;
            int capacity = MaterialCapacity(emo);
            for (int costume = 1; costume <= 7; costume++)
            {
                string first = SkinImport.GameFile(game, fighter, fighter + "_" + costume.ToString("D2") + "_01.obj.emm");
                if (first == null) continue;
                var stock = Materials(File.ReadAllBytes(first));
                if (used.Any(u => !Has(stock, u)) || stock.Count > capacity) continue;
                var broken = colors.Where(c => used.Any(u => !Has(have[c], u))).ToList();
                foreach (string c in broken)
                {
                    string game_ = SkinImport.GameFile(game, fighter, fighter + "_" + costume.ToString("D2") + c.Substring(6));
                    files[c] = File.ReadAllBytes(game_ ?? first);
                }
                return (inEmm != null ? inEmm + "; " : "") + "colors " + string.Join(" ", broken.Select(c => c.Substring(c.Length - 10, 2))) +
                    ": materials from the game's costume " + costume.ToString("D2") + " (it has every material the model uses)";
            }
            var common = have.Values.Aggregate((a, b) => a.Intersect(b, StringComparer.OrdinalIgnoreCase).ToList());
            if (common.Count == 0) return null;
            var counts = Models(emo).SelectMany(m => Submeshes(emo, m)).Select(sm => CString(emo, sm + 6)).Where(n => Has(common, n))
                .GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            var map = new Dictionary<string, string>();
            foreach (string missing in used.Where(u => !Has(common, u)))
            {
                string close = common.Select(n => new { n, r = Alike(n, missing) }).Where(x => x.r >= 0.5).OrderByDescending(x => x.r).Select(x => x.n).FirstOrDefault();
                map[missing] = close ?? (counts.Count > 0 ? counts[0] : common[0]);
            }
            emo = (byte[])emo.Clone();
            foreach (int sm in Models(emo).SelectMany(m => Submeshes(emo, m)).ToList())
            {
                string name = CString(emo, sm + 6), to;
                if (!map.TryGetValue(name, out to)) continue;
                byte[] bytes = new byte[32];
                Encoding.GetEncoding(28591).GetBytes(to, 0, Math.Min(to.Length, 31), bytes, 0);
                Buffer.BlockCopy(bytes, 0, emo, sm + 6, 32);
            }
            files[modelName] = emo;
            return (inEmm != null ? inEmm + "; " : "") + "materials renamed in the model: " + string.Join(", ", map.Select(p => p.Key + " -> " + p.Value));
        }

        // how alike two names are, 0..1 (like Python's difflib ratio, on the longest common subsequence)
        static double Alike(string a, string b)
        {
            var l = new int[a.Length + 1, b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    l[i, j] = a[i - 1] == b[j - 1] ? l[i - 1, j - 1] + 1 : Math.Max(l[i - 1, j], l[i, j - 1]);
            return a.Length + b.Length == 0 ? 1 : 2.0 * l[a.Length, b.Length] / (a.Length + b.Length);
        }

        /// <summary>One costume's files (CHR_NN.* and CHR_NN_*) of a folder or zip that holds more (the game's chara folder),
        /// with the mod's marker files (slot_CHR_NN.txt, code_, needs_exms_, override_).</summary>
        public static Source Only(Source source, string stem)
        {
            Func<string, bool> mine = n => n.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) || n.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase) ||
                (Marker.IsMatch(n) && (!n.StartsWith("slot_", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "slot_" + stem + ".txt", StringComparison.OrdinalIgnoreCase)));
            return new Source { Where = source.Where, Read = source.Read, Names = source.Names.Where(mine).ToList(), CustomOnly = source.CustomOnly, Problem = source.Problem };
        }

        // a path as compared: full, no trailing separator
        static string Full(string path)
        {
            string full = Path.GetFullPath(path);
            return full.Length > 3 ? full.TrimEnd('\\', '/') : full;   // a drive's root keeps its separator
        }

        /// <summary>Whether a path is the game's folder or inside it.</summary>
        public static bool InGame(string game, string path)
        {
            string g = Full(game), p = Full(path);
            return string.Equals(p, g, StringComparison.OrdinalIgnoreCase) || p.StartsWith(g + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Where a fixed copy goes when nobody says: "&lt;name&gt; - fixed" beside it (a folder, or a zip for a zip).</summary>
        public static string DefaultTarget(string where)
        {
            where = Full(where);
            return where.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? where.Substring(0, where.Length - 4) + " - fixed.zip" : where + " - fixed";
        }

        /// <summary>Why a fixed copy can't go to `target`, in a player's words, or null: never over the costume itself (or a
        /// file of its folder), never into the game's folder.</summary>
        public static string TargetProblem(string game, Source source, string target)
        {
            string t = Full(target), s = Full(source.Where);
            if (string.Equals(t, s, StringComparison.OrdinalIgnoreCase) ||
                (Directory.Exists(s) && string.Equals(Path.GetDirectoryName(t), s, StringComparison.OrdinalIgnoreCase) && source.Names.Contains(Path.GetFileName(t), StringComparer.OrdinalIgnoreCase)))
                return "that's the costume itself, and it stays as it is. Pick another place.";
            if (game != null && InGame(game, t)) return "that's inside the game's folder. Pick a place outside it; the copy goes into the game with Advanced > Add Mod From File.";
            return null;
        }

        /// <summary>A fixed copy of the costumes in a folder or zip, next to it ("&lt;name&gt; - fixed", a folder or a zip) or
        /// at `target` (a zip when it ends .zip): locators aligned to the stand-in, materials put right, missing parts
        /// (.shd.emo, .nml.emb, .bsr) from the game's costume 1. The original stays as it is; a target that's the original
        /// or in the game's folder is refused. Returns the copy's path and what was done.</summary>
        public static string Fix(string game, Source source, List<string> done, string target = null)
        {
            if (source.Problem != null) throw new InvalidOperationException("the zip couldn't be read: " + source.Problem);
            target = Full(target ?? DefaultTarget(source.Where));
            string why = TargetProblem(game, source, target);
            if (why != null) throw new InvalidOperationException("the fixed copy can't go to " + target + ": " + why);
            var files = source.Names.ToDictionary(n => n, n => source.Read(n), StringComparer.OrdinalIgnoreCase);
            // models and materials as the game reads them (a packed one is written plain)
            foreach (string n in files.Keys.Where(n => n.EndsWith(".emo", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".emm", StringComparison.OrdinalIgnoreCase)).ToList())
                files[n] = Unwrap(files[n]);
            foreach (string model in source.Names.Where(n => Model.IsMatch(n)).ToList())
            {
                string fighter = model.Substring(0, 3).ToUpperInvariant(), stem = model.Substring(0, 6);
                byte[] standIn = Stock(game, fighter, 1);
                if (standIn == null) { done.Add(stem + ": the game's " + fighter + "_01.obj.emo wasn't found, not aligned"); continue; }
                foreach (string part in new[] { ".obj.emo", ".shd.emo" })
                {
                    byte[] data;
                    if (!files.TryGetValue(stem + part, out data)) continue;
                    data = (byte[])data.Clone();
                    string stockFile = part == ".obj.emo" ? null : SkinImport.GameFile(game, fighter, fighter + "_01" + part);
                    if (part != ".obj.emo" && stockFile == null) continue;
                    byte[] stockPart = part == ".obj.emo" ? standIn : File.ReadAllBytes(stockFile);
                    int bones = Align(data, stockPart);
                    if (bones > 0) { files[stem + part] = data; done.Add(stem + part + ": " + bones + " locator-chain bones set to the stand-in's (costume 01), the mesh moved with them"); }
                }
                string materials = FixMaterials(game, fighter, stem, files);
                if (materials != null) done.Add(stem + ": " + materials);
                foreach (string part in fighter == "BLR" ? new[] { ".shd.emo", ".nml.emb", ".bsr", ".clw.emo", ".msk.emo" } : new[] { ".shd.emo", ".nml.emb", ".bsr" })
                {
                    if (files.ContainsKey(stem + part)) continue;
                    string from = SkinImport.GameFile(game, fighter, fighter + "_01" + part);
                    if (from == null) continue;
                    files[stem + part] = File.ReadAllBytes(from);
                    done.Add(stem + part + ": missing, the game's costume 01's put in");
                }
            }
            bool zip = target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (zip)
            {
                if (File.Exists(target)) File.Delete(target);
                using (var z = ZipFile.Open(target, ZipArchiveMode.Create))
                    foreach (var f in files)
                        using (var s = z.CreateEntry(f.Key).Open()) s.Write(f.Value, 0, f.Value.Length);
            }
            else
            {
                Directory.CreateDirectory(target);
                foreach (var f in files) File.WriteAllBytes(Path.Combine(target, f.Key), f.Value);
            }
            return target;
        }

        // ---- the report ----

        public static string Report(string game, List<Result> results)
        {
            var t = new StringBuilder();
            t.AppendLine("EX More Stuff costume check, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            t.AppendLine("Game: " + game);
            t.AppendLine(results.Count + " costumes: " + string.Join(", ", Enum.GetValues(typeof(Verdict)).Cast<Verdict>().Reverse()
                .Select(v => results.Count(r => r.Verdict == v) + " " + v.ToString().ToUpperInvariant())));
            t.AppendLine();
            foreach (var r in results.OrderByDescending(r => r.Verdict).ThenBy(r => r.Name))
            {
                t.AppendLine(r.Name + "  " + r.Verdict.ToString().ToUpperInvariant() + "  " + r.Where);
                foreach (var f in r.Findings) t.AppendLine("    " + f.Kind.PadRight(8) + " " + f.Text + (f.Fixable ? "  [fixable]" : ""));
            }
            t.AppendLine();
            t.AppendLine("Verdicts: FAULTY breaks in the game or desyncs online against a player without it; LOOK plays the same online,");
            t.AppendLine("but parts of some colors draw white; NOISE differs from the stand-in only at rounding level (plays the same, but");
            t.AppendLine("not bit for bit); MIRROR is clean against costume 01 and differs from 02/03, the stand-ins in a mirror match; OK.");
            t.AppendLine("DESYNC: a gameplay locator bone (hurtboxes, WAIST, LND, TRS) or a bone above it differs from the stand-in where");
            t.AppendLine("the animations leave it to the model. DESYNC?: a rotation IK may override. IK: the IK records of gameplay bones");
            t.AppendLine("differ (not fixed automatically). EFFECTS: only");
            t.AppendLine("effect locators differ (effects sit elsewhere on that PC; not a fault). Sizes: moved >= 0.001, small >= 0.0001,");
            t.AppendLine("noise below. WHITE: a material the model uses is missing from a color (several long-loved skins have it). EMM>CAP:");
            t.AppendLine("a color has more materials than the model takes. FILES / COLORS / SLOT: files or colors the game or rollback games");   // ROLLBACK-NAME
            t.AppendLine("need are missing, or the slot isn't 08-99. BROKEN: a part of the model points past the end of its file. PALETTE:");
            t.AppendLine("more than 24 bones in one skin palette. SUBMESHES: more than 31 in one part. BROKEN, PALETTE and SUBMESHES close");
            t.AppendLine("the game. UNREAD: the files couldn't be read, so the costume wasn't checked.");
            return t.ToString();
        }
    }
}
