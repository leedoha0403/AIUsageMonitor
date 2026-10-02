using System.Diagnostics;
using System.IO;
using AIUsage.Core;

namespace AIUsage.Core.Providers;

// Opens the provider's own CLI login (or installer) in a visible PowerShell window.
// The app never sees the login itself; it only waits for the CLI's credential file to appear.
public static class LoginHelper
{
    private sealed record CliInfo(string Name, string[] ExtraPaths, string LoginArgs, string InstallCommand, string ConfigEnv, string CredentialFolder, string CredentialFile);

    private static readonly Dictionary<string, CliInfo> Clis = new()
    {
        ["claude"] = new("claude",
            [@"%USERPROFILE%\.local\bin\claude.exe", @"%APPDATA%\npm\claude.cmd"],
            "auth login",
            "irm https://claude.ai/install.ps1 | iex",
            "CLAUDE_CONFIG_DIR", ".claude", ".credentials.json"),
        ["codex"] = new("codex",
            [@"%APPDATA%\npm\codex.cmd", @"%USERPROFILE%\.local\bin\codex.exe"],
            "login",
            "npm install -g @openai/codex",
            "CODEX_HOME", ".codex", "auth.json"),
        // The token itself goes to Credential Manager; config.json changes when the login completes.
        ["copilot"] = new("copilot",
            [@"%APPDATA%\npm\copilot.cmd"],
            "login",
            "npm install -g @github/copilot",
            "COPILOT_HOME", ".copilot", "config.json")
    };

    public static string? FindCli(string providerId)
    {
        if (!Clis.TryGetValue(providerId, out var cli)) return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in new[] { ".exe", ".cmd", ".ps1" })
            {
                var candidate = Path.Combine(dir.Trim(), cli.Name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return cli.ExtraPaths.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);
    }

    public static string CredentialPath(UsageProviderState account)
    {
        var cli = Clis[account.ProviderId];
        if (!string.IsNullOrWhiteSpace(account.ConfigDirectory))
        {
            return Path.Combine(Environment.ExpandEnvironmentVariables(account.ConfigDirectory), cli.CredentialFile);
        }
        var env = Environment.GetEnvironmentVariable(cli.ConfigEnv);
        var root = !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cli.CredentialFolder);
        return Path.Combine(root, cli.CredentialFile);
    }

    public static bool LaunchLogin(UsageProviderState account)
    {
        var cli = Clis[account.ProviderId];
        var exe = FindCli(account.ProviderId);
        if (exe == null) return false;
        var banner = Loc.T("login.banner", account.DisplayName, account.AccountName);
        var script = $"Write-Host '{Escape(banner)}' -ForegroundColor Cyan; & '{Escape(exe)}' {cli.LoginArgs}; Write-Host ''; Write-Host '{Escape(Loc.T("login.done"))}' -ForegroundColor Green";
        Start(script, account.ConfigDirectory, cli.ConfigEnv);
        return true;
    }

    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> LastRefresh = new();

    // Runs the CLI headlessly so it refreshes its own expired OAuth token (the app never touches the refresh token).
    // Throttled per config folder so a genuinely signed-out account does not spawn a process every refresh.
    public static async Task<bool> TryRefreshClaudeAsync(string configDirectory, CancellationToken cancellationToken)
    {
        var exe = FindCli("claude");
        if (exe == null) return false;
        var key = configDirectory ?? "";
        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            if (LastRefresh.TryGetValue(key, out var at) && DateTimeOffset.Now - at < TimeSpan.FromMinutes(5)) return false;
            LastRefresh[key] = DateTimeOffset.Now;

            var isCmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            if (exe.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) return false;
            // `claude auth status` does not always refresh the token; a minimal prompt run does.
            var info = new ProcessStartInfo(isCmd ? "cmd.exe" : exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            if (isCmd)
            {
                info.ArgumentList.Add("/c");
                info.ArgumentList.Add(exe);
            }
            info.ArgumentList.Add("-p");
            info.ArgumentList.Add(".");
            info.ArgumentList.Add("--no-session-persistence");
            if (!string.IsNullOrWhiteSpace(configDirectory)) info.Environment["CLAUDE_CONFIG_DIR"] = Environment.ExpandEnvironmentVariables(configDirectory);
            // Do not let a parent Claude Code session's identity leak into the refresh run.
            info.Environment.Remove("CLAUDECODE");
            info.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");
            using var process = Process.Start(info);
            if (process == null) return false;
            process.StandardInput.Close();
            // Output is never read; drain it so a full pipe cannot stall the CLI.
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return false;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    public static void LaunchInstall(string providerId)
    {
        var cli = Clis[providerId];
        var script = $"Write-Host '{Escape(Loc.T("login.installing", providerId))}' -ForegroundColor Cyan; {cli.InstallCommand}; Write-Host ''; Write-Host '{Escape(Loc.T("login.installDone"))}' -ForegroundColor Green";
        Start(script, "", cli.ConfigEnv);
    }

    public static string InstallCommand(string providerId) => Clis[providerId].InstallCommand;

    private static void Start(string script, string configDirectory, string configEnv)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-NoExit");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(script);
        // Extra accounts log in to their own config folder.
        if (!string.IsNullOrWhiteSpace(configDirectory)) info.Environment[configEnv] = Environment.ExpandEnvironmentVariables(configDirectory);
        Process.Start(info);
    }

    private static string Escape(string text) => text.Replace("'", "''");
}
