using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
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
                        args.Outcome.Result is
                        {
                            IsSuccessStatusCode: false, StatusCode: System.Net.HttpStatusCode.RequestTimeout
                            or System.Net.HttpStatusCode.BadGateway
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.GatewayTimeout
                        })
                });
                pipeline.AddTimeout(TimeSpan.FromMinutes(3));
            });
        services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<ModelManager>());
        services.AddSingleton<IModelDownloadCoordinator, InProcessModelDownloadCoordinator>();

        services.AddSingleton<IProcessInspector, SystemProcessInspector>();
        services.AddSingleton(sp => new ServerPidRegistry(
            sp.GetRequiredService<CoreOptions>().RuntimeStatePath,
            sp.GetRequiredService<IProcessInspector>(),
            sp.GetRequiredService<ILogger<ServerPidRegistry>>()));
        services.AddSingleton<ILlamaProcessLauncher, LlamaProcessLauncher>();
        services.AddSingleton<IServerHealthProbe, HttpHealthProbe>();
        services.AddSingleton<IPortAllocator, LoopbackPortAllocator>();
        services.AddSingleton(LlamaServerHostOptions.Default);
        services.AddSingleton(sp => new LlamaServerHost(
            sp.GetRequiredService<IModelManager>(),
            ModelCatalog.LlamaRuntime,
            sp.GetRequiredService<ILlamaProcessLauncher>(),
            sp.GetRequiredService<IServerHealthProbe>(),
            sp.GetRequiredService<IPortAllocator>(),
            sp.GetRequiredService<CoreOptions>(),
            sp.GetRequiredService<LlamaServerHostOptions>(),
            TimeProvider.System,
            sp.GetRequiredService<ILogger<LlamaServerHost>>()));
        services.AddSingleton<ITextCleaner>(sp => new LlmTextCleaner(new LocalChatClient(
            sp.GetRequiredService<LlamaServerHost>(),
            InferenceRole.Cleaner,
            () => ModelCatalog.Cleaner)));
        services.AddSingleton<ITranslator>(sp => new LlmTranslator(new LocalChatClient(
            sp.GetRequiredService<LlamaServerHost>(),
            InferenceRole.Translator,
            () => ModelCatalog.Translator(sp.GetRequiredService<CoreOptions>().TranslatorTier))));

        return services;
    }
}
