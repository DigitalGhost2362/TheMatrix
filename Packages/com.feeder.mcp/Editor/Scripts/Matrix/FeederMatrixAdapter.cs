#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.McpPlugin;
using Feeder.McpPlugin.Common.Model;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;
using UnityEditor;
using UnityEngine;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Feeder.MCP.Editor.Matrix
{
    /// <summary>
    /// Connects the existing Feeder tool registry to Feeder Local Matrix over FMP/1. Tool
    /// implementations remain unchanged; this class only publishes descriptors and routes calls,
    /// including deferred completions that survive Unity compilation and domain reloads.
    /// </summary>
    internal sealed class FeederMatrixAdapter : IDisposable
    {
        const string ProtocolVersion = "1";
        const string InstanceSessionKey = "Feeder.MCP.Matrix.UnityInstanceId";

        readonly UnityMcpPluginEditor _owner;
        readonly ILogger _logger;
        readonly SemaphoreSlim _connectionGate = new(1, 1);
        readonly ConcurrentDictionary<string, CancellationTokenSource> _operations = new();

        HubConnection? _connection;
        IDisposable? _toolsUpdatedSubscription;
        CancellationTokenSource _lifetime = new();
        string _projectId = string.Empty;
        string _projectPathHash = string.Empty;
        string _unityInstanceId = string.Empty;
        string _unityVersion = string.Empty;
        FmpEditorState _lastEditorState = FmpEditorState.Ready;
        bool _disposed;

        public FeederMatrixAdapter(UnityMcpPluginEditor owner)
        {
            _owner = owner;
            _logger = UnityLoggerFactory.LoggerFactory.CreateLogger<FeederMatrixAdapter>();
            _unityVersion = Application.unityVersion;
            EnsureIdentity();
            UpdateEditorState();
            EditorApplication.update += UpdateEditorState;
        }

        public async Task<bool> ConnectAsync()
        {
            if (_disposed)
                return false;

            await _connectionGate.WaitAsync();
            try
            {
                if (_connection?.State == HubConnectionState.Connected)
                    return true;

                _owner.SetMatrixConnectionState(HubConnectionState.Connecting);
                if (!await McpServerManager.InstallServerBinaryIfNeeded(unattended: true))
                {
                    _owner.SetMatrixConnectionState(HubConnectionState.Disconnected);
                    return false;
                }

                // only ever register with this project's own server, never whatever happens to answer on the port
                if (!await McpServerManager.StartOwnServerAsync())
                {
                    _owner.SetMatrixConnectionState(HubConnectionState.Disconnected);
                    ScheduleReconnect();
                    return false;
                }

                await DisposeConnectionAsync();
                _connection = BuildConnection();

                Exception? lastError = null;
                for (var attempt = 0; attempt < 40 && !_lifetime.IsCancellationRequested; attempt++)
                {
                    try
                    {
                        await _connection.StartAsync(_lifetime.Token);
                        await RegisterAsync(_lifetime.Token);
                        SubscribeToRegistryChanges();
                        CancelReconnect();
                        _owner.SetMatrixConnectionState(HubConnectionState.Connected);
                        _logger.LogInformation("Connected Unity editor to Feeder Local Matrix at {url}", MatrixHubUrl);
                        return true;
                    }
                    catch (Exception e) when (attempt < 39 && !_lifetime.IsCancellationRequested)
                    {
                        lastError = e;
                        try
                        {
                            if (_connection.State != HubConnectionState.Disconnected)
                                await _connection.StopAsync();
                        }
                        catch { }
                        await Task.Delay(250, _lifetime.Token);
                    }
                }

                _logger.LogError(lastError, "Unable to connect Unity editor to Feeder Local Matrix at {url}", MatrixHubUrl);
                _owner.SetMatrixConnectionState(HubConnectionState.Disconnected);
                ScheduleReconnect();
                return false;
            }
            catch (OperationCanceledException)
            {
                _owner.SetMatrixConnectionState(HubConnectionState.Disconnected);
                return false;
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        static readonly TimeSpan[] ReconnectBackoff =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
        };

        int _reconnectAttempt;
        int _reconnectGeneration;

        // the SignalR client gives up after its own short retry list; without this the bridge sat in a dead
        // "linking" state until the next domain reload
        void ScheduleReconnect()
        {
            if (_disposed || _lifetime.IsCancellationRequested)
                return;
            if (!UnityMcpPluginEditor.KeepConnected || !MatrixActivation.IsEnabled)
                return;
            if (McpServerManager.ServerFault.CurrentValue != null)
                return;

            if (_reconnectAttempt >= ReconnectBackoff.Length)
            {
                McpServerManager.SetPendingNote(null);
                McpServerManager.SetServerFault($"Cannot reach this project's MCP server on :{UnityMcpPluginEditor.Port} after {ReconnectBackoff.Length} retries - see logs, then Retry.");
                _reconnectAttempt = 0;
                return;
            }

            var delay = ReconnectBackoff[_reconnectAttempt++];
            var generation = Interlocked.Increment(ref _reconnectGeneration);
            McpServerManager.SetPendingNote($"retry {_reconnectAttempt}/{ReconnectBackoff.Length} in {delay.TotalSeconds:0}s");
            _ = Task.Delay(delay, _lifetime.Token).ContinueWith(task =>
            {
                if (task.IsCanceled || generation != Volatile.Read(ref _reconnectGeneration))
                    return;
                Feeder.ReflectorNet.Utils.MainThread.Instance.Run(() =>
                {
                    if (_disposed || !UnityMcpPluginEditor.KeepConnected || !MatrixActivation.IsEnabled)
                    {
                        McpServerManager.SetPendingNote(null);
                        return;
                    }
                    _ = ConnectAsync();
                });
            });
        }

        void CancelReconnect()
        {
            Interlocked.Increment(ref _reconnectGeneration);
            _reconnectAttempt = 0;
            McpServerManager.SetPendingNote(null);
        }

        public async Task DisconnectAsync()
        {
            CancelReconnect();
            await _connectionGate.WaitAsync();
            try
            {
                _toolsUpdatedSubscription?.Dispose();
                _toolsUpdatedSubscription = null;
                if (_connection?.State == HubConnectionState.Connected)
                {
                    try { await _connection.SendAsync("Unregister", _projectId, _unityInstanceId); }
                    catch { }
                }
                await DisposeConnectionAsync();
                _owner.SetMatrixConnectionState(HubConnectionState.Disconnected);
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        public async Task CompleteDeferredAsync(RequestToolCompletedData request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId) || request.Result == null)
                return;

            if (_connection?.State != HubConnectionState.Connected && !await ConnectAsync())
                throw new InvalidOperationException("Feeder Local Matrix is not connected.");

            await SendCompletedResultAsync(request.RequestId, request.Result, cancellationToken);
            RemoveOperation(request.RequestId);
        }

        HubConnection BuildConnection()
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(MatrixHubUrl)
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                })
                .Build();

            connection.On<ToolInvoke>("InvokeTool", invoke => InvokeToolAsync(invoke));
            connection.On<string>("CancelTool", operationId =>
            {
                if (_operations.TryGetValue(operationId, out var cts))
                    cts.Cancel();
            });
            connection.On<McpClientInfo[]>("McpClientsChanged", clients => PublishMcpClients(clients));
            connection.Reconnecting += _ =>
            {
                _owner.SetMatrixConnectionState(HubConnectionState.Reconnecting);
                return Task.CompletedTask;
            };
            connection.Reconnected += async _ =>
            {
                try
                {
                    await RegisterAsync(_lifetime.Token);
                    _owner.SetMatrixConnectionState(HubConnectionState.Connected);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to re-register Unity with Feeder Local Matrix");
                }
            };
            connection.Closed += _ =>
            {
                _owner.SetMatrixConnectionState(HubConnectionState.Disconnected);
                PublishMcpClients(Array.Empty<McpClientInfo>());
                if (ReferenceEquals(connection, _connection))
                    ScheduleReconnect();
                return Task.CompletedTask;
            };
            return connection;
        }

        async Task RegisterAsync(CancellationToken cancellationToken)
        {
            EnsureIdentity();
            var snapshot = BuildRegistrySnapshot();
            var response = await _connection!.InvokeAsync<UnityRegisterResponse>("Register", new UnityRegisterRequest
            {
                ProtocolVersion = ProtocolVersion,
                SupportedProtocolVersions = new[] { ProtocolVersion },
                ProjectId = _projectId,
                UnityInstanceId = _unityInstanceId,
                ProjectPathHash = _projectPathHash,
                UnityVersion = _unityVersion,
                PluginVersion = UnityMcpPlugin.Version,
                ToolRegistryHash = snapshot.RegistryHash,
                AuthToken = UnityMcpPluginEditor.Token ?? string.Empty,
                EditorState = _lastEditorState,
                PendingOperations = _operations.Keys.ToArray(),
            }, cancellationToken);

            if (response.Error != null)
                throw new InvalidOperationException($"[{response.Error.Code}] {response.Error.Message}");
            if (response.ChosenProtocolVersion != ProtocolVersion)
                throw new InvalidOperationException($"Unsupported FMP protocol '{response.ChosenProtocolVersion}'.");
            if (!response.RegistryUpToDate)
                await _connection!.SendAsync("PublishRegistry", snapshot, cancellationToken);
        }

        void SubscribeToRegistryChanges()
        {
            _toolsUpdatedSubscription?.Dispose();
            var manager = _owner.Tools;
            if (manager != null)
                _toolsUpdatedSubscription = manager.OnToolsUpdated.Subscribe(unit => { _ = PublishRegistryAsync(); });
        }

        async Task PublishRegistryAsync()
        {
            try
            {
                if (_connection?.State == HubConnectionState.Connected)
                    await _connection.SendAsync("PublishRegistry", BuildRegistrySnapshot(), _lifetime.Token);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to publish updated tool registry to Feeder Local Matrix");
            }
        }

        RegistrySnapshot BuildRegistrySnapshot()
        {
            EnsureIdentity();
            var manager = _owner.Tools ?? throw new InvalidOperationException("Tool manager is not available.");
            var descriptors = manager.GetAllTools()
                .Where(tool => tool != null && manager.IsToolEnabled(tool.Name))
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .Select(tool => new ToolDescriptor
                {
                    Name = tool.Name,
                    Title = tool.Title,
                    Description = tool.Description,
                    InputSchemaJsonUtf8 = Encoding.UTF8.GetBytes(tool.InputSchema?.ToJsonString() ?? "{}"),
                    ReadOnly = tool.ReadOnlyHint ?? false,
                    Destructive = tool.DestructiveHint ?? false,
                    Idempotent = tool.IdempotentHint ?? false,
                    ThreadSafe = false,
                })
                .ToArray();

            var hashSource = string.Join("\n", descriptors.Select(tool =>
                $"{tool.Name}\n{tool.Title}\n{tool.Description}\n{Encoding.UTF8.GetString(tool.InputSchemaJsonUtf8)}\n{tool.ReadOnly}\n{tool.Destructive}\n{tool.Idempotent}"));
            return new RegistrySnapshot
            {
                ProjectId = _projectId,
                RegistryHash = Sha256(hashSource),
                Tools = descriptors,
            };
        }

        async Task InvokeToolAsync(ToolInvoke invoke)
        {
            var deferred = false;
            var cts = CreateOperationCancellation(invoke);
            _operations[invoke.OperationId] = cts;
            try
            {
                var manager = _owner.Tools ?? throw new InvalidOperationException("Tool manager is not available.");
                var tool = manager.GetAllTools().FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, invoke.ToolName, StringComparison.Ordinal));
                if (tool == null || !manager.IsToolEnabled(invoke.ToolName))
                {
                    await SendErrorAsync(invoke.OperationId, "FMP_TOOL_NOT_FOUND", $"Tool '{invoke.ToolName}' was not found or is disabled.");
                    return;
                }

                var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(invoke.ArgumentsJsonUtf8)
                                ?? new Dictionary<string, JsonElement>();
                var response = await manager.RunTool(tool, invoke.OperationId, arguments, cts.Token);
                if (IsStatus(response, "Processing"))
                {
                    deferred = true;
                    return;
                }

                await SendCompletedResultAsync(invoke.OperationId, response, cts.Token);
            }
            catch (OperationCanceledException)
            {
                await SendErrorAsync(invoke.OperationId, "FMP_CANCELLED", $"Tool '{invoke.ToolName}' was cancelled.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Tool {toolName} failed in Feeder matrix adapter", invoke.ToolName);
                await SendErrorAsync(invoke.OperationId, "FMP_TOOL_FAILED", e.Message);
            }
            finally
            {
                if (!deferred)
                    RemoveOperation(invoke.OperationId);
            }
        }

        async Task SendCompletedResultAsync(string operationId, ResponseCallTool response, CancellationToken cancellationToken)
        {
            var success = !IsStatus(response, "Error");
            var payload = SerializeCallToolResult(response, !success);
            await _connection!.SendAsync("CompleteTool", new ToolResult
            {
                OperationId = operationId,
                Success = true,
                ContentJsonUtf8 = payload,
            }, cancellationToken);
        }

        async Task SendErrorAsync(string operationId, string code, string message)
        {
            if (_connection?.State != HubConnectionState.Connected)
                return;
            await _connection.SendAsync("CompleteTool", new ToolResult
            {
                OperationId = operationId,
                Success = false,
                Error = new FmpError { Code = code, Message = message },
            });
        }

        static byte[] SerializeCallToolResult(ResponseCallTool response, bool isError)
        {
            // written straight to utf-8: large image payloads would otherwise be copied once per intermediate form
            using var stream = new MemoryStream();
            var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteBoolean("isError", isError);
            writer.WriteStartArray("content");
            foreach (var block in response.Content ?? new List<ContentBlock>())
            {
                var type = (block.Type ?? "text").ToLowerInvariant();
                writer.WriteStartObject();
                switch (type)
                {
                    case "image":
                    case "audio":
                        writer.WriteString("type", type);
                        writer.WriteString("data", block.Data ?? string.Empty);
                        writer.WriteString("mimeType", block.MimeType ?? string.Empty);
                        break;
                    case "resource":
                        writer.WriteString("type", type);
                        if (block.Resource != null)
                        {
                            writer.WriteStartObject("resource");
                            writer.WriteString("uri", block.Resource.Uri);
                            writer.WriteString("mimeType", block.Resource.MimeType);
                            if (!string.IsNullOrEmpty(block.Resource.Text)) writer.WriteString("text", block.Resource.Text);
                            if (!string.IsNullOrEmpty(block.Resource.Blob)) writer.WriteString("blob", block.Resource.Blob);
                            writer.WriteEndObject();
                        }
                        break;
                    default:
                        writer.WriteString("type", "text");
                        writer.WriteString("text", block.Text ?? string.Empty);
                        break;
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (response.StructuredContent != null)
            {
                writer.WritePropertyName("structuredContent");
                response.StructuredContent.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.Flush();
            return stream.ToArray();
        }

        CancellationTokenSource CreateOperationCancellation(ToolInvoke invoke)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            if (invoke.DeadlineUnixMs > 0)
            {
                var remaining = DateTimeOffset.FromUnixTimeMilliseconds(invoke.DeadlineUnixMs) - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) linked.Cancel();
                else linked.CancelAfter(remaining);
            }
            return linked;
        }

        McpClientData[] _lastMcpClients = Array.Empty<McpClientData>();

        /// <summary>
        /// Forwards the matrix's MCP client set into <c>McpManager</c> so the plugin's client
        /// observables (and the "AI agent" indicator in the Matrix Bridge window) reflect reality.
        /// </summary>
        void PublishMcpClients(McpClientInfo[]? clients)
        {
            try
            {
                var manager = _owner.McpPluginInstance?.McpManager as McpManager;
                if (manager == null)
                    return;

                var current = (clients ?? Array.Empty<McpClientInfo>())
                    .Select(client => new McpClientData
                    {
                        SessionId = client.SessionId,
                        ClientName = string.IsNullOrEmpty(client.ClientName) ? "MCP client" : client.ClientName,
                        ClientTitle = client.ClientTitle,
                        ClientVersion = client.ClientVersion,
                        IsConnected = client.IsConnected,
                    })
                    .ToArray();

                var previous = Interlocked.Exchange(ref _lastMcpClients, current);
                var added = current.Where(c => previous.All(p => p.SessionId != c.SessionId)).ToArray();
                var removed = previous.Where(p => current.All(c => c.SessionId != p.SessionId)).ToArray();

                _ = ForwardAsync();

                async Task ForwardAsync()
                {
                    try
                    {
                        if (added.Length == 0 && removed.Length == 0)
                        {
                            await manager.OnInitialClientData(current);
                            return;
                        }
                        foreach (var client in added)
                            await manager.OnMcpClientConnected(client, current);
                        foreach (var client in removed)
                            await manager.OnMcpClientDisconnected(client, current);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Failed to publish MCP client change to McpManager");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to process MCP client change from Feeder Local Matrix");
            }
        }

        void RemoveOperation(string operationId)
        {
            if (_operations.TryRemove(operationId, out var cts))
                cts.Dispose();
        }

        void EnsureIdentity()
        {
            if (!string.IsNullOrEmpty(_projectId))
                return;

            var projectRoot = UnityMcpPluginEditor.ProjectRootPath.Replace('\\', '/').TrimEnd('/');
            _projectPathHash = Sha256(projectRoot.ToLowerInvariant());
            var projectName = System.IO.Path.GetFileName(projectRoot);
            _projectId = $"{projectName}-{_projectPathHash.Substring(0, 12)}";
            _unityInstanceId = SessionState.GetString(InstanceSessionKey, string.Empty);
            if (string.IsNullOrEmpty(_unityInstanceId))
            {
                _unityInstanceId = Guid.NewGuid().ToString("N");
                SessionState.SetString(InstanceSessionKey, _unityInstanceId);
            }
        }

        static string Sha256(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }

        static bool IsStatus(ResponseCallTool response, string status)
            => string.Equals(response.Status.ToString(), status, StringComparison.OrdinalIgnoreCase);

        void UpdateEditorState()
        {
            if (EditorApplication.isCompiling) _lastEditorState = FmpEditorState.Compiling;
            else if (EditorApplication.isPaused) _lastEditorState = FmpEditorState.Paused;
            else if (EditorApplication.isPlaying) _lastEditorState = FmpEditorState.Playing;
            else _lastEditorState = FmpEditorState.Ready;
        }

        static string MatrixHubUrl => $"http://127.0.0.1:{UnityMcpPluginEditor.Port}/fmp";

        async Task DisposeConnectionAsync()
        {
            var connection = _connection;
            if (connection == null)
                return;
            _connection = null;
            try { await connection.DisposeAsync(); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Interlocked.Increment(ref _reconnectGeneration);
            EditorApplication.update -= UpdateEditorState;
            _lifetime.Cancel();
            _toolsUpdatedSubscription?.Dispose();
            foreach (var operation in _operations.Keys.ToArray())
                RemoveOperation(operation);
            try { _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { }
            _connection = null;
            _lifetime.Dispose();
            _connectionGate.Dispose();
        }
    }
}
