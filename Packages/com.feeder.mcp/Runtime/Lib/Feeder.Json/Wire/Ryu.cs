#nullable enable
using System.Numerics;

namespace Feeder.Json
{
    // shortest round-trip digits of binary64/binary32 (Ryu, Ulf Adams 2018); the 128-bit power-of-5 tables are computed on first use
    internal static class Ryu
    {
        const int Pow5InvBitCount = 125;
        const int Pow5BitCount = 125;
        const int FloatPow5InvBitCount = Pow5InvBitCount - 64;
        const int FloatPow5BitCount = Pow5BitCount - 64;

        // two words per entry: low then high
        static readonly ulong[] pow5InvSplit = BuildTable(342, true);
        static readonly ulong[] pow5Split = BuildTable(326, false);

        static ulong[] BuildTable(int count, bool inverse)
        {
            var table = new ulong[count * 2];
            var mask = new BigInteger(ulong.MaxValue);
            BigInteger pow = BigInteger.One;
            for (int i = 0; i < count; i++, pow *= 5)
            {
                int bits = Pow5Bits(i);
                BigInteger v = inverse
                    ? (BigInteger.One << (bits - 1 + Pow5InvBitCount)) / pow + 1
                    : bits > Pow5BitCount ? pow >> (bits - Pow5BitCount) : pow << (Pow5BitCount - bits);
                table[i * 2] = (ulong)(v & mask);
                table[i * 2 + 1] = (ulong)(v >> 64);
            }
            return table;
        }

        // value = result * 10^exponent with the fewest digits that still parse back to the same double
        internal static ulong Double(ulong ieeeMantissa, int ieeeExponent, out int exponent)
        {
            int e2;
            ulong m2;
            if (ieeeExponent == 0)
            {
                e2 = 1 - 1023 - 52 - 2;
                m2 = ieeeMantissa;
            }
            else
            {
                e2 = ieeeExponent - 1023 - 52 - 2;
                m2 = (1UL << 52) | ieeeMantissa;
            }
            bool acceptBounds = (m2 & 1) == 0;
            ulong mv = 4 * m2;
            uint mmShift = ieeeMantissa != 0 || ieeeExponent <= 1 ? 1u : 0u;

            ulong vr, vp, vm;
            int e10;
            bool vmIsTrailingZeros = false, vrIsTrailingZeros = false;
            if (e2 >= 0)
            {
                int q = Log10Pow2(e2) - (e2 > 3 ? 1 : 0);
                e10 = q;
                int k = Pow5InvBitCount + Pow5Bits(q) - 1;
                int i = -e2 + q + k;
                vr = MulShiftAll64(m2, pow5InvSplit, q, i, out vp, out vm, mmShift);
                if (q <= 21)
                {
                    if (mv % 5 == 0) vrIsTrailingZeros = MultipleOfPowerOf5(mv, q);
                    else if (acceptBounds) vmIsTrailingZeros = MultipleOfPowerOf5(mv - 1 - mmShift, q);
                    else if (MultipleOfPowerOf5(mv + 2, q)) vp--;
                }
            }
            else
            {
                int q = Log10Pow5(-e2) - (-e2 > 1 ? 1 : 0);
                e10 = q + e2;
                int i = -e2 - q;
                int k = Pow5Bits(i) - Pow5BitCount;
                int j = q - k;
                vr = MulShiftAll64(m2, pow5Split, i, j, out vp, out vm, mmShift);
                if (q <= 1)
                {
                    vrIsTrailingZeros = true;
                    if (acceptBounds) vmIsTrailingZeros = mmShift == 1;
                    else vp--;
                }
                else if (q < 63)
                {
                    vrIsTrailingZeros = MultipleOfPowerOf2(mv, q);
                }
            }

            int removed = 0;
            int lastRemovedDigit = 0;
            ulong output;
            if (vmIsTrailingZeros || vrIsTrailingZeros)
            {
                while (vp / 10 > vm / 10)
                {
                    vmIsTrailingZeros &= vm % 10 == 0;
                    vrIsTrailingZeros &= lastRemovedDigit == 0;
                    lastRemovedDigit = (int)(vr % 10);
                    vr /= 10;
                    vp /= 10;
                    vm /= 10;
                    removed++;
                }
                if (vmIsTrailingZeros)
                {
                    while (vm % 10 == 0)
                    {
                        vrIsTrailingZeros &= lastRemovedDigit == 0;
                        lastRemovedDigit = (int)(vr % 10);
                        vr /= 10;
                        vp /= 10;
                        vm /= 10;
                        removed++;
                    }
                }
                if (vrIsTrailingZeros && lastRemovedDigit == 5 && vr % 2 == 0)
                    lastRemovedDigit = 4;
                output = vr + ((vr == vm && (!acceptBounds || !vmIsTrailingZeros)) || lastRemovedDigit >= 5 ? 1UL : 0UL);
            }
            else
            {
                bool roundUp = false;
                if (vp / 100 > vm / 100)
                {
                    roundUp = vr % 100 >= 50;
                    vr /= 100;
                    vp /= 100;
                    vm /= 100;
                    removed += 2;
                }
                while (vp / 10 > vm / 10)
                {
                    roundUp = vr % 10 >= 5;
                    vr /= 10;
                    vp /= 10;
                    vm /= 10;
                    removed++;
                }
                output = vr + (vr == vm || roundUp ? 1UL : 0UL);
            }
            exponent = e10 + removed;
            return output;
        }

