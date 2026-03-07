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
        Background = Brush.Parse("#0D0D0F");
        BorderBrush = Brush.Parse("#2A2A2C");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(10, 6);
        Cursor = new Cursor(StandardCursorType.Hand);
        MinHeight = 32;

        _display = new TextBlock
        {
            Foreground = Brush.Parse("#E5E5E7"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _display;
    }

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
        _display.Text = "Press key combination…";
        _display.Foreground = Brush.Parse("#8E8E93");
        BorderBrush = Brush.Parse("#0A84FF");
        Focus();
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (!_recording)
            BorderBrush = Brush.Parse("#0A84FF");
    }

    protected override void OnLostFocus(RoutedEventArgs e)
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
        _display.Foreground = Brush.Parse("#E5E5E7");
        BorderBrush = Brush.Parse(IsFocused ? "#0A84FF" : "#2A2A2C");
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
