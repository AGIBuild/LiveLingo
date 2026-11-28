using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.LocalHost.Providers;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// Real-machine probe verifying auto-discovery, suitability ranking, and <c>reasoning: "off"</c> streaming chat against
/// a running local model host (Bionic, LM Studio, or Ollama). Skipped unless <c>LIVELINGO_ENABLE_LOCALHOST_PROBE=1</c>.
/// </summary>
public sealed class LocalHostProbeTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_LOCALHOST_PROBE") == "1";

    [Fact]
    public async Task DiscoverAndRunLiveLocalHost_SucceedsWithoutReasoningLeak()
    {
        if (!Enabled)
            return;

        var ct = CancellationToken.None;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var registry = new LocalHostRegistry(
            [new LmStudioFamilyProvider(http), new OllamaHostProvider(http), new GenericOpenAiLocalProvider(http)]);

        var targets = new[]
        {
            new LocalHostProbeTarget("bionic", LocalHostKind.Bionic, "Bionic", new Uri("http://127.0.0.1:1235")),
            new LocalHostProbeTarget("lmstudio", LocalHostKind.LmStudio, "LM Studio", new Uri("http://127.0.0.1:1234")),
            new LocalHostProbeTarget("ollama", LocalHostKind.Ollama, "Ollama", new Uri("http://127.0.0.1:11434"))
        };

        var hosts = await registry.DiscoverAsync(targets, ct);
        var running = hosts.FirstOrDefault(h => h.Status == LocalHostStatus.Running);
        Assert.NotNull(running);
        output.WriteLine($"Discovered host: {running!.DisplayName} ({running.Kind}, {running.ApiTier}) at {running.BaseUri}, {running.Models.Count} raw LLM(s)");

        var evaluated = LocalHostModelMatcher.Evaluate(ModelStage.Cleaner, running.Models);
        Assert.NotEmpty(evaluated);
        foreach (var item in evaluated)
            output.WriteLine($"  - {item.Model.Key} [{item.Suitability}] loaded={item.Model.IsLoaded} engine={item.Model.LoadedEngine} parallel={item.Model.ParallelSlots} reasoningOff={item.Model.Reasoning?.SupportsOff}");

        var top = evaluated[0];
        var route = LocalHostRoute.Create(
            running.HostId,
            running.DisplayName,
            running.Kind,
            running.BaseUri!.ToString(),
            top.Model.Key,
            running.ApiTier,
            reasoningOffSupported: top.Model.Reasoning?.SupportsOff == true,
            isLoaded: top.Model.IsLoaded,
            parallelSlots: top.Model.ParallelSlots);

        var client = registry.CreateChatClient(route, http);
        var streamed = new List<string>();
        var completion = await client.CompleteAsync(
            new ChatRequest(
                "目标：呃那个就是把，把超时设成三十秒吧。",
                MaxTokens: 96,
                Temperature: 0,
                Extra: new Dictionary<string, object>(),
                System: "把口述整理成通顺的一句话，删除口头语和重复，只输出整理后的句子。",
                OnDelta: streamed.Add),
            ct);

        output.WriteLine($"Completion ({completion.FirstVisibleMs:0} ms TTFT / {completion.TotalMs:0} ms total, {completion.OutputTokens} tokens): {completion.Text}");
        Assert.False(string.IsNullOrWhiteSpace(completion.Text));
        Assert.DoesNotContain("<think>", completion.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(streamed);
    }
}
