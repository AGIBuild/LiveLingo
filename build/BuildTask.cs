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
using Nuke.Common.Tooling;
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
    readonly string Version = "1.4.2";

    [Parameter("Minimum line coverage percentage")]
    readonly int CoverageThreshold = 80;

    [Parameter("Minimum branch coverage percentage")]
    readonly int BranchThreshold = 65;

    [Parameter("Minimum mutation score percentage")]
    readonly int MutationThreshold = 80;

    [Parameter("Model storage directory used by end-to-end probes (defaults to the app's model directory)")]
    readonly string ProbeModelPath = string.Empty;

    [Parameter("Directory with scenarios.json and audio/<id>.wav used by ProbeVoice (see poc/voice-dictation/corpus)")]
    readonly string ProbeCorpus = string.Empty;

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
                    var packageName = cls.Ancestors("package").FirstOrDefault()?.Attribute("name")?.Value;
                    var name = cls.Attribute("name")?.Value;
                    if (name is null) continue;
                    if (!IsCoverageClassIncluded(packageName ?? string.Empty, name)) continue;

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
        });

    Target Mutate => _ => _
        .DependsOn(Build)
        .Executes(() =>
        {
            var score = RunMutationTesting();
            Log.Information("Mutation score:  {Score:F1}% (threshold {Threshold}%)", score, MutationThreshold);
        });

    // macOS frameworks occasionally spam NSLog to stderr during input-method
    // bring-up. These lines are benign but Nuke otherwise reclassifies them
    // as build errors and dumps them under "Errors & Warnings" at the end of
    // the Run target. We recognise and demote them to informational logs so
    // the real errors stay actionable.
    private static readonly Regex[] BenignAppStderrPatterns =
    [
        new(@"IMKCFRunLoopWakeUp", RegexOptions.Compiled),
        new(@"error messaging the mach port", RegexOptions.Compiled),
    ];

    private static void RunProcessLogger(OutputType type, string output)
    {
        if (type == OutputType.Err &&
            BenignAppStderrPatterns.Any(p => p.IsMatch(output)))
        {
            Log.Debug("App stderr (benign): {Line}", output);
            return;
        }

        if (type == OutputType.Err)
            Log.Error(output);
        else
            Log.Information(output);
    }

    Target Run => _ => _
        .DependsOn(Build)
        .Executes(() =>
        {
            DotNetRun(_ => _
                .SetProjectFile(AppProject)
                .SetConfiguration(Configuration.Debug)
                .EnableNoBuild()
                .SetProcessLogger(RunProcessLogger));
        });

    // End-to-end probe: real download (SHA256-verified), real llama-server, real translation. Not part of the unit-test gate.
    Target ProbeTranslation => _ => _
        .DependsOn(Build)
        .Executes(() =>
        {
            var env = new Dictionary<string, string>
            {
                ["LIVELINGO_ENABLE_TRANSLATION_PROBE"] = "1",
                ["LIVELINGO_PROBE_MODEL_PATH"] = ProbeModelPath
            }.AsReadOnly();

            DotNet(
                $"test \"{TestsDir / "LiveLingo.Core.Tests" / "LiveLingo.Core.Tests.csproj"}\" " +
                $"--configuration {Configuration} " +
                $"--no-build " +
                $"--filter \"FullyQualifiedName~TranslationProbeTests\" " +
                $"--logger \"console;verbosity=detailed\" " +
                $"--nologo",
                environmentVariables: env);
        });

    // End-to-end probe of the voice chain: speech models, dictation session and cleanup on recorded audio.
    Target ProbeVoice => _ => _
        .DependsOn(Build)
        .Requires(() => ProbeCorpus)
        .Executes(() =>
        {
            var env = new Dictionary<string, string>
            {
                ["LIVELINGO_ENABLE_VOICE_PROBE"] = "1",
                ["LIVELINGO_PROBE_CORPUS"] = ProbeCorpus,
                ["LIVELINGO_PROBE_MODEL_PATH"] = ProbeModelPath
            }.AsReadOnly();

            DotNet(
                $"test \"{TestsDir / "LiveLingo.Core.Tests" / "LiveLingo.Core.Tests.csproj"}\" " +
                $"--configuration {Configuration} " +
                $"--no-build " +
                $"--filter \"FullyQualifiedName~VoiceProbeTests\" " +
                $"--logger \"console;verbosity=detailed\" " +
                $"--nologo",
                environmentVariables: env);
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
            $"--reporter progress --reporter json",
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

    // One Velopack package per platform: the installer, the full package, and the feed (releases.<channel>.json) that
    // installed applications read to update themselves. The feed and the package must be released together.
    Target Pack => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            DotNetToolRestore();

            var windows = Runtime.StartsWith("win");
            var mainExe = windows ? "LiveLingo.Desktop.exe" : "LiveLingo.Desktop";
            var icon = windows
                ? SourceDir / "LiveLingo.Desktop" / "Assets" / "app-icon.ico"
                : RootDirectory / "build" / "macos" / "app-icon.icns";

            var tempDir = RootDirectory / ".nuke" / "temp" / "vpk";
            tempDir.CreateDirectory();

            var arguments =
                $"vpk pack " +
                $"--packId LiveLingo " +
                $"--packTitle LiveLingo " +
                $"--packAuthors AGIBuild " +
                $"--packVersion {Version} " +
                $"--packDir {PublishDir / Runtime} " +
                $"--mainExe {mainExe} " +
                $"--icon {icon} " +
                $"--outputDir {ReleasesDir} " +
                $"--noPortable";

            if (!windows)
            {
                // The bundle version has no pre-release suffix; the update feed carries the full semantic version.
                var bundleVersion = Version.Split('-')[0];
                var plist = tempDir / "Info.plist";
                File.WriteAllText(plist, File.ReadAllText(RootDirectory / "build" / "macos" / "Info.plist").Replace("__VERSION__", bundleVersion));
                arguments += $" --plist {plist}";
            }

            // Replaces the process environment, so the rest of it (PATH: vpk calls cp, codesign) is carried over.
            var vpkEnv = EnvironmentInfo.Variables
                .ToDictionary(variable => variable.Key, variable => variable.Value);
            vpkEnv["DOTNET_ROLL_FORWARD"] = "LatestMajor";
            vpkEnv["TEMP"] = tempDir;
            vpkEnv["TMP"] = tempDir;

            DotNet(arguments, environmentVariables: vpkEnv.AsReadOnly());

            // The installer keeps the file name of earlier releases.
            var installer = ReleasesDir.GlobFiles(windows ? "*Setup.exe" : "*Setup.pkg").Single();
            var renamed = ReleasesDir / (windows
                ? $"LiveLingo-{Version}-{Runtime}-Setup.exe"
                : $"LiveLingo-{Version}-{Runtime}.pkg");
            File.Move(installer, renamed, overwrite: true);
            Log.Information("Installer: {Path}", renamed);
        });

    Target PackMsi => _ => _
        .DependsOn(Publish)
        .Requires(() => Runtime.StartsWith("win"))
        .Executes(() =>
        {
            DotNetToolRestore();

            ReleasesDir.CreateDirectory();

            var wxsFile = RootDirectory / "build" / "windows" / "LiveLingo.wxs";
            var msiOutput = ReleasesDir / $"LiveLingo-{Version}-{Runtime}.msi";

            DotNet(
                $"wix build \"{wxsFile}\" " +
                $"-o \"{msiOutput}\" " +
                $"-d Version={Version} " +
                $"-d PublishDir={PublishDir / Runtime}");

            Log.Information("Windows MSI created: {Path}", msiOutput);
        });

    static bool IsCoverageClassIncluded(string packageName, string className)
    {
        if (packageName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            return false;

        if (className.StartsWith("LiveLingo.App.Tests.", StringComparison.OrdinalIgnoreCase) ||
            className.Contains(".Tests.", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
