using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using UsageMonitorMac.Core;
using UsageMonitorMac.Providers;

namespace UsageMonitorMac.Refresh;

public static class RefreshError
{
    public const string NoCli = "NoCli";
    public const string LoginExpired = "LoginExpired";
    public const string ProcessFailed = "ProcessFailed";
    public const string NoResponse = "NoResponse";
    public const string Network = "Network";
    public const string Canceled = "Canceled";
    public const string Other = "Other";
}

public sealed class RefreshResult
{
    public bool Success { get; init; }
    public string ErrorKind { get; init; } = "";
    public string Message { get; init; } = "";
    public string Output { get; init; } = "";
    public int DurationMs { get; init; }
    public string CommandLine { get; init; } = "";
}

public sealed class RefreshProfile
{
    public required UsageProviderState Account { get; init; }
    public required string Prompt { get; init; }
    public string? Model { get; init; }
    public required string WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}

public interface IRefreshAdapter
{
    string ProviderId { get; }
    bool CanExecute(UsageProviderState account, out string errorKind, out string reason);
    Task<RefreshResult> ExecuteRefresh(RefreshProfile profile, CancellationToken cancellationToken);
    bool VerifyExecution(RefreshResult result);
}

public sealed class RefreshRunner
{
    private static readonly IReadOnlyList<IRefreshAdapter> Adapters = [new ClaudeRefreshAdapter(), new CodexRefreshAdapter()];
    private readonly string _workspace;

    public RefreshRunner(string dataDirectory)
    {
        _workspace = Path.Combine(dataDirectory, "refresh-workspace");
        Directory.CreateDirectory(_workspace);
    }

    public static IRefreshAdapter? AdapterFor(string providerId) => Adapters.FirstOrDefault(a => a.ProviderId == providerId);

    public async Task<RefreshResult> RunAsync(UsageProviderState account, CancellationToken cancellationToken)
    {
        var adapter = AdapterFor(account.ProviderId);
        if (adapter == null) return new RefreshResult { ErrorKind = RefreshError.Other, Message = "No adapter" };
        if (!adapter.CanExecute(account, out var kind, out var reason)) return new RefreshResult { ErrorKind = kind, Message = reason };

        var settings = account.Refresh;
        var prompt = settings.PromptMode == "Custom" ? SanitizePrompt(settings.Prompt) : "hi";
        var profile = new RefreshProfile
        {
            Account = account,
            Prompt = string.IsNullOrWhiteSpace(prompt) ? "hi" : prompt,
            Model = settings.CostMode == "Custom" && !string.IsNullOrWhiteSpace(settings.Model) ? settings.Model.Trim() : null,
            WorkingDirectory = _workspace
        };
        var result = await adapter.ExecuteRefresh(profile, cancellationToken);
        if (result.Success && !adapter.VerifyExecution(result))
        {
            return new RefreshResult { ErrorKind = RefreshError.NoResponse, Message = "CLI exited without a model response", Output = result.Output, DurationMs = result.DurationMs, CommandLine = result.CommandLine };
        }
        return result;
    }

    public static string SanitizePrompt(string prompt)
    {
        var text = new string((prompt ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > 40 ? text[..40] : text;
    }

    internal static async Task<RefreshResult> RunProcess(string exe, IReadOnlyList<string> args, RefreshProfile profile, string? configEnv, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = profile.WorkingDirectory
        };

        ConfigureExecutable(info, exe, args);
        if (configEnv != null && !string.IsNullOrWhiteSpace(profile.Account.ConfigDirectory))
        {
            info.Environment[configEnv] = Environment.ExpandEnvironmentVariables(profile.Account.ConfigDirectory);
        }
        var commandLine = $"{Path.GetFileName(exe)} {string.Join(" ", args.Select(Quote))} < stdin(\"{profile.Prompt}\")";

        var dryRun = Environment.GetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN");
        if (!string.IsNullOrEmpty(dryRun))
        {
            await Task.Delay(50, cancellationToken);
            return dryRun == "fail"
                ? new RefreshResult { ErrorKind = RefreshError.Network, Message = "dry-run failure", CommandLine = commandLine }
                : new RefreshResult { Success = true, Message = "dry-run", Output = "{\"dry_run\":true}", CommandLine = commandLine };
        }

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new RefreshResult { ErrorKind = RefreshError.ProcessFailed, Message = ex.Message, CommandLine = commandLine };
        }

        await process.StandardInput.WriteAsync(profile.Prompt);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(profile.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new RefreshResult
            {
                ErrorKind = cancellationToken.IsCancellationRequested ? RefreshError.Canceled : RefreshError.NoResponse,
                Message = cancellationToken.IsCancellationRequested ? "Canceled" : $"No response within {profile.Timeout.TotalSeconds:0}s",
                DurationMs = (int)sw.ElapsedMilliseconds,
                CommandLine = commandLine
            };
        }

        var output = await stdout;
        var error = await stderr;
        var ok = process.ExitCode == 0;
        return new RefreshResult
        {
            Success = ok,
            ErrorKind = ok ? "" : Classify(output + "\n" + error),
            Message = ok ? "OK" : FirstLine(error, output, $"Exit code {process.ExitCode}"),
            Output = output,
            DurationMs = (int)sw.ElapsedMilliseconds,
            CommandLine = commandLine
        };
    }

