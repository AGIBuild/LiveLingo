using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiveLingoCore(
        this IServiceCollection services,
        Action<CoreOptions>? configure = null)
    {
        var options = new CoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(Options.Create(options));

        services.AddSingleton<ITranslationPipeline, TranslationPipeline>();
        services.AddSingleton<ILanguageDetector, ScriptBasedDetector>();

        services.AddHttpClient<ModelManager>();
        services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<ModelManager>());

        services.AddSingleton<QwenModelHost>();
        services.AddSingleton<ITranslationEngine, MarianOnnxEngine>();
        services.AddSingleton<ITextProcessor, SummarizeProcessor>();
        services.AddSingleton<ITextProcessor, OptimizeProcessor>();
        services.AddSingleton<ITextProcessor, ColloquializeProcessor>();

        return services;
    }
}
