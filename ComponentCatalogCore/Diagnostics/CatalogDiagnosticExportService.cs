using System.Text;

namespace ComponentCatalogCore.Diagnostics;

public sealed record CatalogDiagnosticExportOptions(string ExportDirectory, string LogDirectory);

public sealed record CatalogDiagnosticReport(
    string ProductVersion,
    string TaxonomyVersion,
    string LocalApiEndpoint,
    string LanApiEndpoint,
    string ApiState,
    string DiscoveryState,
    string AndroidReleaseState,
    string BackupState);

public sealed record CatalogDiagnosticExport(string ReportPath, string Content);

public sealed class CatalogDiagnosticExportService
{
    private const long MaximumLogBytesPerFile = 1_048_576;

    public async Task<CatalogDiagnosticExport> ExportAsync(
        CatalogDiagnosticExportOptions options,
        CatalogDiagnosticReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExportDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LogDirectory);

        var exportDirectory = Path.GetFullPath(options.ExportDirectory);
        var logDirectory = Path.GetFullPath(options.LogDirectory);
        Directory.CreateDirectory(exportDirectory);

        var content = await BuildContentAsync(logDirectory, report, cancellationToken);
        var fileName = $"diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.txt";
        var reportPath = Path.Combine(exportDirectory, fileName);
        await WriteAtomicallyAsync(reportPath, content, cancellationToken);
        return new CatalogDiagnosticExport(reportPath, content);
    }

    private static async Task<string> BuildContentAsync(
        string logDirectory,
        CatalogDiagnosticReport report,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Component Catalog diagnostics");
        builder.AppendLine($"Generated UTC: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Product version: {DiagnosticRedactor.Redact(report.ProductVersion)}");
        builder.AppendLine($"Taxonomy version: {DiagnosticRedactor.Redact(report.TaxonomyVersion)}");
        builder.AppendLine($"Local API: {DiagnosticRedactor.Redact(report.LocalApiEndpoint)}");
        builder.AppendLine($"LAN endpoint: {DiagnosticRedactor.Redact(report.LanApiEndpoint)}");
        builder.AppendLine($"API state: {DiagnosticRedactor.Redact(report.ApiState)}");
        builder.AppendLine($"Discovery state: {DiagnosticRedactor.Redact(report.DiscoveryState)}");
        builder.AppendLine($"Android release: {DiagnosticRedactor.Redact(report.AndroidReleaseState)}");
        builder.AppendLine($"Backup state: {DiagnosticRedactor.Redact(report.BackupState)}");

        if (!Directory.Exists(logDirectory))
        {
            return builder.ToString();
        }

        foreach (var logPath in Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(logPath);
            builder.AppendLine();
            builder.AppendLine($"--- {file.Name} ---");
            if (file.Length > MaximumLogBytesPerFile)
            {
                builder.AppendLine($"Log omitted because it exceeds {MaximumLogBytesPerFile:N0} bytes.");
                continue;
            }

            var log = await File.ReadAllTextAsync(logPath, cancellationToken);
            builder.AppendLine(DiagnosticRedactor.Redact(log));
        }

        return builder.ToString();
    }

    private static async Task WriteAtomicallyAsync(string reportPath, string content, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{reportPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, reportPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
