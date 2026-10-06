#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    // procedural particle textures: white rgb, shape in alpha, so particle color tints them.
    // the recipe is stored in the importer userData; it doubles as the texture's description.
    public static class VfxTextures
    {
        public const string Folder = "Assets/VFX/_Textures";

        public static readonly string[] Shapes =
            { "glow", "circle", "ring", "spark", "star", "flare", "smoke", "noise", "trail", "shockwave", "polygon", "lightning", "crescent", "sparkle" };

        sealed class Recipe
        {
            public string Shape = "glow";
            public int Size = 128;
            public float Soft = 0.5f, Falloff = 2f, Glow, Inner = 0.35f, Sharp = 6f, Thin = 0.15f;
            public float Radius = 0.7f, Width = 0.15f, Scale = 4f, Contrast = 1f, Angle, Evolve = 1.5f, Hollow, Offset = 0.3f;
            public int Points = 5, Rays = 4, Sides = 4, Octaves = 4, Seed = 1, Cols = 1, Rows = 1;
        }

        #region API

        public static string PathOf(string name) => $"{Folder}/{name}.png";

        public static Texture2D? Find(string nameOrPath)
        {
            var path = nameOrPath.StartsWith("Assets/") ? nameOrPath : PathOf(nameOrPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static string Create(string name, JsonObject recipeJson)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/'))
                throw new ArgumentException($"invalid texture name '{name}'");
            var r = Parse(recipeJson);
            var pixels = Render(r);
            var tex = new Texture2D(r.Size, r.Size, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(pixels);
                tex.Apply();
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(PathOf(name), tex.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            var path = PathOf(name);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = r.Shape == "noise" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.maxTextureSize = Mathf.ClosestPowerOfTwo(r.Size);
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.userData = recipeJson.ToJsonString();
            importer.SaveAndReimport();
            return path;
        }

        public static string? RecipeOf(Texture? tex)
        {
            if (tex == null) return null;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
            return importer == null || string.IsNullOrEmpty(importer.userData) || !importer.userData.StartsWith("{") ? null : importer.userData;
        }

        public static (int cols, int rows) Frames(Texture? tex)
        {
            var recipe = RecipeOf(tex);
            if (recipe == null) return (1, 1);
            try
            {
                var r = Parse((JsonObject)JsonNode.Parse(recipe)!);
                return (r.Cols, r.Rows);
            }
            catch
            {
                return (1, 1);
            }
        }

        public static List<string> List()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return new List<string>();
            return AssetDatabase.FindAssets("t:Texture2D", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => $"{Path.GetFileNameWithoutExtension(p)} {RecipeOf(AssetDatabase.LoadAssetAtPath<Texture2D>(p)) ?? "(no recipe)"}")
                .ToList();
        }

        public static void EnsureDefault(string name)
        {
            if (Find(name) != null) return;
            Create(name, new JsonObject { ["shape"] = "glow", ["falloff"] = 2.2m });
        }

        #endregion

        #region Recipe

        static Recipe Parse(JsonObject o)
        {
            var r = new Recipe();
            foreach (var kv in o)
            {
                var k = kv.Key;
                var v = kv.Value;
                switch (k)
                {
                    case "shape":
                        r.Shape = ((string?)v ?? "").ToLowerInvariant();
                        if (!Shapes.Contains(r.Shape)) throw new FormatException($"unknown shape '{r.Shape}'. Valid: {string.Join("|", Shapes)}");
                        break;
                    case "size": r.Size = Mathf.Clamp((int)F(v, k), 16, 2048); break;
                    case "soft": r.Soft = Mathf.Clamp01(F(v, k)); break;
                    case "falloff": r.Falloff = Mathf.Max(0.05f, F(v, k)); break;
                    case "glow": r.Glow = Mathf.Clamp01(F(v, k)); break;
                    case "inner": r.Inner = Mathf.Clamp(F(v, k), 0.01f, 1f); break;
                    case "sharp": r.Sharp = Mathf.Max(0.5f, F(v, k)); break;
                    case "thin": r.Thin = Mathf.Clamp(F(v, k), 0.005f, 1f); break;
                    case "radius": r.Radius = Mathf.Clamp(F(v, k), 0.05f, 0.95f); break;
                    case "width": r.Width = Mathf.Clamp(F(v, k), 0.005f, 1f); break;
                    case "scale": r.Scale = Mathf.Max(1f, F(v, k)); break;
                    case "contrast": r.Contrast = Mathf.Max(0.1f, F(v, k)); break;
                    case "angle": r.Angle = F(v, k); break;
                    case "evolve": r.Evolve = F(v, k); break;
                    case "hollow": r.Hollow = Mathf.Clamp01(F(v, k)); break;
                    case "offset": r.Offset = Mathf.Clamp(F(v, k), 0.02f, 1f); break;
                    case "points": r.Points = Mathf.Clamp((int)F(v, k), 2, 32); break;
                    case "rays": r.Rays = Mathf.Clamp((int)F(v, k), 1, 16); break;
                    case "sides": r.Sides = Mathf.Clamp((int)F(v, k), 3, 12); break;
                    case "octaves": r.Octaves = Mathf.Clamp((int)F(v, k), 1, 8); break;
                    case "seed": r.Seed = (int)F(v, k); break;
                    case "frames":
                    {
                        var a = v as JsonArray ?? throw new FormatException("'frames' expects [cols,rows]");
                        r.Cols = Mathf.Clamp((int)F(a[0], k), 1, 16);
                        r.Rows = Mathf.Clamp((int)F(a[1], k), 1, 16);
                        break;
                    }
                    default:
                        throw new FormatException($"unknown recipe key '{k}'. Valid: shape,size,soft,falloff,glow,inner,sharp,thin,radius,width,scale,contrast,angle,evolve,hollow,offset,points,rays,sides,octaves,seed,frames");
                }
            }
            if (!o.ContainsKey("shape")) throw new FormatException("recipe needs 'shape'");
            return r;
        }

        static float F(JsonNode? n, string k) => VfxCodec.ToFloat(n, k);

        #endregion

        #region Render

        static Color32[] Render(Recipe r)
        {
            var size = r.Size;
            var px = new Color32[size * size];
            var cellW = size / r.Cols;
            var cellH = size / r.Rows;
            var frameCount = r.Cols * r.Rows;
            var rad = r.Angle * Mathf.Deg2Rad;
            var cos = Mathf.Cos(rad);
            var sin = Mathf.Sin(rad);
            for (var py = 0; py < size; py++)
            for (var pxi = 0; pxi < size; pxi++)
            {
                var col = Mathf.Min(pxi / cellW, r.Cols - 1);
                var rowFromTop = Mathf.Min((size - 1 - py) / cellH, r.Rows - 1);
                var frame = rowFromTop * r.Cols + col;
                var lx = (pxi - col * cellW + 0.5f) / cellW * 2f - 1f;
                var ly = (py - (r.Rows - 1 - rowFromTop) * cellH + 0.5f) / cellH * 2f - 1f;
                var x = lx * cos + ly * sin;
                var y = -lx * sin + ly * cos;
                var phase = frameCount > 1 ? (float)frame / frameCount : 0f;
                var a = Mathf.Clamp01(Shape(r, x, y, lx, ly, phase));
                if (r.Glow > 0f) a = Mathf.Max(a, r.Glow * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(lx * lx + ly * ly)), 3f));
                px[py * size + pxi] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            return px;
        }

        static float Edge(float d, float soft) => 1f - Smooth(1f - Mathf.Max(soft, 0.02f), 1f, d);

        static float Smooth(float a, float b, float x)
        {
            var t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Falloff(float d, float p) => Mathf.Pow(Mathf.Clamp01(1f - d), p);

        static float Shape(Recipe r, float x, float y, float ux, float uy, float phase)
        {
            var d = Mathf.Sqrt(x * x + y * y) / 0.95f;
            switch (r.Shape)
            {
                case "glow":
                    return Falloff(d, r.Falloff);
                case "circle":
                    return Hollowed(r, Edge(d, r.Soft), d);
                case "ring":
                {
                    var w = r.Width * 0.5f;
                    return 1f - Smooth(w * (1f - r.Soft), w, Mathf.Abs(d - r.Radius));
                }
                case "shockwave":
                {
                    var w = r.Width * 0.5f;
                    var ring = 1f - Smooth(w * (1f - r.Soft), w, Mathf.Abs(d - r.Radius));
                    var fill = d < r.Radius ? 0.35f * Mathf.Pow(d / r.Radius, 3f) : 0f;
                    return Mathf.Max(ring, fill);
                }
                case "spark":
                    return Falloff(Mathf.Sqrt(x * x + (y / r.Thin) * (y / r.Thin)) / 0.95f, r.Falloff);
                case "star":
                {
                    var theta = Mathf.Atan2(y, x);
                    var spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(r.Points * theta * 0.5f)), r.Sharp);
                    var reach = Mathf.Lerp(r.Inner, 1f, spikes);
                    return Falloff(d / reach, r.Falloff);
                }
                case "flare":
                {
                    var a = 0f;
                    for (var i = 0; i < r.Rays; i++)
                    {
                        var t = Mathf.PI * i / r.Rays;
                        var rx = x * Mathf.Cos(t) + y * Mathf.Sin(t);
                        var ry = -x * Mathf.Sin(t) + y * Mathf.Cos(t);
                        a = Mathf.Max(a, Falloff(Mathf.Sqrt(rx * rx + (ry / r.Thin) * (ry / r.Thin)) / 0.95f, r.Falloff));
                    }
                    return Mathf.Max(a, 0.6f * Falloff(d * 2.5f, 2f));
                }
                case "smoke":
                {
                    var n = Fbm(x * r.Scale * 0.5f, y * r.Scale * 0.5f, phase * r.Evolve * 4f, r.Octaves, r.Seed, 0);
                    var mask = Mathf.Clamp01(1f - d * d);
                    return Mathf.Pow(Mathf.Clamp01(mask * (0.25f + 1.1f * n) * 1.2f - 0.15f), r.Contrast);
                }
                case "noise":
                {
                    var period = Mathf.Max(1, Mathf.RoundToInt(r.Scale));
                    var u = (ux + 1f) * 0.5f * period;
                    var v = (uy + 1f) * 0.5f * period;
                    var n = Fbm(u, v, phase * r.Evolve * 4f, r.Octaves, r.Seed, period);
                    return Mathf.Pow(Mathf.Clamp01((n - 0.5f) * r.Contrast + 0.5f), 1f);
                }
                case "trail":
                {
                    var u = (x + 1f) * 0.5f;
                    var across = Edge(Mathf.Abs(y), r.Soft);
                    return across * Smooth(0f, 0.08f, u) * Mathf.Pow(Mathf.Clamp01(1f - u), r.Falloff * 0.25f);
                }
                case "polygon":
                {
                    var theta = Mathf.Atan2(y, x) + Mathf.PI * 0.5f;
                    var seg = 2f * Mathf.PI / r.Sides;
                    var local = Mathf.Repeat(theta, seg) - seg * 0.5f;
                    var p = d * Mathf.Cos(local) / Mathf.Cos(seg * 0.5f);
                    return Hollowed(r, Edge(p, r.Soft), p);
                }
                case "lightning":
                {
                    var bolt = 0f;
                    for (var i = 0; i < 2; i++)
                    {
                        var offset = (Fbm(x * 3f + i * 7.3f, phase * r.Evolve * 4f, i * 3.1f, 3, r.Seed + i, 0) - 0.5f) * 0.9f;
                        var w = r.Thin * (i == 0 ? 0.5f : 0.25f);
                        bolt = Mathf.Max(bolt, (i == 0 ? 1f : 0.6f) * Falloff(Mathf.Abs(y - offset) / w, r.Falloff));
                    }
                    return bolt * Smooth(1f, 0.85f, Mathf.Abs(x));
                }
                case "sparkle":
                {
                    var e = Mathf.Clamp(r.Sharp, 0.2f, 2f);
                    var q = Mathf.Pow(Mathf.Pow(Mathf.Abs(x), e) + Mathf.Pow(Mathf.Abs(y), e), 1f / e) / 0.95f;
                    return Falloff(q, r.Falloff);
                }
                case "crescent":
                {
                    var cx = x - r.Offset;
                    var cut = Edge(Mathf.Sqrt(cx * cx + y * y) / 0.95f / Mathf.Max(0.05f, r.Radius + r.Offset * 0.5f), r.Soft);
                    return Mathf.Clamp01(Edge(d, r.Soft) - cut);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(r.Shape), r.Shape, "unknown shape");
            }
        }

        static float Hollowed(Recipe r, float solid, float d)
        {
            if (r.Hollow <= 0f) return solid;
            var inner = Edge(d / Mathf.Max(0.01f, 1f - r.Hollow), r.Soft);
            return Mathf.Clamp01(solid - inner);
        }

        static float Fbm(float x, float y, float z, int octaves, int seed, int period)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            var p = period;
            for (var i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(x, y, z, seed + i * 131, p);
                norm += amp;
                amp *= 0.5f;
                x *= 2f; y *= 2f; z *= 2f;
                if (p > 0) p *= 2;
            }
            return sum / norm;
        }

        static float ValueNoise(float x, float y, float z, int seed, int period)
        {
            var xi = Mathf.FloorToInt(x);
            var yi = Mathf.FloorToInt(y);
            var zi = Mathf.FloorToInt(z);
            var fx = x - xi;
            var fy = y - yi;
            var fz = z - zi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float H(int a, int b, int c)
            {
                if (period > 0)
                {
                    a = ((a % period) + period) % period;
                    b = ((b % period) + period) % period;
                }
                unchecked
                {
                    var h = (uint)(a * 374761393 + b * 668265263 + c * 1440662683 + seed * 1274126177);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    h ^= h >> 16;
                    return (h & 0xFFFFFF) / (float)0xFFFFFF;
                }
            }
            var x00 = Mathf.Lerp(H(xi, yi, zi), H(xi + 1, yi, zi), fx);
            var x10 = Mathf.Lerp(H(xi, yi + 1, zi), H(xi + 1, yi + 1, zi), fx);
            var x01 = Mathf.Lerp(H(xi, yi, zi + 1), H(xi + 1, yi, zi + 1), fx);
            var x11 = Mathf.Lerp(H(xi, yi + 1, zi + 1), H(xi + 1, yi + 1, zi + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        #endregion
    }
}
