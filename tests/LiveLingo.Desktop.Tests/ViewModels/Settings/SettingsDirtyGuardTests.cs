using LiveLingo.Desktop.ViewModels.Settings;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class SettingsDirtyGuardTests
{
    [Fact]
    public void IsLoading_IsFalse_BeforeRunLoading()
    {
        var sut = new SettingsDirtyGuard();

        Assert.False(sut.IsLoading);
    }

    [Fact]
    public void RunLoading_TogglesIsLoading_AroundAction()
    {
        var sut = new SettingsDirtyGuard();
        bool? observedDuringAction = null;

        sut.RunLoading(() => observedDuringAction = sut.IsLoading);

        Assert.True(observedDuringAction);
        Assert.False(sut.IsLoading);
    }

    [Fact]
    public void RunLoading_ResetsIsLoading_OnException()
    {
        var sut = new SettingsDirtyGuard();

        Assert.Throws<InvalidOperationException>(() =>
            sut.RunLoading(() => throw new InvalidOperationException("boom")));

        Assert.False(sut.IsLoading);
    }

    [Fact]
    public void RunLoading_ThrowsOnNullAction()
    {
        var sut = new SettingsDirtyGuard();

        Assert.Throws<ArgumentNullException>(() => sut.RunLoading(null!));
    }
}
