using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Web;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Tests.Web;

/// <summary>A page that records what it was sent and lets a test post messages as the page.</summary>
internal sealed class FakeHost : IWebHost
{
    public event Action<string>? MessageReceived;

    public List<string> Posted { get; } = [];

    public void Post(string json) => Posted.Add(json);

    public void FromPage(string json) => MessageReceived?.Invoke(json);

    public void FromPage(WebIntent intent) => FromPage(System.Text.Json.JsonSerializer.Serialize(intent, WebJson.Wire));

    public IEnumerable<string> Types => Posted.Select(json => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("type").GetString()!);
}

/// <summary>Runs posted work immediately, like the UI thread would once it gets to it.</summary>
internal sealed class InlineContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);
}

internal sealed class ListLogger : ILogger
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add($"{logLevel}: {formatter(state, exception)}");
}

internal sealed class FakeFiles : IFileDialogs
{
    public string? Recording { get; set; }

    public string? ExportPath { get; set; }

    public List<ExportFormat> Exports { get; } = [];

    public Exception? Failure { get; set; }

    public Task<string?> PickRecordingAsync() => Failure is null ? Task.FromResult(Recording) : Task.FromException<string?>(Failure);

    public Task<string?> PickExportPathAsync(ExportFormat format)
    {
        Exports.Add(format);
        return Task.FromResult(ExportPath);
    }
}

internal static class Texts
{
    /// <summary>The shipped Chinese strings, so fixtures and tests see what users see.</summary>
    public static ComposerTexts Chinese()
    {
        var loc = new LocalizationService();
        loc.SetCulture("zh-CN");
        return new ComposerTexts(loc);
    }
}
