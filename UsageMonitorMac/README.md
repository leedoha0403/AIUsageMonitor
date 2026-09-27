# Usage Monitor Mac

This is a separate mac-compatible preview project for the existing Windows WPF app.

It intentionally has no UI package dependency yet. The project includes a local browser dashboard plus the cross-platform core:

- Claude Code and Codex usage collectors
- macOS-friendly credential lookup (`~/.claude`, `~/.codex`, and env overrides)
- JSON state and 30-day JSONL history
- scheduled-refresh process runner with dry-run testing
- local dashboard at `http://127.0.0.1:5279/`
- smoke-test command for CI and local verification

## Run

```bash
dotnet run --project UsageMonitorMac -- --once
dotnet run --project UsageMonitorMac -- --ui
```

Open `http://127.0.0.1:5279/` after starting `--ui`.

## Test

```bash
dotnet build UsageMonitorMac/UsageMonitorMac.csproj
dotnet run --project UsageMonitorMac -- --self-test
dotnet run --project UsageMonitorMac -- --refresh-dry-run --provider codex
```

Use an isolated data directory when testing:

```bash
USAGE_MONITOR_DATA_DIR=/tmp/usage-monitor-mac-test dotnet run --project UsageMonitorMac -- --once
```

## Next UI Step

The intended desktop shell is Avalonia or a native macOS menu-bar app. Keep this project as the core executable and add a UI project that references/migrates this code after the mac behavior is validated.
