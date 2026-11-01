using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace LiveLingo.Desktop.Controls;

/// <summary>
/// A line icon drawn on a 24×24 grid at its own size, as the prototype's SVG icons are. A path stretched to its bounds
/// would blow a small drawing up to the whole box.
/// </summary>
public sealed class Icon : Control
{
    private const double Grid = 24;
    private const double StrokeWidth = 1.8;

    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Icon, double>(nameof(Size), 18);

    /// <summary>Overrides the foreground of the parent (the banner tints its icon).</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Icon, IBrush?>(nameof(Stroke));

    static Icon()
    {
        AffectsMeasure<Icon>(SizeProperty);
        AffectsRender<Icon>(DataProperty, SizeProperty, StrokeProperty, TextElement.ForegroundProperty);
        HorizontalAlignmentProperty.OverrideDefaultValue<Icon>(Avalonia.Layout.HorizontalAlignment.Center);
        VerticalAlignmentProperty.OverrideDefaultValue<Icon>(Avalonia.Layout.VerticalAlignment.Center);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Data is null)
            return;

        var scale = Size / Grid;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            var pen = new Pen(Stroke ?? TextElement.GetForeground(this), StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, Data);
        }
    }
}
