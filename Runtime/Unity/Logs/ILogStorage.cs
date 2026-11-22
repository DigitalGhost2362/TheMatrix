#nullable enable
using System;
using System.Threading.Tasks;

namespace Feeder.MCP
{
    public interface ILogStorage : IDisposable
    {
        Task AppendAsync(params LogEntry[] entries);
        void Append(params LogEntry[] entries);

        Task FlushAsync();
        void Flush();

        Task<LogEntry[]> QueryAsync(
            int maxEntries = 100,
            UnityEngine.LogType? logTypeFilter = null,
            bool includeStackTrace = false,
            int lastMinutes = 0,
            DateTime? since = null,
            Func<LogEntry, bool>? filter = null);
        LogEntry[] Query(
            int maxEntries = 100,
            UnityEngine.LogType? logTypeFilter = null,
            bool includeStackTrace = false,
            int lastMinutes = 0,
            DateTime? since = null,
            Func<LogEntry, bool>? filter = null);

        void Clear();
    }
}