using Feeder.Json;
using Feeder.Json.Serialization;

namespace Feeder.ToolHost.Common.Utils
{
public static class JsonOptions
{
	private const string NullJson = "null";

	public static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions
	{
		PropertyNamingPolicy = null,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true,
		AllowTrailingCommas = false,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		ReadCommentHandling = JsonCommentHandling.Skip,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		Converters = { (JsonConverter)new JsonStringEnumConverter() }
	};

	public static string ToPrettyJson<T>(this T obj)
	{
		if (obj == null)
		{
			return "null";
		}
		return JsonSerializer.Serialize(obj, Pretty);
	}
}
}
