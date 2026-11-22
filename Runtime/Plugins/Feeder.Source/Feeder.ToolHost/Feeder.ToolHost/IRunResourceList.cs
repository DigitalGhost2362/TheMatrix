using System.Collections.Generic;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
public interface IRunResourceList
{
	Task<ResponseListResource[]> Run(params object?[] parameters);

	Task<ResponseListResource[]> Run(IDictionary<string, object?>? namedParameters);
}
}
