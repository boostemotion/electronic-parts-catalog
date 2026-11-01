using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ComponentCatalogCore.Transport;

public sealed class MobileDiscoveryServer : IAsyncDisposable
{
    public const int MaximumDatagramBytes = 128;

    private readonly IPAddress _publishedAddress;
    private readonly int _apiPort;
    private readonly int _discoveryPort;
    private readonly byte[] _responseBytes;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Dictionary<IPAddress, DateTimeOffset> _lastResponseTimes = [];
    private readonly object _sync = new();
    private readonly UdpClient _client;
    private readonly Task _receiveLoop;
    private Task? _stopTask;

    private MobileDiscoveryServer(IPAddress publishedAddress, int apiPort, int discoveryPort)
    {
        _publishedAddress = publishedAddress;
        _apiPort = apiPort;
        _discoveryPort = discoveryPort;
        _responseBytes = Encoding.UTF8.GetBytes(MobileDiscoveryProtocol.CreateResponse(_publishedAddress.ToString(), _apiPort));

        // UDP broadcast datagrams arrive through the wildcard socket; the advertised HTTP address remains explicit.
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, _discoveryPort));
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public static Task<MobileDiscoveryServer> StartAsync(IPAddress publishedAddress, int apiPort, int discoveryPort = MobileDiscoveryProtocol.DefaultPort)
    {
        ArgumentNullException.ThrowIfNull(publishedAddress);
        if (publishedAddress.AddressFamily != AddressFamily.InterNetwork || !CatalogApiBindingOptions.IsPrivateIpv4(publishedAddress.ToString()))
        {
            throw new ArgumentException("Published address must be a private IPv4 address.", nameof(publishedAddress));
        }

        if (apiPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(apiPort), "API port must be between 1 and 65535.");
        }

        if (discoveryPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), "Discovery port must be between 1 and 65535.");
        }

        return Task.FromResult(new MobileDiscoveryServer(publishedAddress, apiPort, discoveryPort));
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(StopAsync());
    }

    private async Task StopCoreAsync()
    {
        _cancellation.Cancel();
        _client.Dispose();

        try
        {
            await _receiveLoop;
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _client.ReceiveAsync(_cancellation.Token);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (received.Buffer.Length > MaximumDatagramBytes ||
                !CatalogApiBindingOptions.IsPrivateIpv4(received.RemoteEndPoint.Address.ToString()) ||
                !MobileDiscoveryProtocol.IsDiscoveryRequest(received.Buffer) ||
                !CanRespondTo(received.RemoteEndPoint.Address))
            {
                continue;
            }

            try
            {
                await _client.SendAsync(_responseBytes, received.RemoteEndPoint, _cancellation.Token);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                // A transient network send failure must not stop the discovery listener.
            }
        }
    }

    private bool CanRespondTo(IPAddress remoteAddress)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastResponseTimes.TryGetValue(remoteAddress, out var lastResponse) && now - lastResponse < TimeSpan.FromMilliseconds(250))
            {
                return false;
            }

            _lastResponseTimes[remoteAddress] = now;
            foreach (var staleAddress in _lastResponseTimes.Where(entry => now - entry.Value > TimeSpan.FromMinutes(1)).Select(entry => entry.Key).ToArray())
            {
                _lastResponseTimes.Remove(staleAddress);
            }

            return true;
        }
    }
}
