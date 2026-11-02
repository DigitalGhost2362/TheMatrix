#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Feeder.ToolHost.Common.Model;
using Feeder.MCP.Editor.Matrix;
using UnityEditor;
using UnityEditor.Compilation;

namespace Feeder.MCP.Editor.Utils
{
    [InitializeOnLoad]
    public static partial class ScriptUtils
    {
        private const string PendingNotificationKeysKey = "Feeder_MCP_PendingNotificationKeys";
        private const string NotificationDataSeparator = "<FEEDER_SCRIPT_SEP>";

        private static bool _processPendingScheduled = false;

        // Store compilation messages (thread-safe with lock)
        private static readonly List<CompilerMessage> _lastCompilationMessages = new List<CompilerMessage>();
        private static readonly object _compilationMessagesLock = new object();

        static ScriptUtils()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            // Process any pending notifications after domain reload (successful compilation)
            ScheduleProcessPendingNotifications();

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        // a successful compile is answered after the domain reload; this is the fallback when no reload follows
        private const double NoReloadGraceSeconds = 2;
        private static double _compilationFinishedAt;

        private static void OnCompilationStarted(object context)
        {
            lock (_compilationMessagesLock)
                _lastCompilationMessages.Clear();
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            lock (_compilationMessagesLock)
                _lastCompilationMessages.AddRange(messages);
        }

        private static void OnCompilationFinished(object context)
        {
            _compilationFinishedAt = EditorApplication.timeSinceStartup;
            ScheduleProcessPendingNotifications();
        }

        private static void ScheduleProcessPendingNotifications()
        {
            if (_processPendingScheduled)
                return;

            _processPendingScheduled = true;
            EditorApplication.update += ProcessPendingNotificationsOnce;
        }

        private static void ProcessPendingNotificationsOnce()
        {
            if (EditorApplication.isCompiling)
                return;
            if (_compilationFinishedAt > 0 && !HasCompilationErrors() &&
                EditorApplication.timeSinceStartup - _compilationFinishedAt < NoReloadGraceSeconds)
                return;

            EditorApplication.update -= ProcessPendingNotificationsOnce;
            _compilationFinishedAt = 0;

            if (!_processPendingScheduled)
                return;

            _processPendingScheduled = false;
            ProcessPendingNotifications();
        }

        /// <summary>
        /// Checks if the provided C# code has valid syntax.
        /// This method uses Roslyn to parse the code and check for syntax errors.
        /// </summary>
        /// <param name="code">
        /// <param name="errors"></param>
        /// <returns>True if the code has valid syntax; otherwise, false.</returns>
        public static bool IsValidCSharpSyntax(string code, out IEnumerable<string> errors)
        {
            string reason;
            try
            {
                var response = MatrixPluginEditor.Instance
                    .CompileOnServerAsync(new CompileRequest { Source = code, SyntaxOnly = true }, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(response.Error))
                {
                    errors = response.Diagnostics ?? Array.Empty<string>();
                    return response.Success;
                }
                reason = response.Error!;
            }
            catch (Exception e)
            {
                reason = e.Message;
            }

            UnityEngine.Debug.LogWarning($"C# syntax pre-check skipped, Unity will report real errors on compile: {reason}");
            errors = Array.Empty<string>();
            return true;
        }

        /// <summary>
        /// Schedules a notification to be sent after Unity compilation completes.
        /// Uses SessionState to persist across domain reloads.
        /// </summary>
        /// <param name="requestId">The request ID to track</param>
        /// <param name="filePath">The file path that was modified</param>
        /// <param name="operationType">The type of operation performed</param>
        public static void SchedulePostCompilationNotification(string requestId, string filePath, string operationType)
        {
            var notificationKey = $"Feeder_MCP_PendingNotification_{requestId}";
            var notificationData = $"{requestId}{NotificationDataSeparator}{filePath}{NotificationDataSeparator}{operationType}";

            // Store the notification data
            SessionState.SetString(notificationKey, notificationData);

            // Add to the key list
            var existingKeys = SessionState.GetString(PendingNotificationKeysKey, string.Empty);
            var keyList = string.IsNullOrEmpty(existingKeys)
                ? new List<string>()
                : existingKeys.Split(',').Where(k => !string.IsNullOrEmpty(k)).ToList();

            if (!keyList.Contains(notificationKey))
            {
                keyList.Add(notificationKey);
                SessionState.SetString(PendingNotificationKeysKey, string.Join(",", keyList));
            }
        }

