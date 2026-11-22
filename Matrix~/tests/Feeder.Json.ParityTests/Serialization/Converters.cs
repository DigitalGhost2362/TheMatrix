using System.Globalization;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using Feeder.Json.Serialization.Metadata;
using S = System.Text.Json;
using SN = System.Text.Json.Nodes;
using SS = System.Text.Json.Serialization;
using SM = System.Text.Json.Serialization.Metadata;

namespace Feeder.Json.ParityTests.SerializerParity;

// each converter exists twice with the same body: once per library, shaped like the package's own converters

public sealed class FeederIntAsObject : JsonConverter<object>
{
    public override bool CanConvert(Type typeToConvert) => (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(int);

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Nullable.GetUnderlyingType(typeToConvert) != null ? null : throw new JsonException("null for int");
        if (reader.TokenType == JsonTokenType.String)
            return int.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        double value = reader.GetDouble();
        return value == Math.Floor(value) && value >= int.MinValue && value <= int.MaxValue ? (int)value : throw new JsonException("out of range " + value);
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => writer.WriteNumberValue((int)value);
}

public sealed class ReferenceIntAsObject : SS.JsonConverter<object>
{
    public override bool CanConvert(Type typeToConvert) => (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(int);

    public override object? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options)
    {
        if (reader.TokenType == S.JsonTokenType.Null)
            return Nullable.GetUnderlyingType(typeToConvert) != null ? null : throw new S.JsonException("null for int");
        if (reader.TokenType == S.JsonTokenType.String)
            return int.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        double value = reader.GetDouble();
        return value == Math.Floor(value) && value >= int.MinValue && value <= int.MaxValue ? (int)value : throw new S.JsonException("out of range " + value);
    }

    public override void Write(S.Utf8JsonWriter writer, object value, S.JsonSerializerOptions options) => writer.WriteNumberValue((int)value);
}

public sealed class FeederEnumAsObject : JsonConverter<object>
{
    public override bool CanConvert(Type typeToConvert) => (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert).IsEnum;

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Type type = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        if (reader.TokenType == JsonTokenType.Number)
            return Enum.ToObject(type, reader.GetInt64());
        return Enum.TryParse(type, reader.GetString(), true, out object? result) ? result : throw new JsonException("bad enum");
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

public sealed class ReferenceEnumAsObject : SS.JsonConverter<object>
{
    public override bool CanConvert(Type typeToConvert) => (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert).IsEnum;

    public override object? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options)
    {
        Type type = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        if (reader.TokenType == S.JsonTokenType.Number)
            return Enum.ToObject(type, reader.GetInt64());
        return Enum.TryParse(type, reader.GetString(), true, out object? result) ? result : throw new S.JsonException("bad enum");
    }

