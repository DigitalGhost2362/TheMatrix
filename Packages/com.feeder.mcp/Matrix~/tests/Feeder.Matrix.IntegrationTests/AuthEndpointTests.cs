using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Feeder.Matrix.IntegrationTests;

public sealed class AuthFactory : WebApplicationFactory<Program>
{
    public const string Token = "test-token-1234";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("FeederMatrix:EnableMockBackend", "true");
        builder.UseSetting("FeederMatrix:RequireAuth", "true");
        builder.UseSetting("FeederMatrix:Token", Token);
    }
}

public class AuthEndpointTests(AuthFactory factory) : IClassFixture<AuthFactory>
{
    private const string Ping = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";

    private async Task<HttpStatusCode> PostMcpAsync(string? authorization, string? origin = null)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(Ping, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (origin is not null)
            request.Headers.TryAddWithoutValidation("Origin", origin);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong")]
    [InlineData("Basic test-token-1234")]
    [InlineData("Bearer")]
    public async Task Mcp_without_a_valid_token_is_unauthorized(string? header)
        => Assert.Equal(HttpStatusCode.Unauthorized, await PostMcpAsync(header));

    [Fact]
    public async Task Mcp_with_the_right_token_gets_past_authentication()
    {
        var status = await PostMcpAsync("Bearer " + AuthFactory.Token);
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEqual(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task Foreign_origin_is_refused_even_with_the_right_token()
        => Assert.Equal(HttpStatusCode.Forbidden, await PostMcpAsync("Bearer " + AuthFactory.Token, "http://evil.example"));

    [Fact]
    public async Task Healthz_needs_no_token()
    {
        var response = await factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
