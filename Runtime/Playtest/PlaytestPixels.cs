#nullable enable
using System;
using UnityEngine;

namespace Feeder.MCP.Editor.Playtest
{
    // pixel helpers shared by the Editor capture and a device build; rows bottom first
    internal static class PlaytestPixels
    {
        public const int TileWidth = 480;

        // box filter down to a width, aspect kept
        public static Color32[] Shrink(Color32[] pixels, int width, int height, int toWidth, out int toHeight)
        {
            toHeight = Mathf.Max(1, Mathf.RoundToInt((float)height * toWidth / width));
            if (toWidth >= width)
            {
                toHeight = height;
                return width == toWidth ? pixels : Widen(pixels, width, height, toWidth);
            }
            var result = new Color32[toWidth * toHeight];
            for (var y = 0; y < toHeight; y++)
            {
                int y0 = y * height / toHeight, y1 = Math.Max(y0 + 1, (y + 1) * height / toHeight);
                for (var x = 0; x < toWidth; x++)
                {
                    int x0 = x * width / toWidth, x1 = Math.Max(x0 + 1, (x + 1) * width / toWidth);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (var sy = y0; sy < y1; sy++)
                        for (var sx = x0; sx < x1; sx++)
                        {
                            var p = pixels[sy * width + sx];
                            r += p.r; g += p.g; b += p.b; n++;
                        }
                    result[y * toWidth + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            }
            return result;
        }

        static Color32[] Widen(Color32[] pixels, int width, int height, int toWidth)
        {
            var result = new Color32[toWidth * height];
            for (var y = 0; y < height; y++)
                Array.Copy(pixels, y * width, result, y * toWidth, width);
            return result;
        }
    }
}
