param(
    [switch]$SelfTest
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

$script:AppName = "Usage Monitor"
$script:ProviderOrder = @("claude", "codex")
$script:ConfidenceOrder = @{ High = 3; Medium = 2; Low = 1 }

function New-CollectorInfo {
    param(
        [string]$Name,
        [string]$Kind,
        [string]$Status,
        [bool]$TokenFreeVerified,
        [string]$Verification,
        [string]$Message,
        $LatencyMs = $null
    )

    return @{
        name = $Name
        kind = $Kind
        status = $Status
        tokenFreeVerified = $TokenFreeVerified
        verification = $Verification
        latencyMs = $LatencyMs
        message = $Message
    }
}

function Apply-TokenFreeCollectorPolicy {
    param([hashtable]$Provider)

    foreach ($collector in $Provider.collectors) {
        if (-not $collector.ContainsKey("tokenFreeVerified")) {
            $collector.tokenFreeVerified = $false
        }
        if (-not $collector.ContainsKey("verification")) {
            $collector.verification = "Not verified as token-free"
        }
        if (-not $collector.ContainsKey("kind")) {
            $collector.kind = $collector.name
        }

        if (-not [bool]$collector.tokenFreeVerified) {
            $collector.status = "DISABLED"
            if ([string]::IsNullOrWhiteSpace([string]$collector.message) -or $collector.message -eq "Collector hook reserved") {
                $collector.message = "Disabled until command is verified as token-free"
            }
        }
    }
}

function Get-AppDirectory {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDir)) {
        $scriptDir = (Get-Location).Path
    }
    $candidates = @(
        (Join-Path $env:APPDATA "UsageMonitor"),
        (Join-Path $scriptDir ".usage-monitor")
    )

    foreach ($candidate in $candidates) {
        try {
            if (-not (Test-Path -LiteralPath $candidate)) {
                New-Item -ItemType Directory -Path $candidate | Out-Null
            }
            $probe = Join-Path $candidate ".write-test"
            "ok" | Set-Content -LiteralPath $probe -Encoding ASCII
            Remove-Item -LiteralPath $probe -Force
            return $candidate
        }
        catch {
        }
    }

    throw "No writable data directory is available."
}

$script:DataDir = Get-AppDirectory
$script:StatePath = Join-Path $script:DataDir "state.json"
$script:HistoryPath = Join-Path $script:DataDir "history.json"

function ConvertTo-Hashtable {
    param([Parameter(ValueFromPipeline = $true)]$InputObject)

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $hash = @{}
        foreach ($key in $InputObject.Keys) {
            $hash[$key] = ConvertTo-Hashtable $InputObject[$key]
        }
        return $hash
    }

    if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
        $hash = @{}
        foreach ($property in $InputObject.PSObject.Properties) {
            $hash[$property.Name] = ConvertTo-Hashtable $property.Value
        }
        return $hash
    }

    if (($InputObject -is [System.Collections.IEnumerable]) -and -not ($InputObject -is [string])) {
        $items = @()
        foreach ($item in $InputObject) {
            $items += ConvertTo-Hashtable $item
        }
        return $items
    }

    return $InputObject
}

function New-DefaultState {
    $now = Get-Date
    @{
        settings = @{
            widgetMode = "Normal"
            refreshSeconds = 60
            alwaysOnTop = $true
            opacity = 0.96
            collectionLevel = "Standard"
            allowUnverifiedCollectors = $false
            warnThreshold = 80
            showDataSource = $true
        }
        providers = @{
            claude = @{
                providerId = "claude"
                displayName = "Claude Code"
                accountName = "Personal"
                plan = "Manual"
                status = "READY"
                source = "Manual"
                confidence = "Low"
                sessionUsagePercent = 0
                sessionResetAt = $now.AddHours(5).ToString("o")
                weeklyUsagePercent = 0
                weeklyResetAt = $now.AddDays(3).ToString("o")
                monthlyUsagePercent = $null
                monthlyResetAt = $null
                creditsRemaining = $null
                collectedAt = $now.ToString("o")
                message = "Waiting for first manual snapshot"
                capabilities = @{
                    sessionUsage = $true
                    weeklyUsage = $true
                    credits = $false
                    account = $true
                }
                fieldSources = @{
                    sessionUsagePercent = @{ source = "Manual"; confidence = "Low" }
                    sessionResetAt = @{ source = "Manual"; confidence = "Low" }
                    weeklyUsagePercent = @{ source = "Manual"; confidence = "Low" }
                    weeklyResetAt = @{ source = "Manual"; confidence = "Low" }
                    plan = @{ source = "Manual"; confidence = "Low" }
                }
                collectors = @(
                    (New-CollectorInfo "Official" "official" "DISABLED" $false "No official token-free usage endpoint configured" "Disabled until official source is verified as token-free"),
                    (New-CollectorInfo "CLI" "cli" "DISABLED" $false "Claude Code usage lookup command not verified as token-free" "Disabled until command is verified as token-free"),
                    (New-CollectorInfo "Local" "local-cache" "SUCCESS" $true "Reads only this app's local manual snapshot cache" "Using local cache/manual snapshot" 1),
                    (New-CollectorInfo "Web" "authenticated-web" "DISABLED" $false "Authenticated usage page access not verified as token-free" "Deep collection disabled")
                )
            }
            codex = @{
                providerId = "codex"
                displayName = "Codex"
                accountName = "Personal"
                plan = "Manual"
                status = "READY"
                source = "Manual"
                confidence = "Low"
                sessionUsagePercent = 0
                sessionResetAt = $now.AddHours(5).ToString("o")
                weeklyUsagePercent = 0
                weeklyResetAt = $now.AddDays(7).ToString("o")
                monthlyUsagePercent = $null
                monthlyResetAt = $null
                creditsRemaining = $null
                collectedAt = $now.ToString("o")
                message = "Waiting for first manual snapshot"
                capabilities = @{
                    sessionUsage = $true
                    weeklyUsage = $true
                    credits = $true
                    account = $true
                }
                fieldSources = @{
                    sessionUsagePercent = @{ source = "Manual"; confidence = "Low" }
                    sessionResetAt = @{ source = "Manual"; confidence = "Low" }
                    weeklyUsagePercent = @{ source = "Manual"; confidence = "Low" }
                    weeklyResetAt = @{ source = "Manual"; confidence = "Low" }
                    plan = @{ source = "Manual"; confidence = "Low" }
                }
                collectors = @(
                    (New-CollectorInfo "Official" "official" "DISABLED" $false "Compliance/API usage lookup is not implemented in this MVP" "Disabled until official source is verified as token-free"),
                    (New-CollectorInfo "CLI" "cli" "DISABLED" $false "OpenAI docs mention /status, but this app has not verified it as token-free for automated polling" "Disabled until command is verified as token-free"),
                    (New-CollectorInfo "Local" "local-cache" "SUCCESS" $true "Reads only this app's local manual snapshot cache" "Using local cache/manual snapshot" 1),
                    (New-CollectorInfo "Web" "authenticated-web" "DISABLED" $false "Authenticated usage page access not verified as token-free" "Deep collection disabled")
                )
            }
        }
        updatedAt = $now.ToString("o")
        window = @{
            left = $null
            top = $null
        }
    }
}

