#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using Feeder.Json.Serialization.Metadata;

namespace Feeder.Json
{
    public sealed class JsonSerializerOptions
    {
        internal const int MaxDepth = 64;

        internal static readonly JsonSerializerOptions Default = new JsonSerializerOptions();

        readonly ConcurrentDictionary<Type, JsonTypeInfo> cache = new ConcurrentDictionary<Type, JsonTypeInfo>();
        readonly ConverterList converters;
        volatile bool isReadOnly;
        bool allowTrailingCommas;
        JsonIgnoreCondition defaultIgnoreCondition;
        JavaScriptEncoder? encoder;
        bool includeFields;
        JsonNumberHandling numberHandling;
        bool propertyNameCaseInsensitive;
        JsonNamingPolicy? propertyNamingPolicy;
        JsonCommentHandling readCommentHandling;
        ReferenceHandler? referenceHandler;
        IJsonTypeInfoResolver? typeInfoResolver;
        bool writeIndented;

        public JsonSerializerOptions()
        {
            converters = new ConverterList(this);
        }

        public IList<JsonConverter> Converters => converters;

        public bool AllowTrailingCommas
        {
            get => allowTrailingCommas;
            set => Set(ref allowTrailingCommas, value);
        }

        public JsonIgnoreCondition DefaultIgnoreCondition
        {
            get => defaultIgnoreCondition;
            set => Set(ref defaultIgnoreCondition, value);
        }

        public JavaScriptEncoder? Encoder
        {
            get => encoder;
            set => Set(ref encoder, value);
        }

        public bool IncludeFields
        {
            get => includeFields;
            set => Set(ref includeFields, value);
        }

        public JsonNumberHandling NumberHandling
        {
            get => numberHandling;
            set => Set(ref numberHandling, value);
        }

        public bool PropertyNameCaseInsensitive
        {
            get => propertyNameCaseInsensitive;
            set => Set(ref propertyNameCaseInsensitive, value);
        }

        public JsonNamingPolicy? PropertyNamingPolicy
        {
            get => propertyNamingPolicy;
            set => Set(ref propertyNamingPolicy, value);
        }

        public JsonCommentHandling ReadCommentHandling
        {
            get => readCommentHandling;
            set => Set(ref readCommentHandling, value);
        }

        public ReferenceHandler? ReferenceHandler
        {
            get => referenceHandler;
            set => Set(ref referenceHandler, value);
        }

        public IJsonTypeInfoResolver? TypeInfoResolver
        {
            get => typeInfoResolver;
            set => Set(ref typeInfoResolver, value);
        }

        public bool WriteIndented
        {
            get => writeIndented;
            set => Set(ref writeIndented, value);
        }

        // without a resolver only the converter is resolved, so a type whose member names collide does not throw here
        public JsonConverter GetConverter(Type typeToConvert)
        {
            if (typeToConvert == null)
                throw new ArgumentNullException(nameof(typeToConvert));
            if (isReadOnly)
                return GetTypeInfo(typeToConvert).Converter;
            return typeInfoResolver == null
                ? DefaultJsonTypeInfoResolver.GetConverter(typeToConvert, this)
                : Resolve(typeToConvert).Converter;
        }

        // first use freezes the options, as the reference serializer does
        internal JsonTypeInfo GetTypeInfo(Type type)
        {
            isReadOnly = true;
            if (cache.TryGetValue(type, out JsonTypeInfo? info))
                return info;
            info = Resolve(type);
            info.Configure();
            return cache.GetOrAdd(type, info);
        }

        internal JsonWriterOptions GetWriterOptions() => new JsonWriterOptions
        {
            Encoder = encoder,
            Indented = writeIndented,
            MaxDepth = MaxDepth,
            SkipValidation = true,
        };

        internal JsonReaderOptions GetReaderOptions() => new JsonReaderOptions
        {
            AllowTrailingCommas = allowTrailingCommas,
            CommentHandling = readCommentHandling,
            MaxDepth = MaxDepth,
        };

        internal JsonDocumentOptions GetDocumentOptions() => new JsonDocumentOptions
        {
            AllowTrailingCommas = allowTrailingCommas,
            CommentHandling = readCommentHandling,
        };

        internal JsonNodeOptions GetNodeOptions() => new JsonNodeOptions { PropertyNameCaseInsensitive = propertyNameCaseInsensitive };

        JsonTypeInfo Resolve(Type type)
        {
            IJsonTypeInfoResolver resolver = typeInfoResolver ?? DefaultJsonTypeInfoResolver.Shared;
            return resolver.GetTypeInfo(type, this)
                ?? throw new NotSupportedException("JsonTypeInfo metadata for type '" + type + "' was not provided by TypeInfoResolver of type '" + resolver.GetType() + "'.");
        }

        void Set<T>(ref T field, T value)
        {
            VerifyMutable();
            field = value;
        }

        void VerifyMutable()
        {
            if (isReadOnly)
                throw new InvalidOperationException("This JsonSerializerOptions instance is read-only or has already been used in serialization or deserialization.");
        }

        sealed class ConverterList : Collection<JsonConverter>
        {
            readonly JsonSerializerOptions owner;

            internal ConverterList(JsonSerializerOptions owner)
            {
                this.owner = owner;
            }

            protected override void InsertItem(int index, JsonConverter item)
            {
                owner.VerifyMutable();
                base.InsertItem(index, item ?? throw new ArgumentNullException(nameof(item)));
            }

            protected override void SetItem(int index, JsonConverter item)
            {
                owner.VerifyMutable();
                base.SetItem(index, item ?? throw new ArgumentNullException(nameof(item)));
            }

            protected override void RemoveItem(int index)
            {
                owner.VerifyMutable();
                base.RemoveItem(index);
            }

            protected override void ClearItems()
            {
                owner.VerifyMutable();
                base.ClearItems();
            }
        }
    }
}