    public override void Write(S.Utf8JsonWriter writer, object value, S.JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

// named literals follow the options, like the package's float helper
public sealed class FeederDoubleAsObject : JsonConverter<object>
{
    public override bool CanConvert(Type typeToConvert) => (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(double);

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && (options.NumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            return double.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        return reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        double d = (double)value;
        if ((double.IsNaN(d) || double.IsInfinity(d)) && (options.NumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            writer.WriteStringValue(d.ToString(CultureInfo.InvariantCulture));
        else
            writer.WriteNumberValue(d);
    }
}

public sealed class ReferenceDoubleAsObject : SS.JsonConverter<object>
{
    public override bool CanConvert(Type typeToConvert) => (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert) == typeof(double);

    public override object? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options)
    {
        if (reader.TokenType == S.JsonTokenType.String && (options.NumberHandling & SS.JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            return double.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        return reader.GetDouble();
    }

    public override void Write(S.Utf8JsonWriter writer, object value, S.JsonSerializerOptions options)
    {
        double d = (double)value;
        if ((double.IsNaN(d) || double.IsInfinity(d)) && (options.NumberHandling & SS.JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            writer.WriteStringValue(d.ToString(CultureInfo.InvariantCulture));
        else
            writer.WriteNumberValue(d);
    }
}

// walks the reader by hand: [x, y]
public sealed class FeederPointConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("expected array");
        reader.Read();
        int x = reader.GetInt32();
        reader.Read();
        int y = reader.GetInt32();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("expected end");
        return new Point { X = x, Y = y };
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}

public sealed class ReferencePointConverter : SS.JsonConverter<Point>
{
    public override Point Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options)
    {
        if (reader.TokenType != S.JsonTokenType.StartArray)
            throw new S.JsonException("expected array");
        reader.Read();
        int x = reader.GetInt32();
        reader.Read();
        int y = reader.GetInt32();
        reader.Read();
        if (reader.TokenType != S.JsonTokenType.EndArray)
            throw new S.JsonException("expected end");
        return new Point { X = x, Y = y };
    }

    public override void Write(S.Utf8JsonWriter writer, Point value, S.JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}

// recurses into the serializer with the options it was given, in both directions
public sealed class FeederWrapperConverter : JsonConverter<Wrapper>
{
    public override Wrapper? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("expected object");
        var result = new Wrapper();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name == "items")
                result.Items = JsonSerializer.Deserialize<List<Child>>(ref reader, options)!;
            else if (name == "note")
                result.Note = JsonSerializer.Deserialize<string>(ref reader, options);
            else
                throw new JsonException("unexpected " + name);
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Wrapper value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("items");
        JsonSerializer.Serialize(writer, value.Items, options);
        writer.WritePropertyName("note");
        JsonSerializer.Serialize(writer, value.Note, options);
        writer.WriteEndObject();
    }
}

public sealed class ReferenceWrapperConverter : SS.JsonConverter<Wrapper>
{
    public override Wrapper? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options)
    {
        if (reader.TokenType != S.JsonTokenType.StartObject)
            throw new S.JsonException("expected object");
        var result = new Wrapper();
        while (reader.Read() && reader.TokenType != S.JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name == "items")
                result.Items = S.JsonSerializer.Deserialize<List<Child>>(ref reader, options)!;
            else if (name == "note")
                result.Note = S.JsonSerializer.Deserialize<string>(ref reader, options);
            else
                throw new S.JsonException("unexpected " + name);
        }
        return result;
    }

    public override void Write(S.Utf8JsonWriter writer, Wrapper value, S.JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("items");
        S.JsonSerializer.Serialize(writer, value.Items, options);
        writer.WritePropertyName("note");
        S.JsonSerializer.Serialize(writer, value.Note, options);
        writer.WriteEndObject();
    }
}

public sealed class FeederElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? default : JsonDocument.ParseValue(ref reader).RootElement.Clone();

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
    {
        if (value.ValueKind == JsonValueKind.Null)
            writer.WriteNullValue();
        else
            value.WriteTo(writer);
    }
}

public sealed class ReferenceElementConverter : SS.JsonConverter<S.JsonElement>
{
    public override S.JsonElement Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options) =>
        reader.TokenType == S.JsonTokenType.Null ? default : S.JsonDocument.ParseValue(ref reader).RootElement.Clone();

    public override void Write(S.Utf8JsonWriter writer, S.JsonElement value, S.JsonSerializerOptions options)
    {
        if (value.ValueKind == S.JsonValueKind.Null)
            writer.WriteNullValue();
        else
            value.WriteTo(writer);
    }
}

public sealed class FeederNodeConverter : JsonConverter<JsonNode>
{
    public override JsonNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement.Clone();
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonObject.Create(element),
            JsonValueKind.Array => JsonArray.Create(element),
            _ => JsonValue.Create(element),
        };
    }

    public override void Write(Utf8JsonWriter writer, JsonNode value, JsonSerializerOptions options) => value.WriteTo(writer, options);
}

public sealed class ReferenceNodeConverter : SS.JsonConverter<SN.JsonNode>
{
    public override SN.JsonNode? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options)
    {
        if (reader.TokenType == S.JsonTokenType.Null)
            return null;
        using S.JsonDocument document = S.JsonDocument.ParseValue(ref reader);
        S.JsonElement element = document.RootElement.Clone();
        return element.ValueKind switch
        {
            S.JsonValueKind.Object => SN.JsonObject.Create(element),
            S.JsonValueKind.Array => SN.JsonArray.Create(element),
            _ => SN.JsonValue.Create(element),
        };
    }

    public override void Write(S.Utf8JsonWriter writer, SN.JsonNode value, S.JsonSerializerOptions options) => value.WriteTo(writer, options);
}

// reads one token too few: both serializers must reject it
public sealed class FeederLazyChildConverter : JsonConverter<Child>
{
    public override Child? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Read();
        return new Child();
    }

    public override void Write(Utf8JsonWriter writer, Child value, JsonSerializerOptions options) => writer.WriteStartObject();
}

public sealed class ReferenceLazyChildConverter : SS.JsonConverter<Child>
{
    public override Child? Read(ref S.Utf8JsonReader reader, Type typeToConvert, S.JsonSerializerOptions options)
    {
        reader.Read();
        return new Child();
    }

    public override void Write(S.Utf8JsonWriter writer, Child value, S.JsonSerializerOptions options) => writer.WriteStartObject();
}

// the reflector's resolver modifier: drop members marked [Obsolete]
public static class ObsoleteFilter
{
    public static void Feeder(JsonTypeInfo typeInfo)
    {
        if ((int)typeInfo.Kind != 1)
            return;
        for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].AttributeProvider?.IsDefined(typeof(ObsoleteAttribute), true) == true)
                typeInfo.Properties.RemoveAt(i);
        }
    }

    public static void Reference(SM.JsonTypeInfo typeInfo)
    {
        if ((int)typeInfo.Kind != 1)
            return;
        for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].AttributeProvider?.IsDefined(typeof(ObsoleteAttribute), true) == true)
                typeInfo.Properties.RemoveAt(i);
        }
    }
}
