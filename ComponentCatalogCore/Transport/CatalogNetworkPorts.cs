namespace ComponentCatalogCore.Transport;

/// <summary>
/// Shared validation for the configurable local host ports used by the management
/// API (HTTP) and the mobile discovery responder (UDP).
/// </summary>
public static class CatalogNetworkPorts
{
    public const int Minimum = 1;
    public const int Maximum = 65535;
    public const int DefaultApiPort = CatalogApiBindingOptions.DefaultPort;
    public const int DefaultDiscoveryPort = MobileDiscoveryProtocol.DefaultPort;

    public static bool IsValid(int port) => port is >= Minimum and <= Maximum;

    public static bool IsValidPair(int apiPort, int discoveryPort) =>
        IsValid(apiPort) && IsValid(discoveryPort) && apiPort != discoveryPort;

    public static void ValidatePair(int apiPort, int discoveryPort)
    {
        if (!IsValid(apiPort))
        {
            throw new ArgumentOutOfRangeException(nameof(apiPort), $"API port must be between {Minimum} and {Maximum}.");
        }
        if (!IsValid(discoveryPort))
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), $"Discovery port must be between {Minimum} and {Maximum}.");
        }
        if (apiPort == discoveryPort)
        {
            throw new ArgumentException("API port and discovery port must be different.");
        }
    }
}
