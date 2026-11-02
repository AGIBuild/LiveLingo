using TranslationPoc;

namespace Dictation.TestApp;

public sealed record TranslationBenchView(
    string TranslatedText,
    string MetricsText,
    string UnitLogText,
    IReadOnlyList<UnitView> Units,
    int TotalCalls,
    int DiscardedResults);

/// <summary>
/// Owns the local llama-server translation runtime, the active <see cref="TranslationSession"/>,
/// and the saved translation evaluation cases for the UI workbench.
/// </summary>
public sealed class TranslationBench : IDisposable
{
    private readonly LlamaTranslationServer _server = new();
    private TranslationCaseStore _store;
    private TranslationSession? _session;
    private ITranslator? _translator;
    private string _targetLanguage = "en";
    private IReadOnlyList<Term> _terms = [];

    public TranslationBench(TranslationCaseStore store) => _store = store;

    public event Action<TranslationBenchView>? Changed;

    public TranslationCaseStore Store => _store;
    public string ActiveModelKey => _server.ActiveModelKey;
    public bool IsReady => _session is not null && !string.IsNullOrEmpty(_server.ActiveModelKey);

    public void UseCasesDirectory(string directory) => _store = new TranslationCaseStore(directory);

    public async Task StartOrSwitchModelAsync(
        string modelKey,
        string targetLanguage,
        IReadOnlyList<Term> terms,
        string currentSource = "")
    {
        _targetLanguage = targetLanguage;
        _terms = terms;

        var translator = await _server.StartOrSwitchAsync(modelKey);
        await ReplaceSessionAsync(translator, currentSource);
    }

    public void Configure(string targetLanguage, IReadOnlyList<Term> terms)
    {
        _targetLanguage = targetLanguage;
        _terms = terms;
        _session?.Configure(targetLanguage, terms);
    }

    public void UpdateSource(string source, bool composing = false) =>
        _session?.Update(source, composing);

    public void ResetSession()
    {
        _session?.Update("");
        _session?.ResetCounters();
    }

    public Task FlushTailAsync() =>
        _session is { } session ? session.DrainAsync() : Task.CompletedTask;

    // Finished text (pasted documents, structured data) is translated as one block: the evaluation showed
    // better quality and layout fidelity than sentence units, which exist only to keep live dictation responsive.
    public async Task TranslateWholeAsync(string source)
    {
        var result = await _translator!.TranslateAsync(new Request(source, _targetLanguage, _terms), default);
        var metrics = $"Whole text (1 call): first visible {result.FirstVisibleMs:F0} ms, total {result.TotalMs:F0} ms, {result.OutputTokens} tokens";
        Changed?.Invoke(new TranslationBenchView(result.Text, metrics, "", [], 1, 0));
    }

    public SavedTranslationCase SaveCase(string source, string reference)
    {
        var units = _session?.Snapshot() ?? [];
        var translated = JoinTranslated(units, _targetLanguage, includePlaceholders: false);
        return _store.Save(new SavedTranslationCase(
            Id: "",
            SavedAt: DateTime.Now,
            Model: ActiveModelKey,
            TargetLanguage: _targetLanguage,
            Glossary: _terms,
            Source: source,
            Translated: translated,
            Reference: reference,
            TotalCalls: _session?.TotalCalls ?? 0,
            DiscardedResults: _session?.DiscardedResults ?? 0,
            Units: units));
    }

    private async Task ReplaceSessionAsync(ITranslator translator, string currentSource)
    {
        _translator = translator;
        if (_session is { } old)
        {
            old.Changed -= OnSessionChanged;
            await old.DisposeAsync();
        }
        var next = new TranslationSession(translator, _targetLanguage, _terms);
        next.Changed += OnSessionChanged;
        _session = next;
        if (!string.IsNullOrWhiteSpace(currentSource))
            next.Update(currentSource);
        else
            OnSessionChanged([]);
    }

    private void OnSessionChanged(IReadOnlyList<UnitView> units)
    {
        var session = _session;
        if (session is null) return;

        var completeCount = units.Count(u => u.Complete);
        var tailCount = units.Count - completeCount;
        var literals = units.Sum(u => u.ProtectedLiterals);
        var translated = JoinTranslated(units, _targetLanguage, includePlaceholders: true);

        var metrics =
            $"Units: {units.Count} ({completeCount} complete, {tailCount} tail) | " +
            $"Calls: {session.TotalCalls} | Discarded stale: {session.DiscardedResults} | " +
            $"Protected literals: {literals}";

        var log = string.Join("\n", units.Select(u =>
        {
            var status = u.Error is not null ? "Error"
                : u.InFlight ? "Translating"
                : u.Target is not null ? "Final"
                : "PendingTail";
            var detail = u.Error is not null ? $"error: {u.Error}"
                : u.Target is not null ? $"tgt  : {u.Target}"
                : "tgt  : (waiting for sentence end or flush)";
            return $"#{u.Id} [{(u.Complete ? "Complete" : "ActiveTail")} | {status} | first {u.FirstVisibleMs:F0}ms, total {u.TotalMs:F0}ms | literals: {u.ProtectedLiterals}]\n" +
                   $"    src  : {u.Source}\n" +
                   $"    {detail}";
        }));

        Changed?.Invoke(new TranslationBenchView(translated, metrics, log, units, session.TotalCalls, session.DiscardedResults));
    }

    private static string JoinTranslated(IReadOnlyList<UnitView> units, string targetLanguage, bool includePlaceholders)
    {
        var parts = new List<(string Break, string Target, string Trail)>(units.Count);
        foreach (var u in units)
        {
            if (u.Target is { Length: > 0 } target)
                parts.Add((u.Break, target, u.Trail));
            else if (includePlaceholders && u.Error is { Length: > 0 } err)
                parts.Add((u.Break, $"[Error: {err}]", u.Trail));
            else if (includePlaceholders && u.InFlight)
                parts.Add((u.Break, "[Translating...]", u.Trail));
        }
        return Units.Join(parts, targetLanguage);
    }

    public void Dispose()
    {
        if (_session is { } session)
        {
            session.Changed -= OnSessionChanged;
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _session = null;
        }
        _server.Dispose();
    }
}
