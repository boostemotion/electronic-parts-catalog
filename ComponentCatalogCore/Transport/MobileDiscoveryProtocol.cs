using System.Net;
using System.Text;
using System.Text.Json;

namespace ComponentCatalogCore.Transport;

public static class MobileDiscoveryProtocol
{
    public const int DefaultPort = 10850;
    public const string RequestMagic = "COMPONENT_CATALOG_DISCOVER_V1";

    private static readonly byte[] RequestBytes = Encoding.UTF8.GetBytes(RequestMagic);

    public static bool IsDiscoveryRequest(ReadOnlySpan<byte> datagram)
    {
        return datagram.SequenceEqual(RequestBytes);
    }

    public static string CreateResponse(string host, int port)
    {
        if (!CatalogApiBindingOptions.IsPrivateIpv4(host))
        {
            throw new ArgumentException("Discovery host must be a private IPv4 address.", nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Discovery port must be between 1 and 65535.");
        }

        return JsonSerializer.Serialize(new
        {
            service = "component-catalog",
            version = 1,
            name = "电子元件入库台",
            host = IPAddress.Parse(host).ToString(),
            port,
            path = "/mobile/snapshot",
            readOnly = true,
        });
    }
}
