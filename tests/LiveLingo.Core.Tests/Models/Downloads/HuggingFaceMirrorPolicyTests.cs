using System.Net;
using System.Net.Sockets;
using LiveLingo.Core.Models.Downloads;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Models.Downloads;

public sealed class HuggingFaceMirrorPolicyTests
{
    private static HuggingFaceMirrorPolicy MakePolicy(string? userMirror = null, string? token = null) =>
        new(new CoreOptions
        {
            HuggingFaceMirror = userMirror,
            HuggingFaceToken = token,
        }, NullLogger.Instance);

    // ── ApplyMirror ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMirror_WithUserConfiguredMirror_RewritesHuggingFaceUrl()
    {
        var policy = MakePolicy(userMirror: "https://my.hf-mirror.example/");

        var rewritten = policy.ApplyMirror("https://huggingface.co/owner/repo/resolve/main/file.gguf");

        Assert.Equal("https://my.hf-mirror.example/owner/repo/resolve/main/file.gguf", rewritten);
    }

    [Fact]
    public void ApplyMirror_WithUserConfiguredMirror_LeavesNonHfUrlsAlone()
    {
        var policy = MakePolicy(userMirror: "https://my.hf-mirror.example");

        var url = policy.ApplyMirror("https://other.example/file.bin");

        Assert.Equal("https://other.example/file.bin", url);
    }

    [Fact]
    public void ApplyMirror_NoMirrorAndFallbackInactive_ReturnsOriginal()
    {
        var policy = MakePolicy();

        var url = policy.ApplyMirror("https://huggingface.co/owner/repo/resolve/main/file.gguf");

        Assert.Equal("https://huggingface.co/owner/repo/resolve/main/file.gguf", url);
    }

    [Fact]
    public void ApplyMirror_AfterEngagingFallback_RewritesToHfMirror()
    {
        var policy = MakePolicy();

        policy.EngageFallbackMirror();
        var url = policy.ApplyMirror("https://huggingface.co/owner/repo/resolve/main/file.gguf");

        Assert.Equal("https://hf-mirror.com/owner/repo/resolve/main/file.gguf", url);
    }

    // ── GetEffectiveHubBase ────────────────────────────────────────────────

    [Fact]
    public void GetEffectiveHubBase_HfUrl_PrefersUserMirror()
    {
        var policy = MakePolicy(userMirror: "https://user.example/");

        Assert.Equal("https://user.example", policy.GetEffectiveHubBase("https://huggingface.co/owner/repo"));
    }

    [Fact]
    public void GetEffectiveHubBase_HfUrl_FallsBackToFallbackHubWhenLatched()
    {
        var policy = MakePolicy();
        policy.EngageFallbackMirror();

        Assert.Equal("https://hf-mirror.com", policy.GetEffectiveHubBase("https://huggingface.co/owner/repo"));
    }

    [Fact]
    public void GetEffectiveHubBase_HfUrl_DefaultsToCanonicalHub()
    {
        var policy = MakePolicy();

        Assert.Equal("https://huggingface.co", policy.GetEffectiveHubBase("https://huggingface.co/owner/repo"));
    }

    [Fact]
    public void GetEffectiveHubBase_NonHfUrl_ReturnsOriginalSchemeAndAuthority()
    {
        var policy = MakePolicy(userMirror: "https://ignored.example");

        Assert.Equal("https://other.example", policy.GetEffectiveHubBase("https://other.example/path/to/file.bin"));
    }

    // ── ShouldFallbackToMirror ─────────────────────────────────────────────

    [Fact]
    public void ShouldFallbackToMirror_TrueOnSocketExceptionToHfWithoutUserMirror()
    {
        var policy = MakePolicy();
        var ex = new HttpRequestException("boom", new SocketException());

        Assert.True(policy.ShouldFallbackToMirror("https://huggingface.co/x", ex));
    }

    [Fact]
    public void ShouldFallbackToMirror_FalseWhenAlreadyEngaged()
    {
        var policy = MakePolicy();
        policy.EngageFallbackMirror();
        var ex = new HttpRequestException("boom", new SocketException());

        Assert.False(policy.ShouldFallbackToMirror("https://huggingface.co/x", ex));
    }

