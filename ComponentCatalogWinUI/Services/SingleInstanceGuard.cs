using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ComponentCatalogWinUI.Services;

/// <summary>
/// Prevents two catalog processes from writing the same SQLite database/snapshot at once.
/// The second process exits after trying to bring the existing window to the foreground.
/// </summary>
public static class SingleInstanceGuard
{
    private const string MutexName = @"Local\ComponentCatalogWinUI.SingleInstance";
    private const int SwRestore = 9;
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return createdNew;
    }

    public static void ActivateExistingInstance()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var existing = Process
                .GetProcessesByName(current.ProcessName)
                .FirstOrDefault(process => process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero);
            if (existing is null)
            {
                return;
            }

            var handle = existing.MainWindowHandle;
            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
        }
        catch
        {
            // Activation is best effort; the new process still exits to protect the catalog.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
