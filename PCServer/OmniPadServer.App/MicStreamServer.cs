using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OmniPadServer.App;

/// <summary>
/// Receives wireless microphone 16-bit PCM audio from mobile clients (/ws/mic and TCP 27503)
/// and outputs it to Windows audio system / virtual audio cable for Discord and games.
/// </summary>
public sealed class MicStreamServer : IAsyncDisposable
{
    private readonly WaveFormat _waveFormat = new(16000, 16, 1); // 16kHz, 16-bit, Mono
    private BufferedWaveProvider? _waveProvider;
    private WasapiOut? _waveOut;
    private MMDevice? _targetDevice;
    private readonly object _lock = new();
    private int _activeStreamers;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;

    public bool IsStreaming => _activeStreamers > 0;

    public void StartTcpListener(int port = 27503)
    {
        try
        {
            _cts = new CancellationTokenSource();
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _ = AcceptTcpClientsAsync(_tcpListener, _cts.Token);
            Console.WriteLine($"[MicStreamServer] Direct TCP Mic Audio stream listening on 0.0.0.0:{port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MicStreamServer] Failed to start TCP mic listener: {ex.Message}");
        }
    }

    private async Task AcceptTcpClientsAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleTcpClientAsync(client, ct);
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MicStreamServer] Error accepting TCP mic client: {ex.Message}");
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        Interlocked.Increment(ref _activeStreamers);
        EnsureOutputRunning();
        Console.WriteLine($"[+] Microphone stream connected from {client.Client.RemoteEndPoint}");

        using (client)
        {
            var stream = client.GetStream();
            byte[] buffer = new byte[4096];
            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (read <= 0) break;
                    lock (_lock)
                    {
                        _waveProvider?.AddSamples(buffer, 0, read);
                    }
                }
            }
            catch { }
        }

        Console.WriteLine("[-] Microphone stream disconnected");
        Interlocked.Decrement(ref _activeStreamers);
        lock (_lock)
        {
            if (_activeStreamers <= 0 && _waveOut != null)
            {
                try
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                }
                catch { }
                _waveOut = null;
                _waveProvider = null;
            }
        }
    }

    private void EnsureOutputRunning()
    {
        lock (_lock)
        {
            if (_waveOut != null) return;

            try
            {
                _waveProvider = new BufferedWaveProvider(_waveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(400),
                    DiscardOnBufferOverflow = true
                };

                // Look for Virtual Audio Cable or CABLE Input device if present
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    foreach (var endpoint in endpoints)
                    {
                        if (_targetDevice == null &&
                            (endpoint.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
                             endpoint.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase)))
                        {
                            _targetDevice = endpoint;
                        }
                        else
                        {
                            endpoint.Dispose();
                        }
                    }
                }
                catch { }

                _waveOut = _targetDevice != null
                    ? new WasapiOut(_targetDevice, AudioClientShareMode.Shared, false, 50)
                    : new WasapiOut(AudioClientShareMode.Shared, 50);

                _waveOut.Init(_waveProvider);
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MicStreamServer] Failed to initialize audio output: {ex.Message}");
            }
        }
    }

    public async Task HandleWebSocketAsync(WebSocket socket)
    {
        Interlocked.Increment(ref _activeStreamers);
        EnsureOutputRunning();

        byte[] buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
                {
                    lock (_lock)
                    {
                        _waveProvider?.AddSamples(buffer, 0, result.Count);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Client disconnected or connection dropped
        }
        finally
        {
            Interlocked.Decrement(ref _activeStreamers);
            lock (_lock)
            {
                if (_activeStreamers <= 0 && _waveOut != null)
                {
                    try
                    {
                        _waveOut.Stop();
                        _waveOut.Dispose();
                        _targetDevice?.Dispose();
                    }
                    catch { }
                    _targetDevice = null;
                    _waveOut = null;
                    _waveProvider = null;
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _tcpListener?.Stop();
        lock (_lock)
        {
            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _targetDevice?.Dispose();
            }
            catch { }
            _targetDevice = null;
            _waveOut = null;
            _waveProvider = null;
        }
        return ValueTask.CompletedTask;
    }
}
