#nullable enable
using System;
using System.Numerics;

namespace Feeder.Json
{
    // number text in and out; every input span is a number token the reader already validated
    internal static class JsonNumber
    {
        internal const int MaxFormattedLength = 32;

        static readonly double[] pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
            1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        internal static bool TryGetInt32(ReadOnlySpan<byte> s, out int value)
        {
            if (TryParseInteger(s, out bool negative, out ulong magnitude) && magnitude <= (negative ? 2147483648UL : int.MaxValue))
            {
                value = negative ? (int)(0L - (long)magnitude) : (int)magnitude;
                return true;
            }
            value = 0;
            return false;
        }

        internal static bool TryGetInt64(ReadOnlySpan<byte> s, out long value)
        {
            if (TryParseInteger(s, out bool negative, out ulong magnitude) && magnitude <= (negative ? 9223372036854775808UL : long.MaxValue))
            {
                value = negative ? unchecked((long)(0UL - magnitude)) : (long)magnitude;
                return true;
            }
            value = 0;
            return false;
        }

        internal static bool TryGetUInt64(ReadOnlySpan<byte> s, out ulong value)
        {
            if (TryParseInteger(s, out bool negative, out value) && !negative)
                return true;
            value = 0;
            return false;
        }

        internal static bool TryGetUInt32(ReadOnlySpan<byte> s, out uint value)
        {
            bool ok = TryGetUInt64(s, out ulong v) && v <= uint.MaxValue;
            value = ok ? (uint)v : 0;
            return ok;
        }

        internal static bool TryGetByte(ReadOnlySpan<byte> s, out byte value)
        {
            bool ok = TryGetUInt64(s, out ulong v) && v <= byte.MaxValue;
            value = ok ? (byte)v : (byte)0;
            return ok;
        }

        // unsigned results reject any sign, "-0" included, like the reference parser
        static bool TryParseInteger(ReadOnlySpan<byte> s, out bool negative, out ulong magnitude)
        {
            negative = s[0] == '-';
            magnitude = 0;
            for (int i = negative ? 1 : 0; i < s.Length; i++)
            {
                uint d = (uint)(s[i] - '0');
                if (d > 9 || magnitude > (ulong.MaxValue - d) / 10)
                    return false;
                magnitude = magnitude * 10 + d;
            }
            return true;
        }

        internal static double ParseDouble(ReadOnlySpan<byte> s)
        {
            ulong mantissa = Decompose(s, out bool negative, out long exp10, out bool truncated);
            double result;
            if (mantissa == 0)
                result = 0;
            else if (!truncated && mantissa <= 1UL << 53 && exp10 >= -22 && exp10 <= 22)
                result = exp10 >= 0 ? mantissa * pow10[exp10] : mantissa / pow10[-exp10];
            else if (!EiselLemire.TryDouble(mantissa, exp10, truncated, out result))
                result = ParseExact(s, false);
            return negative ? -result : result;
        }

        internal static float ParseSingle(ReadOnlySpan<byte> s)
        {
            ulong mantissa = Decompose(s, out bool negative, out long exp10, out bool truncated);
            float result;
            if (mantissa == 0)
                result = 0;
            else if (!EiselLemire.TrySingle(mantissa, exp10, truncated, out result))
                result = (float)ParseExact(s, true);
            return negative ? -result : result;
        }

