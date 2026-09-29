using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmniPadServer.App;

/// <summary>
/// Ultra-low-latency desktop / in-game screen capture server for OmniPad background video streaming.
/// Automatically sleeps when no mobile devices are connected to preserve 100% PC gaming performance.
/// Employs adaptive frame-dropping per client so input latency is strictly unaffected.
/// </summary>
public sealed class ScreenStreamServer : IAsyncDisposable
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint NativeThreadProc(IntPtr lpParameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateThread(
        IntPtr lpThreadAttributes,
        uint dwStackSize,
        NativeThreadProc lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly ConcurrentDictionary<Guid, bool> _clientBusy = new();
    private readonly ImageCodecInfo _jpegEncoder;
    private readonly EncoderParameters _encoderParams;
    private CancellationTokenSource? _cts;
    private NativeThreadProc? _nativeThreadProc;
    private IntPtr _hNativeThread = IntPtr.Zero;
    private readonly object _lock = new();
    private bool _hasLoggedFirstCapture;
    private bool _hasLoggedCaptureError;

    public int ConnectedClients => _clients.Count;

    public ScreenStreamServer()
    {
        _jpegEncoder = GetEncoder(ImageFormat.Jpeg);
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 65L); // 65% quality: high visual clarity with low bandwidth
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return codecs[0];
    }

    public async Task HandleWebSocketAsync(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        _clientBusy[id] = false;

        lock (_lock)
        {
            if (_hNativeThread == IntPtr.Zero)
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _nativeThreadProc = (param) =>
                {
                    IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
                    if (hDesk == IntPtr.Zero)
                    {
                        hDesk = OpenInputDesktop(0, false, 0x01FF);
                    }
                    int openErr = Marshal.GetLastWin32Error();
                    bool set = false;
                    if (hDesk != IntPtr.Zero)
                    {
                        set = SetThreadDesktop(hDesk);
                    }
                    int setErr = Marshal.GetLastWin32Error();
                    Console.WriteLine($"[ScreenStreamServer] Native Desktop switch: hDesk={hDesk} (Err={openErr}), SetThreadDesktop={set} (Err={setErr})");

                    try
                    {
                        CaptureLoop(token);
                    }
                    finally
                    {
                        if (hDesk != IntPtr.Zero)
                        {
                            CloseDesktop(hDesk);
                        }
                    }
                    return 0;
                };

                _hNativeThread = CreateThread(IntPtr.Zero, 0, _nativeThreadProc, IntPtr.Zero, 0, out uint threadId);
                int createErr = Marshal.GetLastWin32Error();
                Console.WriteLine($"[ScreenStreamServer] Native capture thread started: hThread={_hNativeThread}, id={threadId}, err={createErr}");
            }
        }

        byte[] dummy = new byte[64];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var res = await socket.ReceiveAsync(new ArraySegment<byte>(dummy), CancellationToken.None).ConfigureAwait(false);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch { }
        finally
        {
            _clients.TryRemove(id, out _);
            _clientBusy.TryRemove(id, out _);

            lock (_lock)
            {
                if (_clients.IsEmpty && _cts != null)
                {
                    _cts.Cancel();
                    _cts = null;
                    if (_hNativeThread != IntPtr.Zero)
                    {
                        CloseHandle(_hNativeThread);
                        _hNativeThread = IntPtr.Zero;
                    }
                    _nativeThreadProc = null;
                }
            }
        }
    }

    private void CaptureLoop(CancellationToken ct)
    {
        // Capture resolution: 960x540 (half-1080p, ultra-fluid for mobile screens & low latency)
        int targetWidth = 960;
        int targetHeight = 540;

        using var memoryStream = new MemoryStream(65536);

        while (!ct.IsCancellationRequested && !_clients.IsEmpty)
        {
            long startTime = Environment.TickCount64;

            try
            {
                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                if (screenWidth <= 0) screenWidth = 1920;
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);
                if (screenHeight <= 0) screenHeight = 1080;

                using var screenBmp = new Bitmap(screenWidth, screenHeight, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(screenBmp))
                {
                    g.CopyFromScreen(0, 0, 0, 0, screenBmp.Size, CopyPixelOperation.SourceCopy);
                }

                using var scaledBmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
                using (var gScaled = Graphics.FromImage(scaledBmp))
                {
                    gScaled.InterpolationMode = InterpolationMode.Bilinear;
                    gScaled.DrawImage(screenBmp, 0, 0, targetWidth, targetHeight);
                }

                memoryStream.SetLength(0);
                scaledBmp.Save(memoryStream, _jpegEncoder, _encoderParams);
                byte[] frameBytes = memoryStream.ToArray();

                if (!_hasLoggedFirstCapture)
                {
                    _hasLoggedFirstCapture = true;
                    Console.WriteLine($"[ScreenStreamServer] Streaming primary PC desktop ({screenWidth}x{screenHeight} -> {targetWidth}x{targetHeight} JPEG @ 30 FPS)");
                }

                BroadcastFrame(frameBytes, ct);
            }
            catch (Exception ex)
            {
                if (!_hasLoggedCaptureError)
                {
                    _hasLoggedCaptureError = true;
                    Console.WriteLine($"[ScreenStreamServer] Desktop capture fallback: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }

                // Standby fallback frame when display surface is locked or in sleep mode
                try
                {
                    using var fallbackBmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(fallbackBmp))
                    {
                        g.Clear(Color.FromArgb(12, 16, 26));
                        using var brush = new LinearGradientBrush(new Rectangle(0, 0, targetWidth, targetHeight), Color.FromArgb(20, 28, 48), Color.FromArgb(8, 12, 20), LinearGradientMode.Vertical);
                        g.FillRectangle(brush, 0, 0, targetWidth, targetHeight);

                        using var gridPen = new Pen(Color.FromArgb(28, 42, 68), 1);
                        for (int x = 0; x < targetWidth; x += 48) g.DrawLine(gridPen, x, 0, x, targetHeight);
                        for (int y = 0; y < targetHeight; y += 48) g.DrawLine(gridPen, 0, y, targetWidth, y);

                        using var fontTitle = new Font("Segoe UI", 22, FontStyle.Bold);
                        using var fontSub = new Font("Segoe UI", 13, FontStyle.Regular);
                        using var fontTime = new Font("Segoe UI", 11, FontStyle.Italic);
                        using var brushTitle = new SolidBrush(Color.FromArgb(0, 230, 118));
                        using var brushSub = new SolidBrush(Color.FromArgb(180, 205, 235));
                        using var brushTime = new SolidBrush(Color.FromArgb(100, 130, 170));

                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("OmniPad Background Stream Active", fontTitle, brushTitle, new RectangleF(0, 180, targetWidth, 45), sf);
                        g.DrawString("Desktop in standby or locked • Launch your PC game to stream", fontSub, brushSub, new RectangleF(0, 230, targetWidth, 35), sf);
                        g.DrawString($"PC Ready  •  {DateTime.Now:HH:mm:ss.ff}  •  30 FPS", fontTime, brushTime, new RectangleF(0, 275, targetWidth, 30), sf);
                    }

                    memoryStream.SetLength(0);
                    fallbackBmp.Save(memoryStream, _jpegEncoder, _encoderParams);
                    byte[] fallbackBytes = memoryStream.ToArray();
                    BroadcastFrame(fallbackBytes, ct);
                }
                catch { }
            }

            // Cap at 30 FPS (~33ms interval) to conserve mobile decoding CPU and maintain low thermals
            long elapsed = Environment.TickCount64 - startTime;
            int sleepMs = (int)Math.Max(5, 33 - elapsed);
            if (ct.WaitHandle.WaitOne(sleepMs))
            {
                break;
            }
        }
    }

    private void BroadcastFrame(byte[] frameBytes, CancellationToken ct)
    {
        foreach (var kvp in _clients)
        {
            var clientId = kvp.Key;
            var socket = kvp.Value;

            if (socket.State == WebSocketState.Open)
            {
                if (_clientBusy.TryGetValue(clientId, out bool busy) && busy)
                {
                    continue;
                }

                _clientBusy[clientId] = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await socket.SendAsync(
                            new ArraySegment<byte>(frameBytes),
                            WebSocketMessageType.Binary,
                            true,
                            CancellationToken.None
                        ).ConfigureAwait(false);
                    }
                    catch { }
                    finally
                    {
                        _clientBusy[clientId] = false;
                    }
                }, ct);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = null;
            if (_hNativeThread != IntPtr.Zero)
            {
                CloseHandle(_hNativeThread);
                _hNativeThread = IntPtr.Zero;
            }
            _nativeThreadProc = null;
            _encoderParams.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
