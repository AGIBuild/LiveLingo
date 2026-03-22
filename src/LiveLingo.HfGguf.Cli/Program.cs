using System.Diagnostics;
using LiveLingo.HfGguf;

return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0)
    {
        PrintHelp();
        return 1;
    }

    var cmd = args[0];
    var opt = CliOptions.Parse(args.AsSpan(1));
    if (opt.ParseError is { } err)
    {
        Console.Error.WriteLine(err);
        return 1;
    }

    try
    {
        return cmd switch
        {
            "list" => await RunListAsync(opt).ConfigureAwait(false),
            "download" => await RunDownloadAsync(opt).ConfigureAwait(false),
            _ => UnknownCommand(cmd)
        };
    }
    catch (HfHubException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 3;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 4;
    }
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"Unknown command '{cmd}'. Use: list | download");
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine("""
        livelingo-hfgguf — list / download GGUF from Hugging Face (HTTP, resumable)

        list --repo <id> [--rev main] [--token ...] [--token-env HF_TOKEN] [--base-url URL]

        download --repo <id> --out <file|dir> [--rev main] [--ctx 4096] [--quant Q4_K_M]
                 [--prefer-safer-memory] [--file path/in/repo.gguf] [--force]
                 [--buffer-size 1048576] [--token ...] [--token-env ...] [--base-url URL]
        """);
}

static async Task<int> RunListAsync(CliOptions o)
{
    if (string.IsNullOrWhiteSpace(o.Repo))
    {
        Console.Error.WriteLine("list requires --repo");
        return 1;
    }

    var bearer = o.ResolveToken();
    var hub = o.HubBase();
    using var http = new HttpClient { Timeout = TimeSpan.FromHours(4) };
    IReadOnlyList<string> paths;
    try
    {
        var api = new HfApiFileLister(http, hub);
        paths = await api.ListGgufPathsAsync(o.Repo!, o.Rev, bearer, CancellationToken.None).ConfigureAwait(false);
    }
    catch (HfHubException ex)
    {
        Console.Error.WriteLine("API list failed: " + ex.Message);
        var html = new HfHtmlFileLister(http, hub);
        paths = await html.ListGgufPathsAsync(o.Repo!, o.Rev, bearer, CancellationToken.None)
            .ConfigureAwait(false);
    }

    foreach (var p in paths)
        Console.WriteLine(p);
    return 0;
}

