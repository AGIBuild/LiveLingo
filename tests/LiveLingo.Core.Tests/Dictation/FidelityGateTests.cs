using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Dictation;

public class FidelityGateTests
{
    [Fact]
    public void FinalPolish_MayUseParagraphsWithoutChangingContent()
    {
        const string raw = "先改接口。然后补测试。";
        const string polished = "先改接口。\n然后补测试。";
        Assert.True(FidelityGate.Check(raw, polished, allowParagraphs: true).Accepted);
        Assert.False(FidelityGate.Check(raw, polished).Accepted);
    }

    [Fact]
    public void Accepts_a_cleanup_that_removes_fillers_and_keeps_content()
    {
        var gate = FidelityGate.Check("呃，那个，我们我们今天讨论 Redis 的过期时间。", "我们今天讨论 Redis 的过期时间。");
        Assert.True(gate.Accepted, gate.Rejection?.Reason.ToString());
    }

    [Fact]
    public void Rejects_a_lost_number() =>
        Assert.Equal(new GateRejection(GateReason.NumberLost, "8080"), FidelityGate.Check("端口改成 8080。", "端口改一下。").Rejection);

    [Fact]
    public void Rejects_a_lost_technical_term() =>
        Assert.Equal(new GateRejection(GateReason.TermLost, "cancellationtoken"), FidelityGate.Check("用 CancellationToken 取消请求。", "用令牌取消请求。").Rejection);

    [Fact]
    public void Rejects_an_expanded_output() =>
        Assert.Equal(GateReason.TooLong, FidelityGate.Check("补测试。", "补测试，并且重构整个下载模块，同时升级所有依赖。").Rejection?.Reason);

    [Fact]
    public void Rejects_an_empty_output() =>
        Assert.Equal(new GateRejection(GateReason.Empty), FidelityGate.Check("补测试。", "  ").Rejection);

    [Fact]
    public void Term_check_is_case_insensitive() =>
        Assert.True(FidelityGate.Check("注入 viewmodel 到页面。", "注入 ViewModel 到页面。").Accepted);

    [Fact]
    public void Accepts_a_short_self_correction_removal()
    {
        var gate = FidelityGate.Check("把下载超时设成十秒，不对，设成三十秒。", "把下载超时设成三十秒。");
        Assert.True(gate.Accepted, gate.Rejection?.Reason.ToString());
    }

    [Fact]
    public void Accepts_light_words_trimmed_around_fillers()
    {
        var gate = FidelityGate.Check(
            "如果说AI能预测股市，选择股票的话。那么，我的生活就轻松多了呀。行业大牛是怎么平衡他们的这个工作的？再清理一下现有的历史输入。",
            "如果说 AI 能预测股市、选择股票，我的生活就轻松多了。行业大牛是怎么平衡工作的？再清理现有的历史输入。");
        Assert.True(gate.Accepted, gate.Rejection?.ToString());
    }

    [Fact]
    public void Rejects_too_many_small_drops()
    {
        var gate = FidelityGate.Check(
            "我们把接口这边的鉴权改成令牌，再把缓存那边的过期改成半小时，把日志这边的级别调到警告。",
            "我们把接口的鉴权改成令牌，再把缓存的过期改成半小时，把日志的级别调到警告。");
        Assert.Equal(GateReason.Deleted, gate.Rejection?.Reason);
    }

    [Fact]
    public void Rejects_a_dropped_lone_negation() =>
        Assert.Equal(GateReason.Deleted, FidelityGate.Check("这个接口不要加重试，其他保持不变。", "这个接口要加重试，其他保持不变。").Rejection?.Reason);

    [Fact]
    public void Rejects_a_dropped_negated_word_even_though_it_is_short() =>
        Assert.Equal(GateReason.Deleted, FidelityGate.Check("发布之前没有评审，我们需要先补上。", "发布之前，我们需要先补上。").Rejection?.Reason);

