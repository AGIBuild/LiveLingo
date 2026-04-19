using LiveLingo.Core.Models;
using LiveLingo.Desktop.ViewModels.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class OllamaProviderProbeOrchestratorTests
{
    [Fact]
    public async Task ProbeAsync_ReturnsProbeUnavailable_WhenServiceMissing()
    {
        var localization = Substitute.For<ISettingsLocalizationHelper>();
        localization.Translate(Arg.Any<string>(), Arg.Any<string>()).Returns("probe missing");
        var sut = new OllamaProviderProbeOrchestrator(probe: null, localization);

        var outcome = await sut.ProbeAsync("http://localhost:11434", TestContext.Current.CancellationToken);

        Assert.Equal(OllamaProbeOutcomeKind.ProbeUnavailable, outcome.Kind);
        Assert.Equal("probe missing", outcome.Message);
        Assert.Empty(outcome.Models);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsInvalidBaseUrl_WhenBaseUrlIsBlank()
    {
        var probe = Substitute.For<IOllamaProbeService>();
        var localization = Substitute.For<ISettingsLocalizationHelper>();
        localization.Translate("settings.ai.ollamaInvalidBaseUrl", Arg.Any<string>()).Returns("base url required");
        var sut = new OllamaProviderProbeOrchestrator(probe, localization);

        var outcome = await sut.ProbeAsync("   ", TestContext.Current.CancellationToken);

        Assert.Equal(OllamaProbeOutcomeKind.InvalidBaseUrl, outcome.Kind);
        Assert.Equal("base url required", outcome.Message);
        await probe.DidNotReceive().TestConnectionAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailure_WhenConnectionTestFails()
    {
        var probe = Substitute.For<IOllamaProbeService>();
        probe.TestConnectionAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaConnectionResult(IsSuccess: false, Message: "refused"));
        var sut = new OllamaProviderProbeOrchestrator(probe, Substitute.For<ISettingsLocalizationHelper>());

        var outcome = await sut.ProbeAsync("http://localhost:11434", TestContext.Current.CancellationToken);

        Assert.Equal(OllamaProbeOutcomeKind.Failure, outcome.Kind);
        Assert.Equal("refused", outcome.Message);
        Assert.Empty(outcome.Models);
        await probe.DidNotReceive().GetModelCatalogAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProbeAsync_ReturnsSuccess_WithCatalog_WhenConnectionSucceeds()
    {
        var probe = Substitute.For<IOllamaProbeService>();
        probe.TestConnectionAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaConnectionResult(IsSuccess: true, Message: "ok", ModelCount: 2));
        probe.GetModelCatalogAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaModelCatalogResult(new List<OllamaModelInfo>
            {
                new("gemma3:4b", 4_000_000_000, null, null),
                new("qwen3:4b", 4_500_000_000, null, null),
            }));
        var sut = new OllamaProviderProbeOrchestrator(probe, Substitute.For<ISettingsLocalizationHelper>());

        var outcome = await sut.ProbeAsync(" http://localhost:11434 ", TestContext.Current.CancellationToken);

        Assert.Equal(OllamaProbeOutcomeKind.Success, outcome.Kind);
        Assert.Equal("ok", outcome.Message);
        Assert.Equal(2, outcome.Models.Count);
    }
}
