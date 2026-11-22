#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Feeder.Json;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor
{
    /// <summary>
    /// Command-line entry for batch mode and CI: installs and starts this project's Matrix server, links the Editor to it,
    /// reports where to connect, and keeps the Editor alive while MCP clients call tools.
    /// <code>
    /// Unity -batchmode -nographics -projectPath P -executeMethod Feeder.MCP.Editor.MatrixBatch.Run
    ///       [-matrixIdleSeconds 900] [-matrixMaxSeconds 0] [-matrixReadyTimeoutSeconds 180]
    ///       [-matrixQuitFile P/Temp/matrix-batch.quit] [-matrixReadyFile P/Temp/matrix-batch.ready.json]
    /// </code>
    /// Do not pass <c>-quit</c>: Unity would leave as soon as <see cref="Run"/> returns. The Editor exits when no tool has been
    /// called for <c>-matrixIdleSeconds</c> (0 = never), after <c>-matrixMaxSeconds</c> in total (0 = no limit), when the quit file
    /// appears, or with exit code 2 when the server is not ready within <c>-matrixReadyTimeoutSeconds</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class MatrixBatch
    {
        const string ActiveKey = "Feeder.Matrix.Batch.Active";
        const string EnabledByBatchKey = "Feeder.Matrix.Batch.EnabledByBatch";
        const string ReadyKey = "Feeder.Matrix.Batch.Ready";
        const string StartedKey = "Feeder.Matrix.Batch.StartedTicks";
        const string IdleKey = "Feeder.Matrix.Batch.IdleSeconds";
        const string MaxKey = "Feeder.Matrix.Batch.MaxSeconds";
        const string ReadyTimeoutKey = "Feeder.Matrix.Batch.ReadyTimeoutSeconds";
        const string QuitFileKey = "Feeder.Matrix.Batch.QuitFile";
        const string ReadyFileKey = "Feeder.Matrix.Batch.ReadyFile";
        const string Tag = "[MatrixBatch] ";

        static long _lastActivityTicks = DateTime.UtcNow.Ticks;
        static long _readyTicks;

        // a domain reload (scripts compiled during the run) drops statics and event hooks: pick the run up again
        static MatrixBatch()
        {
            if (SessionState.GetBool(ActiveKey, false))
                EditorApplication.update += Tick;
        }

        /// <summary>The entry point for <c>-executeMethod</c>.</summary>
        public static void Run()
        {
            if (SessionState.GetBool(ActiveKey, false))
                return;

            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-quit") >= 0)
                Debug.LogWarning(Tag + "-quit makes Unity exit when this method returns; leave it out to keep the Editor serving tools.");

            var projectRoot = MatrixPluginEditor.ProjectRootPath;
            var temp = Path.Combine(projectRoot, "Temp");
            var quitFile = Option(args, "-matrixQuitFile") ?? Path.Combine(temp, "matrix-batch.quit");
            var readyFile = Option(args, "-matrixReadyFile") ?? Path.Combine(temp, "matrix-batch.ready.json");
            Directory.CreateDirectory(temp);
            foreach (var stale in new[] { quitFile, readyFile })
                if (File.Exists(stale))
                    File.Delete(stale);

            var wasCi = EnvironmentUtils.IsCi();
            // the run asked for Matrix: from here on a CI environment no longer switches install, server start and linking off
            Environment.SetEnvironmentVariable(EnvironmentUtils.BatchEntryVariable, "1");

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ReadyKey, false);
            SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            SessionState.SetInt(IdleKey, IntOption(args, "-matrixIdleSeconds", 900));
            SessionState.SetInt(MaxKey, IntOption(args, "-matrixMaxSeconds", 0));
            SessionState.SetInt(ReadyTimeoutKey, IntOption(args, "-matrixReadyTimeoutSeconds", 180));
            SessionState.SetString(QuitFileKey, quitFile);
            SessionState.SetString(ReadyFileKey, readyFile);

            var wasEnabled = MatrixActivation.IsEnabled;
            SessionState.SetBool(EnabledByBatchKey, !wasEnabled);
            if (!wasEnabled)
            {
                MatrixActivation.Enable();
            }
            else if (wasCi)
            {
                // the editor's own startup ran while CI still held everything off
                MatrixPluginEditor.KeepConnected = true;
                MatrixPluginEditor.KeepServerRunning = true;
                McpServerManager.Activate();
                Startup.Activate();
                _ = MatrixPluginEditor.ConnectIfNeeded();
            }

            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log(Tag + "starting Matrix for " + projectRoot);
        }

        // called for every tool call the Editor serves; keeps the idle clock honest
        internal static void NoteToolCall() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

        static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                EditorApplication.update -= Tick;
                return;
            }

            var now = DateTime.UtcNow;
            var started = new DateTime(long.Parse(SessionState.GetString(StartedKey, "0"), CultureInfo.InvariantCulture), DateTimeKind.Utc);
            var quitFile = SessionState.GetString(QuitFileKey, string.Empty);
            if (quitFile.Length > 0 && File.Exists(quitFile))
            {
                Quit(0, "quit file found");
                return;
            }

            var maxSeconds = SessionState.GetInt(MaxKey, 0);
            if (maxSeconds > 0 && now - started > TimeSpan.FromSeconds(maxSeconds))
            {
                Quit(0, "maximum run time reached");
                return;
            }

            if (!SessionState.GetBool(ReadyKey, false))
            {
                if (MatrixPluginEditor.IsConnected.CurrentValue && McpServerManager.IsRunning)
                {
                    SessionState.SetBool(ReadyKey, true);
                    _readyTicks = now.Ticks;
                    Interlocked.Exchange(ref _lastActivityTicks, now.Ticks);
                    WriteReadyFile();
                    Debug.Log(Tag + "ready: " + McpUrl());
                }
                else if (now - started > TimeSpan.FromSeconds(SessionState.GetInt(ReadyTimeoutKey, 180)))
                {
                    Quit(2, "the Matrix server is not ready (server " + McpServerManager.ServerStatus.CurrentValue + ", Editor link "
                            + MatrixPluginEditor.ConnectionState.CurrentValue + ")");
                }
                return;
            }

            var idleSeconds = SessionState.GetInt(IdleKey, 900);
            var last = new DateTime(Math.Max(Interlocked.Read(ref _lastActivityTicks), _readyTicks), DateTimeKind.Utc);
            if (idleSeconds > 0 && now - last > TimeSpan.FromSeconds(idleSeconds))
                Quit(0, "no tool call for " + idleSeconds + " s");
        }

        static void Quit(int exitCode, string reason)
        {
            Debug.Log(Tag + "exiting (" + exitCode + "): " + reason);
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            var readyFile = SessionState.GetString(ReadyFileKey, string.Empty);
            try
            {
                if (readyFile.Length > 0 && File.Exists(readyFile))
                    File.Delete(readyFile);
            }
            catch (IOException) { }

            // leave a machine that had Matrix off the way it was
            if (SessionState.GetBool(EnabledByBatchKey, false))
                MatrixActivation.Disable();
            EditorApplication.Exit(exitCode);
        }

        static string McpUrl() => "http://127.0.0.1:" + MatrixPluginEditor.Port + "/mcp";

        static void WriteReadyFile()
        {
            var path = SessionState.GetString(ReadyFileKey, string.Empty);
            if (path.Length == 0)
                return;
            var withToken = MatrixPluginEditor.AuthOption == Feeder.ToolHost.Common.Consts.MCP.Server.AuthOption.required;
            var json = "{\"state\":\"ready\",\"mcpUrl\":" + Quote(McpUrl()) +
                       ",\"port\":" + MatrixPluginEditor.Port.ToString(CultureInfo.InvariantCulture) +
                       ",\"projectRoot\":" + Quote(MatrixPluginEditor.ProjectRootPath) +
                       ",\"processId\":" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                       (withToken ? ",\"token\":" + Quote(MatrixPluginEditor.Token ?? string.Empty) : string.Empty) + "}";
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        static string Quote(string value) => JsonSerializer.Serialize(value, typeof(string));

        static string? Option(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        static int IntOption(string[] args, string name, int fallback)
            => int.TryParse(Option(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : fallback;
    }
}
