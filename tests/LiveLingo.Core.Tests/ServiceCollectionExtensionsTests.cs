using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddLiveLingoCore_RegistersModelServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        await using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IModelManager>());
        Assert.NotNull(sp.GetService<IModelDownloadCoordinator>());
        Assert.NotNull(sp.GetService<LlamaServerHost>());
        Assert.IsType<LlmTranslator>(sp.GetService<ITranslator>());
        Assert.IsType<LlmTextCleaner>(sp.GetService<ITextCleaner>());
        Assert.NotNull(sp.GetService<IDictationSessionFactory>());
        Assert.NotNull(sp.GetService<IInferenceWarmup>());
    }

    [Fact]
    public async Task TheSpeechModels_HaveOneOwner_ForDictationImportAndPreparation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        await using var sp = services.BuildServiceProvider();

        var engine = sp.GetRequiredService<LiveLingo.Core.Speech.Native.SherpaSpeechEngine>();
        Assert.Same(engine, sp.GetRequiredService<IDictationSessionFactory>());
        Assert.Same(engine, sp.GetRequiredService<ISpeechWarmup>());
        Assert.Same(engine, sp.GetRequiredService<LiveLingo.Core.Transcription.IAudioFileTranscriber>());
    }

    [Fact]
    public async Task AddLiveLingoCore_AppliesConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(o => o.ModelStoragePath = "/tmp/models");
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("/tmp/models", sp.GetRequiredService<IOptions<CoreOptions>>().Value.ModelStoragePath);
        Assert.Same(sp.GetRequiredService<CoreOptions>(), sp.GetRequiredService<IOptions<CoreOptions>>().Value);
    }
}
