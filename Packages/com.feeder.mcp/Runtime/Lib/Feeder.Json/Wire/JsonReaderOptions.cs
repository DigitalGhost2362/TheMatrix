#nullable enable
using System;

namespace Feeder.Json
{
    public struct JsonReaderOptions
    {
        internal const int DefaultMaxDepth = 64;

        int maxDepth;
        JsonCommentHandling commentHandling;

        public JsonCommentHandling CommentHandling
        {
            readonly get => commentHandling;
            set
            {
                if (value > JsonCommentHandling.Allow)
                    throw new ArgumentOutOfRangeException(nameof(value), "The JsonCommentHandling enum must be set to one of the supported values.");
                commentHandling = value;
            }
        }

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

        public bool AllowTrailingCommas { get; set; }
    }
}
