using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OmniPadServer.App;

public static class NetworkHelper
{
    public static string GetLocalIpAddress()
    {
        try
        {
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(n =>
                {
                    string name = (n.Name + " " + n.Description).ToLowerInvariant();
                    return !name.Contains("virtual") &&
                           !name.Contains("vbox") &&
                           !name.Contains("vmware") &&
                           !name.Contains("hyper-v") &&
                           !name.Contains("wsl") &&
                           !name.Contains("tap") &&
                           !name.Contains("pseudo");
                })
                .OrderByDescending(n =>
                {
                    var props = n.GetIPProperties();
                    bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                    int typeScore = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 20 :
                                    n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 10 : 0;
                    return (hasGateway ? 100 : 0) + typeScore;
                });

            foreach (var ni in activeInterfaces)
            {
                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        string ip = addr.Address.ToString();
                        if (!ip.StartsWith("169.254.")) // Skip APIPA
                        {
                            return ip;
                        }
                    }
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }
}
