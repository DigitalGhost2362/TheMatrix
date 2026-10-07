#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.MCP.Data;

namespace Feeder.MCP.Data
{
    public class CopyAssetsResponse
    {
        [Description("List of copied assets.")]
        public List<AssetObjectRef>? CopiedAssets { get; set; }
        [Description("List of errors encountered during copy operations.")]
        public List<string>? Errors { get; set; }
    }
}
