#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.ToolHost.Common;
using Feeder.ToolHost.Common.Model;
using Feeder.MCP.Utils;
using Feeder.Fmp;
using Feeder.Logging;
using Feeder.Rx;

namespace Feeder.MCP
{
    using static Feeder.ToolHost.Common.Consts.MCP.Server;
    using ILogger = Feeder.Logging.ILogger;
    using LogLevel = Feeder.MCP.Runtime.Utils.LogLevel;

    public partial class MatrixPluginEditor
    {
        static readonly Subject<UnityConnectionConfig> _onConfigChanged = new Subject<UnityConnectionConfig>();
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger<MatrixPluginEditor>();
        static readonly object _instanceMutex = new();

        static MatrixPluginEditor instance = null!;

        public static bool HasInstance
        {
            get
            {
                lock (_instanceMutex)
                {
                    return instance != null;
                }
            }
        }
        public static MatrixPluginEditor Instance
        {
            get
            {
                InitSingletonIfNeeded();
                lock (_instanceMutex)
                {
                    return instance;
                }
            }
        }

        public static void InitSingletonIfNeeded()
        {
            lock (_instanceMutex)
            {
                if (instance == null)
                {
                    instance = new MatrixPluginEditor();
                    if (instance == null)
                    {
                        _logger.LogWarning("{method}: MatrixPluginEditor instance is null",
                            nameof(InitSingletonIfNeeded));
                        return;
                    }
                }
            }
        }

        // Replaces Feeder.ToolHost.ToolHost static singleton behavior
        private static readonly ReactiveProperty<IToolHost?> _pluginProperty = new(null);
        public static ReadOnlyReactiveProperty<IToolHost?> PluginProperty => _pluginProperty;
        public static IToolHost? CurrentPlugin => _pluginProperty.Value;
        private static void SetCurrentPlugin(IToolHost? plugin) => _pluginProperty.Value = plugin;

