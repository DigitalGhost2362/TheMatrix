using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Tools;

// One error shape for every tool result the matrix refuses or fails:
// text "[code] message" for people, structuredContent { error: { code, message, retry_after_ms } } for agents.
public static partial class ToolErrors
{
    public const string EditorLocked = "editor_locked";
    public const string LockQueued = "lock_queued";
    public const string CompilingOrReloading = "compiling_or_reloading";
    public const string Interrupted = "interrupted";
    public const string UnityUnavailable = "unity_unavailable";
    public const string Timeout = "timeout";
    public const string ToolNotFound = "tool_not_found";
    public const string ToolFailed = "tool_failed";
    public const string TestsRunning = "tests_running";
    public const string InvalidArguments = "invalid_arguments";
    public const string QueueFull = "queue_full";

    public const int ReloadRetryMs = 3000;
    public const int QueueFullRetryMs = 1000;
    public const int TimeoutRetryMs = 5000;

    public static CallToolResult Create(string code, string message, int? retryAfterMs = null, IReadOnlyDictionary<string, object?>? extra = null)
    {
        var error = new Dictionary<string, object?> { ["code"] = code, ["message"] = message };
        if (retryAfterMs is { } ms)
            error["retry_after_ms"] = ms;
        if (extra is not null)
            foreach (var (k, v) in extra)
                error[k] = v;

        var text = retryAfterMs is { } wait ? $"[{code}] {message} (retry after {wait} ms)" : $"[{code}] {message}";
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["error"] = error }),
        };
    }

    // Unity's FMP error codes (the failure reached the tool runner) in the matrix's one vocabulary.
    public static CallToolResult FromFmp(string fmpCode, string message)
    {
        var origin = new Dictionary<string, object?> { ["fmp_code"] = fmpCode };
        if (TryParseUnityError(message, out var code, out var text, out var retry))
            return Create(code, text, retry, origin);
        return fmpCode switch
        {
            Protocol.FmpErrorCodes.Interrupted => Create(Interrupted, message, ReloadRetryMs, origin),
            Protocol.FmpErrorCodes.QueueFull => Create(QueueFull, message, QueueFullRetryMs, origin),
            Protocol.FmpErrorCodes.DeadlineExceeded => Create(Timeout, message, TimeoutRetryMs, origin),
            Protocol.FmpErrorCodes.UnityUnavailable => Create(UnityUnavailable, message, TimeoutRetryMs, origin),
            Protocol.FmpErrorCodes.ToolNotFound => Create(ToolNotFound, message, null, origin),
            _ => Create(ToolFailed, message, null, origin),
        };
    }

    // A Unity tool result with IsError and a "[code ...]" first line gains the structured error; its content stays.
    public static CallToolResult WithStructuredError(CallToolResult result)
    {
        if (result.IsError != true || result.StructuredContent is not null ||
            result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text is not { } first ||
            !TryParseUnityError(first, out var code, out var message, out var retry))
            return result;
        result.StructuredContent = Create(code, message, retry).StructuredContent;
        return result;
    }

    // Unity-side tools mark their own errors as "[code] message" or "[code retry_after_ms=N] message".
    public static bool TryParseUnityError(string? text, out string code, out string message, out int? retryAfterMs)
    {
        code = ToolFailed; message = text ?? string.Empty; retryAfterMs = null;
        if (string.IsNullOrEmpty(text))
            return false;
        var m = UnityErrorPrefix().Match(text);
        if (!m.Success)
            return false;
        code = m.Groups["code"].Value;
        message = text[m.Length..].TrimStart();
        if (m.Groups["ms"].Success)
            retryAfterMs = int.Parse(m.Groups["ms"].Value);
        return true;
    }

    [GeneratedRegex(@"^\[(?<code>[a-z][a-z0-9_]*)(?: retry_after_ms=(?<ms>\d{1,9}))?\]")]
    private static partial Regex UnityErrorPrefix();
}
