using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feeder.Reflect.Utils
{
public static class ExtensionsTask
{
	public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
		using (cancellationToken.Register(delegate(object s)
		{
			((TaskCompletionSource<bool>)s).TrySetResult(result: true);
		}, taskCompletionSource))
		{
			if (task != await Task.WhenAny(new Task[2] { task, taskCompletionSource.Task }))
			{
				throw new OperationCanceledException(cancellationToken);
			}
		}
		await task;
	}

	public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
	{
		TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>();
		using (cancellationToken.Register(delegate(object s)
		{
			((TaskCompletionSource<T>)s).TrySetResult(default(T));
		}, taskCompletionSource))
		{
			if (task != await Task.WhenAny(new Task<T>[2] { task, taskCompletionSource.Task }))
			{
				throw new OperationCanceledException(cancellationToken);
			}
		}
		return await task;
	}
}
}
