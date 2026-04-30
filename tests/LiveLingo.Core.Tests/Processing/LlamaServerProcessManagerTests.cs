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

    [Theory]
    [InlineData("llama_model_loader: Dumping metadata keys/values. Note: KV overrides do not apply in this output.", false)]
    [InlineData("load: control-looking token: 212 '</s>' was not control-type; its type will be overridden", false)]
    [InlineData("srv init: failed to bind socket", true)]
    [InlineData("error: failed to load model", true)]
    public void IsServerErrorLog_avoids_substring_false_positives(string logLine, bool expected)
    {
        Assert.Equal(expected, LlamaServerProcessManager.IsServerErrorLog(logLine));
    }
}
