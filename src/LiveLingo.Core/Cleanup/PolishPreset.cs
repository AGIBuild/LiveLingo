namespace LiveLingo.Core.Cleanup;

/// <summary>Which deterministic safety check validates a polish output before it replaces the user's words.</summary>
public enum PolishGateKind
{
    /// <summary>Character-level alignment preserving sentence-level wording, length bounds and spoken phrasing.</summary>
    Verbatim,

    /// <summary>Fact-anchor check permitting headings, lists and reorganization while locking active numbers, literals and execution boundaries.</summary>
    FactAnchor
}

/// <summary>How a finished document is fed to the polish model.</summary>
public enum PolishRouteKind
{
    /// <summary>Segmented up to 800 characters per piece while preserving explicit line layout.</summary>
    Piecewise,

    /// <summary>Synthesized holistically across up to 2,400 characters in one window (or map-extracted then reduced once when longer).</summary>
    Holistic
}

/// <summary>
/// One scenario polish mode: either one of the five built-in modes (<see cref="PolishPresets"/>) or a user-created preset.
/// For built-in modes, an empty <see cref="CustomInstructions"/> means use <see cref="DefaultInstructions"/>, so shipped
/// prompt improvements take effect automatically unless the user explicitly tuned that mode.
/// </summary>
public sealed record PolishPreset(
    string Id,
    string Name,
    string Description,
    PolishGateKind Gate,
    PolishRouteKind Route,
    bool BuiltIn,
    bool Enabled,
    string DefaultInstructions,
    string CustomInstructions = "",
    bool ForbidExecution = false,
    bool RelaxedVerbatim = false,
    bool AllowSelectiveExtraction = false,
    string ShortcutKey = "")
{
    public string EffectiveInstructions =>
        BuiltIn ? (PolishStyle.IsUsable(CustomInstructions) ? PolishStyle.NormalizeText(CustomInstructions) : DefaultInstructions)
                : PolishStyle.NormalizeText(CustomInstructions.Length > 0 ? CustomInstructions : DefaultInstructions);

    public bool IsCustomized =>
        BuiltIn && PolishStyle.IsUsable(CustomInstructions)
        && PolishStyle.NormalizeText(CustomInstructions) != PolishStyle.NormalizeText(DefaultInstructions);

    public bool IsDefaultNatural =>
        Id == PolishPresets.NaturalId && !IsCustomized;
}

/// <summary>A one-click starter template offered when creating a custom polish mode in Settings.</summary>
public sealed record PolishStarterTemplate(
    string Id,
    string DefaultName,
    string DefaultDescription,
    PolishGateKind Gate,
    string Instructions);

/// <summary>
/// Canonical registry of built-in scenario polish modes and starter templates (design: quality-first AI-pairing and
/// AI-office polish). Built-in identifiers are stable across locales; user-facing labels come from localization resources.
/// </summary>
public static class PolishPresets
{
    public const string AiTaskId = "ai-task";
    public const string NaturalId = "natural";
    public const string FormalId = "formal";
    public const string TodoId = "todo";
    public const string ReportId = "report";

    public const int MaxNameLength = 24;
    public const int MaxDescriptionLength = 60;

