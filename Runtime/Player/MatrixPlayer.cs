#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Feeder.Fmp;
using UnityEngine;

namespace Feeder.MCP.Player
{
    /// <summary>
    /// Links a running game (a build, or Play mode) to the project's Matrix server as a player. The player sits next to the
    /// Unity Editor's own link, never replaces it, and serves the tools marked <see cref="MatrixRuntimeToolAttribute"/>.
    /// Start it with <see cref="Connect"/>, or launch a build with <c>-matrixPort 20270 -matrixToken T [-matrixHost H] [-matrixProject P]</c>.
    /// </summary>
    public static class MatrixPlayer
    {
        const string Tag = "[Matrix player] ";
        static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        static readonly object Gate = new object();

        static CancellationTokenSource? _life;
        static MatrixPlayerTools? _tools;
        static GameObject? _pump;
        static volatile bool _connected;

        /// <summary>True while the player is registered with the matrix.</summary>
        public static bool IsConnected => _connected;

        /// <summary>Number of runtime tools this player offers; 0 until <see cref="Connect"/> ran.</summary>
        public static int ToolCount => _tools?.Count ?? 0;

        /// <summary>The id the Editor gives a project, so a player started from the Editor lands under the same project.</summary>
        public static string ProjectIdFor(string projectRoot)
        {
            var root = projectRoot.Replace('\\', '/').TrimEnd('/');
            return Path.GetFileName(root) + "-" + Sha256(root.ToLowerInvariant()).Substring(0, 12);
        }

        /// <summary>Starts the link and keeps it alive until <see cref="Disconnect"/>. Call it on the main thread.</summary>
        public static void Connect(string host, int port, string token, string? projectId = null)
        {
            Disconnect();
            if (string.IsNullOrEmpty(projectId))
                projectId = DefaultProjectId();

            var tools = MatrixPlayerTools.Scan();
            _tools = tools;
            // a build that loses focus would stop answering otherwise
            Application.runInBackground = true;
            EnsurePump();
            var life = new CancellationTokenSource();
            lock (Gate)
                _life = life;
            // read here: Unity's Application properties belong to the main thread
            var unityVersion = Application.unityVersion;
            var url = new Uri("http://" + host + ":" + port + "/fmp");
            _ = Task.Run(() => RunAsync(url, token, projectId!, unityVersion, tools, life.Token));
        }

        public static void Disconnect()
        {
            CancellationTokenSource? life;
            lock (Gate)
            {
                life = _life;
                _life = null;
            }
            if (life == null)
                return;
            life.Cancel();
            _connected = false;
            if (_pump != null)
            {
                UnityEngine.Object.Destroy(_pump);
                _pump = null;
            }
        }

        // a build started with the matrix address on its command line links itself; any other start does nothing
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoConnect()
        {
            var args = Environment.GetCommandLineArgs();
            var portText = Option(args, "-matrixPort") ?? Environment.GetEnvironmentVariable("MATRIX_PLAYER_PORT");
            if (!int.TryParse(portText, out var port) || _life != null)
                return;
            Connect(Option(args, "-matrixHost") ?? "127.0.0.1", port,
                Option(args, "-matrixToken") ?? Environment.GetEnvironmentVariable("MATRIX_PLAYER_TOKEN") ?? string.Empty,
                Option(args, "-matrixProject"));
        }

        static string? Option(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        static string DefaultProjectId()
        {
#if UNITY_EDITOR
            return ProjectIdFor(Path.GetDirectoryName(Application.dataPath)!);
#else
            return "player-" + Application.productName;
#endif
        }

        static void EnsurePump()
        {
            _pump = new GameObject("Matrix Player Link") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_pump);
            _pump.AddComponent<MatrixPlayerPump>();
        }

        internal static void Drain()
        {
            while (MainThread.TryDequeue(out var work))
                work();
        }

        static async Task RunAsync(Uri url, string token, string projectId, string unityVersion, MatrixPlayerTools tools, CancellationToken ct)
        {
            var instanceId = Guid.NewGuid().ToString("N");
            var delay = TimeSpan.FromSeconds(1);
            while (!ct.IsCancellationRequested)
            {
                var connection = new FmpConnection(url, new FmpOptions { ReconnectDelays = Array.Empty<TimeSpan>() });
                var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                connection.On("InvokeTool", arguments => InvokeAsync(connection, tools, PlayerJson.First<PlayerToolInvoke>(arguments)));
                connection.On("CancelTool", _ => Task.CompletedTask);
                connection.Closed += _ => closed.TrySetResult(true);
                connection.StateChanged += state =>
                {
                    if (state == FmpState.Disconnected)
                        closed.TrySetResult(true);
                };
                try
                {
                    await connection.StartAsync(ct).ConfigureAwait(false);
                    var snapshot = tools.Snapshot(projectId);
                    var response = PlayerJson.Parse<PlayerRegisterResponse>(await connection.InvokeAsync("RegisterPlayer", PlayerJson.Arguments(new PlayerRegisterRequest
                    {
                        ProjectId = projectId,
                        UnityInstanceId = instanceId,
                        ProjectPathHash = Sha256(projectId),
                        UnityVersion = unityVersion,
                        PluginVersion = "player-1",
                        SupportedProtocolVersions = new[] { "1" },
                        ToolRegistryHash = snapshot.RegistryHash,
                        AuthToken = token,
                    }), ct).ConfigureAwait(false));
                    if (response.Error != null)
                    {
                        Debug.LogWarning(Tag + "the matrix refused the player link: " + response.Error.Message);
                        return;
                    }
                    await connection.SendAsync("PublishRegistry", PlayerJson.Arguments(snapshot), ct).ConfigureAwait(false);
                    _connected = true;
                    delay = TimeSpan.FromSeconds(1);
                    Debug.Log(Tag + "linked to " + url + " with " + tools.Count + " runtime tool(s).");
                    using (ct.Register(() => closed.TrySetResult(true)))
                        await closed.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (e.Message.IndexOf("RegisterPlayer", StringComparison.Ordinal) >= 0 || e.Message.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Debug.LogWarning(Tag + "this Matrix server has no player link; update the server. " + e.Message);
                        return;
                    }
                    Debug.LogWarning(Tag + "cannot link to " + url + ": " + e.Message);
                }
                finally
                {
                    _connected = false;
                    try { connection.Dispose(); }
                    catch { }
                }

                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
            }
        }

        static async Task InvokeAsync(FmpConnection connection, MatrixPlayerTools tools, PlayerToolInvoke invoke)
        {
            var done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            MainThread.Enqueue(() =>
            {
                try { done.TrySetResult(tools.Run(invoke.ToolName, invoke.ArgumentsJsonUtf8)); }
                catch (Exception e) { done.TrySetException(e); }
            });

            var result = new PlayerToolResult { OperationId = invoke.OperationId };
            try
            {
                result.ContentJsonUtf8 = await done.Task.ConfigureAwait(false);
                result.Success = true;
            }
            catch (Exception e)
            {
                var code = e is ToolException known ? known.Code : "FMP_TOOL_FAILED";
                result.Error = new PlayerError { Code = code, Message = e.Message };
            }
            await connection.SendAsync("CompleteTool", PlayerJson.Arguments(result)).ConfigureAwait(false);
        }

        static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
        }
    }

    sealed class MatrixPlayerPump : MonoBehaviour
    {
        void Update() => MatrixPlayer.Drain();
    }
}
