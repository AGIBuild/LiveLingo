using System.Diagnostics;
using System.Text.Json;
using Dictation.Audio;
using Dictation.Core;
using Dictation.Models;
using Dictation.Harness;

// Usage:
//   smoke  --wav <file> [--live xasr-480] [--second xasr,qwen3,funasr,sensevoice]
//   run    [--scenarios a,b|all] [--live xasr-480] [--second none|xasr|qwen3|funasr|sensevoice] [--cleaner none|qwen35-4b|qwen3-4b-2507]
//          [--pace 1] [--commit-after-ms 200] [--glossary on|off] [--vad-silence 0.3] [--holdback-ms 2000] [--out results/x.json]
//   replay [--takes id1,id2|all] [--recordings <dir>] [--live ...] [--second ...] [--cleaner ...]   re-runs takes saved by the test app, in real time
//   mic-check [--seconds 3]   records the default microphone and prints its level; use it to diagnose OS permission
//   serial [--scenarios ...] [--second qwen3] [--cleaner qwen35-4b] [--vad-silence 0.3] [--glossary on|off] [--out results/x.json]

var command = args.Length > 0 ? args[0] : "help";
var options = ParseOptions(args.Skip(1).ToArray());
string Opt(string key, string fallback) => options.TryGetValue(key, out var v) ? v : fallback;

var corpusDir = Path.GetFullPath(Opt("corpus", FindCorpusDir()));
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
};

switch (command)
{
    case "smoke": await SmokeAsync(); break;
    case "trace-live": TraceLive(); break;
    case "run": await RunAsync(); break;
    case "serial": await SerialAsync(); break;
    case "replay": await ReplayAsync(); break;
    case "mic-check": await MicCheckAsync(); break;
    default:
        Console.WriteLine("commands: smoke | trace-live | run | serial (see the header of Program.cs)");
        break;
}

// Prints how the live text grows; --cut-ms closes the segment at that audio time, like a VAD cut would.
void TraceLive()
{
    var wavPath = options.TryGetValue("wav", out var w) ? w : throw new ArgumentException("--wav is required");
    var wav = WavFile.ReadMono16k(wavPath);
    var cuts = Opt("cut-ms", "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
    using var live = ModelCatalog.Live(Opt("live", "xasr-480"));
    var lastPrinted = "";
    for (var i = 0; i < wav.Length; i += 640)
    {
        live.Accept(wav.AsSpan(i, Math.Min(640, wav.Length - i)));
        var ms = (i + 640) / 16;
        if (live.CurrentText != lastPrinted && ms % 80 == 0)
        {
            Console.WriteLine($"{ms,6}ms  {live.CurrentText}");
            lastPrinted = live.CurrentText;
        }
        if (cuts.Count > 0 && ms >= cuts[0])
        {
            cuts.RemoveAt(0);
            Console.WriteLine($"{ms,6}ms  --- CUT -> \"{live.CloseSegment()}\"");
            lastPrinted = "";
        }
    }
    Console.WriteLine($"  end   --- FINAL \"{live.CloseSegment()}\"");
}

async Task SmokeAsync()
{
    var wavPath = options.TryGetValue("wav", out var w) ? w : throw new ArgumentException("--wav is required");
    var wav = WavFile.ReadMono16k(wavPath);
    var seconds = wav.Length / 16000.0;
    Console.WriteLine($"audio {seconds:F1}s");

    using (var live = ModelCatalog.Live(Opt("live", "xasr-480")))
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < wav.Length; i += 640) live.Accept(wav.AsSpan(i, Math.Min(640, wav.Length - i)));
        var text = live.CloseSegment();
        Console.WriteLine($"[live {Opt("live", "xasr-480")}] {sw.Elapsed.TotalSeconds:F2}s (RTF {sw.Elapsed.TotalSeconds / seconds:F2}) {text}");
    }

    foreach (var key in Opt("second", "xasr").Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var load = Stopwatch.StartNew();
        using var recognizer = ModelCatalog.SecondPass(key)!;
        var loadMs = load.Elapsed.TotalMilliseconds;
        var sw = Stopwatch.StartNew();
        var text = recognizer.Recognize(wav);
        Console.WriteLine($"[second {key}] load {loadMs:F0}ms, decode {sw.Elapsed.TotalSeconds:F2}s (RTF {sw.Elapsed.TotalSeconds / seconds:F2}) {text}");
    }
    await Task.CompletedTask;
}

