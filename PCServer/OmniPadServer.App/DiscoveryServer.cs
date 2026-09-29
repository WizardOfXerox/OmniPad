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
    private readonly byte[] _cachedResponse;
    private Task? _listenTask;

    public DiscoveryServer(int port = Protocol.DiscoveryPort, int webPort = Protocol.DefaultWebPort, string? machineName = null)
    {
        _socket = new UdpClient();
        _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        string host = string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName;
        _cachedResponse = DiscoveryResponse.Encode((ushort)webPort, host);
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _socket.ReceiveAsync(_cts.Token);
                if (result.Buffer.Length >= Protocol.SessionMessageSize &&
                    SessionMessage.TryParse(result.Buffer, out var msg) &&
                    msg.Type == Protocol.MsgDiscover)
                {
                    // Respond back to phone with server confirmation and machine info
                    await _socket.SendAsync(_cachedResponse, _cachedResponse.Length, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_cts.IsCancellationRequested) break;
                Console.WriteLine($"[Discovery] Transient error: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Dispose();
        _cts.Dispose();
    }
}
