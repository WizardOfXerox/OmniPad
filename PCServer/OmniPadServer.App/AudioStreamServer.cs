using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
#if WINDOWS
using NAudio.Wave;
#endif

namespace OmniPadServer.App;

/// <summary>
/// Ultra-low latency game audio streaming server (Controller Headphone Jack).
/// Captures Windows default playback audio using WASAPI loopback and streams 
/// 16-bit PCM stereo directly to connected mobile browsers over WebSockets.
/// </summary>
public sealed class AudioStreamServer : IAsyncDisposable
{
    private sealed class AudioClient
    {
        public WebSocket Socket { get; }
        public Channel<byte[]> Channel { get; }

        public AudioClient(WebSocket socket)
        {
            Socket = socket;
            Channel = System.Threading.Channels.Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(16)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });
        }
    }

#if WINDOWS
    private WasapiLoopbackCapture? _capture;
#endif
    private readonly ConcurrentDictionary<Guid, AudioClient> _clients = new();
    private readonly object _lock = new();
    private int _targetSampleRate = 48000;
    private int _targetChannels = 2;
#if WINDOWS
    private bool _isCapturing;
#endif

    public int ConnectedListeners => _clients.Count;

    public async Task HandleWebSocketAsync(WebSocket socket)
    {
        var clientId = Guid.NewGuid();
        var client = new AudioClient(socket);
        _clients[clientId] = client;

        lock (_lock)
        {
            EnsureCaptureRunning();
        }

        // Send Audio Stream Metadata Handshake
        var handshake = new
        {
            type = "audio_init",
            sampleRate = _targetSampleRate,
            channels = _targetChannels,
            format = "pcm16"
        };
        string json = JsonSerializer.Serialize(handshake);
        byte[] metaBytes = Encoding.UTF8.GetBytes(json);

        using var cts = new CancellationTokenSource();

        // Dedicated sequential sender loop: guarantees zero concurrent SendAsync collisions
        var sendLoopTask = Task.Run(async () =>
        {
            try
            {
                var reader = client.Channel.Reader;
                while (await reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var chunk))
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, cts.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch { }
        }, cts.Token);

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(metaBytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }

            byte[] recvBuffer = new byte[128];
            while (socket.State == WebSocketState.Open)
            {
                var res = await socket.ReceiveAsync(recvBuffer, CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // Socket closed or network error
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            cts.Cancel();
            client.Channel.Writer.TryComplete();
            try { await sendLoopTask.ConfigureAwait(false); } catch { }

            lock (_lock)
            {
                if (_clients.IsEmpty)
                {
                    StopCapture();
                }
            }

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Audio stream ended", CancellationToken.None);
                }
            }
            catch { }
        }
    }

    private void EnsureCaptureRunning()
    {
#if WINDOWS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[Audio Jack] Audio loopback streaming via WASAPI is supported on Windows hosts only ({RuntimeInformation.OSDescription}). Audio capture disabled on this platform.");
            Console.ResetColor();
            return;
        }

        if (_isCapturing || _capture != null) return;

        try
        {
            _capture = new WasapiLoopbackCapture();
            var srcFormat = _capture.WaveFormat;
            _targetSampleRate = srcFormat.SampleRate;
            _targetChannels = Math.Min(2, srcFormat.Channels);

            _capture.DataAvailable += OnAudioDataAvailable;
            _capture.RecordingStopped += (_, _) =>
            {
                _isCapturing = false;
            };

            _capture.StartRecording();
            _isCapturing = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Audio Jack] WASAPI Loopback Capture started ({_targetSampleRate} Hz, {_targetChannels} ch).");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Audio Jack] Warning: Could not initialize WASAPI loopback ({ex.Message}).");
            Console.ResetColor();
            _capture = null;
            _isCapturing = false;
        }
#else
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[Audio Jack] Audio loopback streaming via WASAPI is supported on Windows hosts only ({RuntimeInformation.OSDescription}). Audio capture disabled on this platform.");
        Console.ResetColor();
#endif
    }

    private void StopCapture()
    {
#if WINDOWS
        if (!_isCapturing && _capture == null) return;

        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnAudioDataAvailable;
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }
        }
        catch { }
        finally
        {
            _isCapturing = false;
            Console.WriteLine("[Audio Jack] All listeners disconnected. Capture paused (0% CPU).");
        }
#else
        // No capture active on non-Windows
#endif
    }

#if WINDOWS
    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _clients.IsEmpty || _capture == null) return;

        var format = _capture.WaveFormat;
        byte[] pcm16Bytes;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            // Convert 32-bit IEEE Float to 16-bit PCM
            int floatCount = e.BytesRecorded / 4;
            pcm16Bytes = new byte[floatCount * 2];

            ReadOnlySpan<float> floatSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
                e.Buffer.AsSpan(0, e.BytesRecorded));
            Span<short> shortSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm16Bytes);

            for (int i = 0; i < floatSpan.Length; i++)
            {
                float f = floatSpan[i];
                if (f > 1.0f) f = 1.0f;
                else if (f < -1.0f) f = -1.0f;
                shortSpan[i] = (short)(f * 32767f);
            }
        }
        else
        {
            // Already 16-bit PCM or compatible
            pcm16Bytes = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, pcm16Bytes, 0, e.BytesRecorded);
        }

        // Broadcast to all listening mobile phones via their dedicated bounded channel
        foreach (var client in _clients.Values)
        {
            client.Channel.Writer.TryWrite(pcm16Bytes);
        }
    }
#endif

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            StopCapture();
        }
        return ValueTask.CompletedTask;
    }
}
