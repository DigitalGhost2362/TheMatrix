#nullable enable
using System;

namespace Feeder.Json
{
    public struct JsonWriterOptions
    {
        internal const int DefaultMaxDepth = 1000;

        int maxDepth;

        public JavaScriptEncoder? Encoder { get; set; }

        public bool Indented { get; set; }

        public int MaxDepth
        {
            readonly get => maxDepth;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Max depth must be positive.");
                maxDepth = value;
            }
        }

        public bool SkipValidation { get; set; }
    }
}
