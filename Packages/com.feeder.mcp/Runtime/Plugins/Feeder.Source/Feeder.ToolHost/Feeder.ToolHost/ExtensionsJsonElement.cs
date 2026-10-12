using System;
using System.IO;
using Feeder.Json;

namespace Feeder.ToolHost
{
public static class ExtensionsJsonElement
{
	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, int newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var value2) && value2 == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteNumber(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, uint newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.TryGetUInt32(out var value2) && value2 == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteNumber(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, long newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var value2) && value2 == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteNumber(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, ulong newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.TryGetUInt64(out var value2) && value2 == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteNumber(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, float newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.TryGetSingle(out var value2) && value2 == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteNumber(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, double newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var value2) && value2 == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteNumber(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, decimal newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var value2) && value2 == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteNumber(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, string newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteString(name, newValue);
		});
	}

	public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, bool newValue)
	{
		if (originalElement.HasValue && originalElement.Value.TryGetProperty(propertyName, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) && value.GetBoolean() == newValue)
		{
			return originalElement.Value;
		}
		return SetPropertyCore(ref originalElement, propertyName, delegate(Utf8JsonWriter w, string name)
		{
			w.WriteBoolean(name, newValue);
		});
	}

	private static JsonElement SetPropertyCore(ref JsonElement? originalElement, string propertyName, Action<Utf8JsonWriter, string> writeValue)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(memoryStream);
		utf8JsonWriter.WriteStartObject();
		if (!originalElement.HasValue)
		{
			writeValue(utf8JsonWriter, propertyName);
		}
		else
		{
			foreach (JsonProperty item in originalElement.Value.EnumerateObject())
			{
				if (item.Name != propertyName)
				{
					item.WriteTo(utf8JsonWriter);
				}
			}
			writeValue(utf8JsonWriter, propertyName);
		}
		utf8JsonWriter.WriteEndObject();
		utf8JsonWriter.Flush();
		using JsonDocument jsonDocument = JsonDocument.Parse(new ReadOnlyMemory<byte>(memoryStream.GetBuffer(), 0, (int)memoryStream.Length));
		originalElement = jsonDocument.RootElement.Clone();
		return originalElement.Value;
	}
}
}