        internal static uint Single(uint ieeeMantissa, int ieeeExponent, out int exponent)
        {
            int e2;
            uint m2;
            if (ieeeExponent == 0)
            {
                e2 = 1 - 127 - 23 - 2;
                m2 = ieeeMantissa;
            }
            else
            {
                e2 = ieeeExponent - 127 - 23 - 2;
                m2 = (1u << 23) | ieeeMantissa;
            }
            bool acceptBounds = (m2 & 1) == 0;
            uint mv = 4 * m2;
            uint mp = 4 * m2 + 2;
            uint mmShift = ieeeMantissa != 0 || ieeeExponent <= 1 ? 1u : 0u;
            uint mm = 4 * m2 - 1 - mmShift;

            uint vr, vp, vm;
            int e10;
            bool vmIsTrailingZeros = false, vrIsTrailingZeros = false;
            int lastRemovedDigit = 0;
            if (e2 >= 0)
            {
                int q = Log10Pow2(e2);
                e10 = q;
                int k = FloatPow5InvBitCount + Pow5Bits(q) - 1;
                int i = -e2 + q + k;
                vr = MulPow5InvDivPow2(mv, q, i);
                vp = MulPow5InvDivPow2(mp, q, i);
                vm = MulPow5InvDivPow2(mm, q, i);
                if (q != 0 && (vp - 1) / 10 <= vm / 10)
                {
                    int l = FloatPow5InvBitCount + Pow5Bits(q - 1) - 1;
                    lastRemovedDigit = (int)(MulPow5InvDivPow2(mv, q - 1, -e2 + q - 1 + l) % 10);
                }
                if (q <= 9)
                {
                    if (mv % 5 == 0) vrIsTrailingZeros = MultipleOfPowerOf5(mv, q);
                    else if (acceptBounds) vmIsTrailingZeros = MultipleOfPowerOf5(mm, q);
                    else if (MultipleOfPowerOf5(mp, q)) vp--;
                }
            }
            else
            {
                int q = Log10Pow5(-e2);
                e10 = q + e2;
                int i = -e2 - q;
                int k = Pow5Bits(i) - FloatPow5BitCount;
                int j = q - k;
                vr = MulPow5DivPow2(mv, i, j);
                vp = MulPow5DivPow2(mp, i, j);
                vm = MulPow5DivPow2(mm, i, j);
                if (q != 0 && (vp - 1) / 10 <= vm / 10)
                {
                    j = q - 1 - (Pow5Bits(i + 1) - FloatPow5BitCount);
                    lastRemovedDigit = (int)(MulPow5DivPow2(mv, i + 1, j) % 10);
                }
                if (q <= 1)
                {
                    vrIsTrailingZeros = true;
                    if (acceptBounds) vmIsTrailingZeros = mmShift == 1;
                    else vp--;
                }
                else if (q < 31)
                {
                    vrIsTrailingZeros = MultipleOfPowerOf2(mv, q - 1);
                }
            }

            int removed = 0;
            uint output;
            if (vmIsTrailingZeros || vrIsTrailingZeros)
            {
                while (vp / 10 > vm / 10)
                {
                    vmIsTrailingZeros &= vm % 10 == 0;
                    vrIsTrailingZeros &= lastRemovedDigit == 0;
                    lastRemovedDigit = (int)(vr % 10);
                    vr /= 10;
                    vp /= 10;
                    vm /= 10;
                    removed++;
                }
                if (vmIsTrailingZeros)
                {
                    while (vm % 10 == 0)
                    {
                        vrIsTrailingZeros &= lastRemovedDigit == 0;
                        lastRemovedDigit = (int)(vr % 10);
                        vr /= 10;
                        vp /= 10;
                        vm /= 10;
                        removed++;
                    }
                }
                if (vrIsTrailingZeros && lastRemovedDigit == 5 && vr % 2 == 0)
                    lastRemovedDigit = 4;
                output = vr + ((vr == vm && (!acceptBounds || !vmIsTrailingZeros)) || lastRemovedDigit >= 5 ? 1u : 0u);
            }
            else
            {
                while (vp / 10 > vm / 10)
                {
                    lastRemovedDigit = (int)(vr % 10);
                    vr /= 10;
                    vp /= 10;
                    vm /= 10;
                    removed++;
                }
                output = vr + (vr == vm || lastRemovedDigit >= 5 ? 1u : 0u);
            }
            exponent = e10 + removed;
            return output;
        }