function Read-State {
    if (-not (Test-Path -LiteralPath $script:StatePath)) {
        return New-DefaultState
    }

    try {
        $state = Get-Content -LiteralPath $script:StatePath -Raw | ConvertFrom-Json | ConvertTo-Hashtable
        $default = New-DefaultState
        foreach ($key in $script:ProviderOrder) {
            if (-not $state.providers[$key]) {
                $state.providers[$key] = $default.providers[$key]
            }
            Apply-TokenFreeCollectorPolicy $state.providers[$key]
        }
        foreach ($setting in $default.settings.Keys) {
            if (-not $state.settings.ContainsKey($setting)) {
                $state.settings[$setting] = $default.settings[$setting]
            }
        }
        return $state
    }
    catch {
        return New-DefaultState
    }
}

function Save-State {
    param([hashtable]$State)
    $State.updatedAt = (Get-Date).ToString("o")
    $State | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $script:StatePath -Encoding UTF8
}

function Read-History {
    if (-not (Test-Path -LiteralPath $script:HistoryPath)) {
        return @()
    }

    try {
        $rawItems = Get-Content -LiteralPath $script:HistoryPath -Raw | ConvertFrom-Json
        if ($null -eq $rawItems) {
            return @()
        }
        $items = @()
        foreach ($item in @($rawItems)) {
            $items += ConvertTo-Hashtable $item
        }
        return $items
    }
    catch {
        return @()
    }
}

function Append-History {
    param([hashtable]$State)
    $history = @(Read-History)
    foreach ($key in $script:ProviderOrder) {
        $p = $State.providers[$key]
        $history += @{
            timestamp = (Get-Date).ToString("o")
            provider = $p.providerId
            account = $p.accountName
            sessionUsagePercent = $p.sessionUsagePercent
            weeklyUsagePercent = $p.weeklyUsagePercent
            sessionResetAt = $p.sessionResetAt
            source = $p.source
            confidence = $p.confidence
        }
    }
    $cutoff = (Get-Date).AddDays(-30)
    $trimmed = @($history | Where-Object {
        try { ([DateTime]$_.timestamp) -ge $cutoff } catch { $false }
    } | Select-Object -Last 2000)
    $trimmed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $script:HistoryPath -Encoding UTF8
}

function Clamp-Percent {
    param($Value)
    if ($null -eq $Value) {
        return $null
    }
    return [Math]::Max(0, [Math]::Min(100, [int]$Value))
}

function Format-Countdown {
    param($IsoDate)
    if ([string]::IsNullOrWhiteSpace([string]$IsoDate)) {
        return "Unknown"
    }
    try {
        $target = [DateTime]::Parse([string]$IsoDate).ToLocalTime()
        $span = $target - (Get-Date)
        if ($span.TotalSeconds -le 0) {
            return "Reset pending"
        }
        if ($span.TotalDays -ge 1) {
            return "{0}d {1}h" -f [Math]::Floor($span.TotalDays), $span.Hours
        }
        return "{0:00}:{1:00}:{2:00}" -f [Math]::Floor($span.TotalHours), $span.Minutes, $span.Seconds
    }
    catch {
        return "Unknown"
    }
}

function Format-LocalTime {
    param($IsoDate)
    if ([string]::IsNullOrWhiteSpace([string]$IsoDate)) {
        return "Unknown"
    }
    try {
        return ([DateTime]::Parse([string]$IsoDate).ToLocalTime()).ToString("ddd HH:mm")
    }
    catch {
        return "Unknown"
    }
}

