using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace LiveLingo.Desktop.Controls;

public class HotkeyRecorder : Border
{
    public static readonly StyledProperty<string> HotkeyProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(Hotkey), defaultValue: "");

    public string Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private readonly TextBlock _display;
    private bool _recording;

    static HotkeyRecorder()
    {
        FocusableProperty.OverrideDefaultValue<HotkeyRecorder>(true);

        HotkeyProperty.Changed.AddClassHandler<HotkeyRecorder>((r, _) =>
        {
            if (!r._recording)
                r._display.Text = FormatDisplay(r.Hotkey);
        });
    }

    public HotkeyRecorder()
    {
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(10, 7);
        Cursor = new Cursor(StandardCursorType.Hand);
        MinHeight = 34;

        _display = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _display;
    }

    /// <summary>What the box says while it waits for the key combination.</summary>
    public string PromptText { get; set; } = "Press key combination…";

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _display.Text = FormatDisplay(Hotkey);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _recording = true;
        _display.Text = PromptText;
        _display.Classes.Set("prompt", true);
        Classes.Set("recording", true);
        Focus();
        e.Handled = true;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ExitRecording();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            ExitRecording();
            return;
        }

        if (IsModifierKey(e.Key)) return;

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            parts.Add(OperatingSystem.IsMacOS() ? "Option" : "Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");

        if (parts.Count == 0) return;

        parts.Add(e.Key.ToString());
        var combo = string.Join("+", parts);
        Hotkey = combo;
        ExitRecording();
    }

    private void ExitRecording()
    {
        _recording = false;
        _display.Text = FormatDisplay(Hotkey);
        _display.Classes.Set("prompt", false);
        Classes.Set("recording", false);
    }

    private static string FormatDisplay(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return "(none)";
        if (!OperatingSystem.IsMacOS()) return hotkey;

        return hotkey
            .Replace("Win+", "Cmd+", StringComparison.OrdinalIgnoreCase)
            .Replace("Alt+", "Option+", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
}
