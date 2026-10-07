#nullable enable
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.Logging;

namespace Feeder.MCP
{
    using MicrosoftLogLevel = Feeder.Logging.LogLevel;

    /// <summary>
    /// Builder for configuring a runtime <see cref="MatrixPluginRuntime"/> from C# code.
    /// Intended for game builds where no JSON config file is available.
    /// Obtain an instance via <see cref="MatrixPluginRuntime.Initialize()"/>.
    /// <example>
    /// <code>
    /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    /// static void SetupMcp()
    /// {
    ///     MatrixPluginRuntime.Initialize(builder =>
    ///     {
    ///         builder.WithConfig(c =>
    ///         {
    ///             c.Host  = "http://localhost:20000";
    ///             c.Token = "my-token";
    ///         });
    ///     })
    ///     .Build();
    /// }
    /// </code>
    /// </example>
    /// </summary>
    public sealed class MatrixPluginBuilder
    {
        /// <summary>
        /// The underlying <see cref="ToolHostBuilder"/> pre-configured with Unity
        /// defaults (logging, standard ignored assemblies, assembly scanning).
        /// Use this to configure host, token, additional ignored assemblies, custom
        /// tools/prompts/resources, and anything else supported by the MCP plugin.
        /// </summary>
        public IToolHostBuilder ToolHost { get; }

        private readonly MatrixPluginRuntime _runtimePlugin;
        private readonly ILogger? _logger;

        internal MatrixPluginBuilder(IToolHostBuilder mcpBuilder, MatrixPluginRuntime runtimePlugin, ILoggerProvider? loggerProvider = null)
        {
            ToolHost = mcpBuilder;
            _runtimePlugin = runtimePlugin;
            _logger = loggerProvider?.CreateLogger(nameof(MatrixPluginBuilder));

            // Apply Unity-specific defaults — the developer does not need to repeat these.
            ToolHost
                .AddLogging(lb =>
                {
                    lb.ClearProviders();
                    lb.SetMinimumLevel(MicrosoftLogLevel.Trace);
                    if (loggerProvider != null)
                        lb.AddProvider(loggerProvider);
                })
                .IgnoreAssemblies(
                    "mscorlib",
                    "Mono.Security",
                    "netstandard",
                    "nunit.framework",
                    "System",
                    "UnityEngine",
                    "UnityEditor",
                    "Unity.",
                    "Microsoft",
                    "R3",
                    "ToolHost",
                    "Reflect",
                    "Feeder.MCP.TestFiles",
                    "Feeder.MCP.Editor.Tests",
                    "Feeder.MCP.Tests");
        }

        /// <summary>
        /// Builds and connects a runtime <see cref="MatrixPluginRuntime"/> instance alongside the
        /// Editor's existing connection. The Editor's connection is not affected.
        /// Call <see cref="MatrixPluginRuntime.DisposeInstance()"/>
        /// to shut it down, or exit Play mode (handled automatically).
        /// </summary>
        public MatrixPluginRuntime Build()
        {
            _logger?.LogTrace("{method} called.", nameof(Build));

            _logger?.LogDebug("{method}: Building runtime MCP Plugin from builder...", nameof(Build));
            var built = _runtimePlugin.BuildFromBuilder(ToolHost);

            _logger?.LogTrace("{method} completed.", nameof(Build));
            return _runtimePlugin;
        }
    }
}
