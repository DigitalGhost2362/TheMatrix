#nullable enable
using System.Diagnostics;
using Feeder.ToolHost;
using Feeder.Logging;

namespace Feeder.MCP
{
    /// <summary>
    /// Runtime singleton for game builds. Provides the developer-facing
    /// <see cref="Initialize"/> API and maintains a separate MCP connection
    /// alongside the Editor's connection.
    /// </summary>
    public partial class MatrixPluginRuntime : MatrixPlugin
    {
        protected MatrixPluginRuntime(UnityConnectionConfig? config = null) : base()
        {
            unityConnectionConfig = config ?? new UnityConnectionConfig();
            IncrementSingletonCount();
        }

        // NOTE: the runtime plugin has no transport (the old SignalR connection layer was removed and the
        // Matrix adapter is Editor-only), so Connect() returns false and ConnectionState stays Disconnected.
        internal IToolHost BuildFromBuilder(IToolHostBuilder builder)
        {
            _logger.LogTrace("{method} called.", nameof(BuildFromBuilder));

            var stopwatch = Stopwatch.StartNew();
            var built = _plugin.Set(builder.Build(CreateDefaultReflector()));
            stopwatch.Stop();
            _logger.LogDebug("Runtime MCP Plugin built in {elapsedMilliseconds} ms.",
                stopwatch.ElapsedMilliseconds);

            ApplyConfigToToolHost(built);

            _logger.LogTrace("{method} completed.", nameof(BuildFromBuilder));
            return built;
        }

        public override void Dispose()
        {
            DecrementSingletonCount();
            base.Dispose();
            _plugin.Dispose();
        }
    }
}
