using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Cleanup;

public sealed class FactAnchorGateTests
{
    [Fact]
    public void Accepts_StructuredAiTaskSpecWithHeadingsOrdinalsAndRetractedNumberPruned()
    {
        const string raw =
            "呃我想改一下登录模块，先把重试设成 3 次吧，哦不对，网关层有重试了，客户端干脆关掉自动重试，只保留 401 刷新 token，" +
            "超时设成 30 秒不是 15 分钟。你先帮我查下 `AuthInterceptor` 还有没有隐式重试，列出来别改代码。";

        const string candidate = """
            ### 任务目标
            排查 `AuthInterceptor` 中是否存在隐式重试逻辑，并给出改造方案（不要直接改代码）。

            ### 硬性约束
            1. 关闭客户端自动重试，仅保留 `401` 刷新 Token 流程
            2. 超时时间设定为 30 秒
            """;

        var result = FactAnchorGate.Check(raw, candidate, forbidExecution: true);
        Assert.True(result.Accepted, result.Rejection?.ToString());
    }

    [Fact]
    public void Rejects_ExecutionHijackPreambleAndUnrequestedCodeBlocks()
    {
        const string raw = "帮我排查一下 `CacheService` 超时 30 秒的问题。";

        var preamble = FactAnchorGate.Check(
            raw,
            "好的，下面是针对 `CacheService` 30 秒超时问题的排查分析：",
            forbidExecution: true);
        Assert.Equal(GateReason.ExecutionHijack, preamble.Rejection?.Reason);

        var codeBlock = FactAnchorGate.Check(
            raw,
            "### 方案\n```csharp\npublic int Timeout = 30;\n```",
            forbidExecution: true);
        Assert.Equal(GateReason.ExecutionHijack, codeBlock.Rejection?.Reason);
    }

    [Fact]
    public void Rejects_UnrequestedRoleplayPersonaSlop()
    {
        const string raw = "帮我把 `CacheService` 的连接超时改为 30 秒，不要改公共接口。";
        const string candidate = "你是一位资深系统架构师，请将 `CacheService` 的连接超时改为 30 秒且不改公共接口。";

        var result = FactAnchorGate.Check(raw, candidate, forbidExecution: true);
        Assert.Equal(GateReason.RoleplaySlop, result.Rejection?.Reason);
    }

    [Fact]
    public void Rejects_LostActiveNumberInventedNumberAndLostProtectedLiteral()
    {
        const string raw = "将 `CacheService` 超时调整为 30 秒，保持 https://api.example.com/v1 不变。";

        var lostNum = FactAnchorGate.Check(raw, "- [ ] 将 `CacheService` 超时调整为 60 秒，保持 https://api.example.com/v1 不变");
        Assert.Equal(new GateRejection(GateReason.NumberLost, "30"), lostNum.Rejection);

        var inventedNum = FactAnchorGate.Check(raw, "- [ ] 18 点前将 `CacheService` 超时调整为 30 秒，保持 https://api.example.com/v1 不变");
        Assert.Equal(new GateRejection(GateReason.NumberInvented, "18"), inventedNum.Rejection);

        var lostLiteral = FactAnchorGate.Check(raw, "- [ ] 将缓存服务超时调整为 30 秒，保持接口地址不变");
        Assert.Equal(GateReason.TermLost, lostLiteral.Rejection?.Reason);
    }

    [Fact]
    public void Honors_GlossaryAndAccentCorrectionsForDigits()
    {
        var result = FactAnchorGate.Check(
            "修一下六零九的超时配置。",
            "- [ ] 修复 LiveLingo9 的超时配置",
            corrections: [new GlossaryCorrection("六零九", "LiveLingo9")]);
        Assert.True(result.Accepted, result.Rejection?.ToString());
    }

