using System.Diagnostics;
using System.IO;

namespace UsageMonitorWpf.Core;

// Replaces the running exe with a downloaded one. A running exe can't overwrite itself, so a temp copy of
// this exe is launched with --apply-update; it waits for the app to exit, swaps the file, and restarts it.
public static class SelfUpdater
{
    public const string ApplyArgument = "--apply-update";
    public const string ExeAssetName = "AIUsageMonitor.exe";

    public static string UpdateDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageMonitor", "update");

    public static bool CanReplaceInPlace()
    {
        var target = Environment.ProcessPath;
        if (string.IsNullOrEmpty(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var probe = target + ".write-test";
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Starts the helper. The caller must exit the app right after this returns true.
    public static bool LaunchHelper(string newExePath)
    {
        try
        {
            var target = Environment.ProcessPath!;
            var helper = Path.Combine(Path.GetTempPath(), $"AIUsageMonitor-updater-{Guid.NewGuid():N}.exe");
            File.Copy(target, helper, overwrite: true);
            var psi = new ProcessStartInfo(helper) { UseShellExecute = false };
            psi.ArgumentList.Add(ApplyArgument);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add(target);
            psi.ArgumentList.Add(newExePath);
            return Process.Start(psi) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Write("update helper launch failed: " + ex);
            return false;
        }
    }

    // Helper entry point (runs in the temp copy, before any UI). args: --apply-update <pid> <target exe> <new exe>
    public static void RunHelper(string[] args)
    {
        var target = args[2];
        var newExe = args[3];
        var backup = target + ".bak";
        AppLog.Write($"updater: replacing {target}");
        try
        {
            if (int.TryParse(args[1], out var pid))
            {
                try
                {
                    using var parent = Process.GetProcessById(pid);
                    if (!parent.WaitForExit(TimeSpan.FromSeconds(30)))
                    {
                        AppLog.Write("updater: app did not exit in time, aborting");
                        return;
                    }
                }
                catch (ArgumentException)
                {
                    // already exited
                }
            }

            Retry(() =>
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(target, backup);
            });

            try
            {
                Retry(() => File.Copy(newExe, target, overwrite: true));
            }
            catch
            {
                Retry(() =>
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(backup, target);
                });
                throw;
            }

            try { File.Delete(newExe); } catch { }
            AppLog.Write("updater: replaced, restarting");
        }
        catch (Exception ex)
        {
            AppLog.Write("updater failed (previous exe restored if possible): " + ex);
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write("updater: restart failed: " + ex);
        }
    }

    // Leftover from the last update; safe to delete once the new build is up.
    public static void CleanupBackup()
    {
        try
        {
            var target = Environment.ProcessPath;
            if (target is null) return;
            var backup = target + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
        }
        catch
        {
        }
    }

    // The exe may stay locked for a moment after the process exits (antivirus, handle teardown).
    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(300);
            }
        }
    }
}
