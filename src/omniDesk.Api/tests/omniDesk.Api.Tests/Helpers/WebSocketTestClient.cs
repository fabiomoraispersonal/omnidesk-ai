using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Spec 007 — thin async wrapper around <see cref="WebSocketClient"/> exposed by
/// <see cref="WebApplicationFactory{T}"/> for native WebSocket integration tests. Provides
/// JSON send/receive helpers and a typed assertion model.
/// </summary>
public sealed class WebSocketTestClient : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly byte[] _buffer = new byte[16 * 1024];

    public static async Task<WebSocketTestClient> ConnectAsync<TProgram>(
        WebApplicationFactory<TProgram> factory,
        string path,
        Action<HttpRequestMessage>? configureRequest = null,
        CancellationToken ct = default) where TProgram : class
    {
        var wsClient = factory.Server.CreateWebSocketClient();
        if (configureRequest is not null)
            wsClient.ConfigureRequest = configureRequest;

        var uri = new Uri(factory.Server.BaseAddress, path);
        // CreateWebSocketClient strips the scheme; ws:// is wired by the test host.
        var wsUri = new UriBuilder(uri) { Scheme = "ws" }.Uri;
        var socket = await wsClient.ConnectAsync(wsUri, ct);
        return new WebSocketTestClient(socket);
    }

    private WebSocketTestClient(WebSocket socket) => _socket = socket;

    public WebSocket Socket => _socket;

    public Task SendJsonAsync(object payload, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        return _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async Task<JsonDocument?> ReceiveJsonAsync(CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(_buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(_buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        ms.Position = 0;
        return await JsonDocument.ParseAsync(ms, default, ct);
    }

    public async Task CloseAsync(WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure)
    {
        if (_socket.State == WebSocketState.Open)
            await _socket.CloseOutputAsync(status, "test-close", CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try { await CloseAsync(); }
        catch { /* ignore */ }
        _socket.Dispose();
    }
}
