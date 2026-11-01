using System.Text.Json;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class WebBridgeTests
{
    private sealed class Rig
    {
        public FakeHost Host { get; } = new();
        public FakeClock Time { get; } = new();
        public ListLogger Log { get; } = new();
        public List<WebIntent> Handled { get; } = [];
        public int Snapshots { get; private set; }
        public WebBridge Bridge { get; }

        public Rig()
        {
            var handler = new Recorder(Handled);
            Bridge = new WebBridge(
                Host,
                "composer",
                rev => new { Rev = rev, Count = ++Snapshots },
                () => new StringsData("zh-CN", new Dictionary<string, string> { ["placeholder"] = "说点什么" }),
                handler,
                new InlineContext(),
                Time,
                Log);
        }

        public void Ready() => Host.FromPage("""{"v":1,"name":"ready"}""");

        public void Frame() => Time.Advance(WebBridge.Frame);
    }

    private sealed class Recorder(List<WebIntent> handled) : IIntentHandler
    {
        public void Handle(WebIntent intent) => handled.Add(intent);
    }

    private static JsonElement Message(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void BeforeReady_NothingIsSent()
    {
        var rig = new Rig();

        rig.Bridge.Invalidate();
        rig.Frame();

        Assert.Empty(rig.Host.Posted);
    }

    [Fact]
    public void Ready_SendsTheStringsThenTheFirstSnapshot()
    {
        var rig = new Rig();

        rig.Ready();

        Assert.Equal(["strings", "composer"], rig.Host.Types);
        var strings = Message(rig.Host.Posted[0]);
        Assert.Equal("zh-CN", strings.GetProperty("data").GetProperty("locale").GetString());
        Assert.Equal("说点什么", strings.GetProperty("data").GetProperty("strings").GetProperty("placeholder").GetString());
        Assert.Equal(1, Message(rig.Host.Posted[1]).GetProperty("data").GetProperty("rev").GetInt32());
        Assert.Empty(rig.Handled);
    }

    [Fact]
    public void Changes_WithinOneFrameAreSentAsOneSnapshot()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Host.Posted.Clear();

        for (var i = 0; i < 20; i++)
            rig.Bridge.Invalidate();
        Assert.Empty(rig.Host.Posted);

        rig.Frame();

        Assert.Single(rig.Host.Posted);
    }

    [Fact]
    public void Revisions_OnlyGrow()
    {
        var rig = new Rig();
        rig.Ready();

        for (var i = 0; i < 3; i++)
        {
            rig.Bridge.Invalidate();
            rig.Frame();
        }

        var revs = rig.Host.Posted.Skip(1).Select(json => Message(json).GetProperty("data").GetProperty("rev").GetInt32());
        Assert.Equal([1, 2, 3, 4], revs);
    }

    [Fact]
    public void ReadyAgain_ResendsEverythingForAReloadedPage()
    {
        var rig = new Rig();
        rig.Ready();
        rig.Host.Posted.Clear();

        rig.Ready();

        Assert.Equal(["strings", "composer"], rig.Host.Types);
    }

    [Fact]
    public void Intents_ReachTheHandler()
    {
        var rig = new Rig();
        rig.Ready();

        rig.Host.FromPage("""{"v":1,"name":"commit","fromKey":true}""");

        var commit = Assert.IsType<CommitIntent>(Assert.Single(rig.Handled));
        Assert.True(commit.FromKey);
    }

    [Fact]
    public void RejectedMessages_AreDroppedAndLoggedWithoutTheirContent()
    {
        var rig = new Rig();
        rig.Ready();

        rig.Host.FromPage("""{"v":1,"name":"source","text":"my secret draft","rev":"x"}""");
        rig.Host.FromPage("""{"v":1,"name":"drop-tables"}""");

        Assert.Empty(rig.Handled);
        Assert.Equal(2, rig.Log.Lines.Count);
        Assert.All(rig.Log.Lines, line => Assert.DoesNotContain("secret", line));
        Assert.All(rig.Log.Lines, line => Assert.DoesNotContain("drop-tables", line));
    }

    [Fact]
    public void Send_WrapsAnyDataInTheEnvelope()
    {
        var rig = new Rig();

        rig.Bridge.Send("toast", new { Text = "已复制" });

        var message = Message(Assert.Single(rig.Host.Posted));
        Assert.Equal(1, message.GetProperty("v").GetInt32());
        Assert.Equal("toast", message.GetProperty("type").GetString());
        Assert.Equal("已复制", message.GetProperty("data").GetProperty("text").GetString());
    }

    [Fact]
    public void ResendStrings_SendsTheCurrentLabels()
    {
        var rig = new Rig();

        rig.Bridge.ResendStrings();

        Assert.Equal(["strings"], rig.Host.Types);
    }

    [Fact]
    public void Dispose_StopsListeningToThePage()
    {
        var rig = new Rig();
        rig.Ready();

        rig.Bridge.Dispose();
        rig.Host.FromPage("""{"v":1,"name":"copy"}""");

        Assert.Empty(rig.Handled);
    }
}
