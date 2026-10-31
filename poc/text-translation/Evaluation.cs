using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranslationPoc;

public sealed record Case(string Id, string Category, string TargetLanguage, string Source, string Reference,
    IReadOnlyList<string> Keep, IReadOnlyList<string> Forbid, IReadOnlyList<Term> Terms);
public sealed record Sample(string Id, int Repeat, string Source, string Reference, string TargetLanguage,
    string Category, Translation? Translation, IReadOnlyList<string> Violations, string? Error);
public sealed record CallRecord(string Source, string Context, double StartedMs, double EndedMs, Translation Result);
public sealed record ReplayRecord(string Model, string Strategy, string Source, string Final, double InputEndedMs,
    double FirstConfirmedMs, double WaitAfterInputMs, int TranslatedSourceChars, IReadOnlyList<CallRecord> Calls);

public static class Evaluation
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task BenchmarkAsync(ITranslator translator, string corpus, string output, int repeats, CancellationToken ct)
    {
        var cases = JsonSerializer.Deserialize<List<Case>>(File.ReadAllText(corpus), Json)
            ?? throw new InvalidDataException("Empty corpus.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var writer = new StreamWriter(output, append: false);
        var compact = new JsonSerializerOptions(Json) { WriteIndented = false };
        for (var repeat = 0; repeat < repeats; repeat++)
        {
            foreach (var item in cases)
            {
                Translation? translation = null;
                string? error = null;
                var violations = new List<string>();
                try
                {
                    translation = await translator.TranslateAsync(new Request(item.Source, item.TargetLanguage, item.Terms), ct);
                    foreach (var keep in item.Keep)
                        if (!translation.Text.Contains(keep, StringComparison.OrdinalIgnoreCase)) violations.Add("missing: " + keep);
                    foreach (var forbid in item.Forbid)
                        if (translation.Text.Contains(forbid, StringComparison.OrdinalIgnoreCase)) violations.Add("forbidden: " + forbid);
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException or JsonException)
                {
                    if (ct.IsCancellationRequested) throw;
                    error = ex.ToString();
                }
                var sample = new Sample(item.Id, repeat, item.Source, item.Reference, item.TargetLanguage, item.Category,
                    translation, violations, error);
                await writer.WriteLineAsync(JsonSerializer.Serialize(sample, compact));
                await writer.FlushAsync(ct);
                Console.WriteLine($"{item.Id,-23} run={repeat} first={translation?.FirstVisibleMs:F0}ms total={translation?.TotalMs:F0}ms violations={violations.Count} {error}");
            }
        }
    }

    public static async Task<ReplayRecord> ReplayAsync(ITranslator translator, string model, bool incremental, CancellationToken ct,
        int count = 6, int intervalMs = 3000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(intervalMs);
        string[] sentences =
        [
            "这个 PR 先不要合并，等 CI 跑完。",
            "只修改下载接口，其他请求不要动。",
            "如果服务器返回 503，再重试两次。",
            "原文和译文都要保留，方便之后核对。",
            "用户没有授权时，不要调用云端服务。",
            "取消之后的旧结果不能覆盖最新输入。"
        ];
        var clock = Stopwatch.StartNew();
        var recorder = new RecordingTranslator(translator, clock);
        await using var session = new TranslationSession(recorder, "en", []);
        var source = "";
        for (var i = 0; i < count; i++)
        {
            var due = (long)i * intervalMs;
            var remaining = due - clock.Elapsed.TotalMilliseconds;
            if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), ct);
            source += sentences[i % sentences.Length];
            if (incremental) session.Update(source);
        }
        var ended = clock.Elapsed.TotalMilliseconds;
        string final;
        if (incremental)
        {
            await session.DrainAsync();
            var units = session.Snapshot();
            if (units.Any(u => u.Error is not null || u.Target is null))
                throw new InvalidDataException("Incremental replay has incomplete units.");
            final = Units.Join(units.Select(u => (u.Break, u.Target!, u.Trail)), "en");
        }
        else
            final = (await recorder.TranslateAsync(new Request(source, "en", []), ct)).Text;
        var calls = recorder.Calls.ToArray();
        return new ReplayRecord(model, incremental ? "semantic-units" : "after-input", source, final, ended,
            calls[0].EndedMs, clock.Elapsed.TotalMilliseconds - ended, calls.Sum(c => c.Source.Length), calls);
    }

    internal sealed class RecordingTranslator(ITranslator translator, Stopwatch clock) : ITranslator
    {
        public List<CallRecord> Calls { get; } = [];
        public async Task<Translation> TranslateAsync(Request request, CancellationToken ct)
        {
            var started = clock.Elapsed.TotalMilliseconds;
            var result = await translator.TranslateAsync(request, ct);
            Calls.Add(new CallRecord(request.Source, request.Context, started, clock.Elapsed.TotalMilliseconds, result));
            return result;
        }
    }
}
