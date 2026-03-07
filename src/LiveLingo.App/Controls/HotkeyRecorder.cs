using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LiveLingo.App.Controls;

public class HotkeyRecorder : TextBox
{
    public static readonly StyledProperty<string> HotkeyProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(Hotkey), defaultValue: "");

    public string Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private bool _recording;

    static HotkeyRecorder()
    {
        HotkeyProperty.Changed.AddClassHandler<HotkeyRecorder>((r, _) =>
        {
            if (!r._recording)
                r.Text = r.Hotkey;
        });
    }

    public HotkeyRecorder()
    {
        IsReadOnly = true;
        Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand);
        Text = Hotkey;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (!_recording)
            Text = Hotkey;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus();
        _recording = true;
        Text = "Press key combination...";
        e.Handled = true;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _recording = false;
        Text = Hotkey;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording) return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _recording = false;
            Text = Hotkey;
            return;
        }

        if (IsModifierKey(e.Key)) return;

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");

        if (parts.Count == 0) return;

        parts.Add(e.Key.ToString());

        var combo = string.Join("+", parts);
        Hotkey = combo;
        Text = combo;
        _recording = false;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
}