        // up to 19 significant digits and a power of ten; truncated when a dropped digit was not zero
        static ulong Decompose(ReadOnlySpan<byte> s, out bool negative, out long exp10, out bool truncated)
        {
            int i = 0;
            negative = s[0] == '-';
            if (negative)
                i++;
            ulong mantissa = 0;
            int digits = 0;
            exp10 = 0;
            truncated = false;
            for (; i < s.Length && IsDigit(s[i]); i++)
            {
                uint d = (uint)(s[i] - '0');
                if (digits < 19)
                {
                    if (mantissa != 0 || d != 0)
                    {
                        mantissa = mantissa * 10 + d;
                        digits++;
                    }
                }
                else
                {
                    exp10++;
                    truncated |= d != 0;
                }
            }
            if (i < s.Length && s[i] == '.')
            {
                for (i++; i < s.Length && IsDigit(s[i]); i++)
                {
                    uint d = (uint)(s[i] - '0');
                    if (digits < 19)
                    {
                        if (mantissa != 0 || d != 0)
                        {
                            mantissa = mantissa * 10 + d;
                            digits++;
                        }
                        exp10--;
                    }
                    else
                    {
                        truncated |= d != 0;
                    }
                }
            }
            exp10 += ParseExponent(s, i);
            while (mantissa != 0 && mantissa % 10 == 0)
            {
                mantissa /= 10;
                exp10++;
            }
            return mantissa;
        }

        static long ParseExponent(ReadOnlySpan<byte> s, int i)
        {
            if (i >= s.Length)
                return 0;
            i++;
            bool negative = s[i] == '-';
            if (s[i] == '-' || s[i] == '+')
                i++;
            long e = 0;
            for (; i < s.Length; i++)
            {
                if (e < 1_000_000_000)
                    e = e * 10 + (s[i] - '0');
            }
            return negative ? -e : e;
        }

        // correctly rounded (to nearest, ties to even) magnitude of the decimal text, computed with big integers
        internal static double ParseExact(ReadOnlySpan<byte> s, bool single)
        {
            const int MaxDigits = 800;
            BigInteger m = BigInteger.Zero;
            ulong chunk = 0;
            int chunkDigits = 0, count = 0;
            long exp10 = 0;
            bool sticky = false, fraction = false;
            int i = s[0] == '-' ? 1 : 0;
            for (; i < s.Length; i++)
            {
                byte b = s[i];
                if (b == '.')
                {
                    fraction = true;
                    continue;
                }
                if (!IsDigit(b))
                    break;
                uint d = (uint)(b - '0');
                if (count == 0 && d == 0)
                {
                    if (fraction)
                        exp10--;
                    continue;
                }
                if (count < MaxDigits)
                {
                    chunk = chunk * 10 + d;
                    if (++chunkDigits == 18)
                    {
                        m = m * 1_000_000_000_000_000_000UL + chunk;
                        chunk = 0;
                        chunkDigits = 0;
                    }
                    count++;
                    if (fraction)
                        exp10--;
                }
                else
                {
                    sticky |= d != 0;
                    if (!fraction)
                        exp10++;
                }
            }
            m = m * BigInteger.Pow(10, chunkDigits) + chunk;
            exp10 += ParseExponent(s, i);
            if (sticky)
            {
                m = m * 10 + 1;
                exp10--;
                count++;
            }

            long top = exp10 + count - 1;
            if (top > (single ? 39 : 309))
                return double.PositiveInfinity;
            if (top < (single ? -47 : -325))
                return 0;

            int mantissaBits = single ? 24 : 53;
            BigInteger q;
            int binaryExponent = 0;
            bool inexact = false;
            if (exp10 >= 0)
            {
                q = m * BigInteger.Pow(10, (int)exp10);
            }
            else
            {
                BigInteger divisor = BigInteger.Pow(10, (int)-exp10);
                int shift = Math.Max(0, BitLength(divisor) - BitLength(m) + mantissaBits + 3);
                q = BigInteger.DivRem(m << shift, divisor, out BigInteger remainder);
                binaryExponent = -shift;
                inexact = !remainder.IsZero;
            }
            if (single)
            {
                ulong m24 = RoundBinary(q, binaryExponent, inexact, 24, -149, out int e24);
                if (e24 > 104)
                    return double.PositiveInfinity;
                int bits = m24 < 1UL << 23 ? (int)m24 : ((e24 + 150) << 23) | (int)(m24 & ((1UL << 23) - 1));
                return BitConverter.Int32BitsToSingle(bits);
            }
            ulong m53 = RoundBinary(q, binaryExponent, inexact, 53, -1074, out int e53);
            if (e53 > 971)
                return double.PositiveInfinity;
            long bits64 = m53 < 1UL << 52 ? (long)m53 : ((long)(e53 + 1075) << 52) | (long)(m53 & ((1UL << 52) - 1));
            return BitConverter.Int64BitsToDouble(bits64);
        }

