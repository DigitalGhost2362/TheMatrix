#nullable enable

using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost.Common.Model;
using Feeder.ToolHost.Common.Utils;
using Feeder.Rx;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.UI.MatrixSpace
{
    /// <summary>
    /// Topbar indicator showing how many AI clients are connected to the MCP bridge.
    /// Green dot + "LINKS: n"; the tooltip lists each client name and version.
    /// </summary>
    public sealed class McpClientsIndicatorView : VisualElement
    {
        private readonly VisualElement _dot;
        private readonly Label _label;
        private CompositeDisposable? _disposables;

        public McpClientsIndicatorView()
        {
            AddToClassList("matrix-mcp-slot");

            _dot = new VisualElement();
            _dot.AddToClassList("status-indicator-circle");
            _dot.AddToClassList("status-indicator-circle-disconnected");
            _dot.AddToClassList("matrix-mcp-dot");
            Add(_dot);

            _label = new Label("LINKS: 0");
            _label.AddToClassList("matrix-mcp-label");
            Add(_label);

            RegisterCallback<AttachToPanelEvent>(_ => Subscribe());
            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());
        }

        private void Subscribe()
        {
            Unsubscribe();
            _disposables = new CompositeDisposable();

            MatrixPluginEditor.PluginProperty
                .WhereNotNull()
                .Subscribe(plugin =>
                {
                    plugin.McpManager.OnClientsChanged
                        .ObserveOnCurrentSynchronizationContext()
                        .Subscribe(clients => Refresh(clients))
                        .AddTo(_disposables!);

                    Refresh(plugin.McpManager.ActiveClients);
                })
                .AddTo(_disposables);

            // Initial snapshot in case the plugin was already up before this view attached.
            var manager = MatrixPluginEditor.Instance.ToolHostInstance?.McpManager;
            if (manager != null)
                Refresh(manager.ActiveClients);
        }

        private void Unsubscribe()
        {
            _disposables?.Dispose();
            _disposables = null;
        }

        private void Refresh(IEnumerable<McpClientData> clients)
        {
            var connected = clients.Where(c => c.IsConnected).ToList();

            _label.text = $"LINKS: {connected.Count}";
            _dot.EnableInClassList("status-indicator-circle-online", connected.Count > 0);
            _dot.EnableInClassList("status-indicator-circle-disconnected", connected.Count == 0);

            tooltip = connected.Count > 0
                ? "AI clients connected to the MCP bridge:\n" + string.Join("\n", connected.Select(c =>
                    $"{(string.IsNullOrEmpty(c.ClientTitle) ? c.ClientName : c.ClientTitle)} {c.ClientVersion}".Trim()))
                : "No AI clients connected to the MCP bridge.";
        }
    }
}
