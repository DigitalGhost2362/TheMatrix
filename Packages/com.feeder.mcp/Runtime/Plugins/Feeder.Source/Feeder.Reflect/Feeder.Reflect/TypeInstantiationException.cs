using System;

namespace Feeder.Reflect
{
public class TypeInstantiationException : Exception
{
	public Type? TargetType { get; }

	public TypeInstantiationException()
	{
	}

	public TypeInstantiationException(string message)
		: base(message)
	{
	}

	public TypeInstantiationException(string message, Type targetType)
		: base(message)
	{
		TargetType = targetType;
	}

	public TypeInstantiationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public TypeInstantiationException(string message, Type targetType, Exception innerException)
		: base(message, innerException)
	{
		TargetType = targetType;
	}
}
}
