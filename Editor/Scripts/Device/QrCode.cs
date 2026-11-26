#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Feeder.MCP.Editor.Device
{
    // QR Code model 2, byte mode, error correction M, versions 1-10 (up to 213 bytes): enough for an install URL
    internal static class QrCode
    {
        const int MaxVersion = 10;
        // per version, index 0 unused
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] Blocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
        // format bits of level M
        const int EccFormatBits = 0;

        // true = dark module; [y, x]
        public static bool[,] Encode(string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            var version = 1;
            for (; version <= MaxVersion; version++)
                if (DataBits(version) >= 4 + CountBits(version) + data.Length * 8)
                    break;
            if (version > MaxVersion)
                throw new ArgumentException($"Text too long for a QR code here ({data.Length} bytes).");

            var bits = new List<bool>();
            Append(bits, 0b0100, 4);
            Append(bits, data.Length, CountBits(version));
            foreach (var b in data)
                Append(bits, b, 8);
            var capacity = DataBits(version);
            Append(bits, 0, Math.Min(4, capacity - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
                Append(bits, pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (var i = 0; i < bits.Count; i++)
                if (bits[i])
                    codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            var all = Interleave(version, codewords);
            var size = version * 4 + 17;
            var modules = new bool[size, size];
            var function = new bool[size, size];
            DrawFunctionPatterns(version, modules, function);
            DrawCodewords(all, modules, function);

            var best = 0;
            var bestPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask, modules, function);
                DrawFormat(mask, modules, function);
                var penalty = Penalty(modules);
                if (penalty < bestPenalty)
                {
                    best = mask;
                    bestPenalty = penalty;
                }
                ApplyMask(mask, modules, function);
            }
            ApplyMask(best, modules, function);
            DrawFormat(best, modules, function);
            return modules;
        }

        static int CountBits(int version) => version < 10 ? 8 : 16;

        static int RawModules(int version)
        {
            var result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                var align = version / 7 + 2;
                result -= (25 * align - 10) * align - 55;
                if (version >= 7)
                    result -= 36;
            }
            return result;
        }

        static int DataBits(int version) => (RawModules(version) / 8 - EccPerBlock[version] * Blocks[version]) * 8;

        static void Append(List<bool> bits, int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
                bits.Add(((value >> i) & 1) != 0);
        }

        static byte[] Interleave(int version, byte[] data)
        {
            int blocks = Blocks[version], ecc = EccPerBlock[version];
            var raw = RawModules(version) / 8;
            var shortBlocks = blocks - raw % blocks;
            var shortLength = raw / blocks;
            var divisor = Divisor(ecc);
            var parts = new List<byte[]>();
            for (int i = 0, k = 0; i < blocks; i++)
            {
                var length = shortLength - ecc + (i < shortBlocks ? 0 : 1);
                var block = new byte[length];
                Array.Copy(data, k, block, 0, length);
                k += length;
                var remainder = Remainder(block, divisor);
                var full = new byte[shortLength + 1];
                Array.Copy(block, full, length);
                Array.Copy(remainder, 0, full, full.Length - ecc, ecc);
                parts.Add(full);
            }
            var result = new List<byte>();
            for (var i = 0; i < shortLength + 1; i++)
                for (var j = 0; j < parts.Count; j++)
                    if (i != shortLength - ecc || j >= shortBlocks)
                        result.Add(parts[j][i]);
            return result.ToArray();
        }

        static byte[] Divisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            var root = 1;
            for (var i = 0; i < degree; i++)
            {
                for (var j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree)
                        result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        static byte[] Remainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (var b in data)
            {
                var factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (var i = 0; i < result.Length; i++)
                    result[i] ^= Multiply(divisor[i], factor);
            }
            return result;
        }

        static byte Multiply(int x, int y)
        {
            var z = 0;
            for (var i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        static void DrawFunctionPatterns(int version, bool[,] modules, bool[,] function)
        {
            var size = modules.GetLength(0);
            for (var i = 0; i < size; i++)
            {
                Set(modules, function, 6, i, i % 2 == 0);
                Set(modules, function, i, 6, i % 2 == 0);
            }
            Finder(modules, function, 3, 3);
            Finder(modules, function, size - 4, 3);
            Finder(modules, function, 3, size - 4);

            var positions = AlignmentPositions(version);
            for (var i = 0; i < positions.Length; i++)
                for (var j = 0; j < positions.Length; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == positions.Length - 1 || i == positions.Length - 1 && j == 0))
                        for (var dy = -2; dy <= 2; dy++)
                            for (var dx = -2; dx <= 2; dx++)
                                Set(modules, function, positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);

            // format areas reserved now, written per mask
            DrawFormat(0, modules, function);
            if (version >= 7)
            {
                var rem = version;
                for (var i = 0; i < 12; i++)
                    rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                var bits = version << 12 | rem;
                for (var i = 0; i < 18; i++)
                {
                    var bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + i % 3, b = i / 3;
                    Set(modules, function, a, b, bit);
                    Set(modules, function, b, a, bit);
                }
            }
        }

        static int[] AlignmentPositions(int version)
        {
            if (version == 1)
                return Array.Empty<int>();
            var count = version / 7 + 2;
            var step = version == 32 ? 26 : (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = version * 4 + 10; i >= 1; i--, pos -= step)
                result[i] = pos;
            return result;
        }

        static void Finder(bool[,] modules, bool[,] function, int x, int y)
        {
            var size = modules.GetLength(0);
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < size && yy >= 0 && yy < size)
                        Set(modules, function, xx, yy, distance != 2 && distance != 4);
                }
        }

        static void DrawFormat(int mask, bool[,] modules, bool[,] function)
        {
            var size = modules.GetLength(0);
            var data = EccFormatBits << 3 | mask;
            var rem = data;
            for (var i = 0; i < 10; i++)
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            var bits = (data << 10 | rem) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;
            for (var i = 0; i <= 5; i++)
                Set(modules, function, 8, i, Bit(i));
            Set(modules, function, 8, 7, Bit(6));
            Set(modules, function, 8, 8, Bit(7));
            Set(modules, function, 7, 8, Bit(8));
            for (var i = 9; i < 15; i++)
                Set(modules, function, 14 - i, 8, Bit(i));
            for (var i = 0; i < 8; i++)
                Set(modules, function, size - 1 - i, 8, Bit(i));
            for (var i = 8; i < 15; i++)
                Set(modules, function, 8, size - 15 + i, Bit(i));
            Set(modules, function, 8, size - 8, true);
        }

        static void Set(bool[,] modules, bool[,] function, int x, int y, bool dark)
        {
            modules[y, x] = dark;
            function[y, x] = true;
        }

        static void DrawCodewords(byte[] data, bool[,] modules, bool[,] function)
        {
            var size = modules.GetLength(0);
            var i = 0;
            for (var right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                    right = 5;
                for (var vert = 0; vert < size; vert++)
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var upward = ((right + 1) & 2) == 0;
                        var y = upward ? size - 1 - vert : vert;
                        if (function[y, x] || i >= data.Length * 8)
                            continue;
                        modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
            }
        }

        static void ApplyMask(int mask, bool[,] modules, bool[,] function)
        {
            var size = modules.GetLength(0);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    if (function[y, x])
                        continue;
                    var invert = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => (x / 3 + y / 2) % 2 == 0,
                        5 => x * y % 2 + x * y % 3 == 0,
                        6 => (x * y % 2 + x * y % 3) % 2 == 0,
                        _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                    };
                    if (invert)
                        modules[y, x] = !modules[y, x];
                }
        }

        // the standard's four penalty rules, so the chosen mask scans well
        static int Penalty(bool[,] m)
        {
            var size = m.GetLength(0);
            var result = 0;
            for (var pass = 0; pass < 2; pass++)
                for (var a = 0; a < size; a++)
                {
                    var run = 1;
                    for (var b = 1; b < size; b++)
                    {
                        bool current = pass == 0 ? m[a, b] : m[b, a], previous = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                        if (current == previous)
                        {
                            run++;
                            if (run == 5)
                                result += 3;
                            else if (run > 5)
                                result++;
                        }
                        else
                            run = 1;
                    }
                    for (var b = 0; b + 10 < size; b++)
                    {
                        bool At(int k) => pass == 0 ? m[a, b + k] : m[b + k, a];
                        if (At(0) && !At(1) && At(2) && At(3) && At(4) && !At(5) && At(6) &&
                            (!At(7) && !At(8) && !At(9) && !At(10)))
                            result += 40;
                        if (!At(0) && !At(1) && !At(2) && !At(3) && At(4) && !At(5) && At(6) && At(7) && At(8) && !At(9) && At(10))
                            result += 40;
                    }
                }
            var dark = 0;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    if (m[y, x])
                        dark++;
                    if (y + 1 < size && x + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                        result += 3;
                }
            var total = size * size;
            result += (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10 - 10 > 0 ? (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10 - 10 : 0;
            return result;
        }
    }
}
