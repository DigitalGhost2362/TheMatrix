using System.Text;
using Feeder.Matrix.Unity;

namespace Feeder.Matrix.Tools;

/// <summary>
/// The MCP <c>instructions</c> each session gets at <c>initialize</c>: the linked project's own file when it has one,
/// else the built-in text. Read per session, so an edited file applies to the next client session without a restart.
/// </summary>
public sealed class AgentInstructions(UnityLinkRegistry registry, ILogger<AgentInstructions> logger)
{
    public const int MaxBytes = 2048;
    public const string RelativePath = "ProjectSettings/Packages/com.feeder.mcp/AgentInstructions.md";

    public const string Default =
        "Matrix links this session to a Unity Editor.\n" +
        "- Matrix skills (matrix-*) are in your skills list: open the matching one yourself before the work it describes; no user action is needed. " +
        "matrix-shared-editor before play mode, compiles or scene saves; matrix-script-execute before writing C# for the Editor.\n" +
        "- Before writing a script-execute snippet, call unity-tool-list with a regex: a disabled tool may already do the job. Enable it with tool-set-enabled-state.\n" +
        "- If the same job comes up a third time and no tool fits, create a project tool with the matrix-tool-create skill: one tool per area with an `action` parameter, " +
        "Enabled = false, short description. Tell the user what you added.\n" +
        "- Check state with editor-health. Batch .cs edits, then one assets-refresh.\n" +
        "- A dirty scene in an idle Editor is not a reason to stop: save it and go on. Never discard unsaved changes. Temp assets go under Assets/_Mx*.";

    public string Current() => Resolve(registry.GetLatestLink()?.ProjectRoot, logger);

    public static string Resolve(string? projectRoot, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(projectRoot))
            return Default;
        var path = Path.Combine(projectRoot, RelativePath);
        string text;
        try
        {
            if (!File.Exists(path))
                return Default;
            text = File.ReadAllText(path).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Could not read {Path} ({Error}); using the built-in agent instructions.", path, e.Message);
            return Default;
        }
        if (text.Length == 0)
            return Default;
        if (Encoding.UTF8.GetByteCount(text) <= MaxBytes)
            return text;

        logger?.LogWarning("{Path} is longer than {Max} bytes; only the first {Max} bytes are sent to clients.", path, MaxBytes, MaxBytes);
        var cut = Math.Min(text.Length, MaxBytes);
        while (Encoding.UTF8.GetByteCount(text.AsSpan(0, cut)) > MaxBytes)
            cut--;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return text[..cut];
    }
}
