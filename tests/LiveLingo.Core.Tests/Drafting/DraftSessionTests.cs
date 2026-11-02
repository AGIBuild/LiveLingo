using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Tests.Drafting;

public class DraftSessionTests
{
    private sealed class QueueContext : SynchronizationContext
    {
        public Queue<(SendOrPostCallback Callback, object? State)> Pending { get; } = new();

        public override void Post(SendOrPostCallback d, object? state) => Pending.Enqueue((d, state));

        public void RunAll()
        {
            while (Pending.TryDequeue(out var item))
                item.Callback(item.State);
        }
    }

    [Fact]
    public void Mutate_AppliesTheChange_AndNotifiesObservers()
    {
        var session = new DraftSession(hasTarget: true);
        var changes = 0;
        session.Changed += () => changes++;

        var ticket = session.Mutate(draft => draft.Append("好的。"));

        Assert.Equal(1, changes);
        Assert.Equal("好的。", session.Read(draft => draft.Units[0].Raw));
        Assert.True(session.Read(draft => draft.IsCurrent(ticket, UnitKind.Clean)));
    }

    [Fact]
    public void Read_DoesNotNotify()
    {
        var session = new DraftSession(hasTarget: true);
        var changes = 0;
        session.Changed += () => changes++;

        session.Read(draft => draft.Units.Count);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Notifications_AreDeliveredOnTheCreatingContext()
    {
        var context = new QueueContext();
        var session = new DraftSession(hasTarget: true, notifyOn: context);

        var changes = 0;
        session.Changed += () => changes++;
        session.Mutate(draft => draft.Append("好的。"));

        Assert.Equal(0, changes);
        context.RunAll();
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ConcurrentMutations_AreSerialized()
    {
        var session = new DraftSession(hasTarget: true);

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            Task.Run(() => session.Mutate(draft => draft.Append($"句子{i}。")))));

        var raws = session.Read(draft => draft.Units.Select(unit => unit.Raw).ToList());
        Assert.Equal(64, raws.Distinct().Count());
        Assert.Equal(64, session.Read(draft => draft.Units.Select(unit => unit.Id).Distinct().Count()));
    }
}
