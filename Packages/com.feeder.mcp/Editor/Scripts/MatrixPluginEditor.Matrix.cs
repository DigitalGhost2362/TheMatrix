#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;
using Feeder.MCP.Editor.Matrix;
using Feeder.Fmp;

namespace Feeder.MCP
{
    public partial class MatrixPluginEditor
    {
        FeederMatrixAdapter? _matrixAdapter;

        FeederMatrixAdapter MatrixAdapter => _matrixAdapter ??= new FeederMatrixAdapter(this);

        // The Matrix adapter is the only transport: it is created on first use, so nothing is wired up here.
        // Kept as an empty method only because MatrixPluginEditor.cs still calls it from the constructor.
        protected override Task<bool> ConnectTransport() => MatrixAdapter.ConnectAsync();

        protected override Task DisconnectTransport() => _matrixAdapter != null
            ? _matrixAdapter.DisconnectAsync()
            : Task.CompletedTask;

        protected override void DisconnectImmediateTransport()
        {
            if (_matrixAdapter == null)
                return;

            _matrixAdapter.Dispose();
            _matrixAdapter = null;
            SetMatrixConnectionState(FmpState.Disconnected);
        }

        protected override Task NotifyToolRequestCompletedTransport(RequestToolCompletedData request, CancellationToken cancellationToken)
            => MatrixAdapter.CompleteDeferredAsync(request, cancellationToken);

        internal void SetMatrixConnectionState(FmpState state) => _connectionState.Value = state;

        // called from worker threads, so it must never build the adapter (its constructor needs the main thread)
        internal Task<CompileResponse> CompileOnServerAsync(CompileRequest request, CancellationToken cancellationToken)
            => _matrixAdapter?.CompileAsync(request, cancellationToken)
               ?? throw new System.InvalidOperationException("Feeder Local Matrix is not connected, so C# cannot be compiled.");

        /// <summary>
        /// Asks for more time for a running tool call (requestId = the tool's [RequestID] value). Safe from any
        /// thread; false when the call did not come through the matrix or has already finished.
        /// </summary>
        public static bool RequestMoreTime(string? requestId, System.TimeSpan extra)
            => HasInstance && !string.IsNullOrEmpty(requestId) &&
               Instance._matrixAdapter?.ExtendDeadline(requestId!, extra) == true;

        internal void AnnounceReload() => _matrixAdapter?.AnnounceReload();

        void DisposeMatrixAdapter()
        {
            _matrixAdapter?.Dispose();
            _matrixAdapter = null;
        }
    }
}
