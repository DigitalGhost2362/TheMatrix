#nullable enable

namespace Feeder.Json
{
    public ref partial struct Utf8JsonReader
    {
        // the line and the byte offset in that line right after the current token, as the reference reader reports them
        internal readonly void GetPosition(out long line, out long bytePositionInLine)
        {
            long lines = 0;
            int lineStart = 0;
            for (int i = 0; i < consumed; i++)
            {
                if (buffer[i] == '\n')
                {
                    lines++;
                    lineStart = i + 1;
                }
            }
            line = lines;
            bytePositionInLine = consumed - lineStart;
        }

        internal readonly JsonException StartOfValueError() =>
            Error("'" + Show(buffer[tokenStart]) + "' is an invalid start of a value.", tokenStart);
    }
}
