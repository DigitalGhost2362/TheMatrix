using System.Text;
using System.Text.Json;
using Feeder.Matrix.Tools;

namespace Feeder.Matrix.IntegrationTests;

public class AgentInstructionsTests : IClassFixture<MatrixFactory>, IDisposable
{
    private readonly MatrixFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mx-instructions-" + Guid.NewGuid().ToString("N"));

    public AgentInstructionsTests(MatrixFactory factory) => _factory = factory;

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private void WriteProjectFile(string text)
    {
        var path = Path.Combine(_root, AgentInstructions.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public async Task Initialize_returns_the_default_instructions_without_a_linked_project()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"0"}}}""",
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..].Trim()
            : text;

        using var doc = JsonDocument.Parse(json);
        var instructions = doc.RootElement.GetProperty("result").GetProperty("instructions").GetString();
        Assert.Equal(AgentInstructions.Default, instructions);
    }

    [Fact]
    public void Default_stays_under_the_limit() => Assert.True(Encoding.UTF8.GetByteCount(AgentInstructions.Default) <= AgentInstructions.MaxBytes);

    [Fact]
    public void Project_file_wins_over_the_default()
    {
        WriteProjectFile("Team rule: run the smoke test before every commit.");
        Assert.Equal("Team rule: run the smoke test before every commit.", AgentInstructions.Resolve(_root));
    }

    [Fact]
    public void Missing_or_empty_file_falls_back_to_the_default()
    {
        Assert.Equal(AgentInstructions.Default, AgentInstructions.Resolve(_root));
        Assert.Equal(AgentInstructions.Default, AgentInstructions.Resolve(null));
        WriteProjectFile("   \n");
        Assert.Equal(AgentInstructions.Default, AgentInstructions.Resolve(_root));
    }

    [Fact]
    public void A_file_over_the_limit_is_cut_at_the_limit_without_splitting_a_character()
    {
        WriteProjectFile(new string('a', 2047) + "é" + new string('b', 500));
        var result = AgentInstructions.Resolve(_root);
        Assert.True(Encoding.UTF8.GetByteCount(result) <= AgentInstructions.MaxBytes);
        Assert.Equal(new string('a', 2047), result);
    }
}