function Get-UsageState {
    param($Percent)
    $p = Clamp-Percent $Percent
    if ($null -eq $p) { return "Unknown" }
    if ($p -ge 95) { return "Critical" }
    if ($p -ge 85) { return "High" }
    if ($p -ge 70) { return "Notice" }
    return "Normal"
}

function Get-StateColor {
    param($Percent)
    $state = Get-UsageState $Percent
    switch ($state) {
        "Critical" { return [System.Drawing.Color]::FromArgb(197, 64, 73) }
        "High" { return [System.Drawing.Color]::FromArgb(211, 112, 43) }
        "Notice" { return [System.Drawing.Color]::FromArgb(195, 142, 36) }
        default { return [System.Drawing.Color]::FromArgb(31, 138, 112) }
    }
}

function Get-ResetState {
    param($IsoDate)
    try {
        $span = [DateTime]::Parse([string]$IsoDate).ToLocalTime() - (Get-Date)
        if ($span.TotalSeconds -le 0) { return "Reset pending" }
        if ($span.TotalMinutes -lt 15) { return "Reset soon" }
        if ($span.TotalHours -lt 1) { return "Attention" }
        return "Normal"
    }
    catch {
        return "Unknown"
    }
}

function Invoke-ProviderRefresh {
    param([hashtable]$State)

    # Phase 1 collector chain: only token-free verified collectors may run by default.
    # This prevents polling CLI/web commands until they are explicitly verified as
    # read-only and non-billing usage lookups for each provider.
    foreach ($key in $script:ProviderOrder) {
        $provider = $State.providers[$key]
        Apply-TokenFreeCollectorPolicy $provider
        $provider.status = "READY"
        $provider.source = "Local Cache"
        if ($provider.confidence -eq "Low") {
            $provider.source = "Manual"
        }
        $provider.collectedAt = (Get-Date).ToString("o")
        $provider.message = "Snapshot refreshed from local cache"
        foreach ($collector in $provider.collectors) {
            if ($collector.name -eq "Local" -and [bool]$collector.tokenFreeVerified) {
                $collector.status = "SUCCESS"
                $collector.latencyMs = 1
                $collector.message = "Local cache/manual snapshot"
            }
        }
    }

    Save-State $State
    Append-History $State
}

