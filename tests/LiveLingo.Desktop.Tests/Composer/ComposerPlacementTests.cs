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

    [Fact]
    public void ATargetThatExtendsBelowTheScreen_StillGetsAWindowWhoseFooterIsVisible()
    {
        var position = ComposerPlacement.For(Target(0, 800, 1920, 600), 656, 520, 1, Screen);

        Assert.Equal(1080 - 520, position.Y);
    }

    [Fact]
    public void Clamp_KeepsAMovedWindowFullyOnTheScreen()
    {
        Assert.Equal((0, 0), ComposerPlacement.Clamp(-50, -20, 656, 300, 1, Screen));
        Assert.Equal((1920 - 656, 1080 - 300), ComposerPlacement.Clamp(5000, 5000, 656, 300, 1, Screen));
        Assert.Equal((400, 300), ComposerPlacement.Clamp(400, 300, 656, 300, 1, Screen));
    }

    [Fact]
    public void Clamp_UsesPixelsForADisplayWithScaling()
    {
        var retina = new PixelBounds(0, 0, 3024, 1964);

        Assert.Equal((3024 - 1312, 1964 - 600), ComposerPlacement.Clamp(9999, 9999, 656, 300, 2, retina));
    }

    [Fact]
    public void Clamp_OfAWindowLargerThanTheScreen_PinsItsTopLeft()
    {
        Assert.Equal((0, 0), ComposerPlacement.Clamp(300, 300, 2000, 1200, 1, Screen));
    }

    [Fact]
    public void Clamp_KeepsTheWorkAreaOffset()
    {
        var second = new PixelBounds(1920, 25, 1920, 1055);

        Assert.Equal((1920, 25), ComposerPlacement.Clamp(100, 0, 656, 300, 1, second));
    }

    [Theory]
    [InlineData(1055, 1, 470)]
    [InlineData(700, 1, 470)]
    [InlineData(500, 1, 448)]
    [InlineData(480, 1, 428)]
    [InlineData(800, 2, 348)]
    public void MaxPanelHeight_LeavesRoomForTheShadowAndTheScreenEdges(int workHeightPx, double scaling, int expected)
    {
        Assert.Equal(expected, ComposerPlacement.MaxPanelHeight(workHeightPx, scaling));
    }

    [Fact]
    public void MaxPanelHeight_NeverDropsBelowAUsableSize()
    {
        Assert.Equal(ComposerPlacement.MinPanelHeight, ComposerPlacement.MaxPanelHeight(100, 1));
    }
}
