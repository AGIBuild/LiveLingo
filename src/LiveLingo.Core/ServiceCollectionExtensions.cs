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
        var coreOptions = new CoreOptions();
        configure?.Invoke(coreOptions);
        services.AddSingleton(coreOptions);
        services.AddSingleton<IOptions<CoreOptions>>(_ => Options.Create(coreOptions));

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

        services.AddHttpClient<NativeRuntimeUpdater>()
            .AddResilienceHandler("native-runtime-download", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(2),
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or IOException or TaskCanceledException ||
                        args.Outcome.Result is { IsSuccessStatusCode: false, StatusCode: System.Net.HttpStatusCode.RequestTimeout
                            or System.Net.HttpStatusCode.BadGateway
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.GatewayTimeout })
                });
                pipeline.AddTimeout(TimeSpan.FromMinutes(3));
            });
        services.AddSingleton<INativeRuntimeUpdater>(sp => sp.GetRequiredService<NativeRuntimeUpdater>());
        services.AddSingleton<ILlamaServerProcessManager, LlamaServerProcessManager>();
        services.AddSingleton<QwenModelHost>();
        services.AddSingleton<ILlmModelLoadCoordinator>(sp => sp.GetRequiredService<QwenModelHost>());

        services.AddHttpClient<ITranslationEngine, LlamaTranslationEngine>();
        services.AddHttpClient<ITextProcessor, SummarizeProcessor>();
        services.AddHttpClient<ITextProcessor, OptimizeProcessor>();
        services.AddHttpClient<ITextProcessor, ColloquializeProcessor>();

        services.AddSingleton<ISpeechToTextEngine, WhisperSpeechToTextEngine>();
        services.AddSingleton<IVoiceActivityDetector, SileroVadDetector>();

        return services;
    }
}