    [Fact]
    public void Accepts_a_restated_clause_removed_from_a_passage()
    {
        var gate = FidelityGate.Check(
            "因为。V S Code 对于中文的语音识别不是。准确率很高。准确率不是很高，所以。我写了一个验证。第二个呢是灰度范围。灰度先放百分之十的用户。",
            "因为 VS Code 对于中文的语音识别准确率不是很高，所以我写了一个验证。第二个是灰度范围，先放百分之十的用户。");
        Assert.True(gate.Accepted, gate.Rejection?.ToString());
    }

    [Fact]
    public void Rejects_a_dropped_negation_even_when_the_words_occur_elsewhere() =>
        Assert.Equal(
            GateReason.Deleted,
            FidelityGate.Check("发布不能提前。发布时间要确认。", "发布时间要确认，发布。").Rejection?.Reason);

    [Fact]
    public void Accepts_a_term_the_speaker_corrected_away()
    {
        var gate = FidelityGate.Check(
            "绑定到按钮的 click 事件。啊不对，是 command 属性。",
            "绑定到按钮的 Command 属性。");
        Assert.True(gate.Accepted, gate.Rejection?.ToString());
    }

    [Fact]
    public void Rejects_a_dropped_term_when_only_an_ordinary_negation_follows() =>
        Assert.Equal(
            new GateRejection(GateReason.TermLost, "token"),
            FidelityGate.Check("这个 token 不是很长，我们先缓存起来。", "这个不是很长，我们先缓存起来。").Rejection);

    [Fact]
    public void Rejects_a_dropped_clause_even_when_the_length_ratio_is_fine()
    {
        var gate = FidelityGate.Check(
            "我们先把下载接口改掉，然后补一个取消操作的测试，最后更新一下文档和发布说明。",
            "我们先把下载接口改掉，最后更新一下文档和发布说明。");
        Assert.False(gate.Accepted);
        Assert.Equal(GateReason.Deleted, gate.Rejection?.Reason);
    }

    [Fact]
    public void Rejects_output_that_echoes_the_prompt_layout()
    {
        // Observed with a Shaanxi-accent recording: the model prefixed its answer with the glossary and target labels.
        var gate = FidelityGate.Check(
            "整理模型只能修改置顶的句子，前面的内容只作为制读商家文。",
            "词表：Windows、macOS\n目标：整理模型只能修改置顶的句子，前面的内容只作为制读商家文。",
            ["Windows", "macOS"]);
        Assert.False(gate.Accepted);
        Assert.Equal(GateReason.MultiLine, gate.Rejection?.Reason);
    }

    [Theory]
    [InlineData("我说：“子轩啊，你就不能稍微低调一点，要学会入乡随俗嘛。”", "我说：“子轩啊，你就不能稍微低调一点，要学会入乡随俗嘛。")]
    [InlineData("他说（先不管这个）然后走了。", "他说先不管这个）然后走了。")]
    public void Rejects_output_that_unbalances_quotes_or_brackets(string raw, string cleaned)
    {
        // Observed on real speech: the model dropped only the closing quotation mark.
        var gate = FidelityGate.Check(raw, cleaned);
        Assert.False(gate.Accepted);
        Assert.Equal(GateReason.Brackets, gate.Rejection?.Reason);
    }

    [Fact]
    public void Accepts_dropping_a_sentence_final_period_and_keeps_quote_pairs()
    {
        Assert.True(FidelityGate.Check("我说：“好的。”然后走了。", "我说：“好的。”然后走了").Accepted);
    }

    [Fact]
    public void Rejects_silently_deleting_a_garbled_word()
    {
        // Observed: "这意思的" was a garbled "Redis"; the cleaner treated it as noise and removed it, hiding the error.
        var gate = FidelityGate.Check("我觉得可能是这意思的过期时间设置的不对。", "我觉得可能是过期时间设置的不对。");
        Assert.False(gate.Accepted);
        Assert.Equal(GateReason.Deleted, gate.Rejection?.Reason);
    }

