using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Core.LanguageDetection;
using Microsoft.Extensions.DependencyInjection;

namespace LiveLingo.Desktop.Tests.SmokeTests;

public class DiContainerSmokeTests
{
    private ServiceProvider BuildAppServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        services.AddSingleton<ISettingsService>(
            new JsonSettingsService(Path.Combine(Path.GetTempPath(), $"livelingo-smoke-{Guid.NewGuid()}.json")));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AllCoreServices_CanBeResolved()
    {
        using var provider = BuildAppServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ITranslationPipeline>());
        Assert.NotNull(provider.GetRequiredService<IModelManager>());
        Assert.NotNull(provider.GetRequiredService<ILanguageDetector>());
        Assert.NotNull(provider.GetRequiredService<ISettingsService>());
    }

    [Fact]
    public void TranslationPipeline_IsSingleton()
    {
        using var provider = BuildAppServiceProvider();

        var a = provider.GetRequiredService<ITranslationPipeline>();
        var b = provider.GetRequiredService<ITranslationPipeline>();
        Assert.Same(a, b);
    }

    [Fact]
    public void ModelManager_IsSingleton()
    {
        using var provider = BuildAppServiceProvider();

        var a = provider.GetRequiredService<IModelManager>();
        var b = provider.GetRequiredService<IModelManager>();
        Assert.Same(a, b);
    }

    [Fact]
    public async Task SettingsService_LoadsWithoutSettingsFile()
    {
        using var provider = BuildAppServiceProvider();

        var settings = provider.GetRequiredService<ISettingsService>();
        await settings.LoadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(settings.Current);
        Assert.NotNull(settings.Current.Hotkeys);
        Assert.NotNull(settings.Current.Translation);
    }
}
