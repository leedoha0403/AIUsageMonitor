using System.Diagnostics;
using UsageMonitorMac.Core;

namespace UsageMonitorMac.Providers;

public static class LoginHelper
{
    private sealed record CliInfo(string Name, string LoginArgs, string ConfigEnv, string CredentialFolder, string CredentialFile);

    private static readonly Dictionary<string, CliInfo> Clis = new()
    {
        ["claude"] = new("claude", "auth login", "CLAUDE_CONFIG_DIR", ".claude", ".credentials.json"),
        ["codex"] = new("codex", "login", "CODEX_HOME", ".codex", "auth.json")
    };

    public static string? FindCli(string providerId)
    {
        if (!Clis.TryGetValue(providerId, out var cli)) return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var names = OperatingSystem.IsWindows()
            ? new[] { cli.Name + ".exe", cli.Name + ".cmd", cli.Name + ".ps1", cli.Name }
            : new[] { cli.Name };

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static string CredentialPath(UsageProviderState account)
    {
        var cli = Clis[account.ProviderId];
        if (!string.IsNullOrWhiteSpace(account.ConfigDirectory))
        {
            return Path.Combine(Environment.ExpandEnvironmentVariables(account.ConfigDirectory), cli.CredentialFile);
        }
        var env = Environment.GetEnvironmentVariable(cli.ConfigEnv);
        var root = !string.IsNullOrWhiteSpace(env)
            ? env
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cli.CredentialFolder);
        return Path.Combine(root, cli.CredentialFile);
    }

    public static bool LaunchLogin(UsageProviderState account)
    {
        var cli = Clis[account.ProviderId];
        var exe = FindCli(account.ProviderId);
        if (exe == null) return false;

        var shell = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/zsh";
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add($"& '{exe.Replace("'", "''")}' {cli.LoginArgs}");
        }
        else
        {
            info.ArgumentList.Add("-lc");
            info.ArgumentList.Add($"'{exe.Replace("'", "'\\''")}' {cli.LoginArgs}");
        }
        if (!string.IsNullOrWhiteSpace(account.ConfigDirectory)) info.Environment[cli.ConfigEnv] = Environment.ExpandEnvironmentVariables(account.ConfigDirectory);
        Process.Start(info);
        return true;
    }
}