static async Task<int> RunDownloadAsync(CliOptions o)
{
    if (string.IsNullOrWhiteSpace(o.Repo))
    {
        Console.Error.WriteLine("download requires --repo");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(o.Out))
    {
        Console.Error.WriteLine("download requires --out");
        return 1;
    }

    var bearer = o.ResolveToken();
    var hub = o.HubBase();
    using var http = new HttpClient { Timeout = TimeSpan.FromHours(24) };
    string chosenPath;
    if (!string.IsNullOrWhiteSpace(o.File))
    {
        chosenPath = o.File.Trim();
    }
    else
    {
        IReadOnlyList<string> paths;
        try
        {
            var api = new HfApiFileLister(http, hub);
            paths = await api.ListGgufPathsAsync(o.Repo!, o.Rev, bearer, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (HfHubException ex)
        {
            Console.Error.WriteLine("API list failed: " + ex.Message);
            var html = new HfHtmlFileLister(http, hub);
            paths = await html.ListGgufPathsAsync(o.Repo!, o.Rev, bearer, CancellationToken.None)
                .ConfigureAwait(false);
        }

        var pick = QuantChooser.Choose(paths, o.PreferSaferMemory, o.Ctx, o.Quant);
        if (pick is null)
        {
            Console.Error.WriteLine("No matching .gguf file. Use --file <path-in-repo>.");
            return 2;
        }

        chosenPath = pick;
        Console.WriteLine($"Selected: {chosenPath}");
    }

    var dest = o.Out!;
    if (Directory.Exists(dest) || dest.EndsWith(Path.DirectorySeparatorChar) || dest.EndsWith('/'))
    {
        var name = Path.GetFileName(chosenPath.Replace('/', Path.DirectorySeparatorChar));
        dest = Path.Combine(dest.TrimEnd(Path.DirectorySeparatorChar, '/'), name);
    }

    var downloader = new HfResolveDownloader(http, hub);
    var sw = Stopwatch.StartNew();
    var lastReport = Stopwatch.StartNew();
    var progress = new Progress<HfDownloadProgress>(p =>
    {
        if (lastReport.ElapsedMilliseconds < 1000)
            return;
        lastReport.Restart();
        var mb = p.DownloadedBytes / (1024d * 1024d);
        var totalStr = p.TotalBytes is { } t ? $" / {t / (1024d * 1024d):F1} MiB" : "";
        Console.WriteLine($"{mb:F1} MiB{totalStr}");
    });

    await downloader
        .DownloadAsync(
            o.Repo!,
            o.Rev,
            chosenPath,
            dest,
            bearer,
            o.Force,
            o.BufferSize,
            progress,
            CancellationToken.None)
        .ConfigureAwait(false);
    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F1}s → {dest}");
    return 0;
}

internal sealed class CliOptions
{
    public string? Repo { get; private init; }
    public string Rev { get; private init; } = "main";
    public string? Token { get; private init; }
    public string? TokenEnv { get; private init; }
    public string? BaseUrl { get; private init; }
    public string? Out { get; private init; }
    public int Ctx { get; private init; } = 4096;
    public string? Quant { get; private init; }
    public bool PreferSaferMemory { get; private init; }
    public string? File { get; private init; }
    public bool Force { get; private init; }
    public int BufferSize { get; private init; } = 1024 * 1024;
    public string? ParseError { get; private init; }

    public string HubBase() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? "https://huggingface.co" : BaseUrl!.Trim();

    public string? ResolveToken()
    {
        if (!string.IsNullOrWhiteSpace(Token))
            return Token.Trim();
        if (!string.IsNullOrWhiteSpace(TokenEnv))
            return Environment.GetEnvironmentVariable(TokenEnv.Trim());
        return null;
    }

    public static CliOptions Parse(ReadOnlySpan<string> args)
    {
        string? repo = null;
        var rev = "main";
        string? token = null;
        string? tokenEnv = null;
        string? baseUrl = null;
        string? @out = null;
        var ctx = 4096;
        string? quant = null;
        var preferSafer = false;
        string? file = null;
        var force = false;
        var buffer = 1024 * 1024;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
                return new CliOptions { ParseError = $"Unexpected argument: {a}" };

            var key = a[2..];
            string? val = null;
            var isFlag = key is "prefer-safer-memory" or "force";
            if (!isFlag)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return new CliOptions { ParseError = $"Missing value for --{key}" };
                val = args[++i];
            }

            switch (key)
            {
                case "repo": repo = val; break;
                case "rev": rev = val ?? "main"; break;
                case "token": token = val; break;
                case "token-env": tokenEnv = val; break;
                case "base-url": baseUrl = val; break;
                case "out": @out = val; break;
                case "ctx":
                    if (!int.TryParse(val, out ctx))
                        return new CliOptions { ParseError = "Invalid --ctx" };
                    break;
                case "quant": quant = val; break;
                case "prefer-safer-memory": preferSafer = true; break;
                case "file": file = val; break;
                case "force": force = true; break;
                case "buffer-size":
                    if (!int.TryParse(val, out buffer) || buffer < 4096)
                        return new CliOptions { ParseError = "Invalid --buffer-size (min 4096)" };
                    break;
                default:
                    return new CliOptions { ParseError = $"Unknown option --{key}" };
            }
        }

        return new CliOptions
        {
            Repo = repo,
            Rev = rev,
            Token = token,
            TokenEnv = tokenEnv,
            BaseUrl = baseUrl,
            Out = @out,
            Ctx = ctx,
            Quant = quant,
            PreferSaferMemory = preferSafer,
            File = file,
            Force = force,
            BufferSize = buffer
        };
    }
}
