using System.Text.Json;
using TranslationPoc;

if (args.Length == 0)
{
    Console.Error.WriteLine("Commands: bench | replay | translate | corpus; options: --endpoint http://127.0.0.1:8097/ --corpus cases.json --out result.jsonl --repeat 3 --temperature 0 --model hy-mt2-1.8b");
    return 2;
}
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
        throw new ArgumentException("Options require --key value pairs.");
    options.Add(args[i][2..], args[i + 1]);
}
string Opt(string name, string fallback) => options.GetValueOrDefault(name, fallback);
using var client = new HttpClient
{
    BaseAddress = new Uri(Opt("endpoint", "http://127.0.0.1:8097/")),
    Timeout = TimeSpan.FromSeconds(120)
};
if (!client.BaseAddress.IsLoopback)
    throw new ArgumentException("This local-only POC requires a loopback endpoint.");
var outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VoicePoc", "translation-eval");
var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
Directory.CreateDirectory(outputDirectory);
var translator = new HttpTranslator(client,
    double.Parse(Opt("temperature", "0"), System.Globalization.CultureInfo.InvariantCulture),
    protectLiterals: Opt("protect", "on") == "on");
switch (args[0])
{
    case "translate":
        var result = await translator.TranslateAsync(
            new Request(Opt("text", "不要修改其他接口。"), Opt("target", "en"), []), default);
        Console.WriteLine(JsonSerializer.Serialize(result, Evaluation.Json));
        break;
    case "bench":
        await Evaluation.BenchmarkAsync(translator, Opt("corpus", "cases.json"),
            Opt("out", Path.Combine(outputDirectory, $"bench-{runId}.jsonl")),
            int.Parse(Opt("repeat", "3")), default);
        break;
    case "replay":
        var strategy = Opt("strategy", "units");
        if (strategy is not ("units" or "batch"))
            throw new ArgumentException("--strategy must be units or batch.");
        var replay = await Evaluation.ReplayAsync(translator, Opt("model", "hy-mt2-1.8b"),
            strategy == "units", default, int.Parse(Opt("count", "6")), int.Parse(Opt("interval-ms", "3000")));
        await File.WriteAllTextAsync(Opt("out", Path.Combine(outputDirectory, $"replay-{runId}.json")), JsonSerializer.Serialize(replay, Evaluation.Json));
        Console.WriteLine($"first confirmed {replay.FirstConfirmedMs:F0}ms, wait after input {replay.WaitAfterInputMs:F0}ms, source chars translated {replay.TranslatedSourceChars}");
        break;
    case "corpus":
        var corpusStrategy = Opt("strategy", "units");
        if (corpusStrategy is not ("units" or "fulltext" or "stream"))
            throw new ArgumentException("--strategy must be units, fulltext or stream.");
        await CorpusRunner.RunAsync(translator, Opt("corpus", ""), Opt("out", ""), corpusStrategy,
            int.Parse(Opt("context", "2")), double.Parse(Opt("rate", "5"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Opt("limit", "0")), default);
        break;
    default: throw new ArgumentException("Unknown command: " + args[0]);
}
return 0;