        /// <summary>
        /// Called by InitializeOnLoad to process any pending notifications after compilation.
        /// </summary>
        public static void ProcessPendingNotifications()
        {
            // Get the list of pending notification keys from SessionState
            var pendingKeys = SessionState.GetString(PendingNotificationKeysKey, string.Empty);
            if (string.IsNullOrEmpty(pendingKeys))
                return;

            var keys = pendingKeys.Split(',').Where(k => !string.IsNullOrEmpty(k)).ToList();
            if (keys.Count == 0)
                return;

            var processedKeys = new List<string>();

            foreach (var key in keys)
            {
                var notificationData = SessionState.GetString(key, string.Empty);
                if (string.IsNullOrEmpty(notificationData))
                {
                    processedKeys.Add(key);
                    continue;
                }

                var parts = notificationData.Split(NotificationDataSeparator);
                if (parts.Length != 3)
                {
                    processedKeys.Add(key);
                    continue;
                }

                // Send notification and mark for cleanup
                NotifyCompleted(parts[0], parts[1], parts[2]);
                processedKeys.Add(key);
            }

            // Clean up processed keys
            foreach (var key in processedKeys)
                SessionState.EraseString(key);

            // Update the key list
            var remainingKeys = keys.Except(processedKeys).ToList();
            if (remainingKeys.Count > 0)
            {
                SessionState.SetString(PendingNotificationKeysKey, string.Join(",", remainingKeys));
            }
            else
            {
                SessionState.EraseString(PendingNotificationKeysKey);
            }
        }

        /// <summary>
        /// Answers a scheduled notification right away when the refresh that followed it started no compilation
        /// (unchanged content, a file outside any assembly), so the caller is not left waiting for a compile that never comes.
        /// </summary>
        public static void CompletePostCompilationNotificationIfIdle(string requestId)
        {
            if (EditorApplication.isCompiling)
                return;

            var notificationKey = $"Feeder_MCP_PendingNotification_{requestId}";
            var parts = SessionState.GetString(notificationKey, string.Empty).Split(NotificationDataSeparator);
            SessionState.EraseString(notificationKey);

            var remainingKeys = SessionState.GetString(PendingNotificationKeysKey, string.Empty)
                .Split(',')
                .Where(k => !string.IsNullOrEmpty(k) && k != notificationKey)
                .ToList();
            if (remainingKeys.Count > 0)
                SessionState.SetString(PendingNotificationKeysKey, string.Join(",", remainingKeys));
            else
                SessionState.EraseString(PendingNotificationKeysKey);

            if (parts.Length == 3)
                NotifyCompleted(parts[0], parts[1], parts[2]);
        }

        private static void NotifyCompleted(string requestId, string filePath, string operationType)
        {
            var message = HasCompilationErrors()
                ? $"[Warning] {operationType} completed: {filePath}, but compilation errors occurred. Details:\n{GetCompilationErrorDetails()}"
                : $"[Success] {operationType} completed: {filePath}";

            _ = MatrixPluginEditor.NotifyToolRequestCompleted(new RequestToolCompletedData
            {
                RequestId = requestId,
                Result = ResponseCallTool.Success(message).SetRequestID(requestId)
            });
        }

        /// <summary>
        /// Checks if there are any compilation errors by examining Unity's console and compilation pipeline.
        /// </summary>
        /// <returns>True if compilation errors exist</returns>
        public static bool HasCompilationErrors()
        {
            if (EditorUtility.scriptCompilationFailed)
                return true;
            lock (_compilationMessagesLock)
                return _lastCompilationMessages.Any(m => m.type == CompilerMessageType.Error);
        }

        /// <summary>
        /// Retrieves detailed compilation error messages from the last compilation.
        /// </summary>
        /// <returns>Detailed compilation error messages</returns>
        public static string GetCompilationErrorDetails()
        {
            var errors = new List<string>();

            lock (_compilationMessagesLock)
            {
                foreach (var message in _lastCompilationMessages)
                {
                    if (message.type == CompilerMessageType.Error)
                        errors.Add($"[{message.file}] {message.message} (Line: {message.line})");
                }
            }

            if (errors.Count == 0)
            {
                // Since we can reliably check if compilation failed with EditorUtility.scriptCompilationFailed,
                // but getting detailed messages is complex, provide a simple fallback
                if (EditorUtility.scriptCompilationFailed)
                    return "Compilation errors detected. Please check the Unity Console window for detailed error messages.";

                errors.Add("No detailed compilation errors found.");
            }

            return string.Join("\n", errors);
        }
    }
}
