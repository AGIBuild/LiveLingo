using System.Diagnostics;
using System.Text.Json;
using LiveLingo.Core.LocalHost;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Services.LocalHost;

/// <summary>OS-level environment probe that constructs loopback discovery targets and executes user-initiated host actions.</summary>
public interface ILocalHostEnvironmentProbe
{
    Task<IReadOnlyList<LocalHostProbeTarget>> BuildTargetsAsync(LocalHostSettings settings, ISecretStore secrets, CancellationToken ct);
    bool TryOpenHostApp(LocalHostInstance host);
    Task<bool> TryStartLocalServerAsync(LocalHostInstance host, CancellationToken ct);
}

/// <summary>
/// Discovers locally installed Bionic, LM Studio, Ollama, and common loopback OpenAI-compatible servers on macOS and
/// Windows. Strictly reads public server config files (<c>http-server-config.json</c>) and never targets internal RPC
/// ports (<c>http-server.json</c>) or ephemeral child engine ports (<c>omlx-server</c>).
/// </summary>
public sealed class LocalHostEnvironmentProbe : ILocalHostEnvironmentProbe
{
    public const string BionicHostId = "bionic";
    public const string LmStudioHostId = "lmstudio";
    public const string OllamaHostId = "ollama";
    public const string LlamaAppHostId = "llama-app-9931";
    public const string LlamaServerHostId = "llama-server-8080";
    public const string JanHostId = "jan-1337";
    public const string OpenAi8000HostId = "local-openai-8000";

    private readonly string _homeDir;
    private readonly string _localAppData;

