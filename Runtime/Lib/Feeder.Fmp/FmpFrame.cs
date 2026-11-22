using System;
using System.Globalization;
using System.Text;

namespace Feeder.Fmp
{
    internal readonly struct Slice
    {
        public readonly int Start;
        public readonly int End;

        public Slice(int start, int end)
        {
            Start = start;
            End = end;
        }

        public bool IsEmpty => End <= Start;
    }

    internal struct FrameFields
    {
        public int Type;
        public Slice InvocationId;
        public Slice Target;
        public Slice Arguments;
        public Slice Result;
        public Slice Error;
        public Slice AllowReconnect;
    }

    // hub frames are single json objects; their payloads are located by offset and never parsed
    internal static class FmpFrame
    {
        // json strings cannot hold a raw 0x1E, so splitting a byte stream on it is safe
        public const byte Separator = 0x1E;

        public static bool TryScan(byte[] b, int start, int end, out FrameFields f)
        {
            f = default(FrameFields);
            f.Type = -1;
            int i = SkipWhite(b, start, end);
            if (i >= end || b[i] != '{')
                return false;
            i = SkipWhite(b, i + 1, end);
            if (i < end && b[i] == '}')
                return SkipWhite(b, i + 1, end) == end;
            while (true)
            {
                if (i >= end || b[i] != '"')
                    return false;
                int afterKey = SkipString(b, i, end);
                if (afterKey < 0)
                    return false;
                int keyStart = i + 1;
                int keyLength = afterKey - 1 - keyStart;
                i = SkipWhite(b, afterKey, end);
                if (i >= end || b[i] != ':')
                    return false;
                i = SkipWhite(b, i + 1, end);
                int afterValue = SkipValue(b, i, end);
                if (afterValue < 0)
                    return false;
                var value = new Slice(i, afterValue);
                if (KeyIs(b, keyStart, keyLength, "type"))
                {
                    if (!TryParseType(b, value, out f.Type))
                        return false;
                }
                else if (KeyIs(b, keyStart, keyLength, "invocationId")) f.InvocationId = value;
                else if (KeyIs(b, keyStart, keyLength, "target")) f.Target = value;
                else if (KeyIs(b, keyStart, keyLength, "arguments")) f.Arguments = value;
                else if (KeyIs(b, keyStart, keyLength, "result")) f.Result = value;
                else if (KeyIs(b, keyStart, keyLength, "error")) f.Error = value;
                else if (KeyIs(b, keyStart, keyLength, "allowReconnect")) f.AllowReconnect = value;
                i = SkipWhite(b, afterValue, end);
                if (i >= end)
                    return false;
                if (b[i] == ',')
                {
                    i = SkipWhite(b, i + 1, end);
                    continue;
                }
                return b[i] == '}' && SkipWhite(b, i + 1, end) == end;
            }
        }

        // the text of a json string value, or null when the value is absent or not a string
        public static string Text(byte[] b, Slice s)
        {
            if (s.End - s.Start < 2 || b[s.Start] != '"')
                return null;
            string text = Encoding.UTF8.GetString(b, s.Start + 1, s.End - s.Start - 2);
            return text.IndexOf('\\') < 0 ? text : Unescape(text);
        }

        public static string Raw(byte[] b, Slice s) => Encoding.UTF8.GetString(b, s.Start, s.End - s.Start);

        public static bool IsTrue(byte[] b, Slice s) => s.End - s.Start == 4 && b[s.Start] == 't';