    [Fact]
    public void ShouldFallbackToMirror_FalseWhenUserMirrorConfigured()
    {
        var policy = MakePolicy(userMirror: "https://user.example");
        var ex = new HttpRequestException("boom", new SocketException());

        Assert.False(policy.ShouldFallbackToMirror("https://huggingface.co/x", ex));
    }

    [Fact]
    public void ShouldFallbackToMirror_FalseForNonHfUrl()
    {
        var policy = MakePolicy();
        var ex = new HttpRequestException("boom", new SocketException());

        Assert.False(policy.ShouldFallbackToMirror("https://other.example/x", ex));
    }

    [Fact]
    public void ShouldFallbackToMirror_FalseForNonSocketCause()
    {
        var policy = MakePolicy();
        var ex = new HttpRequestException("boom", new InvalidOperationException());

        Assert.False(policy.ShouldFallbackToMirror("https://huggingface.co/x", ex));
    }

    // ── EngageFallbackMirror / Reset / UsingFallbackMirror ─────────────────

    [Fact]
    public void EngageFallbackMirror_FlipsLatch()
    {
        var policy = MakePolicy();

        Assert.False(policy.UsingFallbackMirror);
        policy.EngageFallbackMirror();
        Assert.True(policy.UsingFallbackMirror);
    }

    [Fact]
    public void Reset_ClearsLatch()
    {
        var policy = MakePolicy();
        policy.EngageFallbackMirror();

        policy.Reset();

        Assert.False(policy.UsingFallbackMirror);
    }

    [Fact]
    public void RewriteToFallbackMirror_PrefixSwap()
    {
        var policy = MakePolicy();

        var rewritten = policy.RewriteToFallbackMirror("https://huggingface.co/path");

        Assert.Equal("https://hf-mirror.com/path", rewritten);
    }

    // ── ShouldAttachBearer ─────────────────────────────────────────────────

    [Fact]
    public void ShouldAttachBearer_AlwaysTrueForCanonicalHfHost()
    {
        var policy = MakePolicy();

        Assert.True(policy.ShouldAttachBearer("https://huggingface.co/anything"));
    }

    [Fact]
    public void ShouldAttachBearer_TrueForHfMirrorWhenFallbackEngaged()
    {
        var policy = MakePolicy();
        policy.EngageFallbackMirror();

        Assert.True(policy.ShouldAttachBearer("https://hf-mirror.com/anything"));
    }

    [Fact]
    public void ShouldAttachBearer_FalseForHfMirrorWithoutFallbackEngaged()
    {
        var policy = MakePolicy();

        Assert.False(policy.ShouldAttachBearer("https://hf-mirror.com/anything"));
    }

    [Fact]
    public void ShouldAttachBearer_TrueForUserConfiguredMirrorHost()
    {
        var policy = MakePolicy(userMirror: "https://my.user-mirror.example");

        Assert.True(policy.ShouldAttachBearer("https://my.user-mirror.example/anything"));
    }

    [Fact]
    public void ShouldAttachBearer_FalseForRandomHost()
    {
        var policy = MakePolicy();

        Assert.False(policy.ShouldAttachBearer("https://random.example/path"));
    }

    [Fact]
    public void ShouldAttachBearer_FalseForUnparseableUrl()
    {
        var policy = MakePolicy();

        Assert.False(policy.ShouldAttachBearer("not a url"));
    }

    // ── AttachBearerIfAllowed ──────────────────────────────────────────────

    [Fact]
    public void AttachBearerIfAllowed_AttachesTokenWhenPolicyAllows()
    {
        var policy = MakePolicy(token: "  the-token  ");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://huggingface.co/x");

        policy.AttachBearerIfAllowed(request, "https://huggingface.co/x");

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("the-token", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public void AttachBearerIfAllowed_DoesNotAttachWhenPolicyDisallows()
    {
        var policy = MakePolicy(token: "the-token");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://other.example/x");

        policy.AttachBearerIfAllowed(request, "https://other.example/x");

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public void AttachBearerIfAllowed_SkipsWhenTokenIsBlank()
    {
        var policy = MakePolicy(token: "   ");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://huggingface.co/x");

        policy.AttachBearerIfAllowed(request, "https://huggingface.co/x");

        Assert.Null(request.Headers.Authorization);
    }
}
