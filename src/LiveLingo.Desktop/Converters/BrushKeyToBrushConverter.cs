using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LiveLingo.Desktop.Converters;

/// <summary>
/// Maps a resource key string (e.g. "SuccessBrush") produced by a platform-free
/// view model to the live <see cref="IBrush"/> resolved against the current
/// application theme. This lets view models stay in the
/// <see cref="System.ComponentModel"/> world while still producing presentation
/// hints that respect the theme.
/// </summary>
public sealed class BrushKeyToBrushConverter : IValueConverter
{
    public static readonly BrushKeyToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
            return AvaloniaProperty.UnsetValue;

        var app = Application.Current;
        if (app is null)
            return AvaloniaProperty.UnsetValue;

        return app.Resources.TryGetResource(key, app.ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
