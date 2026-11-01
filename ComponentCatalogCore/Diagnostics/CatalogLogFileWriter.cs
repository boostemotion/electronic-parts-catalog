using System.Text;

namespace ComponentCatalogCore.Diagnostics;

/// <summary>
/// Small append-only log writer with size based rotation.
/// Keeps the active file at <paramref name="path"/> and retained history files at
/// <c>{path}.1</c> (newest) through <c>{path}.N</c> (oldest).
/// </summary>
public static class CatalogLogFileWriter
{
    public const long DefaultMaxFileBytes = 2 * 1024 * 1024;
    public const int DefaultRetainedFileCount = 3;

    public static bool AppendLine(
        string path,
        string line,
        long maxFileBytes = DefaultMaxFileBytes,
        int retainedFileCount = DefaultRetainedFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(line);
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes), "Maximum log file size must be positive.");
        }
        if (retainedFileCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFileCount), "Retained log file count cannot be negative.");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = line + Environment.NewLine;
        var rotated = false;
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Exists && fileInfo.Length > 0 &&
            fileInfo.Length + Encoding.UTF8.GetByteCount(payload) > maxFileBytes)
        {
            Rotate(fullPath, retainedFileCount);
            rotated = true;
        }

        File.AppendAllText(fullPath, payload, Encoding.UTF8);
        return rotated;
    }

    private static void Rotate(string path, int retainedFileCount)
    {
        if (retainedFileCount == 0)
        {
            File.Delete(path);
            return;
        }

        for (var index = retainedFileCount - 1; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            var destination = $"{path}.{index + 1}";
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        var firstRetained = $"{path}.1";
        if (File.Exists(firstRetained))
        {
            File.Delete(firstRetained);
        }
        File.Move(path, firstRetained);
    }
}
