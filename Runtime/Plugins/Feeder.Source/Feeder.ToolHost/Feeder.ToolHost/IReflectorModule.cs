namespace Feeder.ToolHost
{
public interface IReflectorModule
{
	int Order { get; }

	void Configure(IReflectorModuleContext ctx);
}
}
