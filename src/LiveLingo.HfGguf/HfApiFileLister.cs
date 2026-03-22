using System.Net.Http.Headers;
using System.Text.Json;

namespace LiveLingo.HfGguf;

/// <summary>
/// Lists repository files via <c>GET /api/models/{namespace}/{repo}/tree/{revision}</c>.
/// </summary>
public sealed class HfApiFileLister(HttpClient http, string hubApiBase = "https://huggingface.co") : IHfFileLister
{
    private readonly string _hubApiBase = hubApiBase.TrimEnd('/');

    public async Task<IReadOnlyList<string>> ListGgufPathsAsync(
        string repoId,
        string revision,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        var (ns, name) = HfRepoId.Split(repoId);
        var collected = new HashSet<string>(StringComparer.Ordinal);
        string? nextUrl = BuildTreePageUrl(ns, name, revision, cursor: null);

        while (nextUrl is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            if (!string.IsNullOrWhiteSpace(bearerToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());

            using var response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HfHubException(
                    $"HF API list failed for repo '{repoId}', revision '{revision}': HTTP {(int)response.StatusCode}. {body}",
                    (int)response.StatusCode,
                    nextUrl);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AddGgufPathsFromTreeJson(json, collected);
            nextUrl = ParseNextLink(response.Headers);
        }

        return collected.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    private string BuildTreePageUrl(string ns, string name, string revision, string? cursor)
    {
        var rev = Uri.EscapeDataString(revision);
        var baseUrl =
            $"{_hubApiBase}/api/models/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/tree/{rev}?recursive=true&limit=1000";
        return cursor is null ? baseUrl : $"{baseUrl}&cursor={Uri.EscapeDataString(cursor)}";
    }

    private static void AddGgufPathsFromTreeJson(string json, HashSet<string> into)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("type", out var typeEl)
                && typeEl.GetString() == "file"
                && el.TryGetProperty("path", out var pathEl))
            {
                var path = pathEl.GetString();
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                    into.Add(path);
            }
        }
    }

    private string? ParseNextLink(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
            return null;

        foreach (var linkHeader in values)
        {
            // Example: <https://huggingface.co/api/models/...&cursor=abc>; rel="next"
            var parts = linkHeader.Split(',');
            foreach (var part in parts)
            {
                var segment = part.Trim();
                if (!segment.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lt = segment.IndexOf('<');
                var gt = segment.IndexOf('>');
                if (lt >= 0 && gt > lt)
                    return segment[(lt + 1)..gt];
            }
        }

        return null;
    }
}