    [Fact]
    public void Accepts_removing_a_stutter_and_a_single_noise_character()
    {
        Assert.True(FidelityGate.Check("我们我们今天讨论缓存。", "我们今天讨论缓存。").Accepted);
        Assert.True(FidelityGate.Check("当文字母前缀里出现完整的句子时。", "当文字前缀里出现完整的句子时。").Accepted);
    }

    [Fact]
    public void A_glossary_replacement_pays_for_deleting_the_gibberish_it_replaced()
    {
        const string raw = "我想在阿德里面注入一个 e，然后用 solution 绑定到按钮上。";
        const string cleaned = "我想在 Avalonia 里面注入一个 e，然后用 Solution 绑定到按钮上。";

        Assert.True(FidelityGate.Check(raw, cleaned, ["Avalonia"]).Accepted);
        Assert.False(FidelityGate.Check(raw, cleaned).Accepted); // without the glossary the same edit is an unexplained deletion
    }

    [Fact]
    public void Rejects_the_dropped_clause_observed_with_a_4B_model()
    {
        // The second clause vanished; the previous gate (length ratio >= 0.4 only) let this through.
        var gate = FidelityGate.Check("整理我行只能修改只用在句子，前面的内容只作为只读上下文。", "我行只能修改只用在句子");
        Assert.Equal(GateReason.TooShort, gate.Rejection?.Reason);
    }

    [Fact]
    public void Rejects_an_added_clause_even_when_the_length_stays_in_range()
    {
        var gate = FidelityGate.Check(
            "先把下载接口改掉，然后补一个取消操作的测试，最后更新文档和发布说明。",
            "先把下载接口改掉，然后重写全部模块并升级依赖项，最后更新文档和发布说明。");
        Assert.Equal(GateReason.Added, gate.Rejection?.Reason);
    }

    [Fact]
    public void Accepts_a_homophone_fix()
    {
        var gate = FidelityGate.Check("这个接口不要加重视，除非返回五零三。", "这个接口不要加重试，除非返回五零三。");
        Assert.True(gate.Accepted, gate.Rejection?.Reason.ToString());
    }

    [Fact]
    public void A_glossary_term_may_replace_a_misrecognized_english_word()
    {
        const string raw = "整个流程要在 Windows 和 Moux 上使用同一套核心逻辑。";
        const string cleaned = "整个流程要在 Windows 和 macOS 上使用同一套核心逻辑。";

        Assert.True(FidelityGate.Check(raw, cleaned, ["macOS"]).Accepted);
        Assert.False(FidelityGate.Check(raw, cleaned).Accepted); // without the glossary it is just a lost term
    }

    [Fact]
    public void A_glossary_may_turn_phonetic_gibberish_into_identifiers_even_though_the_text_gets_much_longer()
    {
        // Observed: the cleaner reconstructed exactly what was spoken, and a plain length check rejected it (1.89x).
        const string raw = "我想在阿罗亚的 F Morder 里面注入一个 e，然后用 solution 绑定到按钮上。";
        const string cleaned = "我想在 Avalonia 的 ViewModel 里面注入一个 ILogger，然后用 CommunityToolkit 的 RelayCommand 绑定到按钮上。";
        string[] glossary = ["Avalonia", "ViewModel", "ILogger", "CommunityToolkit", "RelayCommand"];

        var gate = FidelityGate.Check(raw, cleaned, glossary);
        Assert.True(gate.Accepted, gate.Rejection?.Reason.ToString());
    }

    [Fact]
    public void A_glossary_term_may_replace_a_run_of_misrecognized_english_words()
    {
        // Observed: "Max Oce" is one phonetic guess for macOS; counting it as two lost words rejected a correct repair.
        var gate = FidelityGate.Check(
            "整个流程要在 Windows 和 Max Oce 上使用同一套核心逻辑。",
            "整个流程要在 Windows 和 macOS 上使用同一套核心逻辑。", ["macOS"]);
        Assert.True(gate.Accepted, gate.Rejection?.Reason.ToString());
    }

