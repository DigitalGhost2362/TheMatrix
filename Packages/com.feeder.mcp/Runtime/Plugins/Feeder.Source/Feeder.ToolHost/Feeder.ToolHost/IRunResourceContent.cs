using System.Collections.Generic;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
public interface IRunResourceContent
{
	Task<ResponseResourceContent[]> Run(params object?[] parameters);

	Task<ResponseResourceContent[]> Run(IDictionary<string, object?>? namedParameters);
}
}
