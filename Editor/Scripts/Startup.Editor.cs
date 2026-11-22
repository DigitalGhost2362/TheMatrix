#nullable enable
using Feeder.MCP.Editor.Activity;
using Feeder.MCP.Runtime.Utils;
using Feeder.Fmp;
using Feeder.Logging;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor
{
    public static partial class Startup
    {
        static void SubscribeOnEditorEvents()
        {
            // not Application.quitting: in the Editor it also fires on leaving Play mode, which tore the link
            // down with no reconnect path except EnteredEditMode, and any other package throwing there skips it
            EditorApplication.quitting += OnEditorQuitting;

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorEvents.OnAfterAssemblyReload(AfterDomainLoad);

            // Handle Play mode state changes to ensure reconnection after exiting Play mode
            EditorEvents.OnPlayModeChanged(OnPlayModeStateChanged);
        }
        static void OnEditorQuitting() => TryDisconnectAndCleanup(nameof(OnEditorQuitting));
        static void OnBeforeAssemblyReload()
        {
            try
            {
                if (MatrixPluginEditor.HasInstance)
                    MatrixPluginEditor.Instance.AnnounceReload();
                TryDisconnectAndCleanup(nameof(OnBeforeAssemblyReload), onlyIfConnected: true);
            }
            catch (System.Exception e)
            {
                // must not cut the reload chain for the packages after this one
                _logger.LogWarning(e, "{class} {method}: {message}", nameof(Startup), nameof(OnBeforeAssemblyReload), e.Message);
            }
        }

        /// <summary>
        /// Safely disconnects and cleans up the MCP plugin instance.
        /// Catches exceptions to prevent blocking Unity's shutdown/reload process.
        /// </summary>
        /// <param name="callerName">Name of the calling method for logging.</param>
        /// <param name="onlyIfConnected">If true, only disconnects when in Connected state.
        /// This prevents issues when cancelling in-progress connection attempts during assembly reload.
        /// Note: Log collector disposal always occurs regardless of this flag.</param>
        static void TryDisconnectAndCleanup(string callerName, bool onlyIfConnected = false)
        {
            if (!MatrixPluginEditor.HasInstance)
            {
                _logger.LogDebug("{class} {method} triggered: No MatrixPluginEditor instance to disconnect",
                    nameof(Startup), callerName);
                return;
            }

            _logger.LogInformation("{method} triggered", callerName);

            var plugin = MatrixPluginEditor.Instance;
            if (plugin.HasToolHostInstance)
            {
                var connectionState = MatrixPluginEditor.ConnectionState.CurrentValue;

                // When onlyIfConnected is true, skip disconnect unless we have an established connection.
                // This prevents hanging when cancelling in-progress connection attempts (Connecting/Reconnecting states).
                if (onlyIfConnected && connectionState != FmpState.Connected)
                {
                    _logger.LogTrace("Skipping {method} - not connected (state: {state})",
                        nameof(plugin.DisconnectImmediate), connectionState);
                }
                else
                {
                    try
                    {
                        plugin.DisconnectImmediate();
                    }
                    catch (System.Exception e)
                    {
                        _logger.LogWarning(e, "{class} {method}: Exception during disconnect (non-blocking): {message}",
                            nameof(Startup), callerName, e.Message);
                    }
                }
            }

            try
            {
                plugin.DisposeLogCollector();
            }
            catch (System.Exception e)
            {
                _logger.LogWarning(e, "{class} {method}: Exception during log collector disposal (non-blocking): {message}",
                    nameof(Startup), callerName, e.Message);
            }

            if (MatrixPluginRuntime.HasInstance)
            {
                try
                {
                    MatrixPluginRuntime.DisposeInstance();
                }
                catch (System.Exception e)
                {
                    _logger.LogWarning(e, "{class} {method}: Exception during runtime MCP plugin disposal (non-blocking): {message}",
                        nameof(Startup), callerName, e.Message);
                }
            }
        }
        static bool _afterDomainLoadRan;

        // afterAssemblyReload is skipped when a handler of another package throws before this one, so the first
        // editor tick of the domain calls this too
        static void AfterDomainLoad()
        {
            if (_afterDomainLoadRan)
                return;
            _afterDomainLoadRan = true;
            OnAfterAssemblyReload();
        }

        static void OnAfterAssemblyReload()
        {
            var isCi = EnvironmentUtils.IsCi();
            var keepConnected = MatrixPluginEditor.KeepConnected;
            var connectionAllowed = !isCi || keepConnected;

            _logger.LogInformation("{method} triggered - BuildAndStart with connectionAllowed: {connectionAllowed} (isCi: {isCi}, keepConnected: {keepConnected})",
                nameof(OnAfterAssemblyReload), connectionAllowed, isCi, keepConnected);

            MatrixPluginEditor.Instance.BuildToolHostIfNeeded();
            MatrixPluginEditor.Instance.AddUnityLogCollectorIfNeeded(() => new BufferedFileLogStorage());

            if (connectionAllowed)
                MatrixPluginEditor.ConnectIfNeeded();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!MatrixPluginEditor.HasInstance)
            {
                _logger.LogDebug("{class} {method} triggered: No MatrixPluginEditor instance available. State: {state}",
                    nameof(Startup), nameof(OnPlayModeStateChanged), state);
                return;
            }

            // Log Play mode state changes for debugging
            _logger.LogInformation("Play mode state changed: {state}", state);

            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    // Dispose the runtime plugin instance created by Initialize().Build().
                    // This handles the no-domain-reload case; TryDisconnectAndCleanup covers the domain-reload case.
                    _logger.LogTrace("Exiting Play mode - disposing runtime MCP plugin instance if present");
                    if (MatrixPluginRuntime.HasInstance)
                        MatrixPluginRuntime.DisposeInstance();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    ResubscribeLogCollector();
                    // Unity has returned to Edit mode - ensure connection is re-established
                    // if the configuration expects it to be connected
                    var isCi = EnvironmentUtils.IsCi();
                    var keepConnected = MatrixPluginEditor.KeepConnected;
                    _logger.LogTrace("Entered Edit mode - KeepConnected: {keepConnected}, IsCi: {isCi}",
                        keepConnected, isCi);

                    if (isCi && !keepConnected)
                    {
                        _logger.LogTrace("Skipping reconnection in CI environment (KeepConnected is false)");
                        break;
                    }

                    // the link survives leaving Play mode; this only restores one dropped during play
                    _logger.LogTrace("Ensuring connection after Play mode exit");

                    MatrixPluginEditor.Instance.BuildToolHostIfNeeded();
                    MatrixPluginEditor.Instance.AddUnityLogCollectorIfNeeded(() => new BufferedFileLogStorage());
                    MatrixPluginEditor.ConnectIfNeeded();
                    break;

                case PlayModeStateChange.ExitingEditMode:
                    _logger.LogTrace("Exiting Edit mode to enter Play mode");
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    _logger.LogTrace("Entered Play mode");
                    PlaySessionGuard.Arm(ResubscribeLogCollector);
                    break;
            }
        }

        // leaving play mode silences every log handler, the collector's included, until one subscribes again
        static void ResubscribeLogCollector()
        {
            if (MatrixPluginEditor.HasInstance)
                MatrixPluginEditor.Instance.LogCollector?.Resubscribe();
        }
    }
}