        public static LogLevel LogLevel
        {
            get => Instance.unityConnectionConfig.LogLevel;
            set
            {
                Instance.unityConnectionConfig.LogLevel = value;
                ApplyLogLevel(value);
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        public static string Host
        {
            get => Instance.unityConnectionConfig.Host;
            set
            {
                Instance.unityConnectionConfig.Host = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        public static string LocalHost
        {
            get => Instance.unityConnectionConfig.LocalHost;
            set
            {
                Instance.unityConnectionConfig.LocalHost = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        public static bool KeepConnected
        {
            get => Instance.unityConnectionConfig.KeepConnected;
            set
            {
                Instance.unityConnectionConfig.KeepConnected = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        public static bool KeepServerRunning
        {
            get => Instance.unityConnectionConfig.KeepServerRunning;
            set
            {
                Instance.unityConnectionConfig.KeepServerRunning = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        public static TransportMethod TransportMethod
        {
            get => Instance.unityConnectionConfig.TransportMethod;
            set
            {
                Instance.unityConnectionConfig.TransportMethod = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        public static int TimeoutMs
        {
            get => Instance.unityConnectionConfig.TimeoutMs;
            set
            {
                Instance.unityConnectionConfig.TimeoutMs = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        /// <summary>How long a tool call waits for an editor that dropped its link (domain reload) to come back.</summary>
        public static int ReconnectGraceMs => Instance.unityConnectionConfig.ReconnectGraceMs;

        /// <summary>Deadline of a tool that completes after a compile or domain reload.</summary>
        public static int DeferredToolTimeoutMs => Instance.unityConnectionConfig.DeferredToolTimeoutMs;

        /// <summary>Whether the server lists its <c>tool-call</c> proxy.</summary>
        public static bool EnableToolCallProxy => Instance.unityConnectionConfig.EnableToolCallProxy;

        public static int Port
        {
            get
            {
                if (Uri.TryCreate(Host, UriKind.Absolute, out var uri) && uri.Port > 0 && uri.Port <= Consts.Hub.MaxPort)
                    return uri.Port;

                return GeneratePortFromDirectory();
            }
        }

        public static string? Token
        {
            get => Instance.unityConnectionConfig.Token;
            set
            {
                Instance.unityConnectionConfig.Token = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }
        public static AuthOption AuthOption
        {
            get => Instance.unityConnectionConfig.AuthOption;
            set
            {
                Instance.unityConnectionConfig.AuthOption = value;
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }

        public static bool IsAutoGenerateSkills(string agentId)
        {
            var dict = Instance.unityConnectionConfig.SkillAutoGenerate;
            return dict.TryGetValue(agentId, out var enabled) && enabled;
        }

        public static void SetAutoGenerateSkills(string agentId, bool enabled)
        {
            Instance.unityConnectionConfig.SkillAutoGenerate[agentId] = enabled;
            NotifyChanged(Instance.unityConnectionConfig);
        }

        public static bool IsAutoConfigureAgent(string agentId)
        {
            var dict = Instance.unityConnectionConfig.AgentAutoConfigure;
            return dict.TryGetValue(agentId, out var enabled) && enabled;
        }

        /// <summary>Agent ids whose MCP config should be written automatically on editor load.</summary>
        public static System.Collections.Generic.List<string> AutoConfigureAgentIds
        {
            get
            {
                var result = new System.Collections.Generic.List<string>();
                foreach (var entry in Instance.unityConnectionConfig.AgentAutoConfigure)
                    if (entry.Value)
                        result.Add(entry.Key);
                return result;
            }
        }

        public static void SetAutoConfigureAgent(string agentId, bool enabled)
        {
            Instance.unityConnectionConfig.AgentAutoConfigure[agentId] = enabled;
            NotifyChanged(Instance.unityConnectionConfig);
        }

        public static string SkillsPath
        {
            get => Instance.unityConnectionConfig.SkillsPath;
            set
            {
                Instance.unityConnectionConfig.SkillsPath = NormalizeSkillsPath(value);
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }

        /// <summary>
        /// Normalises a user-supplied or configurator-supplied skills path so the on-disk
        /// `UserSettings/Feeder-MCP-Config.json` stays portable across machines and
        /// across path-separator conventions. Rules:
        /// <list type="bullet">
        /// <item>null / empty / whitespace → returned verbatim (caller decides what to do).</item>
        /// <item>Absolute path inside <see cref="ProjectRootPath"/> → rewritten to a project-relative
        /// path with forward slashes (e.g. <c>"C:\proj\.claude\skills"</c> → <c>".claude/skills"</c>).
        /// This is the path-portability fix for committing the config file to version control.</item>
        /// <item>Absolute path outside the project → returned unchanged (the user has explicitly
        /// chosen an external location; we do not rewrite it).</item>
        /// <item>Already-relative path → backslashes converted to forward slashes for cross-platform
        /// diff stability; no other change.</item>
        /// </list>
        /// </summary>
        [return: NotNullIfNotNull("value")]
        public static string? NormalizeSkillsPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            // Always use forward slashes in the persisted form for cross-platform diff stability.
            // The `!` is required because the Unity Editor C# compiler does not honor
            // `[NotNullWhen(false)]` on `string.IsNullOrWhiteSpace`, so it does not narrow
            // `value` (declared `string?`) to non-null after the early-return guard above and
            // would otherwise emit CS8602 here. The runtime check above already establishes
            // `value` is non-null at this point.
            var normalized = value!.Replace('\\', '/');

            if (!Path.IsPathRooted(normalized))
                return normalized;

            // Absolute path — check whether it lives inside the project root. If yes, make it relative.
            var root = ProjectRootPath.Replace('\\', '/').TrimEnd('/');
            // Compare case-insensitively on Windows because paths there are not case-sensitive.
            // macOS APFS is also case-insensitive by default, but that is an accepted gap here;
            // the typical Feeder-MCP-Config.json is committed from Windows machines and
            // the legacy values we auto-heal originate from Windows in practice.
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (normalized.StartsWith(root + "/", comparison))
                return normalized.Substring(root.Length + 1);

            if (normalized.Equals(root, comparison))
                return string.Empty;

            // Absolute path outside the project root — leave as-is.
            return normalized;
        }

        // 'new' is intentional: static dispatch on the subtype, instance logic lives in the base.
        public static new ReadOnlyReactiveProperty<FmpState> ConnectionState
            => ((MatrixPlugin)Instance).ConnectionState;
        public static new ReadOnlyReactiveProperty<bool> IsConnected
            => ((MatrixPlugin)Instance).IsConnected;

        public static new Task NotifyToolRequestCompleted(RequestToolCompletedData request, CancellationToken cancellationToken = default)
            => ((MatrixPlugin)Instance).NotifyToolRequestCompleted(request, cancellationToken);

        public static IDisposable SubscribeOnChanged(Action<UnityConnectionConfig> action, bool invokeImmediately = true)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var subscription = _onConfigChanged.Subscribe(action);
            if (invokeImmediately)
            {
                try { action(Instance.unityConnectionConfig); }
                catch (Exception e) { _logger.LogError(e, "{method}: exception invoking action immediately", nameof(SubscribeOnChanged)); }
            }
            return subscription;
        }

        public static new Task<bool> ConnectIfNeeded() => ((MatrixPlugin)Instance).ConnectIfNeeded();

        public static new Task<bool> Connect() => ((MatrixPlugin)Instance).Connect();

        // Disconnect() and DisconnectImmediate() are inherited from MatrixPlugin base.

        public static void StaticDispose()
        {
            _logger.LogTrace("{method} called.", nameof(StaticDispose));

            lock (_instanceMutex)
            {
                instance?.Dispose();
                instance = null!;
            }
        }

        static void NotifyChanged(UnityConnectionConfig data)
        {
            try { _onConfigChanged.OnNext(data); }
            catch (Exception e) { _logger.LogError(e, "{method}: exception", nameof(NotifyChanged)); }
        }
    }
}
