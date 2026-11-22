using System;
using System.Collections;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public abstract class NotArrayReflectionConverter<T> : BaseReflectionConverter<T>
{
	public override int SerializationPriority(Type type, ILogger? logger = null)
	{
		int inheritanceDistance = TypeUtils.GetInheritanceDistance(typeof(T), type);
		if (inheritanceDistance >= 0)
		{
			return 10000 - inheritanceDistance;
		}
		if (!(type != typeof(string)) || !typeof(IEnumerable).IsAssignableFrom(type))
		{
			return base.SerializationPriority(type);
		}
		return 0;
	}
}
}
