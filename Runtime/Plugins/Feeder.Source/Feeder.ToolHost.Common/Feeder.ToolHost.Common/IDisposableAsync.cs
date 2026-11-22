using System;
using System.Threading.Tasks;

namespace Feeder.ToolHost.Common
{
public interface IDisposableAsync : IDisposable
{
	Task DisposeAsync();
}
}
