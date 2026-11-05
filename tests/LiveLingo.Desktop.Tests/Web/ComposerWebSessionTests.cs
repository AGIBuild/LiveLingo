using System.Text.Json;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Web;
using LiveLingo.Core.Inference;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class ComposerWebSessionTests
{
    private sealed class Rig : IAsyncDisposable
    {
        public Harness H { get; } = new();
        public FakeHost Host { get; } = new();
        public FakeClock Time { get; } = new();
        public ComposerWebSession Session { get; }
        public ModelUsage Usage { get; } = new();

        public Rig()
        {
            Session = new ComposerWebSession(
                H.Vm, Texts.Chinese(), new RecentDrafts(), new FakeFiles(), Host, new InlineContext(), Time, new ListLogger(), Usage);
        }

        public JsonElement Message(int index) => JsonDocument.Parse(Host.Posted[index]).RootElement;

        public JsonElement Last => Message(Host.Posted.Count - 1);

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

    private static async Task<Rig> RigWithResultAsync(ComposerOutput output)
    {
        var rig = new Rig();
        rig.H.Vm.Output = output;
        rig.H.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => rig.H.Vm.Lines.Count > 0 && rig.H.Vm.Status == ComposerStatus.Ready);
        rig.Session.Start();
        rig.Host.Posted.Clear();
        return rig;
    }

    private static JsonElement Footer(JsonElement message) => message.GetProperty("data").GetProperty("footer");

    [Fact]
    public async Task ActualModelUse_ReachesThePageInTheNextSnapshot_AsALine_NotAsAToast()
    {
        await using var rig = await RigWithResultAsync(ComposerOutput.Clean);

        rig.Usage.Report(new ModelUse(ModelStage.Cleaner, "Qwen3.5 4B (text cleanup)", false, true, "Qwen3.5 4B"));
        rig.Time.Advance(WebBridge.Frame);

        Assert.Equal(["composer"], rig.Host.Types);
        var model = Footer(rig.Message(0)).GetProperty("model");
        Assert.Equal("整理 · Qwen3.5 4B · 本地", model.GetProperty("text").GetString());
        Assert.Equal("local", model.GetProperty("tone").GetString());
        Assert.Contains("Qwen3.5 4B (text cleanup)", model.GetProperty("title").GetString());
        rig.Host.Posted.Clear();
        rig.Usage.Report(new ModelUse(ModelStage.Cleaner, "Qwen3.5 4B (text cleanup)", false, true, "Qwen3.5 4B"));
        rig.Time.Advance(WebBridge.Frame);
        Assert.Empty(rig.Host.Posted);
    }

    [Fact]
    public async Task TheCardOfThePrivacyChip_ListsEveryStageWithTheActualModelsInFull()
    {
        await using var rig = await RigWithResultAsync(ComposerOutput.Clean);

        rig.Usage.Report(new ModelUse(ModelStage.Cleaner, "Qwen3.5 4B (text cleanup)", false, true, "Qwen3.5 4B"));
        rig.Time.Advance(WebBridge.Frame);

        var rows = rig.Message(0).GetProperty("data").GetProperty("privacy").GetProperty("models").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains("尚未使用", rows[0].GetProperty("text").GetString());
        Assert.Contains("Qwen3.5 4B (text cleanup)", rows[1].GetProperty("text").GetString());
        Assert.Contains("尚未使用", rows[2].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ModelSwitching_DistinguishesRequestedFromPreviouslyUsed()
    {
        await using var rig = await RigWithResultAsync(ComposerOutput.Translate);
        rig.Usage.Report(new ModelUse(ModelStage.Translator, "Hy-MT2 1.8B (fast translation)", false, true, "Hy-MT2-1.8B"));
        rig.Time.Advance(WebBridge.Frame);
        rig.Host.Posted.Clear();

        rig.Usage.Report(new ModelUse(ModelStage.Translator, "Hy-MT2 7B (quality translation)", false, false, "Hy-MT2-7B"));
        rig.Time.Advance(WebBridge.Frame);

        var model = Footer(rig.Message(0)).GetProperty("model");
        Assert.Equal("翻译 · 正在切换到 Hy-MT2-7B（上次 Hy-MT2-1.8B）", model.GetProperty("text").GetString());
        Assert.Equal("info", model.GetProperty("tone").GetString());
    }

    [Fact]
    public async Task ACloudRequest_NamesTheEndpoint_AndTheChipSaysCloudForTheRestOfTheWindow()
    {
        await using var rig = await RigWithResultAsync(ComposerOutput.Translate);

        rig.Usage.Report(new ModelUse(ModelStage.Translator, "gpt-4o-mini", true, true, Endpoint: "Foundry · 东亚"));
        rig.Time.Advance(WebBridge.Frame);
        rig.Usage.Report(new ModelUse(ModelStage.Translator, "Hy-MT2 7B (quality translation)", false, true, "Hy-MT2-7B"));
        rig.Time.Advance(WebBridge.Frame);

        var data = rig.Last.GetProperty("data");
        Assert.True(data.GetProperty("privacy").GetProperty("cloud").GetBoolean());
        Assert.Equal("云端", data.GetProperty("privacy").GetProperty("text").GetString());
        Assert.Equal("翻译 · Hy-MT2-7B · 本地", data.GetProperty("footer").GetProperty("model").GetProperty("text").GetString());
    }

    [Fact]
    public async Task WithoutAnyModelUse_TheFooterHasNoModelLine()
    {
        await using var rig = await RigWithResultAsync(ComposerOutput.Translate);

        rig.H.Vm.SourceText = "先检查缓存。再看日志。";
        rig.Time.Advance(WebBridge.Frame);

        Assert.Equal(JsonValueKind.Null, Footer(rig.Last).GetProperty("model").ValueKind);
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
    public async Task ADragOfThePage_ReachesTheWindow()
    {
        await using var rig = new Rig();
        var drags = 0;
        rig.Session.Dragged += () => drags++;

        rig.Host.FromPage(new DragIntent { V = 1 });

        Assert.Equal(1, drags);
    }

    [Fact]
    public async Task ThePanelLimit_IsSentToThePage()
    {
        await using var rig = new Rig();
        rig.Session.Start();
        rig.Host.Posted.Clear();

        rig.Session.LimitPanelHeight(428);

        var limit = rig.Host.Posted.Select(json => JsonDocument.Parse(json).RootElement).Single(m => m.GetProperty("type").GetString() == "limit");
        Assert.Equal(428, limit.GetProperty("data").GetProperty("maxHeight").GetInt32());
    }

    [Fact]
    public async Task TheFixedHeight_IsSentToThePage_AndAgainWhenAPageStartsLater()
    {
        await using var rig = new Rig();
        rig.Session.Start();
        rig.Host.Posted.Clear();

        rig.Session.SetFixedHeight(true);

        var frame = rig.Host.Posted.Select(json => JsonDocument.Parse(json).RootElement).Single(m => m.GetProperty("type").GetString() == "frame");
        Assert.True(frame.GetProperty("data").GetProperty("fixedHeight").GetBoolean());
    }

    [Fact]
    public async Task AResizeAndAZoomOfThePage_ReachTheWindow()
    {
        await using var rig = new Rig();
        ResizeIntent? resized = null;
        var zooms = 0;
        rig.Session.Resized += resize => resized = resize;
        rig.Session.Zoomed += () => zooms++;

        rig.Host.FromPage(new ResizeIntent(null, 500) { V = 1 });
        rig.Host.FromPage(new ZoomIntent { V = 1 });

        Assert.Equal(500, resized?.Height);
        Assert.Equal(1, zooms);
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
