namespace Feeder.Matrix.UnitTests;

public class OriginCheckTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:6274")]
    [InlineData("https://localhost:8443")]
    [InlineData("http://127.0.0.1:25213")]
    [InlineData("http://[::1]:3000")]
    [InlineData("HTTP://LOCALHOST:6274")]
    public void Loopback_or_absent_origin_is_allowed(string? origin)
        => Assert.True(MatrixAuthentication.IsAllowedOrigin(origin));

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("http://evil.example")]
    [InlineData("https://attacker.com")]
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://evil.example/localhost")]
    [InlineData("http://localhost@evil.example")]
    [InlineData("http://127.0.0.1.evil.example")]
    [InlineData("ftp://localhost")]
    [InlineData("file://")]
    [InlineData("localhost")]
    public void Foreign_or_malformed_origin_is_rejected(string origin)
        => Assert.False(MatrixAuthentication.IsAllowedOrigin(origin));
}
