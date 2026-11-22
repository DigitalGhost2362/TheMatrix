namespace Feeder.Matrix.UnitTests;

public class BearerTokenTests
{
    [Theory]
    [InlineData("Bearer secret")]
    [InlineData("bearer secret")]
    [InlineData("BEARER secret")]
    public void Right_token_is_accepted_whatever_the_scheme_case(string header)
        => Assert.True(MatrixAuthentication.HasValidBearerToken(header, "secret"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer wrong")]
    [InlineData("Bearer secret ")]
    [InlineData("Bearer  secret")]
    [InlineData("Bearer secre")]
    [InlineData("Bearer secretx")]
    [InlineData("Basic secret")]
    [InlineData("secret")]
    public void Missing_malformed_or_wrong_token_is_rejected(string? header)
        => Assert.False(MatrixAuthentication.HasValidBearerToken(header, "secret"));

    [Fact]
    public void No_expected_token_rejects_everything()
    {
        Assert.False(MatrixAuthentication.HasValidBearerToken("Bearer secret", null));
        Assert.False(MatrixAuthentication.HasValidBearerToken("Bearer ", null));
    }

    [Fact]
    public void FixedTimeEquals_needs_both_values_and_an_exact_match()
    {
        Assert.True(MatrixAuthentication.FixedTimeEquals("abc", "abc"));
        Assert.False(MatrixAuthentication.FixedTimeEquals("abc", "abd"));
        Assert.False(MatrixAuthentication.FixedTimeEquals("abc", "abcd"));
        Assert.False(MatrixAuthentication.FixedTimeEquals(null, "abc"));
        Assert.False(MatrixAuthentication.FixedTimeEquals("abc", null));
    }
}
