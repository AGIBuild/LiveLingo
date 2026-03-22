using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLiveLingoCore_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<ITranslationPipeline>());
        Assert.NotNull(sp.GetService<IModelManager>());
        Assert.NotNull(sp.GetService<IModelReadinessService>());
        Assert.NotNull(sp.GetService<ITranslationEngine>());
        Assert.NotNull(sp.GetService<ILanguageDetector>());
        Assert.IsType<QwenModelHost>(sp.GetRequiredService<ILlmModelLoadCoordinator>());
    }

    [Fact]
    public void AddLiveLingoCore_AcceptsConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(o =>
        {
            o.DefaultTargetLanguage = "ja";
            o.ModelStoragePath = "/tmp/models";
        });
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<CoreOptions>>().Value;
        Assert.Equal("ja", opts.DefaultTargetLanguage);
        Assert.Equal("/tmp/models", opts.ModelStoragePath);
    }

    [Fact]
    public void AddLiveLingoCore_CoreOptionsSingletonMatchesIOptionsValue()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(o => o.HuggingFaceToken = "t0");
        var sp = services.BuildServiceProvider();

        var direct = sp.GetRequiredService<CoreOptions>();
        var wrapped = sp.GetRequiredService<IOptions<CoreOptions>>().Value;
        Assert.Same(direct, wrapped);
        direct.HuggingFaceToken = "t1";
        Assert.Equal("t1", wrapped.HuggingFaceToken);
    }

    [Fact]
    public void AddLiveLingoCore_UsesScriptBasedDetector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        var sp = services.BuildServiceProvider();

        Assert.IsType<ScriptBasedDetector>(sp.GetService<ILanguageDetector>());
    }

    [Fact]
    public void AddLiveLingoCore_RegistersModelManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        var sp = services.BuildServiceProvider();

        Assert.IsType<ModelManager>(sp.GetService<IModelManager>());
    }

    [Fact]
    public void AddLiveLingoCore_UsesLlamaAsTranslationEngine()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        var sp = services.BuildServiceProvider();

        Assert.IsType<LlamaTranslationEngine>(sp.GetService<ITranslationEngine>());
    }

    [Fact]
    public void AddLiveLingoCore_PipelineIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        var sp = services.BuildServiceProvider();

        var p1 = sp.GetService<ITranslationPipeline>();
        var p2 = sp.GetService<ITranslationPipeline>();
        Assert.Same(p1, p2);
    }
}
