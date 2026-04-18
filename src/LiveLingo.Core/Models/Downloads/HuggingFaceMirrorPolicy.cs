using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Single source of truth for "which Hugging Face host should this request go to,
/// and may we attach the user's bearer token?".
///
/// The policy owns the volatile <c>_useFallbackMirror</c> latch that flips on the first
/// HF socket failure, so every collaborator that talks to HF agrees on the active hub.
/// </summary>
internal sealed class HuggingFaceMirrorPolicy
{
    public const string HuggingFaceHost = "https://huggingface.co";
    public const string DefaultFallbackMirror = "https://hf-mirror.com";
    private const string HuggingFaceHostName = "huggingface.co";
    private const string DefaultFallbackMirrorHostName = "hf-mirror.com";

    private readonly CoreOptions _options;
    private readonly ILogger _logger;
    private volatile bool _useFallbackMirror;

    public HuggingFaceMirrorPolicy(CoreOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool UsingFallbackMirror => _useFallbackMirror;

    public void Reset() => _useFallbackMirror = false;

    /// <summary>
    /// Returns the scheme+host LiveLingo should use as the resolve-base for a
    /// Hugging Face URL. Honours an explicit user-configured mirror first,
    /// then the auto fallback latch, otherwise the canonical hub.
    /// Non-HF URLs are returned with their own scheme+authority intact.
    /// </summary>
    public string GetEffectiveHubBase(string originalAssetUrl)
    {
        if (originalAssetUrl.StartsWith(HuggingFaceHost, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(_options.HuggingFaceMirror))
                return _options.HuggingFaceMirror.TrimEnd('/');
            if (_useFallbackMirror)
                return DefaultFallbackMirror;
            return HuggingFaceHost;
        }

        var uri = new Uri(originalAssetUrl);
        return $"{uri.Scheme}://{uri.Authority}";
    }

    /// <summary>
    /// Rewrites a Hugging Face hub URL to point at the active mirror (if any).
    /// Non-HF URLs pass through unchanged.
    /// </summary>
    public string ApplyMirror(string url)
    {
        if (!string.IsNullOrWhiteSpace(_options.HuggingFaceMirror))
        {
            if (url.StartsWith(HuggingFaceHost, StringComparison.OrdinalIgnoreCase))
            {
                var mirror = _options.HuggingFaceMirror.TrimEnd('/');
                var rewritten = mirror + url[HuggingFaceHost.Length..];
                _logger.LogDebug("Rewriting HuggingFace URL: {Original} → {Mirror}", url, rewritten);
                return rewritten;
            }

            return url;
        }

        if (_useFallbackMirror && url.StartsWith(HuggingFaceHost, StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = DefaultFallbackMirror + url[HuggingFaceHost.Length..];
            _logger.LogDebug("Rewriting HuggingFace URL via fallback mirror: {Original} → {Mirror}", url, rewritten);
            return rewritten;
        }

        return url;
    }

    /// <summary>
    /// Same as <see cref="ApplyMirror"/> but only for the auto fallback (used after a SocketException retry).
    /// </summary>
    public string RewriteToFallbackMirror(string huggingFaceUrl) =>
        DefaultFallbackMirror + huggingFaceUrl[HuggingFaceHost.Length..];

    /// <summary>
    /// True when the given exception was a socket-level reach failure against the
    /// canonical hub and we have not yet engaged the auto mirror fallback.
    /// </summary>
    public bool ShouldFallbackToMirror(string originalUrl, Exception ex) =>
        !_useFallbackMirror
        && string.IsNullOrWhiteSpace(_options.HuggingFaceMirror)
        && originalUrl.StartsWith(HuggingFaceHost, StringComparison.OrdinalIgnoreCase)
        && ex is HttpRequestException { InnerException: SocketException };

    /// <summary>
    /// Latches the auto fallback so future requests in this process route through hf-mirror.
    /// </summary>
    public void EngageFallbackMirror() => _useFallbackMirror = true;

    /// <summary>
    /// Decides whether to attach the user's HF bearer token for a particular outbound URL.
    /// Tokens are sent to: huggingface.co, the active auto mirror, and an explicit user mirror — never anywhere else.
    /// </summary>
    public bool ShouldAttachBearer(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Host.Equals(HuggingFaceHostName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (_useFallbackMirror && uri.Host.Equals(DefaultFallbackMirrorHostName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(_options.HuggingFaceMirror)
            && Uri.TryCreate(_options.HuggingFaceMirror.Trim(), UriKind.Absolute, out var mirror)
            && uri.Host.Equals(mirror.Host, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Sets the Authorization header on <paramref name="request"/> when the policy
    /// allows attaching the configured HF token to that target URL.
    /// </summary>
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
