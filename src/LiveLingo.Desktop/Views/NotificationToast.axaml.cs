using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LiveLingo.Desktop.Views;

public partial class NotificationToast : Window
{
    public event Action? ConfigureRequested;

    public NotificationToast(string message, TimeSpan autoDismiss, string? buttonLabel = null)
    {
        InitializeComponent();

        var msg = this.FindControl<TextBlock>("MessageText");
        if (msg is not null) msg.Text = message;

        var btn = this.FindControl<Button>("ConfigureButton");
        if (btn is not null)
        {
            if (buttonLabel is not null)
                btn.Content = buttonLabel;
            else
                btn.IsVisible = false;
        }

        DispatcherTimer.RunOnce(Close, autoDismiss);
    }

    public NotificationToast() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        Position = new PixelPoint(
            workArea.Right - (int)Width - 16,
            workArea.Bottom - (int)Height - 16);
    }

    private void OnConfigure(object? sender, RoutedEventArgs e)
    {
        ConfigureRequested?.Invoke();
        Close();
    }
}
