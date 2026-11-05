using System.Security.Cryptography;
using System.Text;

namespace Feeder.Matrix;

public static class MatrixAuthentication
{
    public static bool HasValidBearerToken(string? authorization, string? expectedToken)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(authorization) ||
            !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return FixedTimeEquals(authorization[prefix.Length..], expectedToken);
    }

    // No Origin header (CLI agents, the Unity plugin) passes; a browser page must come from a loopback origin.
    public static bool IsAllowedOrigin(string? origin)
        => origin is null ||
           Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
           uri.Scheme is "http" or "https" &&
           uri.Host is "localhost" or "127.0.0.1" or "[::1]" && uri.PathAndQuery == "/" && uri.UserInfo.Length == 0;

    public static bool FixedTimeEquals(string? actual, string? expected)
    {
        if (actual is null || expected is null)
            return false;

        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
