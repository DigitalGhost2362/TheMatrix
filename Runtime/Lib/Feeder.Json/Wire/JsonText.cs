#nullable enable
using System;
using System.Buffers;

namespace Feeder.Json
{
    internal static class JsonText
    {
        // the serializer reports exceptions carrying this source as JsonException, like the reference does with its own marker
        internal const string RethrowableSource = "Feeder.Json.Rethrowable";

        internal static InvalidOperationException InvalidOperation(string message) =>
            new InvalidOperationException(message) { Source = RethrowableSource };

        internal static FormatException Format(string message) =>
            new FormatException(message) { Source = RethrowableSource };

        // returns the scalar or -1 when invalid; length is the maximal subpart consumed, so one U+FFFD stands for it
        internal static int DecodeScalar(ReadOnlySpan<byte> s, int i, out int length)
        {
            int b0 = s[i];
            if (b0 < 0x80)
            {
                length = 1;
                return b0;
            }

            int need, cp, lo = 0x80, hi = 0xBF;
            if (b0 >= 0xC2 && b0 <= 0xDF)
            {
                need = 1;
                cp = b0 & 0x1F;
            }
            else if (b0 >= 0xE0 && b0 <= 0xEF)
            {
                need = 2;
                cp = b0 & 0x0F;
                if (b0 == 0xE0) lo = 0xA0;
                else if (b0 == 0xED) hi = 0x9F;
            }
            else if (b0 >= 0xF0 && b0 <= 0xF4)
            {
                need = 3;
                cp = b0 & 0x07;
                if (b0 == 0xF0) lo = 0x90;
                else if (b0 == 0xF4) hi = 0x8F;
            }
            else
            {
                length = 1;
                return -1;
            }

            for (int k = 1; k <= need; k++)
            {
                if (i + k >= s.Length || s[i + k] < lo || s[i + k] > hi)
                {
                    length = k;
                    return -1;
                }
                cp = (cp << 6) | (s[i + k] & 0x3F);
                lo = 0x80;
                hi = 0xBF;
            }
            length = need + 1;
            return cp;
        }

        internal static string Utf8ToString(ReadOnlySpan<byte> utf8)
        {
            if (utf8.Length == 0)
                return string.Empty;

            char[] chars = ArrayPool<char>.Shared.Rent(utf8.Length);
            try
            {
                int n = 0;
                for (int i = 0; i < utf8.Length;)
                {
                    int b = utf8[i];
                    if (b < 0x80)
                    {
                        chars[n++] = (char)b;
                        i++;
                        continue;
                    }

                    int cp = DecodeScalar(utf8, i, out int length);
                    if (cp < 0)
                        throw InvalidOperation("Cannot transcode invalid UTF-8 JSON text to UTF-16 string.");
                    i += length;
                    if (cp < 0x10000)
                    {
                        chars[n++] = (char)cp;
                    }
                    else
                    {
                        cp -= 0x10000;
                        chars[n++] = (char)(0xD800 + (cp >> 10));
                        chars[n++] = (char)(0xDC00 + (cp & 0x3FF));
                    }
                }
                return new string(chars, 0, n);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }
        }

        internal static string UnescapeToString(ReadOnlySpan<byte> escaped)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(escaped.Length);
            try
            {
                int n = Unescape(escaped, buffer);
                return Utf8ToString(new ReadOnlySpan<byte>(buffer, 0, n));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // input is the raw content of a string token the reader accepted; output is never longer than input
        internal static int Unescape(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            int w = 0;
            for (int i = 0; i < src.Length; i++)
            {
                byte b = src[i];
                if (b != (byte)'\\')
                {
                    dst[w++] = b;
                    continue;
                }

                b = src[++i];
                switch (b)
                {
                    case (byte)'n': dst[w++] = (byte)'\n'; break;
                    case (byte)'r': dst[w++] = (byte)'\r'; break;
                    case (byte)'t': dst[w++] = (byte)'\t'; break;
                    case (byte)'b': dst[w++] = (byte)'\b'; break;
                    case (byte)'f': dst[w++] = (byte)'\f'; break;
                    case (byte)'u':
                        int cp = Hex4(src, i + 1);
                        i += 4;
                        if (cp >= 0xD800 && cp <= 0xDFFF)
                        {
                            if (cp >= 0xDC00)
                                throw InvalidSurrogate(cp);
                            if (src.Length < i + 7 || src[i + 1] != (byte)'\\' || src[i + 2] != (byte)'u')
                                throw InvalidOperation("Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.");
                            int low = Hex4(src, i + 3);
                            if (low < 0xDC00 || low > 0xDFFF)
                                throw InvalidSurrogate(low);
                            i += 6;
                            cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                        }
                        w += EncodeScalar(cp, dst.Slice(w));
                        break;
                    default: dst[w++] = b; break;
                }
            }
            return w;
        }

        internal static int EncodeScalar(int cp, Span<byte> dst)
        {
            if (cp < 0x80)
            {
                dst[0] = (byte)cp;
                return 1;
            }
            if (cp < 0x800)
            {
                dst[0] = (byte)(0xC0 | (cp >> 6));
                dst[1] = (byte)(0x80 | (cp & 0x3F));
                return 2;
            }
            if (cp < 0x10000)
            {
                dst[0] = (byte)(0xE0 | (cp >> 12));
                dst[1] = (byte)(0x80 | ((cp >> 6) & 0x3F));
                dst[2] = (byte)(0x80 | (cp & 0x3F));
                return 3;
            }
            dst[0] = (byte)(0xF0 | (cp >> 18));
            dst[1] = (byte)(0x80 | ((cp >> 12) & 0x3F));
            dst[2] = (byte)(0x80 | ((cp >> 6) & 0x3F));
            dst[3] = (byte)(0x80 | (cp & 0x3F));
            return 4;
        }

        // byte count of the strict UTF-8 form; lone surrogates are rejected like the reference transcoder does
        internal static int Utf8Length(ReadOnlySpan<char> s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x80) n += 1;
                else if (c < 0x800) n += 2;
                else if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                        throw new ArgumentException("Cannot transcode invalid UTF-16 string to UTF-8 JSON text.");
                    n += 4;
                    i++;
                }
                else n += 3;
            }
            return n;
        }

        // caller sized dst with Utf8Length, which already rejected lone surrogates
        internal static int ToUtf8(ReadOnlySpan<char> s, Span<byte> dst)
        {
            int w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                if (char.IsHighSurrogate((char)c))
                    c = char.ConvertToUtf32((char)c, s[++i]);
                w += EncodeScalar(c, dst.Slice(w));
            }
            return w;
        }

        static int Hex4(ReadOnlySpan<byte> s, int start)
        {
            int v = 0;
            for (int k = start; k < start + 4; k++)
            {
                int b = s[k];
                v = (v << 4) | (b <= '9' ? b - '0' : (b | 0x20) - 'a' + 10);
            }
            return v;
        }

        static InvalidOperationException InvalidSurrogate(int value) =>
            InvalidOperation("Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value: '0x" + value.ToString("X2") + "'.");
    }
}
