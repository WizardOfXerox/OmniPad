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
    private readonly ImageCodecInfo? _jpegEncoder;
    private readonly EncoderParameters? _encoderParams;
    private CancellationTokenSource? _cts;
    private NativeThreadProc? _nativeThreadProc;
    private IntPtr _hNativeThread = IntPtr.Zero;
    private readonly object _lock = new();
    private bool _hasLoggedFirstCapture;
    private bool _hasLoggedCaptureError;

    // Minimal valid 1x1 black JPEG fallback frame for non-Windows platforms
    private static readonly byte[] FallbackStubFrame =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48,
        0x00, 0x48, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08,
        0x07, 0x07, 0x07, 0x09, 0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
        0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20, 0x24, 0x2E, 0x27, 0x20,
        0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29, 0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27,
        0x39, 0x3D, 0x38, 0x32, 0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
        0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00, 0x01, 0x05, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04,
        0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F,
        0x00, 0xBF, 0x00, 0xFF, 0xD9
    ];

    public int ConnectedClients => _clients.Count;

    public ScreenStreamServer()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                _encoderParams = new EncoderParameters(1);
                _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 65L); // 65% quality: high visual clarity with low bandwidth
            }
            catch { }
        }
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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[ScreenStreamServer] Desktop screen capture via DXGI/GDI is supported on Windows hosts only ({RuntimeInformation.OSDescription}). Returning platform fallback frame.");
            Console.ResetColor();

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(new ArraySegment<byte>(FallbackStubFrame), WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                }

                byte[] dummyBuffer = new byte[64];
                while (socket.State == WebSocketState.Open)
                {
                    var res = await socket.ReceiveAsync(new ArraySegment<byte>(dummyBuffer), CancellationToken.None).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Screen streaming is supported on Windows hosts only", CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch { }
            return;
        }

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
                if (_jpegEncoder != null && _encoderParams != null)
                {
                    scaledBmp.Save(memoryStream, _jpegEncoder, _encoderParams);
                }
                else
                {
                    scaledBmp.Save(memoryStream, ImageFormat.Jpeg);
                }
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
                    if (_jpegEncoder != null && _encoderParams != null)
                    {
                        fallbackBmp.Save(memoryStream, _jpegEncoder, _encoderParams);
                    }
                    else
                    {
                        fallbackBmp.Save(memoryStream, ImageFormat.Jpeg);
                    }
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
            if (_hNativeThread != IntPtr.Zero && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CloseHandle(_hNativeThread);
                _hNativeThread = IntPtr.Zero;
            }
            _nativeThreadProc = null;
            _encoderParams?.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
