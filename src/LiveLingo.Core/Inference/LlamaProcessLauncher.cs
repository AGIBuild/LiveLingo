using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Inference;

/// <summary>Starts llama-server as a child process and kills its whole tree on dispose.</summary>
public sealed class LlamaProcessLauncher(ServerPidRegistry registry, ILogger<LlamaProcessLauncher> logger) : ILlamaProcessLauncher
{
    private sealed class ChildProcess : ILlamaProcess
    {
        private readonly Process _process;

        private readonly ServerPidRegistry _registry;
        private readonly int _pid;

        public ChildProcess(Process process, ServerPidRegistry registry)
        {
            _process = process;
            _registry = registry;
            _pid = process.Id;
        }

        public bool HasExited => _process.HasExited;

        public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

        public ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
            _process.Dispose();
            _registry.Remove(_pid);
            return ValueTask.CompletedTask;
        }
    }

    public ILlamaProcess Start(LlamaServerLaunch launch)
    {
        var info = new ProcessStartInfo(launch.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in launch.Arguments)
            info.ArgumentList.Add(argument);

        var process = Process.Start(info)
            ?? throw new LlamaServerStartException($"Could not start '{launch.ExecutablePath}'.");
        registry.Record(process.Id);
        process.OutputDataReceived += (_, e) => Log(e.Data);
        process.ErrorDataReceived += (_, e) => Log(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new ChildProcess(process, registry);
    }

    private void Log(string? line)
    {
        if (!string.IsNullOrEmpty(line))
            logger.LogDebug("llama-server: {Line}", line);
    }
}
