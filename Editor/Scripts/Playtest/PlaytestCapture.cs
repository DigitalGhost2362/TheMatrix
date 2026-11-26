#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.API;
using UnityEngine;

namespace Feeder.MCP.Editor.Playtest
{
    // Game view frames with the playtest cursor painted in, and the contact sheet of a run. The cursor is UI Toolkit
    // drawn over the window, outside the game image, so it is painted from the runner's own cursor state; reading the
    // desktop instead would also capture whatever window lies over Unity.
    internal static class PlaytestCapture
    {
        public const int TileWidth = PlaytestPixels.TileWidth;
        public const string ManifestName = "frames.txt";
        const int Columns = 4;
        const int RowsPerSheet = 8;
        const int Pad = 6;
        const int LabelHeight = 30;
        const int TitleHeight = 44;
        const int LabelFontSize = 18;
        const int TitleFontSize = 24;

        static readonly Dictionary<Texture, Color32[]> _readable = new Dictionary<Texture, Color32[]>();

        // bottom row first; null with an error when there is no Game view image
        public static Color32[]? Frame(PlaytestCursorState? cursor, PlaytestCursorTheme? theme, out int width, out int height, out string error)
        {
            var pixels = Tool_Screenshot.ReadGameView(0, out width, out height, out error);
            if (pixels != null && cursor != null && theme != null)
                PaintCursor(pixels, width, height, cursor, theme);
            return pixels;
        }

