using System.Net.WebSockets;
using System.Text;
using LiveLingo.Core.Cloud;

namespace LiveLingo.Core.Speech.Cloud;

public interface IQwenWebSocketConnection : IAsyncDisposable
{
    Task SendTextAsync(string json, CancellationToken ct);
    Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>Reads the next complete UTF-8 text message, or returns null when the server closed the socket cleanly.</summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);

    Task CloseOutputAsync(CancellationToken ct);
}

public interface IQwenWebSocketTransport
{
    Task<IQwenWebSocketConnection> ConnectAsync(Uri endpoint, string apiKey, CancellationToken ct);
}

public sealed class ClientWebSocketTransport : IQwenWebSocketTransport
{
    public static ClientWebSocketTransport Shared { get; } = new();

    public async Task<IQwenWebSocketConnection> ConnectAsync(Uri endpoint, string apiKey, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        try
        {
            await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
            return new Connection(socket);
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            throw new CloudSpeechException($"Could not connect to cloud speech endpoint '{endpoint.Authority}': {ex.Message}", ex);
        }
    }

    private sealed class Connection(ClientWebSocket socket) : IQwenWebSocketConnection
    {
        public Task SendTextAsync(string json, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }

        public Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
            socket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, ct).AsTask();

        public async Task<string?> ReceiveTextAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                if (result.Count > 0)
                    ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                    ms.SetLength(0);
                }
            }
        }

        public async Task CloseOutputAsync(CancellationToken ct)
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct).ConfigureAwait(false);
                }
                catch
                {
                    // Closing a half-open or peer-terminated socket is best-effort.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort shutdown.
                }
            }
            socket.Dispose();
        }
    }
}
