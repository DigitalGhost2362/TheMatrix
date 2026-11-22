using System;
using System.Collections.Generic;
using System.Reflection;

namespace Feeder.ToolHost
{
public class ToolHostBuilderIgnoreConfig
{
	private readonly object _lock = new object();

	private readonly Dictionary<Assembly, bool> _assemblyIgnoreCache = new Dictionary<Assembly, bool>();

	private readonly Dictionary<string, bool> _namespaceIgnoreCache = new Dictionary<string, bool>(StringComparer.Ordinal);

	internal HashSet<string> IgnoredAssemblyNames { get; } = new HashSet<string>(StringComparer.Ordinal);

	internal HashSet<Assembly> IgnoredAssemblies { get; } = new HashSet<Assembly>();

	internal HashSet<string> IgnoredNamespaces { get; } = new HashSet<string>();

	internal bool IsIgnored(Assembly assembly)
	{
		lock (_lock)
		{
			if (_assemblyIgnoreCache.TryGetValue(assembly, out var value))
			{
				return value;
			}
			bool flag = CheckAssemblyIgnored(assembly);
			_assemblyIgnoreCache[assembly] = flag;
			return flag;
		}
	}

	private bool CheckAssemblyIgnored(Assembly assembly)
	{
		if (IgnoredAssemblies.Contains(assembly))
		{
			return true;
		}
		string name = assembly.GetName().Name;
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		foreach (string ignoredAssemblyName in IgnoredAssemblyNames)
		{
			if (name.StartsWith(ignoredAssemblyName, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	internal bool IsIgnored(Type type)
	{
		return IsNamespaceIgnored(type.Namespace);
	}

	private bool IsNamespaceIgnored(string? typeNamespace)
	{
		if (string.IsNullOrEmpty(typeNamespace))
		{
			return false;
		}
		lock (_lock)
		{
			if (_namespaceIgnoreCache.TryGetValue(typeNamespace, out var value))
			{
				return value;
			}
			bool flag = CheckNamespaceIgnored(typeNamespace);
			_namespaceIgnoreCache[typeNamespace] = flag;
			return flag;
		}
	}

	private bool CheckNamespaceIgnored(string typeNamespace)
	{
		foreach (string ignoredNamespace in IgnoredNamespaces)
		{
			if (typeNamespace.StartsWith(ignoredNamespace, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	internal void InvalidateCaches()
	{
		lock (_lock)
		{
			_assemblyIgnoreCache.Clear();
			_namespaceIgnoreCache.Clear();
		}
	}
}
}
