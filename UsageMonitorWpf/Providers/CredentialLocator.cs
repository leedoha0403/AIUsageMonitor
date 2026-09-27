using System.IO;
using Microsoft.Win32;

namespace UsageMonitorWpf.Providers;

public sealed record CredentialCandidate(string Label, string Path);

// Finds CLI login files on this machine. Only paths are returned here; contents are read by the collector
// and never written to state, history or logs.
public static class CredentialLocator
{
    private static readonly object WslLock = new();
    private static (DateTimeOffset At, List<string> Homes)? _wslHomes;

    public static List<CredentialCandidate> Candidates(string configDirectory, string envVar, string folderName, string fileName, bool includeWsl)
    {
        var list = new List<CredentialCandidate>();
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            list.Add(new("Custom Path", System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(configDirectory), fileName)));
            return list;
        }

        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env)) list.Add(new($"{envVar}", System.IO.Path.Combine(env, fileName)));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        list.Add(new("Windows CLI", System.IO.Path.Combine(home, folderName, fileName)));

        if (includeWsl)
        {
            foreach (var wslHome in WslHomes())
            {
                list.Add(new("WSL", System.IO.Path.Combine(wslHome, folderName, fileName)));
            }
        }
        return list;
    }

    public static CredentialCandidate? FindFirst(IEnumerable<CredentialCandidate> candidates) =>
        candidates.FirstOrDefault(c => SafeExists(c.Path));

    public static IReadOnlyList<string> WslDistributions()
    {
        var names = new List<string>();
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (lxss == null) return names;
            foreach (var sub in lxss.GetSubKeyNames())
            {
                using var key = lxss.OpenSubKey(sub);
                if (key?.GetValue("DistributionName") is string name && !name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
        }
        return names;
    }

    private static List<string> WslHomes()
    {
        lock (WslLock)
        {
            if (_wslHomes is { } cached && DateTimeOffset.Now - cached.At < TimeSpan.FromMinutes(10)) return cached.Homes;
        }

        var homes = new List<string>();
        foreach (var distro in WslDistributions())
        {
            // UNC access to a stopped distro can block; bound it so a refresh never hangs.
            var task = Task.Run(() =>
            {
                var result = new List<string>();
                var root = $@"\\wsl.localhost\{distro}";
                var homeRoot = System.IO.Path.Combine(root, "home");
                if (Directory.Exists(homeRoot)) result.AddRange(Directory.GetDirectories(homeRoot));
                var rootHome = System.IO.Path.Combine(root, "root");
                if (Directory.Exists(rootHome)) result.Add(rootHome);
                return result;
            });
            try
            {
                if (task.Wait(TimeSpan.FromSeconds(3))) homes.AddRange(task.Result);
            }
            catch
            {
            }
        }

        lock (WslLock)
        {
            _wslHomes = (DateTimeOffset.Now, homes);
        }
        return homes;
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