async Task RunAsync()
{
    var cfg = new RunConfig(
        Opt("live", "xasr-480"), Opt("second", "none"), Opt("cleaner", "none"),
        double.Parse(Opt("pace", "1")), int.Parse(Opt("commit-after-ms", "200")),
        Opt("glossary", "off") == "on", float.Parse(Opt("vad-silence", "0.25")), float.Parse(Opt("vad-threshold", ModelCatalog.DefaultVadThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture))), int.Parse(Opt("holdback-ms", "2000")),
        int.Parse(Opt("hard-pause-ms", "800")), int.Parse(Opt("soft-max-ms", "12000")), int.Parse(Opt("context-sentences", "2")),
        Opt("glossary-extra", "").Split(',', StringSplitOptions.RemoveEmptyEntries));
    var scenarios = LoadScenarios();

    using var secondPass = ModelCatalog.SecondPass(cfg.SecondPass);
    using var cleaner = ModelCatalog.Cleaner(cfg.Cleaner) as IDisposable;
    var results = new List<ScenarioResult>();

    Console.WriteLine($"live={cfg.Live} second={cfg.SecondPass} cleaner={cfg.Cleaner} pace={cfg.Pace} glossary={cfg.UseGlossary}");
    foreach (var (timeline, wavPath) in scenarios)
    {
        var result = await Runner.RunAsync(timeline, wavPath, cfg, secondPass, cleaner as ITextCleaner);
        results.Add(result);
        PrintScenario(result);
    }
    PrintSummary(results);
    PrintByVoice(results);
    Save(results, Opt("out", $"results/run-{cfg.Live}-{cfg.SecondPass}-{cfg.Cleaner}.json"));
}

async Task SerialAsync()
{
    var scenarios = LoadScenarios();
    var vadSilence = float.Parse(Opt("vad-silence", "0.25"));
    var cleanerKey = Opt("cleaner", "none");
    using var recognizer = ModelCatalog.SecondPass(Opt("second", "qwen3"))!;
    using var cleaner = ModelCatalog.Cleaner(cleanerKey, maxTokensCap: 3000, contextSize: 8192) as IDisposable;
    var results = new List<SerialResult>();

    foreach (var (timeline, wavPath) in scenarios)
    {
        var r = await Runner.RunSerialAsync(timeline, wavPath, recognizer, cleaner as ITextCleaner, vadSilence, Opt("glossary", "off") == "on");
        results.Add(r);
        Console.WriteLine($"{r.Id,-20} audio {r.AudioMs / 1000.0,6:F1}s | wait after speech {r.WaitMs / 1000.0,6:F2}s (recognize {r.RecognizeMs / 1000.0:F2}s + clean {r.CleanMs / 1000.0:F2}s) | rawCER {r.RawCer:P1} | keep {r.MustKeepRaw}->{r.MustKeepFinal}/{r.MustKeepTotal} forbid {r.ForbidFinal}");
    }
    Save(results, Opt("out", $"results/serial-{Opt("second", "qwen3")}-{cleanerKey}.json"));
}