function New-Font {
    param([string]$Name, [float]$Size, [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Regular)
    return New-Object System.Drawing.Font -ArgumentList $Name, $Size, $Style
}

function New-Label {
    param([string]$Text, [int]$X, [int]$Y, [int]$W, [int]$H, [System.Drawing.Font]$Font = $null)
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Location = New-Object System.Drawing.Point -ArgumentList $X, $Y
    $label.Size = New-Object System.Drawing.Size -ArgumentList $W, $H
    if ($Font) { $label.Font = $Font }
    return $label
}

function New-Progress {
    param([int]$X, [int]$Y, [int]$W)
    $bar = New-Object System.Windows.Forms.ProgressBar
    $bar.Location = New-Object System.Drawing.Point -ArgumentList $X, $Y
    $bar.Size = New-Object System.Drawing.Size -ArgumentList $W, 10
    $bar.Minimum = 0
    $bar.Maximum = 100
    return $bar
}

function Build-ProviderRow {
    param(
        [System.Windows.Forms.Control]$Parent,
        [string]$Key,
        [int]$Top,
        [string]$Mode
    )

    $row = New-Object System.Windows.Forms.Panel
    $row.Location = New-Object System.Drawing.Point -ArgumentList 10, $Top
    $row.Size = New-Object System.Drawing.Size -ArgumentList 302, 92
    $row.BackColor = [System.Drawing.Color]::White
    $row.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
    $Parent.Controls.Add($row)

    $name = New-Label "" 10 8 122 22 (New-Font "Segoe UI Semibold" 10)
    $percent = New-Label "" 224 8 64 22 (New-Font "Segoe UI Semibold" 11)
    $percent.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight
    $sessionText = New-Label "5H" 10 36 34 18 (New-Font "Segoe UI" 8)
    $weeklyText = New-Label "Week" 10 58 38 18 (New-Font "Segoe UI" 8)
    $sessionBar = New-Progress 52 40 150
    $weeklyBar = New-Progress 52 62 150
    $reset = New-Label "" 210 36 80 18 (New-Font "Segoe UI" 8)
    $sync = New-Label "" 210 58 80 18 (New-Font "Segoe UI" 8)

    foreach ($control in @($name, $percent, $sessionText, $weeklyText, $sessionBar, $weeklyBar, $reset, $sync)) {
        $row.Controls.Add($control)
    }

    if ($Mode -eq "Compact") {
        $row.Size = New-Object System.Drawing.Size -ArgumentList 302, 44
        $sessionText.Visible = $false
        $weeklyText.Visible = $false
        $weeklyBar.Visible = $false
        $sync.Visible = $false
        $sessionBar.Location = New-Object System.Drawing.Point -ArgumentList 116, 18
        $sessionBar.Size = New-Object System.Drawing.Size -ArgumentList 82, 8
        $reset.Location = New-Object System.Drawing.Point -ArgumentList 202, 16
        $reset.Size = New-Object System.Drawing.Size -ArgumentList 84, 18
    }

    return @{
        panel = $row
        name = $name
        percent = $percent
        sessionBar = $sessionBar
        weeklyBar = $weeklyBar
        reset = $reset
        sync = $sync
    }
}

function Show-EditDialog {
    param([string]$Key)

    $p = $script:State.providers[$Key]
    $dialog = New-Object System.Windows.Forms.Form
    $dialog.Text = "Edit $($p.displayName)"
    $dialog.Size = New-Object System.Drawing.Size -ArgumentList 380, 390
    $dialog.StartPosition = "CenterParent"
    $dialog.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $dialog.MaximizeBox = $false
    $dialog.MinimizeBox = $false
    $dialog.BackColor = [System.Drawing.Color]::FromArgb(248, 249, 250)
    $dialog.Font = New-Font "Segoe UI" 9

    $labels = @("Account", "Plan", "5H usage %", "5H reset", "Weekly usage %", "Weekly reset", "Source", "Confidence")
    $inputs = @{}
    $top = 18
    foreach ($labelText in $labels) {
        $dialog.Controls.Add((New-Label $labelText 18 $top 120 24))
        if ($labelText -eq "Confidence") {
            $input = New-Object System.Windows.Forms.ComboBox
            $input.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
            [void]$input.Items.AddRange(@("High", "Medium", "Low"))
        }
        else {
            $input = New-Object System.Windows.Forms.TextBox
        }
        $input.Location = New-Object System.Drawing.Point -ArgumentList 150, $top
        $input.Size = New-Object System.Drawing.Size -ArgumentList 190, 24
        $dialog.Controls.Add($input)
        $inputs[$labelText] = $input
        $top += 36
    }

    $inputs["Account"].Text = $p.accountName
    $inputs["Plan"].Text = $p.plan
    $inputs["5H usage %"].Text = [string]$p.sessionUsagePercent
    $inputs["5H reset"].Text = ([DateTime]::Parse([string]$p.sessionResetAt).ToLocalTime()).ToString("yyyy-MM-dd HH:mm")
    $inputs["Weekly usage %"].Text = [string]$p.weeklyUsagePercent
    $inputs["Weekly reset"].Text = ([DateTime]::Parse([string]$p.weeklyResetAt).ToLocalTime()).ToString("yyyy-MM-dd HH:mm")
    $inputs["Source"].Text = $p.source
    $inputs["Confidence"].SelectedItem = $p.confidence
    if (-not $inputs["Confidence"].SelectedItem) { $inputs["Confidence"].SelectedItem = "Low" }

    $hint = New-Label "Reset format: yyyy-MM-dd HH:mm" 18 306 220 24 (New-Font "Segoe UI" 8)
    $hint.ForeColor = [System.Drawing.Color]::FromArgb(105, 113, 122)
    $dialog.Controls.Add($hint)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = "Save"
    $ok.Location = New-Object System.Drawing.Point -ArgumentList 184, 316
    $ok.Size = New-Object System.Drawing.Size -ArgumentList 74, 28
    $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $dialog.Controls.Add($ok)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = "Cancel"
    $cancel.Location = New-Object System.Drawing.Point -ArgumentList 266, 316
    $cancel.Size = New-Object System.Drawing.Size -ArgumentList 74, 28
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $dialog.Controls.Add($cancel)
    $dialog.AcceptButton = $ok
    $dialog.CancelButton = $cancel

    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        try {
            $p.accountName = $inputs["Account"].Text
            $p.plan = $inputs["Plan"].Text
            $p.sessionUsagePercent = Clamp-Percent ([int]$inputs["5H usage %"].Text)
            $p.weeklyUsagePercent = Clamp-Percent ([int]$inputs["Weekly usage %"].Text)
            $p.sessionResetAt = ([DateTime]::Parse($inputs["5H reset"].Text)).ToString("o")
            $p.weeklyResetAt = ([DateTime]::Parse($inputs["Weekly reset"].Text)).ToString("o")
            $p.source = $inputs["Source"].Text
            $p.confidence = [string]$inputs["Confidence"].SelectedItem
            $p.collectedAt = (Get-Date).ToString("o")
            $p.status = "READY"
            $p.message = "Manual snapshot saved"
            foreach ($field in @("sessionUsagePercent", "sessionResetAt", "weeklyUsagePercent", "weeklyResetAt", "plan")) {
                $p.fieldSources[$field] = @{ source = $p.source; confidence = $p.confidence }
            }
            Save-State $script:State
            Append-History $script:State
            Update-AllViews
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show("Could not save. Check percent values and reset date format.", $script:AppName) | Out-Null
        }
    }
}

function Open-Dashboard {
    if ($script:DashboardForm -and -not $script:DashboardForm.IsDisposed) {
        $script:DashboardForm.Activate()
        return
    }

    $form = New-Object System.Windows.Forms.Form
    $script:DashboardForm = $form
    $form.Text = "Usage Monitor Dashboard"
    $form.Size = New-Object System.Drawing.Size -ArgumentList 720, 560
    $form.MinimumSize = New-Object System.Drawing.Size -ArgumentList 640, 500
    $form.StartPosition = "CenterScreen"
    $form.BackColor = [System.Drawing.Color]::FromArgb(246, 247, 249)
    $form.Font = New-Font "Segoe UI" 9

    $tabs = New-Object System.Windows.Forms.TabControl
    $tabs.Dock = [System.Windows.Forms.DockStyle]::Fill
    $form.Controls.Add($tabs)

    foreach ($title in @("Dashboard", "Settings", "Diagnostics", "History")) {
        $page = New-Object System.Windows.Forms.TabPage
        $page.Text = $title
        $page.BackColor = [System.Drawing.Color]::FromArgb(246, 247, 249)
        [void]$tabs.TabPages.Add($page)
        $script:DashboardPages[$title] = $page
    }

    Build-DashboardPage
    Build-SettingsPage
    Build-DiagnosticsPage
    Build-HistoryPage
    Update-AllViews
    [void]$form.Show()
}

