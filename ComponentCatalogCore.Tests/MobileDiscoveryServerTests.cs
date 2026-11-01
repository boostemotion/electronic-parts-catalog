using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ComponentCatalogCore.Transport;

namespace ComponentCatalogCore.Tests;

public sealed class MobileDiscoveryServerTests
{
    [Fact]
    public void Protocol_AcceptsOnlyTheExactV1RequestAndEmitsTheFixedReadOnlySchema()
    {
        Assert.True(MobileDiscoveryProtocol.IsDiscoveryRequest(Encoding.UTF8.GetBytes(MobileDiscoveryProtocol.RequestMagic)));
        Assert.False(MobileDiscoveryProtocol.IsDiscoveryRequest(Encoding.UTF8.GetBytes("COMPONENT_CATALOG_DISCOVER_V2")));
        Assert.False(MobileDiscoveryProtocol.IsDiscoveryRequest(Encoding.UTF8.GetBytes($"{MobileDiscoveryProtocol.RequestMagic}\n")));

        var response = MobileDiscoveryProtocol.CreateResponse("192.168.8.25", 10849);
        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.Equal("component-catalog", root.GetProperty("service").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("192.168.8.25", root.GetProperty("host").GetString());
        Assert.Equal(10849, root.GetProperty("port").GetInt32());
        Assert.Equal("/mobile/snapshot", root.GetProperty("path").GetString());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.DoesNotContain("token", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("catalog.db", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Server_IgnoresInvalidRequestsRespondsToV1AndReleasesItsPort()
    {
        var host = GetPrivateAddress();
        var port = GetAvailableUdpPort();
        await using var server = await MobileDiscoveryServer.StartAsync(IPAddress.Parse(host), 10849, port);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        client.EnableBroadcast = true;
        var target = new IPEndPoint(IPAddress.Parse(host), port);

        await client.SendAsync(Encoding.UTF8.GetBytes("not-a-discovery-request"), target);
        using (var invalidRequestTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.ReceiveAsync(invalidRequestTimeout.Token));
        }

        await client.SendAsync(Encoding.UTF8.GetBytes(MobileDiscoveryProtocol.RequestMagic), target);
        using var responseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var response = await client.ReceiveAsync(responseTimeout.Token);
        using var json = JsonDocument.Parse(response.Buffer);
        Assert.Equal(host, json.RootElement.GetProperty("host").GetString());
        Assert.True(json.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.DoesNotContain("token", Encoding.UTF8.GetString(response.Buffer), StringComparison.OrdinalIgnoreCase);

        await server.StopAsync();
        await server.StopAsync();
        using var replacement = new UdpClient(port);
    }

    private static string GetPrivateAddress()
    {
        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(unicastAddress => unicastAddress.Address.ToString())
            .FirstOrDefault(CatalogApiBindingOptions.IsPrivateIpv4);
        Assert.False(string.IsNullOrWhiteSpace(address));
        return address;
    }

    private static int GetAvailableUdpPort()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
    }
}