async Task ReplayAsync()
{
    var cfg = new RunConfig(
        Opt("live", "xasr-480"), Opt("second", ModelCatalog.DefaultSecondPass), Opt("cleaner", "qwen35-4b"),
        double.Parse(Opt("pace", "1")), int.Parse(Opt("commit-after-ms", "200")),
        true, float.Parse(Opt("vad-silence", "0.25")), float.Parse(Opt("vad-threshold", ModelCatalog.DefaultVadThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture))), int.Parse(Opt("holdback-ms", "2000")),
        int.Parse(Opt("hard-pause-ms", "800")), int.Parse(Opt("soft-max-ms", "12000")), int.Parse(Opt("context-sentences", "2")),
        []);
    var store = new RecordingStore(Opt("recordings", RecordingStore.DefaultDirectory));
    var wanted = Opt("takes", "all");
    var ids = wanted == "all" ? store.Ids() : wanted.Split(',');

    using var secondPass = ModelCatalog.SecondPass(cfg.SecondPass);
    using var cleaner = ModelCatalog.Cleaner(cfg.Cleaner) as IDisposable;
    var results = new List<ScenarioResult>();
    foreach (var id in ids)
    {
        var take = store.Load(id);
        var timeline = new Timeline(id, "user", "human", take.DurationMs,
            [new TimelinePart(take.Reference, 0, take.DurationMs)], [.. take.Glossary], [], []);
        var result = await Runner.RunAsync(timeline, store.WavPath(id), cfg, secondPass, cleaner as ITextCleaner);
        results.Add(result);

        Console.WriteLine($"=== take {id}: {take.DurationMs / 1000.0:F1}s, glossary [{string.Join(", ", take.Glossary)}]");
        Console.WriteLine(take.Reference.Length > 0
            ? $"reference: {take.Reference}\nCER live {result.LiveCer:P1} raw {result.RawCer:P1} final {result.FinalCer:P1}"
            : "reference: (none typed, CER not meaningful)");
        Console.WriteLine($"live : {result.Live}\nraw  : {result.Raw}\nfinal: {result.Final}");
        Console.WriteLine($"commit wait {result.CommitWaitMs:F0}ms, sentences {result.Cleaned} cleaned / {result.Rejected} rejected / {result.RawOnly} raw");
    }
    Save(results, Opt("out", "results/replay.json"));
}

async Task MicCheckAsync()
{
    var seconds = int.Parse(Opt("seconds", "3"));
    var store = new RecordingStore(Opt("recordings", RecordingStore.DefaultDirectory));
    var (id, writer) = store.Begin(DateTime.Now);
    using var mic = new MicrophoneRecorder();
    var peak = 0f;
    var frames = 0;
    var name = mic.Start(frame =>
    {
        writer.Append(frame);
        Interlocked.Increment(ref frames);
        foreach (var x in frame) peak = Math.Max(peak, Math.Abs(x));
    });
    Console.WriteLine($"recording {seconds}s from \"{name}\"; speak now");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    mic.Stop();
    writer.Dispose();
    Console.WriteLine($"frames {frames} (expected about {seconds * 25}), peak {peak:F4}{(peak == 0 ? "  -> SILENCE: the OS is not giving this process microphone data" : "")}");
    Console.WriteLine($"saved {store.WavPath(id)}");
}

List<(Timeline, string)> LoadScenarios()
{
    var wanted = Opt("scenarios", "all");
    var files = Directory.GetFiles(Path.Combine(corpusDir, "audio"), "*.timeline.json").OrderBy(f => f).ToList();
    var all = files.Select(f => (JsonSerializer.Deserialize<Timeline>(File.ReadAllText(f), jsonOptions)!, f.Replace(".timeline.json", ".wav"))).ToList();
    if (wanted == "all") return all.Where(s => s.Item1.Category != "soak").ToList();
    var ids = wanted.Split(',');
    return all.Where(s => ids.Contains(s.Item1.Id)).ToList();
}

void Save<T>(T value, string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, JsonSerializer.Serialize(value, jsonOptions));
    Console.WriteLine($"saved {path}");
}