    public const string AiTaskInstructions = """
        润色要求（AI 任务指令）：
        1. 思维撤回彻底剪枝：说话人中途自我否定或改口的内容（如“先设成 10 秒，不对，关掉重试改成 30 秒”、“本来想改 A，算了只改 B”），被推翻的旧方案与旧数值必须彻底剔除，只保留最终生效意图。
        2. 目标倒装归位：若说话人先铺垫现象、最后才说出真正要 AI 执行的动作（如“先排查原因列出来，别改代码”），必须把核心动作提炼到最开头。
        3. 正负护栏显式分离：原文所有限制、禁令与不变边界（如“保持公共 API 不变”、“不要动单测”、“只改错误处理”、“是 30 秒不是 30 分钟”），必须完整保留否定极性并独立列出。
        4. 自适应篇幅（严禁八股套壳与脑补）：
           - 短指令直出：若原文只是 1～2 句简短操作或中途纠偏（有效内容 ≤ 80 字，无复杂多层背景），直接输出 1～3 行精炼指令，严禁硬套 `### 任务目标`、`### 交付要求` 等标题！
           - 复杂任务分节：当原文含多维背景与多条约束时，仅按原话实际包含的要素选用 `### 任务目标`、`### 上下文与目标行为`、`### 硬性约束`、`### 交付要求` 组织（未提及的小节直接省略，绝不凑数）。
        5. 原文提及的所有代码类名、方法/函数名（如 `RefreshAsync`）、文件名、配置键、状态码与路径必须全部完整保留并使用反引号包裹，切勿在概括背景或原因时漏掉具体函数名或符号。

        示例 1（复杂委派）：
        原文：呃我想改一下登录模块，先把重试设成 3 次吧，哦不对，网关层有重试了，客户端干脆关掉自动重试，只保留 401 刷新 token，超时改成 30 秒不是 30 分钟。你先帮我查下 AuthInterceptor 还有没有隐式重试，列出来给我看，先千万别改代码。
        输出：
        ### 任务目标
        排查 `AuthInterceptor` 中是否存在其他隐式重试逻辑，并给出关闭客户端自动重试的改造方案（先输出排查清单与方案，不要直接修改代码）。

        ### 上下文与目标行为
        - 网关层已具备重试机制，客户端关闭自动重试，仅保留 `401` 状态码刷新 Token 的流程。
        - 客户端超时时间设定为 30 秒（严格区分单位，切勿误设为 30 分钟）。

        ### 硬性约束
        - 当前阶段只输出排查清单与修改方案，禁止直接改动任何业务代码。

        示例 2（短轮次纠偏）：
        原文：呃不对，不要去改 CacheService 的公共 API 签名，外部好多地方调了，你直接在它内部方法里加一层空值兜底就行，单测先别动。
        输出：
        不要修改 `CacheService` 的公共 API 签名（外部存在多处调用）；请仅在其内部方法中补充空值兜底逻辑，且暂不改动现有单元测试。
        """;

    public const string FormalInstructions = """
        润色要求（得体书面）：
        1. 去除口头语、犹豫停顿与碎句，剪除说话人当场自我否定的半截废话，改写为逻辑连贯、简洁克制的书面段落。
        2. 显式理顺因果、转折与条件关系，修正模糊口语指代，完整保留第一人称立场、生效数字、否定边界与英文/代码专有名词。
        3. 严禁使用“赋能、抓手、闭环、对齐颗粒度、综上所述、特此函告”等公文套话，适合正式邮件、PR 评审意见或跨团队沟通直接发送。
        """;

    public const string TodoInstructions = """
        润色要求（待办与派单）：
        1. 从口述中剥离纯背景铺垫、寒暄、感叹杂音与已被说话人当场否决的设想，提炼需要执行或跟进核实的行动项，输出 Markdown 任务清单。
        2. 每条严格以 `- [ ] ` 加明确动词开头；合并对同一事项的重复叮嘱。
        3. 原话明确提及了负责人、前置条件（如“等 CI 跑完后”）或时间节点的，在句末括号内保留（如 `（负责人：小王 · 会后）`）；原话未提及负责人或截止时间时，严禁凭空编造。
        4. 若原话包含待确认的具体安排或疑问（如核实休假起止日期、确认方案细节），提炼为 `- [ ] 确认…` 的跟进项；若全篇纯属无任何事项的闲聊感叹，仅输出一行“（原话未包含明确待办事项）”，切勿强凑任务，且严禁在输出中附带“原话提及…”、“结合上下文推断…”等编辑批注。
        """;

    public const string ReportInstructions = """
        润色要求（汇报与纪要）：
        1. 采用结论先行（BLUF）结构重组流水账口述：首段 `### 核心结论` 用 1～2 句话直接给出当前核心观点、整体状态或拍板决定（仅归纳原话明确说出的信息，原话未点明的具体子类别切勿用常识补全）。
        2. 随后按原文实际内容组织 `### 关键决策与进展`（若为访谈或观点分享也可按 `### 核心要点` 分条归纳，完整保留所有生效指标数值、年限、百分比、组件名与正反条件对比）；仅当原文确实包含后续待办或跟进计划时才输出 `### 下一步行动`，若原文未提及任何待办动作，必须直接省略该小节，严禁输出 `- 无（原文未提及具体待办事项）` 等空占位内容。
        3. 语言客观精炼，自动剔除无关寒暄与口头感叹杂音，不夸大进展，不编造原因与数据，严禁在括号内添加“原文提及…”等编辑批注。
        """;

