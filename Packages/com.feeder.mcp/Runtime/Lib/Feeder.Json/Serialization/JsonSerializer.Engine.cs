#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Feeder.Json.Serialization;
using Feeder.Json.Serialization.Metadata;

namespace Feeder.Json
{
    public static partial class JsonSerializer
    {
        internal static object? ReadValue(ref Utf8JsonReader reader, JsonTypeInfo info)
        {
            try
            {
                if (reader.TokenType == JsonTokenType.Null && !info.Converter.HandleNullOnRead)
                    return info.IsNonNullableValueType ? throw CannotConvert(info.Type) : null;
                switch (info.Strategy)
                {
                    case ConverterStrategy.Value:
                        return ReadWithConverter(ref reader, info);
                    case ConverterStrategy.Nullable:
                        return ReadValue(ref reader, info.ElementInfo);
                    case ConverterStrategy.Polymorphic:
                        return ParseElement(ref reader);
                    case ConverterStrategy.Object:
                        return ReadObject(ref reader, info);
                    case ConverterStrategy.Enumerable:
                        return ReadArray(ref reader, info);
                    default:
                        return ReadDictionary(ref reader, info);
                }
            }
            catch (Exception) when (PathTracker.SetType(info.Type))
            {
                throw;
            }
        }

        static object? ReadWithConverter(ref Utf8JsonReader reader, JsonTypeInfo info)
        {
            JsonConverter converter = info.Converter;
            if (!converter.IsUserConverter)
                return converter.ReadAsObject(ref reader, info.Type, info.Options);
            JsonTokenType token = reader.TokenType;
            int depth = reader.CurrentDepth;
            long consumed = reader.BytesConsumed;
            object? value = converter.ReadAsObject(ref reader, info.Type, info.Options);
            bool consistent = token == JsonTokenType.StartObject ? reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth
                : token == JsonTokenType.StartArray ? reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == depth
                : reader.BytesConsumed == consumed;
            if (!consistent)
                throw Failure("The converter '" + converter.GetType() + "' read too much or not enough.");
            if (converter.TypeToConvert != info.Type)
            {
                if (value == null)
                {
                    if (info.IsNonNullableValueType)
                        throw new InvalidOperationException("Unable to assign 'null' to the property or field of type '" + info.Type + "'.");
                }
                else if (!info.Type.IsInstanceOfType(value))
                {
                    throw new InvalidCastException("Unable to cast object of type '" + value.GetType() + "' to type '" + info.Type + "'.");
                }
            }
            return value;
        }

