using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Tests.Services.Configuration;

public sealed class UserDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "livelingo-migration-" + Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(_root, "LiveLingo");
    private string Current => Path.Combine(_root, "AGIBuild", "LiveLingo");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void LegacyFile(string relative, string text)
    {
        var path = Path.Combine(Legacy, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void TheSettingsAndTheModels_MoveWithoutCopying_AndTheInstallersFilesStay()
    {
        LegacyFile("settings.json", "{}");
        LegacyFile("preferences.json", "[]");
        LegacyFile(Path.Combine("models", "m1", "model.gguf"), "weights");
        LegacyFile(Path.Combine("current", "LiveLingo.Desktop.dll"), "app");
        LegacyFile("Update.exe", "updater");

        var moves = UserDataMigration.Run(Legacy, Current);

        Assert.Equal(["settings.json", "preferences.json", "models"], moves.Select(move => move.Name));
        Assert.All(moves, move => Assert.Null(move.Error));
        Assert.Equal("weights", File.ReadAllText(Path.Combine(Current, "models", "m1", "model.gguf")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(Current, "settings.json")));
        Assert.False(File.Exists(Path.Combine(Legacy, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(Legacy, "models")));
        Assert.True(File.Exists(Path.Combine(Legacy, "current", "LiveLingo.Desktop.dll")));
        Assert.True(File.Exists(Path.Combine(Legacy, "Update.exe")));
    }

    [Fact]
    public void WhereThereIsNothingToMove_NothingHappens()
    {
        Assert.Empty(UserDataMigration.Run(Legacy, Current));
        Assert.False(Directory.Exists(Current));
    }

    [Fact]
    public void ItRunsOnce_TheSecondStartFindsNothing()
    {
        LegacyFile("settings.json", "{}");

        Assert.Single(UserDataMigration.Run(Legacy, Current));
        Assert.Empty(UserDataMigration.Run(Legacy, Current));
    }

    [Fact]
    public void WhatTheDataFolderAlreadyHas_IsNeverReplaced()
    {
        LegacyFile("settings.json", "old");
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "settings.json"), "new");

        Assert.Empty(UserDataMigration.Run(Legacy, Current));

        Assert.Equal("new", File.ReadAllText(Path.Combine(Current, "settings.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Legacy, "settings.json")));
    }

    [Fact]
    public void AnEntryThatCannotBeMoved_IsReported_AndStaysWhereItWas()
    {
        LegacyFile("settings.json", "{}");
        // A file where the data folder should be makes every move fail.
        Directory.CreateDirectory(Path.GetDirectoryName(Current)!);
        File.WriteAllText(Current, "in the way");

        var moves = UserDataMigration.Run(Legacy, Current);

        var move = Assert.Single(moves);
        Assert.Equal("settings.json", move.Name);
        Assert.NotNull(move.Error);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(Legacy, "settings.json")));
    }
}
