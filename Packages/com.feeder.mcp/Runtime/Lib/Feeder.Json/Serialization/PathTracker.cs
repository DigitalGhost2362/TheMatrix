#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Feeder.Json.Serialization
{
    // exception filters record the JSON path while the exception passes, innermost segment first; every public entry point takes it
    internal static class PathTracker
    {
        static readonly char[] special = { '.', ' ', '\'', '/', '"', '[', ']', '(', ')', '\t', '\n', '\r', '\f', '\b', '\\', '\u0085', (char)0x2028, (char)0x2029 };

        [ThreadStatic]
        static List<object>? segments;

        [ThreadStatic]
        static Type? failedType;

        internal static Type? FailedType => failedType;

        internal static bool Add(object segment)
        {
            (segments ??= new List<object>()).Add(segment);
            return false;
        }

        internal static bool SetType(Type type)
        {
            if (failedType == null)
                failedType = type;
            return false;
        }

        internal static string Take()
        {
            var path = new StringBuilder("$");
            List<object>? list = segments;
            if (list != null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] is int index)
                        path.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
                    else if (((string)list[i]).IndexOfAny(special) >= 0)
                        path.Append("['").Append((string)list[i]).Append("']");
                    else
                        path.Append('.').Append((string)list[i]);
                }
                list.Clear();
            }
            failedType = null;
            return path.ToString();
        }
    }
}
