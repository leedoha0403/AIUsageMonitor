<#
.SYNOPSIS
    Builds AIUsage.Widget and drops it into a ModuleDock Host's widgets\AIUsage plugin folder.

.DESCRIPTION
    The Host scans <Host folder>\widgets recursively for *.Widget.dll, so the widget and the two libraries it
    needs (AIUsage.Core, AIUsage.Presentation) go together in one folder. The Host's own Dora.Widget.Abstractions
    is deliberately not copied: the widget must share the Host's copy of the contract types.

.EXAMPLE
    .\tools\deploy-to-host.ps1
    .\tools\deploy-to-host.ps1 -HostDir C:\Work\ModuleDock\src\Dora.Widget.Host\bin\Debug\net8.0-windows -Configuration Debug
#>
[CmdletBinding()]
param(
    [string]$HostDir,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $HostDir) {
    $HostDir = Join-Path (Split-Path -Parent $root) "ModuleDock\src\Dora.Widget.Host\bin\$Configuration\net8.0-windows"
}
if (-not (Test-Path (Join-Path $HostDir "ModuleDock.dll"))) {
    throw "No Host found in '$HostDir'. Build the ModuleDock Host first, or pass -HostDir."
}

dotnet build (Join-Path $root "AIUsage.Widget\AIUsage.Widget.csproj") -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$out = Join-Path $root "AIUsage.Widget\bin\$Configuration\net8.0-windows"
$target = Join-Path $HostDir "widgets\AIUsage"
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($name in "AIUsage.Widget", "AIUsage.Presentation", "AIUsage.Core") {
    Copy-Item (Join-Path $out "$name.dll") $target -Force
    $pdb = Join-Path $out "$name.pdb"
    if (Test-Path $pdb) { Copy-Item $pdb $target -Force }
}
Write-Host "Deployed to $target"
Write-Host "Restart the Host (or use its + menu) to pick it up; the first run adds one of each widget."
