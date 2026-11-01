using ComponentCatalogCore.Diagnostics;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogLogFileWriterTests : IDisposable
{
    private readonly string _directory;

    public CatalogLogFileWriterTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"catalog-log-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AppendLine_AppendsWithoutRotationBelowLimit()
    {
        var path = Path.Combine(_directory, "startup.log");

        var rotated = CatalogLogFileWriter.AppendLine(path, "first", maxFileBytes: 1024);
        rotated = CatalogLogFileWriter.AppendLine(path, "second", maxFileBytes: 1024) || rotated;

        Assert.False(rotated);
        var lines = File.ReadAllLines(path);
        Assert.Equal(["first", "second"], lines);
    }

    [Fact]
    public void AppendLine_RotatesFileThatWouldExceedLimit()
    {
        var path = Path.Combine(_directory, "startup.log");
        CatalogLogFileWriter.AppendLine(path, new string('a', 40), maxFileBytes: 64);

        var rotated = CatalogLogFileWriter.AppendLine(path, new string('b', 40), maxFileBytes: 64);

        Assert.True(rotated);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists($"{path}.1"));
        Assert.Contains("b", File.ReadAllText(path));
        Assert.Contains("a", File.ReadAllText($"{path}.1"));
    }

    [Fact]
    public void AppendLine_KeepsOnlyConfiguredRetainedFiles()
    {
        var path = Path.Combine(_directory, "page.log");
        for (var index = 0; index < 10; index++)
        {
            CatalogLogFileWriter.AppendLine(path, $"line-{index}-{new string('x', 20)}", maxFileBytes: 48, retainedFileCount: 2);
        }

        var files = Directory.GetFiles(_directory, "page.log*");
        Assert.True(files.Length <= 3, $"Expected at most 3 log files, found {files.Length}.");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists($"{path}.1"));
        Assert.True(File.Exists($"{path}.2"));
        Assert.False(File.Exists($"{path}.3"));
        Assert.Contains("line-9", File.ReadAllText(path));
    }

    [Fact]
    public void AppendLine_TruncatesActiveFileWhenRetentionIsZero()
    {
        var path = Path.Combine(_directory, "page.log");
        CatalogLogFileWriter.AppendLine(path, new string('a', 40), maxFileBytes: 64);

        var rotated = CatalogLogFileWriter.AppendLine(path, new string('b', 40), maxFileBytes: 64, retainedFileCount: 0);

        Assert.True(rotated);
        Assert.Single(Directory.GetFiles(_directory, "page.log*"));
        Assert.Contains("b", File.ReadAllText(path));
    }

    [Fact]
    public void AppendLine_ValidatesArguments()
    {
        Assert.Throws<ArgumentException>(() => CatalogLogFileWriter.AppendLine(" ", "line"));
        Assert.Throws<ArgumentNullException>(() => CatalogLogFileWriter.AppendLine(Path.Combine(_directory, "x.log"), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CatalogLogFileWriter.AppendLine(Path.Combine(_directory, "x.log"), "line", maxFileBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CatalogLogFileWriter.AppendLine(Path.Combine(_directory, "x.log"), "line", retainedFileCount: -1));
    }
}
