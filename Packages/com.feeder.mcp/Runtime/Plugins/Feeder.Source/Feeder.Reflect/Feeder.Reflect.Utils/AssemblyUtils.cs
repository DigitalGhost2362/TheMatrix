using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Feeder.Reflect.Utils
{
public static class AssemblyUtils
{
	public static IEnumerable<Assembly> AllAssemblies
	{
		get
		{
			Assembly[] assemblies;
			try
			{
				assemblies = AppDomain.CurrentDomain.GetAssemblies();
			}
			catch (AppDomainUnloadedException)
			{
				yield break;
			}
			for (int i = 0; i < assemblies.Length; i++)
			{
				yield return assemblies[i];
			}
		}
	}

	public static IEnumerable<Type> AllTypes
	{
		get
		{
			foreach (Assembly allAssembly in AllAssemblies)
			{
				Type[] types = GetAssemblyTypes(allAssembly);
				for (int i = 0; i < types.Length; i++)
				{
					yield return types[i];
				}
			}
		}
	}

	public static IEnumerable<Assembly> GetAssembliesStartingWith(string prefix, StringComparison comparison = StringComparison.Ordinal)
	{
		if (string.IsNullOrEmpty(prefix))
		{
			yield break;
		}
		foreach (Assembly allAssembly in AllAssemblies)
		{
			string name = allAssembly.GetName().Name;
			if (name != null && name.StartsWith(prefix, comparison))
			{
				yield return allAssembly;
			}
		}
	}

	public static IEnumerable<Type> GetTypesStartingWith(string prefix, StringComparison comparison = StringComparison.Ordinal)
	{
		foreach (Assembly item in GetAssembliesStartingWith(prefix, comparison))
		{
			Type[] types = GetAssemblyTypes(item);
			for (int i = 0; i < types.Length; i++)
			{
				yield return types[i];
			}
		}
	}

	public static Type[] GetAssemblyTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types?.Where((Type t) => t != null).Select((Type x) => x).ToArray() ?? Array.Empty<Type>();
		}
		catch
		{
			return Array.Empty<Type>();
		}
	}
}
}
