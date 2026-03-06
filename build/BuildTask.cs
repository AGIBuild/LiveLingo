using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class BuildTask : NukeBuild
{
    public static int Main() => Execute<BuildTask>(x => x.Build);

    [Solution("LiveLingo.slnx")]
    readonly Solution Solution;

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Target runtime identifier (win-x64, osx-arm64)")]
    readonly string Runtime = "win-x64";

    [Parameter("Package version in semver2 format")]
    readonly string Version = "0.1.0";

    AbsolutePath SourceDir => RootDirectory / "src";
    AbsolutePath TestsDir => RootDirectory / "tests";
    AbsolutePath PublishDir => RootDirectory / "publish";
    AbsolutePath ReleasesDir => RootDirectory / "releases";
    AbsolutePath RunSettingsFile => RootDirectory / "test.runsettings";

    AbsolutePath AppProject => SourceDir / "LiveLingo.App" / "LiveLingo.App.csproj";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDir.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
            TestsDir.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
            PublishDir.DeleteDirectory();
            ReleasesDir.DeleteDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(_ => _
                .SetProjectFile(Solution));
        });

    Target Build => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .SetNoLogo(true));
        });

    Target Test => _ => _
        .DependsOn(Build)
        .Executes(() =>
        {
            DotNetTest(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .SetSettingsFile(RunSettingsFile)
                .SetDataCollector("XPlat Code Coverage")
                .SetNoLogo(true));
        });

    Target Run => _ => _
        .DependsOn(Build)
        .Executes(() =>
        {
            DotNetRun(_ => _
                .SetProjectFile(AppProject)
                .SetConfiguration(Configuration.Debug)
                .EnableNoBuild());
        });

    Target Publish => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            DotNetPublish(_ => _
                .SetProject(AppProject)
                .SetConfiguration(Configuration.Release)
                .SetRuntime(Runtime)
                .SetSelfContained(true)
                .SetOutput(PublishDir / Runtime)
                .SetNoLogo(true));
        });

    Target Pack => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            DotNetToolRestore();

            var mainExe = Runtime.StartsWith("win") ? "LiveLingo.App.exe" : "LiveLingo.App";

            var tempDir = RootDirectory / ".nuke" / "temp" / "vpk";
            tempDir.CreateDirectory();

            var vpkEnv = new Dictionary<string, string>
            {
                ["DOTNET_ROLL_FORWARD"] = "LatestMajor",
                ["TEMP"] = tempDir,
                ["TMP"] = tempDir
            }.AsReadOnly();

            DotNet(
                $"vpk pack " +
                $"--packId LiveLingo " +
                $"--packVersion {Version} " +
                $"--packDir {PublishDir / Runtime} " +
                $"--mainExe {mainExe} " +
                $"--outputDir {ReleasesDir}",
                environmentVariables: vpkEnv);
        });
}
