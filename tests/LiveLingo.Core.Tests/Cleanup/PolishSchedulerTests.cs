using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Tests.Cleanup;

public class PolishSchedulerTests
{
    private sealed class FakeCleaner(Func<CleanupRequest, string> answer) : ITextCleaner
    {
        public List<CleanupRequest> Requests { get; } = [];

        public Task<string> CleanAsync(CleanupRequest request, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static readonly (string Raw, string Clean)[] Steps =
    [
        ("呃，先改接口。", "先改接口。"),
        ("嗯，然后补测试。", "补测试。"),
        ("那个，最后更新文档。", "最后更新文档。"),
    ];

    private static string Strip(string text) =>
        text.Replace("呃，", "").Replace("嗯，", "").Replace("那个，", "").Replace("然后", "");

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }

    private static DraftSession Dictated(IEnumerable<(string Raw, string Clean)> passages, bool recording = true)
    {
        var session = new DraftSession(hasTarget: true);
        session.Mutate(draft =>
        {
            draft.Output = OutputKind.Clean;
            draft.Recording = recording;
            foreach (var (raw, clean) in passages)
                draft.Complete(draft.Append(raw), UnitKind.Clean, clean);
        });
        return session;
    }

    private static string[] Raws(DraftSession session) => session.Read(d => d.Units.Select(u => u.Raw).ToArray());

    [Fact]
    public async Task WhenRecordingStops_NeighbouringPassagesArePolishedAsOneText()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], contextChars: 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 1);

        Assert.Equal(["呃，先改接口。嗯，然后补测试。那个，最后更新文档。"], Raws(session));
        Assert.Equal("先改接口。补测试。最后更新文档。", session.Read(d => d.Units[0].Clean.Text));
        Assert.False(cleaner.Requests.Single().Continues);
    }

    [Fact]
    public async Task NothingIsPolished_WhileRecording()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], 300);

        await Task.Delay(100);

        Assert.Empty(cleaner.Requests);
        Assert.Equal(3, Raws(session).Length);
    }

    [Fact]
    public async Task ADraftThatWasNeverRecorded_IsNotPolished()
    {
        var session = Dictated(Steps, recording: false);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], 300);

        await Task.Delay(100);

        Assert.Empty(cleaner.Requests);
    }

    [Fact]
    public async Task ARefusedPolish_KeepsThePassages_AndIsNotTriedAgain()
    {
        var session = Dictated([("端口改成 8080。", "端口改成 8080。"), ("超时设成三十秒。", "超时设成三十秒。")]);
        var cleaner = new FakeCleaner(_ => "端口改一下。");
        await using var polish = new PolishScheduler(session, cleaner, [], 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => cleaner.Requests.Count == 1);
        await Task.Delay(100);
        session.Mutate(d => d.Output = OutputKind.Clean);
        await Task.Delay(100);

        Assert.Equal(2, Raws(session).Length);
        Assert.Single(cleaner.Requests);
    }

    [Fact]
    public async Task AFailingCleaner_ChangesNothing()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(_ => throw new HttpRequestException("offline"));
        await using var polish = new PolishScheduler(session, cleaner, [], 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => cleaner.Requests.Count == 1);
        await Task.Delay(50);

        Assert.Equal(3, Raws(session).Length);
    }

    [Fact]
    public async Task AHandEditedPassage_EndsARun()
    {
        var session = Dictated([.. Steps, ("呃，再发布。", "再发布。")]);
        session.Mutate(d => d.Edit(2, UnitKind.Clean, "我改过的。"));
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 3);

        Assert.Equal(["呃，先改接口。嗯，然后补测试。", "那个，最后更新文档。", "呃，再发布。"], Raws(session));
        Assert.Equal("我改过的。", session.Read(d => d.Units[1].Clean.Text));
    }

    [Fact]
    public async Task LongDictation_IsPolishedInBoundedGroups_AndLaterGroupsReadTheEarlierPolish()
    {
        var body = new string('字', 150);
        var session = Dictated(Enumerable.Range(0, 4).Select(i => ($"呃，{body}{i}。", $"{body}{i}。")));
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 2);

        Assert.Equal(2, cleaner.Requests.Count);
        Assert.All(cleaner.Requests, request => Assert.True(request.Target.Length <= PolishScheduler.MaxGroupChars));
        Assert.StartsWith($"{body}0。", cleaner.Requests[1].Preceding);
        Assert.Contains("1。", cleaner.Requests[1].Preceding);
    }

    [Fact]
    public async Task APolishedPassage_IsNotPolishedAgainWhenTheUserRecordsMore()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], 300);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 1);

        session.Mutate(d => d.Recording = true);
        session.Mutate(d => d.Complete(d.Append("呃，再发布。"), UnitKind.Clean, "再发布。"));
        session.Mutate(d => d.Recording = false);
        await Task.Delay(150);

        Assert.Single(cleaner.Requests);
        Assert.Equal(2, Raws(session).Length);
    }

    [Fact]
    public async Task AFrozenDraft_IsNeverChanged()
    {
        var session = Dictated(Steps);
        var release = new ManualResetEventSlim();
        var cleaner = new FakeCleaner(request =>
        {
            release.Wait(5000);
            return Strip(request.Target);
        });
        await using var polish = new PolishScheduler(session, cleaner, [], 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => cleaner.Requests.Count == 1);
        session.Mutate(d => d.RequestCommit());
        release.Set();
        await Task.Delay(100);

        Assert.Equal(3, Raws(session).Length);
    }

    [Fact]
    public async Task Dispose_StopsThePolish()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        var polish = new PolishScheduler(session, cleaner, [], 300);

        await polish.DisposeAsync();
        session.Mutate(d => d.Recording = false);
        await Task.Delay(100);

        Assert.Empty(cleaner.Requests);
    }
}
