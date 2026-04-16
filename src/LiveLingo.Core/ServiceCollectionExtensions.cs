using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Speech;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

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
        services.AddSingleton<IModelCatalog, StaticModelCatalog>();
        services.AddSingleton<ICloudProviderRuntimeState, NullCloudProviderRuntimeState>();
        services.AddSingleton<IModelSelector, DefaultModelSelector>();
        services.AddSingleton<IModelReadinessService, ModelReadinessService>();

        services.AddHttpClient<NativeRuntimeUpdater>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10); // Llama.cpp binaries can be quite large
            })
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
                pipeline.AddTimeout(TimeSpan.FromMinutes(10));
            });
        services.AddSingleton<INativeRuntimeUpdater>(sp => sp.GetRequiredService<NativeRuntimeUpdater>());
        services.AddSingleton<ILlamaServerProcessManager, LlamaServerProcessManager>();
        services.AddSingleton<LocalLlamaModelHost>();
        services.AddSingleton<ILlmModelLoadCoordinator>(sp => sp.GetRequiredService<LocalLlamaModelHost>());
        services.AddSingleton<ILocalModelRuntimeState>(sp => sp.GetRequiredService<LocalLlamaModelHost>());
        services.AddSingleton<IModelRuntime>(sp => new LlamaServerRuntime(sp.GetRequiredService<LocalLlamaModelHost>()));
        services.AddSingleton<IModelRuntime, RemoteHttpRuntime>();
        // Translation inference HttpClients get resilience:
        //   - Local llama-server: 1 retry (transient server hiccup), 120 s per-request timeout
        //   - Cloud provider:     2 retries with exponential back-off, 60 s per-request timeout
        services.AddHttpClient<LlamaServerChatProvider>()
            .AddResilienceHandler("llama-translation", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = TimeSpan.FromMilliseconds(500),
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or IOException or TaskCanceledException ||
                        args.Outcome.Result is { IsSuccessStatusCode: false,
                            StatusCode: System.Net.HttpStatusCode.ServiceUnavailable
                                or System.Net.HttpStatusCode.GatewayTimeout })
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(120));
            });
        services.AddHttpClient<OpenAICompatibleChatProvider>()
            .AddResilienceHandler("cloud-translation", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(500),
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or IOException or TaskCanceledException or TimeoutRejectedException ||
                        args.Outcome.Result is { IsSuccessStatusCode: false,
                            StatusCode: System.Net.HttpStatusCode.RequestTimeout
                                or System.Net.HttpStatusCode.TooManyRequests
                                or System.Net.HttpStatusCode.BadGateway
                                or System.Net.HttpStatusCode.ServiceUnavailable
                                or System.Net.HttpStatusCode.GatewayTimeout })
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(60));
            });
        services.AddHttpClient<OpenAICompatibleProbeService>()
            .AddResilienceHandler("cloud-provider-probe", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(500),
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or IOException or TaskCanceledException or TimeoutRejectedException ||
                        args.Outcome.Result is
                        {
                            IsSuccessStatusCode: false,
                            StatusCode: System.Net.HttpStatusCode.RequestTimeout
                                or System.Net.HttpStatusCode.TooManyRequests
                                or System.Net.HttpStatusCode.BadGateway
                                or System.Net.HttpStatusCode.ServiceUnavailable
                                or System.Net.HttpStatusCode.GatewayTimeout
                        })
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(15));
            });
        services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<LlamaServerChatProvider>());
        services.AddSingleton<IModelProvider>(sp => sp.GetRequiredService<OpenAICompatibleChatProvider>());
        services.AddSingleton<ICloudProviderProbeService>(sp => sp.GetRequiredService<OpenAICompatibleProbeService>());
        services.AddSingleton<IModelInvocationService, DefaultModelInvocationService>();

        // MEA IChatClient pipeline:
        //   LoggingChatClient → DistributedCachingChatClient → TranslationChatClient
        // Same source-text + language-pair requests return from cache without hitting the model.
        services.AddDistributedMemoryCache();
        services.AddChatClient(sp => new TranslationChatClient(
                sp.GetRequiredService<IModelSelector>(),
                sp.GetRequiredService<IModelInvocationService>(),
                sp.GetService<ILogger<TranslationChatClient>>()))
            .UseDistributedCache()
            .UseLogging();

        services.AddSingleton<ITranslationEngine, LlamaTranslationEngine>();
        services.AddSingleton<ITextProcessor, SummarizeProcessor>();
        services.AddSingleton<ITextProcessor, OptimizeProcessor>();
        services.AddSingleton<ITextProcessor, ColloquializeProcessor>();

        services.AddSingleton<ISpeechToTextEngine, WhisperSpeechToTextEngine>();
        services.AddSingleton<IVoiceActivityDetector, SileroVadDetector>();

        return services;
    }
}
