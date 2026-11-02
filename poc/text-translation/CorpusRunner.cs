using System.Diagnostics;
using System.Text.Json;

namespace TranslationPoc;

public sealed record CorpusItem(string Id, string Set, string Direction, string Domain, string Src, IReadOnlyList<Term>? Terms);

public sealed record UnitRecord(string Source, string? Target, double TotalMs, string? Error);

public sealed record CorpusResult(
    string Id, string Strategy, string Hyp, string? Error, int Calls, double CallMs, double FirstVisibleMs,
    double WallMs, int OutputTokens, int Discarded, double WaitAfterInputMs, double FirstConfirmedMs,
    IReadOnlyList<UnitRecord> Units);

public static class CorpusRunner
{
    private static readonly JsonSerializerOptions Compact = new(Evaluation.Json) { WriteIndented = false };

    public static async Task RunAsync(ITranslator translator, string corpusPath, string outputPath, string strategy,
        int contextUnits, double charsPerSecond, int limit, CancellationToken ct)
    {
        var items = File.ReadLines(corpusPath).Where(l => l.Length > 0)
            .Select(l => JsonSerializer.Deserialize<CorpusItem>(l, Evaluation.Json)!).ToList();
        if (limit > 0) items = items.Take(limit).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var done = File.Exists(outputPath)
            ? File.ReadLines(outputPath).Select(l => JsonSerializer.Deserialize<CorpusResult>(l, Evaluation.Json)!.Id).ToHashSet()
            : [];
        await using var writer = new StreamWriter(outputPath, append: true);
        var index = 0;
        foreach (var item in items)
        {
            index++;
            if (done.Contains(item.Id)) continue;
            var result = await RunItemAsync(translator, item, strategy, contextUnits, charsPerSecond, ct);
            await writer.WriteLineAsync(JsonSerializer.Serialize(result, Compact));
            await writer.FlushAsync(ct);
            Console.WriteLine($"[{index}/{items.Count}] {item.Id} calls={result.Calls} wall={result.WallMs:F0}ms wait={result.WaitAfterInputMs:F0}ms {result.Error}");
        }
    }

    private static async Task<CorpusResult> RunItemAsync(ITranslator translator, CorpusItem item, string strategy,
        int contextUnits, double charsPerSecond, CancellationToken ct)
    {
        var target = item.Direction.EndsWith("-zh", StringComparison.Ordinal) ? "zh" : "en";
        var terms = item.Terms ?? [];
        var clock = Stopwatch.StartNew();
        var recorder = new Evaluation.RecordingTranslator(translator, clock);
        string hyp = "";
        string? error = null;
        var discarded = 0;
        double inputEnded = 0;
        IReadOnlyList<UnitView> units = [];
        try
        {
            if (strategy == "fulltext")
                hyp = (await recorder.TranslateAsync(new Request(item.Src, target, terms), ct)).Text;
            else
            {
                await using var session = new TranslationSession(recorder, target, terms, contextUnits);
                if (strategy == "stream")
                    await FeedAsync(session, item.Src, charsPerSecond, clock, ct);
                else session.Update(item.Src);
                inputEnded = strategy == "stream" ? clock.Elapsed.TotalMilliseconds : 0;
                await session.DrainAsync();
                units = session.Snapshot();
                discarded = session.DiscardedResults;
                error = units.FirstOrDefault(u => u.Error is not null)?.Error;
                hyp = Units.Join(units.Where(u => u.Target is not null).Select(u => (u.Break, u.Target!, u.Trail)), target);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or InvalidOperationException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }
        var calls = recorder.Calls.ToArray();
        var wall = clock.Elapsed.TotalMilliseconds;
        return new CorpusResult(item.Id, strategy, hyp, error, calls.Length, calls.Sum(c => c.Result.TotalMs),
            calls.Length > 0 ? calls[0].Result.FirstVisibleMs : 0, wall, calls.Sum(c => c.Result.OutputTokens), discarded,
            strategy == "stream" ? wall - inputEnded : wall, calls.Length > 0 ? calls.Min(c => c.EndedMs) : 0,
            units.Select(u => new UnitRecord(u.Source, u.Target, u.TotalMs, u.Error)).ToArray());
    }

    // Reveals the source at speaking speed, one 400 ms step at a time, like a recognizer emitting a growing transcript.
    private static async Task FeedAsync(TranslationSession session, string source, double charsPerSecond, Stopwatch clock, CancellationToken ct)
    {
        const int stepMs = 400;
        var started = clock.Elapsed.TotalMilliseconds;
        var shown = 0;
        while (shown < source.Length)
        {
            var next = Math.Min(source.Length, (int)((clock.Elapsed.TotalMilliseconds - started) / 1000 * charsPerSecond) + 1);
            if (next > shown)
            {
                shown = next;
                session.Update(source[..shown], composing: false);
            }
            if (shown < source.Length) await Task.Delay(stepMs, ct);
        }
    }
}
