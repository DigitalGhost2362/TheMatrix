using Feeder.Matrix.Tools;

namespace Feeder.Matrix.UnitTests;

public class ClaudePermissionMirrorTests
{
    private const string Target = "mcp__Feeder-MCP__scene-open";
    private static readonly string[] Targets = [Target];
    private static readonly string[] Proxies = ["mcp__Feeder-MCP__tool-call"];

    private static string? Check(params string?[] jsons)
        => ClaudePermissionMirror.Check(Targets, Proxies, jsons.Select((json, i) => new SettingsSource($"file{i}.json", json)));

    [Theory]
    [InlineData("mcp__Feeder-MCP__scene-open", true)]
    [InlineData("mcp__Feeder-MCP", true)]
    [InlineData("mcp__Feeder-MCP__*", true)]
    [InlineData("mcp__*", true)]
    [InlineData("*", true)]
    [InlineData("mcp__Feeder-MCP__scene-*", true)]
    [InlineData("mcp__Feeder-MCP__scene-open(x)", false)]
    [InlineData("mcp__feeder-mcp__scene-open", false)]
    [InlineData("mcp__Feeder-MCP__scene-save", false)]
    [InlineData("mcp__Feeder", false)]
    [InlineData("Bash(rm *)", false)]
    [InlineData("", false)]
    public void Rule_matching_follows_claude_code(string rule, bool expected)
        => Assert.Equal(expected, ClaudePermissionMirror.RuleMatches(rule, Target));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("*", true)]
    [InlineData("mcp__Feeder-MCP__scene-open", true)]
    [InlineData("Bash|mcp__Feeder-MCP__scene-open", true)]
    [InlineData("Bash, mcp__Feeder-MCP__scene-open", true)]
    [InlineData("mcp__Feeder-MCP", false)]
    [InlineData("mcp__Feeder-MCP__.*", true)]
    [InlineData("mcp__.*__scene-.*", true)]
    [InlineData("Write|Edit", false)]
    [InlineData("MCP__FEEDER-MCP__SCENE-OPEN", false)]
    public void Hook_matcher_follows_claude_code(string? matcher, bool expected)
        => Assert.Equal(expected, ClaudePermissionMirror.HookMatches(matcher, Target));

    [Fact]
    public void Missing_or_empty_settings_allow()
    {
        Assert.Null(Check(null, null));
        Assert.Null(Check("{}"));
        Assert.Null(Check("""{"permissions":{"allow":["mcp__Feeder-MCP__scene-open"]}}"""));
    }

    [Fact]
    public void Deny_rule_refuses_and_quotes_rule_and_file()
    {
        var reason = Check("{}", """{"permissions":{"deny":["mcp__Feeder-MCP__scene-open"]}}""");
        Assert.NotNull(reason);
        Assert.Contains("permissions.deny \"mcp__Feeder-MCP__scene-open\"", reason);
        Assert.Contains("file1.json", reason);
    }

    [Theory]
    [InlineData("""{"permissions":{"ask":["mcp__Feeder-MCP__scene-open"]}}""", "permissions.ask")]
    [InlineData("""{"permissions":{"ask":["mcp__Feeder-MCP"]}}""", "permissions.ask")]
    [InlineData("""{"permissions":{"deny":["mcp__Feeder-MCP"]}}""", "permissions.deny")]
    public void Ask_and_whole_server_rules_refuse(string json, string expected)
        => Assert.Contains(expected, Check(json));

    [Fact]
    public void Hook_for_target_only_refuses()
    {
        var reason = Check("""{"hooks":{"PreToolUse":[{"matcher":"mcp__Feeder-MCP__scene-open","hooks":[{"type":"command","command":"x"}]}]}}""");
        Assert.NotNull(reason);
        Assert.Contains("PreToolUse", reason);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("mcp__Feeder-MCP__.*")]
    public void Hook_that_also_runs_for_tool_call_is_not_bypassed(string matcher)
        => Assert.Null(Check($$$"""{"hooks":{"PreToolUse":[{"matcher":"{{{matcher}}}","hooks":[{"type":"command","command":"x"}]}]}}"""));

    [Fact]
    public void Hook_without_matcher_runs_for_every_tool()
        => Assert.Null(Check("""{"hooks":{"PreToolUse":[{"hooks":[{"type":"command","command":"x"}]}]}}"""));

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"permissions":{"deny":"mcp__Feeder-MCP"}}""")]
    [InlineData("""{"hooks":{"PreToolUse":{"matcher":"x"}}}""")]
    [InlineData("""{"hooks":{"PreToolUse":[{"matcher":"(unclosed","hooks":[]}]}}""")]
    public void Broken_settings_fail_closed(string json)
    {
        var reason = Check(json);
        Assert.NotNull(reason);
        Assert.Contains("file0.json", reason);
    }

    [Fact]
    public void Unreadable_file_fails_closed()
    {
        var reason = ClaudePermissionMirror.Check(Targets, Proxies, [new SettingsSource("locked.json", null, "access denied")]);
        Assert.Contains("locked.json", reason);
    }

    [Fact]
    public void Entry_names_are_normalized_like_claude_code()
    {
        Assert.Equal("mcp__Feeder-MCP__scene-open", ClaudePermissionMirror.FullName("Feeder-MCP", "scene-open"));
        Assert.Equal("mcp__my_server__a_b", ClaudePermissionMirror.FullName("my server", "a.b"));
    }

    [Fact]
    public void Project_settings_are_read_from_project_root()
    {
        var root = Directory.CreateTempSubdirectory("mirror-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".claude"));
            File.WriteAllText(Path.Combine(root, ".claude", "settings.local.json"), """{"permissions":{"deny":["mcp__Feeder-MCP__scene-open"]}}""");
            var sources = ClaudePermissionMirror.DiscoverSources(root).ToArray();
            Assert.Contains(sources, s => s.Label.EndsWith("settings.local.json") && s.Json is not null);
            Assert.Contains("settings.local.json", ClaudePermissionMirror.Check(Targets, Proxies, sources.Where(s => s.Label.StartsWith(root))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Entry_names_come_from_every_mcp_config_pointing_at_the_port()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            {"mcpServers":{"unity":{"type":"http","url":"http://localhost:25213/mcp"},"other":{"url":"http://localhost:1/mcp"}},
             "projects":{"D:/Game":{"mcpServers":{"Matrix Local":{"command":"x.exe","args":["port=25213"]}}}}}
            """);
        var names = new List<string>();
        Feeder.Matrix.Tools.ServerTools.CollectEntries(document.RootElement, 25213, names, 0);
        Assert.Equal(new[] { "unity", "Matrix Local" }, names);
        Assert.Equal("mcp__Matrix_Local__scene-open", ClaudePermissionMirror.FullName("Matrix Local", "scene-open"));
    }
}
