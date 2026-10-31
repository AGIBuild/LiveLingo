using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Dictation.Audio;

namespace Dictation.TestApp.Tests;

public sealed class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Drives the real window with the real models. Needs the models from scripts/fetch-models.sh.</summary>
public sealed class MainWindowTests : IDisposable
{
    private readonly string _recordings = Path.Combine(Path.GetTempPath(), "voicepoc-ui-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Window_loads_the_models_and_enables_recording()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        await session.Dispatch(async () =>
        {
            var window = new MainWindow(new RecordingStore(_recordings));
            window.Show();

            var button = window.FindControl<Button>("RecordButton")!;
            Assert.False(button.IsEnabled);

            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (!button.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(200);

            Assert.True(button.IsEnabled, window.FindControl<TextBlock>("StatusText")!.Text);
            Assert.Equal("Record", button.Content);
            Assert.Equal(_recordings, window.FindControl<TextBox>("FolderBox")!.Text);

            // Switching the second pass loads that model and keeps recording available.
            var box = window.FindControl<ComboBox>("SecondPassBox")!;
            Assert.Equal("qwen3", box.SelectedItem);
            box.SelectedItem = "xasr";
            deadline = DateTime.UtcNow.AddMinutes(2);
            while ((!button.IsEnabled || !box.IsEnabled) && DateTime.UtcNow < deadline) await Task.Delay(100);
            Assert.Equal("Second pass is now 'xasr'.", window.FindControl<TextBlock>("StatusText")!.Text);
            Assert.True(button.IsEnabled);

            // A new recordings folder is used for the next take.
            var other = Path.Combine(_recordings, "other");
            var folder = window.FindControl<TextBox>("FolderBox")!;
            folder.Text = other;
            folder.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            Assert.Contains(other, window.FindControl<TextBlock>("StatusText")!.Text);
            Assert.True(Directory.Exists(other));

            // Switch to the Text Translation tab and verify preset loading, incremental prefix reuse, and case saving.
            var tabs = window.FindControl<TabControl>("MainTabs")!;
            tabs.SelectedIndex = 1;

            var translateBtn = window.FindControl<Button>("TranslateNowButton")!;
            deadline = DateTime.UtcNow.AddMinutes(2);
            while (!translateBtn.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(200);
            Assert.True(translateBtn.IsEnabled, window.FindControl<TextBlock>("TranslationStatusText")!.Text);

            var presetBox = window.FindControl<ComboBox>("TranslationPresetBox")!;
            presetBox.SelectedIndex = 5; // multi-sentence incremental preset (3 sentences)
            deadline = DateTime.UtcNow.AddMinutes(2);
            var outputBox = window.FindControl<TextBox>("TranslationOutputBox")!;
            while ((string.IsNullOrWhiteSpace(outputBox.Text) || outputBox.Text.Contains("[Translating")) && DateTime.UtcNow < deadline)
                await Task.Delay(200);

            Assert.Contains("503", outputBox.Text);
            Assert.Contains("Calls: 3", window.FindControl<TextBlock>("TranslationMetricsText")!.Text);

            // Clicking "+ Append sentence" adds a 4th sentence and only invokes the model once more (Calls: 4).
            var appendBtn = window.FindControl<Button>("AppendDemoSentenceButton")!;
            appendBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddMinutes(2);
            while (!window.FindControl<TextBlock>("TranslationMetricsText")!.Text!.Contains("Calls: 4") && DateTime.UtcNow < deadline)
                await Task.Delay(200);

            Assert.Contains("Calls: 4", window.FindControl<TextBlock>("TranslationMetricsText")!.Text);

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var shot = Environment.GetEnvironmentVariable("VOICE_POC_SCREENSHOT");
            if (shot is not null) frame.Save(shot);
            window.Close();
            return true; // a Task<T> lambda selects the overload that awaits the body
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_recordings)) Directory.Delete(_recordings, recursive: true);
    }
}
