using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Web;

/// <summary>The page side of the bridge: a platform web view, or a fake in tests.</summary>
internal interface IWebHost
{
    /// <summary>Raised on the UI thread with the raw text of each message the page posted.</summary>
    event Action<string>? MessageReceived;

    /// <summary>Delivers one JSON message to the page.</summary>
    void Post(string json);
}

/// <summary>Applies an intent to the model behind a page.</summary>
internal interface IIntentHandler
{
    void Handle(WebIntent intent);
}

/// <summary>The labels that do not change with the state, sent on load and when the language changes.</summary>
internal sealed record StringsData(string Locale, IReadOnlyDictionary<string, string> Strings);

/// <summary>
/// One message envelope. C# to page: <c>strings</c>, the state type (<c>composer</c>/<c>settings</c>), <c>toast</c>,
/// <c>focus</c>, <c>openMenu</c>. Page to C#: always a <see cref="WebIntent"/>.
/// </summary>
internal sealed record WebMessage(int V, string Type, object? Data);

/// <summary>
/// Single-direction data flow between a model and a page: the model says "something changed" and the page receives
/// one whole snapshot, at most once per frame; the page answers only with intents. Nothing else crosses.
/// </summary>
internal sealed class WebBridge : IDisposable
{
    /// <summary>Changes inside one frame are sent as one snapshot.</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private readonly IWebHost _host;
    private readonly string _stateType;
    private readonly Func<int, object> _snapshot;
    private readonly Func<StringsData> _strings;
    private readonly IIntentHandler _handler;
    private readonly SynchronizationContext _ui;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private ITimer? _timer;
    private int _rev;
    private bool _ready;
    private bool _scheduled;

    /// <param name="snapshot">Builds the state for a revision; runs on the UI thread.</param>
    public WebBridge(
        IWebHost host,
        string stateType,
        Func<int, object> snapshot,
        Func<StringsData> strings,
        IIntentHandler handler,
        SynchronizationContext ui,
        TimeProvider time,
        ILogger logger)
    {
        _host = host;
        _stateType = stateType;
        _snapshot = snapshot;
        _strings = strings;
        _handler = handler;
        _ui = ui;
        _time = time;
        _logger = logger;
        host.MessageReceived += OnMessage;
    }

    /// <summary>The model changed; the page gets a new snapshot within a frame. Ignored until the page announced itself.</summary>
    public void Invalidate()
    {
        if (!_ready || _scheduled)
            return;

        _scheduled = true;
        _timer = _time.CreateTimer(_ => _ui.Post(_ => Flush(), null), null, Frame, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Sends the strings again (the interface language changed).</summary>
    public void ResendStrings() => Send("strings", _strings());

    public void Send(string type, object? data) =>
        _host.Post(JsonSerializer.Serialize(new WebMessage(WebIntent.Version, type, data), WebJson.Wire));

    public void Dispose()
    {
        _host.MessageReceived -= OnMessage;
        _timer?.Dispose();
    }

    private void Flush()
    {
        _scheduled = false;
        _timer?.Dispose();
        _timer = null;
        Push();
    }

    private void Push() => Send(_stateType, _snapshot(++_rev));

    private void OnMessage(string json)
    {
        var intent = WebIntent.Parse(json, out var reason);
        if (intent is null)
        {
            // Never log the message: it can hold the user's text.
            _logger.LogWarning("Dropped a web message: {Reason}", reason);
            return;
        }

        if (intent is ReadyIntent)
        {
            Start();
            return;
        }

        _handler.Handle(intent);
    }

    /// <summary>
    /// The page is ready to be told everything: the strings, then the first snapshot. Called for the page's own
    /// <c>ready</c> message, and by a host that attached this bridge to a page that had announced itself earlier.
    /// </summary>
    public void Start()
    {
        _ready = true;
        ResendStrings();
        Push();
        Started?.Invoke();
    }

    /// <summary>Raised after the strings and the first snapshot were sent.</summary>
    public event Action? Started;
}
