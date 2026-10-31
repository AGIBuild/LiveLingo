using System.Collections.Concurrent;

namespace TranslationPoc.Tests;

public class SessionTests
{
    [Fact]
    public async Task Adding_a_sentence_does_not_retranslate_completed_prefix()
    {
        var translator = new Recorder();
        await using var session = new TranslationSession(translator, "en", []);
        session.Update("第一句。");
        await session.DrainAsync();
        session.Update("第一句。第二句。");
        await session.DrainAsync();
        Assert.Equal(new[] { "第一句。", "第二句。" }, translator.Sources.ToArray());
        Assert.All(session.Snapshot(), u => Assert.NotNull(u.Target));
    }

    [Fact]
    public async Task Editing_context_invalidates_the_dependent_suffix()
    {
        var translator = new Recorder();
        await using var session = new TranslationSession(translator, "en", []);
        session.Update("第一句。第二句。");
        await session.DrainAsync();
        session.Update("换一句。第二句。");
        await session.DrainAsync();
        Assert.Equal(4, translator.Sources.Count);
        Assert.Contains("换一句。", translator.Requests.Last().Context);
    }

    [Fact]
    public async Task Ime_composition_does_not_invoke_the_model()
    {
        var translator = new Recorder();
        await using var session = new TranslationSession(translator, "en", []);
        session.Update("正在输入。", composing: true);
        await session.DrainAsync();
        Assert.Empty(translator.Sources);
        session.Update("正在输入。");
        await session.DrainAsync();
        Assert.Single(translator.Sources);
    }

    [Fact]
    public async Task Superseded_inflight_result_is_discarded()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new Recorder(async r =>
        {
            if (r.Source == "旧句。") { started.TrySetResult(); await release.Task; }
            return new Translation(r.Source + "-translated", 0, 1);
        });
        await using var session = new TranslationSession(translator, "en", []);
        session.Update("旧句。");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Update("新句。");
        release.SetResult();
        await session.DrainAsync();
        Assert.Single(session.Snapshot());
        Assert.Equal("新句。-translated", session.Snapshot()[0].Target);
        Assert.Equal(1, session.DiscardedResults);
    }

    [Fact]
    public async Task Backend_error_is_visible_and_not_replaced_with_source()
    {
        var translator = new Recorder(_ => throw new InvalidDataException("truncated output"));
        await using var session = new TranslationSession(translator, "en", []);
        session.Update("第一句。");
        await session.DrainAsync();
        Assert.Null(session.Snapshot()[0].Target);
        Assert.Contains("truncated", session.Snapshot()[0].Error);
    }

    [Fact]
    public async Task Active_tail_is_translated_on_drain_not_on_each_keystroke()
    {
        var translator = new Recorder();
        await using var session = new TranslationSession(translator, "en", []);
        session.Update("输入");
        session.Update("输入更多");
        await Task.Delay(20);
        Assert.Empty(translator.Sources);
        await session.DrainAsync();
        Assert.Equal(new[] { "输入更多" }, translator.Sources.ToArray());
    }

    [Fact]
    public async Task Unexpected_backend_fault_is_surfaced_without_an_automatic_retry_loop()
    {
        var calls = 0;
        var translator = new Recorder(_ =>
        {
            Interlocked.Increment(ref calls);
            throw new NotSupportedException("unsupported backend");
        });
        var session = new TranslationSession(translator, "en", []);
        try
        {
            session.Update("第一句。");
            await Assert.ThrowsAsync<NotSupportedException>(() => session.DrainAsync());
            await Task.Delay(20);
            Assert.Equal(1, calls);
        }
        finally
        {
            try { await session.DisposeAsync(); }
            catch (NotSupportedException) { }
        }
    }

    [Fact]
    public async Task Changed_event_reports_inflight_and_completed_unit_metrics()
    {
        var snapshots = new ConcurrentQueue<IReadOnlyList<UnitView>>();
        var translator = new Recorder(async r =>
        {
            await Task.Delay(20);
            return new Translation(r.Source + "-en", 12, 34, 5);
        });
        await using var session = new TranslationSession(translator, "en", []);
        session.Changed += s => snapshots.Enqueue(s);

        session.Update("保留 `CancellationToken`。");
        await session.DrainAsync();

        Assert.Contains(snapshots, s => s.Count == 1 && s[0].InFlight);
        var final = Assert.Single(session.Snapshot());
        Assert.False(final.InFlight);
        Assert.Equal("保留 `CancellationToken`。-en", final.Target);
        Assert.Equal(1, final.ProtectedLiterals);
        Assert.Equal(12, final.FirstVisibleMs);
        Assert.Equal(34, final.TotalMs);
        Assert.Equal(1, session.TotalCalls);
    }

    [Fact]
    public async Task Changing_glossary_only_retranslates_units_where_matched_terms_or_context_changed()
    {
        var translator = new Recorder();
        await using var session = new TranslationSession(translator, "en", [new("缓存穿透", "cache penetration")]);
        session.Update("第一句没有术语。第二句有缓存穿透。");
        await session.DrainAsync();
        Assert.Equal(2, session.TotalCalls);

        // Adding an unmatched term should not retranslate anything.
        session.Configure("en", [new("缓存穿透", "cache penetration"), new("幂等键", "idempotency key")]);
        await session.DrainAsync();
        Assert.Equal(2, session.TotalCalls);

        // Changing the translation of a term in sentence 2 retranslates sentence 2, but preserves sentence 1.
        session.Configure("en", [new("缓存穿透", "cache miss storm")]);
        await session.DrainAsync();
        Assert.Equal(3, session.TotalCalls);
        Assert.Equal("第二句有缓存穿透。", translator.Sources.Last());
    }

    private sealed class Recorder(Func<Request, Task<Translation>>? translate = null) : ITranslator
    {
        public ConcurrentQueue<string> Sources { get; } = new();
        public ConcurrentQueue<Request> Requests { get; } = new();
        public Task<Translation> TranslateAsync(Request request, CancellationToken ct)
        {
            Sources.Enqueue(request.Source);
            Requests.Enqueue(request);
            return translate is null ? Task.FromResult(new Translation(request.Source + "-translated", 0, 1)) : translate(request);
        }
    }
}