    public static IReadOnlyList<PolishPreset> BuiltIns { get; } =
    [
        new(AiTaskId, "AI 任务指令", "倒叙归位、剪除改口废案、分离硬性护栏（短话免套壳）",
            PolishGateKind.FactAnchor, PolishRouteKind.Holistic, BuiltIn: true, Enabled: true,
            AiTaskInstructions, ForbidExecution: true, ShortcutKey: "1"),
        new(NaturalId, "口语顺句", "接碎句、去口头语与改口残片，保留真人语气",
            PolishGateKind.Verbatim, PolishRouteKind.Holistic, BuiltIn: true, Enabled: true,
            PolishStyle.Default, ForbidExecution: true, RelaxedVerbatim: true, ShortcutKey: "2"),
        new(FormalId, "得体书面", "邮件、PR 评论、克制书面段落（零公文黑话）",
            PolishGateKind.FactAnchor, PolishRouteKind.Holistic, BuiltIn: true, Enabled: true,
            FormalInstructions, RelaxedVerbatim: true, ForbidExecution: true, ShortcutKey: "3"),
        new(TodoId, "待办与派单", "过滤闲聊废案，提取 - [ ] 行动项、负责人与条件",
            PolishGateKind.FactAnchor, PolishRouteKind.Holistic, BuiltIn: true, Enabled: true,
            TodoInstructions, ForbidExecution: true, AllowSelectiveExtraction: true, ShortcutKey: "4"),
        new(ReportId, "汇报与纪要", "结论先行（BLUF），归纳关键决策与下一步",
            PolishGateKind.FactAnchor, PolishRouteKind.Holistic, BuiltIn: true, Enabled: true,
            ReportInstructions, ForbidExecution: true, AllowSelectiveExtraction: true, ShortcutKey: "5")
    ];

    public static IReadOnlyList<PolishStarterTemplate> StarterTemplates { get; } =
    [
        new("bug-report", "Bug 缺陷与复现", "提炼复现步骤、预期行为与实际报错", PolishGateKind.FactAnchor,
            """
            将口述整理为结构化缺陷报告：
            - `### 复现步骤`（按顺序列出触发操作、输入与配置参数）
            - `### 预期行为`
            - `### 实际现象与影响范围`
            完整保留所有生效数值、错误码与代码组件名，未提及的环境信息严禁编造。
            """),
        new("spec-ac", "需求规格（Spec/AC）", "提炼解决的问题、非目标边界与验收标准", PolishGateKind.FactAnchor,
            """
            面向 Spec-Driven AI 研发，将口述整理为轻量规格说明：
            - `### 解决的问题`
            - `### 变更范围与非目标（Out of Scope）`
            - `### 验收标准`（仅基于原话已声明的条件逐条列出，不凭空扩写新功能）。
            """),
        new("git-commit", "Commit 说明", "生成简明提交首行与核心改动要点", PolishGateKind.FactAnchor,
            """
            将口述提炼为规范的代码提交说明：
            - 首行：50 字内简明概括本次改动意图与模块；
            - 空一行后用 `- ` 列出核心变更点与兼容边界（保留精确接口名与数值）。
            """)
    ];

    public static PolishPreset FindBuiltIn(string id) =>
        BuiltIns.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
        ?? BuiltIns.First(p => p.Id == NaturalId);

    public static bool IsBuiltInId(string? id) =>
        id is not null && BuiltIns.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var trimmed = name.Trim();
        return trimmed.Length is >= 1 and <= MaxNameLength && !trimmed.Any(char.IsControl);
    }

    public static bool IsValidDescription(string? description)
    {
        if (description is null)
            return true;
        var trimmed = description.Trim();
        return trimmed.Length <= MaxDescriptionLength && !trimmed.Any(char.IsControl);
    }

    public static PolishPreset Resolve(string? modeId, IReadOnlyList<PolishPreset>? configured = null)
    {
        var list = configured is { Count: > 0 } ? configured : BuiltIns;
        if (!string.IsNullOrWhiteSpace(modeId)
            && list.FirstOrDefault(p => string.Equals(p.Id, modeId, StringComparison.Ordinal)) is { } match)
        {
            return match;
        }
        return list.FirstOrDefault(p => p.Id == NaturalId) ?? FindBuiltIn(NaturalId);
    }
}