        public static byte[] Invocation(string invocationId, string target, string argumentsJson)
        {
            if (string.IsNullOrEmpty(target))
                throw new ArgumentException("A target is required.", nameof(target));
            if (!IsArray(argumentsJson))
                throw new ArgumentException("The arguments must be a JSON array.", nameof(argumentsJson));
            var head = new StringBuilder("{\"type\":1,");
            if (invocationId != null)
            {
                head.Append("\"invocationId\":");
                Quote(head, invocationId);
                head.Append(',');
            }
            head.Append("\"target\":");
            Quote(head, target);
            head.Append(",\"arguments\":");
            string prefix = head.ToString();
            int prefixBytes = Encoding.UTF8.GetByteCount(prefix);
            int argumentBytes = Encoding.UTF8.GetByteCount(argumentsJson);
            var frame = new byte[prefixBytes + argumentBytes + 2];
            Encoding.UTF8.GetBytes(prefix, 0, prefix.Length, frame, 0);
            Encoding.UTF8.GetBytes(argumentsJson, 0, argumentsJson.Length, frame, prefixBytes);
            frame[prefixBytes + argumentBytes] = (byte)'}';
            frame[prefixBytes + argumentBytes + 1] = Separator;
            return frame;
        }

        public static byte[] Completion(string invocationId, string error)
        {
            var text = new StringBuilder("{\"type\":3,\"invocationId\":");
            Quote(text, invocationId);
            if (error == null)
                text.Append(",\"result\":null");
            else
            {
                text.Append(",\"error\":");
                Quote(text, error);
            }
            text.Append("}\u001e");
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        static bool IsArray(string json)
        {
            if (json == null)
                return false;
            int first = 0;
            int last = json.Length - 1;
            while (first < last && IsWhite(json[first]))
                first++;
            while (last > first && IsWhite(json[last]))
                last--;
            return first < last && json[first] == '[' && json[last] == ']';
        }

        static void Quote(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\')
                    text.Append('\\').Append(c);
                else if (c < ' ')
                    text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    text.Append(c);
            }
            text.Append('"');
        }

        static string Unescape(string text)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i + 1 == text.Length)
                {
                    result.Append(c);
                    continue;
                }
                c = text[++i];
                switch (c)
                {
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'u':
                        if (i + 4 < text.Length && int.TryParse(text.Substring(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                        {
                            result.Append((char)code);
                            i += 4;
                        }
                        else
                            result.Append(c);
                        break;
                    default: result.Append(c); break;
                }
            }
            return result.ToString();
        }

        static bool TryParseType(byte[] b, Slice s, out int type)
        {
            type = 0;
            if (s.IsEmpty || s.End - s.Start > 3)
                return false;
            for (int i = s.Start; i < s.End; i++)
            {
                int digit = b[i] - '0';
                if (digit < 0 || digit > 9)
                    return false;
                type = type * 10 + digit;
            }
            return true;
        }

        static bool KeyIs(byte[] b, int start, int length, string name)
        {
            if (length != name.Length)
                return false;
            for (int i = 0; i < length; i++)
            {
                if (b[start + i] != name[i])
                    return false;
            }
            return true;
        }

        static bool IsWhite(int c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

        static int SkipWhite(byte[] b, int i, int end)
        {
            while (i < end && IsWhite(b[i]))
                i++;
            return i;
        }

        // returns the index after the closing quote, or -1 when the string never ends
        static int SkipString(byte[] b, int i, int end)
        {
            for (i++; i < end; i++)
            {
                byte c = b[i];
                if (c == '"')
                    return i + 1;
                if (c == '\\')
                    i++;
            }
            return -1;
        }

        // returns the index after the value that starts at i, or -1 when it is cut short
        static int SkipValue(byte[] b, int i, int end)
        {
            if (i >= end)
                return -1;
            byte c = b[i];
            if (c == '"')
                return SkipString(b, i, end);
            int start = i;
            if (c == '{' || c == '[')
            {
                int depth = 0;
                for (; i < end; i++)
                {
                    c = b[i];
                    if (c == '"')
                    {
                        i = SkipString(b, i, end);
                        if (i < 0)
                            return -1;
                        i--;
                    }
                    else if (c == '{' || c == '[')
                        depth++;
                    else if ((c == '}' || c == ']') && --depth == 0)
                        return i + 1;
                }
                return -1;
            }
            while (i < end && b[i] != ',' && b[i] != '}' && b[i] != ']' && b[i] > ' ')
                i++;
            return i > start ? i : -1;
        }
    }
}
