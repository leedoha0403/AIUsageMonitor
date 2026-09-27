using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UsageMonitorMac.Core;
using UsageMonitorMac.Providers;
using UsageMonitorMac.Refresh;
using UsageMonitorMac.Storage;

namespace UsageMonitorMac.Web;

public sealed class LocalDashboardServer
{
    private readonly StateStore _store;
    private readonly UsageAggregator _aggregator;
    private readonly int _port;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public LocalDashboardServer(StateStore store, UsageAggregator aggregator, int port)
    {
        _store = store;
        _aggregator = aggregator;
        _port = port;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        Console.WriteLine($"Usage Monitor Mac UI: http://127.0.0.1:{_port}/");
        Console.WriteLine("Press Ctrl+C to stop.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var request = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request)) return;

        var parts = request.Split(' ');
        var method = parts.Length > 0 ? parts[0] : "GET";
        var target = parts.Length > 1 ? parts[1] : "/";
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 })
        {
        }

        try
        {
            if (method == "GET" && target == "/")
            {
                await WriteAsync(stream, "200 OK", "text/html; charset=utf-8", Page(), cancellationToken);
            }
            else if (method == "GET" && target.StartsWith("/api/state", StringComparison.Ordinal))
            {
                var state = await LoadFreshStateAsync(cancellationToken);
                await WriteJsonAsync(stream, ProjectState(state), cancellationToken);
            }
            else if (method == "POST" && target.StartsWith("/api/refresh-dry-run", StringComparison.Ordinal))
            {
                var provider = Query(target, "provider") ?? "codex";
                var previous = Environment.GetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN");
                Environment.SetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN", "ok");
                try
                {
                    var state = _store.LoadState();
                    var account = state.Providers.Values.FirstOrDefault(p => p.ProviderId == provider) ?? state.Providers.Values.First();
                    var result = await new RefreshRunner(_store.DataDirectory).RunAsync(account, cancellationToken);
                    await WriteJsonAsync(stream, result, cancellationToken);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN", previous);
                }
            }
            else
            {
                await WriteAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await WriteAsync(stream, "500 Internal Server Error", "text/plain; charset=utf-8", ex.Message, cancellationToken);
        }
    }

    private async Task<AppState> LoadFreshStateAsync(CancellationToken cancellationToken)
    {
        var state = _store.LoadState();
        await _aggregator.RefreshAsync(state, cancellationToken);
        _store.SaveState(state);
        _store.AppendHistory(state);
        return state;
    }

    private object ProjectState(AppState state) => new
    {
        state.UpdatedAt,
        _store.DataDirectory,
        Providers = state.Providers.Values
            .Where(p => p.Enabled)
            .OrderBy(p => p.ProviderId)
            .Select(p => new
            {
                p.ProviderId,
                p.DisplayName,
                p.AccountName,
                p.Status,
                p.Source,
                p.Confidence,
                p.Plan,
                p.Message,
                p.SessionUsagePercent,
                SessionResetAt = Formatters.LocalTime(p.SessionResetAt),
                SessionResetIn = Formatters.ShortCountdown(p.SessionResetAt),
                p.WeeklyUsagePercent,
                WeeklyResetAt = Formatters.LocalTime(p.WeeklyResetAt),
                WeeklyResetIn = Formatters.ShortCountdown(p.WeeklyResetAt),
                p.CreditsBalance,
                Collectors = p.Collectors.Select(c => new
                {
                    c.Name,
                    c.Kind,
                    c.Level,
                    c.Status,
                    c.TokenFreeVerified,
                    c.LatencyMs,
                    c.Message,
                    c.Detail
                })
            }),
        History = _store.LoadHistory().TakeLast(80).Select(h => new
        {
            h.Timestamp,
            h.Provider,
            h.Account,
            h.SessionUsagePercent,
            h.WeeklyUsagePercent,
            h.Source
        })
    };

    private async Task WriteJsonAsync(Stream stream, object payload, CancellationToken cancellationToken)
    {
        await WriteAsync(stream, "200 OK", "application/json; charset=utf-8", JsonSerializer.Serialize(payload, _json), cancellationToken);
    }