    [Fact]
    public void Numbers_are_never_invented_from_garbled_numerals()
    {
        // Observed: "承包千零八十" (really 8080) was turned into 1080, a guess no check could otherwise see.
        var gate = FidelityGate.Check("端口在承包千零八十，内存四个 G。", "端口在 1080，内存四个 G。");
        Assert.False(gate.Accepted);
        Assert.Equal(new GateRejection(GateReason.NumberInvented, "1080"), gate.Rejection);
    }

    [Fact]
    public void An_english_term_that_is_neither_spoken_nor_in_the_glossary_is_rejected()
    {
        var gate = FidelityGate.Check("把接口改掉，然后补测试。", "把 Kubernetes 接口改掉，然后补测试。", ["Avalonia"]);
        Assert.False(gate.Accepted);
        Assert.Equal(new GateRejection(GateReason.TermNotInGlossary, "kubernetes"), gate.Rejection);
    }

    [Fact]
    public void Accepts_the_real_bilingual_agent_skill_dictation_polish()
    {
        const string raw =
            "我觉得啊，这个翻译语音输入工具最大的问题还是识别率不高，呃，特别是中英混杂的情况，比如说我说使用agent或者skill，呃。" +
            "那么就会跟我的原原原话不一致。我觉得最好的办法。可能还是你需要从根子上去解决一下这个问题，怎么去修。" +
            "有可能你需要一定的语音输入压力，所以我说的这一段就是语音输入压力。如果说你还需要更强的。呃，Agent用色描述，或者说Scale机制。也可以澄清出来。" +
            "因为你现在，在我输入过程中，你会把我的没说完的一句话给回车换行，可能会本身导致呃理解错位。" +
            "我不确定会不会有这样的问题，但是你可能需要去关注一下，分析一下，是不是会有这样的问题。" +
            "另外，现在从语音润色切换到翻译的时候，需要较长时间的等待，才才能把翻译结果给你。" +
            "或者说，从翻译切换到润色的时候，也是有类似问题。那为什么花这么长时间才能切换过来呢？原因是什么？也需要给我分析一下。" +
            "你可以不必急于开发，但是呢，先给我方案和定位，分析出来的原因。";

        const string polished =
            "我觉得翻译语音输入工具最大的问题还是识别率不高，特别是中英混杂的情况，比如说使用Agent或者Skill，就会跟我的原话不一致。" +
            "我觉得最好的办法是你需要从根子上解决这个问题，怎么去修，可能需要语音输入样例，所以我说的这段就是语音输入样例，" +
            "如果你还需要更强的Agent润色描述，或者SKILL机制也可以澄清出来，因为现在在我输入过程中，你会把我的没说完的一句话给回车换行，可能会本身导致理解错位。" +
            "我不确定会不会有这样的问题，但是你可能需要去关注一下，分析一下是不是会有这样的问题。" +
            "另外，现在从语音润色切换到翻译的时候，需要较长时间的等待才能把翻译结果给你，或者说从翻译切换到润色的时候也是有类似问题，" +
            "那为什么花这么长时间才能切换过来呢？原因是什么？也需要给我分析一下，你可以不必急于开发，但是先给我方案和定位，分析出来的原因。";

        var gateWithBuiltIn = FidelityGate.Check(raw, polished, Glossary.BuiltInTerms, allowParagraphs: true);
        Assert.True(gateWithBuiltIn.Accepted, gateWithBuiltIn.Rejection?.ToString());

        // Even with built-in glossary disabled, "skill" was already spoken in sentence 1 so repairing "Scale" -> "SKILL" in sentence 7 is accepted.
        var gateWithoutGlossary = FidelityGate.Check(raw, polished, [], allowParagraphs: true);
        Assert.True(gateWithoutGlossary.Accepted, gateWithoutGlossary.Rejection?.ToString());
    }

