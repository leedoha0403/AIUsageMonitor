namespace UsageMonitorMac.Providers;

public sealed record CredentialCandidate(string Label, string Path);

public static class CredentialLocator
{
    public static List<CredentialCandidate> Candidates(string configDirectory, string envVar, string folderName, string fileName, bool includeDeepLocalSearch)
    {
        var list = new List<CredentialCandidate>();
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            list.Add(new("Custom Path", Path.Combine(Environment.ExpandEnvironmentVariables(configDirectory), fileName)));
            return list;
        }

        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env)) list.Add(new(envVar, Path.Combine(env, fileName)));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        list.Add(new(DefaultCliLabel(), Path.Combine(home, folderName, fileName)));

        if (includeDeepLocalSearch && OperatingSystem.IsMacOS())
        {
            var appSupport = Path.Combine(home, "Library", "Application Support", folderName.TrimStart('.'), fileName);
            list.Add(new("macOS Application Support", appSupport));
        }
        return list;
    }

    public static CredentialCandidate? FindFirst(IEnumerable<CredentialCandidate> candidates) =>
        candidates.FirstOrDefault(c => SafeExists(c.Path));

    private static string DefaultCliLabel()
    {
        if (OperatingSystem.IsMacOS()) return "macOS CLI";
        if (OperatingSystem.IsLinux()) return "Linux CLI";
        return "Windows CLI";
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
