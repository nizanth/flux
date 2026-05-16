using Jellyfin.Plugin.Flux.Api;
using Xunit;

namespace Flux.Tests;

public class XtreamApiClientTests
{
    [Fact]
    public void RedactUrl_WithCredentials_ReplacesWithStars()
    {
        var url = "http://example.com/player_api.php?username=myuser&password=mysecret";
        var result = XtreamApiClient.RedactUrl(url);
        Assert.Contains("username=***", result);
        Assert.Contains("password=***", result);
        Assert.DoesNotContain("myuser", result);
        Assert.DoesNotContain("mysecret", result);
    }

    [Fact]
    public void RedactUrl_WithExtraParams_PreservesNonCredentialParams()
    {
        var url = "http://example.com/player_api.php?username=myuser&password=mysecret&action=get_live_streams";
        var result = XtreamApiClient.RedactUrl(url);
        Assert.Contains("action=get_live_streams", result);
        Assert.Contains("username=***", result);
        Assert.Contains("password=***", result);
    }

    [Fact]
    public void RedactUrl_EmptyUrl_ReturnsStars()
    {
        var result = XtreamApiClient.RedactUrl(string.Empty);
        Assert.Equal("***", result);
    }

    [Fact]
    public void RedactUrl_UrlWithoutQuery_ReturnsOriginal()
    {
        var url = "http://example.com/some/path";
        var result = XtreamApiClient.RedactUrl(url);
        Assert.Equal(url, result);
    }
}
