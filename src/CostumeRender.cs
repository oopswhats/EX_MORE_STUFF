using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ExMoreStuff
{
    // A costume drawn without the game: the fighter in the stance (CostumePose), lit like the training stage, as SF4
    // Ember's gallery pictures are made (a port of usf4-nesica's render_usf4_stylized.py, drawn on the CPU here: EX More
    // Stuff carries no graphics library). From the game's shaders (Steam SSFIV.exe; notes in that renderer):
    // - SpcBumpBrush*_PS: brush albedo C*C + sat(Brush.x * NdotL - Brush.y) * (C - C*C), brush rim, the stage's key light
    //   with self-shadow (a shadow map from the key light), hemisphere ambient, a second light, specular, normal maps;
    // - the special materials (emission, environment maps, velvet, oil, ...) at rest;
    // - the stage's glare (bloom) and colour filter over the picture.
    // Colors 11 and 12 have no files of their own: the game draws color 1's (loader 0x5e5cf0) in a style (notes in
    // ~/SF4-rea/notes/stylized-colours.md):
    // - 11, dark rim: the same shaders with Brush_PS c98 and RimLight_PS c60 set per fighter (setupEdgeFilterParams 0x564cb0);
    // - 12, ink hatching: each material's technique swapped to Hatching_W and friends (0x53b5e0), tam.emz's tonal art
    //   map in screen space (Hatching_PS).
    static class CostumeRender
    {
        const double FieldOfView = 24.0;
        public const double YawDegrees = 90.0;   // character select's turn: the fighter faces the camera in the stance
        const int Supersample = 3, ShadowSize = 2048;
        public const int Colors = 12;   // 11 and 12 (color 1 in the game's two styles) for View... only: packages hold 01-10

        // drawing takes half the PC's cores at most: the player's PC stays usable (and a game beside it)
        static readonly ParallelOptions Cores = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };

        // ---------- the training stage (STG_TRN.emz TRN_SetupParam.lua) ----------

        static double[] LightDir(double x, double y, double z)
        {
            double a = x * Math.PI / 180, b = y * Math.PI / 180, c = z * Math.PI / 180;
            double sx = Math.Sin(a), cx = Math.Cos(a), sy = Math.Sin(b), cy = Math.Cos(b), sz = Math.Sin(c), cz = Math.Cos(c);
            // (0, -1, 0) times the row-vector R = Rx Ry Rz: minus its second row
            double[] d = { -(sy * sx * cz - sz * cx), -(sy * sx * sz + cz * cx), -(cy * sx) };
            double n = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
            return new[] { d[0] / n, d[1] / n, d[2] / n };
        }

        static readonly double[] MainDir = LightDir(20.0, -180.0, -1.0), SubDir = LightDir(90.0, 65.0, -8.9);
        static readonly double[] Dif0 = { 0.62 * 1.5, 0.62 * 1.5, 0.62 * 1.5 }, Spc0 = { 0.16 * 0.3, 0.16 * 0.3, 0.16 * 0.3 }, Dif1 = { 0.715 * 1.3, 0.595 * 1.3, 0.462 * 1.3 };
        static readonly double[] HemiA, HemiB, HemiC, Rim;
        static readonly double[] SelfShadow = { 0.0, 0.061, 0.076 };
        static readonly double[] FilterLo = { -0.4, -0.1, -0.3 }, FilterHi = { 0.6, 1.0, 1.6 }, FilterBlend = { 0.18, 0.5, 0.5 };
        static readonly double[] Tone = { 0.72 * 1.3, 0.807 * 1.3, 0.647 * 1.3, 0.5 };
        const double BackbufferHeight = 720;
        static readonly double GameScreenHeight = 2 * 4.25 * Math.Tan(20.0 * Math.PI / 180);

        static CostumeRender()
        {
            double[] u = { 0.5 * 0.5, 0.65 * 0.5, 0.75 * 0.5 }, m = { 0.235 * 3, 0.29 * 3, 0.45 * 3 }, l = { 0.4 * 1.5, 0.4 * 1.5, 0.41 * 1.5 };
            HemiA = Enumerable.Range(0, 3).Select(i => (u[i] + l[i] - 2 * m[i]) / 4).ToArray();
            HemiB = Enumerable.Range(0, 3).Select(i => (u[i] - l[i]) / 2).ToArray();
            HemiC = Enumerable.Range(0, 3).Select(i => (u[i] + l[i] + 2 * m[i]) / 4).ToArray();
            double f = (1.0 - MainDir[2]) * 0.5;   // the battle camera looks along +z
            Rim = new[] { 0.473 * f, 0.321 * f, 0.098 * f, f };
        }

        // color 11's c98 (0x564cb0) and c60
        static double[] Brush11(string fighter)
        {
            if (fighter == "DCP") return new[] { 8.0, 4, 10, 4 };
            if (fighter == "HUG") return new[] { 8.0, 4, 5, 3 };
            return new[] { 8.0, 4, 6, 3 };
        }

        static readonly double[] Rim11 = { 0.0, 0.0, 0.2, 1.0 };

        // color 12's technique per material (0x53b5e0): 0 the material's own, else Hatching, Hatching with alpha 1, HatchingOil
        const int Hatching = 12, HatchingAlpha1 = 13, HatchingOil = 14;

        static int HatchingTechnique(string fighter, string shader)
        {
            if (shader.Contains("Emission") || shader.Contains("QLOC_GLARE") || shader.Contains("Eye")) return 0;
            if (shader.Contains("NoAlpha")) return HatchingAlpha1;   // Hatching_Mask
            if (fighter == "BOS") return shader.Contains("Nega") || shader.Contains("Env") ? HatchingAlpha1 : Hatching;
            if (fighter == "RYX" || fighter == "GKX")
            {
                if (shader.Contains("BrushEmiMaskConst")) return 0;
                return shader.Contains("BrushEmi") ? HatchingAlpha1 : Hatching;
            }
            if (fighter == "HKN")
            {
                if (shader.Contains("Scroll")) return 0;
                return shader.Contains("Oil") ? HatchingOil : Hatching;
            }
            return Hatching;   // MKT _Col_ and ROS Halo: as Hatching at rest
        }

        // The hatching strokes are tiled in the game's screen: a 40 degree field of view on 16:9 at the round-start distance
        const double GameDistance = 4.25;
        static readonly double GameTanY = Math.Tan(20.0 * Math.PI / 180);

        // ---------- textures ----------

        class Texture
        {
            readonly List<float[]> levels = new List<float[]>();   // R, G, B, A in 0..1, mip 0 first
            readonly List<int> widths = new List<int>(), heights = new List<int>();

            public Texture(byte[] bgra, int width, int height, bool mips)
            {
                var top = new float[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    top[i * 4] = bgra[i * 4 + 2] / 255f; top[i * 4 + 1] = bgra[i * 4 + 1] / 255f;
                    top[i * 4 + 2] = bgra[i * 4] / 255f; top[i * 4 + 3] = bgra[i * 4 + 3] / 255f;
                }
                levels.Add(top); widths.Add(width); heights.Add(height);
                while (mips && (width > 1 || height > 1))   // a 2x2 box each level, as glGenerateMipmap
                {
                    int w = Math.Max(1, width / 2), h = Math.Max(1, height / 2);
                    float[] src = levels[levels.Count - 1], dst = new float[w * h * 4];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            for (int c = 0; c < 4; c++)
                            {
                                int x0 = Math.Min(x * 2, width - 1), x1 = Math.Min(x * 2 + 1, width - 1), y0 = Math.Min(y * 2, height - 1), y1 = Math.Min(y * 2 + 1, height - 1);
                                dst[(y * w + x) * 4 + c] = (src[(y0 * width + x0) * 4 + c] + src[(y0 * width + x1) * 4 + c] + src[(y1 * width + x0) * 4 + c] + src[(y1 * width + x1) * 4 + c]) * 0.25f;
                            }
                    levels.Add(dst); widths.Add(w); heights.Add(h);
                    width = w; height = h;
                }
            }

            public int Width { get { return widths[0]; } }
            public int Height { get { return heights[0]; } }

            // bilinear at one level, repeating (or clamped)
            public void Bilinear(int level, double u, double v, bool repeat, double[] o)
            {
                int w = widths[level], h = heights[level];
                float[] t = levels[level];
                double x = u * w - 0.5, y = v * h - 0.5;
                int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
                double fx = x - x0, fy = y - y0;
                int ax = Wrap(x0, w, repeat), bx = Wrap(x0 + 1, w, repeat), ay = Wrap(y0, h, repeat), by = Wrap(y0 + 1, h, repeat);
                for (int c = 0; c < 4; c++)
                {
                    double top = t[(ay * w + ax) * 4 + c] * (1 - fx) + t[(ay * w + bx) * 4 + c] * fx;
                    double bottom = t[(by * w + ax) * 4 + c] * (1 - fx) + t[(by * w + bx) * 4 + c] * fx;
                    o[c] = top * (1 - fy) + bottom * fy;
                }
            }

            static int Wrap(int i, int n, bool repeat) { return repeat ? ((i % n) + n) % n : (i < 0 ? 0 : i >= n ? n - 1 : i); }

            // trilinear at the level of detail `lod` (texels per pixel, log2), as LINEAR_MIPMAP_LINEAR
            public void Sample(double u, double v, double lod, double[] o, double[] scratch)
            {
                if (levels.Count == 1 || lod <= 0) { Bilinear(0, u, v, true, o); return; }
                int max = levels.Count - 1;
                if (lod >= max) { Bilinear(max, u, v, true, o); return; }
                int l0 = (int)Math.Floor(lod);
                double f = lod - l0;
                Bilinear(l0, u, v, true, o);
                Bilinear(l0 + 1, u, v, true, scratch);
                for (int c = 0; c < 4; c++) o[c] = o[c] * (1 - f) + scratch[c] * f;
            }
        }

        static Texture Decode(byte[] dds, bool mips = true)
        {
            int w, h;
            byte[] px = GameArt.DecodePixels(dds, out w, out h);
            return px != null ? new Texture(px, w, h, mips) : null;
        }

        // the six faces (+X, -X, +Y, -Y, +Z, -Z) of a DXT cube map, top level
        static Texture[] Cube(byte[] dds)
        {
            int height = BitConverter.ToInt32(dds, 12), width = BitConverter.ToInt32(dds, 16), mips = Math.Max(1, BitConverter.ToInt32(dds, 28));
            int block = Encoding.ASCII.GetString(dds, 84, 4) == "DXT1" ? 8 : 16;
            Func<int, int, int> level = (w, h) => Math.Max(1, w / 4) * Math.Max(1, h / 4) * block;
            int faceSize = Enumerable.Range(0, mips).Sum(m => level(Math.Max(1, width >> m), Math.Max(1, height >> m)));
            var faces = new Texture[6];
            for (int f = 0; f < 6; f++)
            {
                var one = new byte[128 + level(width, height)];
                Buffer.BlockCopy(dds, 0, one, 0, 128);
                BitConverter.GetBytes(1).CopyTo(one, 28);   // one mip
                BitConverter.GetBytes(0).CopyTo(one, 112);  // not a cube
                Buffer.BlockCopy(dds, 128 + f * faceSize, one, 128, level(width, height));
                faces[f] = Decode(one, false);
            }
            return faces;
        }

        // a cube map lookup (OpenGL's face and coordinate rules), linear
        static void CubeSample(Texture[] faces, double x, double y, double z, double[] o)
        {
            double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z), sc, tc, ma;
            int face;
            if (ax >= ay && ax >= az) { face = x > 0 ? 0 : 1; sc = x > 0 ? -z : z; tc = -y; ma = ax; }
            else if (ay >= az) { face = y > 0 ? 2 : 3; sc = x; tc = y > 0 ? z : -z; ma = ay; }
            else { face = z > 0 ? 4 : 5; sc = z > 0 ? x : -x; tc = -y; ma = az; }
            if (ma == 0 || faces[face] == null) { o[0] = o[1] = o[2] = 0; o[3] = 1; return; }
            faces[face].Bilinear(0, (sc / ma + 1) / 2, (tc / ma + 1) / 2, false, o);
        }

        // ---------- what the game gives every picture ----------

        class Stage
        {
            public Texture[] Cube;
            public Texture Tam;   // tam.emz, color 12's strokes
            public Dictionary<string, HashSet<string>> Techniques = new Dictionary<string, HashSet<string>>();   // technique -> /MtxP set
        }

        static readonly Dictionary<string, Stage> stages = new Dictionary<string, Stage>(StringComparer.OrdinalIgnoreCase);

        static Stage StageOf(string game)
        {
            lock (stages)
            {
                Stage stage;
                if (stages.TryGetValue(game, out stage)) return stage;
                stage = new Stage();
                // Ember's pictures are on the training stage: STG_TRN.tex.emz holds TRN_CUBE.emb (0x5f0d10 loads "%s_CUBE.emb")
                string trn = Path.Combine(game, "resource", "battle", "stage", "STG_TRN.tex.emz");
                if (File.Exists(trn))
                {
                    var inner = GameArt.ReadContainer(GameArt.Unpack(File.ReadAllBytes(trn)));
                    if (inner.Count > 1)
                    {
                        var cube = GameArt.ReadContainer(inner[1].Value);
                        if (cube.Count > 0) stage.Cube = Cube(cube[0].Value);
                    }
                }
                // HatchingTexture binds it linear, no mips
                string tam = Path.Combine(game, "resource", "battle", "chara_ex", "hatching", "tam.emz");
                if (File.Exists(tam))
                {
                    var inner = GameArt.ReadContainer(GameArt.Unpack(File.ReadAllBytes(tam)));
                    if (inner.Count > 0) stage.Tam = Decode(inner[0].Value, false);
                }
                // which techniques sample the self-shadow map (/MtxP WLP_SM in setting_technique.emz)
                string techniques = Path.Combine(game, "dlc", "04_ae2", "shader", "setting_technique.emz");
                if (File.Exists(techniques))
                {
                    string text = Encoding.GetEncoding(28591).GetString(GameArt.Unpack(File.ReadAllBytes(techniques)));
                    foreach (Match m in Regex.Matches(text, @"[\x20-\x7e\t]{6,}"))
                    {
                        var words = m.Value.Replace('\t', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        int at = Array.IndexOf(words, "/MtxP");
                        if (words.Length > 3 && at >= 0) stage.Techniques[words[0]] = new HashSet<string>(words.Skip(at + 1));
                    }
                }
                return stages[game] = stage;
            }
        }

        // ---------- materials (.obj.emm) ----------

        class Material
        {
            public double Cut;
            public bool Blend;
            public string Shader = "";
            public double[] Brush = { 2, 1, 2.6, 1.5 };
            public Dictionary<string, double> Params = new Dictionary<string, double>();
            public double Get(string key, double fallback) { double v; return Params.TryGetValue(key, out v) ? v : fallback; }
            public double[] Vec(string prefix, string names, params double[] fallback)
            {
                return names.Select((ch, i) => Get(prefix + ch, fallback[i])).ToArray();
            }
        }

        static Dictionary<string, Material> ReadEmm(byte[] d)
        {
            // the game finds materials whatever the case
            var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            if (d == null || d.Length < 16 || Encoding.ASCII.GetString(d, 0, 4) != "#EMM") return materials;
            // read as far as the file goes: some skins' files are cut short (2026-10-25: SF5 Inspired Cammy's last material
            // ends 40 bytes past the end, and the costume wasn't drawn at all); the game plays them
            int b = BitConverter.ToInt32(d, 0x0C);
            if (b < 0 || b + 4 > d.Length) return materials;
            for (int i = 0; i < BitConverter.ToInt32(d, b) && b + 8 + 4 * i <= d.Length; i++)
            {
                int offset = BitConverter.ToInt32(d, b + 4 + 4 * i), m = b + offset;
                if (offset <= 0 || m + 68 > d.Length) continue;
                var mat = new Material { Shader = Name(d, m + 32) };
                for (int k = 0; k < BitConverter.ToUInt16(d, m + 64) && m + 68 + 40 * (k + 1) <= d.Length; k++)
                {
                    int p = m + 68 + 40 * k;   // name[32], type (1 = int), value
                    mat.Params[Name(d, p)] = BitConverter.ToUInt16(d, p + 32) == 1 ? BitConverter.ToUInt32(d, p + 36) : (double)BitConverter.ToSingle(d, p + 36);
                }
                mat.Cut = mat.Get("AlphaTest", 0) / 255;
                if (mat.Cut == 0 && mat.Shader.Contains("AlphaMask")) mat.Cut = 0.5;
                mat.Blend = mat.Get("AlphaBlend", 0) != 0;
                mat.Brush = new[] { mat.Get("BrushA", 2), mat.Get("BrushB", 1), mat.Get("BrushC", 2.6), mat.Get("BrushD", 1.5) };
                materials[Name(d, m)] = mat;
            }
            return materials;
        }

        static string Name(byte[] d, int at)
        {
            int end = at;
            while (end < d.Length && end - at < 32 && d[end] != 0) end++;
            return Encoding.GetEncoding(28591).GetString(d, at, end - at);
        }

        // material -> index into <CHR>_<NN>.nml.emb (after the colour's own textures): the second layer of its EMG texture entry
        static Dictionary<string, int> NormalLayers(byte[] d)
        {
            var o = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < BitConverter.ToUInt16(d, 0x20); i++)
            {
                int h = 0x20 + BitConverter.ToInt32(d, 0x28 + i * 4) + 0x20;
                int texCount = BitConverter.ToInt32(d, h + 4), texList = BitConverter.ToInt32(d, h + 12), subCount = BitConverter.ToUInt16(d, h + 26), subList = BitConverter.ToInt32(d, h + 28);
                var entries = new int?[texCount];
                for (int t = 0; t < texCount; t++)
                {
                    // a slot pointing past the end is skipped (Raiden for Bison's cape lists 6 slots, 1 real; it uses that one)
                    if (h + texList + t * 4 + 4 > d.Length) break;
                    int e = h + BitConverter.ToInt32(d, h + texList + t * 4);
                    if (e < 0 || e + 4 + 12 + 2 > d.Length) continue;
                    if (BitConverter.ToUInt16(d, e) > 1) entries[t] = d[e + 4 + 12 + 1];
                }
                for (int s = 0; s < subCount; s++)
                {
                    int p = h + BitConverter.ToInt32(d, h + subList + s * 4) + 0x10;
                    int entry = BitConverter.ToUInt16(d, p);
                    if (entry < texCount && entries[entry] != null) o[Name(d, p + 6)] = entries[entry].Value;
                }
            }
            return o;
        }

        // ---------- the special materials ----------

        // c95 (Elapsed_PS) at rest is zero, but for Hakan's oil and Evil Ryu's pulse
        static double[] RestC95(string fighter)
        {
            if (fighter == "HKN") return new[] { 0.5, 0.1, 0.3, 0.0 };
            if (fighter == "RYX") return new[] { 0.6, 0.0, 0.0, 0.0 };
            return new double[4];
        }

        const int Emission = 1, T1 = 2, Emi = 3, Env = 4, Nega = 5, Velvet = 6, ColorShadow = 7, Oil = 8;
        const int RimBrush = 0, RimSquare = 1, RimNone = 2;

        class Look { public int Special, RimKind; public double[] P0 = new double[4], P1 = new double[4]; }

        static Look SpecialMaterial(string fighter, string shader, Material m)
        {
            double[] c95 = RestC95(fighter);
            double[] reflection = { m.Get("ReflectCoeff", 0), m.Get("ReflectFresnelBias", 0), m.Get("ReflectFresnelCoeff", 1), 0 };
            if (shader == "T1_C_Scroll_Emission_W") return new Look { Special = T1, RimKind = RimNone };
            if (shader == "Emission_W" || shader == "EmissionScroll_W" || shader == "EmissionNega_W")
                return new Look { Special = Emission, P0 = m.Vec("MatScale1", "XYZW", 1, 1, 1, 1), P1 = new[] { shader == "EmissionNega_W" ? 1.0 : 0, 0, 0, 0 }, RimKind = RimNone };
            if (shader.StartsWith("SpcBumpBrushEmi") && shader.EndsWith("_UV1_W"))
            {
                string kind = shader.Substring("SpcBumpBrushEmi".Length, shader.Length - "SpcBumpBrushEmi".Length - "_UV1_W".Length);
                double k;
                if (kind.EndsWith("Const")) k = 1.0;
                else if (kind.Length > 0 && char.IsDigit(kind[kind.Length - 1])) k = c95[Math.Min(3, kind[kind.Length - 1] - '0')];
                else k = Math.Min(Math.Max(c95.Sum(), 0.0), 1.0);
                double darken = kind.StartsWith("Mask") || kind.StartsWith("AlphaConst") ? 0.0 : 1.0;
                return new Look { Special = Emi, P0 = m.Vec("MatScale0", "XYZW", 1, 1, 1, 1), P1 = new[] { k, darken, kind == "AlphaConst" ? 1.0 : 0, 0 }, RimKind = RimBrush };
            }
            if (shader == "SpcEnv_W" || shader == "SpcEnvAlpha_W" || shader == "SpcBrushEnv_W" || shader == "SpcEyeEnv_W" || shader == "SpcEyeEnvSSS_W")
            {
                int kind = shader == "SpcEnv_W" ? 0 : shader == "SpcEnvAlpha_W" ? 1 : shader == "SpcBrushEnv_W" ? 2 : 3;
                return new Look { Special = Env, P0 = reflection, P1 = new double[] { kind, 0, 0, 0 }, RimKind = new[] { RimSquare, RimSquare, RimBrush, RimNone }[kind] };
            }
            if (shader == "SpcBumpBrushNega_UV1_W")
                return new Look { Special = fighter == "BOS" ? 0 : Nega, RimKind = RimBrush };   // Seth drawn standard, as Ember's pictures
            if (shader == "SpcBumpBrushVelvet_UV1_W") return new Look { Special = Velvet, P0 = m.Vec("MatCol0", "RGBA", 1, 1, 1, 1), RimKind = RimBrush };
            if (shader == "SpcBumpBrushColor_UV1_W") return new Look { Special = ColorShadow, P0 = m.Vec("MatScale0", "XYZW", 1, 1, 1, 1), RimKind = RimBrush };
            if (shader.StartsWith("SpcBumpBrushOilSSS")) return new Look { Special = Oil, P0 = reflection, RimKind = RimBrush };
            if (shader == "SpcBumpNoRim_UV1_W") return new Look { RimKind = RimNone };
            if (shader.StartsWith("SpcBump_") || shader.StartsWith("Dif_")) return new Look { RimKind = RimSquare };   // no Brush_PS
            if (shader.Contains("Eye")) return new Look { RimKind = RimNone };
            return new Look { RimKind = RimBrush };
        }

        // ---------- drawing ----------

        // one submesh ready to draw, in the order the renderer draws
        class Draw
        {
            public CostumePose.Part Part;
            public int[] Triangles;
            public Texture Texture, Normals;
            public Material Material;
            public Look Look;
            public double Cut;
            public bool Blend, Cull, Sss, Shadowed, GlareOn;
            public int Hatch;   // color 12: Hatching, HatchingAlpha1, HatchingOil, or 0
            public double[] Brush, RimColor, GlareScale, MatDif, MatAmb, MatSpc, MatSss, MatSpec;
        }

        // per-vertex data in the picture's frame
        class Frame
        {
            public double[][] Pos, Nrm, Clip;   // per part: 3, 3, 4 per vertex
            public double[][] Ripple;
            public double[][] Game;   // color 12: per part, game screen x, y and w per vertex
        }

        static double[] MulPoint(double[] m, double x, double y, double z)   // column-vector 4x4 times (x, y, z, 1)
        {
            return new[] { m[0] * x + m[1] * y + m[2] * z + m[3], m[4] * x + m[5] * y + m[6] * z + m[7], m[8] * x + m[9] * y + m[10] * z + m[11], m[12] * x + m[13] * y + m[14] * z + m[15] };
        }

        static double[] MatMul(double[] a, double[] b)   // column-vector convention, row-major storage
        {
            var m = new double[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) for (int k = 0; k < 4; k++) m[r * 4 + c] += a[r * 4 + k] * b[k * 4 + c];
            return m;
        }

        static double[] LookAt(double[] eye, double[] target)
        {
            double[] f = Norm(Sub(target, eye)), right = Norm(Cross(f, new[] { 0.0, 1, 0 })), up = Cross(right, f);
            return new[]
            {
                right[0], right[1], right[2], -Dot(right, eye),
                up[0], up[1], up[2], -Dot(up, eye),
                -f[0], -f[1], -f[2], Dot(f, eye),
                0, 0, 0, 1,
            };
        }

        static double[] Perspective(double fov, double aspect, double near, double far)
        {
            double f = 1 / Math.Tan(fov * Math.PI / 360);
            return new[] { f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) / (near - far), 2 * far * near / (near - far), 0, 0, -1, 0 };
        }

        static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        static double[] Sub(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        static double[] Cross(double[] a, double[] b) { return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] }; }
        static double[] Norm(double[] v) { double n = Math.Sqrt(Dot(v, v)); return new[] { v[0] / n, v[1] / n, v[2] / n }; }
        static double Sat(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static void Normalize(double[] v) { double n = Math.Sqrt(Dot(v, v)); v[0] /= n; v[1] /= n; v[2] /= n; }
        static void CrossInto(double[] a, double[] b, double[] o) { o[0] = a[1] * b[2] - a[2] * b[1]; o[1] = a[2] * b[0] - a[0] * b[2]; o[2] = a[0] * b[1] - a[1] * b[0]; }

        // the pixel shader's working arrays, one set per drawing thread (none made a sample)
        class Scratch
        {
            [ThreadStatic] static Scratch mine;
            public static Scratch Get() { return mine ?? (mine = new Scratch()); }
            public readonly double[] Out = new double[8], C = new double[4], Tap = new double[4], Nml = new double[4], Env = new double[4];
            public readonly double[] P = new double[3], N = new double[3], View = new double[3], R = new double[3], A = new double[3], B = new double[3], T = new double[3], Bt = new double[3];
            public readonly double[] Texel = new double[3], Shade = new double[3], Albedo = new double[3], Light = new double[3], Glow = new double[3], Lit = new double[3];
        }

        // numpy's percentile (linear between the two nearest ranks)
        static double Percentile(List<double> sorted, double q)
        {
            double at = q / 100 * (sorted.Count - 1);
            int lo = (int)Math.Floor(at), hi = Math.Min(lo + 1, sorted.Count - 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (at - lo);
        }

        /// <summary>The posed costume in one color as the training stage lights it: an RGBA picture `width` x `height`,
        /// the fighter framed in it, transparent around them. supersample: samples across a pixel (3; 1 for a quick look), shadowSize:
        /// the self-shadow map's.</summary>
        static float[] Picture(string game, string fighter, List<CostumePose.Part> parts, List<Texture> textures, Dictionary<string, Material> materials,
                               List<Texture> normals, Dictionary<string, int> layers, int width, int height, int style, double yawDegrees,
                               int supersample = Supersample, int shadowSize = ShadowSize)
        {
            Stage stage = StageOf(game);
            double yaw = yawDegrees * Math.PI / 180, cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            // row vector times rot = [[c, 0, -s], [0, 1, 0], [s, 0, c]]; rot's transpose takes it back (to the game's frame)
            Func<double, double, double, double[]> turn = (x, y, z) => new[] { x * cy + z * sy, y, -x * sy + z * cy };
            Func<double[], double[]> back = v => new[] { v[0] * cy - v[2] * sy, v[1], v[0] * sy + v[2] * cy };
            var frame = new Frame { Pos = new double[parts.Count][], Nrm = new double[parts.Count][], Clip = new double[parts.Count][], Ripple = new double[parts.Count][] };
            var xs = new List<double>(); var ys = new List<double>(); var zs = new List<double>();
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                int n = part.Positions.Length / 3;
                frame.Pos[i] = new double[n * 3]; frame.Nrm[i] = new double[n * 3]; frame.Ripple[i] = new double[n];
                for (int v = 0; v < n; v++)
                {
                    double[] p = turn(part.Positions[v * 3], part.Positions[v * 3 + 1], part.Positions[v * 3 + 2]);
                    double[] q = turn(part.Normals[v * 3], part.Normals[v * 3 + 1], part.Normals[v * 3 + 2]);
                    Array.Copy(p, 0, frame.Pos[i], v * 3, 3); Array.Copy(q, 0, frame.Nrm[i], v * 3, 3);
                    // SpcBumpOilSSS VS: sin(frac(pos.y * 3.998 + 0.5) * 2pi - pi) at time 0
                    double r = part.Positions[v * 3 + 1] * 3.99797201 + 0.5;
                    frame.Ripple[i][v] = Math.Sin((r - Math.Floor(r)) * 6.28318548 - 3.14159274);
                }
                foreach (int v in part.Subs.SelectMany(s => s.Triangles).Distinct())   // frame what is drawn
                {
                    xs.Add(frame.Pos[i][v * 3]); ys.Add(frame.Pos[i][v * 3 + 1]); zs.Add(frame.Pos[i][v * 3 + 2]);
                }
            }
            if (xs.Count == 0) return new float[width * height * 4];
            xs.Sort(); ys.Sort(); zs.Sort();
            // the outermost points left out: a stray object far away mustn't shrink the fighter
            double[] lo = { Percentile(xs, 0.2), Percentile(ys, 0.2), Percentile(zs, 0.2) }, hi = { Percentile(xs, 99.8), Percentile(ys, 99.8), Percentile(zs, 99.8) };
            double[] center = { (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2 };
            double aspect = (double)width / height;
            double half = Math.Max((hi[1] - lo[1]) / 2, (hi[0] - lo[0]) / 2 / aspect) * 1.06;
            double distance = half / Math.Tan(FieldOfView * Math.PI / 360) + (hi[2] - lo[2]) / 2;
            double[] eye = { center[0], center[1] + (hi[1] - lo[1]) * 0.12, center[2] - distance };
            double[] viewMatrix = LookAt(eye, center);
            double[] mvp = MatMul(Perspective(FieldOfView, aspect, distance * 0.3, distance * 3), viewMatrix);
            if (style == 12)
            {
                double[] vc = MulPoint(viewMatrix, center[0], center[1], center[2]);
                double gx = 1 / (GameDistance * GameTanY * 16 / 9), gy = 1 / (GameDistance * GameTanY);
                frame.Game = frame.Pos.Select(pos => Enumerable.Range(0, pos.Length / 3).SelectMany(v =>
                {
                    double[] r = MulPoint(viewMatrix, pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]);
                    double rx = r[0] - vc[0], ry = r[1] - vc[1], gw = GameDistance - (r[2] - vc[2]);   // view space looks down -z
                    return new[] { rx * gx * GameDistance / gw, ry * gy * GameDistance / gw, gw };
                }).ToArray()).ToArray();
            }
            double[] l0 = turn(-MainDir[0], -MainDir[1], -MainDir[2]), l1 = turn(-SubDir[0], -SubDir[1], -SubDir[2]);
            // the self-shadow: an orthographic shadow map from the key light over the fighter
            double radius = Math.Sqrt(Dot(Sub(hi, lo), Sub(hi, lo))) / 2;
            double[] lightView = LookAt(new[] { center[0] + l0[0] * radius * 2, center[1] + l0[1] * radius * 2, center[2] + l0[2] * radius * 2 }, center);
            double[] ortho = { 1 / radius, 0, 0, 0, 0, 1 / radius, 0, 0, 0, 0, -1 / (2 * radius), -1, 0, 0, 0, 1 };
            double[] lightMvp = MatMul(ortho, lightView);

            var shadow = new float[shadowSize * shadowSize];
            for (int i = 0; i < shadow.Length; i++) shadow[i] = 1;
            var shadowDraws = new List<Tuple<int, int[]>>();
            for (int i = 0; i < parts.Count; i++)
                foreach (var sub in parts[i].Subs)
                {
                    Material m;
                    if (sub.Triangles.Length > 0 && !(materials.TryGetValue(sub.Material, out m) && m.Blend)) shadowDraws.Add(Tuple.Create(i, sub.Triangles));
                }
            var lightClip = frame.Pos.Select(pos => Enumerable.Range(0, pos.Length / 3).SelectMany(v => MulPoint(lightMvp, pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2])).ToArray()).ToArray();
            Rasterize(shadowSize, shadowSize, shadowDraws.Count, d => lightClip[shadowDraws[d].Item1], d => shadowDraws[d].Item2,
                      (d, x, y, z, b, tri) => { int at = y * shadowSize + x; if (z < shadow[at]) shadow[at] = (float)z; });
            var shadowMap = new ShadowMap { Depth = shadow, Size = shadowSize };

            for (int i = 0; i < parts.Count; i++)
                frame.Clip[i] = Enumerable.Range(0, frame.Pos[i].Length / 3).SelectMany(v => MulPoint(mvp, frame.Pos[i][v * 3], frame.Pos[i][v * 3 + 1], frame.Pos[i][v * 3 + 2])).ToArray();

            // what to draw: the opaque submeshes, then the alpha-blended ones (hair cards, lashes) over them
            var opaque = new List<Draw>(); var blended = new List<Draw>();
            for (int i = 0; i < parts.Count; i++)
                foreach (var sub in parts[i].Subs)
                {
                    // the game's texture list is the color's textures, then the .nml.emb's: some skins keep whole parts' textures
                    // in the .nml.emb (user 2026-10-23: Ghoul Yang's head, mask and weapons were left out)
                    int tex = sub.Texture;
                    Texture texture = tex < textures.Count ? textures[tex] : tex - textures.Count < normals.Count ? normals[tex - textures.Count] : null;
                    if (sub.Triangles.Length == 0 || texture == null) continue;
                    Material m;
                    if (!materials.TryGetValue(sub.Material, out m)) m = new Material();
                    int layer, hatch = style == 12 ? HatchingTechnique(fighter, m.Shader) : 0;
                    bool dark = style == 11 && !m.Shader.Contains("Eye");
                    int nml = layers.TryGetValue(sub.Material, out layer) ? layer - textures.Count : -1;
                    var draw = new Draw
                    {
                        Part = parts[i], Triangles = sub.Triangles, Texture = texture, Material = m, Look = hatch != 0 ? new Look() : SpecialMaterial(fighter, m.Shader, m),
                        Hatch = hatch, Brush = dark ? Brush11(fighter) : m.Brush, RimColor = dark ? Rim11 : Rim,
                        Blend = m.Blend, Cut = m.Blend ? Math.Max(m.Cut, 0.02) : m.Cut,
                        // EmMaterial::applyRenderStates 0x6ce380: back faces culled unless BackFace or TwoSidedRender
                        Cull = m.Get("BackFace", 0) == 0 && m.Get("TwoSidedRender", 0) == 0,
                        Normals = m.Shader.Contains("Bump") && nml >= 0 && nml < normals.Count ? normals[nml] : null,
                        Sss = m.Shader.Contains("SSS"),
                        Shadowed = stage.Techniques.ContainsKey(m.Shader) && stage.Techniques[m.Shader].Contains("WLP_SM"),
                        GlareOn = m.Get("Glare", 0) != 0, GlareScale = m.Vec("MatScale0", "XYZW", 1, 1, 1, 1),
                        MatDif = m.Vec("MatDif", "RGB", 1, 1, 1), MatAmb = m.Vec("MatAmb", "RGB", 1, 1, 1), MatSpc = m.Vec("MatSpc", "RGB", 0, 0, 0), MatSss = m.Vec("Sss", "RGB", 0, 0, 0),
                        MatSpec = new[] { m.Get("SpcCoeff", 0), m.Get("SpcPower", 1) },
                    };
                    (draw.Blend ? blended : opaque).Add(draw);
                }
            var draws = opaque.Concat(blended).ToList();
            var partIndex = new Dictionary<CostumePose.Part, int>();
            for (int i = 0; i < parts.Count; i++) partIndex[parts[i]] = i;

            int sw = width * supersample, sh = height * supersample;
            var color = new float[sw * sh * 4];
            var glare = new float[sw * sh * 4];
            var depth = new float[sw * sh];
            for (int i = 0; i < depth.Length; i++) depth[i] = 1;
            var ctx = new Shading { Eye = eye, L0 = l0, L1 = l1, Back = back, C95 = RestC95(fighter), Cube = stage.Cube, Tam = stage.Tam, Shadow = shadowMap, LightMvp = lightMvp };
            Rasterize(sw, sh, draws.Count, d => frame.Clip[partIndex[draws[d].Part]], d => draws[d].Triangles,
                      (d, x, y, z, b, tri) =>
                      {
                          int at = y * sw + x;
                          Draw draw = draws[d];
                          if (z >= depth[at]) return;
                          int part = partIndex[draw.Part];
                          double[] o = Scratch.Get().Out;
                          if (!ctx.Shade(draw, frame, part, tri, b, x, y, sw, sh, o)) return;
                          if (!draw.Blend)
                          {
                              depth[at] = (float)z;
                              for (int c = 0; c < 4; c++) { color[at * 4 + c] = (float)Sat(o[c]); glare[at * 4 + c] = (float)Sat(o[4 + c]); }
                              // an opaque material covers its pixel whatever its texture's alpha: some skins leave the alpha
                              // empty where they're opaque (user 2026-10-18: Terry Bogard as floating pieces), and the game
                              // ignores it there (PerfTest.Solid does the same for the game's pictures)
                              color[at * 4 + 3] = 1;
                          }
                          else
                          {
                              double a = Sat(o[3]), ga = Sat(o[7]);
                              for (int c = 0; c < 3; c++)
                              {
                                  color[at * 4 + c] = (float)(Sat(o[c]) * a + color[at * 4 + c] * (1 - a));
                                  glare[at * 4 + c] = (float)(Sat(o[4 + c]) * ga + glare[at * 4 + c] * (1 - ga));
                              }
                              color[at * 4 + 3] = (float)(a + color[at * 4 + 3] * (1 - a));
                              glare[at * 4 + 3] = (float)(ga + glare[at * 4 + 3] * (1 - ga));
                          }
                      }, draws.Select(dr => dr.Cull).ToArray(), (d, tri) => ctx.Setup(draws[d], frame, partIndex[draws[d].Part], tri, sw, sh));

            float[] image = supersample > 1 ? Downscale(color, sw, sh, width, height) : color;   // one sample a pixel: as it is
            float[] glareImage = supersample > 1 ? BoxDown(glare, sw, sh, width, height) : glare;
            image = AddGlare(image, glareImage, width, height, 2 * half / GameScreenHeight);
            for (int i = 0; i < width * height; i++)   // the stage's colour filter
                for (int c = 0; c < 3; c++)
                {
                    double v = image[i * 4 + c];
                    image[i * 4 + c] = (float)Sat(v + FilterBlend[c] * (Sat((v - FilterLo[c]) / (FilterHi[c] - FilterLo[c])) - v));
                }
            return image;
        }

        class ShadowMap
        {
            public float[] Depth;
            public int Size;

            // bilinear depth, clamped
            public double At(double u, double v)
            {
                double x = u * Size - 0.5, y = v * Size - 0.5;
                int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
                double fx = x - x0, fy = y - y0;
                return (D(x0, y0) * (1 - fx) + D(x0 + 1, y0) * fx) * (1 - fy) + (D(x0, y0 + 1) * (1 - fx) + D(x0 + 1, y0 + 1) * fx) * fy;
            }

            double D(int i, int j) { return Depth[Math.Min(Math.Max(j, 0), Size - 1) * Size + Math.Min(Math.Max(i, 0), Size - 1)]; }
        }

        // Triangles into a w x h target in bands of rows at once (each band draws every triangle in order, so blending
        // keeps the order). pixel(draw, x, y, depth 0..1, perspective-correct barycentrics, triangle) for each covered
        // pixel centre. cull: per draw, back faces left out (front = counter-clockwise on screen).
        static void Rasterize(int w, int h, int drawCount, Func<int, double[]> clipOf, Func<int, int[]> trianglesOf,
                              Action<int, int, int, double, double[], int> pixel, bool[] cull = null, Action<int, int> setup = null)
        {
            const int band = 32;
            int bands = (h + band - 1) / band;
            if (setup != null)
                for (int d = 0; d < drawCount; d++)
                    for (int t = 0; t < trianglesOf(d).Length / 3; t++) setup(d, t);
            Parallel.For(0, bands, Cores, bi =>
            {
                int y0 = bi * band, y1 = Math.Min(h, y0 + band);
                var b = new double[3];
                for (int d = 0; d < drawCount; d++)
                {
                    double[] clip = clipOf(d);
                    int[] tris = trianglesOf(d);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                        double w0 = clip[i0 * 4 + 3], w1 = clip[i1 * 4 + 3], w2 = clip[i2 * 4 + 3];
                        if (w0 <= 1e-9 || w1 <= 1e-9 || w2 <= 1e-9) continue;
                        // window coordinates, rows from the top
                        double x0 = (clip[i0 * 4] / w0 * 0.5 + 0.5) * w, yy0 = (0.5 - clip[i0 * 4 + 1] / w0 * 0.5) * h, z0 = clip[i0 * 4 + 2] / w0;
                        double x1 = (clip[i1 * 4] / w1 * 0.5 + 0.5) * w, yy1 = (0.5 - clip[i1 * 4 + 1] / w1 * 0.5) * h, z1 = clip[i1 * 4 + 2] / w1;
                        double x2 = (clip[i2 * 4] / w2 * 0.5 + 0.5) * w, yy2 = (0.5 - clip[i2 * 4 + 1] / w2 * 0.5) * h, z2 = clip[i2 * 4 + 2] / w2;
                        double area = (x1 - x0) * (yy2 - yy0) - (x2 - x0) * (yy1 - yy0);   // rows down: counter-clockwise is negative
                        if (area == 0) continue;
                        if (cull != null && cull[d] && area > 0) continue;
                        int minY = Math.Max(y0, (int)Math.Floor(Math.Min(yy0, Math.Min(yy1, yy2)) - 0.5)), maxY = Math.Min(y1 - 1, (int)Math.Ceiling(Math.Max(yy0, Math.Max(yy1, yy2)) + 0.5));
                        if (minY > maxY) continue;
                        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)) - 0.5)), maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2)) + 0.5));
                        double inv = 1 / area;
                        for (int y = minY; y <= maxY; y++)
                        {
                            double py = y + 0.5;
                            for (int x = minX; x <= maxX; x++)
                            {
                                double px = x + 0.5;
                                double e0 = ((x1 - px) * (yy2 - py) - (x2 - px) * (yy1 - py)) * inv;
                                double e1 = ((x2 - px) * (yy0 - py) - (x0 - px) * (yy2 - py)) * inv;
                                double e2 = 1 - e0 - e1;
                                if (e0 < 0 || e1 < 0 || e2 < 0) continue;
                                double z = e0 * z0 + e1 * z1 + e2 * z2;
                                if (z < -1 || z > 1) continue;
                                double p0 = e0 / w0, p1 = e1 / w1, p2 = e2 / w2, s = p0 + p1 + p2;
                                b[0] = p0 / s; b[1] = p1 / s; b[2] = p2 / s;
                                pixel(d, x, y, z * 0.5 + 0.5, b, t / 3);
                            }
                        }
                    }
                }
            });
        }

        // the pixel shader: per-triangle setup (tangent frames), then per pixel
        class Shading
        {
            public double[] Eye, L0, L1, C95, LightMvp;
            public Func<double[], double[]> Back;
            public Texture[] Cube;
            public Texture Tam;
            public ShadowMap Shadow;
            // per triangle: dp1, dp2, duv1, duv2 (screen-signed), and the uv steps per pixel along x and y at its centre
            readonly Dictionary<Draw, double[][][]> frames = new Dictionary<Draw, double[][][]>();

            public void Setup(Draw draw, Frame f, int part, int tri, int w, int h)
            {
                double[][][] list;
                if (!frames.TryGetValue(draw, out list)) frames[draw] = list = new double[draw.Triangles.Length / 3][][];
                int i0 = draw.Triangles[tri * 3], i1 = draw.Triangles[tri * 3 + 1], i2 = draw.Triangles[tri * 3 + 2];
                double[] pos = f.Pos[part], clip = f.Clip[part];
                float[] uv = draw.Part.Uvs;
                // the screen offsets of the two edges (rows up, as dFdx / dFdy): their determinant's sign orients the frame
                Func<int, double> sx = i => clip[i * 4] / clip[i * 4 + 3] * 0.5 * w, sy = i => clip[i * 4 + 1] / clip[i * 4 + 3] * 0.5 * h;
                double det = (sx(i1) - sx(i0)) * (sy(i2) - sy(i0)) - (sy(i1) - sy(i0)) * (sx(i2) - sx(i0));
                double sign = (det < 0 ? -1 : 1);
                list[tri] = new[]
                {
                    new[] { (pos[i1 * 3] - pos[i0 * 3]) * sign, (pos[i1 * 3 + 1] - pos[i0 * 3 + 1]) * sign, (pos[i1 * 3 + 2] - pos[i0 * 3 + 2]) * sign },
                    new[] { pos[i2 * 3] - pos[i0 * 3], pos[i2 * 3 + 1] - pos[i0 * 3 + 1], pos[i2 * 3 + 2] - pos[i0 * 3 + 2] },
                    new[] { (uv[i1 * 2] - uv[i0 * 2]) * sign, (uv[i1 * 2 + 1] - uv[i0 * 2 + 1]) * sign },
                    new double[] { uv[i2 * 2] - uv[i0 * 2], uv[i2 * 2 + 1] - uv[i0 * 2 + 1] },
                    UvSteps(clip, uv, i0, i1, i2, w, h),
                };
            }

            // o: colour RGBA, glare RGBA; false: discarded
            public bool Shade(Draw draw, Frame f, int part, int tri, double[] b, int x, int y, int w, int h, double[] o)
            {
                int i0 = draw.Triangles[tri * 3], i1 = draw.Triangles[tri * 3 + 1], i2 = draw.Triangles[tri * 3 + 2];
                double[] pos = f.Pos[part], nrm = f.Nrm[part], clip = f.Clip[part];
                float[] uvs = draw.Part.Uvs;
                double u = b[0] * uvs[i0 * 2] + b[1] * uvs[i1 * 2] + b[2] * uvs[i2 * 2], v = b[0] * uvs[i0 * 2 + 1] + b[1] * uvs[i1 * 2 + 1] + b[2] * uvs[i2 * 2 + 1];
                Scratch sc = Scratch.Get();
                double[] c = sc.C, scratch = sc.Tap;
                double[][] tf = frames[draw][tri];
                draw.Texture.Sample(u, v, Lod(draw.Texture, tf[4]), c, scratch);
                if (c[3] < draw.Cut) return false;
                Look look = draw.Look;
                if (look.Special == Emission)   // Emission_PS: unlit, alpha 1
                {
                    double[] e = sc.Texel;
                    e[0] = c[0]; e[1] = c[1]; e[2] = c[2];
                    if (look.P1[0] > 0.5)   // EmissionNega_PS
                    {
                        double kx = Sat(c[3] * C95[0]), ky = Sat(c[3] * C95[1]);
                        for (int k = 0; k < 3; k++) { e[k] = kx * (1 - 2 * e[k]) + e[k]; e[k] = ky * (1 - 2 * e[k]) + e[k]; }
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        o[k] = e[k] * (1 + c[3] * (look.P0[k] * look.P0[3] - 1));
                        o[4 + k] = draw.GlareOn ? e[k] * (1 + c[3] * (draw.GlareScale[k] * draw.GlareScale[3] - 1)) - Tone[3] : 0;
                    }
                    o[3] = o[7] = 1;
                    return true;
                }
                if (look.Special == T1)   // T1_Emission_PS
                {
                    for (int k = 0; k < 3; k++)
                    {
                        o[k] = c[k];
                        o[4 + k] = draw.GlareOn ? c[k] * (1 + c[3] * (draw.GlareScale[k] * draw.GlareScale[3] - 1)) - Tone[3] : 0;
                    }
                    o[3] = o[7] = c[3];
                    return true;
                }
                double[] p = sc.P, n = sc.N, view = sc.View;
                p[0] = b[0] * pos[i0 * 3] + b[1] * pos[i1 * 3] + b[2] * pos[i2 * 3]; p[1] = b[0] * pos[i0 * 3 + 1] + b[1] * pos[i1 * 3 + 1] + b[2] * pos[i2 * 3 + 1]; p[2] = b[0] * pos[i0 * 3 + 2] + b[1] * pos[i1 * 3 + 2] + b[2] * pos[i2 * 3 + 2];
                n[0] = b[0] * nrm[i0 * 3] + b[1] * nrm[i1 * 3] + b[2] * nrm[i2 * 3]; n[1] = b[0] * nrm[i0 * 3 + 1] + b[1] * nrm[i1 * 3 + 1] + b[2] * nrm[i2 * 3 + 1]; n[2] = b[0] * nrm[i0 * 3 + 2] + b[1] * nrm[i1 * 3 + 2] + b[2] * nrm[i2 * 3 + 2];
                Normalize(n);
                for (int k = 0; k < 3; k++) view[k] = Eye[k] - p[k];
                Normalize(view);
                if (Dot(n, view) < 0) { n[0] = -n[0]; n[1] = -n[1]; n[2] = -n[2]; }   // two-sided cloth and hair
                if (draw.Hatch != 0)
                {
                    double hv = Sat(Dot(n, view));
                    if (draw.Hatch == HatchingOil) { double k = C95[0] * (1 - hv); c[1] += 0.7 * k; c[2] += k; }   // HatchingOil_PS
                    double ldn = Dot(L0, n), tone = HatchTone(n, hv, f.Game[part], i0, i1, i2, b);
                    double rim = Sat(draw.Brush[2] * (1 - hv) - draw.Brush[3] - ldn) * Sat(draw.Brush[2] - 4);
                    for (int k = 0; k < 3; k++)
                    {
                        double c2 = c[k] * c[k], paper = (ldn * 0.5 + 0.5) * (2 * c[k] - 2 * c2) + c2, ink = Sat(c2 - (0.5 - 0.5 * hv));
                        o[k] = ink + (paper - ink) * tone;
                        o[k] += (draw.RimColor[k] - o[k]) * rim * draw.RimColor[3];
                    }
                    o[3] = o[7] = draw.Hatch == HatchingAlpha1 ? 1 : draw.Blend ? c[3] : 1;
                    for (int k = 0; k < 3; k++) o[4 + k] = draw.GlareOn ? o[k] * (o[0] + o[1] + o[2] - Tone[3]) * Tone[k] : 0;
                    return true;
                }
                if (draw.Normals != null)   // tex2D(s1) * 2 - 1 in the tangent frame from the derivatives
                {
                    double[] e1 = sc.A, e2 = sc.B, t = sc.T, bt = sc.Bt;
                    CrossInto(tf[1], n, e1); CrossInto(n, tf[0], e2);
                    for (int k = 0; k < 3; k++) { t[k] = e1[k] * tf[2][0] + e2[k] * tf[3][0]; bt[k] = e1[k] * tf[2][1] + e2[k] * tf[3][1]; }
                    double s = 1 / Math.Sqrt(Math.Max(Dot(t, t), Dot(bt, bt)) + 1e-20);
                    double[] m = sc.Nml;
                    draw.Normals.Sample(u, v, Lod(draw.Normals, tf[4]), m, scratch);
                    double mx = m[0] * 2 - 1, my = m[1] * 2 - 1, mz = m[2] * 2 - 1;
                    double n0 = t[0] * s * mx + bt[0] * s * my + n[0] * mz, n1 = t[1] * s * mx + bt[1] * s * my + n[1] * mz, n2 = t[2] * s * mx + bt[2] * s * my + n[2] * mz;
                    n[0] = n0; n[1] = n1; n[2] = n2;
                    Normalize(n);
                }
                double ndl = Dot(n, L0), ndv = Sat(Dot(n, view));
                double alpha = draw.Blend ? c[3] : 1;
                double[] texel = sc.Texel, shade = sc.Shade;
                for (int k = 0; k < 3; k++) { texel[k] = c[k]; shade[k] = 1; }
                if (look.Special == Nega)   // SpcBumpBrushNega_PS
                {
                    double kx = Sat(8 * (C95[0] - c[3]) - 7), ky = Sat(8 * (C95[1] - c[3]) - 7), sh = 1 - kx, zz = ky * (1 - 2 * sh) + sh;
                    for (int k = 0; k < 3; k++)
                    {
                        double r4 = kx * (1 - 2 * c[k]) + c[k], r0 = ky * (1 - 2 * r4) + r4, c4 = c[k] * c[k] * c[k] * c[k];
                        texel[k] = r0 + zz * (c[k] * c[k] - c4) + c4;
                    }
                }
                else if (look.Special == Velvet)   // tinted to MatCol0 at grazing angles
                {
                    double k = 1 - look.P0[3] * ndv;
                    for (int j = 0; j < 3; j++) texel[j] = c[j] * (1 + k * (look.P0[j] - 1));
                    if (draw.Blend) alpha = c[3] + (1 - c[3]) * k;
                }
                else if (look.Special == ColorShadow) for (int k = 0; k < 3; k++) shade[k] = look.P0[k] * look.P0[3];
                double[] brush = draw.Brush, rimColor = draw.RimColor;
                double[] albedo = sc.Albedo;
                for (int k = 0; k < 3; k++) albedo[k] = texel[k];
                if (look.RimKind == RimBrush)
                {
                    double bb = Sat(brush[0] * ndl - brush[1]), rim = Sat(brush[2] * (1 - ndv) - brush[3] - ndl) * rimColor[3];
                    for (int k = 0; k < 3; k++)
                    {
                        double dark = shade[k] * texel[k] * texel[k];
                        albedo[k] = dark + bb * (texel[k] - dark);
                        albedo[k] += (rimColor[k] - albedo[k]) * rim;
                    }
                }
                else if (look.RimKind == RimSquare)
                    for (int k = 0; k < 3; k++) albedo[k] += (rimColor[k] - albedo[k]) * (1 - ndv) * (1 - ndv) * rimColor[3];
                // the key light with self-shadow, hemisphere ambient, the second light, specular
                double lit = draw.Shadowed ? Lit(p) : 1;
                double hy = n[1], ndl1 = Sat(Dot(n, L1));
                double[] r = sc.R;
                double twice = 2 * Dot(n, view);
                for (int k = 0; k < 3; k++) r[k] = n[k] * twice - view[k];
                double spec = Math.Pow(Sat(Dot(L0, r)), draw.MatSpec[1]) * draw.MatSpec[0] * c[3] * lit;
                double[] lc = sc.Light;
                for (int k = 0; k < 3; k++)
                {
                    double ambient = (hy * hy * HemiA[k] + hy * HemiB[k] + HemiC[k]) * draw.MatAmb[k] + Dif1[k] * draw.MatDif[k] * ndl1;
                    if (draw.Sss) ambient += draw.MatSss[k] * (0.5 - 0.5 * Math.Abs(ndl));
                    double diffuse = Dif0[k] * draw.MatDif[k] * Sat(ndl) * (SelfShadow[k] + (1 - SelfShadow[k]) * lit) + ambient;
                    lc[k] = diffuse;
                }
                double[] glow = sc.Glow;
                glow[0] = glow[1] = glow[2] = 0;
                if (look.Special == Emi)   // SpcBumpBrushEmi*_PS
                {
                    if (look.P1[1] > 0.5) for (int k = 0; k < 3; k++) albedo[k] += (albedo[k] * albedo[k] - albedo[k]) * (1 - ndv);
                    for (int k = 0; k < 3; k++) glow[k] = look.P0[k] * look.P0[3] * (1 - ndv) * look.P1[0] * c[3];
                    if (look.P1[2] > 0.5 && draw.Blend) alpha = c[3];
                }
                else if (look.Special == Oil)   // SpcBumpBrushOilSSS*_PS: banded fake reflection
                {
                    double[] bt = Norm(Add(Scale(Cross(n, tf[0]), tf[3][1]), Scale(Cross(tf[1], n), tf[2][1])));
                    double ripple = b[0] * f.Ripple[part][i0] + b[1] * f.Ripple[part][i1] + b[2] * f.Ripple[part][i2];
                    double[] rr = Norm(Add(Sub(Scale(n, 2 * Dot(n, view)), view), Scale(bt, 0.5 * ripple)));
                    double tt = Dot(view, rr);
                    double bx = Sat(tt * 16 - 14), by = Sat(tt * -5 - 4), bz = Sat(tt * -5 - 2), bw = Sat(tt * -5 - 1);
                    double[] oil = { bx + by, (bx + by) + ((bx + bz) - (bx + by)) * C95[2], (bx + by) + ((bx + bw) - (bx + by)) * C95[2] };
                    double fres = Sat(look.P0[0] * Sat((1 - ndv) * look.P0[2] - (look.P0[2] - 1)) + look.P0[1]);
                    for (int k = 0; k < 3; k++) albedo[k] += oil[k] * fres * C95[0];
                }
                double[] lit3 = sc.Lit;
                for (int k = 0; k < 3; k++) lit3[k] = albedo[k] * lc[k] + spec * Spc0[k] * draw.MatSpc[k] + glow[k];
                if (look.Special == Env && Cube != null)   // lerp to the stage's cube map by a fresnel weight
                {
                    double ff = (1 - ndv) * look.P0[2] - (look.P0[2] - 1);
                    double wgt = look.P1[0] == 3 ? Sat(look.P0[0] * ff + look.P0[1]) : Sat(look.P0[0] * (Sat(ff) + look.P0[1]));
                    double[] dir = Back(r);
                    double[] e = sc.Env;
                    CubeSample(Cube, dir[0], dir[1], dir[2], e);
                    double ea = Math.Max(e[3], 1.0 / 255);
                    double mixw = look.P1[0] == 1 ? wgt : wgt * c[3];
                    for (int k = 0; k < 3; k++) lit3[k] += (e[k] / ea - lit3[k]) * mixw;
                    if (look.P1[0] == 1) alpha = c[3];
                }
                for (int k = 0; k < 3; k++)
                {
                    o[k] = lit3[k];
                    o[4 + k] = draw.GlareOn ? lit3[k] * (lit3[0] + lit3[1] + lit3[2] - Tone[3]) * Tone[k] : 0;
                }
                o[3] = o[7] = alpha;
                if (look.Special == Emi) for (int k = 0; k < 3; k++) o[4 + k] = draw.GlareOn ? albedo[k] * draw.GlareScale[k] * draw.GlareScale[3] * look.P1[0] * c[3] : 0;
                return true;
            }

            // SamplerShadowMap s7: four taps +-1/1024, depth bias 0.012, averaged
            double Lit(double[] p)
            {
                double[] m = LightMvp;
                double s0 = m[0] * p[0] + m[1] * p[1] + m[2] * p[2] + m[3], s1 = m[4] * p[0] + m[5] * p[1] + m[6] * p[2] + m[7];
                double s2 = m[8] * p[0] + m[9] * p[1] + m[10] * p[2] + m[11], s3 = m[12] * p[0] + m[13] * p[1] + m[14] * p[2] + m[15];
                double x = s0 / s3 * 0.5 + 0.5, y = s1 / s3 * 0.5 + 0.5, z = s2 / s3 * 0.5 + 0.5;
                // the map's rows run from the top (window rows down), its v from the bottom
                double lit = 0;
                foreach (double[] t in Taps)
                    if (z - 0.012 * 0.5 <= Shadow.At(x + t[0] / 1024, 1 - (y + t[1] / 1024))) lit += 0.25;
                return lit;
            }

            static readonly double[][] Taps = { new[] { 1.0, 0 }, new[] { -1.0, 0 }, new[] { 0, 1.0 }, new[] { 0, -1.0 } };

            // Hatching_PS's tone: the four stroke directions of the tonal art map weighted by the object-space normal,
            // banded by NdotV
            double HatchTone(double[] n, double ndv, double[] game, int i0, int i1, int i2, double[] b)
            {
                double[] no = Back(n);
                double s = 1 / Math.Sqrt(no[0] * no[0] + no[1] * no[1] + 0.001), dx = no[0] * s, dy = no[1] * s;
                double[] wgt = { dx * 0.831469655 + dy * 0.555570126, dx * 0.555570424 + dy * 0.831469476, dx * -0.555569887 + dy * 0.831469834, dx * -0.831469297 + dy * 0.555570662 };
                for (int k = 0; k < 4; k++) wgt[k] *= wgt[k];
                double gx = b[0] * game[i0 * 3] + b[1] * game[i1 * 3] + b[2] * game[i2 * 3];
                double gy = b[0] * game[i0 * 3 + 1] + b[1] * game[i1 * 3 + 1] + b[2] * game[i2 * 3 + 1];
                double gw = b[0] * game[i0 * 3 + 2] + b[1] * game[i1 * 3 + 2] + b[2] * game[i2 * 3 + 2];
                double scale = Sat(gw * 0.222222224 - 0.111111112) * 4 + 6;
                var t = new double[4];
                if (Tam != null) Tam.Bilinear(0, gx * scale, gy * scale, true, t);
                Func<double, double> band = bias => Sat(1 - Enumerable.Range(0, 4).Sum(k => Sat(t[k] * 256 - bias) * wgt[k]));
                double rx = band(179.2), ry = band(102.4), rz = band(0);
                double r0 = ndv * 10 - 9, r1 = ndv * 5 - 3.5, r2 = ndv * 2.5 - 0.75, r3 = ndv * 65535 - 58981.5;
                double t0 = rx + (1 - rx) * r0, t1 = ry + (rx - ry) * r1, t2 = rz + (ry - rz) * r2, t3 = rz;
                double b0 = Sat(r3), b1 = Sat((ndv - 0.7 - b0) * 65535), b2 = Sat((ndv - b0 - 0.3 - b1) * 65535), b3 = Sat(1 - b0 - b1 - b2);
                return b0 * t0 + b1 * t1 + b2 * t2 + b3 * t3;
            }

            // log2 of texels per pixel along the longer screen axis, shortened up to 16 times by anisotropic filtering
            // (ponytail: once per triangle, at its centre; triangles are a few pixels across at this size)
            static double Lod(Texture tex, double[] steps)
            {
                if (steps == null) return 0;
                double dux = steps[0] * tex.Width, dvx = steps[1] * tex.Height, duy = steps[2] * tex.Width, dvy = steps[3] * tex.Height;
                double px = Math.Sqrt(dux * dux + dvx * dvx), py = Math.Sqrt(duy * duy + dvy * dvy);
                double max = Math.Max(px, py), min = Math.Min(px, py);
                if (max <= 0) return 0;
                double ratio = Math.Min(Math.Ceiling(max / Math.Max(min, 1e-12)), 16);
                return Math.Log(max / ratio, 2);
            }

            // the uv steps one pixel right and one pixel down from the triangle's centre (perspective-correct)
            static double[] UvSteps(double[] clip, float[] uvs, int i0, int i1, int i2, int w, int h)
            {
                double x = 0, y = 0;
                foreach (int i in new[] { i0, i1, i2 }) { x += (clip[i * 4] / clip[i * 4 + 3] * 0.5 + 0.5) * w / 3; y += (0.5 - clip[i * 4 + 1] / clip[i * 4 + 3] * 0.5) * h / 3; }
                double[] a = UvAt(clip, uvs, i0, i1, i2, x, y, w, h), bx = UvAt(clip, uvs, i0, i1, i2, x + 1, y, w, h), by = UvAt(clip, uvs, i0, i1, i2, x, y + 1, w, h);
                if (a == null || bx == null || by == null) return null;
                return new[] { bx[0] - a[0], bx[1] - a[1], by[0] - a[0], by[1] - a[1] };
            }

            // the perspective-correct uv at a window position (also outside the triangle)
            static double[] UvAt(double[] clip, float[] uvs, int i0, int i1, int i2, double px, double py, int w, int h)
            {
                double w0 = clip[i0 * 4 + 3], w1 = clip[i1 * 4 + 3], w2 = clip[i2 * 4 + 3];
                double x0 = (clip[i0 * 4] / w0 * 0.5 + 0.5) * w, y0 = (0.5 - clip[i0 * 4 + 1] / w0 * 0.5) * h;
                double x1 = (clip[i1 * 4] / w1 * 0.5 + 0.5) * w, y1 = (0.5 - clip[i1 * 4 + 1] / w1 * 0.5) * h;
                double x2 = (clip[i2 * 4] / w2 * 0.5 + 0.5) * w, y2 = (0.5 - clip[i2 * 4 + 1] / w2 * 0.5) * h;
                double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (area == 0) return null;
                double e0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) / area, e1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) / area, e2 = 1 - e0 - e1;
                double p0 = e0 / w0, p1 = e1 / w1, p2 = e2 / w2, s = p0 + p1 + p2;
                if (s == 0) return null;
                return new[] { (p0 * uvs[i0 * 2] + p1 * uvs[i1 * 2] + p2 * uvs[i2 * 2]) / s, (p0 * uvs[i0 * 2 + 1] + p1 * uvs[i1 * 2 + 1] + p2 * uvs[i2 * 2 + 1]) / s };
            }

            static double[] Add(double[] a, double[] b) { return new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] }; }
            static double[] Scale(double[] a, double s) { return new[] { a[0] * s, a[1] * s, a[2] * s }; }
        }

        // ---------- after drawing ----------

        // premultiplied Lanczos-3 down to the picture's size (no dark fringes at the edges), as Pillow's resize
        static float[] Downscale(float[] src, int sw, int sh, int w, int h)
        {
            var pre = new float[src.Length];
            for (int i = 0; i < sw * sh; i++) { float a = src[i * 4 + 3]; pre[i * 4] = src[i * 4] * a; pre[i * 4 + 1] = src[i * 4 + 1] * a; pre[i * 4 + 2] = src[i * 4 + 2] * a; pre[i * 4 + 3] = a; }
            float[] across = Lanczos(pre, sw, sh, w, true);
            float[] both = Lanczos(across, w, sh, h, false);
            for (int i = 0; i < w * h; i++)
            {
                float a = Math.Min(1, Math.Max(0, both[i * 4 + 3]));
                for (int c = 0; c < 3; c++) both[i * 4 + c] = a > 0 ? Math.Min(1, Math.Max(0, both[i * 4 + c] / a)) : 0;
                both[i * 4 + 3] = a;
            }
            return both;
        }

        static float[] Lanczos(float[] src, int sw, int sh, int size, bool horizontal)
        {
            int n = horizontal ? sw : sh;
            double scale = (double)n / size, support = 3 * scale;
            int ow = horizontal ? size : sw, oh = horizontal ? sh : size;
            var dst = new float[ow * oh * 4];
            Func<double, double> sinc = x => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
            Parallel.For(0, size, Cores, i =>
            {
                double center = (i + 0.5) * scale;
                int from = Math.Max(0, (int)(center - support + 0.5)), to = Math.Min(n, (int)(center + support + 0.5));
                var weights = new double[to - from];
                double total = 0;
                for (int j = from; j < to; j++)
                {
                    double x = (j - center + 0.5) / scale;
                    total += weights[j - from] = Math.Abs(x) < 3 ? sinc(x) * sinc(x / 3) : 0;
                }
                if (total != 0) for (int k = 0; k < weights.Length; k++) weights[k] /= total;
                int lines = horizontal ? sh : sw;
                for (int line = 0; line < lines; line++)
                    for (int c = 0; c < 4; c++)
                    {
                        double sum = 0;
                        for (int j = from; j < to; j++) sum += weights[j - from] * src[(horizontal ? line * sw + j : j * sw + line) * 4 + c];
                        dst[(horizontal ? line * ow + i : i * ow + line) * 4 + c] = (float)sum;
                    }
            });
            return dst;
        }

        static float[] BoxDown(float[] src, int sw, int sh, int w, int h)
        {
            int f = sw / w;
            var dst = new float[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        double sum = 0;
                        for (int j = 0; j < f; j++) for (int i = 0; i < f; i++) sum += src[((y * f + j) * sw + x * f + i) * 4 + c];
                        dst[(y * w + x) * 4 + c] = (float)(sum / (f * f));
                    }
            return dst;
        }

        // an h x w grid's centres (+ offset in its own texels) sampled from img (ih x iw x ch), clamped, bilinear
        static double[] Bilinear(double[] img, int ih, int iw, int ch, int h, int w, double ox = 0, double oy = 0)
        {
            var o = new double[h * w * ch];
            for (int y = 0; y < h; y++)
            {
                double sy = Math.Min(Math.Max((y + 0.5 + oy) * ih / h - 0.5, 0), ih - 1);
                int y0 = (int)Math.Floor(sy), y1 = Math.Min(y0 + 1, ih - 1);
                double fy = sy - y0;
                for (int x = 0; x < w; x++)
                {
                    double sx = Math.Min(Math.Max((x + 0.5 + ox) * iw / w - 0.5, 0), iw - 1);
                    int x0 = (int)Math.Floor(sx), x1 = Math.Min(x0 + 1, iw - 1);
                    double fx = sx - x0;
                    for (int c = 0; c < ch; c++)
                        o[(y * w + x) * ch + c] = (img[(y0 * iw + x0) * ch + c] * (1 - fx) + img[(y0 * iw + x1) * ch + c] * fx) * (1 - fy) +
                                                  (img[(y1 * iw + x0) * ch + c] * (1 - fx) + img[(y1 * iw + x1) * ch + c] * fx) * fy;
                }
            }
            return o;
        }

        // The stage's glare (RenderPass::buildPostEffects 0x5289d0), at the 1280x720 back buffer's scale: the glare MRT with
        // a = r + g + b at a quarter size, four blur levels (four bilinear taps each, halving), summed back, doubled and
        // added to the picture. fraction: the picture's height over the game screen's.
        static float[] AddGlare(float[] image, float[] glareImage, int w, int h, double fraction)
        {
            var origin = new double[w * h * 4];
            bool any = false;
            for (int i = 0; i < w * h; i++)
            {
                double r = glareImage[i * 4], g = glareImage[i * 4 + 1], b = glareImage[i * 4 + 2];
                origin[i * 4] = r; origin[i * 4 + 1] = g; origin[i * 4 + 2] = b; origin[i * 4 + 3] = r + g + b;
                any |= r > 0 || g > 0 || b > 0;
            }
            if (!any) return image;
            int h0 = Math.Max(1, (int)Math.Round(BackbufferHeight / 4 * fraction)), w0 = Math.Max(1, (int)Math.Round((double)h0 * w / h));
            double[] level = Bilinear(origin, h, w, 4, h0, w0);
            var levels = new List<Tuple<double[], int, int>>();
            for (int k = 0; k < 4; k++)
            {
                int lh = k == 0 ? h0 : Math.Max(1, levels[k - 1].Item2 / 2), lw = k == 0 ? w0 : Math.Max(1, levels[k - 1].Item3 / 2);
                double[] source = k == 0 ? level : levels[k - 1].Item1;
                int sh = k == 0 ? h0 : levels[k - 1].Item2, sw = k == 0 ? w0 : levels[k - 1].Item3;
                var sum = new double[lh * lw * 4];
                foreach (var t in new[] { new[] { 1, 1 }, new[] { -1, -1 }, new[] { -1, 1 }, new[] { 1, -1 } })
                {
                    double[] tap = Bilinear(source, sh, sw, 4, lh, lw, t[0] * 0.5, t[1] * 0.5);
                    for (int i = 0; i < sum.Length; i++) sum[i] += tap[i];
                }
                for (int i = 0; i < sum.Length; i++) sum[i] *= 0.25;
                levels.Add(Tuple.Create(sum, lh, lw));
            }
            var total = new double[h0 * w0 * 4];
            foreach (var l in levels)
            {
                double[] up = Bilinear(l.Item1, l.Item2, l.Item3, 4, h0, w0);
                for (int i = 0; i < total.Length; i++) total[i] += up[i];
            }
            var add = new double[h0 * w0 * 3];
            for (int i = 0; i < h0 * w0; i++)
            {
                double a = Sat(2 * total[i * 4 + 3]) * 0.9;
                for (int c = 0; c < 3; c++) add[i * 3 + c] = 2 * total[i * 4 + c] * a;
            }
            double[] big = Bilinear(add, h0, w0, 3, h, w);
            var o = (float[])image.Clone();
            for (int i = 0; i < w * h; i++)
            {
                double a = image[i * 4 + 3], plus = Math.Max(big[i * 3], Math.Max(big[i * 3 + 1], big[i * 3 + 2]));
                double alpha = Sat(a + plus);
                for (int c = 0; c < 3; c++) o[i * 4 + c] = (float)(alpha > 0 ? Sat((image[i * 4 + c] * a + big[i * 3 + c]) / Math.Max(alpha, 1e-6)) : 0);
                o[i * 4 + 3] = (float)alpha;
            }
            return o;
        }

        static Bitmap ToBitmap(float[] rgba, int w, int h)
        {
            var px = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                px[i * 4] = (byte)(Sat(rgba[i * 4 + 2]) * 255 + 0.5); px[i * 4 + 1] = (byte)(Sat(rgba[i * 4 + 1]) * 255 + 0.5);
                px[i * 4 + 2] = (byte)(Sat(rgba[i * 4]) * 255 + 0.5); px[i * 4 + 3] = (byte)(Sat(rgba[i * 4 + 3]) * 255 + 0.5);
            }
            var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bitmap.UnlockBits(data);
            return bitmap;
        }

        // ---------- a costume ----------

        /// <summary>One costume (<CHR>_<NN>) posed once, drawn in any of its colors. read(file name) gives a file of the
        /// costume (null when it has none); the animation, stage and missing color materials come from the game.</summary>
        public class Costume
        {
            readonly string game, fighter, stem;
            readonly Func<string, byte[]> read;
            List<CostumePose.Part> parts;
            readonly List<Texture> normals;
            readonly Dictionary<string, int> layers;

            readonly bool gimmicks;   // the fight-worn gimmick parts drawn (CostumePose.WearsGimmicks; the player's answer)

            public Costume(string game, string stem, Func<string, byte[]> read, bool gimmicks = true)
            {
                this.game = game; this.stem = stem; this.read = read; this.gimmicks = gimmicks;
                fighter = stem.Substring(0, 3).ToUpperInvariant();
                byte[] emo = read(stem + ".obj.emo");
                if (emo == null) throw new FileNotFoundException(stem + ".obj.emo is missing");
                Pose(0, 0, true);
                byte[] nml = read(stem + ".nml.emb");
                normals = nml != null ? GameArt.ReadContainer(nml).Select(e => Decode(e.Value)).ToList() : new List<Texture>();
                layers = NormalLayers(emo);
            }

            /// <summary>The fighter's animations (CostumePose.Animations), none without its .obj.ema.</summary>
            public List<KeyValuePair<string, int>> Animations()
            {
                string ema = CostumePose.GameFile(game, fighter, fighter + ".obj.ema");
                return ema != null ? CostumePose.Animations(File.ReadAllBytes(ema)) : new List<KeyValuePair<string, int>>();
            }

            /// <summary>Poses the costume again: frame `frame` of animation `animation` (0, 0, mirrored: character select's).</summary>
            public void Pose(int animation, double frame, bool mirror)
            {
                byte[] emo = read(stem + ".obj.emo");
                string ema = CostumePose.GameFile(game, fighter, fighter + ".obj.ema");
                try { parts = ema != null ? CostumePose.Posed(emo, File.ReadAllBytes(ema), read(stem + ".bsr"), fighter, animation, frame, mirror, gimmicks) : CostumePose.ReadEmo(emo, fighter, gimmicks); }
                // ponytail: the stance failed; the model as it's made. A damaged skeleton isn't drawn at all (its error)
                catch (Exception ex) when (!(ex is CostumePose.Damaged)) { parts = CostumePose.ReadEmo(emo, fighter, gimmicks); }
            }

            /// <summary>Color 1-12 as the training stage lights it, `width` x `height`, transparent around the fighter, turned
            /// `yaw` degrees; null when the color has no textures. 11 and 12 are color 1 in the game's two styles. quick: one
            /// sample a pixel and a smaller shadow map (a look while the viewer's controls move), several times cheaper.</summary>
            public Bitmap Draw(int color, int width = 600, int height = 800, double yaw = YawDegrees, bool quick = false)
            {
                int style = color == 11 || color == 12 ? color : 0;
                string cc = stem + "_" + (style != 0 ? 1 : color).ToString("D2");
                if (cc != shownColor)   // the last color's textures kept: the viewer draws it again and again as it turns
                {
                    shownColor = null; shownTextures = null; shownMaterials = null;
                    byte[] col = read(cc + ".col.emb");
                    if (col == null) return null;
                    shownTextures = GameArt.ReadContainer(col).Select(e => Decode(e.Value)).ToList();
                    byte[] emm = read(cc + ".obj.emm") ?? read(stem + ".obj.emm") ?? GameBytes(stem + ".obj.emm") ?? GameBytes(stem + "_01.obj.emm");
                    shownMaterials = ReadEmm(emm);
                    shownColor = cc;
                }
                return ToBitmap(Picture(game, fighter, parts, shownTextures, shownMaterials, normals, layers, width, height, style, yaw,
                                        quick ? 1 : Supersample, quick ? ShadowSize / 2 : ShadowSize), width, height);
            }

            string shownColor;
            List<Texture> shownTextures;
            Dictionary<string, Material> shownMaterials;

            byte[] GameBytes(string name)
            {
                string path = CostumePose.GameFile(game, fighter, name);
                return path != null ? File.ReadAllBytes(path) : null;
            }
        }
    }
}
