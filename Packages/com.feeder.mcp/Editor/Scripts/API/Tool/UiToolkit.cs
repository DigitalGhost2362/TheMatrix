#nullable enable
using System;
using System.IO;
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_UiToolkit
    {
        internal static string RequireAssetPath(string? path, params string[] extensions)
        {
            var p = (path ?? string.Empty).Trim().Replace('\\', '/');
            if (p.Length == 0)
                throw new ArgumentException("path is required.");
            if (!(p.StartsWith("Assets/", StringComparison.Ordinal) || p.StartsWith("Packages/", StringComparison.Ordinal)) || p.Contains("../"))
                throw new ArgumentException($"path '{p}' must start with 'Assets/' (or a writable 'Packages/' folder) and must not contain '..'.");
            var ext = Path.GetExtension(p);
            if (extensions.Length > 0 && Array.FindIndex(extensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) < 0)
                throw new ArgumentException($"path '{p}' must end with {string.Join(" or ", extensions)}.");
            return p;
        }
    }
}