        public static void SavePng(string path, Color32[] pixels, int width, int height)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Tool_Screenshot.EncodePng(pixels, width, height));
        }

        public static void SaveTile(string path, Color32[] pixels, int width, int height)
        {
            var tile = PlaytestPixels.Shrink(pixels, width, height, TileWidth, out var tileHeight);
            SavePng(path, tile, TileWidth, tileHeight);
        }

        static void PaintCursor(Color32[] pixels, int width, int height, PlaytestCursorState cursor, PlaytestCursorTheme theme)
        {
            // capture pixels per Game view point
            var points = Screen.width * PlaytestCursorOverlay.PointsPerScreenPixel();
            var scale = points > 0 ? width / points : 1f;
            if (cursor.Target.HasValue)
            {
                var r = cursor.Target.Value;
                var line = Mathf.Max(2, Mathf.RoundToInt(2 * scale));
                Outline(pixels, width, height, Mathf.RoundToInt(r.xMin * width) - line, Mathf.RoundToInt(r.yMin * height) - line,
                    Mathf.RoundToInt(r.xMax * width) + line, Mathf.RoundToInt(r.yMax * height) + line, line, theme.accent);
            }
            var clicking = cursor.ClickFrame >= 0 && theme.clickSheet != null;
            var texture = clicking ? theme.clickSheet : theme.idle;
            if (texture == null)
                return;
            var source = Readable(texture);
            int cellW = texture.width, cellH = texture.height, cellX = 0, cellY = 0;
            if (clicking)
            {
                cellW = texture.width / Mathf.Max(1, theme.columns);
                cellH = texture.height / Mathf.Max(1, theme.rows);
                var frame = Mathf.Min(cursor.ClickFrame, theme.frames - 1);
                cellX = frame % theme.columns * cellW;
                cellY = texture.height - (frame / theme.columns + 1) * cellH;
            }
            var size = Mathf.Max(8, Mathf.RoundToInt(theme.size * scale));
            var left = Mathf.RoundToInt(cursor.Position.x * width - size * theme.hotspot.x);
            var top = Mathf.RoundToInt(cursor.Position.y * height - size * theme.hotspot.y);
            for (var dy = 0; dy < size; dy++)
            {
                var y = height - 1 - (top + dy);
                if (y < 0 || y >= height)
                    continue;
                var sy = cellY + Mathf.Min(cellH - 1, (int)((1f - (dy + 0.5f) / size) * cellH));
                for (var dx = 0; dx < size; dx++)
                {
                    var x = left + dx;
                    if (x < 0 || x >= width)
                        continue;
                    var sx = cellX + Mathf.Min(cellW - 1, (int)((dx + 0.5f) / size * cellW));
                    Blend(ref pixels[y * width + x], source[sy * texture.width + sx], 1f);
                }
            }
        }

        // top-left origin coordinates
        static void Outline(Color32[] pixels, int width, int height, int x0, int y0, int x1, int y1, int line, Color color)
        {
            var c = (Color32)color;
            for (var y = y0; y < y1; y++)
            {
                if (y < 0 || y >= height)
                    continue;
                var row = (height - 1 - y) * width;
                var edgeRow = y - y0 < line || y1 - 1 - y < line;
                for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
                    if (edgeRow || x - x0 < line || x1 - 1 - x < line)
                        pixels[row + x] = c;
            }
        }

        static void Blend(ref Color32 under, Color32 over, float opacity)
        {
            var a = over.a / 255f * opacity;
            if (a <= 0f)
                return;
            under.r = (byte)(over.r * a + under.r * (1f - a));
            under.g = (byte)(over.g * a + under.g * (1f - a));
            under.b = (byte)(over.b * a + under.b * (1f - a));
        }

        // pixels of any texture, readable or not, through the GPU
        static Color32[] Readable(Texture texture)
        {
            if (_readable.TryGetValue(texture, out var cached) && cached.Length == texture.width * texture.height)
                return cached;
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                return _readable[texture] = copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        public static void WriteManifest(string folder, IEnumerable<PlaytestTile> tiles)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(System.IO.Path.Combine(folder, ManifestName), tiles.Select(t => t.Serialize()));
        }

        // one or more contact_sheet*.png next to the report; returns their paths
        public static List<string> BuildSheets(string folder, string title)
        {
            var sheets = new List<string>();
            var manifest = System.IO.Path.Combine(folder, ManifestName);
            if (!File.Exists(manifest))
                return sheets;
            var tiles = File.ReadAllLines(manifest).Select(PlaytestTile.Parse).Where(t => t != null).Select(t => t!).ToList();
            foreach (var tile in tiles)
                tile.Path = System.IO.Path.Combine(folder, tile.Path);
            tiles.RemoveAll(t => !File.Exists(t.Path));
            if (tiles.Count == 0)
                return sheets;

            var images = new List<(PlaytestTile tile, Color32[] pixels, int height)>();
            var loader = new Texture2D(2, 2);
            try
            {
                foreach (var tile in tiles)
                {
                    if (!loader.LoadImage(File.ReadAllBytes(tile.Path)))
                        continue;
                    var pixels = PlaytestPixels.Shrink(loader.GetPixels32(), loader.width, loader.height, TileWidth, out var h);
                    images.Add((tile, pixels, h));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loader);
            }

            // grid cells: a NewRow tile moves to the start of the next row
            var cells = new List<(int row, int col)>();
            int row = 0, col = 0;
            foreach (var image in images)
            {
                if (image.tile.NewRow && col > 0)
                {
                    row++;
                    col = 0;
                }
                cells.Add((row, col));
                if (++col == Columns)
                {
                    row++;
                    col = 0;
                }
            }
            var rows = cells.Count == 0 ? 0 : cells[cells.Count - 1].row + 1;
            var tileHeight = images.Max(i => i.height);
            var pages = (rows + RowsPerSheet - 1) / RowsPerSheet;
            for (var page = 0; page < pages; page++)
            {
                var pageRows = Math.Min(RowsPerSheet, rows - page * RowsPerSheet);
                var width = Columns * (TileWidth + Pad) + Pad;
                var height = TitleHeight + pageRows * (tileHeight + LabelHeight + Pad) + Pad;
                var sheet = new Color32[width * height];
                Fill(sheet, width, height, 0, 0, width, height, new Color32(24, 26, 32, 255));
                var texts = new List<(int x, int y, string text, int size)>
                {
                    (Pad + 4, 8, pages > 1 ? $"{title}  ({page + 1}/{pages})" : title, TitleFontSize),
                };
                for (var i = 0; i < images.Count; i++)
                {
                    var (r, c) = cells[i];
                    if (r / RowsPerSheet != page)
                        continue;
                    var x = Pad + c * (TileWidth + Pad);
                    var y = TitleHeight + r % RowsPerSheet * (tileHeight + LabelHeight + Pad);
                    Fill(sheet, width, height, x, y, TileWidth, LabelHeight, new Color32(40, 120, 70, 255));
                    texts.Add((x + 8, y + 5, images[i].tile.Label, LabelFontSize));
                    Paste(sheet, width, height, images[i].pixels, TileWidth, images[i].height, x, y + LabelHeight);
                }
                DrawTexts(sheet, width, height, texts);
                var path = System.IO.Path.Combine(folder, page == 0 ? "contact_sheet.png" : $"contact_sheet_{page + 1}.png");
                SavePng(path, sheet, width, height);
                sheets.Add(path);
            }
            return sheets;
        }

        // top-left origin
        static void Fill(Color32[] pixels, int width, int height, int x, int y, int w, int h, Color32 color)
        {
            for (var yy = y; yy < y + h && yy < height; yy++)
            {
                var row = (height - 1 - yy) * width;
                for (var xx = x; xx < x + w && xx < width; xx++)
                    pixels[row + xx] = color;
            }
        }

        static void Paste(Color32[] pixels, int width, int height, Color32[] image, int w, int h, int x, int y)
        {
            for (var iy = 0; iy < h; iy++)
            {
                var yy = y + iy;
                if (yy >= height)
                    break;
                Array.Copy(image, (h - 1 - iy) * w, pixels, (height - 1 - yy) * width + x, Math.Min(w, width - x));
            }
        }

        // all strings are requested before the atlas is read: a later request may rebuild the font texture
        static void DrawTexts(Color32[] pixels, int width, int height, List<(int x, int y, string text, int size)> texts)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                return;
            foreach (var t in texts)
                font.RequestCharactersInTexture(t.text, t.size, FontStyle.Bold);
            var atlasTexture = font.material.mainTexture;
            _readable.Remove(atlasTexture);
            var atlas = Readable(atlasTexture);
            _readable.Remove(atlasTexture);
            int aw = atlasTexture.width, ah = atlasTexture.height;
            foreach (var t in texts)
            {
                var pen = t.x;
                var baseline = t.y + Mathf.RoundToInt(t.size * 0.9f);
                foreach (var ch in t.text)
                {
                    if (!font.GetCharacterInfo(ch, out var info, t.size, FontStyle.Bold))
                        continue;
                    int gw = info.glyphWidth, gh = info.glyphHeight, left = pen + info.minX, top = baseline - info.maxY;
                    if (pen + info.advance > width - Pad)
                        break;
                    for (var gy = 0; gy < gh; gy++)
                    {
                        var y = top + gy;
                        if (y < 0 || y >= height)
                            continue;
                        var v = (gy + 0.5f) / gh;
                        for (var gx = 0; gx < gw; gx++)
                        {
                            var x = left + gx;
                            if (x < 0 || x >= width)
                                continue;
                            var u = (gx + 0.5f) / gw;
                            var uv = info.uvTopLeft + (info.uvTopRight - info.uvTopLeft) * u + (info.uvBottomLeft - info.uvTopLeft) * v;
                            var sx = Mathf.Clamp((int)(uv.x * aw), 0, aw - 1);
                            var sy = Mathf.Clamp((int)(uv.y * ah), 0, ah - 1);
                            var a = atlas[sy * aw + sx].a;
                            Blend(ref pixels[(height - 1 - y) * width + x], new Color32(255, 255, 255, a), 1f);
                        }
                    }
                    pen += info.advance;
                }
            }
        }
    }
}
