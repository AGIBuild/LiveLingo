using System.Text.Json;

namespace TranslationPoc;

public sealed record UnitView(
    int Id,
    string Source,
    string? Target,
    bool Complete,
    string? Error,
    bool InFlight = false,
    double FirstVisibleMs = 0,
    double TotalMs = 0,
    int ProtectedLiterals = 0,
    string Break = "",
    string Trail = "");

public sealed class TranslationSession(ITranslator translator, string targetLanguage, IReadOnlyList<Term> terms, int contextUnits = 2) : IAsyncDisposable
{
    private sealed class Entry(int id, string source, bool complete, string dependency, string lineBreak, string trail)
    {
        public int Id { get; } = id;
        public string Source { get; } = source;
        public bool Complete { get; } = complete;
        public string Dependency { get; } = dependency;
        public string Break { get; set; } = lineBreak;
        public string Trail { get; set; } = trail;
        public int ProtectedLiterals { get; } = ProtectedText.Spans(source).Count;
        public bool InFlight { get; set; }
        public Translation? Result { get; set; }
        public string? Error { get; set; }
    }

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private string _targetLanguage = targetLanguage;
    private IReadOnlyList<Term> _terms = terms;
    private string _source = "";
    private List<Entry> _units = [];
    private bool _composing;
    private bool _flushTail;
    private bool _running;
    private bool _disposed;
    private bool _faulted;
    private Task _worker = Task.CompletedTask;
    private int _discarded;
    private int _totalCalls;

    public event Action<IReadOnlyList<UnitView>>? Changed;

    public int DiscardedResults { get { lock (_gate) return _discarded; } }
    public int TotalCalls { get { lock (_gate) return _totalCalls; } }

    public void ResetCounters()
    {
        lock (_gate)
        {
            _totalCalls = 0;
            _discarded = 0;
        }
        Notify();
    }

    public void Configure(string targetLanguage, IReadOnlyList<Term> terms)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted)
                throw new InvalidOperationException("Translation session faulted; create a new session.", _worker.Exception);
            _targetLanguage = targetLanguage;
            _terms = terms;
            RebuildUnits();
            StartWorker();
        }
        Notify();
    }

    public void Update(string source, bool composing = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted)
                throw new InvalidOperationException("Translation session faulted; create a new session.", _worker.Exception);
            _source = source;
            _composing = composing;
            _flushTail = false;
            RebuildUnits();
            StartWorker();
        }
        Notify();
    }

    private void RebuildUnits()
    {
        var next = new List<Entry>();
        var dependency = _targetLanguage;
        foreach (var unit in Units.Split(_source))
        {
            var matched = string.Join(";", _terms
                .Where(t => unit.Text.Contains(t.Source, StringComparison.OrdinalIgnoreCase))
                .Select(t => $"{t.Source}={t.Target}"));
            var unitDependency = $"{dependency}|{matched}";
            var index = next.Count;
            var old = index < _units.Count ? _units[index] : null;
            if (old is not null && old.Source == unit.Text && old.Dependency == unitDependency && old.Complete == unit.Complete)
            {
                old.Break = unit.Break;
                old.Trail = unit.Trail;
                next.Add(old);
            }
            else next.Add(new Entry(index, unit.Text, unit.Complete, unitDependency, unit.Break, unit.Trail));
            dependency = $"{unitDependency}|{unit.Text}";
        }
        _units = next;
    }

    public IReadOnlyList<UnitView> Snapshot()
    {
        lock (_gate)
            return BuildSnapshot();
    }

    private IReadOnlyList<UnitView> BuildSnapshot() =>
        _units.Select(u => new UnitView(
            u.Id,
            u.Source,
            u.Result?.Text,
            u.Complete,
            u.Error,
            u.InFlight,
            u.Result?.FirstVisibleMs ?? 0,
            u.Result?.TotalMs ?? 0,
            u.ProtectedLiterals,
            u.Break,
            u.Trail)).ToArray();

    private void Notify()
    {
        if (Changed is { } handler)
            handler(Snapshot());
    }

    public async Task DrainAsync()
    {
        lock (_gate)
        {
            _flushTail = true;
            StartWorker();
        }
        while (true)
        {
            Task work;
            lock (_gate) work = _worker;
            await work;
            lock (_gate) if (!_running) return;
        }
    }

    private void StartWorker()
    {
        if (_running || _composing || _disposed || _faulted) return;
        _running = true;
        _worker = Task.Run(ProcessAsync);
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (true)
            {
                Entry? target;
                string context;
                string lang;
                IReadOnlyList<Term> currentTerms;
                lock (_gate)
                {
                    if (_composing || _disposed) { _running = false; return; }
                    target = _units.FirstOrDefault(u => u.Result is null && u.Error is null && (u.Complete || _flushTail));
                    if (target is null) { _running = false; return; }
                    target.InFlight = true;
                    lang = _targetLanguage;
                    currentTerms = _terms;
                    context = string.Join("\n", _units.Take(target.Id).TakeLast(contextUnits)
                        .Select(u => $"Source: {u.Source}\nTranslation: {u.Result?.Text}"));
                }
                Notify();
                Translation? result = null;
                string? error = null;
                try
                {
                    Interlocked.Increment(ref _totalCalls);
                    result = await translator.TranslateAsync(new Request(target.Source, lang, currentTerms, context), _stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    lock (_gate) { target.InFlight = false; _running = false; }
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException or JsonException)
                {
                    error = $"{ex.GetType().Name}: {ex.Message}";
                }
                lock (_gate)
                {
                    target.InFlight = false;
                    if (_units.Contains(target) && !_disposed)
                    {
                        target.Result = result;
                        target.Error = error;
                    }
                    else _discarded++;
                }
                Notify();
            }
        }
        catch
        {
            lock (_gate) { _running = false; _faulted = true; }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task work;
        lock (_gate) { _disposed = true; work = _worker; }
        _stop.Cancel();
        try { await work; }
        finally { _stop.Dispose(); }
    }
}
