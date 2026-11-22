using System;
using System.Collections.Generic;
using Feeder.Json.Nodes;

namespace Feeder.Reflect.Json
{
public interface IJsonSchemaConverter
{
	string Id { get; }

	JsonNode GetSchema();

	JsonNode GetSchemaRef();

	IEnumerable<Type> GetDefinedTypes();
}
}
