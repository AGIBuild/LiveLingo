using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Cloud;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Services.Cloud;

public sealed class CloudProviderRuntimeStateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cachePath;

    public CloudProviderRuntimeStateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingoCloudState_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cachePath = Path.Combine(_tempDir, "cloud-provider-state.json");
    }

    [Fact]
    public async Task RefreshAsync_PersistsHealthySnapshot_AndLaterReloadsFromCache()
    {
        var probe = Substitute.For<ICloudProviderProbeService>();
        probe.GetModelCatalogAsync(Arg.Any<CloudProviderProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new CloudProviderModelCatalogResult(
                    true,
                    [
                        new CloudProviderModelInfo("gpt-4.1-mini", "openai"),
                        new CloudProviderModelInfo("gpt-4.1", "openai")
                    ]));

        var service = new CloudProviderRuntimeStateService(
            probe,
            NullLogger<CloudProviderRuntimeStateService>.Instance,
            _cachePath,
            () => DateTimeOffset.Parse("2026-04-15T10:00:00Z"));
        var preferences = new CloudModelPreferences(
            true,
            "https://api.openai.com/v1",
            "sk-test",
            "gpt-4.1-mini",
            "gpt-4.1");

        var snapshot = await service.RefreshAsync(preferences, TestContext.Current.CancellationToken);
        var reloaded = new CloudProviderRuntimeStateService(
            Substitute.For<ICloudProviderProbeService>(),
            NullLogger<CloudProviderRuntimeStateService>.Instance,
            _cachePath,
            () => DateTimeOffset.Parse("2026-04-15T10:05:00Z"));

        Assert.Equal(CloudProviderRuntimeStatus.Healthy, snapshot.Status);
        Assert.Equal(CloudProviderValidationMode.ModelCatalog, snapshot.ValidationMode);
        Assert.Equal(2, snapshot.Models.Count);
        Assert.Equal(CloudProviderRuntimeStatus.Healthy, reloaded.Current.Status);
        Assert.Equal(CloudProviderValidationMode.ModelCatalog, reloaded.Current.ValidationMode);
        Assert.Collection(
            reloaded.Current.Models,
            first => Assert.Equal("gpt-4.1-mini", first.Id),
            second => Assert.Equal("gpt-4.1", second.Id));
    }

    [Fact]
    public async Task RefreshAsync_ReturnsInvalidModelSelection_WhenConfiguredModelMissingFromProviderCatalog()
    {
        var probe = Substitute.For<ICloudProviderProbeService>();
        probe.GetModelCatalogAsync(Arg.Any<CloudProviderProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CloudProviderModelCatalogResult(true, [new CloudProviderModelInfo("gpt-4.1", "openai")]));
        var service = new CloudProviderRuntimeStateService(
            probe,
            NullLogger<CloudProviderRuntimeStateService>.Instance,
            _cachePath);
        var preferences = new CloudModelPreferences(
            true,
            "https://api.openai.com/v1",
            "sk-test",
            "gpt-4.1-mini",
            null);

        var snapshot = await service.RefreshAsync(preferences, TestContext.Current.CancellationToken);
        var routingState = service.GetRoutingState(preferences);

        Assert.Equal(CloudProviderRuntimeStatus.InvalidModelSelection, snapshot.Status);
        Assert.False(routingState.IsHealthy);
        Assert.True(routingState.HasValidation);
        Assert.Contains("gpt-4.1-mini", snapshot.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRoutingState_TreatsExpiredFailureCacheAsUnknown()
    {
        var probe = Substitute.For<ICloudProviderProbeService>();
        probe.GetModelCatalogAsync(Arg.Any<CloudProviderProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<CloudProviderModelCatalogResult>>(_ => throw new HttpRequestException("provider unavailable"));
        var preferences = new CloudModelPreferences(
            true,
            "https://api.openai.com/v1",
            "sk-test",
            "gpt-4.1-mini",
            null);

        var service = new CloudProviderRuntimeStateService(
            probe,
            NullLogger<CloudProviderRuntimeStateService>.Instance,
            _cachePath,
            () => DateTimeOffset.Parse("2026-04-15T10:00:00Z"));
        await service.RefreshAsync(preferences, TestContext.Current.CancellationToken);

        var reloaded = new CloudProviderRuntimeStateService(
            Substitute.For<ICloudProviderProbeService>(),
            NullLogger<CloudProviderRuntimeStateService>.Instance,
            _cachePath,
            () => DateTimeOffset.Parse("2026-04-15T10:30:00Z"));
        var routingState = reloaded.GetRoutingState(preferences);

        Assert.False(routingState.HasValidation);
        Assert.Null(routingState.Message);
    }

    [Fact]
    public async Task RefreshAsync_FallsBackToDirectModelProbe_WhenCatalogUnsupported()
    {
        var probe = Substitute.For<ICloudProviderProbeService>();
        probe.GetModelCatalogAsync(Arg.Any<CloudProviderProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CloudProviderModelCatalogResult(false, []));
        probe.ProbeModelAsync(Arg.Any<CloudProviderProbeRequest>(), "gpt-4.1-mini", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var service = new CloudProviderRuntimeStateService(
            probe,
            NullLogger<CloudProviderRuntimeStateService>.Instance,
            _cachePath);
        var preferences = new CloudModelPreferences(
            true,
            "https://api.groq.com/openai/v1",
            "sk-test",
            "gpt-4.1-mini",
            null);

        var snapshot = await service.RefreshAsync(preferences, TestContext.Current.CancellationToken);
        var routingState = service.GetRoutingState(preferences);

        Assert.Equal(CloudProviderRuntimeStatus.Healthy, snapshot.Status);
        Assert.Equal(CloudProviderValidationMode.DirectModelProbe, snapshot.ValidationMode);
        Assert.Empty(snapshot.Models);
        Assert.True(routingState.HasValidation);
        Assert.True(routingState.IsHealthy);
        Assert.Contains("does not expose a model catalog", snapshot.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAsync_ReturnsUnavailable_WhenCatalogUnsupportedAndDirectProbeFails()
    {
        var probe = Substitute.For<ICloudProviderProbeService>();
        probe.GetModelCatalogAsync(Arg.Any<CloudProviderProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CloudProviderModelCatalogResult(false, []));
        probe.ProbeModelAsync(Arg.Any<CloudProviderProbeRequest>(), "gpt-4.1-mini", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("chat probe failed"));
        var service = new CloudProviderRuntimeStateService(
            probe,
            NullLogger<CloudProviderRuntimeStateService>.Instance,
            _cachePath);
        var preferences = new CloudModelPreferences(
            true,
            "https://gateway.example.com/v1",
            "sk-test",
            "gpt-4.1-mini",
            null);

        var snapshot = await service.RefreshAsync(preferences, TestContext.Current.CancellationToken);

        Assert.Equal(CloudProviderRuntimeStatus.Unavailable, snapshot.Status);
        Assert.Equal(CloudProviderValidationMode.DirectModelProbe, snapshot.ValidationMode);
        Assert.Contains("chat probe failed", snapshot.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
