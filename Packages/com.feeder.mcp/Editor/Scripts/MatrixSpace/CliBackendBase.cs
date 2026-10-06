#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>
    /// Shared plumbing for CLI-process agent backends: one short-lived process per turn,
    /// stdout parsed off-thread into <see cref="AgentEvent"/>s, queued, and drained on the
    /// main thread by an EditorApplication.update pump so listeners can touch UI Toolkit.
    /// All live processes are killed before a domain reload / editor quit.
    /// </summary>
    public abstract class CliBackendBase : IAgentBackend
    {
        private static readonly HashSet<Process> _liveProcesses = new();
        private static readonly object _liveProcessesLock = new();

        [InitializeOnLoadMethod]
        private static void RegisterDomainReloadKill()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            AssemblyReloadEvents.beforeAssemblyReload += KillAllLiveProcesses;
            EditorApplication.quitting += KillAllLiveProcesses;
        }

        private static void KillAllLiveProcesses()
        {
            Process[] toKill;
            lock (_liveProcessesLock)
            {
                toKill = new Process[_liveProcesses.Count];
                _liveProcesses.CopyTo(toKill);
                _liveProcesses.Clear();
            }

            foreach (var process in toKill)
                KillProcessTree(process);
        }

        protected static void KillProcessTree(Process process)
        {
            try
            {
                if (process.HasExited)
                    return;

                // Unity's .NET profile has no Process.Kill(entireProcessTree); shims spawn
                // node as a child, so taskkill /T is required to stop the real work.
                var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {process.Id} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                killer?.WaitForExit(3000);

                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception)
            {
                // Process may have exited between the check and the kill; nothing to do.
            }
        }

        private readonly ConcurrentQueue<AgentEvent> _pendingEvents = new();
        private readonly List<string> _stderrTail = new();
        private const int StderrTailMaxLines = 20;

        private Process? _process;
        private bool _disposed;

        public abstract string Name { get; }
        public bool IsRunning => _process != null;
        public string? SessionId { get; set; }
        public string? ModelOverride { get; set; }
        public string? ModeOverride { get; set; }
        public string? EffortOverride { get; set; }
        public string? AgentLabel { get; set; }

        public event Action<AgentEvent>? EventReceived;

        protected CliBackendBase()
        {
            EditorApplication.update += Pump;
        }

        public abstract void SendPrompt(string prompt);

        /// <summary>Parses one stdout line (called off the main thread; must be pure).</summary>
        protected abstract IEnumerable<AgentEvent> ParseStdoutLine(string line);

        /// <summary>Main-thread hook invoked for every event before listeners see it.</summary>
        protected virtual void OnMainThreadEvent(AgentEvent evt)
        {
        }

        /// <summary>Called for each stderr line (off the main thread; must be pure).</summary>
        protected virtual void OnStderrLineReceived(string line)
        {
        }

        protected void ThrowIfBusy()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
            if (IsRunning)
                throw new InvalidOperationException("A turn is already running. Cancel it first.");
        }

        protected void EnqueueEvent(AgentEvent evt) => _pendingEvents.Enqueue(evt);

        protected void EnqueueProcessError(string message)
            => _pendingEvents.Enqueue(new AgentEvent(AgentEventKind.ProcessError) { Text = message, IsError = true });

        protected string StderrTailText
        {
            get
            {
                lock (_stderrTail)
                    return string.Join("\n", _stderrTail);
            }
        }

        protected static string GetProjectRoot()
            => Path.GetDirectoryName(UnityEngine.Application.dataPath)!;

        /// <summary>
        /// Starts one turn's process. <paramref name="stdinText"/> (when non-null) is written
        /// to stdin, which is closed either way. Failures surface as ProcessError events.
        /// </summary>
        protected void StartTurnProcess(string fileName, string arguments, string? stdinText,
            IEnumerable<KeyValuePair<string, string>>? extraEnvironment = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = GetProjectRoot(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            // Applied before extraEnvironment so a user-provided PATH override still wins.
            CliEnvironment.Apply(startInfo);

            if (extraEnvironment != null)
            {
                foreach (var pair in extraEnvironment)
                {
                    if (pair.Key.Length > 0)
                        startInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += OnStdoutLine;
            process.ErrorDataReceived += OnStderrLine;

            lock (_stderrTail)
                _stderrTail.Clear();

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                process.Dispose();
                EnqueueProcessError($"Failed to start '{fileName}': {ex.Message}");
                return;
            }

            _process = process;
            lock (_liveProcessesLock)
                _liveProcesses.Add(process);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                var stdin = process.StandardInput;
                if (stdinText != null)
                    stdin.Write(stdinText);
                stdin.Close();
            }
            catch (Exception ex)
            {
                EnqueueProcessError($"Failed to send prompt to CLI stdin: {ex.Message}");
                Cancel();
                return;
            }

            // WaitForExit() (the blocking overload) flushes the async output events, so the
            // ProcessExited event is guaranteed to be queued after every stdout line.
            Task.Run(() =>
            {
                int exitCode;
                try
                {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
                catch (Exception)
                {
                    exitCode = -1;
                }

                _pendingEvents.Enqueue(new AgentEvent(AgentEventKind.ProcessExited) { ExitCode = exitCode });
            });
        }

        public void Cancel()
        {
            var process = _process;
            if (process == null)
                return;

            KillProcessTree(process);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            EditorApplication.update -= Pump;
            Cancel();
        }

        private void OnStdoutLine(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;

            foreach (var evt in ParseStdoutLine(e.Data))
                _pendingEvents.Enqueue(evt);
        }

        private void OnStderrLine(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            OnStderrLineReceived(e.Data);

            lock (_stderrTail)
            {
                _stderrTail.Add(e.Data);
                if (_stderrTail.Count > StderrTailMaxLines)
                    _stderrTail.RemoveAt(0);
            }
        }

        /// <summary>Main-thread drain of the event queue (EditorApplication.update).</summary>
        private void Pump()
        {
            while (_pendingEvents.TryDequeue(out var evt))
            {
                CoalesceQueuedDeltas(evt);

                if (evt.Kind == AgentEventKind.ProcessExited)
                    ReleaseProcess();

                OnMainThreadEvent(evt);
                EventReceived?.Invoke(evt);
            }
        }

        // one append per frame instead of one per token keeps the transcript's string growth linear
        private void CoalesceQueuedDeltas(AgentEvent evt)
        {
            if (!evt.IsDelta || evt.Kind is not (AgentEventKind.AssistantText or AgentEventKind.AssistantThinking))
                return;

            StringBuilder? merged = null;
            while (_pendingEvents.TryPeek(out var next) && next.IsDelta && next.Kind == evt.Kind &&
                   _pendingEvents.TryDequeue(out next))
            {
                merged ??= new StringBuilder(evt.Text);
                merged.Append(next.Text);
            }

            if (merged != null)
                evt.Text = merged.ToString();
        }

        private void ReleaseProcess()
        {
            var process = _process;
            _process = null;
            if (process == null)
                return;

            lock (_liveProcessesLock)
                _liveProcesses.Remove(process);
            process.Dispose();
        }
    }
}
