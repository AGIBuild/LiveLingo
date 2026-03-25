using LiveLingo.Core.Processing;

namespace LiveLingo.Core.Tests.Processing;

public sealed class LlamaServerProcessManagerTests
{
    [Fact]
    public void BuildArguments_includes_reasoning_flags()
    {
        var args = LlamaServerProcessManager.BuildArguments("/tmp/model.gguf", 4096, 6, 50123);

        Assert.Contains("-m \"/tmp/model.gguf\"", args);
        Assert.Contains("-c 4096", args);
        Assert.Contains("--port 50123", args);
        Assert.Contains("--threads 6", args);
        Assert.Contains("--reasoning-format none", args);
        Assert.Contains("--reasoning off", args);
    }
}
