<#
.SYNOPSIS
    Builds a distributable release package of Usage Monitor (self-contained,
    single-file win-x64 exe) and drops it under dist\release.

.EXAMPLE
    .\tools\publish-release.ps1
    .\tools\publish-release.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $true
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "UsageMonitorWpf\UsageMonitorWpf.csproj"

if (-not $Version) {
    [xml]$csprojXml = Get-Content $project
    $Version = $csprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) {
    throw "Could not determine version from $project. Pass -Version explicitly."
}

$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$releaseDir = Join-Path $root "dist\release"
$publishDir = Join-Path $releaseDir "$Version\$Runtime"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$selfContainedArg = $SelfContained.ToString().ToLowerInvariant()
$singleFileArg = $SingleFile.ToString().ToLowerInvariant()

Write-Host "Publishing Usage Monitor v$Version ($Runtime, self-contained=$selfContainedArg, single-file=$singleFileArg)..."

& $dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained $selfContainedArg `
    -p:PublishSingleFile=$singleFileArg `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:InformationalVersion=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$zipName = "AIUsageMonitor-v$Version-$Runtime.zip"
$zipPath = Join-Path $releaseDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
$sumsPath = Join-Path $releaseDir "SHA256SUMS.txt"
$existing = @()
if (Test-Path $sumsPath) {
    $existing = Get-Content $sumsPath | Where-Object { $_ -notmatch [regex]::Escape($zipName) }
}
$existing + "$hash  $zipName" | Set-Content -Path $sumsPath -Encoding utf8

Write-Host ""
Write-Host "Release package created:"
Write-Host "  $zipPath"
Write-Host "  SHA256: $hash"
Write-Host "  (recorded in $sumsPath)"
