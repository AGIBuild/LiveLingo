using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Single source of truth for "which Hugging Face host should this request go to, and may we attach the
/// user's bearer token?". The canonical hub is used unless the user configured a mirror; a failed request is
/// never silently re-sent to another host.
/// </summary>
internal sealed class HuggingFaceMirrorPolicy
{
    public const string HuggingFaceHost = "https://huggingface.co";
    private const string HuggingFaceHostName = "huggingface.co";

    private readonly CoreOptions _options;
    private readonly ILogger _logger;

    public HuggingFaceMirrorPolicy(CoreOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Returns the scheme+host to use as the resolve-base for a Hugging Face URL: the user's mirror when
    /// configured, otherwise the canonical hub. Non-HF URLs keep their own scheme+authority.
    /// </summary>
    public string GetEffectiveHubBase(string originalAssetUrl)
    {
        if (originalAssetUrl.StartsWith(HuggingFaceHost, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(_options.HuggingFaceMirror)
                ? HuggingFaceHost
                : _options.HuggingFaceMirror.TrimEnd('/');
        }

        var uri = new Uri(originalAssetUrl);
        return $"{uri.Scheme}://{uri.Authority}";
    }

    /// <summary>Rewrites a Hugging Face hub URL to the user's mirror, if one is configured. Other URLs pass through.</summary>
    public string ApplyMirror(string url)
    {
        if (string.IsNullOrWhiteSpace(_options.HuggingFaceMirror)
            || !url.StartsWith(HuggingFaceHost, StringComparison.OrdinalIgnoreCase))
            return url;

        var rewritten = _options.HuggingFaceMirror.TrimEnd('/') + url[HuggingFaceHost.Length..];
        _logger.LogDebug("Rewriting HuggingFace URL: {Original} → {Mirror}", url, rewritten);
        return rewritten;
    }

    /// <summary>Tokens are sent to huggingface.co and to the user's explicit mirror — never anywhere else.</summary>
    public bool ShouldAttachBearer(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Host.Equals(HuggingFaceHostName, StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrWhiteSpace(_options.HuggingFaceMirror)
            && Uri.TryCreate(_options.HuggingFaceMirror.Trim(), UriKind.Absolute, out var mirror)
            && uri.Host.Equals(mirror.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sets the Authorization header when the configured HF token may be sent to that URL.</summary>
    public void AttachBearerIfAllowed(HttpRequestMessage request, string url)
    {
        if (!ShouldAttachBearer(url))
            return;
        var token = _options.HuggingFaceToken;
        if (string.IsNullOrWhiteSpace(token))
            return;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }
}