    [Fact]
    public void Accepts_interrogative_a_not_a_rewritten_to_shifou_without_excusing_dropped_negations()
    {
        var accepted = FidelityGate.Check(
            "我不确定会不会有这样的问题，但是你可能需要去关注一下，分析一下，是不是会有这样的问题。",
            "我不确定是否会有这样的问题，但你可能需要关注一下，分析一下是否会有这样的问题。");
        Assert.True(accepted.Accepted, accepted.Rejection?.ToString());

        var rejected = FidelityGate.Check("我不确定会不会有这样的问题。", "我确定会有这样的问题。");
        Assert.Equal(GateReason.Deleted, rejected.Rejection?.Reason);
    }

    [Fact]
    public void Accepts_triple_stutter_and_explicit_accent_corrections()
    {
        Assert.True(FidelityGate.Check("那么就会跟我的原原原话不一致。", "那样就会和我的原话不一致。").Accepted);

        var corrected = FidelityGate.Check(
            "下个阶段把六零九里面的隐秘命令修一下。",
            "下个阶段把 LiveLingo 里面的 init 命令修一下。",
            corrections: [new GlossaryCorrection("六零九", "LiveLingo"), new GlossaryCorrection("隐秘", "init")]);
        Assert.True(corrected.Accepted, corrected.Rejection?.ToString());
    }

    [Fact]
    public void Prefix_glossary_terms_do_not_double_count_longer_identifiers()
    {
        // "GitHub" alone must not count as both "GitHub" and "Git" to excuse deleting an unrelated English word "Redis".
        var gate = FidelityGate.Check(
            "把代码推到 Gethub，然后清理 Redis 缓存。",
            "把代码推到 GitHub，然后清理缓存。",
            ["Git", "GitHub"]);
        Assert.Equal(GateReason.TermLost, gate.Rejection?.Reason);
    }

    [Theory]
    [InlineData("他不光要按照学校节奏来，还要找到自己的节奏。", "他不仅不按照学校节奏来，还要找到自己的节奏。")]
    [InlineData("今天留什么作业，他就写什么作业。", "今天留什么作业，他就不写什么作业。")]
    public void Rejects_an_invented_negation_that_flips_clause_polarity(string raw, string cleaned)
    {
        var gate = FidelityGate.Check(raw, cleaned);
        Assert.False(gate.Accepted);
        Assert.Equal(GateReason.Added, gate.Rejection?.Reason);
    }

    [Fact]
    public void Accepts_trimming_non_numeric_yi_aspect_words_while_protecting_counted_quantities()
    {
        var accepted = FidelityGate.Check(
            "你看，他这会学那个方法，一会儿又去试别的。",
            "他这会儿学那个方法，又去试别的。");
        Assert.True(accepted.Accepted, accepted.Rejection?.ToString());

        var rejected = FidelityGate.Check(
            "坚持一个方法，这是一口深井。",
            "坚持方法，这是深井。");
        Assert.Equal(GateReason.Deleted, rejected.Rejection?.Reason);
    }

    [Fact]
    public void Accepts_arabic_digits_retracted_by_spoken_self_correction_without_excusing_sole_negated_numbers()
    {
        var accepted = FidelityGate.Check(
            "把下载超时设成 10 秒，不对，设成 30 秒。",
            "把下载超时设成 30 秒。");
        Assert.True(accepted.Accepted, accepted.Rejection?.ToString());

        var contrastAccepted = FidelityGate.Check(
            "超时设为 30 秒，不是 15 分钟，请确认好。",
            "超时设为 30 秒，请确认好。",
            relaxedVerbatim: true);
        Assert.True(contrastAccepted.Accepted, contrastAccepted.Rejection?.ToString());

        // When 8080 is the only number in the statement, "不是 8080" is the point of the sentence and must never be dropped.
        var soleNegatedRejected = FidelityGate.Check("端口不是 8080。", "端口改一下。");
        Assert.Equal(new GateRejection(GateReason.NumberLost, "8080"), soleNegatedRejected.Rejection);
    }

