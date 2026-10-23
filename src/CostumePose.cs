using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    // A costume model posed in the fighting stance, as the game would show it a moment into a round, for pictures drawn
    // without the game (CostumeRender). A port of the Python renderer the SF4 Ember gallery's pictures come from
    // (usf4-nesica tools: usf4_pose.py, render_usf4_costumes.py, render_usf4_stylized.py), which follows the game:
    // - the .obj.ema animation's frame on the animation skeleton, its IK (two-bone legs, blended helper bones), after
    //   github.com/Barnacle/USF4-Character-Extractor (D3D/Ema.h);
    // - bones the animation doesn't move keep the costume's own placement relative to the nearest bone it does;
    // - the stance mirrored (a P2 picture, as Ember's gallery shows them): vertices mirrored, moved by the bone of the
    //   other side and mirrored back, so textures stay readable;
    // - cloth: the game's secondary motion (<CHR>_<NN>.bsr; SSFIV.exe 0x669620..0x673e60) run from the stance until it
    //   settles; accessories the animation doesn't place, far from the body, are hidden (the game's physics puts them).
    // Matrices are 4x4 row-major in the game's row-vector convention (v' = v * M), as double[16].
    static class CostumePose
    {
        public class Sub { public int Texture; public int[] Triangles; public string Material; }

        public class Part
        {
            public double[] Positions, Normals;   // 3 per vertex
            public float[] Uvs;                   // 2 per vertex
            public List<Sub> Subs = new List<Sub>();
            internal byte[] Bones;                // 4 per vertex, into the submesh's node list
            internal double[] Weights;            // 3 per vertex, the 4th is the rest
            internal List<int[]> Nodes = new List<int[]>();   // per submesh: skeleton node indices
        }

        // GIM parts that are what a character wears in a fight (most are intro or win props)
        static readonly Dictionary<string, string[]> KeepGim = new Dictionary<string, string[]>
        {
            { "YAN", new[] { "GIM4" } }, { "ROS", new[] { "GIM4" } }, { "BLR", new[] { "GIM4", "GIM5" } },
        };
        const double LooseDistance = 0.15, ClothLooseDistance = 0.4;
        const int BsrSteps = 240;
        const double BsrDt = 1.0 / 60.0;

        // where the game keeps each fighter's files (the animation, the stock costumes), in the order the renderer looks
        static readonly string[] CharaDirs = { "resource", @"dlc\02_costume", @"dlc\03_character", @"dlc\04_ae2", @"dlc\04_costume", @"dlc\03_character_free" };

        internal static string GameFile(string game, string fighter, string name)
        {
            foreach (string dir in CharaDirs)
            {
                string path = System.IO.Path.Combine(game, dir, "battle", "chara", fighter, name);
                if (System.IO.File.Exists(path)) return path;
            }
            return null;
        }

        // ---------- math ----------

        static double[] Identity() { return new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }; }

        internal static double[] Mul(double[] a, double[] b)
        {
            var m = new double[16];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    m[r * 4 + c] = a[r * 4] * b[c] + a[r * 4 + 1] * b[4 + c] + a[r * 4 + 2] * b[8 + c] + a[r * 4 + 3] * b[12 + c];
            return m;
        }

        internal static double[] Inverse(double[] m)
        {
            var a = new double[4, 8];
            for (int r = 0; r < 4; r++) { for (int c = 0; c < 4; c++) a[r, c] = m[r * 4 + c]; a[r, 4 + r] = 1; }
            for (int c = 0; c < 4; c++)
            {
                int p = c;
                for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                for (int k = 0; k < 8; k++) { double t = a[c, k]; a[c, k] = a[p, k]; a[p, k] = t; }
                double d = a[c, c];
                for (int k = 0; k < 8; k++) a[c, k] /= d;
                for (int r = 0; r < 4; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c];
                    if (f != 0) for (int k = 0; k < 8; k++) a[r, k] -= f * a[c, k];
                }
            }
            var o = new double[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) o[r * 4 + c] = a[r, 4 + c];
            return o;
        }

        // a point (row vector, w = 1) through a matrix
        static double[] Point(double x, double y, double z, double[] m)
        {
            return new[] { x * m[0] + y * m[4] + z * m[8] + m[12], x * m[1] + y * m[5] + z * m[9] + m[13], x * m[2] + y * m[6] + z * m[10] + m[14] };
        }

        static double[] Translation(double[] m) { return new[] { m[12], m[13], m[14] }; }
        static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        static double Length(double[] a) { return Math.Sqrt(Dot(a, a)); }
        static double[] Sub3(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        static double[] Add3(double[] a, double[] b) { return new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] }; }
        static double[] Scale3(double[] a, double s) { return new[] { a[0] * s, a[1] * s, a[2] * s }; }
        static double[] Cross(double[] a, double[] b) { return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] }; }
        static double[] Normalize(double[] v) { double n = Length(v); return n != 0 ? Scale3(v, 1 / n) : v; }

        static double[] QMatrix(double[] q)
        {
            double x = q[0], y = q[1], z = q[2], w = q[3];
            return new double[]
            {
                1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
                0, 0, 0, 1,
            };
        }

        // the inverse of QMatrix for a rotation whose rows may be scaled
        static double[] QFromMatrix(double[] m)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
            {
                double n = Math.Sqrt(m[i * 4] * m[i * 4] + m[i * 4 + 1] * m[i * 4 + 1] + m[i * 4 + 2] * m[i * 4 + 2]);
                for (int j = 0; j < 3; j++) r[i * 3 + j] = m[i * 4 + j] / n;
            }
            Func<int, int, double> c = (i, j) => r[j * 3 + i];   // column-vector form
            double t = c(0, 0) + c(1, 1) + c(2, 2), s, w, x, y, z;
            if (t > 0)
            {
                s = Math.Sqrt(t + 1) * 2;
                w = 0.25 * s; x = (c(2, 1) - c(1, 2)) / s; y = (c(0, 2) - c(2, 0)) / s; z = (c(1, 0) - c(0, 1)) / s;
            }
            else if (c(0, 0) > c(1, 1) && c(0, 0) > c(2, 2))
            {
                s = Math.Sqrt(1 + c(0, 0) - c(1, 1) - c(2, 2)) * 2;
                w = (c(2, 1) - c(1, 2)) / s; x = 0.25 * s; y = (c(0, 1) + c(1, 0)) / s; z = (c(0, 2) + c(2, 0)) / s;
            }
            else if (c(1, 1) > c(2, 2))
            {
                s = Math.Sqrt(1 + c(1, 1) - c(0, 0) - c(2, 2)) * 2;
                w = (c(0, 2) - c(2, 0)) / s; x = (c(0, 1) + c(1, 0)) / s; y = 0.25 * s; z = (c(1, 2) + c(2, 1)) / s;
            }
            else
            {
                s = Math.Sqrt(1 + c(2, 2) - c(0, 0) - c(1, 1)) * 2;
                w = (c(1, 0) - c(0, 1)) / s; x = (c(0, 2) + c(2, 0)) / s; y = (c(1, 2) + c(2, 1)) / s; z = 0.25 * s;
            }
            return new[] { x, y, z, w };
        }

        // D3DXQuaternionMultiply(q1, q2): q1, then q2
        static double[] QMul(double[] q1, double[] q2)
        {
            double x1 = q2[0], y1 = q2[1], z1 = q2[2], w1 = q2[3], x2 = q1[0], y2 = q1[1], z2 = q1[2], w2 = q1[3];
            return new[] { w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2, w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
                           w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2, w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2 };
        }

        // Ema.h's eulerToQuaternionXYZ
        static double[] QEulerXyz(double pitch, double yaw, double roll)
        {
            double sp = Math.Sin(pitch / 2), cp = Math.Cos(pitch / 2), sy = Math.Sin(yaw / 2), cy = Math.Cos(yaw / 2), sr = Math.Sin(roll / 2), cr = Math.Cos(roll / 2);
            return new[] { sr * cp * cy - cr * sp * sy, cr * sp * cy + sr * cp * sy, cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy };
        }

        static double[] Compose(double[] q, double[] scale, double[] t)
        {
            double[] m = QMatrix(q);
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i * 4 + j] *= scale[i];
            m[12] = t[0]; m[13] = t[1]; m[14] = t[2];
            return m;
        }

        static void Decompose(double[] m, out double[] scale, out double[] q, out double[] t)
        {
            scale = new double[3];
            for (int i = 0; i < 3; i++) scale[i] = Math.Sqrt(m[i * 4] * m[i * 4] + m[i * 4 + 1] * m[i * 4 + 1] + m[i * 4 + 2] * m[i * 4 + 2]);
            q = QFromMatrix(m);
            t = Translation(m);
        }

        static double[] TransformCoord(double[] v, double[] m)
        {
            double w = v[0] * m[3] + v[1] * m[7] + v[2] * m[11] + m[15];
            double[] p = Point(v[0], v[1], v[2], m);
            return new[] { p[0] / w, p[1] / w, p[2] / w };
        }

        // Ema.h's sub_504330: a rotation about an axis from (sin, cos)
        static double[] AxisMatrix(double[] axis, double s, double c)
        {
            double x = axis[0], y = axis[1], z = axis[2], k = 1 - c;
            return new double[]
            {
                k * x * x + c, k * x * y + s * z, k * x * z - s * y, 0,
                k * x * y - s * z, k * y * y + c, k * y * z + s * x, 0,
                k * x * z + s * y, k * y * z - s * x, k * z * z + c, 0,
                0, 0, 0, 1,
            };
        }

        // the row-vector 3x3 rotation (in a 4x4) taking direction a to direction b
        static double[] RotationBetween(double[] a, double[] b)
        {
            a = Scale3(a, 1 / Length(a)); b = Scale3(b, 1 / Length(b));
            double[] v = Cross(a, b);
            double c = Dot(a, b);
            var m = Identity();
            if (Length(v) < 1e-8)
            {
                if (c > 0) return m;
                double[] u = Sub3(new[] { 1.0, 0, 0 }, Scale3(a, a[0]));
                if (Length(u) < 1e-6) u = Sub3(new[] { 0, 1.0, 0 }, Scale3(a, a[1]));
                u = Scale3(u, 1 / Length(u));
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i * 4 + j] = 2 * u[i] * u[j] - (i == j ? 1 : 0);
                return m;
            }
            var k = new[,] { { 0, -v[2], v[1] }, { v[2], 0, -v[0] }, { -v[1], v[0], 0 } };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double kk = 0;
                    for (int l = 0; l < 3; l++) kk += k[i, l] * k[l, j];
                    m[j * 4 + i] = (i == j ? 1 : 0) + k[i, j] + kk / (1 + c);   // transposed: row-vector form
                }
            return m;
        }

        static double[] Translate(double[] t) { var m = Identity(); m[12] = t[0]; m[13] = t[1]; m[14] = t[2]; return m; }

        // ---------- files ----------

        static int I32(byte[] d, int o) { return BitConverter.ToInt32(d, o); }
        static int U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }
        static float F32(byte[] d, int o) { return BitConverter.ToSingle(d, o); }
        static string CString(byte[] d, int o, int max = int.MaxValue)
        {
            int end = o;
            while (end < d.Length && end - o < max && d[end] != 0) end++;
            return Encoding.GetEncoding(28591).GetString(d, o, end - o);
        }

        static double[] Matrix(byte[] d, int o)
        {
            var m = new double[16];
            for (int i = 0; i < 16; i++) m[i] = F32(d, o + i * 4);
            return m;
        }

        static List<string> Names(byte[] d, int b, int count, int listAt)
        {
            var names = new List<string>();
            for (int i = 0; i < count; i++) names.Add(CString(d, b + I32(d, b + listAt + i * 4)));
            return names;
        }

        // the model's own skeleton: names, parent indices (-1 for none), local matrices
        class EmoSkeleton
        {
            public List<string> Names = new List<string>();
            public int[] Parents = new int[0];
            public List<double[]> Local = new List<double[]>();
            public Dictionary<string, double[]> Bind = new Dictionary<string, double[]>();   // name -> global rest matrix

            public EmoSkeleton(byte[] d)
            {
                int b = I32(d, 16);
                if (b == 0) return;
                int count = U16(d, b), start = I32(d, b + 8);
                Names = CostumePose.Names(d, b, count, I32(d, b + 12));
                Parents = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int p = U16(d, b + start + i * 80);
                    Parents[i] = p < count ? p : -1;
                    Local.Add(Matrix(d, b + start + i * 80 + 16));
                }
                // a damaged model's bone can be its own ancestor (or its own parent): every walk up the skeleton would
                // never end, so it's refused here
                var state = new byte[count];   // 0 not seen, 1 on the walk now, 2 reaches the top
                var walk = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    walk.Clear();
                    int j = i;
                    while (j >= 0 && state[j] == 0) { state[j] = 1; walk.Add(j); j = Parents[j]; }
                    if (j >= 0 && state[j] == 1) throw new Damaged("its skeleton loops (bone " + Names[j] + " is its own ancestor)");
                    foreach (int k in walk) state[k] = 2;
                }
                // global matrices, ancestors first (a walk, not a recursion: a long chain mustn't run out of stack)
                var global = new double[count][];
                for (int i = 0; i < count; i++)
                {
                    walk.Clear();
                    for (int j = i; j >= 0 && global[j] == null; j = Parents[j]) walk.Add(j);
                    for (int w = walk.Count - 1; w >= 0; w--)
                    {
                        int k = walk[w];
                        global[k] = Parents[k] < 0 ? Local[k] : Mul(Local[k], global[Parents[k]]);
                    }
                    Bind[Names[i]] = global[i];
                }
            }
        }

        /// <summary>A costume's model that can't be posed: its skeleton is damaged (the reason in its message).</summary>
        public class Damaged : Exception
        {
            public Damaged(string message) : base(message) { }
        }

        // ---------- the animation skeleton (.obj.ema) ----------

        class Node
        {
            public int Number, Parent, Child1, Sibling, Flags;
            public double UnknownScale;
            public double[] Matrix, Scale, Q, T;
            public uint UpdateFlags;
            public double[] AMatrix = Identity(), AT = new double[3], AScale = { 1, 1, 1 }, AQ = { 0, 0, 0, 1 };
        }

        class Command { public bool Absolute; public int[] Frames; public int[] Index, Tangent; }

        class Animation
        {
            public float[] Values;
            public Dictionary<int, Dictionary<int, Dictionary<int, Command>>> Commands = new Dictionary<int, Dictionary<int, Dictionary<int, Command>>>();
        }

        class Ema
        {
            readonly byte[] data;
            public readonly int[] AnimationOffsets;
            public readonly List<string> Names;
            readonly List<Tuple<int, int, int, int, int, double, double[]>> raw = new List<Tuple<int, int, int, int, int, double, double[]>>();
            public readonly List<Tuple<int, byte[]>> Ik = new List<Tuple<int, byte[]>>();

            public Ema(byte[] d)
            {
                data = d;
                int header = U16(d, 6), skeleton = I32(d, 12), count = U16(d, 16);
                AnimationOffsets = Enumerable.Range(0, count).Select(i => I32(d, header + i * 4)).ToArray();
                int nodeCount = U16(d, skeleton), ikCount = U16(d, skeleton + 4), start = I32(d, skeleton + 8), namesAt = I32(d, skeleton + 12), ikAt = I32(d, skeleton + 0x20);
                Names = CostumePose.Names(d, skeleton, nodeCount, namesAt);
                for (int i = 0; i < nodeCount; i++)
                {
                    int o = skeleton + start + i * 80;
                    raw.Add(Tuple.Create(i, U16(d, o), U16(d, o + 2), U16(d, o + 4), U16(d, o + 10), (double)F32(d, o + 12), Matrix(d, o + 16)));
                }
                int at = skeleton + ikAt;
                for (int i = 0; i < (ikAt != 0 ? ikCount : 0); i++)
                {
                    int method = U16(d, at), size = U16(d, at + 2);
                    var block = new byte[0x20];
                    Buffer.BlockCopy(d, at + 4, block, 0, Math.Max(0, Math.Min(size - 4, 0x20)));
                    Ik.Add(Tuple.Create(method, block));
                    at += size;
                }
            }

            public Animation Read(int index)
            {
                int o = AnimationOffsets[index];
                int commands = U16(data, o + 2), valueCount = I32(data, o + 4), valuesAt = I32(data, o + 16);
                var a = new Animation { Values = new float[valueCount] };
                for (int i = 0; i < valueCount; i++) a.Values[i] = F32(data, o + valuesAt + i * 4);
                for (int c = 0; c < commands; c++)
                {
                    int co = o + I32(data, o + 20 + c * 4);
                    int bone = U16(data, co), kind = data[co + 2], flags = data[co + 3], steps = U16(data, co + 4), idxOff = U16(data, co + 6);
                    var cmd = new Command { Absolute = (flags & 0x10) != 0, Frames = new int[steps], Index = new int[steps], Tangent = new int[steps] };
                    for (int s = 0; s < steps; s++)
                    {
                        cmd.Frames[s] = (flags & 0x20) != 0 ? U16(data, co + 8 + s * 2) : data[co + 8 + s];
                        if ((flags & 0x40) != 0) { uint r = BitConverter.ToUInt32(data, co + idxOff + s * 4); cmd.Index[s] = (int)(r & 0x3FFFFFFF); cmd.Tangent[s] = (int)((r >> 30) & 3); }
                        else { int r = U16(data, co + idxOff + s * 2); cmd.Index[s] = r & 0x3FFF; cmd.Tangent[s] = (r >> 14) & 3; }
                    }
                    if (!a.Commands.ContainsKey(bone)) a.Commands[bone] = new Dictionary<int, Dictionary<int, Command>>();
                    if (!a.Commands[bone].ContainsKey(kind)) a.Commands[bone][kind] = new Dictionary<int, Command>();
                    a.Commands[bone][kind][flags & 3] = cmd;
                }
                return a;
            }

            // global node matrices for a frame of an animation
            public List<double[]> Pose(int animation, double frame)
            {
                var nodes = new SortedDictionary<int, Node>();
                foreach (var r in raw)
                {
                    var n = new Node { Number = r.Item1, Parent = r.Item2, Child1 = r.Item3, Sibling = r.Item4, Flags = r.Item5, UnknownScale = r.Item6, Matrix = r.Item7 };
                    Decompose(n.Matrix, out n.Scale, out n.Q, out n.T);
                    nodes[n.Number] = n;
                }
                new Poser(nodes, Ik, Read(animation), frame).Run();
                return Enumerable.Range(0, nodes.Count).Select(i => nodes[i].AMatrix).ToList();
            }

            // the names the animation keys, plus the bones its IK sets
            public HashSet<string> Driven(int animation)
            {
                var driven = new HashSet<string>(Read(animation).Commands.Keys.Where(b => b < Names.Count).Select(b => Names[b]));
                foreach (var ik in Ik)
                {
                    int count = ik.Item1 == 0 ? 5 : 3;
                    for (int i = 0; i < count; i++) { int b = U16(ik.Item2, 2 + i * 2); if (b < Names.Count) driven.Add(Names[b]); }
                }
                return driven;
            }

            // Skin matrices for a costume model: bones the animation drives get its pose; the others keep the model's own
            // placement relative to the nearest ancestor it does move (the animation skeleton's rest pose for them can be
            // far off, e.g. Dhalsim's stretched arm bones). mirror: the stance mirrored left and right.
            public Dictionary<string, double[]> CostumeSkin(EmoSkeleton emo, int animation, double frame, bool mirror, Func<string, ICollection<string>, string> mirrorName)
            {
                var animated = new Dictionary<string, double[]>();
                var pose = Pose(animation, frame);
                for (int i = 0; i < Names.Count; i++) animated[Names[i]] = pose[i];   // a later duplicate name wins, as dict(zip())
                var driven = Driven(animation);
                var posed = new Dictionary<string, double[]>();
                Func<int, double[]> solve = null;
                solve = i =>
                {
                    string name = emo.Names[i];
                    double[] done;
                    if (posed.TryGetValue(name, out done)) return done;
                    int parent = emo.Parents[i];
                    if (animated.ContainsKey(name) && (driven.Contains(name) || parent < 0)) return posed[name] = animated[name];
                    int a = parent;
                    while (a >= 0 && !(animated.ContainsKey(emo.Names[a]) && driven.Contains(emo.Names[a]))) a = emo.Parents[a];
                    if (a < 0) return posed[name] = emo.Bind[name];
                    string anchor = emo.Names[a];
                    solve(a);
                    return posed[name] = Mul(Mul(emo.Bind[name], Inverse(emo.Bind[anchor])), posed[anchor]);
                };
                for (int i = 0; i < emo.Names.Count; i++) solve(i);
                var skin = new Dictionary<string, double[]>();
                foreach (var p in posed) skin[p.Key] = Mul(Inverse(emo.Bind[p.Key]), p.Value);
                if (!mirror) return skin;
                // a vertex is mirrored across x = 0, moved by the bone of the other side and mirrored back
                var mirrored = new Dictionary<string, double[]>();
                var keys = skin.Keys.ToList();
                foreach (string name in keys) mirrored[name] = MirrorX(skin[mirrorName(name, keys)]);
                return mirrored;
            }
        }

        static double[] MirrorX(double[] m)
        {
            var o = (double[])m.Clone();
            o[1] = -o[1]; o[2] = -o[2]; o[3] = -o[3]; o[4] = -o[4]; o[8] = -o[8]; o[12] = -o[12];
            return o;
        }

        class Poser
        {
            readonly SortedDictionary<int, Node> nodes;
            readonly List<Tuple<int, byte[]>> ik;
            readonly Animation animation;
            readonly double frame;

            public Poser(SortedDictionary<int, Node> nodes, List<Tuple<int, byte[]>> ik, Animation animation, double frame)
            {
                this.nodes = nodes; this.ik = ik; this.animation = animation; this.frame = frame;
            }

            Node Get(int number) { Node n; return nodes.TryGetValue(number, out n) ? n : null; }

            public void Run()
            {
                foreach (Node n in nodes.Values) Setup(n);
                foreach (int k in nodes.Keys.ToList()) Update(k, true, false);
                foreach (var e in ik)
                {
                    if (e.Item1 == 0 && e.Item2[0] == 2) IkTwoBone(e.Item2);
                    else if (e.Item1 == 1) IkBlend(e.Item2);
                }
                foreach (int k in nodes.Keys.ToList()) Update(k, true, false);
            }

            // Ema.h setupFrame + getTransform
            void Setup(Node node)
            {
                Decompose(node.Matrix, out node.Scale, out node.Q, out node.T);
                node.UpdateFlags = 0;
                node.AT = new double[3]; node.AScale = new double[] { 1, 1, 1 }; node.AQ = new double[] { 0, 0, 0, 1 };
                Dictionary<int, Dictionary<int, Command>> perType;
                if (animation != null && animation.Commands.TryGetValue(node.Number, out perType))
                {
                    foreach (var k in new[] { Tuple.Create(0, 0x1u, 0x10000u), Tuple.Create(1, 0x2u, 0x20000u), Tuple.Create(2, 0x4u, 0x40000u) })
                    {
                        var values = k.Item1 == 2 ? new double[] { 1, 1, 1 } : new double[3];
                        Dictionary<int, Command> per;
                        bool? absolute = perType.TryGetValue(k.Item1, out per) ? Sample(per, values) : null;
                        if (absolute == null) continue;
                        if (k.Item1 == 0) node.AT = values;
                        else if (k.Item1 == 1)
                        {
                            double r = Math.PI / 180;
                            node.AQ = QEulerXyz(values[1] * r, values[2] * r, values[0] * r);
                        }
                        else node.AScale = values;
                        node.UpdateFlags |= absolute.Value ? k.Item3 : k.Item2;
                    }
                }
                if ((node.UpdateFlags & 0x10001) != 0) node.T = (double[])node.AT.Clone(); else node.AT = (double[])node.T.Clone();
                if ((node.UpdateFlags & 0x20002) != 0) node.Q = (double[])node.AQ.Clone(); else node.AQ = (double[])node.Q.Clone();
                if ((node.UpdateFlags & 0x40004) != 0) node.Scale = (double[])node.AScale.Clone(); else node.AScale = (double[])node.Scale.Clone();
                node.AMatrix = Compose(node.AQ, node.AScale, node.AT);
                node.Matrix = (double[])node.AMatrix.Clone();
            }

            bool? Sample(Dictionary<int, Command> perComponent, double[] values)
            {
                if (perComponent.Count == 0) return null;
                bool animated = false, absolute = false;
                float[] v = animation.Values;
                foreach (var pc in perComponent)
                {
                    if (pc.Key >= 3) continue;
                    Command c = pc.Value;
                    for (int k = 1; k < c.Frames.Length; k++)
                    {
                        if (c.Frames[k] < frame) continue;
                        double s = (frame - c.Frames[k - 1]) / Math.Max(c.Frames[k] - c.Frames[k - 1], 1);
                        double p1 = v[c.Index[k - 1]], p2 = v[c.Index[k]];
                        double t1 = c.Tangent[k - 1] != 0 ? v[c.Index[k - 1] + c.Tangent[k - 1]] : 0, t2 = c.Tangent[k] != 0 ? v[c.Index[k] + c.Tangent[k]] : 0;
                        double s2 = s * s, s3 = s * s * s;
                        values[pc.Key] = (2 * s3 - 3 * s2 + 1) * p1 + (-2 * s3 + 3 * s2) * p2 + (s3 - 2 * s2 + s) * t1 + (s3 - s2) * t2;
                        absolute |= c.Absolute;
                        animated = true;
                        break;
                    }
                }
                return animated ? (bool?)absolute : null;
            }

            // Ema.h update_node; a damaged skeleton (a node its own parent, child or sibling, round and round) is refused
            // before the walk runs out of stack
            int depth;

            bool Update(int number, bool initialize, bool updateParent)
            {
                Node node = Get(number);
                if (node == null) return false;
                if (++depth > 2 * nodes.Count + 16) throw new Damaged("its animation skeleton loops (node " + number + ")");
                try { return UpdateNode(node, initialize, updateParent); }
                finally { depth--; }
            }

            bool UpdateNode(Node node, bool initialize, bool updateParent)
            {
                Node parent = Get(node.Parent);
                if (updateParent && parent != null) Update(parent.Number, initialize, updateParent);
                bool ok;
                if (initialize)
                {
                    ok = (updateParent || (node.Flags & 0x8004) == 0) && (node.UpdateFlags & 0x80000000) == 0;
                    ok = ok && (node.Parent == 0xFFFF || (parent != null && (parent.UpdateFlags & 0x80000000) != 0));
                }
                else ok = node.Parent != 0xFFFF && (node.UpdateFlags & 0x70000000) == 0;
                if (!ok)
                {
                    if (!initialize) Update(node.Sibling, initialize, updateParent);
                    return (node.UpdateFlags & 0x80000000) != 0;
                }
                double[] scaleRest, qRest, tRest;
                Decompose(node.Matrix, out scaleRest, out qRest, out tRest);
                double[] t = (node.UpdateFlags & 0x10001) != 0 ? (double[])node.T.Clone() : tRest;
                if (parent != null && (node.UpdateFlags & 0x10000) == 0) t = TransformCoord(t, parent.AMatrix);
                double[] q = (node.UpdateFlags & 0x20002) != 0 ? (double[])node.Q.Clone() : qRest;
                if (parent != null && (node.UpdateFlags & 0x20000) == 0) q = QMul(q, parent.AQ);
                double[] scale = (node.UpdateFlags & 0x40004) != 0 ? (double[])node.Scale.Clone() : new double[] { 1, 1, 1 };
                if (parent != null && (node.UpdateFlags & 0x40000) == 0) scale = new[] { scale[0] * parent.AScale[0], scale[1] * parent.AScale[1], scale[2] * parent.AScale[2] };
                node.AScale = scale; node.AT = t; node.AQ = q;
                node.AMatrix = Compose(q, scale, t);
                node.UpdateFlags |= 0x80000000;
                if (!initialize)
                {
                    Update(node.Child1, initialize, updateParent);
                    Update(node.Sibling, initialize, updateParent);
                }
                return true;
            }

            // Ema.h processIKData0x00_02: two-bone IK (thigh, shin) towards a target
            void IkTwoBone(byte[] data)
            {
                Node n0 = Get(U16(data, 2)), n1 = Get(U16(data, 4)), n2 = Get(U16(data, 6)), n3 = Get(U16(data, 8)), n4 = Get(U16(data, 10));
                Node np = n0 != null ? Get(n0.Parent) : null;
                if (np == null || n0 == null || n1 == null || n2 == null || n3 == null || n4 == null) return;
                n3.UpdateFlags |= 0x10000000;
                n4.UpdateFlags |= 0x20000000;
                Update(n1.Number, true, true);
                Update(n2.Number, true, true);
                double[] origin = n0.AT, toTarget = Sub3(n3.AT, origin), toPole = Sub3(n4.AT, origin);
                double[] side = Cross(toPole, toTarget), up = Cross(side, toTarget);
                if ((data[1] & 1) != 0) { side = Scale3(side, -1); up = Scale3(up, -1); }
                double[] x = Normalize(toTarget), y = Normalize(up), z = Normalize(side);
                double[] basis = { x[0], x[1], x[2], 0, y[0], y[1], y[2], 0, z[0], z[1], z[2], 0, origin[0], origin[1], origin[2], 1 };
                double distance = Length(toTarget);
                double length1 = n1.AScale[0] * n1.UnknownScale, length2 = n2.AScale[0] * n2.UnknownScale;
                double cos1 = Clamp((distance * distance + length1 * length1 - length2 * length2) * 0.5 / (length1 * distance), -1, 1);
                double cos2 = Clamp((length1 * length1 + length2 * length2 - distance * distance) * 0.5 / (length2 * length1), -1, 1);
                double sin1 = Math.Sqrt(1 - cos1 * cos1), sin2 = Math.Sqrt(1 - cos2 * cos2);
                double[] axis = { 0, 0, -1 };
                double[] m1 = Mul(AxisMatrix(axis, sin1, cos1), basis);
                double[] bone1 = Mul(Diag(n1.AScale), m1);
                double[] joint = (double[])m1.Clone();
                for (int i = 0; i < 4; i++) joint[12 + i] = bone1[12 + i];
                double[] knee = Row(new[] { length1, 0, 0, 1 }, joint);
                for (int i = 0; i < 4; i++) joint[12 + i] = knee[i];
                double[] m2 = Mul(AxisMatrix(axis, -sin2, -cos2), joint);
                double[] bone2 = Mul(Diag(n2.AScale), m2);
                double[] end = Row(new[] { n2.UnknownScale, 0, 0, 1 }, bone2);
                foreach (var b in new[] { Tuple.Create(n1, bone1, m1), Tuple.Create(n2, bone2, m2) })
                {
                    b.Item1.AT = Translation(b.Item2); b.Item1.AQ = QFromMatrix(b.Item3); b.Item1.AMatrix = b.Item2;
                    b.Item1.UpdateFlags = (b.Item1.UpdateFlags & 0xFFFFFFFC) | 0xC0003000;
                }
                n3.AT = new[] { end[0], end[1], end[2] };
                n3.AMatrix[12] = end[0]; n3.AMatrix[13] = end[1]; n3.AMatrix[14] = end[2];
                n3.UpdateFlags = (n3.UpdateFlags & 0xFFFFFFFE) | 0x1000;
                n3.T = (double[])n3.AT.Clone();
                Update(n1.Child1, false, false);
                Update(n2.Child1, false, false);
            }

            // Ema.h processIKData0x01_00: a helper bone blended between two others
            void IkBlend(byte[] data)
            {
                Node n0 = Get(U16(data, 2)), n1 = Get(U16(data, 4)), n2 = Get(U16(data, 6));
                Node np = n0 != null ? Get(n0.Parent) : null;
                if (np == null || n0 == null || n1 == null || n2 == null) return;
                Update(n1.Number, true, true);
                Update(n2.Number, true, true);
                Update(np.Number, true, true);
                double ft = F32(data, 0x0C), fr = F32(data, 0x10), fs = F32(data, 0x14);
                double[] t = (data[1] & 1) != 0 ? Add3(n2.AT, Scale3(Sub3(n1.AT, n2.AT), ft)) : TransformCoord(n0.T, np.AMatrix);
                double[] q;
                if ((data[1] & 2) != 0) q = Enumerable.Range(0, 4).Select(i => n1.AQ[i] + (n2.AQ[i] - n1.AQ[i]) * fr).ToArray();
                else q = QMul(n0.AQ, np.AQ);
                double qn = Math.Sqrt(q.Sum(e => e * e));
                if (qn != 0) q = q.Select(e => e / qn).ToArray();
                double[] scale = (data[1] & 4) != 0 ? Add3(n2.AScale, Scale3(Sub3(n1.AScale, n2.AScale), fs)) : (double[])np.AScale.Clone();
                n0.AScale = scale; n0.AT = t; n0.AQ = q;
                n0.AMatrix = Compose(q, scale, t);
                n0.UpdateFlags = (n0.UpdateFlags & 0xFFFFFFF8) | 0x80007000;
                n0.Scale = (double[])scale.Clone(); n0.T = (double[])t.Clone(); n0.Q = (double[])q.Clone();
                Update(n0.Child1, false, false);
            }
        }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : v > hi ? hi : v; }
        static double[] Diag(double[] s) { var m = Identity(); m[0] = s[0]; m[5] = s[1]; m[10] = s[2]; return m; }
        static double[] Row(double[] v, double[] m)
        {
            var o = new double[4];
            for (int c = 0; c < 4; c++) o[c] = v[0] * m[c] + v[1] * m[4 + c] + v[2] * m[8 + c] + v[3] * m[12 + c];
            return o;
        }

        // ---------- left and right ----------

        // the left/right counterpart of a bone (LLeg1 / RLeg1, SCD_05_01_Lsuso / _Rsuso, SodeL / SodeR, ringforarm_ln /
        // _rn, and numbered cloth panels at the mirrored spot); itself without one
        static string MirrorName(string name, ICollection<string> names, Dictionary<string, string> byPosition)
        {
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if ((ch == 'L' || ch == 'R') && (i == 0 || name[i - 1] == '_'))
                {
                    string other = name.Substring(0, i) + (ch == 'L' ? 'R' : 'L') + name.Substring(i + 1);
                    if (names.Contains(other)) return other;
                }
            }
            Func<char, char> swap = c => c == 'L' ? 'R' : c == 'R' ? 'L' : c == 'l' ? 'r' : c == 'r' ? 'l' : '\0';
            if (name.Length > 0 && swap(name[name.Length - 1]) != '\0')
            {
                string other = name.Substring(0, name.Length - 1) + swap(name[name.Length - 1]);
                if (names.Contains(other)) return other;
            }
            int us = name.LastIndexOf('_');
            if (us > 0 && us + 1 < name.Length && swap(name[us + 1]) != '\0')
            {
                string other = name.Substring(0, us + 1) + swap(name[us + 1]) + name.Substring(us + 2);
                if (names.Contains(other)) return other;
            }
            string spot;
            return byPosition.TryGetValue(name, out spot) && names.Contains(spot) ? spot : name;
        }

        static Dictionary<string, string> MirrorByPosition(Dictionary<string, double[]> bind)
        {
            var names = bind.Keys.Where(n => n.StartsWith("SCD")).ToList();
            var map = new Dictionary<string, string>();
            foreach (string n in names)
            {
                double[] p = Translation(bind[n]);
                p[0] = -p[0];
                string best = null;
                double bestD = double.MaxValue;
                foreach (string m in names)
                {
                    double d = Length(Sub3(Translation(bind[m]), p));
                    if (d < bestD) { bestD = d; best = m; }
                }
                if (bestD < 0.01 && best != n) map[n] = best;
            }
            return map;
        }

        // ---------- physics ----------

        static readonly string[] PhysicsWords = { "himo", "hair", "mitsuami", "kami", "muffler", "scarf", "obi", "ribbon", "cape", "mant", "skirt", "suso" };

        static bool IsPhysics(string name)
        {
            string lower = name.ToLowerInvariant();
            return name.StartsWith("SCD") || PhysicsWords.Any(lower.Contains);
        }

        // bones the animation doesn't drive follow their nearest ancestor that it does (walks up end: EmoSkeleton refuses
        // a skeleton that loops)
        static Dictionary<string, double[]> WithInherited(Dictionary<string, double[]> matrices, EmoSkeleton emo)
        {
            var result = new Dictionary<string, double[]>(matrices);
            for (int i = 0; i < emo.Names.Count; i++)
            {
                string name = emo.Names[i];
                int p = emo.Parents[i];
                while (!result.ContainsKey(name) && p >= 0)
                {
                    double[] m;
                    if (result.TryGetValue(emo.Names[p], out m)) { result[name] = m; break; }
                    p = emo.Parents[p];
                }
            }
            return result;
        }

        class BsrPart
        {
            public double Gravity, Damping, Ground;
            public Dictionary<string, double[]> Structures = new Dictionary<string, double[]>();   // name -> (a1, a2)
            public List<string> ParticleOrder = new List<string>();
            public Dictionary<string, double[]> Particles = new Dictionary<string, double[]>();    // name -> (mass, radius, k)
            public List<Tuple<int, string, string, double[], int>> Joints = new List<Tuple<int, string, string, double[], int>>();
            public List<Tuple<int, string, string, double[], double, double[]>> Colliders = new List<Tuple<int, string, string, double[], double, double[]>>();
        }

        static List<BsrPart> ReadBsr(byte[] d)
        {
            var parts = new List<BsrPart>();
            if (d == null || d.Length < 0x24 || Encoding.ASCII.GetString(d, 0, 4) != "#BSR") return parts;
            int count = I32(d, 0x0C), table = I32(d, 0x10), namesAt = I32(d, 0x18), colliderNamesAt = I32(d, 0x20);
            Func<int, int, string> name = (at, i) => CString(d, I32(d, at + i * 4));
            for (int p = 0; p < count; p++)
            {
                int po = I32(d, table + p * 4);
                var part = new BsrPart { Gravity = F32(d, po), Damping = F32(d, po + 4), Ground = F32(d, po + 12) };
                int ns = I32(d, po + 0x10), so = I32(d, po + 0x14), npt = I32(d, po + 0x18), pto = I32(d, po + 0x1c);
                int nj = I32(d, po + 0x20), jo = I32(d, po + 0x24), nc = I32(d, po + 0x28), co = I32(d, po + 0x2c);
                for (int i = 0; i < ns; i++)
                {
                    int o = po + so + i * 12;
                    part.Structures[name(namesAt, I32(d, o))] = new double[] { F32(d, o + 4), F32(d, o + 8) };
                }
                for (int i = 0; i < npt; i++)
                {
                    int o = po + pto + i * 36;
                    string n = name(namesAt, I32(d, o));
                    if (!part.Particles.ContainsKey(n)) part.ParticleOrder.Add(n);
                    part.Particles[n] = new double[] { F32(d, o + 4), F32(d, o + 8), F32(d, o + 12) };
                }
                for (int i = 0; i < nj; i++)
                {
                    int o = po + jo + i * 28;
                    var f = Enumerable.Range(0, 6).Select(k => (double)F32(d, o + 4 + k * 4)).ToArray();
                    part.Joints.Add(Tuple.Create(U16(d, o), name(namesAt, d[o + 2]), name(namesAt, d[o + 3]), f, I32(d, o + 0x10)));
                }
                for (int i = 0; i < nc; i++)
                {
                    int o = po + co + i * 40;
                    int t = U16(d, o);
                    var a = new double[] { F32(d, o + 12), F32(d, o + 16), F32(d, o + 20) };
                    double r = F32(d, o + 24);
                    if (t == 0) part.Colliders.Add(Tuple.Create(0, name(colliderNamesAt, U16(d, o + 2)), (string)null, a, r, (double[])null));
                    else part.Colliders.Add(Tuple.Create(1, name(colliderNamesAt, d[o + 2]), d[o + 3] == 0xFF ? null : name(colliderNamesAt, d[o + 3]), a, r,
                                                         new double[] { F32(d, o + 28), F32(d, o + 32), F32(d, o + 36) }));
                }
                parts.Add(part);
            }
            return parts;
        }

        static double[] RotY(double a) { double c = Math.Cos(a), s = Math.Sin(a); return new[] { c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1 }; }
        static double[] RotZ(double a) { double c = Math.Cos(a), s = Math.Sin(a); return new[] { c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }; }

        // full: name -> skin matrix (the rigid-follow pose); returns it with the .bsr bones settled
        static Dictionary<string, double[]> SimulateBsr(Dictionary<string, double[]> full, EmoSkeleton emo, List<BsrPart> parts)
        {
            var bind = emo.Bind;
            var parentOf = new Dictionary<string, string>();
            for (int i = 0; i < emo.Names.Count; i++) parentOf[emo.Names[i]] = emo.Parents[i] >= 0 ? emo.Names[emo.Parents[i]] : null;
            Func<string, double[]> world = n => Mul(bind[n], full[n]);
            var output = new Dictionary<string, double[]>(full);
            foreach (BsrPart part in parts)
            {
                var names = part.ParticleOrder.Where(n => bind.ContainsKey(n) && full.ContainsKey(n)).ToList();
                if (names.Count == 0) continue;
                int count = names.Count;
                var idx = new Dictionary<string, int>();
                for (int i = 0; i < count; i++) idx[names[i]] = i;
                var target = names.Select(n => Translation(world(n))).ToArray();
                var pos = target.Select(p => (double[])p.Clone()).ToArray();
                var vel = names.Select(n => new double[3]).ToArray();
                var pinned = names.Select(n => part.Structures.ContainsKey(n) || n == "SCD_00_01_mituami").ToArray();
                var mass = names.Select(n => part.Particles[n][0]).ToArray();
                var radius = names.Select(n => part.Particles[n][1]).ToArray();
                var k = names.Select(n => part.Particles[n][2]).ToArray();
                var prev = names.Select(n => { string p; int j; return parentOf.TryGetValue(n, out p) && p != null && idx.TryGetValue(p, out j) ? j : -1; }).ToArray();
                var offset = part.Structures.ToDictionary(s => s.Key, s => Mul(RotY(s.Value[0]), RotZ(s.Value[1])));
                var joints = part.Joints.Where(j => idx.ContainsKey(j.Item2) && idx.ContainsKey(j.Item3))
                                        .Select(j => Tuple.Create(j.Item1, idx[j.Item2], idx[j.Item3], j.Item4, j.Item5)).ToList();
                var rest = new Dictionary<int, double>();
                for (int j = 0; j < joints.Count; j++)
                    if (joints[j].Item1 == 0) rest[j] = Length(Sub3(target[joints[j].Item3], target[joints[j].Item2])) * joints[j].Item4[3];

                Func<int, double[]> refDir = a =>
                {
                    if (!pinned[a] && prev[a] >= 0 && Length(Sub3(pos[a], pos[prev[a]])) > 1e-9)
                    {
                        double[] dd = Sub3(pos[a], pos[prev[a]]);
                        return Scale3(dd, 1 / Length(dd));
                    }
                    string n = names[a], p;
                    parentOf.TryGetValue(n, out p);
                    double[] frame = p != null && bind.ContainsKey(p) && full.ContainsKey(p) ? world(p) : Identity();
                    double[] off;
                    double[] dir = Row(new[] { 1.0, 0, 0, 0 }, offset.TryGetValue(n, out off) ? off : Identity());
                    double[] d3 = Row(new[] { dir[0], dir[1], dir[2], 0 }, frame);   // w = 0: the rotation part only
                    double[] v = { d3[0], d3[1], d3[2] };
                    return Scale3(v, 1 / Math.Max(Length(v), 1e-9));
                };

                var colliders = new List<Tuple<double[], double[], double>>();
                foreach (var c in part.Colliders)
                {
                    if (!bind.ContainsKey(c.Item2) || !full.ContainsKey(c.Item2)) continue;
                    double[] m1 = world(c.Item2);
                    double[] pa = Point(c.Item4[0], c.Item4[1], c.Item4[2], m1);
                    if (c.Item1 == 0) { colliders.Add(Tuple.Create(pa, (double[])null, c.Item5)); continue; }
                    string other = c.Item3 ?? (parentOf.ContainsKey(c.Item2) ? parentOf[c.Item2] : null);
                    double[] m2 = other != null && bind.ContainsKey(other) && full.ContainsKey(other) ? world(other) : m1;
                    colliders.Add(Tuple.Create(pa, Point(c.Item6[0], c.Item6[1], c.Item6[2], m2), c.Item5));
                }
                double[] g = { 0, -part.Gravity, 0 };
                for (int step = 0; step < BsrSteps; step++)
                {
                    var force = names.Select(n => new double[3]).ToArray();
                    for (int j = 0; j < joints.Count; j++)   // Joint::preStep
                    {
                        var jt = joints[j];
                        int a = jt.Item2, b = jt.Item3;
                        double[] f = jt.Item4, d = Sub3(pos[b], pos[a]);
                        double length = Length(d);
                        if (length < 1e-9) continue;
                        double[] u = Scale3(d, 1 / length);
                        if (jt.Item1 == 0)
                        {
                            double lo = rest[j] * f[5], hi = rest[j] * f[4];
                            double s = (Math.Min(Math.Max(length, lo), hi) - rest[j]) * f[0];
                            if (!pinned[b]) force[b] = Sub3(force[b], Scale3(u, s));
                            if (!pinned[a]) force[a] = Add3(force[a], Scale3(u, s));
                        }
                        else if (jt.Item1 == 2 && (f[0] != 0 || f[1] != 0))
                        {
                            double[] r = refDir(a);
                            double ang = Math.Acos(Clamp(Dot(u, r), -1, 1));
                            if (jt.Item5 == 0) ang = Math.Min(ang, f[4]);
                            double x = Clamp(ang / Math.PI * 4.0, 1.0, 2.0);
                            double s = Math.Max(0.0, (f[0] * x + f[1] * x * x) * length);
                            if (!pinned[b]) force[b] = Add3(force[b], Scale3(Cross(Cross(u, r), u), s));
                        }
                    }
                    for (int i = 0; i < count; i++)
                    {
                        if (pinned[i]) { vel[i] = new double[3]; pos[i] = (double[])target[i].Clone(); continue; }
                        var acc = new double[3];
                        for (int c = 0; c < 3; c++) acc[c] = force[i][c] / mass[i] + g[c] - part.Damping * vel[i][c] + (k[i] / mass[i]) * (target[i][c] - pos[i][c]);
                        for (int c = 0; c < 3; c++) { vel[i][c] += acc[c] * BsrDt; pos[i][c] += vel[i][c] * BsrDt; }
                        pos[i][1] = Math.Max(pos[i][1], part.Ground);   // Particle::clampToGround 0x670ec0
                    }
                    for (int j = 0; j < joints.Count; j++)   // Joint::postStep
                    {
                        var jt = joints[j];
                        int a = jt.Item2, b = jt.Item3;
                        double[] f = jt.Item4, d = Sub3(pos[b], pos[a]);
                        double length = Length(d);
                        if (length < 1e-9 || pinned[b]) continue;
                        double[] u = Scale3(d, 1 / length);
                        if (jt.Item1 == 0)
                        {
                            double lo = rest[j] * f[5], hi = rest[j] * f[4];
                            if (length < lo || length > hi)
                            {
                                pos[b] = Add3(pos[a], Scale3(u, length < lo ? lo : hi));
                                vel[b] = Sub3(vel[b], Scale3(u, Dot(vel[b], u)));
                            }
                        }
                        else if (jt.Item1 == 2 && jt.Item5 == 0)
                        {
                            double[] r = refDir(a);
                            double ang = Math.Acos(Clamp(Dot(u, r), -1, 1));
                            if (ang > f[4])
                            {
                                double[] n = Add3(r, Scale3(Sub3(u, r), f[4] / ang));
                                n = Scale3(n, 1 / Length(n));
                                vel[b] = Scale3(n, Dot(vel[b], n));
                                pos[b] = Add3(pos[a], Scale3(n, length));
                            }
                        }
                    }
                    foreach (var c in colliders)   // Collider::collide: push out, drop the normal velocity
                    {
                        var hits = new List<Tuple<int, double[], double[]>>();   // particle, pushed to, normal
                        for (int i = 0; i < count; i++)
                        {
                            if (pinned[i]) continue;
                            double[] p = pos[i], q;
                            if (c.Item2 == null) q = c.Item1;
                            else
                            {
                                double[] seg = Sub3(c.Item2, c.Item1);
                                q = Add3(c.Item1, Scale3(seg, Clamp(Dot(Sub3(p, c.Item1), seg) / Math.Max(Dot(seg, seg), 1e-12), 0, 1)));
                            }
                            double[] d = Sub3(p, q);
                            double dist = Length(d), reach = c.Item3 + radius[i];
                            if (dist > 1e-9 && dist < reach) hits.Add(Tuple.Create(i, Add3(q, Scale3(d, reach / dist)), Scale3(d, 1 / dist)));
                        }
                        foreach (var h in hits)
                        {
                            pos[h.Item1] = h.Item2;
                            vel[h.Item1] = Sub3(vel[h.Item1], Scale3(h.Item3, Math.Min(Dot(vel[h.Item1], h.Item3), 0.0)));
                        }
                    }
                }
                // Structure::writeBonePoses: each bone turns to the segment to its child particle
                var children = new Dictionary<int, List<int>>();
                for (int i = 0; i < count; i++)
                    if (prev[i] >= 0) { if (!children.ContainsKey(prev[i])) children[prev[i]] = new List<int>(); children[prev[i]].Add(i); }
                for (int i = 0; i < count; i++)
                {
                    double[] oldDir, newDir;
                    List<int> kids;
                    if (children.TryGetValue(i, out kids)) { oldDir = Sub3(target[kids[0]], target[i]); newDir = Sub3(pos[kids[0]], pos[i]); }
                    else if (prev[i] >= 0) { oldDir = Sub3(target[i], target[prev[i]]); newDir = Sub3(pos[i], pos[prev[i]]); }
                    else continue;
                    double[] r = Math.Min(Length(oldDir), Length(newDir)) < 1e-9 ? Identity() : RotationBetween(oldDir, newDir);
                    output[names[i]] = Mul(Mul(Mul(full[names[i]], Translate(Scale3(target[i], -1))), r), Translate(pos[i]));
                }
            }
            return output;
        }

        // ---------- the mesh ----------

        /// <summary>The parts of an .obj.emo (GIM props left out but for the ones a fighter wears in a fight).</summary>
        public static List<Part> ReadEmo(byte[] d, string fighter)
        {
            if (Encoding.ASCII.GetString(d, 0, 4) != "#EMO") throw new InvalidOperationException("not an #EMO model");
            string[] keep;
            if (!KeepGim.TryGetValue(fighter, out keep)) keep = new string[0];
            int count = U16(d, 0x20), namesAt = I32(d, 0x24);
            var parts = new List<Part>();
            for (int i = 0; i < count; i++)
            {
                string partName = CString(d, 0x20 + I32(d, 0x20 + namesAt + i * 4)).ToUpperInvariant();
                if (partName.StartsWith("GIM") && !keep.Contains(partName)) continue;   // intro and win props
                int h = 0x20 + I32(d, 0x28 + i * 4) + 0x20;
                int texCount = I32(d, h + 4), texList = I32(d, h + 12), vCount = U16(d, h + 16), vSize = U16(d, h + 18), vOff = I32(d, h + 20);
                int strip = U16(d, h + 24), subCount = U16(d, h + 26), subList = I32(d, h + 28);
                var texIds = Enumerable.Range(0, texCount).Select(t => (int)d[h + I32(d, h + texList + t * 4) + 5]).ToArray();
                var part = new Part { Positions = new double[vCount * 3], Normals = new double[vCount * 3], Uvs = new float[vCount * 2], Bones = new byte[vCount * 4], Weights = new double[vCount * 3] };
                bool skinned = vSize >= 64;
                for (int v = 0; v < vCount; v++)
                {
                    int o = h + vOff + v * vSize;
                    for (int c = 0; c < 3; c++) { part.Positions[v * 3 + c] = F32(d, o + c * 4); part.Normals[v * 3 + c] = F32(d, o + 12 + c * 4); }
                    part.Uvs[v * 2] = F32(d, o + 24); part.Uvs[v * 2 + 1] = F32(d, o + 28);
                    if (skinned)
                    {
                        Buffer.BlockCopy(d, o + 48, part.Bones, v * 4, 4);
                        for (int c = 0; c < 3; c++) part.Weights[v * 3 + c] = F32(d, o + 52 + c * 4);
                    }
                    else part.Weights[v * 3] = 1;
                }
                for (int s = 0; s < subCount; s++)
                {
                    int p = h + I32(d, h + subList + s * 4) + 0x10;
                    int entry = U16(d, p), idxCount = U16(d, p + 2), nodeCount = U16(d, p + 4);
                    var idx = Enumerable.Range(0, idxCount).Select(k => U16(d, p + 38 + k * 2)).ToArray();
                    part.Nodes.Add(Enumerable.Range(0, nodeCount).Select(k => U16(d, p + 38 + idxCount * 2 + k * 2)).ToArray());
                    part.Subs.Add(new Sub { Texture = entry < texIds.Length ? texIds[entry] : 0, Triangles = strip != 0 ? StripToList(idx) : idx.Take(idxCount / 3 * 3).ToArray(), Material = CString(d, p + 6, 32) });
                }
                parts.Add(part);
            }
            return parts;
        }

        static int[] StripToList(int[] idx)
        {
            var tris = new List<int>();
            for (int i = 0; i + 2 < idx.Length; i++)
            {
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                if (a == b || b == c || a == c) continue;   // the degenerate joint between strips
                if (i % 2 == 0) { tris.Add(a); tris.Add(b); tris.Add(c); } else { tris.Add(b); tris.Add(a); tris.Add(c); }
            }
            return tris.ToArray();
        }

        static string[] NodeNames(EmoSkeleton emo, int[] nodes)
        {
            return nodes.Select(n => n < emo.Names.Count ? emo.Names[n] : "").ToArray();
        }

        // the bone with the largest weight of a vertex
        static int MainBone(Part part, int v)
        {
            double w0 = part.Weights[v * 3], w1 = part.Weights[v * 3 + 1], w2 = part.Weights[v * 3 + 2], w3 = 1 - w0 - w1 - w2;
            int best = 0;
            double top = w0;
            if (w1 > top) { top = w1; best = 1; }
            if (w2 > top) { top = w2; best = 2; }
            if (w3 > top) best = 3;
            return part.Bones[v * 4 + best];
        }

        // linear blend skinning of a vertex: bone ids index the submesh's node names
        static void Skin(Part part, int v, string[] nodeNames, Dictionary<string, double[]> matrices, double[] p, double[] n)
        {
            double x = part.Positions[v * 3], y = part.Positions[v * 3 + 1], z = part.Positions[v * 3 + 2];
            double nx = part.Normals[v * 3], ny = part.Normals[v * 3 + 1], nz = part.Normals[v * 3 + 2];
            double w0 = part.Weights[v * 3], w1 = part.Weights[v * 3 + 1], w2 = part.Weights[v * 3 + 2];
            double[] w = { w0, w1, w2, 1 - w0 - w1 - w2 };
            p[0] = p[1] = p[2] = n[0] = n[1] = n[2] = 0;
            for (int k = 0; k < 4; k++)
            {
                double wk = Clamp(w[k], 0, 1);
                int b = Math.Min(part.Bones[v * 4 + k], Math.Max(nodeNames.Length, 1) - 1);
                double[] m;
                if (nodeNames.Length == 0 || !matrices.TryGetValue(nodeNames[b], out m)) m = IdentityMatrix;
                p[0] += wk * (x * m[0] + y * m[4] + z * m[8] + m[12]);
                p[1] += wk * (x * m[1] + y * m[5] + z * m[9] + m[13]);
                p[2] += wk * (x * m[2] + y * m[6] + z * m[10] + m[14]);
                n[0] += wk * (nx * m[0] + ny * m[4] + nz * m[8]);
                n[1] += wk * (nx * m[1] + ny * m[5] + nz * m[9]);
                n[2] += wk * (nx * m[2] + ny * m[6] + nz * m[10]);
            }
        }

        static readonly double[] IdentityMatrix = Identity();

        /// <summary>The animations of a fighter's .obj.ema: name and frame count each, in order.</summary>
        public static List<KeyValuePair<string, int>> Animations(byte[] emaData)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (int o in new Ema(emaData).AnimationOffsets)
            {
                int at = o + I32(emaData, o + 12) + 10;   // the name: a length byte, then its text
                string name = at < emaData.Length ? Encoding.GetEncoding(28591).GetString(emaData, at + 1, Math.Min(emaData[at], emaData.Length - at - 1)) : "";
                list.Add(new KeyValuePair<string, int>(name, U16(emaData, o)));
            }
            return list;
        }

        /// <summary>The model's parts in frame `frame` of animation `animation` of the fighter's .obj.ema (0: the fighting
        /// stance), mirrored (`mirror`: 1P, as character select; else 2P), cloth settled (`bsr`, may be null); stray
        /// accessories left out of the triangles.</summary>
        public static List<Part> Posed(byte[] emoData, byte[] emaData, byte[] bsr, string fighter, int animation = 0, double frame = 0, bool mirror = true)
        {
            var emo = new EmoSkeleton(emoData);
            var byPosition = MirrorByPosition(emo.Bind);
            Func<string, ICollection<string>, string> mirrorName = (n, names) => MirrorName(n, names, byPosition);
            var ema = new Ema(emaData);
            var driven = ema.CostumeSkin(emo, animation, frame, mirror, mirrorName);
            // A bone off the centre line with no counterpart (Blanka's single ankle chain) would be mirrored onto the other
            // side and moved by that side's limb; without its own matrix it takes its parent's and stays on its limb.
            var animated = ema.Driven(animation);
            foreach (string n in driven.Keys.ToList())
                if (mirror && !animated.Contains(n) && emo.Bind.ContainsKey(n) && Math.Abs(emo.Bind[n][12]) > 0.02 && mirrorName(n, driven.Keys) == n)
                    driven.Remove(n);
            var bsrParts = ReadBsr(bsr);
            var matrices = SimulateBsr(WithInherited(driven, emo), emo, bsrParts);
            var simulated = new HashSet<string>(bsrParts.SelectMany(b => b.Particles.Keys));
            var parts = ReadEmo(emoData, fighter);

            // the body (no physics bones) for telling stray accessories
            var body = new List<double[]>();
            var p = new double[3]; var nn = new double[3];
            foreach (Part part in parts)
                for (int s = 0; s < part.Subs.Count; s++)
                {
                    string[] names = NodeNames(emo, part.Nodes[s]);
                    foreach (int v in part.Subs[s].Triangles.Distinct())
                    {
                        int b = MainBone(part, v);
                        if (IsPhysics(b < names.Length ? names[b] : "")) continue;
                        Skin(part, v, names, matrices, p, nn);
                        body.Add((double[])p.Clone());
                    }
                }
            var near = new NearestPoints(body);
            var keyed = new HashSet<string>(animated.Concat(simulated));
            foreach (Part part in parts)
            {
                var positions = (double[])part.Positions.Clone();
                var normals = (double[])part.Normals.Clone();
                var loose = new bool[positions.Length / 3];
                for (int s = 0; s < part.Subs.Count; s++)
                {
                    string[] names = NodeNames(emo, part.Nodes[s]);
                    foreach (int v in part.Subs[s].Triangles.Distinct())
                    {
                        Skin(part, v, names, matrices, p, nn);
                        for (int c = 0; c < 3; c++) { positions[v * 3 + c] = p[c]; normals[v * 3 + c] = nn[c]; }
                        int b = MainBone(part, v);
                        string bone = b < names.Length ? names[b] : "";
                        // accessories the animation doesn't place, far from the body: the game's physics puts them
                        if (keyed.Contains(bone) && !simulated.Contains(bone)) continue;
                        loose[v] = near.Distance(p) > (simulated.Contains(bone) ? ClothLooseDistance : LooseDistance);
                    }
                }
                part.Positions = positions;
                part.Normals = normals;
                foreach (Sub sub in part.Subs)
                {
                    var kept = new List<int>();
                    for (int t = 0; t + 2 < sub.Triangles.Length; t += 3)
                        if (!loose[sub.Triangles[t]] && !loose[sub.Triangles[t + 1]] && !loose[sub.Triangles[t + 2]])
                            kept.AddRange(new[] { sub.Triangles[t], sub.Triangles[t + 1], sub.Triangles[t + 2] });
                    sub.Triangles = kept.ToArray();
                }
            }
            return parts;
        }

        // nearest-point distances in a grid of 5 cm cells
        class NearestPoints
        {
            const double Cell = 0.05;
            readonly Dictionary<long, List<double[]>> cells = new Dictionary<long, List<double[]>>();
            readonly List<double[]> all;

            public NearestPoints(List<double[]> points)
            {
                all = points;
                foreach (double[] p in points)
                {
                    long key = Key((int)Math.Floor(p[0] / Cell), (int)Math.Floor(p[1] / Cell), (int)Math.Floor(p[2] / Cell));
                    List<double[]> list;
                    if (!cells.TryGetValue(key, out list)) cells[key] = list = new List<double[]>();
                    list.Add(p);
                }
            }

            static long Key(int x, int y, int z) { return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF); }

            public double Distance(double[] p)
            {
                if (all.Count == 0) return double.MaxValue;
                int cx = (int)Math.Floor(p[0] / Cell), cy = (int)Math.Floor(p[1] / Cell), cz = (int)Math.Floor(p[2] / Cell);
                double best = double.MaxValue;
                for (int r = 0; r < 64; r++)   // rings of cells outwards until nothing nearer can be left
                {
                    for (int x = cx - r; x <= cx + r; x++)
                        for (int y = cy - r; y <= cy + r; y++)
                            for (int z = cz - r; z <= cz + r; z++)
                            {
                                if (Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz))) != r) continue;
                                List<double[]> list;
                                if (!cells.TryGetValue(Key(x, y, z), out list)) continue;
                                foreach (double[] q in list)
                                {
                                    double dx = q[0] - p[0], dy = q[1] - p[1], dz = q[2] - p[2], d = dx * dx + dy * dy + dz * dz;
                                    if (d < best) best = d;
                                }
                            }
                    if (best < double.MaxValue && Math.Sqrt(best) <= r * Cell) break;
                }
                if (best == double.MaxValue) best = all.Min(q => (q[0] - p[0]) * (q[0] - p[0]) + (q[1] - p[1]) * (q[1] - p[1]) + (q[2] - p[2]) * (q[2] - p[2]));
                return Math.Sqrt(best);
            }
        }
    }
}
