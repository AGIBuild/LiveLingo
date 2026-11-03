using Avalonia;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Tests.Web;

namespace LiveLingo.Desktop.Tests.Composer;

public sealed class ComposerGeometryLogTests
{
    private static ComposerGeometry Geometry(
        double height = 560, double clientHeight = 560, int y = 200, double maxHeight = 900, double? userHeight = null) =>
        new(656, height, new Size(656, clientHeight), 400, y, maxHeight, null, userHeight, 540, false, false, 1.25,
            new PixelRect(0, 0, 1920, 1040));

    private readonly ListLogger _lines = new();

    private ComposerGeometryLog Log() => new(_lines);

    [Fact]
    public void EachSummon_HasItsOwnNumber_AndEveryLineCarriesTheGeometry()
    {
        var log = Log();

        log.SummonStarted("Slack rect=(10,10,800x600)", Geometry());
        log.Sized(620, 540, Geometry());
        log.SummonStarted("none", Geometry());

        Assert.Equal(3, _lines.Lines.Count);
        Assert.Contains("summon=1 event=summon target=Slack", _lines.Lines[0]);
        Assert.Contains("summon=1 event=size page=620x540css", _lines.Lines[1]);
        Assert.Contains("summon=2 event=summon target=none", _lines.Lines[2]);
        Assert.All(_lines.Lines, line =>
        {
            Assert.Contains("window=656x560dip", line);
            Assert.Contains("work=(0,0,1920x1040)px", line);
            Assert.Contains("scale=1.25", line);
        });
    }

    [Fact]
    public void AWindowCapLowerThanThePage_IsAWarning_OnlyWhileTheWindowFollowsThePage()
    {
        var log = Log();

        log.Sized(620, 1200, Geometry(maxHeight: 900));
        log.Sized(620, 1200, Geometry(maxHeight: 900, userHeight: 700));

        Assert.Single(_lines.Lines, line => line.StartsWith("Warning:") && line.Contains("PROBLEM=content-taller-than-window-cap"));
    }

    [Fact]
    public void ASystemThatChangedTheSize_IsAWarning_AndAnObeyedRequestIsNot()
    {
        var log = Log();

        log.SystemResized(Geometry(height: 560, clientHeight: 560));
        log.SystemResized(Geometry(height: 560, clientHeight: 280));

        Assert.Equal(["Information", "Warning"], _lines.Lines.Select(line => line[..line.IndexOf(':')]));
        Assert.Contains("PROBLEM=system-changed-the-size", _lines.Lines[1]);
        Assert.Contains("client=656x280", _lines.Lines[1]);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(700, true)]
    public void AWindowBelowTheWorkingArea_IsAWarning(int y, bool outside)
    {
        var log = Log();

        log.CheckOnScreen(Geometry(y: y));

        Assert.Equal(outside, _lines.Lines.Any(line => line.Contains("PROBLEM=window-outside-the-working-area")));
    }

    [Fact]
    public void ABurst_IsLoggedOnce()
    {
        var log = Log();

        for (var i = 0; i < 20; i++)
            log.InfoThrottled("user-resize", Geometry(), "asked=700x-css");

        Assert.Single(_lines.Lines);
    }

    [Fact]
    public void ARepeatedProblem_IsLoggedOnceAWhile_AndADifferentOneStillIs()
    {
        var log = Log();

        for (var i = 0; i < 20; i++)
            log.SystemResized(Geometry(height: 560, clientHeight: 280));
        log.Sized(620, 1200, Geometry(maxHeight: 900));

        Assert.Equal(
            ["system-changed-the-size", "content-taller-than-window-cap"],
            _lines.Lines.Where(line => line.StartsWith("Warning:")).Select(line => line.Split("PROBLEM=")[1].Split(' ')[0]));
    }
}
