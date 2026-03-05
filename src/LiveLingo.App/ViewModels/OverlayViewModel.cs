using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.App.Services.Platform.Windows;

namespace LiveLingo.App.ViewModels;

public enum InjectionMode
{
    PasteOnly,
    PasteAndSend
}

public partial class OverlayViewModel : ObservableObject
{
    private readonly TargetWindowInfo _targetWindow;
    private CancellationTokenSource? _debounceCts;

    private static InjectionMode _lastMode = InjectionMode.PasteAndSend;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _translatedText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private InjectionMode _mode;

    [ObservableProperty]
    private string _modeLabel = string.Empty;

    public IntPtr TargetWindowHandle => _targetWindow.Handle;
    public IntPtr TargetInputChild => _targetWindow.InputChildHandle;
    public bool AutoSend => Mode == InjectionMode.PasteAndSend;

    public event Action? RequestClose;

    public OverlayViewModel(TargetWindowInfo targetWindow)
    {
        _targetWindow = targetWindow;
        Mode = _lastMode;
        UpdateModeDisplay();
    }

    partial void OnSourceTextChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = TranslateWithDebounce(value, token);
    }

    private async Task TranslateWithDebounce(string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            if (ct.IsCancellationRequested) return;

            // PoC stub: real implementation calls ONNX Runtime
            TranslatedText = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : $"[EN] {text}";
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        Mode = Mode == InjectionMode.PasteAndSend
            ? InjectionMode.PasteOnly
            : InjectionMode.PasteAndSend;

        _lastMode = Mode;
        UpdateModeDisplay();
    }

    private void UpdateModeDisplay()
    {
        if (Mode == InjectionMode.PasteAndSend)
        {
            ModeLabel = "Paste & Send";
            StatusText = "Ctrl+Enter paste & send · Esc cancel";
        }
        else
        {
            ModeLabel = "Paste Only";
            StatusText = "Ctrl+Enter paste · Esc cancel";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
        NativeMethods.SetForegroundWindow(_targetWindow.Handle);
    }
}