    [Fact]
    public void Accepts_prefix_truncated_and_consonant_close_glossary_repairs_even_when_canonical_already_occurs_later()
    {
        var ageToAgent = FidelityGate.Check(
            "把一个产品经理变成一个前端 age 的一个测试，这就是我们这个 Agent 思路。",
            "把一个产品经理变成一个前端 Agent 和一个测试，这就是我们这个 Agent 思路。",
            ["Agent"]);
        Assert.True(ageToAgent.Accepted, ageToAgent.Rejection?.ToString());

        var ipsecToDeepSeek = FidelityGate.Check(
            "今天借着这个话头聊聊 ipsec 最新报告，看看 DeepSeek 是怎么拆的。",
            "今天借着这个话头聊聊 DeepSeek 最新报告，看看 DeepSeek 是怎么拆的。",
            ["DeepSeek"]);
        Assert.True(ipsecToDeepSeek.Accepted, ipsecToDeepSeek.Rejection?.ToString());
    }

    [Fact]
    public void SpokenDocument_CleansBgmLoopsStutteredNegationsAndAsrNoise_WhileGuardingCodeSymbolsAndMarkdown()
    {
        const string noisyRaw =
            "噔噔噔噔噔噔噔噔。感谢晚风的小心心啊！打的还不错，的可以点点人气票呀，点人气票呀。" +
            "是不是没有皮肤就没有手感？当然不是啊！我我他那五分儿，说实在的上半五分啊！是啊，上半五分啊！皮肤不重要。" +
            "<thibbe Na na牛魔来，你大招呀？OK，真好，给牛没收了。来得好，来得好。" +
            "哥，能不能给打给给龙输了？哥，能不能给龙输了？给龙输了，没有别的意外了，直接打呀。Like any power.快打！快打！";

        const string polished =
            "感谢晚风的小心心！打得还不错，可以点点人气票。" +
            "是不是没有皮肤就没有手感？当然不是！他那五分，说实在的上半五分，皮肤不重要。" +
            "牛魔来，你大招？OK，真好，给牛魔收了。来得好。" +
            "哥，能不能给龙输了？没有别的意外了，直接打，快打！";

        var gate = FidelityGate.Check(noisyRaw, polished, Glossary.BuiltInTerms, allowParagraphs: true, relaxedVerbatim: true, spokenDocument: true);
        Assert.True(gate.Accepted, gate.Rejection?.ToString());

        // Restoring a proper English entity ("Rockstar") from spoken Chinese ("R星") in spokenDocument mode is accepted.
        var rockstar = FidelityGate.Check(
            "这次 R星 往里面塞的细节真的是有史以来前所未有。",
            "这次 Rockstar 往里面塞的细节真的是有史以来前所未有。",
            Glossary.BuiltInTerms,
            allowParagraphs: true,
            relaxedVerbatim: true,
            spokenDocument: true);
        Assert.True(rockstar.Accepted, rockstar.Rejection?.ToString());

        // Code symbols and glossary terms remain strictly protected in spokenDocument mode.
        var droppedSymbol = FidelityGate.Check(
            "请把 CacheService 的超时调成三十秒。",
            "请把缓存服务的超时调成三十秒。",
            Glossary.BuiltInTerms,
            allowParagraphs: true,
            relaxedVerbatim: true,
            spokenDocument: true);
        Assert.Equal(GateReason.TermLost, droppedSymbol.Rejection?.Reason);

        // Inventing Markdown headings in natural Verbatim mode is refused.
        var inventedHeading = FidelityGate.Check(
            "先把接口改掉，然后补测试。",
            "### 目标\n先把接口改掉，然后补测试。",
            Glossary.BuiltInTerms,
            allowParagraphs: true,
            relaxedVerbatim: true,
            spokenDocument: true);
        Assert.Equal(GateReason.Added, inventedHeading.Rejection?.Reason);
    }
}
