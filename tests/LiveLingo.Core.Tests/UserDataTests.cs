namespace LiveLingo.Core.Tests;

public sealed class UserDataTests
{
    [Fact]
    public void OnMacOS_TheDataIsUnderApplicationSupport() =>
        Assert.Equal(
            Path.Combine("/Users/a", "Library", "Application Support", "LiveLingo"),
            UserData.Resolve(macOS: true, home: "/Users/a", localAppData: "/ignored"));

    [Fact]
    public void OnWindows_TheDataIsNextToTheInstallersFolder_NotInIt()
    {
        var local = Path.Combine("C:", "Users", "a", "AppData", "Local");

        var data = UserData.Resolve(macOS: false, home: "ignored", localAppData: local);

        Assert.Equal(Path.Combine(local, "AGIBuild", "LiveLingo"), data);
        Assert.DoesNotContain(Path.Combine(local, "LiveLingo") + Path.DirectorySeparatorChar, data + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void TheModelsAndTheRuntimeState_LiveInTheDataFolder()
    {
        var options = new CoreOptions();

        Assert.Equal(Path.Combine(UserData.Directory, "models"), options.ModelStoragePath);
        Assert.Equal(Path.Combine(UserData.Directory, "run"), options.RuntimeStatePath);
    }
}
