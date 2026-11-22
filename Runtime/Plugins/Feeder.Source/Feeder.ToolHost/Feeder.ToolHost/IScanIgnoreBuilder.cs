namespace Feeder.ToolHost
{
public interface IScanIgnoreBuilder
{
	IScanIgnoreBuilder IgnoreAssemblies(params string[] assemblyNamePrefixes);

	IScanIgnoreBuilder IgnoreNamespaces(params string[] namespacePrefixes);
}
}