    public LocalHostEnvironmentProbe(string? homeDir = null, string? localAppData = null)
    {
        _homeDir = homeDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public async Task<IReadOnlyList<LocalHostProbeTarget>> BuildTargetsAsync(
        LocalHostSettings settings,
        ISecretStore secrets,
        CancellationToken ct)
    {
        var targets = new List<LocalHostProbeTarget>();
        var lmsPath = FindLmsCliPath();

        // 1. Bionic (first-class default: port 1235 or ~/.lmstudio/apps/bionic/.internal/http-server-config.json)
        var bionicInternal = Path.Combine(_homeDir, ".lmstudio", "apps", "bionic", ".internal");
        var bionicInstall = ReadInstallPath(Path.Combine(bionicInternal, "app-install-location.json"))
            ?? FindAppPath(macAppName: "Bionic.app", winSubPath: Path.Combine("Programs", "Bionic", "Bionic.exe"));
        var bionicPort = ReadConfigPort(Path.Combine(bionicInternal, "http-server-config.json")) ?? 1235;
        var bionicUri = ResolveOverrideOrDefault(settings, BionicHostId, $"http://127.0.0.1:{bionicPort}");
        var bionicToken = await SafeGetSecretAsync(secrets, LocalHostSettings.TokenSecretIdFor(BionicHostId), ct).ConfigureAwait(false);
        targets.Add(new LocalHostProbeTarget(
            BionicHostId,
            LocalHostKind.Bionic,
            "Bionic",
            bionicUri,
            bionicToken,
            IsInstalledHint: bionicInstall is not null || Directory.Exists(bionicInternal),
            InstallPath: bionicInstall,
            CanStartServer: lmsPath is not null,
            CanOpenApp: bionicInstall is not null));

        // 2. LM Studio / llmster (classic port 1234 or ~/.lmstudio/.internal/http-server-config.json)
        var lmStudioInstall = FindAppPath(macAppName: "LM Studio.app", winSubPath: Path.Combine("Programs", "LM Studio", "LM Studio.exe"))
            ?? FindAppPath(macAppName: null, winSubPath: Path.Combine("LM-Studio", "LM Studio.exe"));
        var lmStudioPort =
            ReadConfigPort(Path.Combine(_homeDir, ".lmstudio", "apps", "lmstudio", ".internal", "http-server-config.json"))
            ?? ReadConfigPort(Path.Combine(_homeDir, ".lmstudio", ".internal", "http-server-config.json"))
            ?? ReadConfigPort(Path.Combine(_homeDir, ".cache", "lm-studio", ".internal", "http-server-config.json"))
            ?? 1234;
        var lmStudioUri = ResolveOverrideOrDefault(settings, LmStudioHostId, $"http://127.0.0.1:{lmStudioPort}");
        var lmStudioToken = await SafeGetSecretAsync(secrets, LocalHostSettings.TokenSecretIdFor(LmStudioHostId), ct).ConfigureAwait(false);
        targets.Add(new LocalHostProbeTarget(
            LmStudioHostId,
            LocalHostKind.LmStudio,
            "LM Studio",
            lmStudioUri,
            lmStudioToken,
            IsInstalledHint: lmStudioInstall is not null,
            InstallPath: lmStudioInstall,
            CanStartServer: lmsPath is not null,
            CanOpenApp: lmStudioInstall is not null));

        // 3. Ollama (loopback OLLAMA_HOST or default 11434)
        var ollamaInstall = FindAppPath(macAppName: "Ollama.app", winSubPath: Path.Combine("Programs", "Ollama", "ollama app.exe"));
        var ollamaDefault = ReadLoopbackOllamaHost() ?? "http://127.0.0.1:11434";
        var ollamaUri = ResolveOverrideOrDefault(settings, OllamaHostId, ollamaDefault);
        targets.Add(new LocalHostProbeTarget(
            OllamaHostId,
            LocalHostKind.Ollama,
            "Ollama",
            ollamaUri,
            IsInstalledHint: ollamaInstall is not null || Directory.Exists(Path.Combine(_homeDir, ".ollama")),
            InstallPath: ollamaInstall,
            CanStartServer: false,
            CanOpenApp: ollamaInstall is not null));

        // 4. Well-known fixed OpenAI-compatible local server ports (never scanning ephemeral ports).
        if (settings.AutoDetect)
        {
            var llamaAppInstall = FindAppPath(macAppName: "Llama.app", winSubPath: Path.Combine("Programs", "Llama", "Llama.exe"));
            var llamaAppUri = ResolveOverrideOrDefault(settings, LlamaAppHostId, "http://127.0.0.1:9931");
            var llamaAppToken = await SafeGetSecretAsync(secrets, LocalHostSettings.TokenSecretIdFor(LlamaAppHostId), ct).ConfigureAwait(false);
            targets.Add(new LocalHostProbeTarget(
                LlamaAppHostId,
                LocalHostKind.OpenAiLocal,
                "Llama.app (:9931)",
                llamaAppUri,
                llamaAppToken,
                IsInstalledHint: llamaAppInstall is not null,
                InstallPath: llamaAppInstall,
                CanOpenApp: llamaAppInstall is not null));
            targets.Add(new LocalHostProbeTarget(
                LlamaServerHostId, LocalHostKind.OpenAiLocal, "Local llama-server (:8080)", new Uri("http://127.0.0.1:8080")));
            targets.Add(new LocalHostProbeTarget(
                JanHostId, LocalHostKind.OpenAiLocal, "Jan (:1337)", new Uri("http://127.0.0.1:1337")));
            targets.Add(new LocalHostProbeTarget(
                OpenAi8000HostId, LocalHostKind.OpenAiLocal, "Local OpenAI (:8000)", new Uri("http://127.0.0.1:8000")));
        }

        // 5. User-added custom loopback hosts
        foreach (var custom in settings.CustomHosts)
        {
            if (string.IsNullOrWhiteSpace(custom.Id) || !LocalHostRoute.TryParseLoopbackUri(custom.BaseUrl, out var uri) || uri is null)
                continue;
            var token = await SafeGetSecretAsync(secrets, LocalHostSettings.TokenSecretIdFor(custom.Id), ct).ConfigureAwait(false);
            var label = string.IsNullOrWhiteSpace(custom.Name) ? $"Local ({uri.Authority})" : custom.Name.Trim();
            targets.Add(new LocalHostProbeTarget(custom.Id, LocalHostKind.OpenAiLocal, label, uri, token));
        }

        return targets;
    }

    public bool TryOpenHostApp(LocalHostInstance host)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var appName = host.Kind switch
                {
                    LocalHostKind.Bionic => "Bionic",
                    LocalHostKind.LmStudio => "LM Studio",
                    LocalHostKind.Ollama => "Ollama",
                    _ => null
                };
                if (appName is null)
                    return false;

                Process.Start(new ProcessStartInfo("open", $"-a \"{appName}\"") { UseShellExecute = false });
                return true;
            }

            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(host.InstallPath) && File.Exists(host.InstallPath))
            {
                Process.Start(new ProcessStartInfo(host.InstallPath) { UseShellExecute = true });
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public async Task<bool> TryStartLocalServerAsync(LocalHostInstance host, CancellationToken ct)
    {
        if (host.Kind is not (LocalHostKind.Bionic or LocalHostKind.LmStudio))
            return false;

        var lms = FindLmsCliPath();
        if (lms is null)
            return false;

        try
        {
            using var proc = Process.Start(new ProcessStartInfo(lms, "server start")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc is null)
                return false;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Uri ResolveOverrideOrDefault(LocalHostSettings settings, string hostId, string fallback)
    {
        if (settings.PortOverrides.TryGetValue(hostId, out var raw)
            && LocalHostRoute.TryParseLoopbackUri(raw, out var overridden)
            && overridden is not null)
        {
            return overridden;
        }
        return new Uri(fallback);
    }

    internal static int? ReadConfigPort(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("port", out var portEl)
                && portEl.ValueKind == JsonValueKind.Number
                && portEl.TryGetInt32(out var port)
                && port is >= 1024 and <= 65535)
            {
                return port;
            }
        }
        catch
        {
            // Internal config format is a best-effort hint, never a hard dependency.
        }

        return null;
    }

    internal static string? ReadInstallPath(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("path", out var pathEl)
                && pathEl.ValueKind == JsonValueKind.String
                && pathEl.GetString() is { Length: > 0 } path
                && (File.Exists(path) || Directory.Exists(path)))
            {
                return path;
            }
        }
        catch
        {
            // Ignore malformed install metadata.
        }

        return null;
    }

    private string? FindLmsCliPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "lms.exe" : "lms";
        var candidate = Path.Combine(_homeDir, ".lmstudio", "bin", fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private string? FindAppPath(string? macAppName, string? winSubPath)
    {
        if (OperatingSystem.IsMacOS() && !string.IsNullOrEmpty(macAppName))
        {
            var systemApp = Path.Combine("/Applications", macAppName);
            if (Directory.Exists(systemApp))
                return systemApp;
            var userApp = Path.Combine(_homeDir, "Applications", macAppName);
            if (Directory.Exists(userApp))
                return userApp;
        }
        else if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(winSubPath) && !string.IsNullOrEmpty(_localAppData))
        {
            var winPath = Path.Combine(_localAppData, winSubPath);
            if (File.Exists(winPath))
                return winPath;
        }

        return null;
    }

    private static string? ReadLoopbackOllamaHost()
    {
        var env = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (string.IsNullOrWhiteSpace(env))
            return null;

        var normalized = env.Contains("://", StringComparison.Ordinal) ? env : "http://" + env;
        return LocalHostRoute.TryParseLoopbackUri(normalized, out var uri) && uri is not null ? uri.ToString() : null;
    }

    private static async Task<string?> SafeGetSecretAsync(ISecretStore secrets, string secretId, CancellationToken ct)
    {
        try
        {
            return await secrets.GetSecretAsync(secretId, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
