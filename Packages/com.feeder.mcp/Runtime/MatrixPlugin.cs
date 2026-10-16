#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;
using Feeder.Fmp;
using Feeder.Logging;
using Feeder.Rx;

namespace Feeder.MCP
{
    using LogLevel = Runtime.Utils.LogLevel;

    public partial class MatrixPlugin : IDisposable
    {
        public const string Version = "0.89.0";

        private static int _singletonCount = 0;
        public static bool HasAnyInstance => _singletonCount > 0;
        protected static void IncrementSingletonCount() => Interlocked.Increment(ref _singletonCount);
        protected static void DecrementSingletonCount() => Interlocked.Decrement(ref _singletonCount);

        private static LogLevel _configuredLogLevel = LogLevel.Warning;
        // Uses direct enum comparison: configured threshold <= requested level means enabled
        public static bool IsLogEnabled(LogLevel level) => _configuredLogLevel <= level;
        public static void ApplyLogLevel(LogLevel level) => _configuredLogLevel = level;

        protected readonly CompositeDisposable _disposables = new();
        protected readonly ToolHostSlot _plugin = new();

        public IToolHost? ToolHostInstance => _plugin.Instance;
        public bool HasToolHostInstance => _plugin.HasInstance;

        public ILogger Logger => ToolHostInstance?.Logger ?? _logger;
        public Reflector? Reflector => ToolHostInstance?.McpManager.Reflector;
        public IToolManager? Tools => ToolHostInstance?.McpManager.ToolManager;

        public UnityLogCollector? LogCollector { get; protected set; } = null;

        // --- Connection state ---

        protected readonly ReactiveProperty<FmpState> _connectionState
            = new(FmpState.Disconnected);

        public ReadOnlyReactiveProperty<FmpState> ConnectionState => _connectionState;

        public ReadOnlyReactiveProperty<bool> IsConnected => _connectionState
            .Select(x => x == FmpState.Connected)
            .ToReadOnlyReactiveProperty(false);

        // --- Constructor / Dispose ---

        protected MatrixPlugin()
        {
            _disposables.Add(_connectionState);
        }

        public virtual void Dispose()
        {
            _disposables.Dispose();
            // LogCollector and _connectionState are disposed by _disposables
            LogCollector = null;
        }

        // --- Log collector ---

        public void AddUnityLogCollector(ILogStorage logStorage)
        {
            if (logStorage == null)
                throw new ArgumentNullException(nameof(logStorage));

            if (LogCollector != null)
                throw new InvalidOperationException($"{nameof(UnityLogCollector)} is already added.");

            LogCollector = new UnityLogCollector(logStorage);
            _disposables.Add(LogCollector);
        }

        public void AddUnityLogCollectorIfNeeded(Func<ILogStorage> logStorageProvider)
        {
            if (LogCollector != null)
                return;

            AddUnityLogCollector(logStorageProvider());
        }

        public void DisposeLogCollector()
        {
            LogCollector?.Save();
            LogCollector?.Dispose();
            LogCollector = null;
        }

        // --- Connection methods ---

        public virtual Task<bool> ConnectIfNeeded()
        {
            if (!unityConnectionConfig.KeepConnected)
                return Task.FromResult(false);
            return Connect();
        }

        public virtual async Task<bool> Connect() => await ConnectTransport();

        public virtual async Task Disconnect() => await DisconnectTransport();

        public virtual void DisconnectImmediate() => DisconnectImmediateTransport();

        public virtual async Task NotifyToolRequestCompleted(RequestToolCompletedData request, CancellationToken cancellationToken = default)
            => await NotifyToolRequestCompletedTransport(request, cancellationToken);

        // The base plugin has no connection of its own: the Editor subclass supplies the transport
        // (the Matrix adapter) through these hooks. They are separate methods because the Editor's
        // static Connect()/NotifyToolRequestCompleted() share the signature of the public ones above.
        protected virtual Task<bool> ConnectTransport() => Task.FromResult(false);

        protected virtual Task DisconnectTransport() => Task.CompletedTask;

        protected virtual void DisconnectImmediateTransport() { }

        protected virtual Task NotifyToolRequestCompletedTransport(RequestToolCompletedData request, CancellationToken cancellationToken)
            => Task.CompletedTask;

        // --- Token / Port utilities ---

        /// <summary>
        /// Generates a cryptographically random URL-safe token.
        /// Used by <see cref="UnityConnectionConfig.SetDefault"/> for initial token generation.
        /// </summary>
        public static string GenerateToken()
        {
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Generate a deterministic TCP port based on current directory.
        /// Uses SHA256 hash for better distribution and less collisions.
        /// Port range: 20000-29999 (avoids Windows ephemeral/reserved port ranges).
        /// </summary>
        public static int GeneratePortFromDirectory()
        {
            const int MinPort = 20000; // Range chosen to avoid Windows ephemeral/reserved ports (49152-65535)
            const int MaxPort = 29999;
            const int PortRange = MaxPort - MinPort + 1;

            var currentDir = Environment.CurrentDirectory.ToLowerInvariant();

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(currentDir));

                // Use first 4 bytes as an unsigned integer to avoid Math.Abs(int.MinValue) overflow
                var hash = (uint)BitConverter.ToInt32(hashBytes, 0);

                // Map to port range. Offset by +1 vs the original com.feeder.mcp
                // package so both plugins in the same project never derive the same port.
                var port = MinPort + (int)((hash + 1) % PortRange);

                return port;
            }
        }
    }
}
