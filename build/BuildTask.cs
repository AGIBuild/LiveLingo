using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Serilog;
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

    [Parameter("Minimum line coverage percentage")]
    readonly int CoverageThreshold = 96;

    [Parameter("Minimum branch coverage percentage")]
    readonly int BranchThreshold = 92;

    [Parameter("Minimum mutation score percentage")]
    readonly int MutationThreshold = 80;

    AbsolutePath SourceDir => RootDirectory / "src";
    AbsolutePath TestsDir => RootDirectory / "tests";
    AbsolutePath PublishDir => RootDirectory / "publish";
    AbsolutePath ReleasesDir => RootDirectory / "releases";
    AbsolutePath RunSettingsFile => RootDirectory / "test.runsettings";

    AbsolutePath AppProject => SourceDir / "LiveLingo.Desktop" / "LiveLingo.Desktop.csproj";

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
            TestsDir.GlobDirectories("**/TestResults").DeleteDirectories();

            DotNetTest(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .SetSettingsFile(RunSettingsFile)
                .SetDataCollector("XPlat Code Coverage")
                .SetNoLogo(true));

            var reports = TestsDir.GlobFiles("**/TestResults/**/coverage.cobertura.xml");
            Assert.NotEmpty(reports, "No coverage reports found");

            var lineCoverage = new Dictionary<string, (int Valid, int Covered)>();
            var branchCoverage = new Dictionary<string, (int Valid, int Covered)>();

            foreach (var report in reports)
            {
                var doc = XDocument.Load(report);
                foreach (var cls in doc.Descendants("class"))
                {
                    var name = cls.Attribute("name")?.Value;
                    if (name is null) continue;

                    var lines = cls.Descendants("line").ToList();
                    if (lines.Count == 0) continue;

                    var linesValid = lines.Count;
                    var linesCovered = lines.Count(l => int.Parse(l.Attribute("hits")!.Value) > 0);

                    if (lineCoverage.TryGetValue(name, out var existLine))
                        lineCoverage[name] = (existLine.Valid, Math.Max(existLine.Covered, linesCovered));
                    else
                        lineCoverage[name] = (linesValid, linesCovered);

                    var branchLines = lines.Where(l => l.Attribute("branch")?.Value == "True").ToList();
                    int bValid = 0, bCovered = 0;
                    foreach (var bl in branchLines)
                    {
                        var cc = bl.Attribute("condition-coverage")?.Value;
                        if (cc is null) continue;
                        var m = Regex.Match(cc, @"\((\d+)/(\d+)\)");
                        if (!m.Success) continue;
                        bCovered += int.Parse(m.Groups[1].Value);
                        bValid += int.Parse(m.Groups[2].Value);
                    }

                    if (bValid > 0)
                    {
                        if (branchCoverage.TryGetValue(name, out var existBranch))
                            branchCoverage[name] = (existBranch.Valid, Math.Max(existBranch.Covered, bCovered));
                        else
                            branchCoverage[name] = (bValid, bCovered);
                    }
                }
            }

            var totalLines = lineCoverage.Values.Sum(c => c.Valid);
            var coveredLines = lineCoverage.Values.Sum(c => c.Covered);
            var linePct = totalLines > 0 ? coveredLines * 100.0 / totalLines : 0;

            var totalBranches = branchCoverage.Values.Sum(c => c.Valid);
            var coveredBranches = branchCoverage.Values.Sum(c => c.Covered);
            var branchPct = totalBranches > 0 ? coveredBranches * 100.0 / totalBranches : 0;

            Log.Information("Line coverage:   {Pct:F1}% ({Covered}/{Total})", linePct, coveredLines, totalLines);
            Log.Information("Branch coverage: {Pct:F1}% ({Covered}/{Total})", branchPct, coveredBranches, totalBranches);

            Assert.True(linePct >= CoverageThreshold,
                $"Line coverage {linePct:F1}% is below threshold {CoverageThreshold}%");
            Assert.True(branchPct >= BranchThreshold,
                $"Branch coverage {branchPct:F1}% is below threshold {BranchThreshold}%");

            var mutationScore = RunMutationTesting();
            Log.Information("Mutation score:  {Score:F1}% (threshold {Threshold}%)", mutationScore, MutationThreshold);

            Assert.True(mutationScore >= MutationThreshold,
                $"Mutation score {mutationScore:F1}% is below threshold {MutationThreshold}%");
        });

    Target Mutate => _ => _
        .DependsOn(Build)
        .Executes(() =>
        {
            var score = RunMutationTesting();
            Log.Information("Mutation score:  {Score:F1}% (threshold {Threshold}%)", score, MutationThreshold);
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

    // App project skipped: Stryker cannot instrument Avalonia projects (source generator incompatibility)
    double RunMutationTesting()
    {
        DotNetToolRestore();

        var coreTestDir = TestsDir / "LiveLingo.Core.Tests";

        DotNet(
            $"stryker " +
            $"--project LiveLingo.Core.csproj " +
            $"--break-at {MutationThreshold} " +
            $"--reporter progress --reporter json " +
            $"--mutate \"!**/Engines/MarianOnnxEngine*\" " +
            $"--mutate \"!**/Processing/QwenModelHost.cs\" " +
            $"--mutate \"!**/Processing/QwenTextProcessor.cs\" " +
            $"--mutate \"!**/Processing/SummarizeProcessor.cs\" " +
            $"--mutate \"!**/Processing/OptimizeProcessor.cs\" " +
            $"--mutate \"!**/Processing/ColloquializeProcessor.cs\" " +
            $"--mutate \"!**/ServiceCollectionExtensions.cs\"",
            workingDirectory: coreTestDir);

        var reportFile = coreTestDir.GlobFiles("**/StrykerOutput/**/mutation-report.json")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .FirstOrDefault();

        if (reportFile is null) return 0;

        using var json = JsonDocument.Parse(File.ReadAllText(reportFile));
        int killed = 0, survived = 0, timeout = 0, noCoverage = 0;

        foreach (var file in json.RootElement.GetProperty("files").EnumerateObject())
        foreach (var mutant in file.Value.GetProperty("mutants").EnumerateArray())
        {
            switch (mutant.GetProperty("status").GetString())
            {
                case "Killed": killed++; break;
                case "Survived": survived++; break;
                case "Timeout": timeout++; break;
                case "NoCoverage": noCoverage++; break;
            }
        }

        var detected = killed + timeout;
        var total = detected + survived + noCoverage;
        return total > 0 ? detected * 100.0 / total : 0;
    }

    Target Pack => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            DotNetToolRestore();

            var mainExe = Runtime.StartsWith("win") ? "LiveLingo.Desktop.exe" : "LiveLingo.Desktop";

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

    Target PackMac => _ => _
        .DependsOn(Publish)
        .Requires(() => Runtime.StartsWith("osx"))
        .Executes(() =>
        {
            var macosDir = RootDirectory / "build" / "macos";
            var appBundle = PublishDir / "LiveLingo.app";
            var contentsDir = appBundle / "Contents";
            var macOsDir = contentsDir / "MacOS";
            var resourcesDir = contentsDir / "Resources";

            appBundle.DeleteDirectory();
            macOsDir.CreateDirectory();
            resourcesDir.CreateDirectory();

            var infoPlist = File.ReadAllText(macosDir / "Info.plist");
            infoPlist = infoPlist.Replace("__VERSION__", Version);
            File.WriteAllText(contentsDir / "Info.plist", infoPlist);

            File.Copy(macosDir / "entitlements.plist", contentsDir / "entitlements.plist", overwrite: true);

            var publishedFiles = PublishDir / Runtime;
            foreach (var file in Directory.GetFiles(publishedFiles, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(publishedFiles, file);
                var dest = macOsDir / relative;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }

            var mainExe = macOsDir / "LiveLingo.Desktop";
            if (File.Exists(mainExe))
                Chmod(mainExe, "755");

            var svgIcon = RootDirectory / "src" / "LiveLingo.Desktop" / "Assets" / "app-icon.svg";
            if (File.Exists(svgIcon))
                File.Copy(svgIcon, resourcesDir / "app-icon.svg", overwrite: true);

            ReleasesDir.CreateDirectory();

            var componentPkg = PublishDir / "LiveLingo-component.pkg";
            RunProcess("pkgbuild",
                $"--root \"{appBundle}\" " +
                $"--identifier com.livelingo.app " +
                $"--version {Version} " +
                $"--install-location /Applications/LiveLingo.app " +
                $"\"{componentPkg}\"");

            var finalPkg = ReleasesDir / $"LiveLingo-{Version}-{Runtime}.pkg";
            RunProcess("productbuild",
                $"--package \"{componentPkg}\" " +
                $"\"{finalPkg}\"");

            componentPkg.DeleteFile();
            Log.Information("macOS PKG created: {Path}", finalPkg);
        });

    static void Chmod(string path, string mode)
    {
        RunProcess("chmod", $"{mode} \"{path}\"");
    }

    static void RunProcess(string tool, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(tool, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var stderr = proc.StandardError.ReadToEnd();
            throw new Exception($"{tool} failed (exit {proc.ExitCode}): {stderr}");
        }
    }
}
