using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace LiveLingo.Desktop.Web;

/// <summary>
/// Assembles the single HTML page the web view loads (design D17, D20). The embedded styles and scripts are
/// inlined under one CSP nonce, so the page needs no network, no origin and no build step.
/// </summary>
internal static partial class WebPage
{
    private const string Prefix = "LiveLingo.Desktop.Web.Assets.";
    private static readonly Assembly Assets = typeof(WebPage).Assembly;

    // The nonce lands inside an HTML attribute and a CSP header value: accept only the base64 alphabet.
    [GeneratedRegex(@"^[A-Za-z0-9+/_-]{16,}={0,2}$")]
    private static partial Regex NoncePattern();

    public static string Build(string nonce)
    {
        if (!NoncePattern().IsMatch(nonce))
            throw new ArgumentException("The nonce must be at least 16 characters of the base64 alphabet.", nameof(nonce));

        var styles = string.Join('\n', Names("styles").Select(Read));
        // Libraries first, then our own scripts in file-name order (00-, 10-, ...).
        var scripts = string.Concat(Names("vendor").Concat(Names("app"))
            .Select(name => $"<script nonce=\"{nonce}\">{Read(name)}</script>\n"));

        return Read(Prefix + "page.html")
            .Replace("__NONCE__", nonce)
            .Replace("__STYLES__", styles)
            .Replace("__SCRIPTS__", scripts);
    }

    /// <summary>Our own sources (not the vendored libraries), for the forbidden-API scan.</summary>
    internal static IEnumerable<(string Name, string Text)> OwnSources() =>
        Names("app").Concat(Names("styles")).Append(Prefix + "page.html").Select(name => (name, Read(name)));

    private static IEnumerable<string> Names(string folder) =>
        Assets.GetManifestResourceNames()
            .Where(name => name.StartsWith($"{Prefix}{folder}.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static string Read(string resourceName)
    {
        using var stream = Assets.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded web asset {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
