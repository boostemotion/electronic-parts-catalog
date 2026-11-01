using ComponentCatalogCore.Transport;

namespace ComponentCatalogWinUI.Services;

/// <summary>
/// Persisted LAN/phone-access settings. Schema v1 stored only the toggle and address;
/// schema v2 adds configurable API and discovery ports with backward-compatible defaults.
/// </summary>
public sealed record CatalogLanSettings
{
    public const int CurrentSchemaVersion = 2;
    public const int OldestSupportedSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool LanEnabled { get; init; }

    public string? SelectedAddress { get; init; }

    public int ApiPort { get; init; } = CatalogNetworkPorts.DefaultApiPort;

    public int DiscoveryPort { get; init; } = CatalogNetworkPorts.DefaultDiscoveryPort;

    public static CatalogLanSettings Disabled { get; } = new();
}
