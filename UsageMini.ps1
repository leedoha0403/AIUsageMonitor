param(
    [switch]$SelfTest
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Resolve-AppDirectory {
    $preferred = Join-Path $env:APPDATA "UsageMini"
    $fallback = Join-Path $scriptDir ".usage-mini"

    foreach ($candidate in @($preferred, $fallback)) {
        try {
            if (-not (Test-Path $candidate)) {
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

$appDir = Resolve-AppDirectory
$dataPath = Join-Path $appDir "usage.json"

function New-DefaultUsageData {
    @{
        services = @{
            codex = @{
                name = "Codex"
                usedPercent = 0
                resetAt = ""
                note = "Enter the Codex usage limit shown in the Codex app."
            }
            claude = @{
                name = "Claude"
                usedPercent = 0
                resetAt = ""
                note = "Enter the Claude usage limit shown in your account."
            }
        }
        updatedAt = (Get-Date).ToString("s")
    }
}

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

    return $InputObject
}

function Read-UsageData {
    if (-not (Test-Path $dataPath)) {
        return New-DefaultUsageData
    }

    try {
        $data = Get-Content -LiteralPath $dataPath -Raw | ConvertFrom-Json | ConvertTo-Hashtable
        if (-not $data.services.codex -or -not $data.services.claude) {
            return New-DefaultUsageData
        }
        return $data
    }
    catch {
        return New-DefaultUsageData
    }
}

function Save-UsageData {
    param([hashtable]$Data)

    $Data.updatedAt = (Get-Date).ToString("s")
    $Data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $dataPath -Encoding UTF8
}

function Clamp-Percent {
    param([int]$Value)
    return [Math]::Max(0, [Math]::Min(100, $Value))
}

function Get-RemainingText {
    param([int]$UsedPercent)
    $remaining = 100 - (Clamp-Percent $UsedPercent)
    return "$remaining% left"
}

function Get-StatusColor {
    param([int]$UsedPercent)
    $remaining = 100 - (Clamp-Percent $UsedPercent)

    if ($remaining -le 10) {
        return [System.Drawing.Color]::FromArgb(214, 69, 65)
    }
    if ($remaining -le 30) {
        return [System.Drawing.Color]::FromArgb(224, 154, 56)
    }
    return [System.Drawing.Color]::FromArgb(45, 154, 99)
}

if ($SelfTest) {
    $sample = New-DefaultUsageData
    $sample.services.codex.usedPercent = 88
    if ((Get-RemainingText 88) -ne "12% left") {
        throw "Remaining text calculation failed."
    }
    Save-UsageData $sample
    $loaded = Read-UsageData
    if ([int]$loaded.services.codex.usedPercent -ne 88) {
        throw "Save/read round-trip failed."
    }
    Write-Output "Self-test passed. Data file: $dataPath"
    exit 0
}

$data = Read-UsageData

$form = New-Object System.Windows.Forms.Form
$form.Text = "Usage Mini"
$form.Size = New-Object System.Drawing.Size -ArgumentList 392, 410
$form.MinimumSize = New-Object System.Drawing.Size -ArgumentList 360, 390
$form.StartPosition = "CenterScreen"
$form.BackColor = [System.Drawing.Color]::FromArgb(246, 247, 249)
$form.Font = New-Object System.Drawing.Font -ArgumentList "Segoe UI", 9
$form.TopMost = $true

$title = New-Object System.Windows.Forms.Label
$title.Text = "Claude / Codex Usage"
$title.Font = New-Object System.Drawing.Font -ArgumentList "Segoe UI Semibold", 14
$title.Location = New-Object System.Drawing.Point -ArgumentList 18, 16
$title.Size = New-Object System.Drawing.Size -ArgumentList 230, 30
$form.Controls.Add($title)

$updatedLabel = New-Object System.Windows.Forms.Label
$updatedLabel.ForeColor = [System.Drawing.Color]::FromArgb(96, 104, 115)
$updatedLabel.Location = New-Object System.Drawing.Point -ArgumentList 20, 48
$updatedLabel.Size = New-Object System.Drawing.Size -ArgumentList 330, 22
$form.Controls.Add($updatedLabel)

$controls = @{}

function Add-ServiceCard {
    param(
        [string]$Key,
        [int]$Top
    )

    $service = $data.services[$Key]

    $panel = New-Object System.Windows.Forms.Panel
    $panel.Location = New-Object System.Drawing.Point -ArgumentList 18, $Top
    $panel.Size = New-Object System.Drawing.Size -ArgumentList 340, 118
    $panel.BackColor = [System.Drawing.Color]::White
    $panel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
    $form.Controls.Add($panel)

    $nameLabel = New-Object System.Windows.Forms.Label
    $nameLabel.Text = $service.name
    $nameLabel.Font = New-Object System.Drawing.Font -ArgumentList "Segoe UI Semibold", 12
    $nameLabel.Location = New-Object System.Drawing.Point -ArgumentList 12, 10
    $nameLabel.Size = New-Object System.Drawing.Size -ArgumentList 110, 24
    $panel.Controls.Add($nameLabel)

    $remainingLabel = New-Object System.Windows.Forms.Label
    $remainingLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight
    $remainingLabel.Font = New-Object System.Drawing.Font -ArgumentList "Segoe UI Semibold", 11
    $remainingLabel.Location = New-Object System.Drawing.Point -ArgumentList 190, 10
    $remainingLabel.Size = New-Object System.Drawing.Size -ArgumentList 132, 24
    $panel.Controls.Add($remainingLabel)

    $progress = New-Object System.Windows.Forms.ProgressBar
    $progress.Location = New-Object System.Drawing.Point -ArgumentList 14, 40
    $progress.Size = New-Object System.Drawing.Size -ArgumentList 308, 16
    $progress.Minimum = 0
    $progress.Maximum = 100
    $panel.Controls.Add($progress)

    $usedLabel = New-Object System.Windows.Forms.Label
    $usedLabel.Text = "Used"
    $usedLabel.ForeColor = [System.Drawing.Color]::FromArgb(96, 104, 115)
    $usedLabel.Location = New-Object System.Drawing.Point -ArgumentList 14, 68
    $usedLabel.Size = New-Object System.Drawing.Size -ArgumentList 52, 20
    $panel.Controls.Add($usedLabel)

    $usedInput = New-Object System.Windows.Forms.NumericUpDown
    $usedInput.Location = New-Object System.Drawing.Point -ArgumentList 66, 66
    $usedInput.Size = New-Object System.Drawing.Size -ArgumentList 58, 24
    $usedInput.Minimum = 0
    $usedInput.Maximum = 100
    $usedInput.Value = Clamp-Percent ([int]$service.usedPercent)
    $panel.Controls.Add($usedInput)

    $percentLabel = New-Object System.Windows.Forms.Label
    $percentLabel.Text = "%"
    $percentLabel.ForeColor = [System.Drawing.Color]::FromArgb(96, 104, 115)
    $percentLabel.Location = New-Object System.Drawing.Point -ArgumentList 128, 68
    $percentLabel.Size = New-Object System.Drawing.Size -ArgumentList 20, 20
    $panel.Controls.Add($percentLabel)

    $resetInput = New-Object System.Windows.Forms.TextBox
    $resetInput.Location = New-Object System.Drawing.Point -ArgumentList 158, 66
    $resetInput.Size = New-Object System.Drawing.Size -ArgumentList 164, 24
    $resetInput.Text = $service.resetAt
    $panel.Controls.Add($resetInput)

    $noteInput = New-Object System.Windows.Forms.TextBox
    $noteInput.Location = New-Object System.Drawing.Point -ArgumentList 14, 92
    $noteInput.Size = New-Object System.Drawing.Size -ArgumentList 308, 22
    $noteInput.Text = $service.note
    $panel.Controls.Add($noteInput)

    $controls[$Key] = @{
        remaining = $remainingLabel
        progress = $progress
        used = $usedInput
        reset = $resetInput
        note = $noteInput
    }
}

function Update-View {
    foreach ($key in @("codex", "claude")) {
        $entry = $controls[$key]
        $used = Clamp-Percent ([int]$entry.used.Value)
        $entry.progress.Value = $used
        $entry.remaining.Text = Get-RemainingText $used
        $entry.remaining.ForeColor = Get-StatusColor $used
    }

    if ($data.updatedAt) {
        $updatedLabel.Text = "Last saved: $($data.updatedAt)"
    }
    else {
        $updatedLabel.Text = "Not saved yet"
    }
}

Add-ServiceCard -Key "codex" -Top 78
Add-ServiceCard -Key "claude" -Top 208

foreach ($key in @("codex", "claude")) {
    $controls[$key].used.Add_ValueChanged({ Update-View })
}

$saveButton = New-Object System.Windows.Forms.Button
$saveButton.Text = "Save"
$saveButton.Location = New-Object System.Drawing.Point -ArgumentList 196, 338
$saveButton.Size = New-Object System.Drawing.Size -ArgumentList 76, 28
$form.Controls.Add($saveButton)

$refreshButton = New-Object System.Windows.Forms.Button
$refreshButton.Text = "Reload"
$refreshButton.Location = New-Object System.Drawing.Point -ArgumentList 282, 338
$refreshButton.Size = New-Object System.Drawing.Size -ArgumentList 76, 28
$form.Controls.Add($refreshButton)

$pinCheck = New-Object System.Windows.Forms.CheckBox
$pinCheck.Text = "Topmost"
$pinCheck.Checked = $true
$pinCheck.Location = New-Object System.Drawing.Point -ArgumentList 18, 342
$pinCheck.Size = New-Object System.Drawing.Size -ArgumentList 82, 22
$form.Controls.Add($pinCheck)

$pinCheck.Add_CheckedChanged({
    $form.TopMost = $pinCheck.Checked
})

$saveButton.Add_Click({
    foreach ($key in @("codex", "claude")) {
        $data.services[$key].usedPercent = [int]$controls[$key].used.Value
        $data.services[$key].resetAt = $controls[$key].reset.Text
        $data.services[$key].note = $controls[$key].note.Text
    }

    Save-UsageData $data
    Update-View
})

$refreshButton.Add_Click({
    $script:data = Read-UsageData
    foreach ($key in @("codex", "claude")) {
        $controls[$key].used.Value = Clamp-Percent ([int]$script:data.services[$key].usedPercent)
        $controls[$key].reset.Text = $script:data.services[$key].resetAt
        $controls[$key].note.Text = $script:data.services[$key].note
    }
    Update-View
})

Update-View
[void]$form.ShowDialog()
