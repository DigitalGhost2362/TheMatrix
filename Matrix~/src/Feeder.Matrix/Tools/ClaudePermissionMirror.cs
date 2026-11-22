using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Feeder.Matrix.Tools;

/// <summary>One Claude Code settings source: a file or a policy value. <see cref="Json"/> is null when it does not exist.</summary>
public sealed record SettingsSource(string Label, string? Json, string? ReadError = null);

/// <summary>
/// Mirrors the Claude Code rules that a call through <c>tool-call</c> would otherwise skip: deny and ask
/// permission rules, and PreToolUse hooks whose matcher picks the target tool but not <c>tool-call</c>.
/// Reads every settings source the matrix can see on each call (no cache) and refuses when one is
/// unreadable. Sources the matrix cannot see: command-line flags, rules added inside a session,
/// remote or MDM policy, hooks from plugins, skills and agents.
/// </summary>
public static class ClaudePermissionMirror
{
    static readonly Regex ExactMatcher = new("^[a-zA-Z0-9_|, -]+$", RegexOptions.CultureInvariant);
    static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Claude Code's spelling of a server or tool inside <c>mcp__server__tool</c>.</summary>
    public static string NormalizeName(string name) => Regex.Replace(name, "[^a-zA-Z0-9_-]", "_");

    public static string FullName(string entry, string tool) => $"mcp__{NormalizeName(entry)}__{NormalizeName(tool)}";

