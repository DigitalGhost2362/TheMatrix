#nullable enable
using System.Linq;
using Feeder.Json;

namespace Feeder.MCP.Editor.Matrix
{
    internal static class FmpJson
    {
        // the hub's own JSON defaults: camelCase names, case-insensitive reads, relaxed escaping
        static readonly JsonSerializerOptions Options = new()
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
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.EnumerateArray().First().Deserialize<T>(Options)!;
        }
    }
}
