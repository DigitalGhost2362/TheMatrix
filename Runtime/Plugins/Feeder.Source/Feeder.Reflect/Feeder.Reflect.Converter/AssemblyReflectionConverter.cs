using System;
using System.Linq;
using System.Reflection;
using Feeder.Json;
using Feeder.Reflect.Model;
using Feeder.Logging;

namespace Feeder.Reflect.Converter
{
public class AssemblyReflectionConverter : IgnoreFieldsAndPropertiesReflectionConverter<Assembly>
{
	public AssemblyReflectionConverter()
		: base(true, true)
	{
	}

	protected override SerializedMember InternalSerialize(Reflector reflector, object? obj, Type type, string? name = null, bool recursive = true, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, int depth = 0, Logs? logs = null, ILogger? logger = null, SerializationContext? context = null)
	{
		if (obj is Assembly assembly)
		{
			string value = assembly.FullName ?? assembly.GetName().Name ?? string.Empty;
			return SerializedMember.FromValue(reflector, type, value, name);
		}
		return base.InternalSerialize(reflector, obj, type, name, recursive, flags, depth, logs, logger, context);
	}

	public override object? CreateInstance(Reflector reflector, Type type)
	{
		return Assembly.GetExecutingAssembly();
	}

	protected override bool TryDeserializeValueInternal(Reflector reflector, SerializedMember data, out object? result, Type type, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		result = null;
		if (!data.valueJsonElement.HasValue)
		{
			return false;
		}
		try
		{
			string assemblyName = data.valueJsonElement.Value.GetString();
			if (!string.IsNullOrEmpty(assemblyName))
			{
				Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.FullName == assemblyName || a.GetName().Name == assemblyName);
				if (assembly == null)
				{
					try
					{
						assembly = Assembly.Load(assemblyName);
					}
					catch
					{
						assembly = Assembly.Load(new AssemblyName(assemblyName));
					}
				}
				if (assembly != null)
				{
					result = assembly;
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			logger?.LogError("Failed to deserialize Assembly from value '{Value}': {Message}", data.valueJsonElement, ex.Message);
			return false;
		}
		return false;
	}

	protected override bool SetValue(Reflector reflector, ref object? obj, Type type, JsonElement? value, int depth = 0, Logs? logs = null, ILogger? logger = null)
	{
		if (!value.HasValue)
		{
			return false;
		}
		try
		{
			string assemblyName = value.Value.GetString();
			if (!string.IsNullOrEmpty(assemblyName))
			{
				Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.FullName == assemblyName || a.GetName().Name == assemblyName);
				if (assembly == null)
				{
					try
					{
						assembly = Assembly.Load(assemblyName);
					}
					catch
					{
						assembly = Assembly.Load(new AssemblyName(assemblyName));
					}
				}
				if (assembly != null)
				{
					obj = assembly;
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			logger?.LogError("Failed to deserialize Assembly from value '{Value}': {Message}", value, ex.Message);
			return false;
		}
		return false;
	}
}
}
