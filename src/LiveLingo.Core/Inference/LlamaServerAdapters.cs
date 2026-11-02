using System.Net;
using System.Net.Sockets;

namespace LiveLingo.Core.Inference;

/// <summary>Probes llama-server's <c>/health</c> endpoint. A refused connection simply means it is still starting.</summary>
public sealed class HttpHealthProbe : IServerHealthProbe
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task<bool> IsReadyAsync(Uri endpoint, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(endpoint, "health"), ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}

public sealed class LoopbackPortAllocator : IPortAllocator
{
    public int NextLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
