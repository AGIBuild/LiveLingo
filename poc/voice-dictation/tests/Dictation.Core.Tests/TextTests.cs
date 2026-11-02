using Dictation.Core;

namespace Dictation.Core.Tests;

public class SentenceSplitterTests
{
    [Fact]
    public void Splits_on_chinese_sentence_punctuation_and_keeps_the_unfinished_tail()
    {
        var (sentences, tail) = SentenceSplitter.Split("先补测试。然后改接口！还有一个问题");
        Assert.Equal(["先补测试。", "然后改接口！"], sentences);
        Assert.Equal("还有一个问题", tail);
    }

    [Theory]
    [InlineData("版本号是 10.0.301，端口 8080。")]
    [InlineData("打开 Program.cs 看一下。")]
    public void Does_not_split_inside_numbers_or_identifiers(string text)
    {
        var (sentences, tail) = SentenceSplitter.Split(text);
        Assert.Single(sentences);
        Assert.Equal("", tail);
    }

    [Fact]
    public void A_dot_followed_by_space_ends_an_english_sentence()
    {
        var (sentences, tail) = SentenceSplitter.Split("Use the token. Then retry");
        Assert.Equal(["Use the token."], sentences);
        Assert.Equal("Then retry", tail);
    }

    [Fact]
    public void Trailing_quotes_stay_with_their_sentence()
    {
        var (sentences, _) = SentenceSplitter.Split("他说“好的。”然后走了。");
        Assert.Equal(["他说“好的。”", "然后走了。"], sentences);
    }

    [Theory]
    [InlineData("设成", "30秒", "设成30秒")]
    [InlineData("用 Redis", "cache", "用 Redis cache")]
    [InlineData("", "你好", "你好")]
    public void Join_adds_a_space_only_between_ascii_words(string left, string right, string expected) =>
        Assert.Equal(expected, SentenceSplitter.Join(left, right));
}

public class FidelityGateTests
{
    [Fact]
    public void Accepts_a_cleanup_that_removes_fillers_and_keeps_content()
    {
        var gate = FidelityGate.Check("呃，那个，我们我们今天讨论 Redis 的过期时间。", "我们今天讨论 Redis 的过期时间。");
        Assert.True(gate.Accepted, gate.Reason);
    }

    [Fact]
    public void Rejects_a_lost_number() =>
        Assert.False(FidelityGate.Check("端口改成 8080。", "端口改一下。").Accepted);

    [Fact]
    public void Rejects_a_lost_technical_term() =>
        Assert.False(FidelityGate.Check("用 CancellationToken 取消请求。", "用令牌取消请求。").Accepted);

    [Fact]
    public void Rejects_an_expanded_output() =>
        Assert.False(FidelityGate.Check("补测试。", "补测试，并且重构整个下载模块，同时升级所有依赖。").Accepted);

    [Fact]
    public void Rejects_an_empty_output() =>
        Assert.False(FidelityGate.Check("补测试。", "  ").Accepted);

    [Fact]
    public void Term_check_is_case_insensitive() =>
        Assert.True(FidelityGate.Check("注入 viewmodel 到页面。", "注入 ViewModel 到页面。").Accepted);

    [Fact]
    public void Accepts_a_short_self_correction_removal()
    {
        var gate = FidelityGate.Check("把下载超时设成十秒，不对，设成三十秒。", "把下载超时设成三十秒。");
        Assert.True(gate.Accepted, gate.Reason);
    }

    [Fact]
    public void Rejects_a_dropped_clause_even_when_the_length_ratio_is_fine()
    {
        var gate = FidelityGate.Check(
            "我们先把下载接口改掉，然后补一个取消操作的测试，最后更新一下文档和发布说明。",
            "我们先把下载接口改掉，最后更新一下文档和发布说明。");
        Assert.False(gate.Accepted);
        Assert.Contains("deleted", gate.Reason);
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
        Assert.Equal("multi-line output", gate.Reason);
    }

