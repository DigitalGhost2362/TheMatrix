using System.Globalization;
using System.Text;

namespace Feeder.Json.ParityTests;

internal static class TestSupport
{
    // test text is ASCII; <HH> stands for one raw byte so invalid UTF-8 and control bytes stay visible in source
    public static byte[] J(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' && i + 3 < text.Length && text[i + 3] == '>' && IsHex(text[i + 1]) && IsHex(text[i + 2]))
            {
                bytes.Add(byte.Parse(text.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 3;
                continue;
            }
            bytes.AddRange(Encoding.UTF8.GetBytes(text[i].ToString()));
        }
        return bytes.ToArray();
    }

    static bool IsHex(char c) => Uri.IsHexDigit(c);

    // readable form of arbitrary bytes for trace diffs
    public static string Show(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            if (b >= 0x20 && b < 0x7F && b != '<')
                sb.Append((char)b);
            else
                sb.Append('<').Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append('>');
        }
        return sb.ToString();
    }

    public static string Show(string? text)
    {
        if (text == null)
            return "(null)";
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 0x20 && c < 0x7F && c != '<')
                sb.Append(c);
            else
                sb.Append("<u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('>');
        }
        return sb.ToString();
    }

    // both libraries throw subclasses of their own JsonException; the comparison only cares about the family, plus
    // whether the exception carries the marker that makes a serializer rethrow it as JsonException
    public static string Name(Exception e)
    {
        string name = e is System.Text.Json.JsonException || e is JsonException ? "JsonException" : e.GetType().Name;
        return e.Source == "System.Text.Json.Rethrowable" || e.Source == JsonText.RethrowableSource ? name + "+rethrowable" : name;
    }

    public static string Try(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception e)
        {
            return "!" + Name(e);
        }
    }

    public static string Bits(double d) => BitConverter.DoubleToInt64Bits(d).ToString("X16", CultureInfo.InvariantCulture);

    public static string Bits(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8", CultureInfo.InvariantCulture);

    public static string Bits(decimal m) => string.Join(",", decimal.GetBits(m).Select(i => i.ToString("X8", CultureInfo.InvariantCulture)));

    public static System.Text.Json.JsonReaderOptions ToReference(JsonReaderOptions o) => new()
    {
        AllowTrailingCommas = o.AllowTrailingCommas,
        CommentHandling = (System.Text.Json.JsonCommentHandling)o.CommentHandling,
        MaxDepth = o.MaxDepth,
    };

    public static System.Text.Json.JsonDocumentOptions ToReference(JsonDocumentOptions o) => new()
    {
        AllowTrailingCommas = o.AllowTrailingCommas,
        CommentHandling = (System.Text.Json.JsonCommentHandling)o.CommentHandling,
        MaxDepth = o.MaxDepth,
    };

    public static System.Text.Json.JsonWriterOptions ToReference(JsonWriterOptions o) => new()
    {
        Indented = o.Indented,
        SkipValidation = o.SkipValidation,
        MaxDepth = o.MaxDepth,
        Encoder = o.Encoder == null ? null
            : o.Encoder == JavaScriptEncoder.UnsafeRelaxedJsonEscaping ? System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            : System.Text.Encodings.Web.JavaScriptEncoder.Default,
    };
}
