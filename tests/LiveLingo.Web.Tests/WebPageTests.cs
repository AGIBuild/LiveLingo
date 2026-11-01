using System.Text;
using System.Text.RegularExpressions;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Web.Tests;

public sealed class WebPageTests
{
    internal const string Nonce = "dGVzdC1ub25jZS0xMjM0NTY3OA==";

    // The design (section 6) forbids every way of turning a string into markup or code in our own sources.
    private static readonly Regex Banned = new(
        @"innerHTML|outerHTML|insertAdjacentHTML|document\.write|dangerouslySetInnerHTML|createContextualFragment|\beval\s*\(|new\s+Function|createElement\(\s*['""]script['""]",
        RegexOptions.Compiled);

    [Fact]
    public void Build_WritesTheNonceIntoEveryInlineTagAndLeavesNoPlaceholder()
    {
        var page = WebPage.Build(Nonce);

        Assert.DoesNotContain("__NONCE__", page);
        Assert.DoesNotContain("__STYLES__", page);
        Assert.DoesNotContain("__SCRIPTS__", page);
        var opening = Regex.Matches(page, "<(?<tag>script|style)(?<attrs>[^>]*)>");
        Assert.NotEmpty(opening);
        Assert.All(opening, m => Assert.Contains($"nonce=\"{Nonce}\"", m.Groups["attrs"].Value));
        // An asset containing a closing tag would end its element early and leak the rest as markup.
        foreach (var tag in new[] { "script", "style" })
            Assert.Equal(opening.Count(m => m.Groups["tag"].Value == tag), Regex.Matches(page, $"</{tag}>").Count);
    }

    [Fact]
    public void Build_AllowsOnlyTheNonceScriptsAndNoNetwork()
    {
        var page = WebPage.Build(Nonce);

        Assert.Contains(
            $"content=\"default-src 'none'; script-src 'nonce-{Nonce}'; style-src 'nonce-{Nonce}'; img-src data:; font-src 'none'; connect-src 'none'; base-uri 'none'; form-action 'none'\"",
            page);
    }

    [Fact]
    public void Build_StaysWithinTheSizeBudget()
    {
        Assert.InRange(Encoding.UTF8.GetByteCount(WebPage.Build(Nonce)), 1, 400 * 1024);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("a\"onload=alert(1) b=cccccccccc")]
    public void Build_RejectsANonceThatCouldBreakOutOfTheAttribute(string nonce)
    {
        Assert.Throws<ArgumentException>(() => WebPage.Build(nonce));
    }

    [Fact]
    public void OwnSources_UseNoApiThatTurnsAStringIntoMarkupOrCode()
    {
        var sources = WebPage.OwnSources().ToList();

        Assert.Contains(sources, s => s.Name.EndsWith("99-main.js", StringComparison.Ordinal));
        Assert.All(sources, s => Assert.False(Banned.IsMatch(s.Text), $"{s.Name} uses a forbidden API"));
        Assert.DoesNotContain(sources, s => s.Name.Contains("vendor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("el.innerHTML = x")]
    [InlineData("eval('1')")]
    [InlineData("new Function('return 1')")]
    [InlineData("document.createElement('script')")]
    public void ForbiddenApiScanner_FlagsTheApisItGuardsAgainst(string code)
    {
        Assert.Matches(Banned, code);
    }
}
