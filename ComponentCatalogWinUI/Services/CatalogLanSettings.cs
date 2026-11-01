namespace ComponentCatalogWinUI.Services;

public sealed record CatalogLanSettings(int SchemaVersion, bool LanEnabled, string? SelectedAddress)
{
    public const int CurrentSchemaVersion = 1;

    public static CatalogLanSettings Disabled { get; } = new(CurrentSchemaVersion, false, null);
}
