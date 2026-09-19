using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OmniPadServer.Core;

namespace OmniPadServer.App;

public sealed class DiscoveryServer : IDisposable
{
    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public DiscoveryServer(int port = Protocol.DiscoveryPort)
    {
        _socket = new UdpClient();
        _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        byte[] response = new byte[Protocol.SessionMessageSize];
        var welcomeMsg = new SessionMessage(Protocol.MsgWelcome, Protocol.NoPad);
        welcomeMsg.WriteTo(response);

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(_cts.Token);
                if (result.Buffer.Length == Protocol.SessionMessageSize &&
                    SessionMessage.TryParse(result.Buffer, out var msg) &&
                    msg.Type == Protocol.MsgDiscover)
                {
                    // Respond back to phone with server confirmation
                    await _socket.SendAsync(response, response.Length, result.RemoteEndPoint);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] Listener error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Dispose();
        _cts.Dispose();
    }
}
