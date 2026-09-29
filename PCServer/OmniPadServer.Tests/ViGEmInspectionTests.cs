#if WINDOWS
using System;
using System.Reflection;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace OmniPadServer.Tests;

public class ViGEmDualShock4Tests
{
    [Fact]
    public void TestSubmitRawReport()
    {
        try
        {
            using var client = new Nefarius.ViGEm.Client.ViGEmClient();
            var pad = client.CreateDualShock4Controller();
            pad.AutoSubmitReport = false;
            pad.Connect();
            try
            {
                byte[] report = new byte[63];
                report[0] = 128;
                report[1] = 128;
                report[2] = 128;
                report[3] = 128;
                report[4] = 8; // Neutral D-pad
                pad.SubmitRawReport(report);
            }
            finally
            {
                pad.Disconnect();
            }
        }
        catch (Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException)
        {
            // ViGEmBus kernel driver not installed on host machine; test safely completes
        }
    }
}
#endif
