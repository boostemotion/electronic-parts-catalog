namespace ComponentCatalogCore.Storage;

public static class CatalogStoragePathResolver
{
    public static string ResolveRootDirectory(string localApplicationData, string? overrideRoot, string applicationFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationFolderName);
        return string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(localApplicationData, applicationFolderName)
            : Path.GetFullPath(overrideRoot.Trim());
    }

    public static IReadOnlyList<string> GetMigrationCandidateDirectories(string localApplicationData, string applicationFolderName, string legacyWorkspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationFolderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyWorkspaceRoot);
        return
        [
            Path.Combine(localApplicationData, applicationFolderName, "CatalogData"),
            Path.Combine(legacyWorkspaceRoot, "ComponentCatalogData"),
        ];
    }
}
