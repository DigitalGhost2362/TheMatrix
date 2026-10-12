using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feeder.Reflect.Utils
{
public class MainThread
{
	private static SynchronizationContext mainContext = SynchronizationContext.Current;

	public static MainThread Instance { get; set; } = new MainThread();

	public virtual bool IsMainThread => Thread.CurrentThread.ManagedThreadId == 1;

	public virtual void Run(Task task)
	{
		RunAsync(task).Wait();
	}

	public virtual T Run<T>(Task<T> task)
	{
		return RunAsync(task).Result;
	}

	public virtual T Run<T>(Func<T> func)
	{
		return RunAsync(func).Result;
	}

	public virtual void Run(Action action)
	{
		RunAsync(action).Wait();
	}

	public virtual Task RunAsync(Task task)
	{
		if (IsMainThread)
		{
			return task;
		}
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		mainContext.Post(delegate
		{
			try
			{
				task.Wait();
				tcs.SetResult(result: true);
			}
			catch (Exception exception)
			{
				tcs.SetException(exception);
			}
		}, null);
		return tcs.Task;
	}

	public virtual Task<T> RunAsync<T>(Task<T> task)
	{
		if (IsMainThread)
		{
			return task;
		}
		TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
		mainContext.Post(delegate
		{
			try
			{
				T result = task.Result;
				tcs.SetResult(result);
			}
			catch (Exception exception)
			{
				tcs.SetException(exception);
			}
		}, null);
		return tcs.Task;
	}

	public virtual Task<T> RunAsync<T>(Func<T> func)
	{
		if (IsMainThread)
		{
			return Task.FromResult(func());
		}
		TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
		mainContext.Post(delegate
		{
			try
			{
				T result = func();
				tcs.SetResult(result);
			}
			catch (Exception exception)
			{
				tcs.SetException(exception);
			}
		}, null);
		return tcs.Task;
	}

	public virtual Task RunAsync(Action action)
	{
		if (IsMainThread)
		{
			action();
			return Task.CompletedTask;
		}
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		mainContext.Post(delegate
		{
			try
			{
				action();
				tcs.SetResult(result: true);
			}
			catch (Exception exception)
			{
				tcs.SetException(exception);
			}
		}, null);
		return tcs.Task;
	}
}
}
