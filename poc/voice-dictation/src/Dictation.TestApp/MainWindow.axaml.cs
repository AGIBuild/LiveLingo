using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Dictation.Audio;
using Dictation.Models;
using TranslationPoc;

namespace Dictation.TestApp;

public sealed partial class MainWindow : Window
{
    private static readonly string[] Directions = ["zh → en", "en → zh"];
    private static readonly string[] DemoAppendSentencesZh =
    [
        "原文和译文都要保留，方便之后核对。",
        "用户没有授权时，不要调用云端服务。",
        "取消之后的旧结果不能覆盖最新输入。"
    ];
    private static readonly string[] DemoAppendSentencesEn =
    [
        "Both the source text and the translation stay visible for verification.",
        "Cloud services are never called without explicit user consent.",
        "Stale results after cancellation cannot overwrite newer input."
    ];

    private readonly TestBench _bench;
    private readonly TranslationBench _translationBench;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _tailFlushTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private DateTime _recordingStarted;
    private string _finalText = "";
    private string _rawText = "";
    private string _translatedText = "";
    private string? _lastTakeId;
    private bool _busy;
    private bool _suppressTranslationEvents;
    private int _appendIndex;

    public MainWindow(RecordingStore store, TranslationCaseStore? translationStore = null)
    {
        _bench = new TestBench(store);
        _translationBench = new TranslationBench(translationStore ?? new TranslationCaseStore(TranslationCaseStore.DefaultDirectory));
        InitializeComponent();
        FolderBox.Text = _bench.Store.Directory;
        SecondPassBox.ItemsSource = ModelCatalog.SecondPassKeys;
        SecondPassBox.SelectedItem = ModelCatalog.DefaultSecondPass;
        SecondPassBox.SelectionChanged += async (_, _) => await ChangeSecondPassAsync();
        FolderBox.LostFocus += (_, _) => ApplyFolder(FolderBox.Text);
        FolderBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) ApplyFolder(FolderBox.Text);
        };
        BrowseButton.Click += async (_, _) => await BrowseAsync();

        RecordButton.Click += async (_, _) => await ToggleAsync();
        OpenFolderButton.Click += (_, _) => Process.Start(new ProcessStartInfo(_bench.Store.Directory) { UseShellExecute = true });
        CopyFinalButton.Click += async (_, _) => await Clipboard!.SetTextAsync(_finalText);
        CopyRawButton.Click += async (_, _) => await Clipboard!.SetTextAsync(_rawText);
        SendToTranslationButton.Click += async (_, _) => await SendSpeechToTranslationAsync();
        SaveReferenceButton.Click += (_, _) => SaveReference();

        InitializeTranslationTab();

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                if (MainTabs.SelectedIndex == 1)
                    await FlushTranslationNowAsync();
                else
                    await ToggleAsync();
            }
        };

        _bench.DraftChanged += draft => Dispatcher.UIThread.Post(() =>
        {
            RawBox.Text = draft.Raw;
            CleanedBox.Text = draft.Cleaned;
        });
        var lastLevelPost = DateTime.MinValue;
        _bench.Level += level =>
        {
            var now = DateTime.UtcNow;
            if (now - lastLevelPost < TimeSpan.FromMilliseconds(60)) return;
            lastLevelPost = now;
            Dispatcher.UIThread.Post(() => LevelBar.Value = Math.Min(1, level * 4));
        };
        _ticker.Tick += (_, _) => UpdateRecordingStatus();

        Opened += async (_, _) =>
        {
            var voiceTask = LoadModelsAsync();
            var transTask = LoadTranslationServerAsync();
            await Task.WhenAll(voiceTask, transTask);
        };
        Closing += (_, _) =>
        {
            _tailFlushTimer.Stop();
            _bench.Dispose();
            _translationBench.Dispose();
        };
    }

    private void InitializeTranslationTab()
    {
        TranslationModelBox.ItemsSource = LlamaTranslationServer.ModelKeys;
        TranslationModelBox.SelectedItem = LlamaTranslationServer.DefaultModelKey;
        TranslationDirectionBox.ItemsSource = Directions;
        TranslationDirectionBox.SelectedIndex = 0;
        TranslationPresetBox.ItemsSource = TranslationPresets.All.Select(p => p.Title).ToList();

        TranslationModelBox.SelectionChanged += async (_, _) => await SwitchTranslationModelAsync();
        TranslationDirectionBox.SelectionChanged += (_, _) => ApplyTranslationOptions();
        TranslationGlossaryBox.LostFocus += (_, _) => ApplyTranslationOptions();
        TranslationGlossaryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) ApplyTranslationOptions();
        };
        TranslationPresetBox.SelectionChanged += async (_, _) => await ApplySelectedPresetAsync();
        TranslationSourceBox.TextChanged += (_, _) => OnTranslationSourceChanged();
        TranslateNowButton.Click += async (_, _) => await FlushTranslationNowAsync();
        TranslateWholeButton.Click += async (_, _) => await TranslateWholeTextAsync();
        ClearTranslationButton.Click += (_, _) => ClearTranslation();
        AppendDemoSentenceButton.Click += (_, _) => AppendDemoSentence();
        CopyTranslationButton.Click += async (_, _) => await Clipboard!.SetTextAsync(_translatedText);
        SaveTranslationCaseButton.Click += (_, _) => SaveTranslationCase();
        OpenTranslationFolderButton.Click += (_, _) => Process.Start(new ProcessStartInfo(_translationBench.Store.Directory) { UseShellExecute = true });

        _tailFlushTimer.Tick += async (_, _) =>
        {
            _tailFlushTimer.Stop();
            if (AutoFlushTailCheck.IsChecked == true && _translationBench.IsReady)
                await _translationBench.FlushTailAsync();
        };

        _translationBench.Changed += view => Dispatcher.UIThread.Post(() =>
        {
            _translatedText = view.TranslatedText;
            TranslationOutputBox.Text = view.TranslatedText;
            TranslationMetricsText.Text = view.MetricsText;
            TranslationUnitLogBox.Text = view.UnitLogText;
            var hasContent = !string.IsNullOrWhiteSpace(TranslationSourceBox.Text);
            CopyTranslationButton.IsEnabled = !string.IsNullOrWhiteSpace(view.TranslatedText);
            SaveTranslationCaseButton.IsEnabled = hasContent;
        });
    }

    private string CurrentTargetLanguage =>
        TranslationDirectionBox.SelectedIndex == 1 ? "zh" : "en";

    private IReadOnlyList<Term> CurrentTranslationTerms =>
        TermParser.Parse(TranslationGlossaryBox.Text);

    private async Task LoadTranslationServerAsync()
    {
        var started = Stopwatch.StartNew();
        var modelKey = (string?)TranslationModelBox.SelectedItem ?? LlamaTranslationServer.DefaultModelKey;
        TranslationStatusText.Text = $"Starting local llama-server ({modelKey}, Metal)...";
        try
        {
            await _translationBench.StartOrSwitchModelAsync(
                modelKey,
                CurrentTargetLanguage,
                CurrentTranslationTerms,
                TranslationSourceBox.Text ?? "");
        }
        catch (Exception e)
        {
            TranslateNowButton.Content = "Server unavailable";
            TranslationStatusText.Text = $"Cannot start translation server: {e.Message}";
            return;
        }
        TranslateNowButton.Content = "Translate / Flush Tail";
        TranslateNowButton.IsEnabled = true;
        TranslateWholeButton.IsEnabled = true;
        ClearTranslationButton.IsEnabled = true;
        AppendDemoSentenceButton.IsEnabled = true;
        TranslationStatusText.Text = $"Ready ({modelKey} loaded in {started.Elapsed.TotalSeconds:F1} s). Type, pick a preset, or click '+ Append sentence'.";
    }

    private async Task SwitchTranslationModelAsync()
    {
        if (!_translationBench.IsReady) return;
        var modelKey = (string?)TranslationModelBox.SelectedItem ?? LlamaTranslationServer.DefaultModelKey;
        TranslateNowButton.IsEnabled = TranslationModelBox.IsEnabled = false;
        try
        {
            TranslationStatusText.Text = $"Switching translation model to '{modelKey}'...";
            await _translationBench.StartOrSwitchModelAsync(
                modelKey,
                CurrentTargetLanguage,
                CurrentTranslationTerms,
                TranslationSourceBox.Text ?? "");
            TranslationStatusText.Text = $"Translation model is now '{modelKey}'.";
            if (!string.IsNullOrWhiteSpace(TranslationSourceBox.Text))
                await _translationBench.FlushTailAsync();
        }
        catch (Exception e)
        {
            TranslationStatusText.Text = $"Switch failed: {e.Message}";
        }
        finally
        {
            TranslateNowButton.IsEnabled = TranslationModelBox.IsEnabled = true;
        }
    }

    private void ApplyTranslationOptions()
    {
        if (_suppressTranslationEvents || !_translationBench.IsReady) return;
        _translationBench.Configure(CurrentTargetLanguage, CurrentTranslationTerms);
        if (AutoFlushTailCheck.IsChecked == true)
        {
            _tailFlushTimer.Stop();
            _tailFlushTimer.Start();
        }
    }

    private async Task ApplySelectedPresetAsync()
    {
        var idx = TranslationPresetBox.SelectedIndex;
        if (idx < 0 || idx >= TranslationPresets.All.Count) return;
        var preset = TranslationPresets.All[idx];

        _suppressTranslationEvents = true;
        try
        {
            TranslationDirectionBox.SelectedIndex = preset.TargetLanguage == "zh" ? 1 : 0;
            TranslationGlossaryBox.Text = preset.Glossary;
            PresetHintText.Text = preset.WhatToVerify.Summary;
            _translationBench.Configure(CurrentTargetLanguage, CurrentTranslationTerms);
            TranslationSourceBox.Text = preset.Source;
        }
        finally
        {
            _suppressTranslationEvents = false;
        }

        _appendIndex = 0;
        if (_translationBench.IsReady)
        {
            _translationBench.ResetSession();
            _translationBench.UpdateSource(preset.Source);
            await _translationBench.FlushTailAsync();
        }
    }

    private void ClearTranslation()
    {
        _tailFlushTimer.Stop();
        _suppressTranslationEvents = true;
        try
        {
            TranslationPresetBox.SelectedIndex = -1;
            TranslationSourceBox.Text = "";
            TranslationReferenceBox.Text = "";
        }
        finally
        {
            _suppressTranslationEvents = false;
        }
        _appendIndex = 0;
        _translationBench.ResetSession();
    }

    private void OnTranslationSourceChanged()
    {
        if (_suppressTranslationEvents || !_translationBench.IsReady) return;
        var text = TranslationSourceBox.Text ?? "";
        _translationBench.UpdateSource(text);
        _tailFlushTimer.Stop();
        if (AutoFlushTailCheck.IsChecked == true && !string.IsNullOrWhiteSpace(text))
            _tailFlushTimer.Start();
    }

    private async Task FlushTranslationNowAsync()
    {
        if (!_translationBench.IsReady) return;
        _tailFlushTimer.Stop();
        _translationBench.UpdateSource(TranslationSourceBox.Text ?? "");
        await _translationBench.FlushTailAsync();
    }

    private async Task TranslateWholeTextAsync()
    {
        if (!_translationBench.IsReady || string.IsNullOrWhiteSpace(TranslationSourceBox.Text)) return;
        _tailFlushTimer.Stop();
        TranslateWholeButton.IsEnabled = false;
        TranslationStatusText.Text = "Translating whole text...";
        try
        {
            await _translationBench.TranslateWholeAsync(TranslationSourceBox.Text!);
            TranslationStatusText.Text = "Whole-text translation finished.";
        }
        catch (Exception e)
        {
            TranslationStatusText.Text = $"Whole-text translation failed: {e.Message}";
        }
        finally
        {
            TranslateWholeButton.IsEnabled = true;
        }
    }

    private void AppendDemoSentence()
    {
        if (!_translationBench.IsReady) return;
        var pool = CurrentTargetLanguage == "zh" ? DemoAppendSentencesEn : DemoAppendSentencesZh;
        var addition = pool[_appendIndex++ % pool.Length];
        var current = (TranslationSourceBox.Text ?? "").TrimEnd();
        var sep = current.Length == 0 ? "" : (CurrentTargetLanguage == "zh" ? " " : "");
        TranslationSourceBox.Text = current + sep + addition;
    }

    private void SaveTranslationCase()
    {
        var saved = _translationBench.SaveCase(TranslationSourceBox.Text ?? "", TranslationReferenceBox.Text ?? "");
        TranslationStatusText.Text = $"Saved case {saved.Id}.json to {_translationBench.Store.Directory}";
    }

    private async Task SendSpeechToTranslationAsync()
    {
        var speech = !string.IsNullOrWhiteSpace(_finalText) ? _finalText : CleanedBox.Text ?? RawBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(speech)) return;
        MainTabs.SelectedIndex = 1;
        TranslationSourceBox.Text = speech;
        if (_translationBench.IsReady)
            await _translationBench.FlushTailAsync();
    }

    private async Task LoadModelsAsync()
    {
        var started = Stopwatch.StartNew();
        try
        {
            await _bench.LoadModelsAsync((string)SecondPassBox.SelectedItem!);
        }
        catch (Exception e)
        {
            RecordButton.Content = "Models missing";
            StatusText.Text = $"Cannot load models: {e.Message} Run scripts/fetch-models.sh first.";
            return;
        }
        RecordButton.Content = "Record";
        RecordButton.IsEnabled = true;
        StatusText.Text = $"Models loaded in {started.Elapsed.TotalSeconds:F1} s. Click Record, speak, then Stop (Cmd/Ctrl+Enter).";
    }

    private async Task ChangeSecondPassAsync()
    {
        var key = (string?)SecondPassBox.SelectedItem;
        if (key is null || key == _bench.SecondPassKey || !RecordButton.IsEnabled) return;

        RecordButton.IsEnabled = SecondPassBox.IsEnabled = false;
        StatusText.Text = $"Loading second-pass recognizer '{key}'...";
        await _bench.SelectSecondPassAsync(key);
        StatusText.Text = $"Second pass is now '{key}'.";
        RecordButton.IsEnabled = SecondPassBox.IsEnabled = true;
    }

    private void ApplyFolder(string? path)
    {
        path = path?.Trim();
        if (string.IsNullOrEmpty(path) || path == _bench.Store.Directory) return;
        try
        {
            _bench.UseRecordingsDirectory(path);
            StatusText.Text = $"New takes are saved to {path}";
        }
        catch (Exception e)
        {
            FolderBox.Text = _bench.Store.Directory;
            StatusText.Text = $"Cannot use folder: {e.Message}";
        }
    }

    private async Task BrowseAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Recordings folder", AllowMultiple = false });
        if (folders.Count == 0) return;
        FolderBox.Text = folders[0].Path.LocalPath;
        ApplyFolder(FolderBox.Text);
    }

    private async Task ToggleAsync()
    {
        if (_busy || !RecordButton.IsEnabled) return;
        _busy = true;
        try
        {
            if (_bench.IsRecording) await StopAsync();
            else StartRecording();
        }
        finally
        {
            _busy = false;
        }
    }

    private void StartRecording()
    {
        var glossary = (GlossaryBox.Text ?? "")
            .Split([',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            _bench.StartTake(glossary, SaveAudio.IsChecked == true);
        }
        catch (Exception e)
        {
            StatusText.Text = "Cannot start recording: " + e.Message;
            return;
        }

        RawBox.Text = CleanedBox.Text = SentenceLogBox.Text = ResultText.Text = "";
        CleanedHeader.Text = "Cleaned draft (becomes the final text on stop)";
        _finalText = _rawText = "";
        _lastTakeId = null;
        CopyFinalButton.IsEnabled = CopyRawButton.IsEnabled = SendToTranslationButton.IsEnabled = SaveReferenceButton.IsEnabled = ReferenceBox.IsEnabled = false;
        ReferenceBox.Text = "";
        SaveAudio.IsEnabled = GlossaryBox.IsEnabled = SecondPassBox.IsEnabled = FolderBox.IsEnabled = BrowseButton.IsEnabled = false;

        _recordingStarted = DateTime.Now;
        RecordButton.Content = "Stop and commit";
        _ticker.Start();
    }

    private async Task StopAsync()
    {
        _ticker.Stop();
        RecordButton.IsEnabled = false;
        StatusText.Text = "Finishing: recognizing the tail and cleaning the last sentences...";

        var outcome = await _bench.StopTakeAsync();
        var commit = outcome.Commit;
        _finalText = commit.Text;
        _rawText = commit.RawText;
        RawBox.Text = commit.RawText;
        CleanedBox.Text = commit.Text;
        CleanedHeader.Text = "Final text (what Enter would insert)";
        LevelBar.Value = 0;

        ResultText.Text = $"Waited {commit.WaitMs / 1000:F1} s after stop. {commit.Sentences} sentences: " +
                          $"{commit.Cleaned} cleaned, {commit.Rejected} rejected by the fidelity gate, {commit.RawOnly} left raw." +
                          (commit.TimedOut ? " TIMED OUT." : "");
        CopyFinalButton.IsEnabled = CopyRawButton.IsEnabled = SendToTranslationButton.IsEnabled = true;

        if (outcome.Record is { } record)
        {
            _lastTakeId = record.Id;
            SentenceLogBox.Text = string.Join("\n", record.Sentences.Select(s =>
                $"[{s.State}] raw: {s.Raw}\n    cleaned: {s.Cleaned ?? "-"}" +
                (s.RejectReason is null ? "" : $"\n    rejected ({s.RejectReason}): {s.RejectedText}")));
            ReferenceBox.IsEnabled = SaveReferenceButton.IsEnabled = true;
            StatusText.Text = $"Saved take {record.Id} ({record.DurationMs / 1000.0:F1} s) to the recordings folder.";
        }
        else
        {
            StatusText.Text = "Done. Audio was not saved.";
        }

        SaveAudio.IsEnabled = GlossaryBox.IsEnabled = SecondPassBox.IsEnabled = FolderBox.IsEnabled = BrowseButton.IsEnabled = true;
        RecordButton.Content = "Record";
        RecordButton.IsEnabled = true;
    }

    private void SaveReference()
    {
        _bench.Store.SetReference(_lastTakeId!, ReferenceBox.Text ?? "");
        StatusText.Text = $"Reference saved for take {_lastTakeId}.";
    }

    private void UpdateRecordingStatus()
    {
        var seconds = (DateTime.Now - _recordingStarted).TotalSeconds;
        StatusText.Text = seconds > 1.5 && _bench.Peak == 0
            ? $"Recording {seconds:F0} s, but the microphone delivers silence. Check the OS microphone permission for this app."
            : $"Recording {seconds:F0} s";
    }
}