function Build-DashboardPage {
    $page = $script:DashboardPages["Dashboard"]
    $page.Controls.Clear()
    $script:DashboardControls = @{}

    $title = New-Label "Usage overview" 22 18 260 32 (New-Font "Segoe UI Semibold" 18)
    $page.Controls.Add($title)
    $subtitle = New-Label "Provider snapshots, reset countdowns, source, and confidence." 24 50 470 24
    $subtitle.ForeColor = [System.Drawing.Color]::FromArgb(105, 113, 122)
    $page.Controls.Add($subtitle)

    $x = 24
    foreach ($key in $script:ProviderOrder) {
        $card = New-Object System.Windows.Forms.Panel
        $card.Location = New-Object System.Drawing.Point -ArgumentList $x, 92
        $card.Size = New-Object System.Drawing.Size -ArgumentList 310, 250
        $card.BackColor = [System.Drawing.Color]::White
        $card.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
        $page.Controls.Add($card)

        $name = New-Label "" 16 14 180 28 (New-Font "Segoe UI Semibold" 14)
        $status = New-Label "" 206 18 86 22 (New-Font "Segoe UI" 8)
        $status.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight
        $sessionTitle = New-Label "5 Hour" 16 58 88 20 (New-Font "Segoe UI Semibold" 9)
        $sessionPercent = New-Label "" 230 58 60 20 (New-Font "Segoe UI Semibold" 10)
        $sessionPercent.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight
        $sessionBar = New-Progress 16 84 274
        $sessionReset = New-Label "" 16 102 274 22
        $weeklyTitle = New-Label "Weekly" 16 138 88 20 (New-Font "Segoe UI Semibold" 9)
        $weeklyPercent = New-Label "" 230 138 60 20 (New-Font "Segoe UI Semibold" 10)
        $weeklyPercent.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight
        $weeklyBar = New-Progress 16 164 274
        $weeklyReset = New-Label "" 16 182 274 22
        $source = New-Label "" 16 214 180 20
        $confidence = New-Label "" 210 214 80 20
        $confidence.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight

        foreach ($c in @($name, $status, $sessionTitle, $sessionPercent, $sessionBar, $sessionReset, $weeklyTitle, $weeklyPercent, $weeklyBar, $weeklyReset, $source, $confidence)) {
            $card.Controls.Add($c)
        }

        $script:DashboardControls[$key] = @{
            name = $name
            status = $status
            sessionPercent = $sessionPercent
            sessionBar = $sessionBar
            sessionReset = $sessionReset
            weeklyPercent = $weeklyPercent
            weeklyBar = $weeklyBar
            weeklyReset = $weeklyReset
            source = $source
            confidence = $confidence
        }
        $x += 334
    }

    $refresh = New-Object System.Windows.Forms.Button
    $refresh.Text = "Refresh"
    $refresh.Location = New-Object System.Drawing.Point -ArgumentList 24, 366
    $refresh.Size = New-Object System.Drawing.Size -ArgumentList 92, 32
    $refresh.Add_Click({
        Invoke-ProviderRefresh $script:State
        Update-AllViews
    })
    $page.Controls.Add($refresh)

    $editClaude = New-Object System.Windows.Forms.Button
    $editClaude.Text = "Edit Claude"
    $editClaude.Location = New-Object System.Drawing.Point -ArgumentList 128, 366
    $editClaude.Size = New-Object System.Drawing.Size -ArgumentList 104, 32
    $editClaude.Add_Click({ Show-EditDialog "claude" })
    $page.Controls.Add($editClaude)

    $editCodex = New-Object System.Windows.Forms.Button
    $editCodex.Text = "Edit Codex"
    $editCodex.Location = New-Object System.Drawing.Point -ArgumentList 244, 366
    $editCodex.Size = New-Object System.Drawing.Size -ArgumentList 104, 32
    $editCodex.Add_Click({ Show-EditDialog "codex" })
    $page.Controls.Add($editCodex)

    $summary = New-Label "" 24 420 620 48
    $summary.ForeColor = [System.Drawing.Color]::FromArgb(85, 92, 102)
    $page.Controls.Add($summary)
    $script:DashboardControls.summary = $summary
}