    private static void ConfigureExecutable(ProcessStartInfo info, string exe, IReadOnlyList<string> args)
    {
        var ext = Path.GetExtension(exe).ToLowerInvariant();
        if (OperatingSystem.IsWindows() && (ext == ".cmd" || ext == ".bat"))
        {
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(exe);
        }
        else if (OperatingSystem.IsWindows() && ext == ".ps1")
        {
            info.FileName = "powershell.exe";
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", exe }) info.ArgumentList.Add(a);
        }
        else
        {
            info.FileName = exe;
        }
        foreach (var a in args) info.ArgumentList.Add(a);
    }

    private static string Classify(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("login") || t.Contains("log in") || t.Contains("unauthorized") || t.Contains("401") || t.Contains("oauth") || t.Contains("expired") || t.Contains("authenticat")) return RefreshError.LoginExpired;
        if (t.Contains("network") || t.Contains("enotfound") || t.Contains("econn") || t.Contains("timed out") || t.Contains("timeout") || t.Contains("getaddrinfo") || t.Contains("connection")) return RefreshError.Network;
        return RefreshError.Other;
    }

    private static string FirstLine(params string[] candidates)
    {
        foreach (var c in candidates)
        {
            var line = (c ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            if (line != null) return line.Length > 160 ? line[..160] : line;
        }
        return "";
    }

    private static string Quote(string value) => value.Contains(' ') || value.Length == 0 ? $"\"{value}\"" : value;
}

public sealed class ClaudeRefreshAdapter : IRefreshAdapter
{
    private const string CheapestModel = "haiku";

    public string ProviderId => "claude";

    public bool CanExecute(UsageProviderState account, out string errorKind, out string reason)
    {
        errorKind = "";
        reason = "";
        if (LoginHelper.FindCli("claude") == null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN")))
        {
            errorKind = RefreshError.NoCli;
            reason = "Claude Code CLI not found";
            return false;
        }
        if (account.Status == "NOT_SIGNED_IN")
        {
            errorKind = RefreshError.LoginExpired;
            reason = "Not signed in";
            return false;
        }
        return true;
    }

    public async Task<RefreshResult> ExecuteRefresh(RefreshProfile profile, CancellationToken cancellationToken)
    {
        var exe = LoginHelper.FindCli("claude") ?? "claude";
        var args = new List<string>
        {
            "-p",
            "--safe-mode",
            "--strict-mcp-config",
            "--tools", "",
            "--disable-slash-commands",
            "--no-session-persistence",
            "--effort", "low",
            "--output-format", "json",
            "--model", profile.Model ?? CheapestModel
        };
        var result = await RefreshRunner.RunProcess(exe, args, profile, "CLAUDE_CONFIG_DIR", cancellationToken);
        if (!result.Success && profile.Model == null && result.Message.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            args.RemoveRange(args.Count - 2, 2);
            result = await RefreshRunner.RunProcess(exe, args, profile, "CLAUDE_CONFIG_DIR", cancellationToken);
        }
        return result;
    }

    public bool VerifyExecution(RefreshResult result)
    {
        if (result.Output.Contains("\"dry_run\"")) return true;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
            var root = doc.RootElement;
            var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.True;
            return !isError && root.TryGetProperty("result", out _);
        }
        catch (System.Text.Json.JsonException)
        {
            return !string.IsNullOrWhiteSpace(result.Output);
        }
    }
}

public sealed class CodexRefreshAdapter : IRefreshAdapter
{
    public string ProviderId => "codex";

    public bool CanExecute(UsageProviderState account, out string errorKind, out string reason)
    {
        errorKind = "";
        reason = "";
        if (LoginHelper.FindCli("codex") == null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN")))
        {
            errorKind = RefreshError.NoCli;
            reason = "Codex CLI not found";
            return false;
        }
        if (account.Status == "NOT_SIGNED_IN")
        {
            errorKind = RefreshError.LoginExpired;
            reason = "Not signed in";
            return false;
        }
        return true;
    }

    public Task<RefreshResult> ExecuteRefresh(RefreshProfile profile, CancellationToken cancellationToken)
    {
        var exe = LoginHelper.FindCli("codex") ?? "codex";
        var args = new List<string>
        {
            "exec",
            "--ephemeral",
            "--ignore-user-config",
            "--skip-git-repo-check",
            "--sandbox", "read-only",
            "-C", profile.WorkingDirectory,
            "-c", "model_reasoning_effort=low",
            "--color", "never",
            "--json"
        };
        if (profile.Model != null)
        {
            args.Add("-m");
            args.Add(profile.Model);
        }
        args.Add("-");
        return RefreshRunner.RunProcess(exe, args, profile, "CODEX_HOME", cancellationToken);
    }

    public bool VerifyExecution(RefreshResult result) =>
        result.Output.Contains("\"dry_run\"") || result.Output.Contains("turn.completed") || result.Output.Contains("agent_message") || result.Output.Contains("\"type\"");
}
