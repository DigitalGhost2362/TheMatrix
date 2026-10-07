#nullable enable
using System;
using System.Numerics;

namespace Feeder.Json
{
    // w * 10^q rounded to nearest even for w < 2^64 (Eisel-Lemire as in the fast_float library); false sends the caller to the exact path
    internal static class EiselLemire
    {
        const int SmallestPower = -342;
        const int LargestPower = 308;

        // two words per power of five, high then low, normalized to 128 bits
        static readonly ulong[] powers = BuildTable();

        static ulong[] BuildTable()
        {
            var table = new ulong[(LargestPower - SmallestPower + 1) * 2];
            var mask = new BigInteger(ulong.MaxValue);
            for (int q = SmallestPower; q <= LargestPower; q++)
            {
                BigInteger c;
                if (q < 0)
                {
                    BigInteger power5 = BigInteger.Pow(5, -q);
                    int z = BitLength(power5);
                    c = (BigInteger.One << (q >= -27 ? z + 127 : 2 * z + 128)) / power5 + 1;
                }
                else
                {
                    c = BigInteger.Pow(5, q);
                }
                int length = BitLength(c);
                c = length > 128 ? c >> (length - 128) : c << (128 - length);
                int i = 2 * (q - SmallestPower);
                table[i] = (ulong)(c >> 64);
                table[i + 1] = (ulong)(c & mask);
            }
            return table;
        }

        internal static bool TryDouble(ulong w, long q, bool truncated, out double value)
        {
            value = 0;
            if (q < SmallestPower)
                return true;
            if (q > LargestPower)
            {
                value = double.PositiveInfinity;
                return true;
            }
            if (!Compute(w, (int)q, 52, -1023, 0x7FF, -4, 23, out ulong bits))
                return false;
            if (truncated && (!Compute(w + 1, (int)q, 52, -1023, 0x7FF, -4, 23, out ulong upper) || upper != bits))
                return false;
            value = BitConverter.Int64BitsToDouble((long)bits);
            return true;
        }

        internal static bool TrySingle(ulong w, long q, bool truncated, out float value)
        {
            value = 0;
            if (q < -64)
                return true;
            if (q > 38)
            {
                value = float.PositiveInfinity;
                return true;
            }
            if (!Compute(w, (int)q, 23, -127, 0xFF, -17, 10, out ulong bits))
                return false;
            if (truncated && (!Compute(w + 1, (int)q, 23, -127, 0xFF, -17, 10, out ulong upper) || upper != bits))
                return false;
            value = BitConverter.Int32BitsToSingle((int)bits);
            return true;
        }

        static bool Compute(ulong w, int q, int explicitBits, int minimumExponent, int infinitePower, int minRoundToEven, int maxRoundToEven, out ulong bits)
        {
            bits = 0;
            int lz = LeadingZeros(w);
            w <<= lz;
            int index = 2 * (q - SmallestPower);
            ulong low = Ryu.UMul128(w, powers[index], out ulong high);
            ulong precisionMask = ulong.MaxValue >> (explicitBits + 3);
            if ((high & precisionMask) == precisionMask)
            {
                Ryu.UMul128(w, powers[index + 1], out ulong secondHigh);
                low += secondHigh;
                if (secondHigh > low)
                    high++;
            }
            if (low == ulong.MaxValue && (q < -27 || q > 55))
                return false;

            int upperBit = (int)(high >> 63);
            int shift = upperBit + 64 - explicitBits - 3;
            ulong mantissa = high >> shift;
            int power2 = ((217706 * q) >> 16) + 63 + upperBit - lz - minimumExponent;
            if (power2 <= 0)
            {
                if (-power2 + 1 >= 64)
                    return true;
                mantissa >>= -power2 + 1;
                mantissa += mantissa & 1;
                mantissa >>= 1;
                power2 = mantissa < 1UL << explicitBits ? 0 : 1;
                bits = ((ulong)power2 << explicitBits) | mantissa;
                return true;
            }
            if (low <= 1 && q >= minRoundToEven && q <= maxRoundToEven && (mantissa & 3) == 1 && (mantissa << shift) == high)
                mantissa &= ~1UL;
            mantissa += mantissa & 1;
            mantissa >>= 1;
            if (mantissa >= 2UL << explicitBits)
            {
                mantissa = 1UL << explicitBits;
                power2++;
            }
            mantissa &= ~(1UL << explicitBits);
            if (power2 >= infinitePower)
            {
                power2 = infinitePower;
                mantissa = 0;
            }
            bits = ((ulong)power2 << explicitBits) | mantissa;
            return true;
        }

        static int LeadingZeros(ulong w)
        {
            int n = 0;
            for (ulong bit = 1UL << 63; (w & bit) == 0; bit >>= 1)
                n++;
            return n;
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
    }
}