        static object ReadObject(ref Utf8JsonReader reader, JsonTypeInfo info)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw CannotConvert(info.Type);
            object target = info.CreateObject();
            while (true)
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.EndObject)
                    return target;
                string name = reader.GetString()!;
                try
                {
                    reader.Read();
                    if (info.TryGetProperty(name, out JsonPropertyInfo? property))
                        property!.SetValue(target, ReadValue(ref reader, property.ValueInfo));
                    else
                        reader.Skip();
                }
                catch (Exception) when (PathTracker.Add(name))
                {
                    throw;
                }
            }
        }

        static object ReadArray(ref Utf8JsonReader reader, JsonTypeInfo info)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw CannotConvert(info.Type);
            var collection = (EnumerableConverter)info.Converter;
            if (collection.Shape == CollectionShape.Unsupported)
                throw new NotSupportedException("The collection type '" + info.Type + "' is not supported.");
            JsonTypeInfo element = info.ElementInfo;
            var items = (IList)Activator.CreateInstance(collection.ListType)!;
            while (true)
            {
                try
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.EndArray)
                        return collection.Complete(items);
                    items.Add(ReadValue(ref reader, element));
                }
                catch (Exception) when (PathTracker.Add(items.Count))
                {
                    throw;
                }
            }
        }

        static object ReadDictionary(ref Utf8JsonReader reader, JsonTypeInfo info)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw CannotConvert(info.Type);
            var map = (DictionaryConverter)info.Converter;
            if (!map.HasStringKeys)
                throw map.UnsupportedKey();
            if (map.CreateType == null)
                throw new NotSupportedException("The dictionary type '" + info.Type + "' is not supported.");
            object target = Activator.CreateInstance(map.CreateType)!;
            JsonTypeInfo element = info.ElementInfo;
            while (true)
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.EndObject)
                    return target;
                string key = reader.GetString()!;
                try
                {
                    reader.Read();
                    map.Set(target, key, ReadValue(ref reader, element));
                }
                catch (Exception) when (PathTracker.Add(key))
                {
                    throw;
                }
            }
        }

        internal static JsonElement ParseElement(ref Utf8JsonReader reader)
        {
            JsonDocument.TryParseValue(ref reader, out JsonDocument? document, true, false);
            return document!.RootElement;
        }

        internal static void WriteValue(Utf8JsonWriter writer, object? value, JsonTypeInfo info, HashSet<object>? ancestors)
        {
            if (writer.CurrentDepth >= JsonSerializerOptions.MaxDepth)
                throw Failure("A possible object cycle was detected. This can either be due to a cycle or if the object depth is larger than the maximum allowed depth of " + JsonSerializerOptions.MaxDepth + ".");
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            if (info.Strategy == ConverterStrategy.Polymorphic && value.GetType() != typeof(object))
                info = info.Options.GetTypeInfo(value.GetType());
            switch (info.Strategy)
            {
                case ConverterStrategy.Value:
                    WriteWithConverter(writer, value, info);
                    return;
                case ConverterStrategy.Nullable:
                    WriteValue(writer, value, info.ElementInfo, ancestors);
                    return;
            }
            bool tracked = ancestors != null && !info.Type.IsValueType;
            if (tracked && !ancestors!.Add(value))
            {
                writer.WriteNullValue();
                return;
            }
            switch (info.Strategy)
            {
                case ConverterStrategy.Object:
                    WriteObject(writer, value, info, ancestors);
                    break;
                case ConverterStrategy.Enumerable:
                    WriteArray(writer, (IEnumerable)value, info, ancestors);
                    break;
                case ConverterStrategy.Dictionary:
                    WriteDictionary(writer, value, info, ancestors);
                    break;
                default:
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                    break;
            }
            if (tracked)
                ancestors!.Remove(value);
        }

        static void WriteWithConverter(Utf8JsonWriter writer, object value, JsonTypeInfo info)
        {
            JsonConverter converter = info.Converter;
            int depth = writer.CurrentDepth;
            converter.WriteAsObject(writer, value, info.Options);
            if (converter.IsUserConverter && writer.CurrentDepth != depth)
                throw Failure("The converter '" + converter.GetType() + "' wrote too much or not enough.");
        }

        static void WriteObject(Utf8JsonWriter writer, object value, JsonTypeInfo info, HashSet<object>? ancestors)
        {
            writer.WriteStartObject();
            foreach (JsonPropertyInfo property in info.WriteOrder)
            {
                try
                {
                    object? member = property.GetValue(value);
                    JsonTypeInfo memberInfo = property.ValueInfo;
                    // a back reference reads as null, so WhenWritingNull drops it
                    if (member != null && ancestors != null && memberInfo.Strategy != ConverterStrategy.Value && memberInfo.Strategy != ConverterStrategy.Nullable
                        && ancestors.Contains(member))
                        member = null;
                    if (member == null && property.SkipNullOnWrite)
                        continue;
                    writer.WritePropertyName(property.Name);
                    if (member == null)
                        writer.WriteNullValue();
                    else
                        WriteValue(writer, member, memberInfo, ancestors);
                }
                catch (Exception) when (PathTracker.Add(property.MemberName))
                {
                    throw;
                }
            }
            writer.WriteEndObject();
        }

        static void WriteArray(Utf8JsonWriter writer, IEnumerable value, JsonTypeInfo info, HashSet<object>? ancestors)
        {
            writer.WriteStartArray();
            JsonTypeInfo element = info.ElementInfo;
            foreach (object? item in value)
                WriteValue(writer, item, element, ancestors);
            writer.WriteEndArray();
        }

        static void WriteDictionary(Utf8JsonWriter writer, object value, JsonTypeInfo info, HashSet<object>? ancestors)
        {
            var map = (DictionaryConverter)info.Converter;
            if (!map.HasStringKeys)
                throw map.UnsupportedKey();
            writer.WriteStartObject();
            JsonTypeInfo element = info.ElementInfo;
            foreach (KeyValuePair<string, object?> entry in map.Entries(value))
            {
                try
                {
                    writer.WritePropertyName(entry.Key);
                    WriteValue(writer, entry.Value, element, ancestors);
                }
                catch (Exception) when (PathTracker.Add(entry.Key))
                {
                    throw;
                }
            }
            writer.WriteEndObject();
        }

        internal static object? ReadRoot(ReadOnlySpan<byte> utf8Json, JsonTypeInfo info)
        {
            var reader = new Utf8JsonReader(utf8Json, info.Options.GetReaderOptions());
            try
            {
                reader.Read();
                object? value = ReadValue(ref reader, info);
                reader.Read();
                return value;
            }
            catch (Exception e)
            {
                Exception replacement = DecorateRead(e, ref reader, info);
                if (replacement == e)
                    throw;
                throw replacement;
            }
        }

        // a nested call over a caller's reader: the value is validated first and the reader is restored on JsonException
        internal static object? ReadNested(ref Utf8JsonReader reader, JsonTypeInfo info)
        {
            Utf8JsonReader restore = reader;
            try
            {
                if (reader.TokenType == JsonTokenType.None || reader.TokenType == JsonTokenType.PropertyName)
                    reader.Read();
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        Utf8JsonReader probe = reader;
                        probe.Skip();
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                    case JsonTokenType.PropertyName:
                        throw reader.StartOfValueError();
                }
                return ReadValue(ref reader, info);
            }
            catch (Exception e)
            {
                Exception replacement = DecorateRead(e, ref reader, info);
                if (replacement is JsonException)
                    reader = restore;
                if (replacement == e)
                    throw;
                throw replacement;
            }
        }

        internal static void WriteRoot(Utf8JsonWriter writer, object? value, JsonTypeInfo info)
        {
            HashSet<object>? ancestors = info.Options.ReferenceHandler != null ? new HashSet<object>(ReferenceComparer.Instance) : null;
            try
            {
                WriteValue(writer, value, info, ancestors);
            }
            catch (Exception e)
            {
                Exception replacement = DecorateWrite(e);
                if (replacement == e)
                    throw;
                throw replacement;
            }
        }

        internal static MemoryStream WriteToStream(object? value, JsonTypeInfo info)
        {
            var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, info.Options.GetWriterOptions()))
                WriteRoot(writer, value, info);
            return output;
        }

        internal static string Utf8ToString(MemoryStream output) => Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);

        internal static object? ReadWithFreshState(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            ReadValue(ref reader, options.GetTypeInfo(type));

        internal static void WriteWithFreshState(Utf8JsonWriter writer, object value, Type type, JsonSerializerOptions options) =>
            WriteRoot(writer, value, options.GetTypeInfo(type));

        // a JsonValue built from a primitive keeps its own converter; anything else goes through the caller's options
        internal static void WriteNodeValue(Utf8JsonWriter writer, object value, Type type, JsonSerializerOptions? options)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (JsonSerializerOptions.Default.GetTypeInfo(type).Converter is PrimitiveConverter primitive && primitive.IsNodePrimitive)
                primitive.Write(writer, value, options?.NumberHandling ?? JsonNumberHandling.Strict, options);
            else
                WriteRoot(writer, value, (options ?? JsonSerializerOptions.Default).GetTypeInfo(type));
        }

        internal static JsonException CannotConvert(Type type) => Failure("The JSON value could not be converted to " + type + ".");

        static JsonException Failure(string message) => new JsonException(message) { AppendPathInformation = true };

        static Exception DecorateRead(Exception e, ref Utf8JsonReader reader, JsonTypeInfo info)
        {
            Type? failedType = PathTracker.FailedType;
            string path = PathTracker.Take();
            long line, column;
            switch (e)
            {
                case JsonException json when json.Path == null:
                    if (json.LineNumber.HasValue && !json.AppendPathInformation)
                    {
                        string message = json.Message;
                        int at = message.LastIndexOf(" LineNumber: ", StringComparison.Ordinal);
                        message = at >= 0 ? message.Substring(0, at) + " Path: " + path + " |" + message.Substring(at) : message + " Path: " + path + ".";
                        return new JsonException(message, json) { Path = path, LineNumber = json.LineNumber, BytePositionInLine = json.BytePositionInLine };
                    }
                    reader.GetPosition(out line, out column);
                    json.Path = path;
                    json.LineNumber = line;
                    json.BytePositionInLine = column;
                    if (json.AppendPathInformation)
                        json.SetMessage(json.Message + " Path: " + path + " | LineNumber: " + line + " | BytePositionInLine: " + column + ".");
                    return json;
                case InvalidOperationException _ when e.Source == JsonText.RethrowableSource:
                case FormatException _ when e.Source == JsonText.RethrowableSource:
                    reader.GetPosition(out line, out column);
                    return new JsonException("The JSON value could not be converted to " + (failedType ?? info.Type) + ". Path: " + path + " | LineNumber: " + line + " | BytePositionInLine: " + column + ".", e)
                    {
                        Path = path,
                        LineNumber = line,
                        BytePositionInLine = column,
                    };
                case NotSupportedException _ when e.Message.IndexOf(" Path: ", StringComparison.Ordinal) < 0:
                    reader.GetPosition(out line, out column);
                    return new NotSupportedException(e.Message + " Path: " + path + " | LineNumber: " + line + " | BytePositionInLine: " + column + ".", e);
                default:
                    return e;
            }
        }

        static Exception DecorateWrite(Exception e)
        {
            string path = PathTracker.Take();
            switch (e)
            {
                case JsonException json when json.Path == null:
                    json.Path = path;
                    if (json.AppendPathInformation)
                        json.SetMessage(json.Message + " Path: " + path + ".");
                    return json;
                case InvalidOperationException _ when e.Source == JsonText.RethrowableSource:
                    return new JsonException("The value could not be serialized. Path: " + path + ".", e) { Path = path };
                case NotSupportedException _ when e.Message.IndexOf(" Path: ", StringComparison.Ordinal) < 0:
                    return new NotSupportedException(e.Message + " Path: " + path + ".", e);
                default:
                    return e;
            }
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
