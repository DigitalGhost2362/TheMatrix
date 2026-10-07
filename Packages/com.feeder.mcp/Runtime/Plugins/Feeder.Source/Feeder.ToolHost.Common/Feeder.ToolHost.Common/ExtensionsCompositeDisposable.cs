using System;
using System.Threading;
using Feeder.Rx;

namespace Feeder.ToolHost.Common
{
public static class ExtensionsCompositeDisposable
{
	public static CancellationTokenSource ToCancellationTokenSource(this CompositeDisposable disposables)
	{
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		disposables.Add((IDisposable)cancellationTokenSource);
		return cancellationTokenSource;
	}
}
}