    /// <summary>Null when no rule or hook stops the call, else the reason to show the agent.</summary>
    public static string? Check(IReadOnlyList<string> targetNames, IReadOnlyList<string> proxyNames, IEnumerable<SettingsSource> sources)
    {
        foreach (var source in sources)
        {
            if (source.ReadError is not null)
                return $"tool-call refused: cannot read Claude Code settings {source.Label} ({source.ReadError}). It fails closed until the file is readable.";
            if (source.Json is null)
                continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(source.Json);
            }
            catch (JsonException e)
            {
                return $"tool-call refused: Claude Code settings {source.Label} is not valid JSON ({e.Message}). It fails closed until the file is fixed.";
            }

            using (document)
            {
                var reason = CheckOne(document.RootElement, source.Label, targetNames, proxyNames);
                if (reason is not null)
                    return reason;
            }
        }
        return null;
    }

    static string? CheckOne(JsonElement root, string label, IReadOnlyList<string> targets, IReadOnlyList<string> proxies)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return $"tool-call refused: Claude Code settings {label} is not a JSON object.";

        if (root.TryGetProperty("permissions", out var permissions) && permissions.ValueKind == JsonValueKind.Object)
        {
            foreach (var kind in new[] { "deny", "ask" })
            {
                if (!permissions.TryGetProperty(kind, out var rules) || rules.ValueKind == JsonValueKind.Null)
                    continue;
                if (rules.ValueKind != JsonValueKind.Array)
                    return $"tool-call refused: permissions.{kind} in {label} is not a list.";
                foreach (var rule in rules.EnumerateArray())
                {
                    if (rule.ValueKind != JsonValueKind.String)
                        continue;
                    var text = rule.GetString()!;
                    var target = targets.FirstOrDefault(name => RuleMatches(text, name));
                    if (target is null)
                        continue;
                    return kind == "deny"
                        ? $"tool-call refused: Claude Code rule permissions.deny \"{text}\" in {label} blocks {target}."
                        : $"tool-call refused: Claude Code rule permissions.ask \"{text}\" in {label} needs your approval for {target}, " +
                          "and tool-call cannot ask for it. Reconnect the MCP server (/mcp) or start a new session so the tool is listed, then call it by its real name.";
                }
            }
        }

        if (root.TryGetProperty("hooks", out var hooks) && hooks.ValueKind == JsonValueKind.Object &&
            hooks.TryGetProperty("PreToolUse", out var preToolUse) && preToolUse.ValueKind != JsonValueKind.Null)
        {
            if (preToolUse.ValueKind != JsonValueKind.Array)
                return $"tool-call refused: hooks.PreToolUse in {label} is not a list.";
            foreach (var entry in preToolUse.EnumerateArray())
            {
                string? matcher = null;
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("matcher", out var m) && m.ValueKind == JsonValueKind.String)
                    matcher = m.GetString();

                bool hitsTarget, hitsProxy;
                try
                {
                    hitsTarget = targets.Any(name => HookMatches(matcher, name));
                    hitsProxy = proxies.Any(name => HookMatches(matcher, name));
                }
                catch (ArgumentException e)
                {
                    return $"tool-call refused: PreToolUse matcher \"{matcher}\" in {label} cannot be read ({e.Message}).";
                }
                catch (RegexMatchTimeoutException)
                {
                    return $"tool-call refused: PreToolUse matcher \"{matcher}\" in {label} took too long to evaluate.";
                }

                // a hook that also matches tool-call runs on this very call and sees the target in tool_input
                if (hitsTarget && !hitsProxy)
                    return $"tool-call refused: a PreToolUse hook (matcher \"{matcher}\") in {label} guards {targets[0]}, " +
                           "and hooks only run for the real tool name. Reconnect the MCP server (/mcp) so the tool is listed, then call it directly.";
            }
        }
        return null;
    }

    /// <summary>Claude Code rule for an MCP tool: exact name, whole server (<c>mcp__server</c>) or a glob over the full name.</summary>
    public static bool RuleMatches(string rule, string fullName)
    {
        rule = rule.Trim();
        if (rule.Length == 0)
            return false;
        // Claude Code skips MCP rules that carry a parenthesised argument
        if (rule.StartsWith("mcp__", StringComparison.Ordinal) && rule.Contains('('))
            return false;
        if (rule == fullName)
            return true;
        if (rule.StartsWith("mcp__", StringComparison.Ordinal) && !rule.Contains('*') &&
            rule.IndexOf("__", 5, StringComparison.Ordinal) < 0 &&
            fullName.StartsWith(rule + "__", StringComparison.Ordinal))
            return true;
        if (!rule.Contains('*'))
            return false;
        var pattern = "^" + string.Join(".*", rule.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(fullName, pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexTimeout);
    }

    /// <summary>Claude Code PreToolUse matcher: empty or <c>*</c> is every tool, a plain word list is exact names, anything else an unanchored regex.</summary>
    public static bool HookMatches(string? matcher, string toolName)
    {
        if (string.IsNullOrEmpty(matcher) || matcher == "*")
            return true;
        if (ExactMatcher.IsMatch(matcher))
            return matcher.Split('|', ',').Select(part => part.Trim()).Any(part => part == toolName);
        return Regex.IsMatch(toolName, matcher, RegexOptions.CultureInvariant, RegexTimeout);
    }

    /// <summary>Every settings source Claude Code reads that this process can see, highest precedence first.</summary>
    public static IEnumerable<SettingsSource> DiscoverSources(string? projectRoot)
    {
        foreach (var path in ManagedFiles())
            yield return ReadFile(path);
        if (OperatingSystem.IsWindows())
        {
            foreach (var source in RegistryPolicies())
                yield return source;
        }
        if (!string.IsNullOrEmpty(projectRoot))
        {
            yield return ReadFile(Path.Combine(projectRoot, ".claude", "settings.local.json"));
            yield return ReadFile(Path.Combine(projectRoot, ".claude", "settings.json"));
        }
        foreach (var path in UserFiles())
            yield return ReadFile(path);
    }

    static IEnumerable<string> UserFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new List<string>();
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDir))
            paths.Add(Path.Combine(configDir, "settings.json"));
        // the matrix may not share the agent's environment, so the default folder is always read too
        if (!string.IsNullOrEmpty(home))
            paths.Add(Path.Combine(home, ".claude", "settings.json"));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    static IEnumerable<string> ManagedFiles()
    {
        string folder;
        if (OperatingSystem.IsWindows())
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode");
        else if (OperatingSystem.IsMacOS())
            folder = "/Library/Application Support/ClaudeCode";
        else
            folder = "/etc/claude-code";

        yield return Path.Combine(folder, "managed-settings.json");
        var dropIns = Path.Combine(folder, "managed-settings.d");
        string[] files = [];
        try
        {
            if (Directory.Exists(dropIns))
                files = Directory.GetFiles(dropIns, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        }
        catch (Exception)
        {
            files = [dropIns];
        }
        foreach (var file in files)
            yield return file;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static IEnumerable<SettingsSource> RegistryPolicies()
    {
        foreach (var (hive, name) in new[] { (Registry.LocalMachine, "HKLM"), (Registry.CurrentUser, "HKCU") })
        {
            var label = $@"{name}\SOFTWARE\Policies\ClaudeCode\Settings";
            SettingsSource source;
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Policies\ClaudeCode");
                source = new SettingsSource(label, key?.GetValue("Settings") as string);
            }
            catch (Exception e)
            {
                source = new SettingsSource(label, null, e.Message);
            }
            yield return source;
        }
    }

    static SettingsSource ReadFile(string path)
    {
        try
        {
            return File.Exists(path)
                ? new SettingsSource(path, File.ReadAllText(path))
                : new SettingsSource(path, null);
        }
        catch (Exception e)
        {
            return new SettingsSource(path, null, e.Message);
        }
    }
}
