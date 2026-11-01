using ComponentCatalogCore.Diagnostics;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogDiagnosticsTests : IAsyncLifetime
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-diagnostics-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("Authorization: Bearer secret-value", "Authorization: Bearer <redacted>")]
    [InlineData("authorization=Bearer secret-value", "authorization=Bearer <redacted>")]
    [InlineData("apiToken=secret-value password: another-secret", "apiToken=<redacted> password: <redacted>")]
    [InlineData("keystorePassword=secret-value", "keystorePassword=<redacted>")]
    public void Redact_RemovesCredentialsButKeepsDiagnosticContext(string source, string expected)
    {
        Assert.Equal(expected, DiagnosticRedactor.Redact(source));
    }

    [Fact]
    public async Task ExportAsync_WritesRedactedDiagnosticsWithoutDatabaseOrSnapshot()
    {
        var logDirectory = Path.Combine(_workingDirectory, "Logs");
        var exportDirectory = Path.Combine(_workingDirectory, "Diagnostics");
        Directory.CreateDirectory(logDirectory);
        await File.WriteAllTextAsync(Path.Combine(logDirectory, "page.log"), "Authorization: Bearer log-secret\npassword=also-secret");
        await File.WriteAllTextAsync(Path.Combine(logDirectory, "catalog.db"), "live database must not be exported");
        await File.WriteAllTextAsync(Path.Combine(logDirectory, "catalog.json"), "live snapshot must not be exported");

        var report = new CatalogDiagnosticReport(
            "1.1",
            "1",
            "http://127.0.0.1:10851",
            "http://192.168.1.8:10851/mobile/snapshot",
            "running",
            "enabled",
            "hosted release available",
            "last backup valid");

        var export = await new CatalogDiagnosticExportService().ExportAsync(
            new CatalogDiagnosticExportOptions(exportDirectory, logDirectory),
            report);

        Assert.True(File.Exists(export.ReportPath));
        Assert.Contains("Product version: 1.1", export.Content);
        Assert.Contains("Authorization: Bearer <redacted>", export.Content);
        Assert.Contains("password=<redacted>", export.Content);
        Assert.DoesNotContain("log-secret", export.Content);
        Assert.DoesNotContain("also-secret", export.Content);
        Assert.DoesNotContain("live database", export.Content);
        Assert.DoesNotContain("live snapshot", export.Content);
        Assert.DoesNotContain(Directory.EnumerateFiles(exportDirectory), path => path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || path.EndsWith("catalog.json", StringComparison.OrdinalIgnoreCase));
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_workingDirectory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }
}
