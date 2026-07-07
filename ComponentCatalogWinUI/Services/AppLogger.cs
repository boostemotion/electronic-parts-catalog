namespace ComponentCatalogWinUI.Services;

public static class AppLogger
{
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComponentCatalogWinUI",
        "Logs");

    public static string StartupLogPath => Path.Combine(LogDirectory, "startup.log");

    public static string PageLogPath => Path.Combine(LogDirectory, "page.log");

    public static void WriteStartup(string message)
    {
        WriteLine(StartupLogPath, message);
    }

    public static void WritePage(string message)
    {
        WriteLine(PageLogPath, message);
    }

    private static void WriteLine(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never block app startup or normal UI operations.
        }
    }
}
