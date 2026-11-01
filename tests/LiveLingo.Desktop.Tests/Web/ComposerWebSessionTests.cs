using System.Text.Json;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class ComposerWebSessionTests
{
    private sealed class Rig : IAsyncDisposable
    {
        public Harness H { get; } = new();
        public FakeHost Host { get; } = new();
        public FakeClock Time { get; } = new();
        public ComposerWebSession Session { get; }

        public Rig()
        {
            Session = new ComposerWebSession(
                H.Vm, Texts.Chinese(), new RecentDrafts(), new FakeFiles(), Host, new InlineContext(), Time, new ListLogger());
        }

        public JsonElement Message(int index) => JsonDocument.Parse(Host.Posted[index]).RootElement;

        public ValueTask DisposeAsync()
        {
            Session.Dispose();
            return H.DisposeAsync();
        }
    }

    [Fact]
    public async Task APageThatWasReadyBefore_GetsTheStringsTheSnapshotAndTheFocusOnStart()
    {
        await using var rig = new Rig();

        rig.Session.Start();

        Assert.Equal(["strings", "composer", "focus"], rig.Host.Types);
        Assert.Equal("zh-CN", rig.Message(0).GetProperty("data").GetProperty("locale").GetString());
        Assert.Equal("source", rig.Message(2).GetProperty("data").GetProperty("target").GetString());
    }

    [Fact]
    public async Task APageThatIsStillLoading_StartsTheConversationWithItsReadyMessage()
    {
        await using var rig = new Rig();

        rig.Host.FromPage("""{"v":1,"name":"ready"}""");

        Assert.Equal(["strings", "composer", "focus"], rig.Host.Types);
    }

    [Fact]
    public async Task ADraftAlreadyOnTheScreen_FocusesTheMicrophone()
    {
        await using var rig = new Rig();
        rig.H.Vm.Output = ComposerOutput.Clean;
        rig.H.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => rig.H.Vm.Lines.Count > 0);

        rig.Session.Start();

        Assert.Equal("mic", rig.Message(2).GetProperty("data").GetProperty("target").GetString());
    }

    [Fact]
    public async Task ChangesOfTheViewModel_ReachThePageAsOneSnapshotPerFrame()
    {
        await using var rig = new Rig();
        rig.Session.Start();
        rig.Host.Posted.Clear();

        rig.H.Vm.SourceText = "a";
        rig.H.Vm.SourceText = "ab";
        rig.Time.Advance(WebBridge.Frame);

        Assert.Equal(["composer"], rig.Host.Types);
        Assert.Equal("ab", rig.Message(0).GetProperty("data").GetProperty("source").GetProperty("text").GetString());
    }

    [Fact]
    public async Task AToast_IsShownWithTheTextOfItsKind()
    {
        await using var rig = new Rig();
        rig.Session.Start();
        rig.Host.Posted.Clear();

        await rig.H.Vm.CommitAsync(fromKey: false); // nothing to write

        var toasts = rig.Host.Posted.Select(json => JsonDocument.Parse(json).RootElement).Where(m => m.GetProperty("type").GetString() == "toast").ToList();
        Assert.NotEmpty(toasts);
        Assert.All(toasts, t => Assert.False(string.IsNullOrEmpty(t.GetProperty("data").GetProperty("text").GetString())));
    }

    [Fact]
    public async Task RetargetingARequest_OpensTheTargetMenuInThePage()
    {
        await using var rig = new Rig();
        rig.Session.Start();
        rig.Host.Posted.Clear();

        await rig.H.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.Retarget);

        Assert.Contains(rig.Host.Posted, json => json.Contains("\"openMenu\"") && json.Contains("\"target\""));
    }

    [Fact]
    public async Task TheWidthChanges_WhenTheLayoutSwitches()
    {
        await using var rig = new Rig();
        var changes = 0;
        rig.Session.WideChanged += () => changes++;

        rig.H.Vm.ForceWide = true;
        rig.H.Vm.SourceText = "still wide";
        rig.H.Vm.ForceWide = false;

        Assert.Equal(2, changes);
        Assert.False(rig.Session.Wide);
    }

    [Fact]
    public async Task AMeasurementOfThePage_ReachesTheWindow()
    {
        await using var rig = new Rig();
        SizeIntent? measured = null;
        rig.Session.SizeChanged += size => measured = size;

        rig.Host.FromPage(new SizeIntent(656, 320) { V = 1 });

        Assert.Equal(320, measured?.Height);
    }

    [Fact]
    public async Task AfterDispose_TheViewModelNoLongerReachesThePage()
    {
        await using var rig = new Rig();
        rig.Session.Start();
        rig.Session.Dispose();
        rig.Host.Posted.Clear();

        rig.H.Vm.SourceText = "late";
        rig.Time.Advance(WebBridge.Frame);

        Assert.Empty(rig.Host.Posted);
    }
}
