#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Feeder.Logging
{
    // the state handed to providers: ToString() renders the message template with the n-th {Name} taking the n-th argument
    internal readonly struct FormattedLogValues
    {
        const int MaxCachedFormats = 1024;
        const string NullMessage = "[null]";
        const string NullValue = "(null)";
        static readonly char[] Delimiters = { ',', ':' };
        static readonly ConcurrentDictionary<string, string> Formats = new ConcurrentDictionary<string, string>();
        static int cachedFormats;

        readonly string message;
        readonly string? compositeFormat;
        readonly object?[]? values;

        public FormattedLogValues(string? format, object?[]? values)
        {
            message = format ?? NullMessage;
            this.values = values;
            // without arguments the template is the message itself: braces and escapes stay as written
            compositeFormat = format != null && values != null && values.Length != 0 ? IndexedFormat(format) : null;
        }

        public override string ToString()
        {
            if (compositeFormat == null)
                return message;
            var arguments = new object[values!.Length];
            for (var i = 0; i < arguments.Length; i++)
                arguments[i] = FormatArgument(values[i]);
            return string.Format(CultureInfo.InvariantCulture, compositeFormat, arguments);
        }

        static string IndexedFormat(string format)
        {
            if (Formats.TryGetValue(format, out var cached))
                return cached;
            var indexed = ToIndexed(format);
            if (Volatile.Read(ref cachedFormats) < MaxCachedFormats && Formats.TryAdd(format, indexed))
                Interlocked.Increment(ref cachedFormats);
            return indexed;
        }

        // {Name} and {Name:spec} become {0} and {0:spec}; a template without a hole is used as it is
        static string ToIndexed(string format)
        {
            var builder = new StringBuilder();
            var scanIndex = 0;
            var endIndex = format.Length;
            var holes = 0;
            while (scanIndex < endIndex)
            {
                var openBraceIndex = FindBraceIndex(format, '{', scanIndex, endIndex);
                if (scanIndex == 0 && openBraceIndex == endIndex)
                    return format;

                var closeBraceIndex = FindBraceIndex(format, '}', openBraceIndex, endIndex);
                if (closeBraceIndex == endIndex)
                {
                    builder.Append(format, scanIndex, endIndex - scanIndex);
                    scanIndex = endIndex;
                }
                else
                {
                    var delimiterIndex = format.IndexOfAny(Delimiters, openBraceIndex, closeBraceIndex - openBraceIndex);
                    if (delimiterIndex == -1)
                        delimiterIndex = closeBraceIndex;
                    builder.Append(format, scanIndex, openBraceIndex - scanIndex + 1);
                    builder.Append(holes++);
                    builder.Append(format, delimiterIndex, closeBraceIndex - delimiterIndex + 1);
                    scanIndex = closeBraceIndex + 1;
                }
            }
            return builder.ToString();
        }

        // "{{prefix{{{Argument}}}suffix}}": '{' picks the last unescaped occurrence, '}' the first
        static int FindBraceIndex(string format, char brace, int startIndex, int endIndex)
        {
            var braceIndex = endIndex;
            var scanIndex = startIndex;
            var braceOccurrenceCount = 0;
            while (scanIndex < endIndex)
            {
                if (braceOccurrenceCount > 0 && format[scanIndex] != brace)
                {
                    if (braceOccurrenceCount % 2 == 0)
                    {
                        braceOccurrenceCount = 0;
                        braceIndex = endIndex;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (format[scanIndex] == brace)
                {
                    if (brace == '}')
                    {
                        if (braceOccurrenceCount == 0)
                            braceIndex = scanIndex;
                    }
                    else
                    {
                        braceIndex = scanIndex;
                    }
                    braceOccurrenceCount++;
                }
                scanIndex++;
            }
            return braceIndex;
        }

        static object FormatArgument(object? value)
        {
            if (value == null)
                return NullValue;
            if (value is string || !(value is IEnumerable enumerable))
                return value;
            var builder = new StringBuilder();
            var first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                    builder.Append(", ");
                builder.Append(item != null ? item.ToString() : NullValue);
                first = false;
            }
            return builder.ToString();
        }
    }
}
