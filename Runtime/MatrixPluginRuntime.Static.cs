#nullable enable
using System;
using Feeder.ToolHost;
using Feeder.MCP.Utils;
using Feeder.Logging;
using Feeder.Rx;

namespace Feeder.MCP
{
    using ILogger = Feeder.Logging.ILogger;
    using LogLevel = Runtime.Utils.LogLevel;

    public partial class MatrixPluginRuntime
    {
        static readonly Subject<UnityConnectionConfig> _onConfigChanged = new Subject<UnityConnectionConfig>();
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger<MatrixPluginRuntime>();
        static readonly object _instanceMutex = new();

        static MatrixPluginRuntime _instance = null!;

        public static bool HasInstance
        {
            get { lock (_instanceMutex) { return _instance != null; } }
        }

        public static MatrixPluginRuntime Instance
        {
            get { lock (_instanceMutex) { return _instance; } }
        }

        static void InitSingletonIfNeeded()
        {
            lock (_instanceMutex)
            {
                if (_instance == null)
                    _instance = new MatrixPluginRuntime();
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
        public static LogLevel LogLevel
        {
            get => _instance.unityConnectionConfig.LogLevel;
            set
            {
                _instance.unityConnectionConfig.LogLevel = value;
                ApplyLogLevel(value);
                NotifyChanged(Instance.unityConnectionConfig);
            }
        }

        /// <summary>
        /// Disposes the runtime plugin instance created by <see cref="Initialize"/>.
        /// Safe to call multiple times; no-op if already disposed.
        /// </summary>
        public static void DisposeInstance()
        {
            lock (_instanceMutex)
            {
                _instance?._plugin.Dispose();
            }
        }

        /// <summary>
        /// Creates a <see cref="MatrixPluginBuilder"/> pre-configured with Unity
        /// defaults. Use <see cref="MatrixPluginBuilder.ToolHost"/> to configure
        /// host, token, ignored assemblies, custom tools, etc., then call
        /// <see cref="MatrixPluginBuilder.Build"/> to apply and connect.
        /// <para>
        /// Intended for game builds where no JSON config file is available.
        /// In Unity Editor (Edit and Play mode), the editor plugin is automatically
        /// initialized from <c>UserSettings/Feeder-MCP-Config.json</c>.
        /// </para>
        /// <example>
        /// <code>
        /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        /// static void SetupMcp()
        /// {
        ///     var mcpPlugin = MatrixPluginRuntime.Initialize(builder =>
        ///         {
        ///             builder.WithConfig(c =>
        ///             {
        ///                 c.Host  = "http://localhost:20000";
        ///                 c.Token = "my-token";
        ///             });
        ///         })
        ///         .Build();
        ///     mcpPlugin.Connect();
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public static MatrixPluginBuilder Initialize(Action<IToolHostBuilder>? configure = null)
        {
            InitSingletonIfNeeded();
            var runtimeInstance = Instance;
            var version = runtimeInstance.BuildVersion();
            var loggerProvider = runtimeInstance.BuildLoggerProvider();

            var mcpBuilder = new ToolHostBuilder(version, loggerProvider);
            configure?.Invoke(mcpBuilder);

            return new MatrixPluginBuilder(mcpBuilder, runtimeInstance, loggerProvider);
        }

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

        static void NotifyChanged(UnityConnectionConfig data)
        {
            try { _onConfigChanged.OnNext(data); }
            catch (Exception e) { _logger.LogError(e, "{method}: exception", nameof(NotifyChanged)); }
        }

        public static void StaticDispose()
        {
            _logger.LogTrace("{method} called.", nameof(StaticDispose));
            lock (_instanceMutex)
            {
                _instance?.Dispose();
                _instance = null!;
            }
        }
    }
}
