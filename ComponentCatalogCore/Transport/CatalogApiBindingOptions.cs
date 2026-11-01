using System.Net;
using System.Net.Sockets;

namespace ComponentCatalogCore.Transport;

public sealed class CatalogApiBindingOptions
{
    public const int DefaultPort = 10849;

    public CatalogApiBindingOptions(int port = DefaultPort, bool lanEnabled = false, string? lanIpv4 = null)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "API port must be between 1 and 65535.");
        }

        Port = port;
        LanEnabled = lanEnabled;
        LanIpv4 = string.IsNullOrWhiteSpace(lanIpv4) ? null : ValidatePrivateIpv4(lanIpv4);
        if (LanEnabled && LanIpv4 is null)
        {
            throw new ArgumentException("LAN address is required when LAN access is enabled.", nameof(lanIpv4));
        }
        var baseUrls = LanEnabled
            ? new[] { $"http://127.0.0.1:{Port}", $"http://{LanIpv4}:{Port}" }
            : new[] { $"http://127.0.0.1:{Port}" };
        BaseUrls = Array.AsReadOnly(baseUrls);
        Prefixes = Array.AsReadOnly(baseUrls.Select(url => $"{url}/").ToArray());
    }

    public int Port { get; }

    public bool LanEnabled { get; }

    public string? LanIpv4 { get; }

    public string? MobileSnapshotUrl => LanIpv4 is null ? null : $"http://{LanIpv4}:{Port}/mobile/snapshot";

    public string? MobileAppUpdateUrl => LanIpv4 is null ? null : $"http://{LanIpv4}:{Port}{AndroidAppRelease.ManifestPath}";

    public string? MobileAppApkUrl => LanIpv4 is null ? null : $"http://{LanIpv4}:{Port}{AndroidAppRelease.ApkPath}";

    public IReadOnlyList<string> BaseUrls { get; }

    public IReadOnlyList<string> Prefixes { get; }

    public string BaseUrl => BaseUrls[0];

    public static bool IsPrivateIpv4(string? value)
    {
        return IPAddress.TryParse(value, out var address) &&
            address.AddressFamily == AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(address) &&
            IsPrivateIpv4(address);
    }

    private static string ValidatePrivateIpv4(string? value)
    {
        if (!IsPrivateIpv4(value))
        {
            throw new ArgumentException("LAN address must be a private IPv4 address.", nameof(value));
        }

        return IPAddress.Parse(value!).ToString();
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }
}