        // value = q * 2^binaryExponent (+ a sliver when inexact) rounded to nearest even as mantissa * 2^exponent
        static ulong RoundBinary(BigInteger q, int binaryExponent, bool inexact, int mantissaBits, int minExponent, out int exponent)
        {
            int e = Math.Max(binaryExponent + BitLength(q) - mantissaBits, minExponent);
            int drop = e - binaryExponent;
            ulong mantissa;
            if (drop <= 0)
            {
                mantissa = (ulong)(q << -drop);
            }
            else
            {
                BigInteger kept = q >> drop;
                BigInteger rest = q - (kept << drop);
                int half = rest.CompareTo(BigInteger.One << (drop - 1));
                mantissa = (ulong)kept;
                if (half > 0 || (half == 0 && (inexact || (mantissa & 1) != 0)))
                    mantissa++;
            }
            if (mantissa == 1UL << mantissaBits)
            {
                mantissa >>= 1;
                e++;
            }
            exponent = e;
            return mantissa;
        }

        static int BitLength(BigInteger v)
        {
            byte[] bytes = v.ToByteArray();
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0)
                top--;
            int bits = top * 8;
            for (int b = bytes[top]; b != 0; b >>= 1)
                bits++;
            return bits;
        }

        // mirrors the reference decimal parser: first 30 digits kept, trailing zeros give scale, ties to even on the 29th digit
        internal static bool TryParseDecimal(ReadOnlySpan<byte> s, out decimal value)
        {
            const int MaxDigits = 30;
            Span<byte> digits = stackalloc byte[MaxDigits];
            bool negative = s[0] == '-';
            int i = negative ? 1 : 0;
            while (i < s.Length && s[i] == '0')
                i++;
            int digitCount = 0, stored = 0;
            bool tail = false;
            long scale = 0;
            int start = i;
            for (; i < s.Length && IsDigit(s[i]); i++)
            {
                if (++digitCount >= MaxDigits)
                    tail |= s[i] != '0';
            }
            for (int k = start; k < i && stored < MaxDigits; k++)
                digits[stored++] = s[k];
            scale = i - start;
            if (i < s.Length && s[i] == '.')
            {
                int fractionStart = ++i;
                for (; i < s.Length && IsDigit(s[i]); i++)
                {
                    if (++digitCount >= MaxDigits)
                        tail |= s[i] != '0';
                }
                int k = fractionStart;
                if (stored == 0)
                {
                    for (; k < i && s[k] == '0'; k++)
                        scale--;
                }
                for (; k < i && stored < MaxDigits; k++)
                    digits[stored++] = s[k];
            }
            scale += ParseExponent(s, i);

            if (stored == 0)
            {
                value = new decimal(0, 0, 0, negative, (byte)Math.Max(0, Math.Min(28, -scale)));
                return true;
            }
            if (scale > 29)
            {
                value = 0;
                return false;
            }

            int p = 0;
            int c = digits[0];
            ulong low64 = 0;
            long e = scale;
            while (e > -28)
            {
                e--;
                low64 = low64 * 10 + (ulong)(c - '0');
                c = ++p < stored ? digits[p] : 0;
                if (low64 >= ulong.MaxValue / 10)
                    break;
                if (c == 0)
                {
                    while (e > 0)
                    {
                        e--;
                        low64 *= 10;
                        if (low64 >= ulong.MaxValue / 10)
                            break;
                    }
                    break;
                }
            }

            uint high = 0;
            while ((e > 0 || (c != 0 && e > -28)) &&
                   (high < uint.MaxValue / 10 || (high == uint.MaxValue / 10 && (low64 < 0x99999999_99999999UL || (low64 == 0x99999999_99999999UL && c <= '5')))))
            {
                ulong tmpLow = (uint)low64 * 10UL;
                ulong tmp64 = (uint)(low64 >> 32) * 10UL + (tmpLow >> 32);
                low64 = (uint)tmpLow + (tmp64 << 32);
                high = (uint)(tmp64 >> 32) + high * 10;
                if (c != 0)
                {
                    uint d = (uint)(c - '0');
                    low64 += d;
                    if (low64 < d)
                        high++;
                    c = ++p < stored ? digits[p] : 0;
                }
                e--;
            }

            if (c >= '5')
            {
                bool round = true;
                if (c == '5' && (low64 & 1) == 0)
                {
                    bool zeroTail = !tail;
                    for (int k = p + 1; k < stored && zeroTail; k++)
                        zeroTail = digits[k] == '0';
                    round = !zeroTail;
                }
                if (round && ++low64 == 0 && ++high == 0)
                {
                    low64 = 0x99999999_9999999AUL;
                    high = uint.MaxValue / 10;
                    e++;
                }
            }

            if (e > 0)
            {
                value = 0;
                return false;
            }
            value = e <= -29
                ? new decimal(0, 0, 0, negative, 28)
                : new decimal((int)low64, (int)(low64 >> 32), (int)high, negative, (byte)-e);
            return true;
        }

