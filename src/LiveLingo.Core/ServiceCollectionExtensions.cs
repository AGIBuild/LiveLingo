using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Speech;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

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

        services.AddHttpClient<ModelManager>()
            .AddResilienceHandler("model-download", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(2),
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or IOException ||
                        args.Outcome.Result is { IsSuccessStatusCode: false, StatusCode: System.Net.HttpStatusCode.RequestTimeout
                            or System.Net.HttpStatusCode.BadGateway
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.GatewayTimeout })
                });
                pipeline.AddTimeout(TimeSpan.FromMinutes(3));
            });
        services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<ModelManager>());
        services.AddSingleton<IModelReadinessService, ModelReadinessService>();

        services.AddSingleton<QwenModelHost>();
        services.AddSingleton<ITranslationEngine, LlamaTranslationEngine>();
        services.AddSingleton<ITextProcessor, SummarizeProcessor>();
        services.AddSingleton<ITextProcessor, OptimizeProcessor>();
        services.AddSingleton<ITextProcessor, ColloquializeProcessor>();

        services.AddSingleton<ISpeechToTextEngine, WhisperSpeechToTextEngine>();
        services.AddSingleton<IVoiceActivityDetector, SileroVadDetector>();

        return services;
    }
}
