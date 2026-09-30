namespace AIUsage.Core;

// Appends to %APPDATA%\UsageMonitorWpf\startup.log. Never throws: logging must not be what crashes the app.
public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "UsageMonitorWpf");
            System.IO.Directory.CreateDirectory(dir);
            lock (Gate)
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "startup.log"),
                    $"[{System.DateTimeOffset.Now:O}] {message}{System.Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
