using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

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

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _recording = true;
        Text = "Press key combination...";
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _recording = false;
        Text = Hotkey;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording) { base.OnKeyDown(e); return; }

        e.Handled = true;

        if (IsModifierKey(e.Key)) return;

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");

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