    private static async Task WriteAsync(Stream stream, string status, string contentType, string body, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static string? Query(string target, string key)
    {
        var question = target.IndexOf('?');
        if (question < 0) return null;
        foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == key) return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static string Page() => """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Usage Monitor Mac</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #101114;
      --panel: #181a1f;
      --line: #2a2e36;
      --text: #f3f5f7;
      --muted: #9ea7b3;
      --green: #34c38f;
      --yellow: #e0b84d;
      --red: #ef6b73;
      --blue: #68a7ff;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font: 14px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      background: var(--bg);
      color: var(--text);
    }
    header {
      position: sticky;
      top: 0;
      z-index: 2;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 16px 22px;
      border-bottom: 1px solid var(--line);
      background: color-mix(in srgb, var(--bg) 92%, black);
    }
    h1 { margin: 0; font-size: 18px; font-weight: 700; }
    button {
      border: 1px solid var(--line);
      border-radius: 7px;
      background: #222733;
      color: var(--text);
      padding: 8px 12px;
      font: inherit;
      cursor: pointer;
    }
    button:hover { border-color: var(--blue); }
    main { max-width: 1180px; margin: 0 auto; padding: 22px; }
    .meta { color: var(--muted); font-size: 12px; }
    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; }
    .card {
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--panel);
      padding: 16px;
    }
    .card-head { display: flex; justify-content: space-between; gap: 12px; align-items: start; }
    .provider { font-size: 17px; font-weight: 700; }
    .pill {
      border: 1px solid var(--line);
      border-radius: 999px;
      padding: 4px 9px;
      color: var(--muted);
      font-size: 12px;
      white-space: nowrap;
    }
    .metric { margin-top: 16px; }
    .metric-row { display: flex; justify-content: space-between; margin-bottom: 7px; color: var(--muted); }
    .bar { height: 9px; border-radius: 999px; background: #0d0f12; overflow: hidden; border: 1px solid var(--line); }
    .fill { height: 100%; width: 0; background: var(--green); transition: width 200ms ease; }
    .fill.notice { background: var(--yellow); }
    .fill.critical { background: var(--red); }
    .note { margin-top: 12px; color: var(--muted); min-height: 20px; }
    table { width: 100%; border-collapse: collapse; margin-top: 12px; }
    th, td { text-align: left; border-bottom: 1px solid var(--line); padding: 8px 6px; font-size: 12px; }
    th { color: var(--muted); font-weight: 600; }
    .wide { margin-top: 14px; }
    .history { display: grid; grid-template-columns: repeat(auto-fill, minmax(7px, 1fr)); gap: 3px; align-items: end; height: 80px; }
    .tick { background: var(--blue); min-height: 2px; border-radius: 2px 2px 0 0; opacity: .85; }
    @media (max-width: 760px) {
      header { align-items: stretch; flex-direction: column; }
      .grid { grid-template-columns: 1fr; }
      main { padding: 14px; }
    }
  </style>
</head>
<body>
  <header>
    <div>
      <h1>Usage Monitor Mac</h1>
      <div class="meta" id="meta">Loading...</div>
    </div>
    <div>
      <button id="reload">Refresh</button>
      <button id="dryrun">Dry-run Codex</button>
    </div>
  </header>
  <main>
    <section class="grid" id="providers"></section>
    <section class="card wide">
      <div class="card-head">
        <div>
          <div class="provider">History</div>
          <div class="meta">Recent 5H usage samples</div>
        </div>
      </div>
      <div class="history" id="history"></div>
    </section>
  </main>
  <script>
    const providers = document.querySelector('#providers');
    const history = document.querySelector('#history');
    const meta = document.querySelector('#meta');
    const reload = document.querySelector('#reload');
    const dryrun = document.querySelector('#dryrun');

    function cls(percent) {
      if (percent >= 95) return 'critical';
      if (percent >= 70) return 'notice';
      return '';
    }

    function metric(label, percent, reset) {
      return `<div class="metric">
        <div class="metric-row"><span>${label}</span><strong>${percent}%</strong></div>
        <div class="bar"><div class="fill ${cls(percent)}" style="width:${percent}%"></div></div>
        <div class="meta" style="margin-top:6px">resets in ${reset}</div>
      </div>`;
    }

    function render(data) {
      meta.textContent = `Updated ${new Date(data.UpdatedAt).toLocaleString()} · ${data.DataDirectory}`;
      providers.innerHTML = data.Providers.map(p => `
        <article class="card">
          <div class="card-head">
            <div>
              <div class="provider">${p.DisplayName}</div>
              <div class="meta">${p.AccountName} · ${p.Plan || 'Unknown plan'}</div>
            </div>
            <div class="pill">${p.Status} · ${p.Source}</div>
          </div>
          ${metric('5H window', p.SessionUsagePercent, p.SessionResetIn)}
          ${metric('Weekly window', p.WeeklyUsagePercent, p.WeeklyResetIn)}
          <div class="note">${p.Message || ''}</div>
          <table>
            <thead><tr><th>Collector</th><th>Status</th><th>Latency</th><th>Message</th></tr></thead>
            <tbody>${p.Collectors.map(c => `<tr><td>${c.Name}</td><td>${c.Status}</td><td>${c.LatencyMs ?? ''}</td><td>${c.Message || ''}</td></tr>`).join('')}</tbody>
          </table>
        </article>
      `).join('');
      history.innerHTML = data.History.map(h => `<div class="tick" title="${h.Provider} ${h.SessionUsagePercent}%" style="height:${Math.max(2, h.SessionUsagePercent)}%"></div>`).join('');
    }

    async function load() {
      reload.disabled = true;
      const response = await fetch('/api/state');
      render(await response.json());
      reload.disabled = false;
    }

    reload.addEventListener('click', load);
    dryrun.addEventListener('click', async () => {
      dryrun.disabled = true;
      const response = await fetch('/api/refresh-dry-run?provider=codex', { method: 'POST' });
      const result = await response.json();
      alert(result.Success ? `Dry-run OK\n${result.CommandLine}` : `Dry-run failed\n${result.Message}`);
      dryrun.disabled = false;
    });
    load();
    setInterval(load, 60000);
  </script>
</body>
</html>
""";
}
