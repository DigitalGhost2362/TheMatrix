using System;
using System.Collections.Generic;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Json
{
public abstract class JsonSchemaConverter<T> : JsonConverter<T>, IJsonSchemaConverter
{
	private static readonly Type[] _emptyTypes = Array.Empty<Type>();

	public static string StaticId => TypeUtils.GetSchemaTypeId<T>();

	public virtual string Id => StaticId;

	public abstract JsonNode GetSchema();

	public abstract JsonNode GetSchemaRef();

	public virtual IEnumerable<Type> GetDefinedTypes()
	{
		return _emptyTypes;
	}
}
}
