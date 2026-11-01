using System.Security.Cryptography;

namespace ComponentCatalogCore.Transport;

public sealed class AndroidAppRelease
{
    public const string ManifestPath = "/mobile/app-update";
    public const string ApkPath = "/mobile/app.apk";

    public AndroidAppRelease(
        string sourceApkPath,
        string packageName,
        int versionCode,
        string versionName,
        string? releaseNotes = null,
        int minimumSupportedVersionCode = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceApkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSupportedVersionCode);
        if (minimumSupportedVersionCode > versionCode)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSupportedVersionCode), "Minimum supported version cannot be newer than the release.");
        }

        SourceApkPath = Path.GetFullPath(sourceApkPath);
        PackageName = packageName;
        VersionCode = versionCode;
        VersionName = versionName;
        ReleaseNotes = releaseNotes;
        MinimumSupportedVersionCode = minimumSupportedVersionCode;
    }

    public string SourceApkPath { get; }

    public string PackageName { get; }

    public int VersionCode { get; }

    public string VersionName { get; }

    public string? ReleaseNotes { get; }

    public int MinimumSupportedVersionCode { get; }

    public async Task<AndroidAppReleaseManifest?> TryGetManifestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SourceApkPath))
        {
            return null;
        }

        await using var stream = new FileStream(SourceApkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return new AndroidAppReleaseManifest(
            PackageName,
            VersionCode,
            VersionName,
            ApkPath,
            stream.Length,
            sha256,
            ReleaseNotes,
            MinimumSupportedVersionCode);
    }
}

public sealed record AndroidAppReleaseManifest(
    string PackageName,
    int VersionCode,
    string VersionName,
    string ApkPath,
    long SizeBytes,
    string Sha256,
    string? ReleaseNotes,
    int MinimumSupportedVersionCode);
