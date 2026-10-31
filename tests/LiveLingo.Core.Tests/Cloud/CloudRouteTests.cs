using LiveLingo.Core.Cloud;

namespace LiveLingo.Core.Tests.Cloud;

public class CloudRouteTests
{
    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://my.openai.azure.com/openai/v1", "https://my.openai.azure.com/openai/v1/chat/completions")]
    [InlineData("  https://api.example.com/v1  ", "https://api.example.com/v1/chat/completions")]
    public void Create_AppendsTheChatCompletionsPath(string baseUrl, string expected)
    {
        var route = CloudRoute.Create(baseUrl, "gpt-4.1-mini", "sk-test");

        Assert.Equal(expected, route.Endpoint.ToString());
        Assert.Equal("gpt-4.1-mini", route.Model);
        Assert.Equal("sk-test", route.ApiKey);
    }

    [Fact]
    public void Create_AcceptsPlainHttpOnlyForLoopback()
    {
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", CloudRoute.Create("http://127.0.0.1:11434/v1", "m", "k").Endpoint.ToString());
        Assert.Equal("http://localhost:1234/v1/chat/completions", CloudRoute.Create("http://localhost:1234/v1", "m", "k").Endpoint.ToString());
        Assert.Throws<ArgumentException>(() => CloudRoute.Create("http://api.example.com/v1", "m", "k"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("api.example.com/v1")]
    [InlineData("ftp://api.example.com/v1")]
    public void Create_RejectsAnUnusableBaseUrl(string baseUrl)
    {
        Assert.Throws<ArgumentException>(() => CloudRoute.Create(baseUrl, "m", "k"));
    }

    [Theory]
    [InlineData("", "k")]
    [InlineData("  ", "k")]
    [InlineData("m", "")]
    [InlineData("m", "  ")]
    public void Create_RequiresAModelAndAKey(string model, string key)
    {
        Assert.Throws<ArgumentException>(() => CloudRoute.Create("https://api.example.com/v1", model, key));
    }

    [Fact]
    public void Create_TrimsTheModelAndKey()
    {
        var route = CloudRoute.Create("https://api.example.com/v1", " m ", " k ");

        Assert.Equal("m", route.Model);
        Assert.Equal("k", route.ApiKey);
    }

    [Fact]
    public void ToString_NeverRevealsTheKey()
    {
        var route = CloudRoute.Create("https://api.example.com/v1", "m", "sk-secret-value");

        Assert.DoesNotContain("sk-secret-value", route.ToString());
    }
}
