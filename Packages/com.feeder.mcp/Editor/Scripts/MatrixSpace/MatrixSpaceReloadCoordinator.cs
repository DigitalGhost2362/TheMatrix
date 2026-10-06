#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>
    /// Keeps the current editor domain alive while Matrix Space CLI turns are running.
    /// Script compilation is allowed to finish, but the resulting assemblies are not loaded
    /// until the last active turn has drained its process output and released its lease.
    /// </summary>
    [InitializeOnLoad]
    internal static class MatrixSpaceReloadCoordinator
    {
        private static readonly HashSet<long> ActiveLeases = new();
        private static long _nextLeaseId;
        private static bool _assembliesLocked;
        private static bool _compilationHadErrors;
        private static bool _reloadPending;
        private static int _lockGeneration;

        public static event Action<int, bool>? StateChanged;

        public static int ActiveTurnCount => ActiveLeases.Count;
        public static bool ReloadPending => _reloadPending;
        internal static bool IsDeferringCompilationCompletion => _assembliesLocked && ActiveLeases.Count > 0;
        internal static bool IsAssemblyReloadLockedByMatrix => _assembliesLocked;
        internal static int LockGeneration => _lockGeneration;

        private static bool Quiet => ActiveLeases.Count == 0 && !MatrixActivation.IsInUse;

        static MatrixSpaceReloadCoordinator()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
        }

        public static bool CanStartTurn(out string reason)
        {
            if (EditorApplication.isCompiling)
            {
                reason = "Unity is compiling scripts. Wait for compilation to finish before starting another turn.";
                return false;
            }

            if (_reloadPending)
            {
                reason = "A script reload is pending. Wait for the running agents to finish and Unity to reload.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static ReloadLease Acquire(string paneId)
        {
            if (!CanStartTurn(out var reason))
                throw new InvalidOperationException(reason);

            var leaseId = ++_nextLeaseId;
            ActiveLeases.Add(leaseId);

            if (!_assembliesLocked)
            {
                EditorApplication.LockReloadAssemblies();
                _assembliesLocked = true;
                _lockGeneration++;
                Debug.Log($"[MatrixSpaceReload] Assembly reload locked by {paneId}.");
            }

            RaiseStateChanged();
            return new ReloadLease(leaseId, paneId);
        }

        internal static void Release(long leaseId, string paneId)
        {
            if (!ActiveLeases.Remove(leaseId))
                return;

            Debug.Log($"[MatrixSpaceReload] Turn lease released by {paneId}; {ActiveLeases.Count} active.");
            RaiseStateChanged();

            if (ActiveLeases.Count == 0)
                UnlockWhenIdle();
        }

        private static void OnCompilationStarted(object context)
        {
            _compilationHadErrors = false;
            if (Quiet)
                return;

            Debug.Log($"[MatrixSpaceReload] Script compilation started; {ActiveLeases.Count} active turn(s).");
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    _compilationHadErrors = true;
                    break;
                }
            }
        }

        private static void OnCompilationFinished(object context)
        {
            _reloadPending = !_compilationHadErrors && ActiveLeases.Count > 0;
            if (Quiet)
                return;

            Debug.Log($"[MatrixSpaceReload] Script compilation finished; errors={_compilationHadErrors}, " +
                      $"reloadPending={_reloadPending}, activeTurns={ActiveLeases.Count}.");
            RaiseStateChanged();
        }

        private static void UnlockWhenIdle()
        {
            if (!_assembliesLocked || ActiveLeases.Count != 0)
                return;

            MatrixSpaceSessionStore.instance.PersistNow();

            // Clear managed state before Unity is allowed to synchronously tear the domain down.
            _assembliesLocked = false;
            _reloadPending = false;
            RaiseStateChanged();
            Debug.Log("[MatrixSpaceReload] All turns drained; assembly reload unlocked.");
            EditorApplication.UnlockReloadAssemblies();
        }

        private static void OnBeforeAssemblyReload()
        {
            if (Quiet)
                return;

            // A normal Matrix Space reload reaches this point only after all leases are gone.
            // Persist defensively in case Unity or another extension forced a reload.
            MatrixSpaceSessionStore.instance.PersistNow();
            if (ActiveLeases.Count > 0)
                Debug.LogWarning($"[MatrixSpaceReload] Forced reload with {ActiveLeases.Count} active turn(s); " +
                                 "their snapshots will be restored as interrupted.");
        }

        private static void OnEditorQuitting()
        {
            if (Quiet)
                return;

            MatrixSpaceSessionStore.instance.PersistNow();
            ActiveLeases.Clear();

            if (!_assembliesLocked)
                return;

            _assembliesLocked = false;
            _reloadPending = false;
            EditorApplication.UnlockReloadAssemblies();
        }

        private static void RaiseStateChanged()
            => StateChanged?.Invoke(ActiveLeases.Count, _reloadPending);
    }

    /// <summary>A one-shot ownership token for the global Matrix Space assembly reload lock.</summary>
    internal sealed class ReloadLease : IDisposable
    {
        private readonly long _leaseId;
        private readonly string _paneId;
        private bool _disposed;

        internal ReloadLease(long leaseId, string paneId)
        {
            _leaseId = leaseId;
            _paneId = paneId;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            MatrixSpaceReloadCoordinator.Release(_leaseId, _paneId);
        }
    }
}
