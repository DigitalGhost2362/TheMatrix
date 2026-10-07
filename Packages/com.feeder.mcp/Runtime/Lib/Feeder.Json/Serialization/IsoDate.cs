#nullable enable
using System;

namespace Feeder.Json.Serialization
{
    // the ISO 8601 subset the reference reader accepts: date, optional time, up to 16 fraction digits (7 kept), Z or +-hh[:mm]
    internal static class IsoDate
    {
        // the round-trip "O" text minus the fraction's trailing zeros, and minus its dot when nothing is left
        internal static string Format(string roundTrip)
        {
            int last = 26;
            while (last > 19 && roundTrip[last] == '0')
                last--;
            if (last == 19)
                last = 18;
            return roundTrip.Substring(0, last + 1) + roundTrip.Substring(27);
        }

        internal static bool TryParse(string text, out DateTime value)
        {
            value = default;
            if (!TryParse(text, out Parts parts))
                return false;
            if (parts.Offset == 'Z')
                return TryCreate(parts, DateTimeKind.Utc, out value);
            if (parts.Offset != '\0')
            {
                if (!TryCreate(parts, out DateTimeOffset offset))
                    return false;
                value = offset.LocalDateTime;
                return true;
            }
            return TryCreate(parts, DateTimeKind.Unspecified, out value);
        }

        internal static bool TryParse(string text, out DateTimeOffset value)
        {
            value = default;
            if (!TryParse(text, out Parts parts))
                return false;
            if (parts.Offset != '\0')
                return TryCreate(parts, out value);
            if (!TryCreate(parts, DateTimeKind.Local, out DateTime local))
                return false;
            try
            {
                value = new DateTimeOffset(local);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        struct Parts
        {
            internal int Year;
            internal int Month;
            internal int Day;
            internal int Hour;
            internal int Minute;
            internal int Second;
            internal int Fraction;
            internal char Offset;
            internal int OffsetHours;
            internal int OffsetMinutes;
        }

        static bool TryParse(string s, out Parts parts)
        {
            parts = default;
            if (s.Length < 10 || s.Length > 42)
                return false;
            if (!Digits(s, 0, 4, out parts.Year) || s[4] != '-' || !Digits(s, 5, 2, out parts.Month) || s[7] != '-' || !Digits(s, 8, 2, out parts.Day))
                return false;
            if (s.Length == 10)
                return true;
            if (s.Length < 16 || s[10] != 'T' || s[13] != ':' || !Digits(s, 11, 2, out parts.Hour) || !Digits(s, 14, 2, out parts.Minute))
                return false;
            if (s.Length == 16)
                return true;
            int i = 16;
            if (s[i] == ':')
            {
                if (s.Length < 19 || !Digits(s, 17, 2, out parts.Second))
                    return false;
                if (s.Length == 19)
                    return true;
                i = 19;
                if (s[i] == '.')
                {
                    if (s.Length < 21)
                        return false;
                    int end = Math.Min(20 + 16, s.Length);
                    int read = 0;
                    for (i = 20; i < end && s[i] >= '0' && s[i] <= '9'; i++)
                    {
                        if (read < 7)
                        {
                            parts.Fraction = parts.Fraction * 10 + (s[i] - '0');
                            read++;
                        }
                    }
                    if (parts.Fraction != 0)
                    {
                        for (; read < 7; read++)
                            parts.Fraction *= 10;
                    }
                    if (i == s.Length)
                        return true;
                }
            }
            char token = s[i++];
            if (token == 'Z')
            {
                parts.Offset = 'Z';
                return i == s.Length;
            }
            if (token != '+' && token != '-')
                return false;
            parts.Offset = token;
            int length = s.Length - i;
            if (length < 2 || !Digits(s, i, 2, out parts.OffsetHours))
                return false;
            if (length == 2)
                return true;
            return length == 5 && s[i + 2] == ':' && Digits(s, i + 3, 2, out parts.OffsetMinutes);
        }

        static bool Digits(string s, int start, int count, out int value)
        {
            value = 0;
            for (int i = start; i < start + count; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                    return false;
                value = value * 10 + (s[i] - '0');
            }
            return true;
        }

        static bool TryCreate(Parts parts, DateTimeKind kind, out DateTime value)
        {
            value = default;
            if (parts.Year == 0 || parts.Month < 1 || parts.Month > 12 || parts.Day < 1 || parts.Day > DateTime.DaysInMonth(parts.Year, parts.Month)
                || parts.Hour > 23 || parts.Minute > 59 || parts.Second > 59)
                return false;
            value = new DateTime(parts.Year, parts.Month, parts.Day, parts.Hour, parts.Minute, parts.Second, kind).AddTicks(parts.Fraction);
            return true;
        }

        static bool TryCreate(Parts parts, out DateTimeOffset value)
        {
            value = default;
            if (parts.OffsetHours > 14 || parts.OffsetMinutes > 59 || (parts.OffsetHours == 14 && parts.OffsetMinutes != 0)
                || !TryCreate(parts, DateTimeKind.Unspecified, out DateTime local))
                return false;
            var offset = new TimeSpan(parts.OffsetHours, parts.OffsetMinutes, 0);
            try
            {
                value = new DateTimeOffset(local, parts.Offset == '-' ? -offset : offset);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}
