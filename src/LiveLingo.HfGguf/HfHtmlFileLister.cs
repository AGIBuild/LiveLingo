using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace LiveLingo.HfGguf;

/// <summary>
/// Fallback lister that scrapes the web tree page. May break if HTML layout changes.
/// </summary>
public sealed class HfHtmlFileLister(HttpClient http, string hubWebBase = "https://huggingface.co") : IHfFileLister
{
    private readonly string _hubWebBase = hubWebBase.TrimEnd('/');

    public async Task<IReadOnlyList<string>> ListGgufPathsAsync(
        string repoId,
        string revision,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_hubWebBase}/{repoId.Trim().Trim('/')}/tree/{Uri.EscapeDataString(revision)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());

        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HfHubException(
                $"HF HTML tree failed for repo '{repoId}': HTTP {(int)response.StatusCode}. Prefer API list or pass --file.",
                (int)response.StatusCode,
                url);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var escapedRepo = Regex.Escape(repoId.Trim());
        var escapedRev = Regex.Escape(revision);
        var pattern = $@"href=""/{escapedRepo}/blob/{escapedRev}/([^""]+\.gguf)""";
        foreach (Match m in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
        {
            if (m.Groups.Count > 1)
                paths.Add(Uri.UnescapeDataString(m.Groups[1].Value));
        }

        return paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
}
