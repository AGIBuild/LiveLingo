using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Inference;

public interface IProcessInspector
{
    /// <summary>The process name, or null when no process with that id exists.</summary>
    string? NameOf(int pid);

    void Kill(int pid);
}

public sealed class SystemProcessInspector : IProcessInspector
{
    public string? NameOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Kill(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
    }
}

/// <summary>
/// Remembers the llama-server processes this app started, one file per pid, so servers orphaned by a crash or a
/// forced exit are stopped at the next start. A recorded pid is only killed while it still names llama-server,
/// so a recycled pid is never touched.
/// </summary>
public sealed class ServerPidRegistry(string directory, IProcessInspector inspector, ILogger<ServerPidRegistry> logger)
{
    public const string ServerProcessName = "llama-server";

    public void Record(int pid)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathFor(pid), "");
    }

    public void Remove(int pid)
    {
        var path = PathFor(pid);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Stops recorded servers that are still running and clears the records. Returns how many were stopped.</summary>
    public int ReapOrphans()
    {
        if (!Directory.Exists(directory))
            return 0;

        var stopped = 0;
        foreach (var file in Directory.GetFiles(directory, "*.pid"))
        {
            if (int.TryParse(Path.GetFileNameWithoutExtension(file), out var pid)
                && inspector.NameOf(pid) is { } name
                && name.StartsWith(ServerProcessName, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Stopping orphaned llama-server from a previous run: pid={Pid}", pid);
                inspector.Kill(pid);
                stopped++;
            }
            File.Delete(file);
        }
        return stopped;
    }

    private string PathFor(int pid) => Path.Combine(directory, pid + ".pid");
}
