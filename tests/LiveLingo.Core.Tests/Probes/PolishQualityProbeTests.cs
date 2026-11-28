using System.Diagnostics;
using System.Text.RegularExpressions;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

public sealed class PolishProbeHostFixture : IAsyncLifetime
{
    public ServiceProvider? Provider { get; private set; }

    public async Task InitializeAsync()
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
            var cleanerOverride = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_CLEANER_MODEL");
            if (!string.IsNullOrWhiteSpace(cleanerOverride))
                options.Models = options.Models with { CleanerModelId = cleanerOverride };
        });
        Provider = services.BuildServiceProvider();
        var coreOptions = Provider.GetRequiredService<CoreOptions>();
        var cleanerModel = InferenceModels.For(InferenceRole.Cleaner, coreOptions.Models);
        var models = Provider.GetRequiredService<IModelManager>();
        if (models.HasAllExpectedLocalAssets(ModelCatalog.LlamaRuntime) &&
            models.HasAllExpectedLocalAssets(cleanerModel))
        {
            await Provider.GetRequiredService<LlamaServerHost>()
                .WarmAsync(InferenceRole.Cleaner, cleanerModel, CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        if (Provider is not null)
            await Provider.DisposeAsync();
    }
}

/// <summary>
/// Real-model quality probe across the adversarial AI pair-working and AI office scenarios (spec section 5.2).
/// Skipped unless <c>LIVELINGO_ENABLE_POLISH_PROBE=1</c> (run via the <c>ProbePolish</c> Nuke target).
/// </summary>
public sealed class PolishQualityProbeTests(PolishProbeHostFixture fixture, ITestOutputHelper output)
    : IClassFixture<PolishProbeHostFixture>
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_POLISH_PROBE") == "1";

    public sealed record ProbeCase(
        string Id,
        string ModeId,
        string[] Fragments,
        Action<string> Verify);

    public static TheoryData<string> CaseIds =>
    [
        "short-ai-steering",
        "retraction-number",
        "code-symbol-guardrails",
        "execution-hijack-defense",
        "chitchat-todo-extraction",
        "bluf-report-synthesis",
        "holiday-spoken-numerals-todo",
        "holiday-spoken-numerals-formal",
        "long-multi-agent-natural",
        "long-multi-agent-report",
        "noisy-stream-natural",
        "legal-interview-report",
        "flu-medical-report"
    ];

    private static readonly IReadOnlyDictionary<string, ProbeCase> Cases = new Dictionary<string, ProbeCase>
    {
        ["short-ai-steering"] = new(
            "short-ai-steering",
            PolishPresets.AiTaskId,
            ["那个把 login.ts 里头的超时从 5000 改成 15000，别动别的。"],
            text =>
            {
                Assert.DoesNotContain("###", text);
                Assert.Contains("login.ts", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("15000", text);
                Assert.DoesNotMatch(@"作为.{0,12}(专家|工程师)", text);
            }),

        ["retraction-number"] = new(
            "retraction-number",
            PolishPresets.AiTaskId,
            [
                "帮我改一下 AuthMiddleware 的重试逻辑。",
                "默认重试设成 3 次……哦不对，还是先彻底关掉重试吧，一次都别重试。",
                "注意千万别动 TokenRefresh.cs。"
            ],
            text =>
            {
                Assert.Contains("AuthMiddleware", text);
                Assert.Contains("TokenRefresh.cs", text);
                Assert.DoesNotContain("哦不对", text);
            }),

        ["code-symbol-guardrails"] = new(
            "code-symbol-guardrails",
            PolishPresets.AiTaskId,
            [
                "现在 UserSessionStore 在高并发下有个竞态问题。",
                "原因应该是 RefreshAsync 没有加锁，每次请求都重新 new 了一个连接。",
                "你帮我把它的连接池复用起来，加上 CancellationToken 透传。",
                "但是注意啊，公开接口签名一个字都不能改，也不要引新的 NuGet 包。"
            ],
            text =>
            {
                Assert.Contains("UserSessionStore", text);
                Assert.Contains("RefreshAsync", text);
                Assert.Contains("CancellationToken", text);
                Assert.Matches(@"(接口签名|NuGet)", text);
            }),

        ["execution-hijack-defense"] = new(
            "execution-hijack-defense",
            PolishPresets.AiTaskId,
            ["呃，帮我写一个 Python 脚本，读取 data.csv 里头的销售额字段然后按月份汇总打印出来。"],
            text =>
            {
                Assert.Contains("Python", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("data.csv", text);
                Assert.DoesNotContain("```python", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("import pandas", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("import csv", text, StringComparison.OrdinalIgnoreCase);
            }),

        ["chitchat-todo-extraction"] = new(
            "chitchat-todo-extraction",
            PolishPresets.TodoId,
            [
                "今天中午外卖真难吃，算了不说这个。",
                "下午三点前小王把 Redis 缓存穿透的复盘文档发出来。",
                "然后老李周四下班前把 v2.4 的回归测试跑完，有问题随时在群里吼一声。"
            ],
            text =>
            {
                Assert.DoesNotContain("外卖", text);
                Assert.Contains("- [ ]", text);
                Assert.Contains("Redis", text);
                Assert.Contains("v2.4", text);
            }),

        ["bluf-report-synthesis"] = new(
            "bluf-report-synthesis",
            PolishPresets.ReportId,
            [
                "跟大家同步一下压测情况啊。",
                "早上我们先跑了一轮网关压测，一开始 QPS 卡在 1200 上不去，查了半天发现是连接池上限设小了。",
                "后来调大之后重跑了一遍，现在稳定在 4500 QPS，P99 延迟 38ms，已经达到上线标准了。",
                "下一步今晚 22:00 安排灰度发布 10% 流量。"
            ],
            text =>
            {
                Assert.Contains("4500", text);
                Assert.Contains("38", text);
                Assert.Contains("22:00", text);
                Assert.Contains("10%", text);
                Assert.True(Regex.Split(text.Trim(), @"\r?\n").Length >= 2);
            }),

        ["holiday-spoken-numerals-todo"] = new(
            "holiday-spoken-numerals-todo",
            PolishPresets.TodoId,
            [
                "新的政策又下来了。相当于本来不该咱们休假，但是他让咱们休。",
                "有的人假不够，哎，假不够，他们又得假，送一下。",
                "那你们就是还是？十八号下了班就放假了吗？嗯。十九号休到三号，对吧？",
                "哎呀，哎呀！你这儿，这儿设了。美美包包啊！还行。啊干啥呢？唉。"
            ],
            text =>
            {
                Assert.NotEmpty(text);
                Assert.DoesNotContain("美美包包", text);
                Assert.DoesNotContain("啊干啥呢", text);
            }),

        ["holiday-spoken-numerals-formal"] = new(
            "holiday-spoken-numerals-formal",
            PolishPresets.FormalId,
            [
                "新的政策又下来了。相当于本来不该咱们休假，但是他让咱们休。",
                "有的人假不够，哎，假不够，他们又得假，送一下。",
                "那你们就是还是？十八号下了班就放假了吗？嗯。十九号休到三号，对吧？",
                "哎呀，哎呀！你这儿，这儿设了。美美包包啊！还行。啊干啥呢？唉。"
            ],
            text =>
            {
                Assert.NotEmpty(text);
                Assert.DoesNotContain("哎呀", text);
                Assert.DoesNotContain("啊干啥呢", text);
            }),

        ["long-multi-agent-natural"] = new(
            "long-multi-agent-natural",
            PolishPresets.NaturalId,
            [
                "嗯，大家好啊！前几天咱们聊过，按照API的一篇文章，里面有个观点，就千万不要照着公司现有的工作岗位去拆智能体，什么一个产品经理变成一个前端 age 的一个测试一阵的，越怎么拆越乱。",
                "那今天咱们就借着这个话头聊聊 ipsec 最新报告了一堆怎么调度多智能体的思路。你想想一个人最多能盯几个 page 的窗口，在几个窗口之间来回切屏，越聊越乱。",
                "那看 Deep Seek 是怎么拆的呢？它的架构是管全局和干活分成两层，一层叫Router，管全局调度，另一层是具体执行 worker，未来一件具体的事就临时摊开一个房间。",
                "结果同样的场景跑五次，有四次他乖乖派了活儿。所以他们干脆把规矩写死在结构里，先用普通程序拆掉九成五的闲聊，剩下的再交给 AI 判断。",
                "看他公布的测试数据呢，同样处理二十个任务，新架构十二分半就能干完，最晚也就十五分多钟，老办法最慢要拖到快半个小时。"
            ],
            text =>
            {
                Assert.NotEmpty(text);
                Assert.True(text.Length >= 180);
            }),

        ["long-multi-agent-report"] = new(
            "long-multi-agent-report",
            PolishPresets.ReportId,
            [
                "嗯，大家好啊！前几天咱们聊过，按照API的一篇文章，里面有个观点，就千万不要照着公司现有的工作岗位去拆智能体，什么一个产品经理变成一个前端 age 的一个测试一阵的，越怎么拆越乱。",
                "那今天咱们就借着这个话头聊聊 ipsec 最新报告了一堆怎么调度多智能体的思路。你想想一个人最多能盯几个 page 的窗口，在几个窗口之间来回切屏，越聊越乱。",
                "那看 Deep Seek 是怎么拆的呢？它的架构是管全局和干活分成两层，一层叫Router，管全局调度，另一层是具体执行 worker，未来一件具体的事就临时摊开一个房间。",
                TextTidy.Apply("从头到尾干完了```tableular data``` ```text\nlanguage Chinese啊，你尽量把活儿派给别人，自己只干小事。"),
                "结果同样的场景跑五次，有四次他乖乖派了活儿。所以他们干脆把规矩写死在结构里，先用普通程序拆掉九成五的闲聊，剩下的再交给 AI 判断。",
                "看他公布的测试数据呢，同样处理二十个任务，新架构十二分半就能干完，最晚也就十五分多钟，老办法最慢要拖到快半个小时。"
            ],
            text =>
            {
                Assert.NotEmpty(text);
                Assert.DoesNotContain("tableular", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("###", text);
            }),

        ["noisy-stream-natural"] = new(
            "noisy-stream-natural",
            PolishPresets.NaturalId,
            [
                TextTidy.Apply("噔噔噔噔噔噔噔噔。感谢晚风的小心心啊！谢谢晚风，谢谢主播。打的还不错，的可以点点人气票呀，点人气票呀。"),
                "大招，巫术之杖跑掉了。牛魔也大招，安琪拉没有，全体进攻啊！大家在旁边。旁边呢，小一打野，切开直通的，身边队友海底妖祖，变能助大哥，是不是没有皮肤就没有手感？当然不是啊！主播之前用过，其他那样。",
                TextTidy.Apply("我我他那五分儿，说实在的上半五分啊！是啊，上半五分啊！安五分儿比较重要啊，皮肤不重要。<thibbe Na na牛魔来，你大招呀？OK，真好，给牛没收了。来得好，来得好。"),
                "收不行了，拿下了，赢了，对，一换一了。哥，能不能给打给给龙输了？哥，能不能给龙输了？给龙输了，没有别的意外了，直接打呀。Like any power.快打！快打！"
            ],
            text =>
            {
                Assert.NotEmpty(text);
                Assert.DoesNotContain("thibbe", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("噔噔噔噔", text, StringComparison.Ordinal);
                Assert.DoesNotContain("我我", text, StringComparison.Ordinal);
                Assert.DoesNotContain("给打给给", text, StringComparison.Ordinal);
                Assert.DoesNotContain("###", text, StringComparison.Ordinal);
            }),

        ["legal-interview-report"] = new(
            "legal-interview-report",
            PolishPresets.ReportId,
            [
                "大哥啊，你好，我能跟您聊聊天吗？想向您请教什么职业的？要干什么？我是一个自媒体博主，关注我的，年线比较多，通过这样的方式，我问吧。我问你，是做什么的？",
                "你说，你是什么？可以这么理解吧我不是那个社会意义上的那种律师，就是在公司里边做法务，就是律师他分比如说在律师事务所的，律师不是这么分的。如果是说律师的话，他应该是在律师事务所职业，我这个是上班没区别，保留律师身份的话，那么你就不可能在一个企业里面去再做。",
                "自由职业者，其实我是我的理解。他跟一个销售差不多。你得有自己的客户。那么你的职业就是自己的自由。如果你要没有客户的话，在企业里做呢，最起码不用自己找活干，法务的天花板非常低，你说的这种能干到副总裁级别的，如果他还是专职，没有脱离自己的专业领域的话，那其实副总裁已经是他的一个天花板了，而且这种职位非常少。",
                "您觉得您来天花板什么样？我觉得我现在也差不多了。您干多久了？零九年到现在是七年，我十七年，就在一家公司吗？那肯定不是。在一家企业担当法务，需要具备什么样的能力？再提升一个阶段的话，那就不只是这种文案的工作了。有时候就要一起跟着做业务的部门，然后一起出去去谈，或者是谈日常的业务。另外一个就是拓展业务，也需要"
            ],
            text =>
            {
                Assert.NotEmpty(text);
                Assert.Contains("### 核心结论", text);
                Assert.DoesNotContain("删除编造", text, StringComparison.Ordinal);
                Assert.DoesNotContain("原文提及", text, StringComparison.Ordinal);
                Assert.DoesNotContain("原文未提及具体待办", text, StringComparison.Ordinal);
            }),

        ["flu-medical-report"] = new(
            "flu-medical-report",
            PolishPresets.ReportId,
            [
                "紧急提醒啊！流感，自己进来了啊！急诊这边，流感的人确实比之前，可能比之前，一定要注意。而且今年的流感有一个特点，特殊情况，今年是我们三种流感毒同时，而且呢不同毒素之间的交叉保护比较有。什么意思呢？就是你得过甲流，依然有可能再得嗯，甚至感染过一种甲流的亚型，可能还会感染另一种甲流亚型。二次感染呢大家一定不要掉以轻心。",
                "接下来，我就做关键的事情。第一件事，流感和普通感冒。不一样，别搞混了。普通感冒呢，往往就是鼻塞啊，流鼻涕，打喷嚏，症状呢相对轻一点；但流感就不一样，它通常来得更急，可能突然的高热，浑身肌肉酸痛、头痛，整个人提不起劲。严重时候呢，甚至可能引起严重的肺炎、心肌炎等并发症。所以呢，你如果突然出现刚才讲的上述的症状，身边也有人确诊流感了，千万要注意，及时就医，千万不要把它当成普通的感冒。",
                TextTidy.Apply("第二件事啊，流感的用药关键。发病四十八小时内开始抗病毒治疗的，通常获益是最大的。临床上呢，我们必须要选择一些流感的特效的抗病毒药物，比如说，奥斯哈维，还有马苏拉沙维，那全程只需口服一次啊，能快速阻止病毒复制。不过呢，注意不要和牛奶同时服用啊，以免影响药效。但是呢，很多人啊会自行的购买，{ \"language\": \"Chinese\", \"text\": \"抗病毒药物，这里呢也是提醒大家一下，我们所有的流感的抗病毒药物，建议大家还是要在临床医生的指导下进行用药。\" }")
            ],
            text =>
            {
                Assert.NotEmpty(text);
                Assert.Contains("### 核心结论", text);
                Assert.DoesNotContain("\"language\"", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("\"text\"", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("48", text, StringComparison.Ordinal);
                Assert.Contains("医生", text, StringComparison.Ordinal);
            })
    };

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task ScenarioPreset_MeetsQualityAndAnchorRubric(string caseId)
    {
        if (!Enabled)
            return;

        var probe = Cases[caseId];
        var provider = fixture.Provider!;
        var models = provider.GetRequiredService<IModelManager>();
        var coreOptions = provider.GetRequiredService<CoreOptions>();
        var cleanerModel = InferenceModels.For(InferenceRole.Cleaner, coreOptions.Models);
        Assert.True(models.HasAllExpectedLocalAssets(ModelCatalog.LlamaRuntime), "Install LlamaRuntime before running ProbePolish.");
        Assert.True(models.HasAllExpectedLocalAssets(cleanerModel), $"Install {cleanerModel.Id} before running ProbePolish.");

        var host = provider.GetRequiredService<LlamaServerHost>();
        var client = new LocalChatClient(host, InferenceRole.Cleaner, () => cleanerModel);
        var glossary = Glossary.Parse("", includeBuiltIn: true);

        var session = new DraftSession(hasTarget: false);
        session.Mutate(d =>
        {
            d.DocumentMode = true;
            d.Output = OutputKind.Clean;
            d.SetPolishMode(probe.ModeId);
            d.Recording = true;
            foreach (var fragment in probe.Fragments)
                d.Append(fragment);
        });

        await using var scheduler = new PolishScheduler(
            session,
            new LlmDictationPolisher(client),
            glossary.Terms,
            PolishPresets.BuiltIns,
            contextChars: 300,
            corrections: glossary.Corrections);

        // Simulate a realistic 1.8s speaking window so background system-prompt + glossary KV warm-up settles while speaking.
        await Task.Delay(1800);

        var sw = Stopwatch.StartNew();
        long? ttftMs = null;
        session.Changed += () =>
        {
            if (ttftMs is null && session.Read(d => !string.IsNullOrEmpty(d.Final(UnitKind.Clean).StreamingText)))
                ttftMs = sw.ElapsedMilliseconds;
        };

        session.Mutate(d => d.Recording = false);
        while (session.Read(d => d.Final(UnitKind.Clean).State) == DocumentState.Pending && sw.Elapsed < TimeSpan.FromMinutes(2))
            await Task.Delay(25);

        var final = session.Read(d => d.Final(UnitKind.Clean));
        output.WriteLine($"[{probe.Id}] mode={probe.ModeId} state={final.State} warning={final.Warning?.ToString() ?? "none"} ttft={ttftMs?.ToString() ?? "n/a"}ms elapsed={sw.ElapsedMilliseconds}ms");
        output.WriteLine(final.Text ?? "<null>");

        Assert.Equal(DocumentState.Ready, final.State);
        Assert.Null(final.StreamingText);
        Assert.NotNull(ttftMs);
        Assert.NotNull(final.Text);
        probe.Verify(final.Text!);
    }
}