        internal static int FormatInt64(long value, Span<byte> dst)
        {
            if (value >= 0)
                return FormatUInt64((ulong)value, dst);
            dst[0] = (byte)'-';
            return 1 + FormatUInt64(0UL - (ulong)value, dst.Slice(1));
        }

        internal static int FormatUInt64(ulong value, Span<byte> dst)
        {
            int n = 1;
            for (ulong v = value; v >= 10; v /= 10)
                n++;
            for (int i = n - 1; i >= 0; i--, value /= 10)
                dst[i] = (byte)('0' + (int)(value % 10));
            return n;
        }

        // same text as the reference: shortest round trip, fixed notation up to 17 integer digits
        internal static int FormatDouble(double value, Span<byte> dst)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int n = 0;
            if (bits < 0)
                dst[n++] = (byte)'-';
            ulong mantissa = (ulong)bits & ((1UL << 52) - 1);
            int exponent = (int)((bits >> 52) & 0x7FF);
            if (mantissa == 0 && exponent == 0)
            {
                dst[n] = (byte)'0';
                return n + 1;
            }
            ulong digits = Ryu.Double(mantissa, exponent, out int e10);
            return n + Layout(digits, e10, 17, dst.Slice(n));
        }

        // same text as the reference: shortest round trip, fixed notation up to 9 integer digits
        internal static int FormatSingle(float value, Span<byte> dst)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            int n = 0;
            if (bits < 0)
                dst[n++] = (byte)'-';
            uint mantissa = (uint)bits & ((1u << 23) - 1);
            int exponent = (bits >> 23) & 0xFF;
            if (mantissa == 0 && exponent == 0)
            {
                dst[n] = (byte)'0';
                return n + 1;
            }
            uint digits = Ryu.Single(mantissa, exponent, out int e10);
            return n + Layout(digits, e10, 9, dst.Slice(n));
        }

        static int Layout(ulong digits, int exponent, int maxFixedDigits, Span<byte> dst)
        {
            while (digits % 10 == 0)
            {
                digits /= 10;
                exponent++;
            }
            Span<byte> text = stackalloc byte[20];
            int count = FormatUInt64(digits, text);
            int scale = exponent + count;
            int n = 0;
            if (scale > Math.Max(count, maxFixedDigits) || scale < -3)
            {
                dst[n++] = text[0];
                if (count > 1)
                {
                    dst[n++] = (byte)'.';
                    text.Slice(1, count - 1).CopyTo(dst.Slice(n));
                    n += count - 1;
                }
                int e = scale - 1;
                dst[n++] = (byte)'E';
                dst[n++] = e < 0 ? (byte)'-' : (byte)'+';
                if (e < 0)
                    e = -e;
                if (e >= 100)
                    dst[n++] = (byte)('0' + e / 100);
                dst[n++] = (byte)('0' + e / 10 % 10);
                dst[n++] = (byte)('0' + e % 10);
            }
            else if (scale > 0)
            {
                int whole = Math.Min(scale, count);
                text.Slice(0, whole).CopyTo(dst);
                n = whole;
                for (int k = count; k < scale; k++)
                    dst[n++] = (byte)'0';
                if (count > scale)
                {
                    dst[n++] = (byte)'.';
                    text.Slice(scale, count - scale).CopyTo(dst.Slice(n));
                    n += count - scale;
                }
            }
            else
            {
                dst[n++] = (byte)'0';
                dst[n++] = (byte)'.';
                for (int k = scale; k < 0; k++)
                    dst[n++] = (byte)'0';
                text.Slice(0, count).CopyTo(dst.Slice(n));
                n += count;
            }
            return n;
        }

        // same text as the reference: all scale digits kept, no sign on zero
        internal static int FormatDecimal(decimal value, Span<byte> dst)
        {
            int[] parts = decimal.GetBits(value);
            uint lo = (uint)parts[0], mid = (uint)parts[1], hi = (uint)parts[2];
            int scale = (parts[3] >> 16) & 0xFF;
            Span<byte> text = stackalloc byte[29];
            int count = 0;
            while ((lo | mid | hi) != 0)
            {
                ulong r = hi;
                hi = (uint)(r / 10);
                r = ((r % 10) << 32) | mid;
                mid = (uint)(r / 10);
                r = ((r % 10) << 32) | lo;
                lo = (uint)(r / 10);
                text[28 - count++] = (byte)('0' + (int)(r % 10));
            }
            int n = 0;
            if (count == 0)
            {
                text[28] = (byte)'0';
                count = 1;
            }
            else if (parts[3] < 0)
            {
                dst[n++] = (byte)'-';
            }
            ReadOnlySpan<byte> digits = text.Slice(29 - count);
            if (count <= scale)
            {
                dst[n++] = (byte)'0';
                dst[n++] = (byte)'.';
                for (int k = count; k < scale; k++)
                    dst[n++] = (byte)'0';
                digits.CopyTo(dst.Slice(n));
                return n + count;
            }
            int whole = count - scale;
            digits.Slice(0, whole).CopyTo(dst.Slice(n));
            n += whole;
            if (scale > 0)
            {
                dst[n++] = (byte)'.';
                digits.Slice(whole).CopyTo(dst.Slice(n));
                n += scale;
            }
            return n;
        }

        // full grammar check for number text that did not come from the reader
        internal static bool IsValid(ReadOnlySpan<byte> s)
        {
            int i = 0;
            if (i < s.Length && s[i] == '-')
                i++;
            if (i >= s.Length || !IsDigit(s[i]))
                return false;
            if (s[i++] != '0')
            {
                while (i < s.Length && IsDigit(s[i]))
                    i++;
            }
            if (i < s.Length && s[i] == '.')
            {
                if (++i >= s.Length || !IsDigit(s[i]))
                    return false;
                while (i < s.Length && IsDigit(s[i]))
                    i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                if (++i < s.Length && (s[i] == '+' || s[i] == '-'))
                    i++;
                if (i >= s.Length || !IsDigit(s[i]))
                    return false;
                while (i < s.Length && IsDigit(s[i]))
                    i++;
            }
            return i == s.Length;
        }

        internal static bool IsDigit(byte b) => (uint)(b - '0') <= 9;
    }
}