    [Fact]
    public void Accepts_ChineseSpokenNumeralsNormalizedToArabicDigits_AndSpokenClockTimes()
    {
        const string holidayRaw =
            "新的政策又下来了。相当于本来不该咱们休假，但是他让咱们休。有的人假不够，哎，假不够，他们又得假，送一下。" +
            "那你们就是还是？十八号下了班就放假了吗？嗯。十九号休到三号，对吧？哎呀，哎呀！你这儿，这儿设了。美美包包啊！还行。啊干啥呢？唉。";

        var todoWithDigits = FactAnchorGate.Check(
            holidayRaw,
            "- [ ] 确认放假起止时间（18号下班后是否开始放假，是否为19号休至3号）\n- [ ] 核实假期不足人员的处理方案",
            forbidExecution: true,
            allowSelectiveExtraction: true);
        Assert.True(todoWithDigits.Accepted, todoWithDigits.Rejection?.ToString());

        var noTodoSummary = FactAnchorGate.Check(
            holidayRaw,
            "（原话未包含明确待办事项）",
            forbidExecution: true,
            allowSelectiveExtraction: true);
        Assert.True(noTodoSummary.Accepted, noTodoSummary.Rejection?.ToString());

        var clockNormalized = FactAnchorGate.Check(
            "下午三点半前把 `Redis` 复盘发出来，预算控制在两千五百以内。",
            "- [ ] 15:30 前提交 `Redis` 复盘，预算控制在 2500 以内",
            forbidExecution: true);
        Assert.True(clockNormalized.Accepted, clockNormalized.Rejection?.ToString());
    }

    [Fact]
    public void SelectiveExtraction_AllowsDroppingChatterNumbers_WhileStillBlockingInventedNumbers()
    {
        const string raw = "今天中午 12 点吃的外卖花了 28 块钱真难吃。小王把 `Redis` 缓存穿透复盘发出来。";

        var accepted = FactAnchorGate.Check(
            raw,
            "- [ ] 提交 `Redis` 缓存穿透复盘（负责人：小王）",
            forbidExecution: true,
            allowSelectiveExtraction: true);
        Assert.True(accepted.Accepted, accepted.Rejection?.ToString());

        var invented = FactAnchorGate.Check(
            raw,
            "- [ ] 18:00 前提交 `Redis` 缓存穿透复盘（负责人：小王）",
            forbidExecution: true,
            allowSelectiveExtraction: true);
        Assert.Equal(new GateRejection(GateReason.NumberInvented, "18"), invented.Rejection);
    }

    [Fact]
    public void Accepts_ChinesePercentagesTrialRatiosHalfUnits_AndIgnoresAsrBacktickNoiseOnSpokenSources()
    {
        const string spoken =
            "同样的场景跑五次，有四次他乖乖派了活儿。先用普通程序拆掉九成五的闲聊，十二分半就能干完。```tableular data```";

        var result = FactAnchorGate.CheckDetailed(
            spoken,
            "### 核心结论\n- 任务派发成功率达 80%，前置规则过滤 95% 闲聊，平均耗时约 12.5 分钟。",
            glossary: ["Agent"],
            corrections: null,
            forbidExecution: true,
            allowSelectiveExtraction: true,
            ignoreBacktickLiterals: true,
            out var isHardFailure);

        Assert.True(result.Accepted, result.Rejection?.ToString());
        Assert.False(isHardFailure);

        // Typed/pasted code literals remain hard-protected when ignoreBacktickLiterals is false.
        var typedDrop = FactAnchorGate.CheckDetailed(
            "请保留 `AuthMiddleware` 超时 30 秒。",
            "请保留鉴权中间件超时 30 秒。",
            glossary: null,
            corrections: null,
            forbidExecution: true,
            allowSelectiveExtraction: false,
            ignoreBacktickLiterals: false,
            out var typedHard);
        Assert.Equal(GateReason.TermLost, typedDrop.Rejection?.Reason);
        Assert.True(typedHard);

        // Uppercase+digit product names like "GTA6" formatted with a space ("GTA 6") must not trigger false TermLost.
        var spacedProduct = FactAnchorGate.Check(
            "这次 GTA6 往里面塞的细节是前所未有的。",
            "此次 GTA 6 所包含的内容细节规模空前。",
            forbidExecution: true);
        Assert.True(spacedProduct.Accepted, spacedProduct.Rejection?.ToString());

        // Spoken 2-digit years ("零九年" -> 2009, "九八年" -> 1998) are accepted, whereas "十七年" (duration) never excuses "2017".
        var spokenYear = FactAnchorGate.Check(
            "您干多久了？零九年到现在是七年，我十七年，九八年毕业的。",
            "- 自 2009 年起从事法务工作已 17 年（1998 年毕业）。",
            forbidExecution: true,
            allowSelectiveExtraction: true);
        Assert.True(spokenYear.Accepted, spokenYear.Rejection?.ToString());

        var inventedYearFromDuration = FactAnchorGate.Check(
            "我在这行干了十七年。",
            "- 自 2017 年起从业。",
            forbidExecution: true,
            allowSelectiveExtraction: true);
        Assert.Equal(new GateRejection(GateReason.NumberInvented, "2017"), inventedYearFromDuration.Rejection);
    }
}
