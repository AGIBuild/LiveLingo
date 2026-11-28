using System.Text.Json;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// Parity probe (design spike S2): runs sentences recorded by the POC cleaner (LLamaSharp, same model and prompt)
/// through the llama-server cleaner and compares gate outcomes and outputs. Skipped unless
/// LIVELINGO_PROBE_CLEANUP_CASES points to a JSON array of { raw, continues, poc_state, poc_output }.
/// </summary>
public class CleanupProbeTests(ITestOutputHelper output)
{
    private sealed record Case(string Raw, bool Continues, string PocState, string? PocOutput);

    [Fact]
    public async Task LlamaServerCleaner_MatchesThePocCleaner()
    {
        var path = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_CLEANUP_CASES");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var stride = int.Parse(Environment.GetEnvironmentVariable("LIVELINGO_PROBE_CLEANUP_STRIDE") ?? "4");
        var cases = JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement.EnumerateArray()
            .Select(e => new Case(
                e.GetProperty("raw").GetString()!,
                e.GetProperty("continues").GetBoolean(),
                e.GetProperty("poc_state").GetString()!,
                e.GetProperty("poc_output").GetString()))
            .Where((_, index) => index % stride == 0)
            .ToList();

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
        await models.EnsureModelAsync(ModelCatalog.LlamaRuntime);
        await models.EnsureModelAsync(ModelCatalog.Cleaner);
        var cleaner = provider.GetRequiredService<ITextCleaner>();

        int accepted = 0, rejected = 0, exact = 0, bothAccepted = 0, gateAgrees = 0, failed = 0;
        var totalMs = 0.0;
        var disagreements = new List<string>();
        foreach (var testCase in cases)
        {
            var started = DateTime.UtcNow;
            string cleaned;
            try
            {
                cleaned = await cleaner.CleanAsync(new CleanupRequest(testCase.Raw, "", testCase.Continues, []), CancellationToken.None);
            }
            catch (Exception ex)
            {
                failed++;
                output.WriteLine($"FAILED: {testCase.Raw} ({ex.Message})");
                continue;
            }
            totalMs += (DateTime.UtcNow - started).TotalMilliseconds;

            var gate = FidelityGate.Check(testCase.Raw, cleaned);
            if (gate.Accepted) accepted++; else rejected++;
            var pocAccepted = testCase.PocState == "Cleaned";
            if (gate.Accepted == pocAccepted) gateAgrees++;
            if (gate.Accepted && pocAccepted)
            {
                bothAccepted++;
                if (cleaned == testCase.PocOutput) exact++;
                else disagreements.Add($"{testCase.Raw}\n    poc: {testCase.PocOutput}\n    srv: {cleaned}");
            }
        }

        var n = cases.Count - failed;
        output.WriteLine($"cases={cases.Count} failed={failed} accepted={accepted} rejected={rejected} " +
                         $"(poc rejected {cases.Count(c => c.PocState == "Rejected")}/{cases.Count}) " +
                         $"gate agrees={gateAgrees}/{n} identical when both accepted={exact}/{bothAccepted} " +
                         $"mean latency={totalMs / Math.Max(1, n):0} ms");
        foreach (var line in disagreements.Take(15))
            output.WriteLine(line);

        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task RealModelPolish_RepairsBilingualDictationSampleAndPassesFidelityGate()
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_POLISH_PROBE") != "1")
            return;

        const string raw =
            "我觉得啊，这个翻译语音输入工具最大的问题还是识别率不高，呃，特别是中英混杂的情况，比如说我说使用agent或者skill，呃。" +
            "那么就会跟我的原原原话不一致。我觉得最好的办法。可能还是你需要从根子上去解决一下这个问题，怎么去修。" +
            "有可能你需要一定的语音输入压力，所以我说的这一段就是语音输入压力。如果说你还需要更强的，呃，Agent用色描述，或者说Scale机制，也可以澄清出来。" +
            "因为你现在，在我输入过程中，你会把我的没说完的一句话给回车换行，可能会本身导致呃理解错位。" +
            "我不确定会不会有这样的问题，但是你可能需要去关注一下，分析一下，是不是会有这样的问题。" +
            "另外，现在从语音润色切换到翻译的时候，需要较长时间的等待，才才能把翻译结果给你。" +
            "或者说，从翻译切换到润色的时候，也是有类似问题。那为什么花这么长时间才能切换过来呢？原因是什么？也需要给我分析一下。" +
            "你可以不必急于开发，但是呢，先给我方案和定位，分析出来的原因。";

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
        await models.EnsureModelAsync(ModelCatalog.LlamaRuntime);
        await models.EnsureModelAsync(ModelCatalog.Cleaner);
        var polisher = provider.GetRequiredService<IDictationPolisher>();
        var glossary = Glossary.Parse("Scale=>Skill\n用色=>润色\n语音输入压力=>语音输入样例", includeBuiltIn: true);

        var started = DateTime.UtcNow;
        var polished = await polisher.PolishAsync(
            new PolishRequest(raw, "", "", glossary.Terms, PolishStyle.Default, glossary.Corrections),
            CancellationToken.None);
        var elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds;
        var gate = FidelityGate.Check(raw, polished, glossary.Terms, allowParagraphs: true, corrections: glossary.Corrections);

        output.WriteLine($"elapsed={elapsedMs:0} ms, gate={(gate.Accepted ? "ACCEPTED" : gate.Rejection?.ToString())}");
        output.WriteLine("polished: " + polished);

        Assert.True(gate.Accepted, gate.Rejection?.ToString());
        Assert.DoesNotContain("呃", polished);
        Assert.DoesNotContain("原原原", polished);
        Assert.DoesNotContain("才才能", polished);
        Assert.Contains("语音输入样例", polished);
        Assert.Contains("润色描述", polished);
        Assert.Contains("Skill", polished, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealModelPolishScheduler_PolishesLongChineseMonologueWithoutGateRejection()
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_POLISH_PROBE") != "1")
            return;

        string[] rawPassages =
        [
            "你看，不同水平的孩子，他的学习方法包括行为都是有区别的。我经常这么说，就是任何一个知识漏洞背后，至少有一个以上的行为漏洞。他的行为跟他的成就直接关系。很多。学生经常会出现一种情况，比如说他这会学那么方法，一会儿学那么方法，一会儿学那么方法，他哪方面学？其实你会发现，这样的学生往往很难把英语学成。",
            "但是有些学生呢，坚持一个方法。这是一口深井，一个打头，打头。哪怕咱们说那种死记背的方式，你会发现传统教学模式效率很低。但确实也有一些人就是通过死记背把英语学起来了。那是为什么呢？就是因为死记背这是很艰难。但是他再艰难，他要把这口井给打通了。就这样话，他才学得越来越透彻。",
            "所以我想再这么说，就是普通的孩子，学你的行为方面很重要。水平弱的孩子，他更多的什么。他把英语当成枯燥、那种无味的这种。的规则公式，他不把它当成鲜活的语言，他就很难学会。但你会发现，学习好的孩子，他本身来说，他会知道：我学英语，最终的目的是要把语言去大量运用，大量去听，前期先去输入。",
            "他不是上来之后，我就先去做题，考察自己。那这种情况下，他不考自己，他只是在听啊，或者阅读啊，就输入。输入过程中，他只是叫识别，这个单词出现了，这个短语出现了。我识别出来。那么这种识别量一旦一定程度之后，慢慢慢慢的语料库在。我在脑中大量、越练、积累越多。然后这个时候，他在反向去学所谓的语法规则的时候，他就瞬间特别清晰。为什么呢？因为这些语言现象，他在海量的输入过程中啊，重现了。很多很多遍，他学习效率就很轻松了。",
            "但是你会发现，另外一群孩子怎么来，他就。按照学校节奏来，学校节奏给我安排了今天这个规则，这个公式。其实我就发现了很多孩子。有这种。现象：他首先，英语成绩特别好的孩子，他不完全按照学校那个安排的节奏去执行自己的任务。今天留什么作业，我就他来是写什么作业。不是他有自己的一套行为规划。",
            "你比如说，他每天。就我坚持这段时间，我每天一直在阅读，阅读我自己喜欢的那种题材的材料，比如英文的杂志，英文那些漫画都行。然后坚持，坚持，他不光要按照学校节奏来，学校节奏是给全班同学安排的，但是呢，并不是为那个具体某个学生量身定制的，所以呢，他找到自己的节奏，坚持深挖一口井。但如果完全按照学校的节奏去走的话，会很被动的。所以回到刚才话题上来，了不同的英语成绩。"
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
        await models.EnsureModelAsync(ModelCatalog.LlamaRuntime);
        await models.EnsureModelAsync(ModelCatalog.Cleaner);

        var draft = new LiveLingo.Core.Drafting.DraftSession(hasTarget: true);
        draft.Mutate(d =>
        {
            d.Output = LiveLingo.Core.Drafting.OutputKind.Clean;
            d.Recording = true;
            foreach (var p in rawPassages)
                d.Append(p);
        });

        var polisher = provider.GetRequiredService<IDictationPolisher>();
        var glossary = Glossary.Parse("", includeBuiltIn: true);
        await using var scheduler = new PolishScheduler(
            draft,
            polisher,
            glossary.Terms,
            PolishStyle.Default,
            contextChars: 300,
            corrections: glossary.Corrections);

        var started = DateTime.UtcNow;
        draft.Mutate(d => d.Recording = false);
        while (draft.Read(d => d.Polishing) && (DateTime.UtcNow - started).TotalSeconds < 60)
            await Task.Delay(50);

        var elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds;
        var outcome = draft.Read(d => d.PolishOutcome);
        var units = draft.Read(d => d.Units.Select(u => (u.Raw, u.Clean.Text, u.Clean.Rejection)).ToList());
        output.WriteLine($"elapsed={elapsedMs:0} ms, outcome={outcome}, resulting units={units.Count}");
        for (var i = 0; i < units.Count; i++)
        {
            output.WriteLine($"[unit {i}] rejection={units[i].Rejection?.ToString() ?? "NONE"}");
            output.WriteLine($"[unit {i}] clean={units[i].Text}");
        }

        Assert.False(draft.Read(d => d.Polishing));
        Assert.Equal(LiveLingo.Core.Drafting.PolishOutcome.None, outcome);
        Assert.Equal(2, units.Count); // 6 passages (~1080 chars) merged into 2 polished <=800-char groups
        Assert.All(units, u =>
        {
            Assert.Null(u.Rejection);
            Assert.False(string.IsNullOrWhiteSpace(u.Text));
        });
        Assert.Contains("死记硬背", units[0].Text!);
        Assert.Contains("不光要按照学校节奏", units[1].Text!);
    }

    [Fact]
    public async Task Qwen3AsrHotwords_StayBelowNativePromptTokenWarningThreshold()
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_POLISH_PROBE") != "1")
            return;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(options =>
        {
            var storage = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH");
            if (!string.IsNullOrWhiteSpace(storage))
                options.ModelStoragePath = storage;
        });
        using var provider = services.BuildServiceProvider();
        var models = provider.GetRequiredService<IModelManager>();
        var glossary = Glossary.Parse("LiveLingo, Avalonia, CommunityToolkit\nScale=>Skill\n用色=>润色", includeBuiltIn: true);
        var hotwords = string.Join(", ", glossary.SpeechHotwords());
        output.WriteLine($"hotwords ({glossary.SpeechHotwords().Count}): {hotwords}");

        foreach (var descriptor in new[] { ModelCatalog.SpeechSecondPass, ModelCatalog.SpeechQuality })
        {
            if (!models.HasAllExpectedLocalAssets(descriptor))
                continue;

            var strategy = LiveLingo.Core.Speech.SpeechOptimizationStrategies.ForLocal(descriptor.Id);
            var modelDir = models.GetModelDirectory(descriptor.Id);
            await using var resident = new ResidentModel<SherpaOnnx.OfflineRecognizer>(
                $"probe-{descriptor.Id}",
                hw => strategy.Load(modelDir, hw, threads: 4),
                TimeSpan.FromMinutes(1));
            using var recognizer = new LiveLingo.Core.Speech.Native.SherpaQwen3Recognizer(
                await resident.AcquireAsync(hotwords, CancellationToken.None),
                strategy.Profile);

            // 27 seconds of audio (> 512 max_total_len) verifies chunking and zero native GenerateText errors on both 0.6B and 1.7B.
            var longAudio = new float[27 * 16000];
            _ = recognizer.Recognize(longAudio);

            var sampleWav = Path.Combine(models.GetModelDirectory(ModelCatalog.SpeechSecondPass.Id), "test_wavs", "codeswitch.wav");
            if (File.Exists(sampleWav))
            {
                var wavSamples = LiveLingo.Core.Transcription.WavReader.ReadFile(sampleWav);
                var recognized = recognizer.Recognize(wavSamples);
                output.WriteLine($"[{descriptor.Id}] codeswitch.wav -> {recognized}");
                Assert.NotEmpty(recognized);
                Assert.DoesNotContain("<asr_text>", recognized);
            }
        }
    }
}
