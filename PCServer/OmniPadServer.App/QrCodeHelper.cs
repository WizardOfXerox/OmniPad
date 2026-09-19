using System;
using QRCoder;

namespace OmniPadServer.App;

public static class QrCodeHelper
{
    public static void PrintTerminalQr(string url)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.L);
            var asciiQr = new AsciiQRCode(data);
            string qrText = asciiQr.GetGraphic(1, "██", "  ");

            Console.WriteLine(qrText);
        }
        catch
        {
            // Fallback if terminal character rendering fails
            Console.WriteLine("[Scan URL]: " + url);
        }
    }
}
