using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Cloud;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.ViewModels.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class CloudProviderProbeOrchestratorTests
{
    [Fact]
    public void BuildOutcomeFromCachedSnapshot_ReturnsEmpty_WhenSnapshotIsUnknown()
    {
        var runtime = Substitute.For<ICloudProviderRuntimeState>();
        runtime.Current.Returns(CloudProviderRuntimeSnapshot.Unknown);
        var sut = new CloudProviderProbeOrchestrator(runtime, localization: null);

        var outcome = sut.BuildOutcomeFromCachedSnapshot(BuildSettings(enabled: false));

        Assert.Empty(outcome.Models);
    }

    [Fact]
    public void BuildOutcomeFromCachedSnapshot_ReturnsEmpty_WhenSnapshotMismatchPreferences()
    {
        var settings = BuildSettings();
        var staleSnapshot = MakeSnapshot("stale-fingerprint", []);
        var runtime = Substitute.For<ICloudProviderRuntimeState>();
        runtime.Current.Returns(staleSnapshot);
        var sut = new CloudProviderProbeOrchestrator(runtime, localization: null);

        var outcome = sut.BuildOutcomeFromCachedSnapshot(settings);

        Assert.Empty(outcome.Models);
        Assert.Null(outcome.StatusMessage);
    }

    [Fact]
    public async Task RefreshAsync_PersistsSnapshotAndProjectsModels_WhenMatching()
    {
        var settings = BuildSettings();
        var fingerprint = CloudProviderConfigurationFingerprint.Create(
            CoreOptionsSync.CreateCloudModelPreferences(settings));
        var snapshot = new CloudProviderRuntimeSnapshot(
            fingerprint,
            CloudProviderRuntimeStatus.Healthy,
            CloudProviderValidationMode.ModelCatalog,
            "ok",
            DateTimeOffset.UtcNow,
            null,
            new List<CloudProviderModelInfo>
            {
                new("gpt-4.1-mini", "openai"),
                new("gpt-4.1-nano", "openai"),
            });

        var runtime = Substitute.For<ICloudProviderRuntimeState>();
        runtime.RefreshAsync(Arg.Any<CloudModelPreferences>(), Arg.Any<CancellationToken>())
            .Returns(snapshot);
        var sut = new CloudProviderProbeOrchestrator(runtime, localization: null);

        var outcome = await sut.RefreshAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Models.Count);
        Assert.Equal("gpt-4.1-mini", outcome.Models[0].Id);
        Assert.Equal("openai", outcome.Models[0].OwnedBy);
    }

    private static SettingsModel BuildSettings(bool enabled = true)
    {
        var settings = SettingsModel.CreateDefault();
        settings.Translation.CloudProvider = new CloudProviderSettings
        {
            Enabled = enabled,
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-test",
            TranslationModelId = "gpt-4.1-mini",
            PostProcessingModelId = "gpt-4.1-nano",
            PresetId = "OpenAI",
            ProviderType = "OpenAICompatible",
        };
        return settings;
    }

    private static CloudProviderRuntimeSnapshot MakeSnapshot(
        string fingerprint, IReadOnlyList<CloudProviderModelInfo> models) =>
        new(fingerprint,
            CloudProviderRuntimeStatus.Healthy,
            CloudProviderValidationMode.ModelCatalog,
            null,
            DateTimeOffset.UtcNow,
            null,
            models);
}
