using System;
using System.Collections.Generic;
using System.Reflection;
using Feeder.ToolHost.Skills;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public interface IToolHostBuilder
{
	IToolHostBuilder WithTool(Type classType, MethodInfo methodInfo);

	IToolHostBuilder WithTool(AiToolAttribute attribute, Type classType, MethodInfo methodInfo);

	IToolHostBuilder WithTool(string name, string? title, Type classType, MethodInfo methodInfo);

	IToolHostBuilder AddTool(string name, IRunTool runner);

	IToolHostBuilder WithTools<T>();

	IToolHostBuilder WithTools(params Type[] targetTypes);

	IToolHostBuilder WithTools(IEnumerable<Type> targetTypes);

	IToolHostBuilder WithTools(Type classType);

	IToolHostBuilder WithToolsFromAssembly(IEnumerable<Assembly> assemblies);

	IToolHostBuilder WithToolsFromAssembly(Assembly? assembly = null);

	IToolHostBuilder WithPrompt(string name, Type classType, MethodInfo methodInfo);

	IToolHostBuilder AddPrompt(string name, IRunPrompt runner);

	IToolHostBuilder WithPrompts<T>();

	IToolHostBuilder WithPrompts(params Type[] targetTypes);

	IToolHostBuilder WithPrompts(IEnumerable<Type> targetTypes);

	IToolHostBuilder WithPrompts(Type classType);

	IToolHostBuilder WithPromptsFromAssembly(IEnumerable<Assembly> assemblies);

	IToolHostBuilder WithPromptsFromAssembly(Assembly? assembly = null);

	IToolHostBuilder WithResource(Type classType, MethodInfo getContentMethod);

	IToolHostBuilder AddResource(IRunResource resourceParams);

	IToolHostBuilder WithResources<T>();

	IToolHostBuilder WithResources(params Type[] targetTypes);

	IToolHostBuilder WithResources(IEnumerable<Type> targetTypes);

	IToolHostBuilder WithResources(Type classType);

	IToolHostBuilder WithResourcesFromAssembly(IEnumerable<Assembly> assemblies);

	IToolHostBuilder WithResourcesFromAssembly(Assembly? assembly = null);

	IToolHostBuilder WithSkills<T>();

	IToolHostBuilder WithSkills(params Type[] targetTypes);

	IToolHostBuilder WithSkills(IEnumerable<Type> targetTypes);

	IToolHostBuilder WithSkills(Type classType);

	IToolHostBuilder WithSkillsFromAssembly(IEnumerable<Assembly> assemblies);

	IToolHostBuilder WithSkillsFromAssembly(Assembly? assembly = null);

	IToolHostBuilder AddLogging(Action<ILoggingBuilder> loggingBuilder);

	IToolHostBuilder SetConfig(ConnectionConfig config);

	IToolHostBuilder WithConfig(Action<ConnectionConfig> config);

	IToolHostBuilder WithSkillFileGenerator(ISkillFileGenerator instance);

	IToolHostBuilder WithReflectorModulesFromAssembly(IEnumerable<Assembly> assemblies);

	IToolHost Build(Reflector reflector);

	IToolHostBuilder IgnoreAssembly(Assembly assembly);

	IToolHostBuilder IgnoreAssembly(string assemblyName);

	IToolHostBuilder IgnoreAssemblies(IEnumerable<Assembly> assemblies);

	IToolHostBuilder IgnoreAssemblies(params string[] assemblyNames);

	IToolHostBuilder IgnoreNamespace(string namespaceName);

	IToolHostBuilder IgnoreNamespaces(params string[] namespaceNames);

	IToolHostBuilder RemoveIgnoredAssembly(Assembly assembly);

	IToolHostBuilder RemoveIgnoredAssembly(string assemblyName);

	IToolHostBuilder RemoveIgnoredAssemblies(IEnumerable<Assembly> assemblies);

	IToolHostBuilder RemoveIgnoredAssemblies(params string[] assemblyNames);

	IToolHostBuilder RemoveIgnoredNamespace(string namespaceName);

	IToolHostBuilder RemoveIgnoredNamespaces(params string[] namespaceNames);

	int GetIgnoredAssembliesCount();

	int GetIgnoredTypesCount();

	IToolHostBuilder ClearIgnoredAssemblies();

	IToolHostBuilder ClearIgnoredNamespaces();

	IToolHostBuilder ClearAllIgnored();
}
}
