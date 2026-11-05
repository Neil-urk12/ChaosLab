using Backend.Infrastructure.Identity;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

public sealed class BootstrapSetupOptionsTests
{
    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://example.test/chaos/", true)]
    [InlineData("/relative", false)]
    [InlineData("ftp://example.test", false)]
    [InlineData("https://example.test/?secret=value", false)]
    [InlineData("https://example.test/#fragment", false)]
    [InlineData("https://example.test/?", false)]
    [InlineData("https://example.test/#", false)]
    [InlineData("https://user:password@example.test", false)]
    [InlineData("", false)]
    public void Public_url_must_be_absolute_http_without_query_fragment_or_credentials(string url, bool valid)
    {
        Assert.Equal(valid, BootstrapSetupOptions.IsValidPublicUrl(url));
    }

    [Fact]
    public void Token_is_random_stable_for_the_process_and_expires_at_one_hour()
    {
        var time = new SetupTimeProvider();
        var token = new BootstrapSetupToken(time);
        var url = token.SetupUrl(new BootstrapSetupOptions().PublicUrl);
        var value = url.Split("#token=")[1];
        Assert.Equal(64, value.Length);
        Assert.True(token.IsValid(value));
        Assert.False(token.IsValid(null));
        Assert.False(token.IsValid(value + "a"));
        Assert.False(url == new BootstrapSetupToken(time).SetupUrl(new BootstrapSetupOptions().PublicUrl));
        Assert.True(url == token.SetupUrl(new BootstrapSetupOptions().PublicUrl));
        time.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
        Assert.True(token.IsValid(value));
        time.Advance(TimeSpan.FromTicks(1));
        Assert.False(token.IsValid(value));
    }
}
