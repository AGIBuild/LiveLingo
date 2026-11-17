using System.Text.Json;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Tests.Settings;
using LiveLingo.Desktop.Web;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class SettingsWebSessionTests
{
    private sealed class Rig : IDisposable
    {
        public SettingsViewModelTests.Fixture F { get; }
        public FakeHost Host { get; } = new();
        public FakeClock Time { get; } = new();
        public SettingsWebSession Session { get; }

        public Rig(SettingsModel? model = null, string? storedKey = null, string section = "general", UpdateChecks? updates = null)
        {
            F = new SettingsViewModelTests.Fixture(model, storedKey);
            Session = new SettingsWebSession(F.Vm, Texts.ChineseSettings(), section, SettingsScenarios.App, updates, _ => { }, Host, new InlineContext(), Time, new ListLogger());
            Host.FromPage("""{"v":1,"name":"ready"}""");
        }

        public JsonElement Last => JsonDocument.Parse(Host.Posted[^1]).RootElement;

        /// <summary>
        /// Waits until the page was told about the devices. The view model sets the list before it announces the change,
        /// so the clock is advanced until the snapshot shows it rather than trusting the property alone.
        /// </summary>
        public Task DevicesRead() => Wait.Until(() =>
        {
            Time.Advance(WebBridge.Frame);
            return Last.GetProperty("data").GetProperty("voice").GetProperty("microphones").GetArrayLength() == 3;
        });

        public string? Section => Last.GetProperty("data").GetProperty("section").GetString();

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void ThePageThatAnnouncesItself_GetsTheStringsAndTheSnapshot()
    {
        using var rig = new Rig(section: "voice");

        Assert.Equal(["strings", "settings"], rig.Host.Types.Take(2));
        var strings = JsonDocument.Parse(rig.Host.Posted[0]).RootElement.GetProperty("data");
        Assert.Equal("zh-CN", strings.GetProperty("locale").GetString());
        Assert.Equal("设置", strings.GetProperty("strings").GetProperty("title").GetString());
        Assert.Equal("voice", JsonDocument.Parse(rig.Host.Posted[1]).RootElement.GetProperty("data").GetProperty("section").GetString());
    }

    [Fact]
    public async Task WhatTheSystemProvides_ReachesThePageOnceItIsRead()
    {
        using var rig = new Rig();
        await rig.DevicesRead();

        rig.Time.Advance(WebBridge.Frame);

        var microphones = rig.Last.GetProperty("data").GetProperty("voice").GetProperty("microphones");
        Assert.Equal(["", "USB Mic", "Built-in"], microphones.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ThePageActs_AndTheNextSnapshotShowsTheResult()
    {
        using var rig = new Rig();
        await rig.DevicesRead();
        rig.Host.Posted.Clear();

        rig.Host.FromPage("""{"v":1,"name":"setting","key":"theme","value":"Dark"}""");
        rig.Time.Advance(WebBridge.Frame);

        Assert.Equal(["settings"], rig.Host.Types);
        Assert.Equal("Dark", rig.Last.GetProperty("data").GetProperty("options").GetProperty("theme").GetString());
        Assert.Equal("Dark", rig.F.Last.Appearance.Theme);
    }

    [Fact]
    public async Task ChangesInAFrame_ReachThePageAsOneSnapshot()
    {
        using var rig = new Rig();
        await rig.DevicesRead();
        rig.Host.Posted.Clear();

        rig.Host.FromPage("""{"v":1,"name":"setting","key":"hotkey","value":"Ctrl+Alt+K"}""");
        rig.Host.FromPage("""{"v":1,"name":"setting","key":"reduceMotion","value":"true"}""");
        rig.Time.Advance(WebBridge.Frame);

        Assert.Equal(["settings"], rig.Host.Types);
    }

    [Fact]
    public async Task TheConsentQuestion_TravelsInTheSnapshot()
    {
        using var rig = new Rig(CloudTestData.WithEndpoints(), storedKey: "sk", section: "models");
        await Wait.Until(() => rig.F.Vm.Endpoints[0].HasKey);
        rig.Host.Posted.Clear();

        rig.Host.FromPage("""{"v":1,"name":"selectCloudModel","step":"translate","endpointId":"main"}""");
        rig.Time.Advance(WebBridge.Frame);
        var consent = rig.Last.GetProperty("data").GetProperty("models").GetProperty("consent");
        Assert.Equal("translate", consent.GetProperty("step").GetString());
        Assert.Equal("main", consent.GetProperty("endpointId").GetString());

        rig.Host.FromPage("""{"v":1,"name":"consent","allow":true}""");
        rig.Time.Advance(WebBridge.Frame);
        var models = rig.Last.GetProperty("data").GetProperty("models");
        Assert.Equal(JsonValueKind.Null, models.GetProperty("consent").ValueKind);
        Assert.True(models.GetProperty("hardware").GetProperty("anyCloud").GetBoolean());
    }

    [Fact]
    public async Task TheUpdateProgress_TravelsInTheSnapshots()
    {
        var gate = new TaskCompletionSource<string?>();
        var service = Substitute.For<IUpdateService>();
        service.CanUpdate.Returns(true);
        service.ReadyVersion.Returns((string?)null);
        service.FindAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);
        using var checks = new UpdateChecks(service, TimeSpan.FromHours(4), NullLogger.Instance);
        using var rig = new Rig(section: "about", updates: checks);
        await rig.DevicesRead();
        rig.Host.Posted.Clear();

        rig.Host.FromPage("""{"v":1,"name":"checkUpdates"}""");
        await Wait.Until(() => checks.State.Phase == UpdatePhase.Checking);
        rig.Time.Advance(WebBridge.Frame);
        var checking = rig.Last.GetProperty("data").GetProperty("about").GetProperty("update");
        Assert.True(checking.GetProperty("busy").GetBoolean());
        Assert.Equal("正在检查更新…", checking.GetProperty("status").GetString());

        gate.SetResult(null);
        await Wait.Until(() => checks.State.Phase == UpdatePhase.UpToDate);
        rig.Time.Advance(WebBridge.Frame);
        var latest = rig.Last.GetProperty("data").GetProperty("about").GetProperty("update");
        Assert.False(latest.GetProperty("busy").GetBoolean());
        Assert.True(latest.GetProperty("canCheck").GetBoolean());
        Assert.StartsWith("已是最新版本（2.0.0）", latest.GetProperty("status").GetString());
    }

    [Fact]
    public void WithUpdatesOff_ThePageSaysSo()
    {
        using var rig = new Rig(section: "about");

        var update = rig.Last.GetProperty("data").GetProperty("about").GetProperty("update");

        Assert.Contains("已关闭自动更新", update.GetProperty("status").GetString());
        Assert.False(update.GetProperty("canCheck").GetBoolean());
    }

    [Fact]
    public void AskingForAnotherGroup_ShowsItWithoutTheUserClicking()
    {
        using var rig = new Rig();
        rig.Host.Posted.Clear();

        rig.Session.ShowSection("models");
        rig.Time.Advance(WebBridge.Frame);

        Assert.Equal("models", rig.Section);
    }

    [Fact]
    public void TheNavigation_IsAnIntentToo()
    {
        using var rig = new Rig();
        rig.Host.Posted.Clear();

        rig.Host.FromPage("""{"v":1,"name":"openSettings","section":"models"}""");
        rig.Time.Advance(WebBridge.Frame);

        Assert.Equal("models", rig.Section);
    }

    [Fact]
    public void AfterDispose_NothingReachesThePageAnymore()
    {
        using var rig = new Rig();
        rig.Session.Dispose();
        rig.Host.Posted.Clear();

        rig.F.Vm.ReduceMotion = true;
        rig.Time.Advance(WebBridge.Frame);

        Assert.Empty(rig.Host.Posted);
    }
}
