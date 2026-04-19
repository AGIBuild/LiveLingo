using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.ViewModels.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class SettingsPersistenceCoordinatorTests
{
    [Fact]
    public async Task PersistAsync_SkipsMigration_WhenStoragePathUnchanged()
    {
        var settingsService = Substitute.For<ISettingsService>();
        var modelManager = Substitute.For<IModelManager>();
        var sut = BuildCoordinator(settingsService, modelManager);

        var workingCopy = SettingsModel.CreateDefault();
        workingCopy.Advanced.ModelStoragePath = "/tmp/models";
        var beforeSave = workingCopy.DeepClone();

        var ct = TestContext.Current.CancellationToken;
        var outcome = await sut.PersistAsync(
            new SettingsPersistenceRequest(workingCopy, "/tmp/models", beforeSave),
            ct);

        Assert.True(outcome.MigrationSucceeded);
        Assert.Null(outcome.MigrationErrorMessage);
        Assert.Equal("/tmp/models", outcome.UpdatedOriginalModelStoragePath);
        await modelManager.DidNotReceive().MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistAsync_RunsMigration_WhenStoragePathChanged()
    {
        var settingsService = Substitute.For<ISettingsService>();
        var modelManager = Substitute.For<IModelManager>();
        modelManager.MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sut = BuildCoordinator(settingsService, modelManager);

        var workingCopy = SettingsModel.CreateDefault();
        workingCopy.Advanced.ModelStoragePath = "/new/location";
        var beforeSave = workingCopy.DeepClone();

        var ct = TestContext.Current.CancellationToken;
        var outcome = await sut.PersistAsync(
            new SettingsPersistenceRequest(workingCopy, "/old/location", beforeSave),
            ct);

        Assert.True(outcome.MigrationSucceeded);
        Assert.Equal("/new/location", outcome.UpdatedOriginalModelStoragePath);
        await modelManager.Received(1).MigrateStoragePathAsync("/new/location", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistAsync_ReturnsMigrationFailure_OnMigrateException()
    {
        var settingsService = Substitute.For<ISettingsService>();
        var modelManager = Substitute.For<IModelManager>();
        modelManager.MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("disk full"));

        var localization = Substitute.For<ISettingsLocalizationHelper>();
        localization
            .Translate("settings.advanced.migrationFailed", Arg.Any<string>(), Arg.Any<object>())
            .Returns("Migration failed: disk full");

        var sut = new SettingsPersistenceCoordinator(
            settingsService, modelManager,
            coreOptions: null, llmCoordinator: null, secretStore: new InMemorySecretStore(),
            localization, logger: null);

        var workingCopy = SettingsModel.CreateDefault();
        workingCopy.Advanced.ModelStoragePath = "/new/location";
        var beforeSave = workingCopy.DeepClone();

        var ct = TestContext.Current.CancellationToken;
        var outcome = await sut.PersistAsync(
            new SettingsPersistenceRequest(workingCopy, "/old/location", beforeSave),
            ct);

        Assert.False(outcome.MigrationSucceeded);
        Assert.Equal("Migration failed: disk full", outcome.MigrationErrorMessage);
        settingsService.DidNotReceive().Replace(Arg.Any<SettingsModel>());
    }

    [Fact]
    public async Task PersistAsync_CallsReplace_AndSyncsCoreOptions()
    {
        var settingsService = Substitute.For<ISettingsService>();
        var modelManager = Substitute.For<IModelManager>();
        var coreOptions = new CoreOptions();
        var sut = new SettingsPersistenceCoordinator(
            settingsService, modelManager, coreOptions,
            llmCoordinator: null, secretStore: new InMemorySecretStore(),
            Substitute.For<ISettingsLocalizationHelper>(),
            logger: null);

        var workingCopy = SettingsModel.CreateDefault();
        var beforeSave = workingCopy.DeepClone();

        var ct = TestContext.Current.CancellationToken;
        var outcome = await sut.PersistAsync(
            new SettingsPersistenceRequest(workingCopy, OriginalModelStoragePath: null, beforeSave),
            ct);

        Assert.True(outcome.MigrationSucceeded);
        settingsService.Received(1).Replace(workingCopy);
    }

    [Fact]
    public async Task PersistAsync_TriggersLlmRetry_WhenTranslationModelChanged()
    {
        var settingsService = Substitute.For<ISettingsService>();
        var modelManager = Substitute.For<IModelManager>();
        var llm = Substitute.For<ILlmModelLoadCoordinator>();
        var sut = new SettingsPersistenceCoordinator(
            settingsService, modelManager, coreOptions: null,
            llmCoordinator: llm, secretStore: new InMemorySecretStore(),
            Substitute.For<ISettingsLocalizationHelper>(), logger: null);

        var beforeSave = SettingsModel.CreateDefault();
        beforeSave.Translation.ActiveTranslationModelId = "opus-mt-en-zh";

        var workingCopy = beforeSave.DeepClone();
        workingCopy.Translation.ActiveTranslationModelId = "opus-mt-zh-en";

        var ct = TestContext.Current.CancellationToken;
        await sut.PersistAsync(
            new SettingsPersistenceRequest(workingCopy, OriginalModelStoragePath: null, beforeSave),
            ct);

        await llm.Received(1).RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistAsync_DoesNotCallLlmRetry_WhenNoChange()
    {
        var settingsService = Substitute.For<ISettingsService>();
        var modelManager = Substitute.For<IModelManager>();
        var llm = Substitute.For<ILlmModelLoadCoordinator>();
        var sut = new SettingsPersistenceCoordinator(
            settingsService, modelManager, coreOptions: null,
            llmCoordinator: llm, secretStore: new InMemorySecretStore(),
            Substitute.For<ISettingsLocalizationHelper>(), logger: null);

        var beforeSave = SettingsModel.CreateDefault();
        var workingCopy = beforeSave.DeepClone();

        var ct = TestContext.Current.CancellationToken;
        await sut.PersistAsync(
            new SettingsPersistenceRequest(workingCopy, OriginalModelStoragePath: null, beforeSave),
            ct);

        await llm.DidNotReceive().RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>());
    }

    private static SettingsPersistenceCoordinator BuildCoordinator(
        ISettingsService settingsService, IModelManager modelManager) =>
        new(
            settingsService,
            modelManager,
            coreOptions: null,
            llmCoordinator: null,
            secretStore: new InMemorySecretStore(),
            Substitute.For<ISettingsLocalizationHelper>(),
            logger: null);
}
