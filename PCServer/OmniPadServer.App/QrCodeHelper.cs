using System;
using QRCoder;

namespace OmniPadServer.App;

public static class QrCodeHelper
{
    public static void PrintTerminalQr(string url, bool invert = false)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            // ECCLevel M (15%) for robust scanning off monitors/terminals
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var asciiQr = new AsciiQRCode(data);

            // GetGraphicSmall condenses two vertical rows into single unicode half-block characters (▀, ▄, █).
            // This achieves a crisp, compact, perfect 1:1 square aspect ratio (~33 chars wide x 17 lines high),
            // completely eliminating vertical stretching and terminal line-wrapping!
            var prevBg = Console.BackgroundColor;
            var prevFg = Console.ForegroundColor;
            try
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;
                string qrText = asciiQr.GetGraphicSmall(true, invert, "\n");
                Console.WriteLine(qrText);
            }
            finally
            {
                Console.BackgroundColor = prevBg;
                Console.ForegroundColor = prevFg;
            }
        }
        catch
        {
            // Fallback: URL text link
            Console.WriteLine("[Scan URL]: " + url);
        }
    }
}
