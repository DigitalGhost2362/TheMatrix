#nullable enable
using System;
using System.Linq;
using Feeder.Json;

namespace Feeder.MCP.Player
{
    // JSON wire DTOs of FMP/1 for the player link; the property names match the matrix's records.
    internal sealed class PlayerError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    internal sealed class PlayerRegisterRequest
    {
        public string ProtocolVersion { get; set; } = "1";
        public string ProjectId { get; set; } = string.Empty;
        public string UnityInstanceId { get; set; } = string.Empty;
        public string ProjectPathHash { get; set; } = string.Empty;
        public string UnityVersion { get; set; } = string.Empty;
        public string PluginVersion { get; set; } = string.Empty;
        public string[] SupportedProtocolVersions { get; set; } = Array.Empty<string>();
        public string ToolRegistryHash { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
    }

    internal sealed class PlayerRegisterResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public string ChosenProtocolVersion { get; set; } = string.Empty;
        public PlayerError? Error { get; set; }
    }

    internal sealed class PlayerToolDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public byte[] InputSchemaJsonUtf8 { get; set; } = Array.Empty<byte>();
        public bool ReadOnly { get; set; }
        public string Target { get; set; } = "Runtime";
    }

    internal sealed class PlayerRegistrySnapshot
    {
        public string ProjectId { get; set; } = string.Empty;
        public string RegistryHash { get; set; } = string.Empty;
        public PlayerToolDescriptor[] Tools { get; set; } = Array.Empty<PlayerToolDescriptor>();
    }

    internal sealed class PlayerToolInvoke
    {
        public string OperationId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public byte[] ArgumentsJsonUtf8 { get; set; } = Array.Empty<byte>();
    }

    internal sealed class PlayerToolResult
    {
        public string OperationId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public byte[]? ContentJsonUtf8 { get; set; }
        public PlayerError? Error { get; set; }
    }

    internal static class PlayerJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string Arguments(params object[] values)
            => "[" + string.Join(",", values.Select(value => JsonSerializer.Serialize(value, value.GetType(), Options))) + "]";

        public static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

        public static T First<T>(string argumentsJson)
        {
            using (var document = JsonDocument.Parse(argumentsJson))
                return document.RootElement.EnumerateArray().First().Deserialize<T>(Options)!;
        }
    }
}
