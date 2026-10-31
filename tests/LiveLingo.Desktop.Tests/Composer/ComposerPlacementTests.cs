using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerPlacementTests
{
    private static readonly PixelBounds Screen = new(0, 0, 1920, 1080);

    private static TargetWindowInfo Target(int left, int top, int width, int height) =>
        new(1, 0, "App", "t", left, top, width, height);

    [Fact]
    public void IsCenteredOnTheTarget_WithItsBottomEdgeNearTheTargetsBottom()
    {
        var position = ComposerPlacement.For(Target(200, 100, 1000, 800), 640, 240, scaling: 1, Screen);

        Assert.Equal(200 + (1000 - 640) / 2, position.X);
        Assert.Equal(100 + 800 - 240 - ComposerPlacement.BottomMargin, position.Y);
    }

    [Fact]
    public void ScalingConvertsDipsToPixels()
    {
        var position = ComposerPlacement.For(Target(100, 100, 1500, 800), 640, 240, scaling: 2, Screen);

        Assert.Equal(100 + (1500 - 640 * 2) / 2, position.X);
        Assert.Equal(100 + 800 - 240 * 2 - ComposerPlacement.BottomMargin, position.Y);
    }

    [Fact]
    public void StaysInsideTheScreen()
    {
        var offLeft = ComposerPlacement.For(Target(-300, 900, 300, 600), 640, 240, 1, Screen);
        Assert.Equal(0, offLeft.X);
        Assert.Equal(1080 - 240, offLeft.Y);

        var offRight = ComposerPlacement.For(Target(1800, -50, 400, 100), 640, 240, 1, Screen);
        Assert.Equal(1920 - 640, offRight.X);
        Assert.Equal(0, offRight.Y);
    }

    [Fact]
    public void ATargetSmallerThanTheComposer_StillGetsAFullyVisibleWindow()
    {
        var position = ComposerPlacement.For(Target(100, 100, 300, 100), 640, 240, 1, Screen);

        Assert.InRange(position.X, 0, 1920 - 640);
        Assert.InRange(position.Y, 0, 1080 - 240);
    }
}
