using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Core.Inference;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// End-to-end probe: installs the pinned runtime and translation model (SHA256-verified), starts llama-server and
/// translates real text. Skipped unless LIVELINGO_ENABLE_TRANSLATION_PROBE=1 (see the ProbeTranslation Nuke target).
/// </summary>
public class TranslationProbeTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_TRANSLATION_PROBE") == "1";

    [Fact]
    public async Task FastQualityFast_ReallyChangesTheLoadedModelAndProviderIdentity()
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_MODEL_SWITCH_PROBE") != "1")
            return;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        await using var provider = services.BuildServiceProvider();
        var models = provider.GetRequiredService<IModelManager>();
        Assert.All(new[] { ModelCatalog.TranslatorFast, ModelCatalog.TranslatorQuality, ModelCatalog.LlamaRuntime },
            model => Assert.True(models.HasAllExpectedLocalAssets(model), $"Install {model.Id} before this probe."));
        var options = provider.GetRequiredService<CoreOptions>();
        var usage = provider.GetRequiredService<ModelUsage>();
        var used = new List<string>();
        usage.Changed += model => { if (model.Confirmed) used.Add(model.Name); };
        var client = new LocalChatClient(provider.GetRequiredService<LlamaServerHost>(), InferenceRole.Translator,
            () => InferenceModels.For(InferenceRole.Translator, options.TranslatorTier), usage);
        foreach (var tier in new[] { TranslatorTier.Fast, TranslatorTier.Quality, TranslatorTier.Fast })
        {
            options.TranslatorTier = tier;
            var completion = await client.CompleteAsync(new ChatRequest(
                "将以下文本翻译成英文：这个 PR 先不要合并。", 200, 0, new Dictionary<string, object>()),
                CancellationToken.None);
            output.WriteLine($"{tier}: provider={completion.Model}; text={completion.Text}");
            Assert.Contains(tier == TranslatorTier.Fast ? "1.8B" : "7B", completion.Model, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(completion.Text));
        }
        Assert.Equal(new[]
        {
            ModelCatalog.TranslatorFast.DisplayName, ModelCatalog.TranslatorQuality.DisplayName, ModelCatalog.TranslatorFast.DisplayName
        }, used);
    }

    [Fact]
    public async Task PinnedRuntimeAndModel_TranslateRealText()
    {
        if (!Enabled)
            return;

        var storage = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(options =>
        {
            if (!string.IsNullOrWhiteSpace(storage))
                options.ModelStoragePath = storage;
        });
        await using var provider = services.BuildServiceProvider();
        var models = provider.GetRequiredService<IModelManager>();

        var lastReported = -1L;
        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            var percent = p.TotalBytes > 0 ? p.BytesDownloaded * 100 / p.TotalBytes : 0;
            if (percent / 10 != lastReported / 10)
                output.WriteLine($"{p.ModelId}: {percent}%");
            lastReported = percent;
        });
        await models.EnsureModelAsync(ModelCatalog.LlamaRuntime, progress);
        await models.EnsureModelAsync(ModelCatalog.TranslatorFast, progress);

        var translator = provider.GetRequiredService<ITranslator>();
        var plain = await translator.TranslateAsync(
            new TranslationRequest("这个 PR 先不要合并，等 CI 跑完。", TargetLanguage.English, []), CancellationToken.None);
        output.WriteLine($"plain: {plain.Text} (first {plain.FirstVisibleMs:0} ms, total {plain.TotalMs:0} ms)");
        Assert.Contains("merge", plain.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__KEEP_", plain.Text);

        var literal = await translator.TranslateAsync(
            new TranslationRequest("请保留 `CancellationToken` 这个参数。", TargetLanguage.English, []), CancellationToken.None);
        output.WriteLine($"literal: {literal.Text}");
        Assert.Contains("`CancellationToken`", literal.Text);

        var back = await translator.TranslateAsync(
            new TranslationRequest("Please do not retry until the cache is warm.", TargetLanguage.Chinese, []), CancellationToken.None);
        output.WriteLine($"zh: {back.Text}");
        Assert.Matches("[\u4e00-\u9fff]", back.Text);
    }
}
