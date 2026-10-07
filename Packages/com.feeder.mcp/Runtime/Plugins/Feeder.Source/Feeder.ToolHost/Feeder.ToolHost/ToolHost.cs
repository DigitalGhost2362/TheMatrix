using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Feeder.ToolHost.Common;
using Feeder.ToolHost.Skills;
using Feeder.Logging;
using Feeder.Rx;

namespace Feeder.ToolHost
{
public class ToolHost : IToolHost, IDisposable
{
	private readonly ILogger<ToolHost> _logger;

	private readonly CompositeDisposable _disposables;

	private readonly CancellationTokenSource _cancellationTokenSource;

	private readonly ThreadSafeBool _isDisposed;

	private readonly Feeder.ToolHost.Common.Version _version;

	private readonly ISkillFileGenerator _skillFileGenerator;

	private readonly SkillContentCollection _skillContentCollection;

	private readonly ConnectionConfig _connectionConfig;

	public ILogger Logger => _logger;

	public IMcpManager McpManager { get; private set; }

	public Feeder.ToolHost.Common.Version Version => _version;

	public ulong ToolCallsCount => McpManager.ToolManager?.ToolCallsCount ?? 0;

	public ToolHost(ILogger<ToolHost> logger, IMcpManager mcpManager, Feeder.ToolHost.Common.Version version, ISkillFileGenerator skillFileGenerator, SkillContentCollection skillContentCollection, ConnectionConfig? connectionConfig = null)
	{
		_disposables = new CompositeDisposable();
		_isDisposed = new ThreadSafeBool();
		_logger = logger ?? throw new ArgumentNullException("logger");
		_logger.LogTrace("{class} Ctor.", "ToolHost");
		McpManager = mcpManager ?? throw new ArgumentNullException("mcpManager");
		_cancellationTokenSource = _disposables.ToCancellationTokenSource();
		_version = version ?? throw new ArgumentNullException("version");
		_connectionConfig = connectionConfig ?? new ConnectionConfig();
		_skillFileGenerator = skillFileGenerator ?? throw new ArgumentNullException("skillFileGenerator");
		_skillContentCollection = skillContentCollection ?? throw new ArgumentNullException("skillContentCollection");
		IToolManager? toolManager = McpManager.ToolManager;
		if (toolManager != null)
		{
			Disposable.AddTo<IDisposable>(ObservableSubscribeExtensions.Subscribe<Unit>(ObservableExtensions.ThrottleFirst<Unit>(toolManager.OnToolsUpdated, TimeSpan.FromMilliseconds(100.0)), (Action<Unit>)delegate
			{
				_logger.LogDebug("{method}, tools updated event received.", "OnToolsUpdated");
				if (!_cancellationTokenSource.Token.IsCancellationRequested)
				{
					try
					{
						GenerateSkillFilesIfNeeded();
					}
					catch (InvalidOperationException exception2)
					{
						_logger.LogError(exception2, "{method}: skill auto-generation skipped — host did not provide a project root. Set ConnectionConfig.ProjectRootPath or pass basePath to GenerateSkillFiles.", "OnToolsUpdated");
					}
				}
			}), (ICollection<IDisposable>)_disposables);
		}
		try
		{
			GenerateSkillFilesIfNeeded();
		}
		catch (InvalidOperationException exception)
		{
			_logger.LogError(exception, "{ctor}: initial skill generation skipped — host did not provide a project root. Set ConnectionConfig.ProjectRootPath or pass basePath to GenerateSkillFiles.", "ToolHost");
		}
	}

	public bool GenerateSkillFilesIfNeeded(string? path = null)
	{
		if (!_connectionConfig.GenerateSkillFiles)
		{
			return false;
		}
		return GenerateSkillFiles(path);
	}

	public bool GenerateSkillFiles(string? path = null)
	{
		string skillsPath = ResolveSkillsPath(path);
		bool result = true;
		IToolManager? toolManager = McpManager.ToolManager;
		IEnumerable<IRunTool> enumerable = toolManager?.GetAllTools();
		if (enumerable == null)
		{
			result = false;
		}
		else
		{
			List<IRunTool> disabledTools = enumerable.Where((IRunTool tool) => !toolManager!.IsToolEnabled(tool.Name)).ToList();
			enumerable = enumerable.Except(disabledTools);
			IEnumerable<IRunTool> enumerable2 = McpManager.SystemToolManager?.GetAllTools();
			IEnumerable<IRunTool> enumerable4;
			if (enumerable2 == null)
			{
				IEnumerable<IRunTool> enumerable3 = enumerable;
				enumerable4 = enumerable3;
			}
			else
			{
				enumerable4 = enumerable.Concat(enumerable2);
			}
			IEnumerable<IRunTool> tools = enumerable4;
			if (!_skillFileGenerator.Generate(tools, skillsPath, _connectionConfig.Host))
			{
				result = false;
			}
			if (disabledTools.Count > 0 && !_skillFileGenerator.Delete(disabledTools, skillsPath))
			{
				result = false;
			}
		}
		if (_skillContentCollection.Count > 0 && !_skillFileGenerator.Generate(_skillContentCollection.Values, skillsPath))
		{
			result = false;
		}
		return result;
	}

	public bool DeleteSkillFiles(string? path = null)
	{
		string skillsPath = ResolveSkillsPath(path);
		bool result = true;
		IEnumerable<IRunTool> enumerable = McpManager.ToolManager?.GetAllTools();
		if (enumerable == null)
		{
			result = false;
		}
		else
		{
			IEnumerable<IRunTool> enumerable2 = McpManager.SystemToolManager?.GetAllTools();
			IEnumerable<IRunTool> enumerable4;
			if (enumerable2 == null)
			{
				IEnumerable<IRunTool> enumerable3 = enumerable;
				enumerable4 = enumerable3;
			}
			else
			{
				enumerable4 = enumerable.Concat(enumerable2);
			}
			IEnumerable<IRunTool> tools = enumerable4;
			if (!_skillFileGenerator.Delete(tools, skillsPath))
			{
				result = false;
			}
		}
		if (_skillContentCollection.Count > 0 && !_skillFileGenerator.Delete(_skillContentCollection.Values, skillsPath))
		{
			result = false;
		}
		return result;
	}

	private string ResolveSkillsPath(string? basePath)
	{
		string skillsPath = _connectionConfig.SkillsPath;
		if (Path.IsPathRooted(skillsPath))
		{
			return Path.GetFullPath(skillsPath);
		}
		string? obj = basePath ?? _connectionConfig.ProjectRootPath;
		if (string.IsNullOrEmpty(obj))
		{
			throw new InvalidOperationException("Cannot resolve relative SkillsPath '" + skillsPath + "': no basePath was supplied and ConnectionConfig.ProjectRootPath is not set. Host applications must either pass an explicit basePath to GenerateSkillFiles / DeleteSkillFiles, or set ConnectionConfig.ProjectRootPath at construction time. Silent fallback to Environment.CurrentDirectory has been removed to prevent skill files landing outside the host project.");
		}
		return Path.GetFullPath(Path.Combine(obj, skillsPath));
	}

	public void Dispose()
	{
		if (_isDisposed.TrySetTrue())
		{
			_logger.LogDebug("{method} called.", "Dispose");
			_disposables.Dispose();
			McpManager.Dispose();
			_logger.LogDebug("{method} completed.", "Dispose");
		}
	}

	~ToolHost()
	{
		Dispose();
	}
}
}
