using LiveLingo.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLiveLingoCore_RegistersModelServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IModelManager>());
        Assert.NotNull(sp.GetService<IModelDownloadCoordinator>());
    }

    [Fact]
    public void AddLiveLingoCore_AppliesConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(o => o.ModelStoragePath = "/tmp/models");
        using var sp = services.BuildServiceProvider();

        Assert.Equal("/tmp/models", sp.GetRequiredService<IOptions<CoreOptions>>().Value.ModelStoragePath);
        Assert.Same(sp.GetRequiredService<CoreOptions>(), sp.GetRequiredService<IOptions<CoreOptions>>().Value);
    }
}
