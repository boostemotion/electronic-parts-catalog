using ComponentCatalogCore.Persistence;

namespace ComponentCatalogWinUI.Services;

public static class CatalogStoragePaths
{
    private const string AppDataFolderName = "ComponentCatalogWinUI";
    private const string CatalogDataFolderName = "CatalogData";
    private const string DatabaseFileName = "catalog.db";
    private const string SnapshotFileName = "catalog.json";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataFolderName,
        CatalogDataFolderName);

    public static CatalogRepositoryOptions CreateRepositoryOptions()
    {
        return new CatalogRepositoryOptions(
            Path.Combine(DataDirectory, DatabaseFileName),
            Path.Combine(DataDirectory, SnapshotFileName));
    }
}