        static int Pow5Bits(int e) => (int)(((uint)e * 1217359) >> 19) + 1;

        static int Log10Pow2(int e) => (int)(((uint)e * 78913) >> 18);

        static int Log10Pow5(int e) => (int)(((uint)e * 732923) >> 20);

        static bool MultipleOfPowerOf5(ulong value, int p)
        {
            int count = 0;
            for (; value % 5 == 0; value /= 5)
                count++;
            return count >= p;
        }

        static bool MultipleOfPowerOf2(ulong value, int p) => (value & ((1UL << p) - 1)) == 0;

        static ulong MulShiftAll64(ulong m, ulong[] table, int index, int j, out ulong vp, out ulong vm, uint mmShift)
        {
            ulong lo = table[index * 2], hi = table[index * 2 + 1];
            vp = MulShift64(4 * m + 2, lo, hi, j);
            vm = MulShift64(4 * m - 1 - mmShift, lo, hi, j);
            return MulShift64(4 * m, lo, hi, j);
        }

        static ulong MulShift64(ulong m, ulong mulLo, ulong mulHi, int j)
        {
            ulong low1 = UMul128(m, mulHi, out ulong high1);
            UMul128(m, mulLo, out ulong high0);
            ulong sum = high0 + low1;
            if (sum < high0)
                high1++;
            int dist = j - 64;
            return (high1 << (64 - dist)) | (sum >> dist);
        }

        internal static ulong UMul128(ulong a, ulong b, out ulong high)
        {
            ulong aLo = (uint)a, aHi = a >> 32, bLo = (uint)b, bHi = b >> 32;
            ulong b00 = aLo * bLo, b01 = aLo * bHi, b10 = aHi * bLo, b11 = aHi * bHi;
            ulong mid1 = b10 + (b00 >> 32);
            ulong mid2 = b01 + (uint)mid1;
            high = b11 + (mid1 >> 32) + (mid2 >> 32);
            return (mid2 << 32) | (uint)b00;
        }

        static uint MulShift32(uint m, ulong factor, int shift)
        {
            ulong bits0 = m * (ulong)(uint)factor;
            ulong bits1 = m * (factor >> 32);
            ulong sum = (bits0 >> 32) + bits1;
            return (uint)(sum >> (shift - 32));
        }

        static uint MulPow5InvDivPow2(uint m, int q, int j) => MulShift32(m, pow5InvSplit[q * 2 + 1] + 1, j);

        static uint MulPow5DivPow2(uint m, int i, int j) => MulShift32(m, pow5Split[i * 2 + 1], j);
    }
}