    [Theory]
    [InlineData("我说：“子轩啊，你就不能稍微低调一点，要学会入乡随俗嘛。”", "我说：“子轩啊，你就不能稍微低调一点，要学会入乡随俗嘛。")]
    [InlineData("他说（先不管这个）然后走了。", "他说先不管这个）然后走了。")]
    public void Rejects_output_that_unbalances_quotes_or_brackets(string raw, string cleaned)
    {
        // Observed on real speech: the model dropped only the closing quotation mark.
        var gate = FidelityGate.Check(raw, cleaned);
        Assert.False(gate.Accepted);
        Assert.Equal("quotes or brackets unbalanced", gate.Reason);
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
        Assert.Contains("deleted", gate.Reason);
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
        Assert.False(gate.Accepted);
    }

    [Fact]
    public void Rejects_an_added_clause_even_when_the_length_stays_in_range()
    {
        var gate = FidelityGate.Check("先补测试，然后改接口。", "先补测试，然后改接口，并且全部重写一遍。");
        Assert.False(gate.Accepted);
    }

    [Fact]
    public void Accepts_a_homophone_fix()
    {
        var gate = FidelityGate.Check("这个接口不要加重视，除非返回五零三。", "这个接口不要加重试，除非返回五零三。");
        Assert.True(gate.Accepted, gate.Reason);
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
        Assert.True(gate.Accepted, gate.Reason);
    }

    [Fact]
    public void A_glossary_term_may_replace_a_run_of_misrecognized_english_words()
    {
        // Observed: "Max Oce" is one phonetic guess for macOS; counting it as two lost words rejected a correct repair.
        var gate = FidelityGate.Check(
            "整个流程要在 Windows 和 Max Oce 上使用同一套核心逻辑。",
            "整个流程要在 Windows 和 macOS 上使用同一套核心逻辑。", ["macOS"]);
        Assert.True(gate.Accepted, gate.Reason);
    }

    [Fact]
    public void Numbers_are_never_invented_from_garbled_numerals()
    {
        // Observed: "承包千零八十" (really 8080) was turned into 1080, a guess no check could otherwise see.
        var gate = FidelityGate.Check("端口在承包千零八十，内存四个 G。", "端口在 1080，内存四个 G。");
        Assert.False(gate.Accepted);
        Assert.Contains("invented", gate.Reason);
    }

    [Fact]
    public void An_english_term_that_is_neither_spoken_nor_in_the_glossary_is_rejected()
    {
        var gate = FidelityGate.Check("把接口改掉，然后补测试。", "把 Kubernetes 接口改掉，然后补测试。", ["Avalonia"]);
        Assert.False(gate.Accepted);
        Assert.Contains("glossary", gate.Reason);
    }
}

public class TextTidyTests
{
    [Theory]
    [InlineData("设成 三十 秒， 不对", "设成三十秒，不对")]
    [InlineData("昨天是 Monday， today is 礼拜二", "昨天是 Monday，today is 礼拜二")]
    [InlineData("  用 Redis 缓存  ", "用 Redis 缓存")]
    public void Removes_recognizer_spacing_but_keeps_spaces_around_english_words(string input, string expected) =>
        Assert.Equal(expected, TextTidy.Apply(input));

    [Theory]
    [InlineData("还是在指导工作方面，都可。<|endoftext|>Human Rights Watch 是美国的组织。有相应的减轻负担。", "还是在指导工作方面，都可。")]
    [InlineData("<|zh|>你好<|en|>", "你好")]
    public void Drops_special_tokens_and_everything_a_model_invents_after_its_end_of_text(string input, string expected) =>
        Assert.Equal(expected, TextTidy.Apply(input));
}

public class AudioBufferTests
{
    [Fact]
    public void Slice_spans_several_chunks()
    {
        var buffer = new AudioBuffer();
        buffer.Append([1f, 2f, 3f]);
        buffer.Append([4f, 5f]);
        buffer.Append([6f, 7f, 8f]);
        Assert.Equal([3f, 4f, 5f, 6f], buffer.Slice(2, 6));
    }

    [Fact]
    public void Slice_is_clamped_to_available_audio()
    {
        var buffer = new AudioBuffer();
        buffer.Append([1f, 2f, 3f]);
        Assert.Equal([1f, 2f, 3f], buffer.Slice(-5, 99));
        Assert.Empty(buffer.Slice(5, 9));
    }
}
