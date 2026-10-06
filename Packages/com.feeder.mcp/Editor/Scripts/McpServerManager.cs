#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ReflectorNet.Utils;
using Feeder.MCP.Editor.UI;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Runtime.Utils;
using Feeder.MCP.Utils;
using Microsoft.Extensions.Logging;
using R3;
using UnityEditor;
using UnityEngine;
using McpConsts = Feeder.McpPlugin.Common.Consts;

namespace Feeder.MCP.Editor
{
    using static Feeder.McpPlugin.Common.Consts.MCP.Server;
    using Consts = Feeder.McpPlugin.Common.Consts;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using AiAgentConfig = Feeder.McpPlugin.AgentConfig.AiAgentConfig;

    public enum McpServerStatus
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        External,
        // The server binary is being installed from the package's bundled copy. Distinct from Starting
        // so the UI can show an honest "Installing server…" state instead of a misleading "Starting…"
        // while the process has not been launched yet.
        Installing
    }

    /// <summary>
    /// Manages the MCP server binary and process lifecycle independently from UI.
    /// Provides cross-platform support for Windows, macOS, and Linux.
    /// </summary>
    [InitializeOnLoad]
    public static class McpServerManager
    {
        // pre-0.88 plugins stored the pid here; EditorPrefs are shared by every project, so it is only ever deleted
        const string LegacyProcessIdKey = "Feeder_MCP_ServerManager_ProcessId";

        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger(typeof(McpServerManager));
        static readonly ReactiveProperty<McpServerStatus> _serverStatus = new(McpServerStatus.Stopped);
        // Last server-binary install failure reason, or null when there is no outstanding failure. The
        // editor window observes this to surface the error + an "Install / Retry server" button. Cleared
        // at the start of every install attempt and on a confirmed-current binary.
        static readonly ReactiveProperty<string?> _lastInstallError = new(null);
        static readonly object _processMutex = new();

        static Process? _serverProcess;
        static bool _stopRequested;
        static DateTime _serverStartedUtc;
        static bool _restartScheduled;
        static readonly ServerRestartPolicy _restartPolicy = new();
        static readonly ReactiveProperty<string?> _pendingNote = new(null);
        static readonly ReactiveProperty<string?> _serverFault = new(null);
        static readonly ReactiveProperty<string?> _portMoveNote = new(null);

        // Single-flight guard for the server-binary install (0 = idle, 1 = an InstallServerBinary is in
        // flight). Claimed atomically via Interlocked.CompareExchange in TryBeginInstall, released in
        // EndInstall from InstallServerBinary's finally — so two concurrent triggers (the
        // [InitializeOnLoad] static ctor plus a user-initiated menu/button path) can never run two
        // colliding installs against the same staging area.
        static int _installInProgress;

        public static ReadOnlyReactiveProperty<McpServerStatus> ServerStatus => _serverStatus;

        /// <summary>
        /// Last server-binary install failure reason (null when none). The Matrix Bridge window
        /// subscribes to surface the failure + a retry button instead of silently dead-ending.
        /// </summary>
        public static ReadOnlyReactiveProperty<string?> LastInstallError => _lastInstallError;

        /// <summary>Non-null while a server restart or a bridge reconnect is scheduled; the text explains what is pending.</summary>
        public static ReadOnlyReactiveProperty<string?> PendingNote => _pendingNote;

        /// <summary>Non-null when the server could not be kept alive (crash loop, unresolvable port conflict).</summary>
        public static ReadOnlyReactiveProperty<string?> ServerFault => _serverFault;

        /// <summary>Set once when the configured port was taken by someone else and the project moved to a free port.</summary>
        public static ReadOnlyReactiveProperty<string?> PortMoveNote => _portMoveNote;

        internal static void SetPendingNote(string? note) => MainThread.Instance.Run(() => _pendingNote.Value = note);

        internal static void SetServerFault(string? fault) => MainThread.Instance.Run(() => _serverFault.Value = fault);

        public static bool IsRunning => _serverStatus.CurrentValue == McpServerStatus.Running;
        public static bool IsStarting => _serverStatus.CurrentValue == McpServerStatus.Starting;

        /// <summary>
        /// True when a version-matching server binary is present on disk and can be launched without an
        /// install. The Start path (<c>HandleServerButton</c>) uses this to decide whether to recover a
        /// missing/outdated binary before launching.
        /// </summary>
        public static bool IsBinaryReadyToStart() => IsBinaryExists() && IsVersionMatches();

        static McpServerManager()
        {
            // an import worker that quits would otherwise stop the server it adopted from this project
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            // Register for editor quit to clean up the server process
            EditorApplication.quitting += OnEditorQuitting;

            RemoveLegacyLeftovers();

            if (MatrixActivation.IsEnabled)
                Activate();
        }

        /// <summary>
        /// Packages up to 0.87 installed the server as <c>gamedev-mcp-server</c>, and an interrupted install leaves a
        /// <c>mcp-server-staging-*</c> folder behind. Staging folders are always removed. The old exe is removed here
        /// only while Matrix is off; when Matrix is on, the install triggered by the missing <c>matrix-server</c> exe
        /// stops the old process (it runs from this project's Library) and replaces the whole folder.
        /// </summary>
        static void RemoveLegacyLeftovers()
        {
            try
            {
                var root = ExecutableFolderRootPath;
                var parent = Path.GetDirectoryName(root);
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                    return;

                foreach (var staging in Directory.GetDirectories(parent, Path.GetFileName(root) + "-staging-*"))
                {
                    try
                    {
                        Directory.Delete(staging, recursive: true);
                        _logger.LogInformation("Removed leftover MCP server staging folder: {path}", staging);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Could not remove staging folder {path}: {message}", staging, ex.Message);
                    }
                }

                var legacyExe = Path.Combine(ExecutableFolderPath, ServerOwnership.LegacyProcessName + Path.GetExtension(ExecutableFullName));
                if (MatrixActivation.IsEnabled || !File.Exists(legacyExe))
                    return;

                Directory.Delete(root, recursive: true);
                UnityEngine.Debug.Log($"Removed the old MCP server binary (gamedev-mcp-server) from {root}; matrix-server is installed when Matrix is turned on.");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not remove legacy MCP server files: {message}", ex.Message);
            }
        }

        internal static void Activate()
        {
            EditorPrefs.DeleteKey(LegacyProcessIdKey);

            // Check if server process is still running (e.g., after domain reload)
            EditorApplication.update += CheckExistingProcess;

            InstallServerBinaryIfNeeded(unattended: true)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted || !task.Result)
                        return; // Failed to install binaries, skip auto-start

                    if (EnvironmentUtils.IsCi())
                        return; // Skip auto-start in CI environment

                    EditorApplication.update += StartServerIfNeeded;
                });
        }

        #region Binary Metadata

        /// <summary>
        /// The PINNED version of the bundled server release. MUST always equal the content of the
        /// <c>version</c> file inside <c>Server~/&lt;rid&gt;/</c> in this package — the installer compares
        /// the two to decide whether the cached binary in <c>Library/mcp-server</c> is current. Bumping
        /// the server means replacing the whole <c>Server~/&lt;rid&gt;</c> payload AND this constant together.
        /// </summary>
        public const string ServerVersion = "0.2.1";

        public const string ExecutableName = ServerOwnership.ProcessName;
        public const string BundledServerArchiveName = "server-payload.zip";

        public static string McpServerName
            => string.IsNullOrEmpty(Application.productName)
                ? "Unity Unknown"
                : $"Unity {Application.productName}";

        public static string OperationSystem =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            "unknown";

        public static string CpuArch => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "unknown"
        };

        public static string PlatformName => $"{OperationSystem}-{CpuArch}";

        // Server executable file name
        // Sample (mac linux): matrix-server
        // Sample   (windows): matrix-server.exe
        public static string ExecutableFullName
            => ExecutableName.ToLowerInvariant() + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ".exe"
                : string.Empty);

        // Full path to the server executable
        // Sample (mac linux): ../Library/mcp-server
        // Sample   (windows): ../Library/mcp-server
        public static string ExecutableFolderRootPath
            => Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../Library",
                    "mcp-server"
                )
            );

        // Full path to the server executable
        // Sample (mac linux): ../Library/mcp-server/osx-x64
        // Sample   (windows): ../Library/mcp-server/win-x64
        public static string ExecutableFolderPath
            => Path.GetFullPath(
                Path.Combine(
                    ExecutableFolderRootPath,
                    PlatformName
                )
            );

        // Full path to the server executable
        // Sample (mac linux): ../Library/mcp-server/osx-x64/matrix-server
        // Sample   (windows): ../Library/mcp-server/win-x64/matrix-server.exe
        public static string ExecutableFullPath
            => Path.GetFullPath(
                Path.Combine(
                    ExecutableFolderPath,
                    ExecutableFullName
                )
            );

        public static string VersionFullPath
            => Path.GetFullPath(
                Path.Combine(
                    ExecutableFolderPath,
                    "version"
                )
            );

        /// <summary>
        /// The bundled server payload shipped inside this package at <c>Server~/&lt;rid&gt;/</c>.
        /// The trailing '~' keeps the folder invisible to Unity's importer (no .meta files, no import
        /// cost). The <c>version</c> file inside it must always equal <see cref="ServerVersion"/>.
        /// </summary>
        public static string BundledServerFolderPath
            => Path.GetFullPath(
                Path.Combine(
                    UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpServerManager).Assembly)?.resolvedPath
                        ?? Path.GetFullPath("Packages/com.feeder.mcp"),
                    "Server~",
                    PlatformName));

        /// <summary>
        /// Compressed server payload distributed with the package. Keeping the self-contained executable
        /// inside an ordinary ZIP lets Git-hosted Unity packages stay below GitHub's per-file size limit
        /// without requiring package consumers to install Git LFS or a separate .NET runtime.
        /// </summary>
        public static string BundledServerArchivePath
            => Path.Combine(BundledServerFolderPath, BundledServerArchiveName);

        #endregion // Binary Metadata

        #region Binary Lifecycle

        public static bool IsBinaryExists()
        {
            if (string.IsNullOrEmpty(ExecutableFullPath))
                return false;

            return File.Exists(ExecutableFullPath);
        }

        public static string? GetBinaryVersion()
        {
            if (!File.Exists(VersionFullPath))
                return null;

            return File.ReadAllText(VersionFullPath);
        }

        public static bool IsVersionMatches()
        {
            var binaryVersion = GetBinaryVersion();
            if (binaryVersion == null)
                return false;

            // Compared against the pinned server version, NOT the plugin version -
            // the cached binary is a server release with its own version number.
            return binaryVersion == ServerVersion;
        }

        /// <param name="interactive">
        /// When true (menu / user-initiated paths) a blocking <see cref="EditorUtility.DisplayDialog"/> asks the
        /// user to retry/skip if the folder can't be deleted (e.g. the server is still holding a file lock).
        /// When false (the unattended <c>[InitializeOnLoad]</c> / package-update download path) the blocking
        /// dialog is SKIPPED — after the silent retries the failure is rethrown so the caller surfaces it via the
        /// non-modal failure popup + retry button instead of freezing editor startup behind a modal.
        /// </param>
        public static bool DeleteBinaryFolderIfExists(bool interactive = true)
        {
            if (Directory.Exists(ExecutableFolderRootPath))
            {
                // Intentional infinite loop (interactive path only):
                // - Deletion can fail while the MCP server binaries are in use (e.g., server still running).
                // - On the first failure, we automatically attempt to stop the server process via McpServerManager.
                // - The retry/exit behavior is fully controlled by the user via the dialog below.
                // - We do not impose a fixed maximum retry count so the user can take as long as needed
                //   to shut down their MCP client and release file locks before trying again.
                // - The loop terminates when the user selects "Skip", at which point the exception is rethrown.
                // In the unattended path the blocking dialog is skipped: after the silent retries the exception
                // is rethrown so the download path fails-loud (non-modal popup) instead of blocking startup.
                var silentRetries = 0;
                while (true)
                {
                    try
                    {
                        Directory.Delete(ExecutableFolderRootPath, recursive: true);
                        UnityEngine.Debug.Log($"Deleted existing MCP server folder: <color=orange>{ExecutableFolderRootPath}</color>");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // First failure: try to stop the running server process that may be locking files
                        if (silentRetries == 0)
                        {
                            silentRetries++;
                            UnityEngine.Debug.Log($"Failed to delete MCP server folder. Attempting to stop the server process...");
                            try
                            {
                                StopServer(force: true);

                                // StopServer only knows the process THIS editor session tracks, and it
                                // returns true without killing anything when _serverProcess is null. That
                                // leaves two very common cases holding the file lock forever:
                                //  - an orphan kept alive by Keep Server Running across an editor restart/crash;
                                //  - a live process not re-attached yet, because CheckExistingProcess runs on
                                //    EditorApplication.update while this path runs earlier, from
                                //    OnAfterAssemblyReload -> ConnectIfNeeded -> InstallServerBinaryIfNeeded.
                                // Without the sweep below the delete never succeeds, so the install never
                                // completes and the plugin never connects — with no way to recover short of
                                // killing the process by hand.
                                var swept = KillProcessesLockingBinaryFolder();

                                if (swept > 0)
                                {
                                    UnityEngine.Debug.Log($"Stopped {swept} MCP server process(es) holding the binary. Retrying deletion...");
                                    Thread.Sleep(2000); // Wait a moment for the process to exit and release file locks
                                }
                                else
                                {
                                    UnityEngine.Debug.LogWarning($"No running MCP server process found to stop.");
                                }
                            }
                            catch (Exception stopEx)
                            {
                                UnityEngine.Debug.LogWarning($"Failed to stop MCP server: {stopEx.Message}");
                            }
                            continue; // Retry deletion after stopping the server
                        }

                        // Second failure: retry once more silently (OS may need time to release file locks)
                        if (silentRetries <= 1)
                        {
                            silentRetries++;
                            continue;
                        }

                        // Unattended path: never block startup behind a modal — rethrow so the caller
                        // surfaces the failure via the non-modal popup + retry button.
                        if (!interactive)
                        {
                            UnityEngine.Debug.LogError(
                                $"Failed to delete MCP server folder (unattended): {ex.Message}");
                            throw;
                        }

                        var retry = EditorUtility.DisplayDialog(
                            title: "Failed to Delete MCP Server Binaries",
                            message: $"The current Feeder MCP server binaries can't be deleted. " +
                                $"This is very likely because the MCP server is currently running.\n\n" +
                                $"Please close your MCP client to make sure the server is not running, then click \"Retry\".\n\n" +
                                $"Path: {ExecutableFolderRootPath}\n\n" +
                                $"Error: {ex.Message}",
                            ok: "Retry",
                            cancel: "Skip"
                        );

                        if (!retry)
                        {
                            throw;
                        }
                        // If retry is true, loop continues and tries again
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Force-kills every MCP server process running from THIS project's binary cache folder,
        /// tracked or not, and returns how many were killed.
        ///
        /// Matching is by executable path under <see cref="ExecutableFolderRootPath"/>, so a server
        /// belonging to another Unity project (its own Library/mcp-server) is never touched.
        /// </summary>
        static int KillProcessesLockingBinaryFolder()
        {
            var rootPath = ExecutableFolderRootPath;
            var killed = 0;
            _stopRequested = true;

            Process[] candidates;
            try
            {
                candidates = ServerOwnership.AllProcessNames.SelectMany(Process.GetProcessesByName).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not enumerate MCP server processes: {message}", ex.Message);
                return 0;
            }

            foreach (var process in candidates)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    // MainModule throws for processes owned by another user or already gone — those are
                    // not ours to kill anyway, so only a confirmed Ours is killed.
                    if (ServerOwnership.ClassifyProcess(process, rootPath) != ServerOwner.Ours)
                        continue;

                    _logger.LogInformation("Killing MCP server process holding the binary (PID: {pid})", process.Id);
                    process.Kill();
                    process.WaitForExit(5000);
                    killed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not kill MCP server process: {message}", ex.Message);
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (killed > 0)
            {
                // The tracked handle (if any) now points at a dead process, and the persisted PID would
                // make CheckExistingProcess re-attach to it after the next domain reload.
                // CleanupProcess drops both and moves the status machine to Stopped.
                CleanupProcess();
            }

            return killed;
        }

        /// <param name="unattended">
        /// When true (the <c>[InitializeOnLoad]</c> editor-startup path) the install runs without any
        /// blocking modal and without the result popup — failures are surfaced only through
        /// <see cref="LastInstallError"/> (the in-window error + retry button). When false (menu /
        /// Start button / retry button — user-initiated) the result popup is shown and the delete step
        /// may prompt interactively.
        /// </param>
        /// <param name="force">Reinstall from the bundled copy even when a version-matching binary is
        /// already present in the cache folder.</param>
        public static Task<bool> InstallServerBinaryIfNeeded(bool unattended = false, bool force = false)
        {
            if (EnvironmentUtils.IsCi())
            {
                // Ignore in CI environment
                UnityEngine.Debug.Log($"Ignore MCP server install in CI environment");
                return Task.FromResult(false);
            }

            // Cheap best-effort early-out: if an install already advanced the lifecycle machine to
            // Installing, skip re-entering InstallServerBinary at all. This is only an OPTIMIZATION —
            // the authoritative single-flight guard is InstallServerBinary's atomic TryBeginInstall.
            if (_serverStatus.CurrentValue == McpServerStatus.Installing)
                return Task.FromResult(true); // install already in progress; let it complete

            if (!force && IsBinaryExists() && IsVersionMatches())
            {
                // Binary is present and current — clear any stale failure so the window hides the error/retry UI.
                _lastInstallError.Value = null;
                return Task.FromResult(true);
            }

            UnityEngine.Debug.Log($"MCP server install needed: force={force}, exists={IsBinaryExists()}, " +
                $"versionRead='{GetBinaryVersion()}', expected='{ServerVersion}', path={ExecutableFullPath}");
            return Task.FromResult(InstallServerBinary(unattended));
        }

        /// <summary>
        /// Installs the bundled server payload (<see cref="BundledServerArchivePath"/>) into the per-RID
        /// cache folder (<see cref="ExecutableFolderPath"/>) ATOMICALLY: the payload is first extracted into a
        /// SAME-VOLUME staging folder (binary + sidecars + exec bit + version marker), and only then
        /// published with a single <see cref="Directory.Move"/> rename. The destination folder therefore
        /// never exists in a partial state: it is either absent or complete. The old working binary is left
        /// untouched until the replacement is fully staged.
        /// </summary>
        static bool InstallServerBinary(bool unattended = false)
        {
            // SINGLE-FLIGHT GUARD: atomically claim the one install slot so two concurrent triggers
            // (the [InitializeOnLoad] static ctor and a user-initiated menu/button path) never run
            // two colliding installs against the cache folder. Released in the finally.
            if (!TryBeginInstall())
                return true; // an install is already in progress; let it complete

            string? stagingRoot = null;
            try
            {
                // Clear any prior failure + reflect the in-progress install in the status machine.
                _lastInstallError.Value = null;
                SetInstallingStatus();

                var previousKeepServerRunning = UnityMcpPluginEditor.KeepServerRunning;

                var bundledBinary = Path.Combine(BundledServerFolderPath, ExecutableFullName);
                var hasArchive = File.Exists(BundledServerArchivePath);
                var hasUnpackedPayload = File.Exists(bundledBinary);
                if (!hasArchive && !hasUnpackedPayload)
                {
                    return FailInstall(
                        $"Bundled server payload missing. Expected '{BundledServerArchivePath}' " +
                        $"or development binary '{bundledBinary}'. Restore the 'Server~/{PlatformName}' " +
                        "folder inside the com.feeder.mcp package.", unattended);
                }

                var payloadSource = hasArchive ? BundledServerArchivePath : BundledServerFolderPath;
                UnityEngine.Debug.Log($"Installing MCP server binary from bundled payload: <color=yellow>{payloadSource}</color>");

                // Stage the fully-prepared payload as a SIBLING of the cache root (same volume) so the
                // final publish is an atomic rename, never a cross-volume copy interrupted mid-write.
                stagingRoot = Path.GetFullPath($"{ExecutableFolderRootPath}-staging-{Guid.NewGuid():N}");
                if (hasArchive)
                    ExtractServerArchive(BundledServerArchivePath, stagingRoot);
                else
                    CopyDirectory(BundledServerFolderPath, stagingRoot, excludeDirectoryName: "logs");

                var stagedBinary = Path.Combine(stagingRoot, ExecutableFullName);
                if (!File.Exists(stagedBinary))
                    throw new InvalidDataException(
                        $"Bundled server payload does not contain the expected executable '{ExecutableFullName}'.");

                // Set executable permission on macOS and Linux BEFORE publishing, so the published payload
                // is launch-ready the instant it appears under the cache folder.
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    UnixUtils.Set0755(stagedBinary);

                // The version marker travels with the payload, so the published per-RID folder is complete
                // the instant it appears — no window where the binary exists without its version file.
                File.WriteAllText(Path.Combine(stagingRoot, "version"), ServerVersion);

                // Stop the running server + remove the OLD cache folder. Only now do we touch the live
                // binary; everything above operated on staging. Unattended path never blocks behind a
                // modal (see DeleteBinaryFolderIfExists).
                DeleteBinaryFolderIfExists(interactive: !unattended);

                // Atomic publish: a single same-volume rename of the fully-prepared payload.
                PublishStagedBinary(stagingRoot, ExecutableFolderPath);
                stagingRoot = null; // ownership transferred to the cache folder

                var success = IsBinaryExists() && IsVersionMatches();
                if (!success)
                {
                    return FailInstall(
                        $"Server binary missing or version mismatch after install at: {ExecutableFullPath}", unattended);
                }

                UnityEngine.Debug.Log($"Installed MCP server binary to: <color=green>{ExecutableFullPath}</color>");

                if (previousKeepServerRunning)
                {
                    // StartServer() moves the status machine Installing -> Starting. If it early-returns
                    // false it never wrote Starting, so the status is still Installing — reset it to
                    // Stopped so the UI does not hang on "Installing server…".
                    if (!StartServer())
                    {
                        UnityEngine.Debug.LogError($"Failed to start MCP server after installing binary. Please try starting the server manually.");
                        ResetInstallingToStopped();
                    }
                }
                else
                {
                    ResetInstallingToStopped();
                }

                if (!unattended)
                    ShowUpdateResultPopup(success: true);

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                return FailInstall($"Failed to install server binary: {ex.Message}", unattended);
            }
            finally
            {
                // Release the single-flight install slot claimed at entry so a later retry can proceed.
                EndInstall();

                if (stagingRoot != null)
                {
                    try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); }
                    catch { /* best effort */ }
                }
            }
        }

        /// <summary>
        /// Recursively copies <paramref name="sourceDir"/> into <paramref name="destDir"/> (created when
        /// missing), optionally skipping any directory named <paramref name="excludeDirectoryName"/>
        /// (case-insensitive) at any depth — used to keep runtime 'logs' out of the staged payload.
        /// </summary>
        static void CopyDirectory(string sourceDir, string destDir, string? excludeDirectoryName = null)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var name = Path.GetFileName(dir);
                if (excludeDirectoryName != null && string.Equals(name, excludeDirectoryName, StringComparison.OrdinalIgnoreCase))
                    continue;
                CopyDirectory(dir, Path.Combine(destDir, name), excludeDirectoryName);
            }
        }

        /// <summary>
        /// Extracts the package-owned ZIP payload into an empty staging folder. Every entry is resolved and
        /// checked against the destination root before it is written, preventing a malformed archive from
        /// escaping the project's Library folder through absolute paths or ".." traversal.
        /// </summary>
        internal static void ExtractServerArchive(string archivePath, string destDir)
        {
            Directory.CreateDirectory(destDir);

            var destinationRoot = Path.GetFullPath(destDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            using var archiveStream = new FileStream(
                archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                if (!destinationPath.StartsWith(destinationRoot, pathComparison))
                    throw new InvalidDataException(
                        $"Server payload entry escapes the install folder: '{entry.FullName}'.");

                // ZIP directory entries have an empty Name. Create them explicitly; file entries create
                // their own parents so archives without explicit directory records work as well.
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                var parent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                using var source = entry.Open();
                using var destination = new FileStream(
                    destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
            }
        }

        /// <summary>
        /// Records an install failure: logs it, stores the reason in <see cref="LastInstallError"/> (so the
        /// window shows the error + retry button), returns the status machine to Stopped, and — for
        /// user-initiated (non-unattended) calls — shows the "Install Failed" popup. Always returns false so
        /// it can be used as the single return expression of every failure branch.
        /// </summary>
        static bool FailInstall(string reason, bool unattended)
        {
            UnityEngine.Debug.LogError($"MCP server binary install failed: {reason}");
            _lastInstallError.Value = reason;
            ResetInstallingToStopped();
            if (!unattended)
                ShowUpdateResultPopup(success: false);
            return false;
        }

        /// <summary>Moves the status machine into Installing from an idle state (Stopped/Installing only).</summary>
        static void SetInstallingStatus()
        {
            var current = _serverStatus.CurrentValue;
            if (current == McpServerStatus.Stopped || current == McpServerStatus.Installing)
                _serverStatus.Value = McpServerStatus.Installing;
        }

        /// <summary>Returns the status machine to Stopped, but ONLY if it is still Installing (so it never
        /// stomps a Starting/Running/Stopping state that a concurrent path may have moved it to).</summary>
        static void ResetInstallingToStopped()
        {
            if (_serverStatus.CurrentValue == McpServerStatus.Installing)
                _serverStatus.Value = McpServerStatus.Stopped;
        }

        /// <summary>
        /// Atomically claims the single server-binary install slot. Returns true when THIS caller acquired
        /// it (and must eventually release it via <see cref="EndInstall"/>), or false when an install is
        /// already in flight and the caller must no-op.
        /// </summary>
        internal static bool TryBeginInstall()
            => Interlocked.CompareExchange(ref _installInProgress, 1, 0) == 0;

        /// <summary>Releases the single install slot claimed by <see cref="TryBeginInstall"/>.</summary>
        internal static void EndInstall()
            => Interlocked.Exchange(ref _installInProgress, 0);

        /// <summary>True while a server-binary install slot is currently held (diagnostic / test view).</summary>
        internal static bool IsInstallInProgress
            => Volatile.Read(ref _installInProgress) != 0;

        /// <summary>Shows the non-modal server-binary install result popup (success or failure).</summary>
        static void ShowUpdateResultPopup(bool success)
        {
            NotificationPopupWindow.Show(
                windowTitle: success ? "Installed" : "Install Failed",
                height: 235,
                minHeight: 235,
                title: success ? "Server Binary Installed" : "Server Binary Install Failed",
                message: success
                    ? "The MCP server binary was installed from the package's bundled copy. \n\n" +
                        $"Version: {GetBinaryVersion()}\n\n" +
                        "You may need to restart your AI agent to reconnect to the updated server."
                    : "Failed to install the MCP server binary. Please check the logs for details.");
        }

        /// <summary>
        /// Atomically publishes a fully-prepared staged payload folder into <paramref name="destFolder"/> via a
        /// single same-volume <see cref="Directory.Move"/>. Ensures the destination's parent exists and removes
        /// any existing destination first (Directory.Move requires the target to not exist). The caller
        /// guarantees <paramref name="stagedFolder"/> and <paramref name="destFolder"/> share a volume (staging
        /// is a sibling of the cache root) so the rename is atomic, never a partial cross-volume copy.
        /// </summary>
        internal static void PublishStagedBinary(string stagedFolder, string destFolder)
        {
            var parent = Path.GetDirectoryName(destFolder);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            if (Directory.Exists(destFolder))
                Directory.Delete(destFolder, recursive: true);

            Directory.Move(stagedFolder, destFolder);
        }

        #endregion // Binary Lifecycle

        #region Client Configuration

        /// <summary>
        /// Generates a JSON configuration for stdio transport.
        /// <code>
        /// {
        ///   "mcpServers": {
        ///     "Unity ProjectName": {
        ///       "type": "...",    // optional, only if provided
        ///       "command": "path/to/feeder-mcp-server",
        ///       "args": ["port=...", "plugin-timeout=...", "client-transport=stdio" /*, "token=..." if auth required */]
        ///     }
        ///   }
        /// }
        /// </code>
        /// </summary>
        public static JsonNode RawJsonConfigurationStdio(
            int port,
            string bodyPath = "mcpServers",
            int timeoutMs = Consts.Hub.DefaultTimeoutMs,
            string? type = null)
        {
            var pathSegments = BodyPathSegments(bodyPath);

            // Build innermost content first
            var serverConfig = new JsonObject();

            if (type != null)
                serverConfig["type"] = type;

            serverConfig["command"] = ExecutableFullPath.Replace('\\', '/');

            var args = new JsonArray
            {
                $"{Args.Port}={port}",
                $"{Args.PluginTimeout}={timeoutMs}",
                $"{Args.ClientTransportMethod}={TransportMethod.stdio}",
                $"{Args.Authorization}={UnityMcpPluginEditor.AuthOption}"
            };

            var authRequired = UnityMcpPluginEditor.AuthOption == AuthOption.required;
            if (authRequired && !string.IsNullOrEmpty(UnityMcpPluginEditor.Token))
                args.Add($"{Args.Token}={UnityMcpPluginEditor.Token}");

            serverConfig["args"] = args;

            var innerContent = new JsonObject
            {
                ["Feeder-MCP"] = serverConfig
            };

            // Build nested structure from innermost to outermost
            var result = innerContent;
            for (int i = pathSegments.Length - 1; i >= 0; i--)
            {
                result = new JsonObject { [pathSegments[i]] = result };
            }

            return result;
        }

        /// <summary>
        /// Generates a JSON configuration for HTTP transport.
        /// <code>
        /// {
        ///   "mcpServers": {
        ///     "Unity ProjectName": {
        ///       "type": "...",  // optional, only if provided
        ///       "url": "http://localhost:port",
        ///      "headers": {     // only if token is provided
        ///        "Authorization": "Bearer token"
        ///      }
        ///     }
        ///   }
        /// }
        /// </code>
        /// </summary>
        public static JsonNode RawJsonConfigurationHttp(
            string url,
            string bodyPath = "mcpServers",
            string? type = null)
        {
            var pathSegments = BodyPathSegments(bodyPath);

            // Build innermost content first
            var serverConfig = new JsonObject();

            if (type != null)
                serverConfig["type"] = type;

            serverConfig["url"] = GetMcpEndpointUrl(url);

            var authRequired = UnityMcpPluginEditor.AuthOption == AuthOption.required;
            if (authRequired && !string.IsNullOrEmpty(UnityMcpPluginEditor.Token))
            {
                serverConfig["headers"] = new JsonObject
                {
                    ["Authorization"] = $"Bearer {UnityMcpPluginEditor.Token}"
                };
            }

            var innerContent = new JsonObject
            {
                ["Feeder-MCP"] = serverConfig
            };

            // Build nested structure from innermost to outermost
            var result = innerContent;
            for (int i = pathSegments.Length - 1; i >= 0; i--)
            {
                result = new JsonObject { [pathSegments[i]] = result };
            }

            return result;
        }

        #endregion // Client Configuration

        #region Process Lifecycle

        static void CheckExistingProcess()
        {
            EditorApplication.update -= CheckExistingProcess;
            if (_serverProcess != null)
                return;

            // The owner file lives in this project's Temp folder, so it can only ever name this project's server.
            // Start time guards against Windows reusing the pid; the path and port guard against everything else.
            var record = ServerOwnership.ReadRecord();
            if (record == null)
                return;

            Process? process = null;
            try
            {
                process = Process.GetProcessById(record.pid);
                var owner = ServerOwnership.ClassifyProcess(process, ExecutableFolderRootPath);
                if (ServerOwnership.CanAdopt(record, ServerOwnership.ReadStartTimeUtc(process), owner, UnityMcpPluginEditor.Port))
                {
                    Adopt(process);
                    _logger.LogInformation("Reconnected to existing MCP server process (PID: {pid})", record.pid);
                    return;
                }
                _logger.LogDebug("Owner file names PID {pid} but it is not this project's server ({owner}); ignoring it", record.pid, owner);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not reconnect to previous process: {message}", ex.Message);
            }

            process?.Dispose();
            ServerOwnership.DeleteRecord();
        }

        /// <summary>Takes over a live server process that is confirmed to be this project's own.</summary>
        static void Adopt(Process process)
        {
            lock (_processMutex)
            {
                _serverProcess = process;
                _stopRequested = false;
                _serverStartedUtc = ServerOwnership.ReadStartTimeUtc(process) ?? DateTime.UtcNow;
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
                WriteOwnerRecord(process);
                _serverStatus.Value = McpServerStatus.Running;
            }
            ScheduleStartupVerification(process.Id);
        }

        static void WriteOwnerRecord(Process process)
        {
            try
            {
                ServerOwnership.WriteRecord(new ServerOwnerRecord
                {
                    pid = process.Id,
                    startTimeUtcTicks = ServerOwnership.ReadStartTimeUtc(process)?.Ticks ?? 0,
                    port = UnityMcpPluginEditor.Port,
                    exePath = ServerOwnership.ReadExePath(process) ?? ExecutableFullPath,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not write the MCP server owner file: {message}", ex.Message);
            }
        }

        static void OnEditorQuitting()
        {
            StopServer(force: true);
        }

        public static bool StartServer()
        {
            lock (_processMutex)
            {
                if (_serverStatus.CurrentValue == McpServerStatus.Running ||
                    _serverStatus.CurrentValue == McpServerStatus.Starting ||
                    _serverStatus.CurrentValue == McpServerStatus.Stopping)
                {
                    _logger.LogWarning("MCP server is already {status}", _serverStatus.CurrentValue);
                    return false;
                }

                if (!IsBinaryExists())
                {
                    _logger.LogError("MCP server binary not found at: {path}", ExecutableFullPath);
                    return false;
                }

                _serverStatus.Value = McpServerStatus.Starting;
                _stopRequested = false;

                // Kill this project's own orphan on the port, or move to a free port when someone else holds it
                if (!ResolvePortOwner())
                {
                    _serverStatus.Value = McpServerStatus.Stopped;
                    return false;
                }

                try
                {
                    var executablePath = ExecutableFullPath;
                    var arguments = BuildArguments();

                    _logger.LogInformation("Starting MCP server: {path} {args}", executablePath, arguments);

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = ExecutableFolderPath
                    };
                    startInfo.EnvironmentVariables["FEEDER_MATRIX_FILE_LOG_ONLY"] = "1";

                    // Set executable permissions on Unix-like systems
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        UnixUtils.Set0755(executablePath);
                    }

                    _serverProcess = new Process
                    {
                        StartInfo = startInfo,
                        EnableRaisingEvents = true
                    };
                    _serverProcess.Exited += OnProcessExited;
                    _serverProcess.OutputDataReceived += OnOutputDataReceived;
                    _serverProcess.ErrorDataReceived += OnErrorDataReceived;

                    if (!_serverProcess.Start())
                    {
                        _logger.LogError("Failed to start MCP server process");
                        CleanupProcess();
                        return false;
                    }

                    _serverProcess.BeginOutputReadLine();
                    _serverProcess.BeginErrorReadLine();

                    // Save ownership for reconnection after domain reload
                    _serverStartedUtc = DateTime.UtcNow;
                    WriteOwnerRecord(_serverProcess);

                    // Keep status as Starting - it will be set to Running after verification
                    _logger.LogInformation("MCP server process started (PID: {pid}), awaiting verification...", _serverProcess.Id);

                    // Schedule a delayed check to verify the process is still running
                    // This catches early crashes that might not trigger the Exited event reliably
                    // Status will be set to Running only after successful verification
                    ScheduleStartupVerification(_serverProcess.Id);

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to start MCP server: {message}", ex.Message);
                    CleanupProcess();
                    return false;
                }
            }
        }

        /// <summary>
        /// Stops the MCP server process.
        /// By default, this method is non-blocking: it sends the kill/terminate signal
        /// and lets the Exited event handler perform cleanup asynchronously.
        /// When force is true (e.g., editor quitting), it blocks until the process exits.
        /// </summary>
        public static bool StopServer(bool force = false)
        {
            lock (_processMutex)
            {
                if (_serverStatus.CurrentValue == McpServerStatus.Stopped ||
                    _serverStatus.CurrentValue == McpServerStatus.Stopping)
                {
                    _logger.LogDebug("MCP server is already stopped or stopping");
                    return true;
                }

                _stopRequested = true;
                CancelPendingRestart();

                if (_serverProcess == null)
                {
                    _serverStatus.Value = McpServerStatus.Stopped;
                    ServerOwnership.DeleteRecord();
                    return true;
                }

                // Never signal a process that is not confirmed to run from this project's Library.
                var owner = ServerOwnership.ClassifyProcess(_serverProcess, ExecutableFolderRootPath);
                if (owner != ServerOwner.Ours)
                {
                    if (!HasExitedSafe(_serverProcess))
                        _logger.LogWarning("Not stopping PID {pid}: it is not this project's MCP server ({owner}). Releasing the handle only.",
                            SafeId(_serverProcess), owner);
                    CleanupProcess();
                    return true;
                }

                _serverStatus.Value = McpServerStatus.Stopping;

                try
                {
                    _logger.LogInformation("Stopping MCP server (PID: {pid})", _serverProcess.Id);

                    if (!_serverProcess.HasExited)
                    {
                        SendTerminateSignal();
                    }

                    if (force)
                    {
                        // Synchronous path: block until exit (used during editor quitting)
                        WaitForExitAndForceKillIfNeeded();
                        CleanupProcess();
                    }
                    else
                    {
                        if (_serverProcess.HasExited)
                        {
                            CleanupProcess();
                        }
                        else
                        {
                            // Non-blocking path: schedule background wait + force kill safety net.
                            // CleanupProcess will be called by OnProcessExited or the background task.
                            ScheduleForceKillIfNeeded();
                        }
                    }

                    _logger.LogInformation("MCP server stop initiated");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error stopping MCP server: {message}", ex.Message);
                    CleanupProcess();
                    return false;
                }
            }
        }

        /// <summary>
        /// Sends the platform-appropriate terminate signal without waiting for exit.
        /// </summary>
        static void SendTerminateSignal()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _serverProcess!.Kill();
            }
            else
            {
                // On Unix-like systems, send SIGTERM for graceful shutdown
                try
                {
                    using var killProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "kill",
                        Arguments = $"-TERM {_serverProcess!.Id}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    killProcess?.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("SIGTERM failed, falling back to Kill(): {message}", ex.Message);
                    _serverProcess!.Kill();
                }
            }
        }

        /// <summary>
        /// Blocking wait for process exit, with force-kill fallback.
        /// Used only during editor quitting to prevent orphaned processes.
        /// </summary>
        static void WaitForExitAndForceKillIfNeeded()
        {
            if (_serverProcess == null || _serverProcess.HasExited)
                return;

            if (!_serverProcess.WaitForExit(5000))
            {
                _logger.LogWarning("MCP server did not exit gracefully, forcing termination");
                try
                {
                    _serverProcess.Kill();
                    _serverProcess.WaitForExit(2000);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Force kill failed: {message}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Background safety net: waits for the process to exit and force-kills after timeout.
        /// Calls CleanupProcess on the main thread when done.
        /// </summary>
        static void ScheduleForceKillIfNeeded()
        {
            var process = _serverProcess;
            if (process == null)
                return;

            Task.Run(() =>
            {
                try
                {
                    if (!process.HasExited && !process.WaitForExit(5000))
                    {
                        _logger.LogWarning("MCP server did not exit gracefully, forcing termination");
                        try
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Force kill error: {message}", ex.Message);
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogDebug("Process already exited or disposed while waiting for exit: {message}", ex.Message);
                }

                // Ensure cleanup on the main thread.
                // Safe to call even if OnProcessExited already triggered cleanup.
                MainThread.Instance.Run(CleanupProcess);
            });
        }

        /// <summary>
        /// Makes sure the configured port is free for this project's server. A leftover server of THIS project
        /// (exe under our Library) is killed. Anything else on the port - another project's server, a foreign
        /// process, or a process we cannot inspect - is never touched: the project moves to a free port instead.
        /// Returns false only when no free port could be found.
        /// </summary>
        static bool ResolvePortOwner()
        {
            var port = UnityMcpPluginEditor.Port;
            if (!ServerOwnership.IsPortListening(port))
                return true;

            var listeningPid = GetPidListeningOnPort(port);
            var currentPid = _serverProcess != null ? SafeId(_serverProcess) : -1;
            if (listeningPid > 0 && listeningPid == currentPid)
                return true;

            Process? process = null;
            try
            {
                if (listeningPid > 0)
                    process = Process.GetProcessById(listeningPid);
            }
            catch (Exception)
            {
                process = null;
            }

            using (process)
            {
                var owner = process != null ? ServerOwnership.ClassifyProcess(process, ExecutableFolderRootPath) : ServerOwner.Unknown;
                if (owner == ServerOwner.Ours)
                {
                    try
                    {
                        _logger.LogWarning("Killing orphaned MCP server of this project (PID: {pid}) occupying port {port}", listeningPid, port);
                        process!.Kill();
                        if (!process.WaitForExit(3000))
                            _logger.LogWarning("Orphaned MCP server process (PID: {pid}) did not exit within 3 seconds after kill", listeningPid);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Orphaned MCP server (PID: {pid}) could not be killed: {message}", listeningPid, ex.Message);
                        return true;
                    }
                }

                if (!ServerOwnership.IsPortListening(port))
                    return true;

                var holder = owner == ServerOwner.OtherProject
                    ? $"the MCP server of project '{ServerOwnership.DescribeOwner(process != null ? ServerOwnership.ReadExePath(process) : null)}'"
                    : owner == ServerOwner.NotMcp && process != null
                        ? $"process '{SafeName(process)}' (PID {listeningPid})"
                        : listeningPid > 0 ? $"PID {listeningPid}" : "another process";
                return MoveToFreePort(port, holder);
            }
        }

        static bool MoveToFreePort(int port, string holder)
        {
            var reserved = new System.Collections.Generic.HashSet<int>();
            try
            {
                foreach (var clone in Feeder.MCP.Editor.HyperTesting.HyperPoolState.Load().clones)
                    reserved.Add(clone.port);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read the hyper testing pool: {message}", ex.Message);
            }

            var newPort = ServerOwnership.PickFreePort(port, ServerOwnership.ListeningPorts(), reserved);
            if (newPort < 0)
            {
                var fault = $"Port {port} is used by {holder} and no free port was found in {ServerOwnership.MinAutoPort}-{ServerOwnership.MaxAutoPort}.";
                _logger.LogError(fault);
                _serverFault.Value = fault;
                return false;
            }

            UnityMcpPluginEditor.LocalHost = $"http://localhost:{newPort}";
            UnityMcpPluginEditor.Instance.Save();
            Startup.ConfigureAgents();
            var note = $"port {port} is used by {holder}, moved to {newPort} - restart your agent session";
            UnityEngine.Debug.LogWarning($"[Matrix Bridge] {note}");
            _portMoveNote.Value = note;
            return true;
        }

        static int SafeId(Process process)
        {
            try { return process.Id; }
            catch (Exception) { return -1; }
        }

        static string SafeName(Process process)
        {
            try { return process.ProcessName; }
            catch (Exception) { return "unknown"; }
        }

        static bool HasExitedSafe(Process process)
        {
            try { return process.HasExited; }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Returns the PID of the process listening on the specified TCP port,
        /// or -1 if no process is found or the lookup fails.
        /// </summary>
        static int GetPidListeningOnPort(int port)
        {
            try
            {
                var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = "-ano -p tcp",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                    : new ProcessStartInfo
                    {
                        FileName = "lsof",
                        Arguments = $"-ti tcp:{port} -sTCP:LISTEN",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                using var process = Process.Start(startInfo);
                if (process == null) return -1;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var portSuffix = $":{port}";
                    foreach (var line in output.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (!trimmed.Contains("LISTENING"))
                            continue;

                        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 5)
                            continue;

                        var localAddress = parts[1];
                        if (localAddress.EndsWith(portSuffix) && int.TryParse(parts[parts.Length - 1], out var pid))
                            return pid;
                    }
                }
                else
                {
                    var trimmed = output.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        return -1;

                    var firstLine = trimmed.Split('\n')[0].Trim();
                    if (int.TryParse(firstLine, out var pid))
                        return pid;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to determine PID listening on port {port}: {message}", port, ex.Message);
            }

            return -1;
        }

        static string BuildArguments()
        {
            var port = UnityMcpPluginEditor.Port;
            var timeout = UnityMcpPluginEditor.TimeoutMs;
            var token = UnityMcpPluginEditor.Token;
            var authOption = UnityMcpPluginEditor.AuthOption;

            var args =
                $"--FeederMatrix:Port={port} " +
                $"--FeederMatrix:DefaultToolTimeout={TimeSpan.FromMilliseconds(timeout):c} " +
                $"--FeederMatrix:RequireAuth={authOption == AuthOption.required}";

            if (authOption == AuthOption.required && !string.IsNullOrEmpty(token))
                args += $" --FeederMatrix:Token={token}";

            return args;
        }

        /// <summary>Normalizes a local matrix URL to the MCP Streamable HTTP endpoint.</summary>
        public static string GetMcpEndpointUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            var normalized = url.TrimEnd('/');
            return normalized.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : normalized + "/mcp";
        }

        /// <summary>
        /// Schedules a verification check 5 seconds after startup to detect early crashes.
        /// If the process is still running after verification, the status is set to Running.
        /// If the process has exited and no longer exists, the status is set to Stopped.
        /// </summary>
        static void ScheduleStartupVerification(int processId)
        {
            var startTime = DateTime.UtcNow;
            const double verificationDelaySeconds = 5.0;

            void CheckProcess()
            {
                // If status is no longer Starting (e.g., OnProcessExited already cleaned up), unsubscribe
                if (_serverStatus.CurrentValue != McpServerStatus.Starting)
                {
                    EditorApplication.update -= CheckProcess;
                    return;
                }

                var elapsed = DateTime.UtcNow - startTime;

                // If we haven't reached verification delay yet, wait for next frame
                if (elapsed.TotalSeconds < verificationDelaySeconds)
                    return;

                // Detect early process exit before the verification delay
                // This catches crashes that happen within the first few seconds (e.g., port already in use)
                if (!IsProcessRunning(processId))
                {
                    _logger.LogError("MCP server process (PID: {pid}) exited early within {seconds:F1} seconds after launch",
                        processId, elapsed.TotalSeconds);

                    EditorApplication.update -= CheckProcess;
                    if (_serverStatus.CurrentValue == McpServerStatus.Starting)
                    {
                        CleanupProcess();
                        HandleUnexpectedExit();
                    }
                    return;
                }

                // Process is still running after the verification delay - mark as Running
                _logger.LogDebug("MCP server process (PID: {pid}) is still running after {seconds:F1}s verification",
                    processId, elapsed.TotalSeconds);

                EditorApplication.update -= CheckProcess;
                if (_serverStatus.CurrentValue == McpServerStatus.Starting)
                {
                    _serverStatus.Value = McpServerStatus.Running;
                    _logger.LogInformation("MCP server verified and running (PID: {pid})", processId);
                }
            }

            EditorApplication.update += CheckProcess;
        }

        /// <summary>
        /// Checks if a process with the given ID is still running and is the MCP server.
        /// </summary>
        static bool IsProcessRunning(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process == null || process.HasExited)
                    return false;

                var processName = process.ProcessName.ToLowerInvariant();
                return ServerOwnership.AllProcessNames.Any(processName.Contains);
            }
            catch (ArgumentException)
            {
                // Process with this ID does not exist
                return false;
            }
            catch (InvalidOperationException)
            {
                // Process has exited
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error checking process status: {message}", ex.Message);
                return false;
            }
        }

        static void OnProcessExited(object? sender, EventArgs e)
        {
            _logger.LogInformation("MCP server process exited");
            // Marshal to main thread since this event is raised from a thread pool thread
            // and CleanupProcess modifies reactive properties that may be observed on the main thread
            MainThread.Instance.Run(() =>
            {
                // a stale handle (already replaced or released) must not tear down the current server's state
                if (_serverProcess != null && !ReferenceEquals(sender, _serverProcess))
                    return;
                var current = _serverProcess != null;
                CleanupProcess();
                if (current)
                    HandleUnexpectedExit();
            });
        }

        /// <summary>
        /// Brings the server back after it died on its own (Keep Server Running on), backing off 1/2/5/10/30 s.
        /// Gives up after <see cref="ServerRestartPolicy.MaxAttempts"/> crashes inside five minutes and raises
        /// <see cref="ServerFault"/> instead of looping forever.
        /// </summary>
        static void HandleUnexpectedExit()
        {
            if (_stopRequested || _restartScheduled)
                return;
            if (!UnityMcpPluginEditor.KeepServerRunning || !MatrixActivation.IsEnabled || EnvironmentUtils.IsCi())
                return;

            var lifetime = DateTime.UtcNow - _serverStartedUtc;
            var delay = _restartPolicy.NextDelay(DateTime.UtcNow, lifetime);
            if (delay == null)
            {
                var fault = $"MCP server keeps crashing ({ServerRestartPolicy.MaxAttempts} restarts in 5 min) - see logs, then Retry.";
                _logger.LogError(fault);
                _pendingNote.Value = null;
                _serverFault.Value = fault;
                return;
            }

            _restartScheduled = true;
            var attempt = _restartPolicy.Attempts;
            _logger.LogWarning("MCP server exited unexpectedly; restart {attempt}/{max} in {delay}s",
                attempt, ServerRestartPolicy.MaxAttempts, delay.Value.TotalSeconds);
            _pendingNote.Value = $"server exited - restart {attempt}/{ServerRestartPolicy.MaxAttempts} in {delay.Value.TotalSeconds:0}s";
            var generation = ++_restartGeneration;
            Task.Delay(delay.Value).ContinueWith(_ => MainThread.Instance.Run(() =>
            {
                if (generation != _restartGeneration)
                    return;
                _restartScheduled = false;
                _pendingNote.Value = null;
                if (_stopRequested || IsInstallInProgress || _serverStatus.CurrentValue != McpServerStatus.Stopped)
                    return;
                StartOwnServerAsync().ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result)
                        _ = UnityMcpPluginEditor.ConnectIfNeeded();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }));
        }

        static int _restartGeneration;

        static void CancelPendingRestart()
        {
            _restartGeneration++;
            _restartScheduled = false;
            _pendingNote.Value = null;
        }

        /// <summary>Clears a crash-loop / unreachable / port fault so the next connect starts over (the Bridge Retry action).</summary>
        public static void ResetFault()
        {
            _serverFault.Value = null;
            _restartPolicy.Reset();
            CancelPendingRestart();
        }

        static void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogDebug("[MCP Server] {output}", e.Data);
            }
        }

        static void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogWarning("[MCP Server Error] {error}", e.Data);
            }
        }

        static void CleanupProcess()
        {
            _logger.LogDebug("Cleaning up MCP server process resources");
            lock (_processMutex)
            {
                var processToDispose = _serverProcess;
                _serverProcess = null;

                if (processToDispose != null)
                {
                    processToDispose.Exited -= OnProcessExited;
                    processToDispose.OutputDataReceived -= OnOutputDataReceived;
                    processToDispose.ErrorDataReceived -= OnErrorDataReceived;

                    // Dispose on a background thread to prevent deadlock.
                    // Process.Dispose() can hang on the main thread when redirected
                    // stdout/stderr streams are active, even after CancelOutputRead/CancelErrorRead.
                    Task.Run(() =>
                    {
                        try
                        {
                            try { processToDispose.CancelOutputRead(); } catch { }
                            try { processToDispose.CancelErrorRead(); } catch { }
                            processToDispose.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Error disposing MCP server process: {message}", ex.Message);
                        }
                    });
                }

                ServerOwnership.DeleteRecord();
                _serverStatus.Value = McpServerStatus.Stopped;
            }
        }

        /// <summary>
        /// Starts the MCP server if KeepServerRunning is enabled.
        /// This method is called during Unity Editor startup to auto-start the server based on user preference.
        /// A server already listening on the port is adopted only when it runs from this project's Library.
        /// </summary>
        public static void StartServerIfNeeded()
        {
            EditorApplication.update -= StartServerIfNeeded;

            // Check if user wants the server to keep running
            if (!UnityMcpPluginEditor.KeepServerRunning)
            {
                _logger.LogDebug("StartServerIfNeeded: KeepServerRunning is false, skipping auto-start");
                return;
            }

            _ = StartOwnServerAsync();
        }

        /// <summary>
        /// Guarantees that the server on <see cref="UnityMcpPluginEditor.Port"/> is THIS project's server:
        /// keeps a live tracked one, adopts an untracked one running from our Library, otherwise starts a new
        /// one (moving to a free port when another project or process holds ours). Must be called on the main
        /// thread; the netstat lookup runs on a worker thread. Returns false when no server could be started.
        /// </summary>
        public static async Task<bool> StartOwnServerAsync()
        {
            // a raised fault (crash loop, no free port, unreachable) holds every automatic start until Retry
            if (_serverFault.Value != null)
                return false;

            var status = _serverStatus.CurrentValue;
            if (status == McpServerStatus.Starting || status == McpServerStatus.Installing || status == McpServerStatus.Stopping)
                return true;

            var tracked = _serverProcess;
            if (status == McpServerStatus.Running && tracked != null)
            {
                if (!HasExitedSafe(tracked) && ServerOwnership.ClassifyProcess(tracked, ExecutableFolderRootPath) == ServerOwner.Ours)
                    return true;
                _logger.LogWarning("Tracked MCP server handle is stale or not this project's; releasing it");
                CleanupProcess();
            }
            else if (status == McpServerStatus.Running)
            {
                CleanupProcess();
            }

            var port = UnityMcpPluginEditor.Port;
            var root = ExecutableFolderRootPath;
            var found = await Task.Run(() =>
            {
                if (!ServerOwnership.IsPortListening(port))
                    return (pid: -1, owner: ServerOwner.Unknown);
                var pid = GetPidListeningOnPort(port);
                return (pid, owner: ServerOwnership.ClassifyProcess(pid, root));
            });

            if (_serverProcess != null || _serverStatus.CurrentValue != McpServerStatus.Stopped)
                return true;

            if (found.owner == ServerOwner.Ours)
            {
                try
                {
                    Adopt(Process.GetProcessById(found.pid));
                    _logger.LogInformation("Adopted this project's MCP server already listening on port {port} (PID: {pid})", port, found.pid);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Could not adopt PID {pid}: {message}", found.pid, ex.Message);
                }
            }

            if (!IsBinaryExists())
                return false;
            _logger.LogInformation("Starting this project's MCP server on port {port}", port);
            return StartServer();
        }

        #endregion // Process Lifecycle
    }
}
