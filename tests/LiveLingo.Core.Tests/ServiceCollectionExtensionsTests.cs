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
