using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Web;

/// <summary>
/// The platform web view (WKWebView on macOS, WebView2 on Windows) hosting one page of the app. It is a closed box:
/// no network, no navigation away, no new windows, no persisted web data. Everything that crosses goes through
/// <see cref="WebBridge"/>. Only the page it was built for is ever shown.
/// </summary>
internal sealed class WebSurface : IWebHost
{
    private static readonly Uri BaseUri = new("https://livelingo.invalid/");

    private readonly ILogger _logger;
    private bool _navigated;

    public WebSurface(ILogger logger)
    {
        _logger = logger;
        View = new NativeWebView { Background = Brushes.Transparent };
        View.EnvironmentRequested += (_, e) => Isolate(e);
        View.AdapterCreated += (_, _) => Load();
        View.WebMessageReceived += (view, e) =>
        {
            var message = e.Body ?? "";
            IsReady = IsReady || WebIntent.Parse(message, out _) is ReadyIntent;
            MessageReceived?.Invoke(message);
        };
        View.NavigationStarted += (_, e) =>
        {
            // The page is loaded once from a string; any other navigation (a link, a redirect, a script) is refused.
            if (_navigated && e.Request != BaseUri)
            {
                e.Cancel = true;
                logger.LogWarning("Refused a web view navigation");
            }
        };
        View.NavigationCompleted += (_, e) => _navigated |= e.IsSuccess;
        View.NewWindowRequested += (_, e) => e.Handled = true;
    }

    public NativeWebView View { get; }

    public event Action<string>? MessageReceived;

    /// <summary>The page announced itself with <c>ready</c> and has not been loaded again since.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Loads the page again with a fresh nonce; the page announces itself with <c>ready</c> and gets everything again.</summary>
    public void Load()
    {
        _navigated = false;
        IsReady = false;
        View.NavigateToString(WebPage.Build(Nonce()), BaseUri);
    }

    public void Post(string json) => _ = Run(View.InvokeScript($"void LL.receive({JsonSerializer.Serialize(json)})"));

    private async Task Run(Task<string?> script)
    {
        try
        {
            await script.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The type only: the script carries the user's text.
            _logger.LogError("A web view script failed: {Exception}", ex.GetType().Name);
        }
    }

    private static string Nonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    /// <summary>No developer tools and nothing kept between runs: the page holds a draft, never web storage.</summary>
    private static void Isolate(WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;
        switch (e)
        {
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                apple.NonPersistentDataStore = true;
                break;
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                windows.IsInPrivateModeEnabled = true;
                windows.UserDataFolder = Path.Combine(Path.GetTempPath(), "LiveLingo", "WebView2");
                break;
        }
    }
}