function Build-SettingsPage {
    $page = $script:DashboardPages["Settings"]
    $page.Controls.Clear()

    $page.Controls.Add((New-Label "Settings" 22 18 240 32 (New-Font "Segoe UI Semibold" 18)))

    $modeLabel = New-Label "Widget mode" 24 74 150 24
    $page.Controls.Add($modeLabel)
    $mode = New-Object System.Windows.Forms.ComboBox
    $mode.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
    [void]$mode.Items.AddRange(@("Compact", "Normal", "Detailed"))
    $mode.SelectedItem = $script:State.settings.widgetMode
    $mode.Location = New-Object System.Drawing.Point -ArgumentList 190, 72
    $mode.Size = New-Object System.Drawing.Size -ArgumentList 160, 24
    $mode.Add_SelectedIndexChanged({
        $script:State.settings.widgetMode = [string]$mode.SelectedItem
        Save-State $script:State
        Rebuild-MiniRows
        Update-AllViews
    })
    $page.Controls.Add($mode)

    $intervalLabel = New-Label "Refresh interval" 24 116 150 24
    $page.Controls.Add($intervalLabel)
    $interval = New-Object System.Windows.Forms.ComboBox
    $interval.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
    [void]$interval.Items.AddRange(@("15", "30", "60", "120", "300", "Manual"))
    $interval.SelectedItem = [string]$script:State.settings.refreshSeconds
    $interval.Location = New-Object System.Drawing.Point -ArgumentList 190, 114
    $interval.Size = New-Object System.Drawing.Size -ArgumentList 160, 24
    $interval.Add_SelectedIndexChanged({
        if ([string]$interval.SelectedItem -eq "Manual") {
            $script:RefreshTimer.Stop()
            $script:State.settings.refreshSeconds = 0
        }
        else {
            $script:State.settings.refreshSeconds = [int]$interval.SelectedItem
            $script:RefreshTimer.Interval = $script:State.settings.refreshSeconds * 1000
            $script:RefreshTimer.Start()
        }
        Save-State $script:State
    })
    $page.Controls.Add($interval)

    $topmost = New-Object System.Windows.Forms.CheckBox
    $topmost.Text = "Always on top"
    $topmost.Checked = [bool]$script:State.settings.alwaysOnTop
    $topmost.Location = New-Object System.Drawing.Point -ArgumentList 24, 158
    $topmost.Size = New-Object System.Drawing.Size -ArgumentList 160, 24
    $topmost.Add_CheckedChanged({
        $script:State.settings.alwaysOnTop = $topmost.Checked
        $script:MiniForm.TopMost = $topmost.Checked
        Save-State $script:State
    })
    $page.Controls.Add($topmost)

    $collectionLabel = New-Label "Collection level" 24 204 150 24
    $page.Controls.Add($collectionLabel)
    $collection = New-Object System.Windows.Forms.ComboBox
    $collection.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
    [void]$collection.Items.AddRange(@("Safe", "Standard", "Deep"))
    $collection.SelectedItem = $script:State.settings.collectionLevel
    $collection.Location = New-Object System.Drawing.Point -ArgumentList 190, 202
    $collection.Size = New-Object System.Drawing.Size -ArgumentList 160, 24
    $collection.Add_SelectedIndexChanged({
        $script:State.settings.collectionLevel = [string]$collection.SelectedItem
        Save-State $script:State
    })
    $page.Controls.Add($collection)

    $note = New-Label "Phase 1 enables only token-free verified collectors by default. CLI, official, and web collectors stay disabled until their lookup commands are verified as read-only and non-billing." 24 258 600 70
    $note.ForeColor = [System.Drawing.Color]::FromArgb(105, 113, 122)
    $page.Controls.Add($note)
}

function Build-DiagnosticsPage {
    $page = $script:DashboardPages["Diagnostics"]
    $page.Controls.Clear()
    $script:DiagnosticsText = New-Object System.Windows.Forms.TextBox
    $script:DiagnosticsText.Multiline = $true
    $script:DiagnosticsText.ReadOnly = $true
    $script:DiagnosticsText.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
    $script:DiagnosticsText.Font = New-Font "Consolas" 9
    $script:DiagnosticsText.Dock = [System.Windows.Forms.DockStyle]::Fill
    $page.Controls.Add($script:DiagnosticsText)
}

function Build-HistoryPage {
    $page = $script:DashboardPages["History"]
    $page.Controls.Clear()
    $script:HistoryText = New-Object System.Windows.Forms.TextBox
    $script:HistoryText.Multiline = $true
    $script:HistoryText.ReadOnly = $true
    $script:HistoryText.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
    $script:HistoryText.Font = New-Font "Consolas" 9
    $script:HistoryText.Dock = [System.Windows.Forms.DockStyle]::Fill
    $page.Controls.Add($script:HistoryText)
}

function Rebuild-MiniRows {
    $script:MiniRowsPanel.Controls.Clear()
    $script:MiniRows = @{}
    $mode = $script:State.settings.widgetMode
    $top = 10
    foreach ($key in $script:ProviderOrder) {
        $row = Build-ProviderRow $script:MiniRowsPanel $key $top $mode
        $row.panel.Add_DoubleClick({ Open-Dashboard })
        $script:MiniRows[$key] = $row
        if ($mode -eq "Compact") { $top += 52 } else { $top += 100 }
    }
    $height = if ($mode -eq "Compact") { 154 } elseif ($mode -eq "Detailed") { 260 } else { 250 }
    $script:MiniForm.Size = New-Object System.Drawing.Size -ArgumentList 326, $height
    $script:MiniRowsPanel.Size = New-Object System.Drawing.Size -ArgumentList 326, ($height - 42)
}

function Update-MiniView {
    foreach ($key in $script:ProviderOrder) {
        $p = $script:State.providers[$key]
        $row = $script:MiniRows[$key]
        if (-not $row) { continue }
        $session = Clamp-Percent $p.sessionUsagePercent
        $weekly = Clamp-Percent $p.weeklyUsagePercent
        $remaining = 100 - $session
        $row.name.Text = $p.displayName
        $row.percent.Text = "$remaining% left"
        $row.percent.ForeColor = Get-StateColor $session
        $row.sessionBar.Value = $session
        if ($row.weeklyBar) { $row.weeklyBar.Value = $weekly }
        $row.reset.Text = Format-Countdown $p.sessionResetAt
        $row.sync.Text = $p.confidence

        $tip = @(
            "$($p.displayName) / $($p.accountName)",
            "5H usage: $session%",
            "5H reset: $(Format-Countdown $p.sessionResetAt)",
            "Weekly usage: $weekly%",
            "Weekly reset: $(Format-LocalTime $p.weeklyResetAt)",
            "Source: $($p.source)",
            "Confidence: $($p.confidence)"
        ) -join [Environment]::NewLine
        $script:ToolTip.SetToolTip($row.panel, $tip)
    }
}

