using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Dictation;
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
    public Task DocumentClean_RepairsFragmentedChineseWithoutChangingSource() =>
        RunDocumentProbe(OutputKind.Clean, TargetLanguage.Chinese);

    [Theory]
    [InlineData(TargetLanguage.Chinese)]
    [InlineData(TargetLanguage.English)]
    public Task DocumentTranslation_IsFaithfulOrRequiresExplicitCleanChoice(TargetLanguage target) =>
        RunDocumentProbe(OutputKind.Translation, target);

    [Theory]
    [InlineData(OutputKind.Clean)]
    [InlineData(OutputKind.Translation)]
    public Task DocumentScreenshot_ExplicitCleanChoicePreservesRepairRequirements(OutputKind kind) =>
        RunDocumentProbe(kind, TargetLanguage.Chinese, screenshot: true);

    private sealed class ObservedClient(IChatCompletionClient inner) : IChatCompletionClient
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public List<ChatCompletion> Responses { get; } = [];
        public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            var response = await inner.CompleteAsync(request, ct);
            Responses.Add(response);
            return response;
        }
    }

    private static void AssertScreenshotRequirements(string text)
    {
        (string Label, string Pattern, bool Required)[] checks =
        [
            ("no filler", "呃", false),
            ("no dangling alternative", @"要么\s*[。！？!?]", false),
            ("initial question", @"^[^？?]*(最好|最佳)[^？?]*(修复|解决)[^？?]*[？?]", true),
            ("automatic repair risk", @"自动修复[^。！？?]{0,40}(出错|错误|失败|风险)", true),
            ("no invented certainty", @"自动修复(很容易|必然|一定|肯定)", false),
            ("manual repair capability", @"(提供|允许|支持|让)[^。！？?]{0,45}(手动|人工)修复", true),
            // Discovery during checking must precede the tool-mediated reminder.
            ("state-check discovery then tool reminder", @"(状态检查|检查执行状态|检查状态)[^。！？?]{0,30}(发现|检测)[^。！？?]{0,20}(时|后|随后|然后|再)[^。！？?]{0,20}Ask[^。！？?]{0,15}Questions[^。！？?]{0,30}(提醒|提示|通知)用户", true),
            ("uncertain source UI name preserved", "无忧页面", true),
            // “在哪里” is a factual equivalent of “什么地方”; explicit uncertainty is still mandatory.
            ("uncertain placement", @"(不确定|不清楚|没想好|尚未确定|尚不确定)[^。！？?]{0,45}(地方|位置|环节|哪里|何处|地点)", true),
            ("request to find placement", @"(帮|请|你|找|选|确定)[^。！？?]{0,25}合适[^。！？?]{0,10}(地方|位置|环节|地点)", true),
            ("user-friendly goal", @"用户[^。！？?]{0,8}友好", true),
            ("actual repair goal", @"(真正|切实|实际|彻底)[^。！？?]{0,10}(修复|解决)[^。！？?]{0,10}问题", true),
            ("negated notification-only requirement", @"(而不是|而非|不能|不应|不要|不仅|不只是|并非|而不只是)[^。！？?]{0,20}(提示|提醒|通知)", true)
        ];
        var failures = checks.Where(c => System.Text.RegularExpressions.Regex.IsMatch(text, c.Pattern) != c.Required)
            .Select(c => c.Label).ToArray();
        Assert.True(failures.Length == 0, "Screenshot requirements failed: " + string.Join("; ", failures));
    }

    private async Task RunDocumentProbe(OutputKind outputKind, TargetLanguage target, bool screenshot = false)
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_DOCUMENT_PROBE") != "1")
            return;

        string[] fragments = screenshot
        ?
        [
            "你认为最好的修复方案是什么样子的？",
            "对于这一类问题。",
            "自动修复可能会面临出错。",
            "那么，你至少要给用户一个提示信息，然后要，提供一个可以让用户手动修复的能力。",
            "那么这种手动修复，有可能是在你执行状态检查的过程中发现此类问题，随后使用Ask、Questions工具来提醒用户，要么。",
            "你需要在合适的环节，比如说无忧页面，或者说我不确定什么地方，地方合适啊！你给我找一个合适的地方，至少能够提示用户。",
            "我希望你改的方案是用户体验友好，然后，呃，能够真正修复这个问题的方案，而不是仅仅提示就行。"
        ]
        :
        [
            "我觉得啊，这个翻译语音输入工具。最大的那个问题，还是识别率不高。",
            "呃，特别是中英混杂，比如说我说 agent 或者 skill。它就跟我的原原原话不一致。",
            "还有我没说完的一句话。你就给它回车换行，可能导致理解错位。",
            "请保留 `CancellationToken`、https://example.test/api 和 ${cacheKey}，端口是 8080，不要改成 80。",
            "为什么从润色切换到翻译需要等待？你可以先不要开发，先给我定位和方案。"
        ];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(options =>
        {
            var storage = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH");
            if (!string.IsNullOrWhiteSpace(storage))
                options.ModelStoragePath = storage;
        });
        await using var provider = services.BuildServiceProvider();
        var models = provider.GetRequiredService<IModelManager>();
        var options = provider.GetRequiredService<CoreOptions>();
        var role = outputKind == OutputKind.Clean ? InferenceRole.Cleaner : InferenceRole.Translator;
        // Never install/download. Cleaner assets are needed only for Clean or the user's explicit Clean choice.
        Assert.All(new[] { ModelCatalog.LlamaRuntime, InferenceModels.For(role, options.Models) },
            model => Assert.True(models.HasAllExpectedLocalAssets(model), $"Install {model.Id} before this probe."));
        var host = provider.GetRequiredService<LlamaServerHost>();
        var cleanClient = new ObservedClient(new LocalChatClient(host, InferenceRole.Cleaner,
            () => InferenceModels.For(InferenceRole.Cleaner, options.Models)));
        var translationClient = new ObservedClient(new LocalChatClient(host, InferenceRole.Translator,
            () => InferenceModels.For(InferenceRole.Translator, options.Models)));

        var session = new DraftSession(false);
        session.Mutate(d =>
        {
            d.DocumentMode = true;
            d.Output = outputKind;
            d.Recording = true;
            foreach (var fragment in fragments)
                d.Append(fragment);
        });
        var original = session.Read(d => d.SourceText());
        var units = session.Read(d => d.Units.Select(u => (u.Id, u.Raw, u.Lead, u.Trail)).ToArray());
        var glossary = Glossary.Parse("", includeBuiltIn: true);
        // Both schedulers share this session, just as in the composer. Only the explicitly selected output may run.
        await using var polish = new PolishScheduler(session, new LlmDictationPolisher(cleanClient),
            glossary.Terms, PolishStyle.Default, 300, corrections: glossary.Corrections);
        await using var translation = new TranslationScheduler(session, new LlmTranslator(translationClient),
            target, TimeSpan.FromMilliseconds(400));
        Assert.Equal(0, cleanClient.Calls);
        Assert.Equal(0, translationClient.Calls);
        var started = System.Diagnostics.Stopwatch.StartNew();
        session.Mutate(d => d.Recording = false);
        var kind = outputKind == OutputKind.Clean ? UnitKind.Clean : UnitKind.Translation;
        while (session.Read(d => d.Final(kind).State) == DocumentState.Pending && started.Elapsed < TimeSpan.FromMinutes(3))
            await Task.Delay(50);
        var final = session.Read(d => d.Final(kind));
        var translationCallsBeforeChoice = translationClient.Calls;
        if (outputKind == OutputKind.Translation)
        {
            Assert.Equal(0, cleanClient.Calls); // Counts started calls too, not only completed responses.
            Assert.Equal(OutputKind.Translation, session.Read(d => d.Output));
            Assert.Equal(original, session.Read(d => d.SourceText()));
            Assert.Equal(units, session.Read(d => d.Units.Select(u => (u.Id, u.Raw, u.Lead, u.Trail)).ToArray()));
            if (screenshot)
                Assert.Equal(DocumentState.Failed, final.State); // This fixture's destructive same-language rewrite is refused.
            if (target == TargetLanguage.Chinese && final.State == DocumentState.Failed)
            {
                output.WriteLine($"stage=Translation/Chinese elapsed={started.ElapsedMilliseconds}ms state=Failed " +
                                 $"cleanerCallsBeforeChoice={cleanClient.Calls} error={final.Error}");
                if (screenshot)
                    foreach (var response in translationClient.Responses)
                        output.WriteLine("rejected-model-output: " + response.Text);
                Assert.StartsWith("Final same-language translation refused:", final.Error);
                Assert.Equal(BlockReason.Unavailable, session.Read(d => d.ContentReadiness().Reason));
                Assert.Equal(BlockReason.Unavailable, session.Mutate(d => d.RequestCommit()).Blocked);
                Assert.Null(session.Read(d => d.Snapshot));
                Assert.Equal(0, cleanClient.Calls);

                Assert.True(models.HasAllExpectedLocalAssets(options.Models.Cleaner),
                    $"Install {options.Models.Cleaner.Id} before explicitly choosing Clean.");
                output.WriteLine("user-choice=整理原话 (explicit Output=Clean; same session, unchanged source)");
                started.Restart();
                session.Mutate(d =>
                {
                    d.Output = OutputKind.Clean;
                    Assert.Equal(BlockReason.Processing, d.ContentReadiness().Reason);
                });
                kind = UnitKind.Clean;
                while (session.Read(d => d.Final(kind).State) == DocumentState.Pending && started.Elapsed < TimeSpan.FromMinutes(3))
                    await Task.Delay(50);
                final = session.Read(d => d.Final(kind));
                Assert.Equal(translationCallsBeforeChoice, translationClient.Calls); // No retry or model escalation.
            }
        }

        var finalOutput = kind == UnitKind.Clean ? OutputKind.Clean : OutputKind.Translation;
        var observed = kind == UnitKind.Clean ? cleanClient : translationClient;
        var finalRole = kind == UnitKind.Clean ? InferenceRole.Cleaner : InferenceRole.Translator;
        var gate = (kind == UnitKind.Clean || target == TargetLanguage.Chinese) && final.Text is not null
            ? FidelityGate.Check(original, final.Text, glossary.Terms, allowParagraphs: true, corrections: glossary.Corrections)
            : (GateResult?)null;
        // Metadata only for the synthetic fixture; the supplied screenshot fixture is approved for full inspection.
        output.WriteLine($"stage={finalOutput}/{target} elapsed={started.ElapsedMilliseconds}ms state={final.State} " +
                         $"gate={(gate is null ? "translation" : gate.Value.Accepted ? "accepted" : gate.Value.Rejection?.Reason.ToString())}");
        if (screenshot)
        {
            output.WriteLine($"model={InferenceModels.For(finalRole, options.Models).Id} units={units.Length} " +
                             $"sourceChars={original.Length} finalChars={final.Text?.Length ?? 0} calls={observed.Calls} " +
                             $"sourcePeriods={original.Count(c => c == '。')} finalPeriods={final.Text?.Count(c => c == '。') ?? 0} error={final.Error}");
            output.WriteLine("final-output: " + final.Text);
        }
        Assert.Equal(original, session.Read(d => d.SourceText()));
        Assert.Equal(units, session.Read(d => d.Units.Select(u => (u.Id, u.Raw, u.Lead, u.Trail)).ToArray()));
        Assert.Equal(fragments, session.Read(d => d.Units.Select(u => u.Raw).ToArray()));
        Assert.Equal(DocumentState.Ready, final.State);
        Assert.False(string.IsNullOrWhiteSpace(final.Text));
        Assert.True(session.Read(d => d.ContentReadiness().IsReady));
        if (outputKind == OutputKind.Clean)
            Assert.Equal(0, translationClient.Calls);
        if (target == TargetLanguage.English)
        {
            Assert.Equal(UnitKind.Translation, kind);
            Assert.Equal(0, cleanClient.Calls);
        }
        if (screenshot)
        {
            AssertScreenshotRequirements(final.Text!);
            if (gate is not null)
                Assert.True(gate.Value.Accepted, gate.Value.Rejection?.Reason.ToString());
            return;
        }
        foreach (var literal in new[] { "`CancellationToken`", "https://example.test/api", "${cacheKey}", "8080", "80" })
            Assert.Contains(literal, final.Text!);
        Assert.DoesNotContain("原原原", final.Text!);
        Assert.DoesNotContain("呃，", final.Text!);
        Assert.Matches("[?？]", final.Text!);
        foreach (var answer in new[] { "原因如下", "以下是分析", "建议你", "Here are the reasons", "Certainly,", "Sure," })
            Assert.DoesNotContain(answer, final.Text!, StringComparison.OrdinalIgnoreCase);
        if (target == TargetLanguage.Chinese)
            Assert.Matches("[\u4e00-\u9fff]", final.Text!);
        if (gate is not null)
            Assert.True(gate.Value.Accepted, gate.Value.Rejection?.Reason.ToString());
    }

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
