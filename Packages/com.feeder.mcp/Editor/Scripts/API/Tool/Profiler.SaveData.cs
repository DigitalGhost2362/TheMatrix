#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using Feeder.Json;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string ProfilerSaveDataToolId = "profiler-save-data";
        [AiTool
        (
            ProfilerSaveDataToolId,
            Title = "Profiler / Save Data",
            Enabled = false
        )]
        [AiSkillDescription("Save a snapshot of profiler-derived stats (status + memory + rendering + script + frame capture) to a JSON file. Built-in Unity APIs only.")]
        [AiSkillBody("Composes the outputs of `profiler-get-status`, `profiler-get-memory-stats`, " +
            "`profiler-get-rendering-stats`, `profiler-get-script-stats` and `profiler-capture-frame` into a single " +
            "JSON document and writes it to `filePath`. Creates any missing parent directories.\n\n" +
            "## Inputs\n\n" +
            "- `filePath` (required) — absolute or workspace-relative path to write to.\n\n" +
            "## Errors\n\n" +
            "- Returns `[Error]` when `filePath` is empty or the write fails (message includes the underlying exception text).\n\n" +
            "## Behavior\n\n" +
            "Uses `Feeder.Json` for serialization and `System.IO.File.WriteAllText` for the write. " +
            "No external Unity package is required.")]
        [Description("Saves a profiler snapshot (status + memory + rendering + script + frame) to a JSON file.")]
        public string SaveData
        (
            [Description("Absolute or workspace-relative output file path.")]
            string filePath
        )
        {
            return MainThread.Instance.Run(() =>
            {
                if (string.IsNullOrEmpty(filePath))
                    return Error.FilePathIsRequired();

                try
                {
                    // Compose the snapshot by delegating to sibling Get* methods so the
                    // serialized shape stays in sync with each tool's POCO. Direct field reads
                    // here would silently drift whenever a POCO gains/loses a field. The set of
                    // enabled modules is carried by `status.ActiveModules` (already sorted) —
                    // a separate top-level field would duplicate the data and risk drift.
                    var snapshot = new
                    {
                        savedAt = DateTime.UtcNow.ToString("O"),
                        status = GetStatus(),
                        memory = GetMemoryStats(),
                        rendering = GetRenderingStats(),
                        script = GetScriptStats(),
                        frame = CaptureFrame()
                    };

                    var json = Feeder.Json.JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(filePath, json);
                    return $"[Success] Profiler snapshot saved to '{filePath}'.";
                }
                catch (Exception ex)
                {
                    return Error.FailedToSaveData(ex.Message);
                }
            });
        }
    }
}