function Update-DashboardView {
    if (-not $script:DashboardControls) { return }

    foreach ($key in $script:ProviderOrder) {
        $p = $script:State.providers[$key]
        $c = $script:DashboardControls[$key]
        if (-not $c) { continue }
        $session = Clamp-Percent $p.sessionUsagePercent
        $weekly = Clamp-Percent $p.weeklyUsagePercent
        $c.name.Text = $p.displayName
        $c.status.Text = $p.status
        $c.sessionPercent.Text = "$session%"
        $c.sessionPercent.ForeColor = Get-StateColor $session
        $c.sessionBar.Value = $session
        $c.sessionReset.Text = "Reset: $(Format-Countdown $p.sessionResetAt)  ($((Get-ResetState $p.sessionResetAt)))"
        $c.weeklyPercent.Text = "$weekly%"
        $c.weeklyBar.Value = $weekly
        $c.weeklyReset.Text = "Weekly reset: $(Format-LocalTime $p.weeklyResetAt)"
        $c.source.Text = "Source: $($p.source)"
        $c.confidence.Text = $p.confidence
    }

    if ($script:DashboardControls.summary) {
        $claude = $script:State.providers.claude
        $codex = $script:State.providers.codex
        $script:DashboardControls.summary.Text = "Claude: $((100 - [int]$claude.sessionUsagePercent))% remaining, resets in $(Format-Countdown $claude.sessionResetAt)`r`nCodex: $((100 - [int]$codex.sessionUsagePercent))% remaining, resets in $(Format-Countdown $codex.sessionResetAt)"
    }

    if ($script:DiagnosticsText) {
        $lines = @()
        foreach ($key in $script:ProviderOrder) {
            $p = $script:State.providers[$key]
            $lines += "$($p.displayName)"
            foreach ($collector in $p.collectors) {
                $latency = if ($collector.latencyMs -ne $null) { "$($collector.latencyMs) ms" } else { "-" }
                $tokenFree = if ([bool]$collector.tokenFreeVerified) { "TOKEN-FREE" } else { "UNVERIFIED" }
                $lines += ("  {0,-10} {1,-9} {2,-8} {3,-11} {4}" -f $collector.name, $collector.status, $latency, $tokenFree, $collector.message)
                $lines += ("    verify: {0}" -f $collector.verification)
            }
            $lines += "  Selected   $($p.source)"
            $lines += "  Last       $($p.collectedAt)"
            $lines += ""
        }
        $script:DiagnosticsText.Text = ($lines -join [Environment]::NewLine)
    }

    if ($script:HistoryText) {
        $history = @(Read-History | Select-Object -Last 40)
        $lines = @("timestamp                  provider account    5h   week source", "----------------------------------------------------------------")
        foreach ($h in $history) {
            $time = try { ([DateTime]$h.timestamp).ToLocalTime().ToString("MM-dd HH:mm:ss") } catch { "unknown" }
            $lines += ("{0,-26} {1,-8} {2,-9} {3,3}% {4,5}% {5}" -f $time, $h.provider, $h.account, $h.sessionUsagePercent, $h.weeklyUsagePercent, $h.source)
        }
        $script:HistoryText.Text = ($lines -join [Environment]::NewLine)
    }
}

function Update-Tray {
    if (-not $script:NotifyIcon) { return }
    $claude = $script:State.providers.claude
    $codex = $script:State.providers.codex
    $script:NotifyIcon.Text = "Claude $($claude.sessionUsagePercent)% / $(Format-Countdown $claude.sessionResetAt)`nCodex $($codex.sessionUsagePercent)% / $(Format-Countdown $codex.sessionResetAt)"
}

function Update-AllViews {
    Update-MiniView
    Update-DashboardView
    Update-Tray
}

