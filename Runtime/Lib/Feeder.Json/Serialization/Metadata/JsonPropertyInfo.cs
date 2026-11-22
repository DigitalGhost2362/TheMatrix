#nullable enable
using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Feeder.Json.Serialization.Metadata
{
    public sealed class JsonPropertyInfo
    {
        readonly JsonSerializerOptions options;
        readonly MemberInfo member;
        readonly PropertyInfo? property;
        readonly FieldInfo? field;
        JsonTypeInfo? valueInfo;

        internal JsonPropertyInfo(MemberInfo member, Type propertyType, bool include, JsonSerializerOptions options)
        {
            this.options = options;
            this.member = member;
            MemberName = member.Name;
            PropertyType = propertyType;
            DeclaringType = member.DeclaringType!;
            IsIgnored = member.GetCustomAttribute<JsonIgnoreAttribute>(false) != null;
            Name = member.GetCustomAttribute<JsonPropertyNameAttribute>(false)?.Name
                ?? (options.PropertyNamingPolicy != null ? options.PropertyNamingPolicy.ConvertName(member.Name) : member.Name)
                ?? throw new InvalidOperationException("The JSON property name for '" + DeclaringType + "." + MemberName + "' cannot be null.");
            property = member as PropertyInfo;
            field = member as FieldInfo;
            if (property != null)
            {
                MethodInfo? getter = property.GetMethod;
                MethodInfo? setter = property.SetMethod;
                IsVirtual = getter?.IsVirtual == true || setter?.IsVirtual == true;
                CanRead = !IsIgnored && getter != null && (getter.IsPublic || include);
                CanWrite = !IsIgnored && setter != null && (setter.IsPublic || include);
            }
            else
            {
                CanRead = !IsIgnored;
                CanWrite = !IsIgnored && !field!.IsInitOnly;
            }
            SkipNullOnWrite = options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull
                && (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) != null);
        }

        public ICustomAttributeProvider? AttributeProvider => member;

        internal string Name { get; }

        internal string MemberName { get; }

        internal Type PropertyType { get; }

        internal Type DeclaringType { get; }

        internal bool IsIgnored { get; }

        internal bool IsVirtual { get; }

        internal bool CanRead { get; }

        internal bool CanWrite { get; }

        internal bool SkipNullOnWrite { get; }

        // [JsonConverter] on the member wins over every other converter
        internal JsonTypeInfo ValueInfo
        {
            get
            {
                if (valueInfo == null)
                {
                    JsonConverterAttribute? attribute = member.GetCustomAttribute<JsonConverterAttribute>(false);
                    valueInfo = attribute == null
                        ? options.GetTypeInfo(PropertyType)
                        : new JsonTypeInfo(PropertyType, DefaultJsonTypeInfoResolver.FromAttribute(attribute, PropertyType, member), options);
                }
                return valueInfo;
            }
        }

        internal bool IsHiddenBy(JsonPropertyInfo other) => MemberName == other.MemberName && DeclaringType.IsAssignableFrom(other.DeclaringType);

        internal object? GetValue(object target)
        {
            if (field != null)
                return field.GetValue(target);
            try
            {
                return property!.GetValue(target);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        internal void SetValue(object target, object? value)
        {
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            try
            {
                property!.SetValue(target, value);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }
}