static void PrintScenario(ScenarioResult r)
{
    Console.WriteLine(
        $"{r.Id,-20} {r.AudioMs / 1000.0,5:F1}s seg={r.Segments,2} | CER live {r.LiveCer:P1} raw {r.RawCer:P1} | firstText p50/p95 {r.FirstTextP50:F0}/{r.FirstTextP95:F0}ms | closeLag p50 {r.CloseLagP50:F0}ms" +
        $" | cleanLag p50/p95/max {r.CleanLagP50:F0}/{r.CleanLagP95:F0}/{r.CleanLagMax:F0}ms | commitWait {r.CommitWaitMs:F0}ms{(r.CommitTimedOut ? " TIMEOUT" : "")}" +
        $" | sent {r.Cleaned}c/{r.Rejected}r/{r.RawOnly}w | keep {r.MustKeepRaw}->{r.MustKeepFinal}/{r.MustKeepTotal} forbid {r.ForbidRaw}->{r.ForbidFinal} | terms {r.TermsLive}/{r.TermsRaw}/{r.TermsFinal} of {r.TermsTotal} falseIns {r.FalseGlossaryInsertions}" +
        $" | retract {r.LiveRevisions}/{r.LiveRetractedChars} backlog {r.MaxBacklogMs:F0}ms");
}

static void PrintSummary(IReadOnlyList<ScenarioResult> results)
{
    Console.WriteLine();
    Console.WriteLine($"mean live CER {results.Average(r => r.LiveCer):P2}, mean raw CER {results.Average(r => r.RawCer):P2}; " +
                      $"worst commit wait {results.Max(r => r.CommitWaitMs):F0}ms; worst clean lag {results.Max(r => r.CleanLagMax):F0}ms; max backlog {results.Max(r => r.MaxBacklogMs):F0}ms");
}

// One row per voice (accent variant) and a scenario x voice matrix of settled raw CER. CER is the mean over scenarios, not pooled by characters.
static void PrintByVoice(IReadOnlyList<ScenarioResult> results)
{
    var voices = results.Select(r => r.Voice).Distinct().ToList();
    if (voices.Count < 2) return;

    Console.WriteLine();
    Console.WriteLine($"{"voice",-12} {"n",2} | {"live CER",8} {"raw CER",8} {"final CER",9} | terms raw/final | mustKeep raw/final | rejected | commit wait");
    foreach (var voice in voices)
    {
        var g = results.Where(r => r.Voice == voice).ToList();
        Console.WriteLine(
            $"{voice,-12} {g.Count,2} | {g.Average(r => r.LiveCer),8:P1} {g.Average(r => r.RawCer),8:P1} {g.Average(r => r.FinalCer),9:P1} |" +
            $" {g.Sum(r => r.TermsRaw),5}/{g.Sum(r => r.TermsFinal),2} of {g.Sum(r => r.TermsTotal),2} | {g.Sum(r => r.MustKeepRaw),7}/{g.Sum(r => r.MustKeepFinal),2} of {g.Sum(r => r.MustKeepTotal),2} |" +
            $" {g.Sum(r => r.Rejected),8} | {g.Average(r => r.CommitWaitMs),6:F0} ms");
    }

    Console.WriteLine();
    Console.WriteLine("settled raw CER by scenario (rows) and voice (columns)");
    Console.WriteLine($"{"",-20}" + string.Concat(voices.Select(v => $"{v,12}")));
    foreach (var scenario in results.Select(r => r.Id.Split('@')[0]).Distinct())
        Console.WriteLine($"{scenario,-20}" + string.Concat(voices.Select(v =>
            results.FirstOrDefault(r => r.Voice == v && r.Id.Split('@')[0] == scenario) is { } r ? $"{r.RawCer,12:P1}" : $"{"-",12}")));
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var map = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            map[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
    return map;
}

static string FindCorpusDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "corpus")))
        dir = dir.Parent;
    return dir is null ? throw new DirectoryNotFoundException("corpus directory not found") : Path.Combine(dir.FullName, "corpus");
}