function Build-MiniApp {
    $script:State = Read-State
    $script:DashboardPages = @{}

    $form = New-Object System.Windows.Forms.Form
    $script:MiniForm = $form
    $form.Text = $script:AppName
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.BackColor = [System.Drawing.Color]::FromArgb(246, 247, 249)
    $form.Font = New-Font "Segoe UI" 9
    $form.TopMost = [bool]$script:State.settings.alwaysOnTop
    $form.Opacity = [double]$script:State.settings.opacity

    if ($script:State.window.left -ne $null -and $script:State.window.top -ne $null) {
        $form.Location = New-Object System.Drawing.Point -ArgumentList ([int]$script:State.window.left), ([int]$script:State.window.top)
    }
    else {
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $form.Location = New-Object System.Drawing.Point -ArgumentList ($screen.Right - 350), ($screen.Bottom - 290)
    }

    $header = New-Object System.Windows.Forms.Panel
    $header.Location = New-Object System.Drawing.Point -ArgumentList 0, 0
    $header.Size = New-Object System.Drawing.Size -ArgumentList 326, 42
    $header.BackColor = [System.Drawing.Color]::FromArgb(22, 27, 31)
    $form.Controls.Add($header)

    $title = New-Label "Usage Monitor" 12 10 160 22 (New-Font "Segoe UI Semibold" 10)
    $title.ForeColor = [System.Drawing.Color]::White
    $header.Controls.Add($title)

    $open = New-Object System.Windows.Forms.Button
    $open.Text = "Open"
    $open.Location = New-Object System.Drawing.Point -ArgumentList 208, 8
    $open.Size = New-Object System.Drawing.Size -ArgumentList 52, 26
    $open.Add_Click({ Open-Dashboard })
    $header.Controls.Add($open)

    $close = New-Object System.Windows.Forms.Button
    $close.Text = "X"
    $close.Location = New-Object System.Drawing.Point -ArgumentList 266, 8
    $close.Size = New-Object System.Drawing.Size -ArgumentList 44, 26
    $close.Add_Click({ $script:MiniForm.Hide() })
    $header.Controls.Add($close)

    $dragging = $false
    $dragStart = $null
    $header.Add_MouseDown({
        $script:DragActive = $true
        $script:DragStart = [System.Windows.Forms.Cursor]::Position
        $script:FormStart = $form.Location
    })
    $header.Add_MouseMove({
        if ($script:DragActive) {
            $current = [System.Windows.Forms.Cursor]::Position
            $dx = $current.X - $script:DragStart.X
            $dy = $current.Y - $script:DragStart.Y
            $form.Location = New-Object System.Drawing.Point -ArgumentList ($script:FormStart.X + $dx), ($script:FormStart.Y + $dy)
        }
    })
    $header.Add_MouseUp({
        $script:DragActive = $false
        $script:State.window.left = $form.Left
        $script:State.window.top = $form.Top
        Save-State $script:State
    })

    $rowsPanel = New-Object System.Windows.Forms.Panel
    $script:MiniRowsPanel = $rowsPanel
    $rowsPanel.Location = New-Object System.Drawing.Point -ArgumentList 0, 42
    $rowsPanel.BackColor = [System.Drawing.Color]::FromArgb(246, 247, 249)
    $form.Controls.Add($rowsPanel)

    $script:ToolTip = New-Object System.Windows.Forms.ToolTip
    Rebuild-MiniRows

    $menu = New-Object System.Windows.Forms.ContextMenuStrip
    [void]$menu.Items.Add("Open Dashboard", $null, { Open-Dashboard })
    [void]$menu.Items.Add("Refresh", $null, {
        Invoke-ProviderRefresh $script:State
        Update-AllViews
    })
    [void]$menu.Items.Add("Compact Mode", $null, {
        $script:State.settings.widgetMode = "Compact"
        Save-State $script:State
        Rebuild-MiniRows
        Update-AllViews
    })
    [void]$menu.Items.Add("Normal Mode", $null, {
        $script:State.settings.widgetMode = "Normal"
        Save-State $script:State
        Rebuild-MiniRows
        Update-AllViews
    })
    [void]$menu.Items.Add("Detailed Mode", $null, {
        $script:State.settings.widgetMode = "Detailed"
        Save-State $script:State
        Rebuild-MiniRows
        Update-AllViews
    })
    [void]$menu.Items.Add("Exit", $null, {
        $script:NotifyIcon.Visible = $false
        [System.Windows.Forms.Application]::Exit()
    })
    $form.ContextMenuStrip = $menu
    $rowsPanel.ContextMenuStrip = $menu

    $notify = New-Object System.Windows.Forms.NotifyIcon
    $script:NotifyIcon = $notify
    $notify.Icon = [System.Drawing.SystemIcons]::Information
    $notify.Visible = $true
    $notify.ContextMenuStrip = $menu
    $notify.Add_DoubleClick({
        $script:MiniForm.Show()
        $script:MiniForm.Activate()
    })

    $script:CountdownTimer = New-Object System.Windows.Forms.Timer
    $script:CountdownTimer.Interval = 1000
    $script:CountdownTimer.Add_Tick({ Update-AllViews })
    $script:CountdownTimer.Start()

    $script:RefreshTimer = New-Object System.Windows.Forms.Timer
    $script:RefreshTimer.Interval = [Math]::Max(15, [int]$script:State.settings.refreshSeconds) * 1000
    $script:RefreshTimer.Add_Tick({
        if ([int]$script:State.settings.refreshSeconds -gt 0) {
            Invoke-ProviderRefresh $script:State
            Update-AllViews
        }
    })
    if ([int]$script:State.settings.refreshSeconds -gt 0) {
        $script:RefreshTimer.Start()
    }

    Update-AllViews
}

if ($SelfTest) {
    $state = New-DefaultState
    $state.providers.claude.sessionUsagePercent = 72
    $state.providers.codex.weeklyUsagePercent = 22
    Save-State $state
    $loaded = Read-State
    if ([int]$loaded.providers.claude.sessionUsagePercent -ne 72) {
        throw "State round-trip failed."
    }
    Invoke-ProviderRefresh $loaded
    $history = @(Read-History)
    if ($history.Count -lt 2) {
        throw "History append failed."
    }
    if ((Format-Countdown $loaded.providers.claude.sessionResetAt).Length -lt 2) {
        throw "Countdown formatting failed."
    }
    Write-Output "Self-test passed. Data directory: $script:DataDir"
    exit 0
}

[System.Windows.Forms.Application]::EnableVisualStyles()
Build-MiniApp
[void][System.Windows.Forms.Application]::Run($script:MiniForm)
