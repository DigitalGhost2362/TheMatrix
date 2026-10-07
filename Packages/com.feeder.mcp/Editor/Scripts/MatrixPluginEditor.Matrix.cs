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

        void DisposeMatrixAdapter()
        {
            _matrixAdapter?.Dispose();
            _matrixAdapter = null;
        }
    }
}
