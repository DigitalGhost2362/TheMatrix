#nullable enable
using System;
using UnityEngine;

namespace Feeder.MCP
{
    public class LogEntry
    {
        public LogType LogType { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string? StackTrace { get; set; }

        /// <summary>How many identical entries this one stands for; set only when duplicates are grouped.</summary>
        public int? Count { get; set; }

        public LogEntry()
        {
            LogType = LogType.Log;
            Message = string.Empty;
            Timestamp = DateTime.Now;
            StackTrace = null;
        }
        public LogEntry(LogType logType, string message)
        {
            LogType = logType;
            Message = message;
            Timestamp = DateTime.Now;
            StackTrace = null;
        }
        public LogEntry(LogType logType, string message, string? stackTrace = null)
        {
            LogType = logType;
            Message = message;
            Timestamp = DateTime.Now;
            StackTrace = string.IsNullOrEmpty(stackTrace) ? null : stackTrace;
        }
        public LogEntry(LogType logType, string message, DateTime timestamp, string? stackTrace = null)
        {
            LogType = logType;
            Message = message;
            Timestamp = timestamp;
            StackTrace = string.IsNullOrEmpty(stackTrace) ? null : stackTrace;
        }

        public override string ToString() => ToString(includeStackTrace: false);

        public string ToString(bool includeStackTrace)
        {
            return includeStackTrace && !string.IsNullOrEmpty(StackTrace)
                ? $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{LogType}] {Message}\nStack Trace:\n{StackTrace}"
                : $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{LogType}] {Message}";
        }
    }
}

