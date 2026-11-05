using System.Text;
using System.Text.Json;
using Feeder.Matrix.Backends;
using Feeder.Matrix.Unity;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Tools;

/// <summary>
/// Read-only MCP resources: what the Unity editor looks like right now (link state, selection, open scenes, recent console
/// errors) and templates for any read-only query. Everything except the link state is read by forwarding a call of the
/// read-only <c>rql-query</c> tool to Unity, so a resource never changes the editor and never needs the editor lock.
/// </summary>
public sealed class MatrixResources(UnityLinkRegistry registry)
{
    public const string QueryTool = "rql-query";
    const string Scheme = "matrix://";
    const string Json = "application/json";
    const string Plain = "text/plain";
    const int MaxRows = 200;

    static readonly string[] LogTypes = ["Error", "Warning", "Log", "Exception", "Assert"];

    sealed record Entry(string Uri, string Name, string Description, string? Query);

    static readonly Entry[] Entries =
    [
        new("matrix://editor/state", "Editor state", "Whether the Unity editor is linked, its play/compile state, project and plugin version. Works while Unity reloads.", null),
        new("matrix://editor/selection", "Selection", "The objects selected in the Unity editor (name and hierarchy path).", "selection | select name, $path"),
        new("matrix://editor/scenes", "Open scenes", "The scenes open in the Unity editor.", "scenes"),
        new("matrix://console/errors", "Recent console errors", "The latest error and exception lines of the Unity console.",
            "logs | where type == \"Error\" or type == \"Exception\" | take 30"),
    ];

    public ListResourcesResult List()
        => new() { Resources = [.. Entries.Select(e => new Resource { Uri = e.Uri, Name = e.Name, Description = e.Description, MimeType = Plain })] };

    public ListResourceTemplatesResult Templates()
        => new()
        {
            ResourceTemplates =
            [
                new ResourceTemplate
                {
                    UriTemplate = "matrix://rql/{query}",
                    Name = "Read-only query",
                    Description = "Result of a read-only rql-query pipeline over the open scenes, assets and console. {query} is URL-encoded, e.g. matrix://rql/scenes.",
                    MimeType = Plain,
                },
                new ResourceTemplate
                {
                    UriTemplate = "matrix://console/{type}/{count}",
                    Name = "Console lines by type",
                    Description = $"The latest {{count}} (1-{MaxRows}) Unity console lines of {{type}}: {string.Join(", ", LogTypes)}.",
                    MimeType = Plain,
                },
            ],
        };

    public async ValueTask<ReadResourceResult> ReadAsync(string? uri, IUnityBackend backend, CancellationToken ct)
    {
        if (uri is null || !uri.StartsWith(Scheme, StringComparison.Ordinal))
            throw NotFound(uri);
        var entry = Entries.FirstOrDefault(e => string.Equals(e.Uri, uri, StringComparison.Ordinal));
        string query;
        if (entry is { Query: null })
            return Result(uri, Json, StateJson());
        if (entry is not null)
            query = entry.Query!;
        else if (uri.StartsWith("matrix://rql/", StringComparison.Ordinal))
            query = Uri.UnescapeDataString(uri["matrix://rql/".Length..]);
        else if (ConsoleQuery(uri) is { } console)
            query = console;
        else
            throw NotFound(uri);

        if (string.IsNullOrWhiteSpace(query))
            throw new McpProtocolException($"Empty query in '{uri}'.", McpErrorCode.InvalidParams);
        var args = new Dictionary<string, JsonElement> { ["query"] = JsonSerializer.SerializeToElement(query) };
        CallToolResult call;
        try
        {
            call = await backend.CallToolAsync(new CallToolRequestParams { Name = QueryTool, Arguments = args }, ct);
        }
        catch (McpProtocolException e)
        {
            throw new McpProtocolException($"Could not read '{uri}' from Unity: {e.Message}", McpErrorCode.InternalError);
        }
        var text = string.Join("\n", (call.Content ?? []).OfType<TextContentBlock>().Select(b => b.Text));
        if (call.IsError == true)
            throw new McpProtocolException($"Could not read '{uri}': {text}", McpErrorCode.InternalError);
        return Result(uri, Plain, text);
    }

    // matrix://console/{type}/{count}
    static string? ConsoleQuery(string uri)
    {
        const string prefix = "matrix://console/";
        if (!uri.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var parts = uri[prefix.Length..].Split('/');
        var type = LogTypes.FirstOrDefault(t => string.Equals(t, parts[0], StringComparison.OrdinalIgnoreCase));
        if (type is null || parts.Length > 2)
            return null;
        var count = 30;
        if (parts.Length == 2 && !(int.TryParse(parts[1], out count) && count is >= 1 and <= MaxRows))
            return null;
        return $"logs | where type == \"{type}\" | take {count}";
    }

    string StateJson()
    {
        var link = registry.GetLatestLink();
        var sb = new StringBuilder();
        using (var stream = new MemoryStream())
        {
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteBoolean("linked", link is not null);
                w.WriteBoolean("connected", link is { Connected: true });
                w.WriteString("editorState", link?.EditorState.ToString());
                w.WriteString("projectRoot", link?.ProjectRoot);
                w.WriteString("pluginVersion", link?.PluginVersion);
                w.WriteNumber("toolCount", link?.Registry?.Tools.Length ?? 0);
                w.WriteEndObject();
            }
            sb.Append(Encoding.UTF8.GetString(stream.ToArray()));
        }
        return sb.ToString();
    }

    static ReadResourceResult Result(string uri, string mime, string text)
        => new() { Contents = [new TextResourceContents { Uri = uri, MimeType = mime, Text = text }] };

    static McpProtocolException NotFound(string? uri)
        => new($"Unknown resource: '{uri}'", McpErrorCode.InvalidParams);
}
