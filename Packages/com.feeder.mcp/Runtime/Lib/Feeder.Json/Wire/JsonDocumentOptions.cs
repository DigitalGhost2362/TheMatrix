#nullable enable
using System;

namespace Feeder.Json
{
    public struct JsonDocumentOptions
    {
        int maxDepth;
        JsonCommentHandling commentHandling;

        public JsonCommentHandling CommentHandling
        {
            readonly get => commentHandling;
            set
            {
                if (value > JsonCommentHandling.Skip)
                    throw new ArgumentOutOfRangeException(nameof(value), "Comments cannot be stored in a JsonDocument, only the Skip and Disallow comment handling modes are supported.");
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

        internal readonly JsonReaderOptions GetReaderOptions() => new JsonReaderOptions
        {
            AllowTrailingCommas = AllowTrailingCommas,
            CommentHandling = CommentHandling,
            MaxDepth = MaxDepth,
        };
    }
}
