<#
.SYNOPSIS
    Copies the shared feature projects (Core, Presentation, Widget, tests) into a new repository folder so the
    standalone app repo can consume Core/Presentation as packages while the Host widget lives next to them.

.DESCRIPTION
    Nothing is removed from this repo and nothing is pushed anywhere: the script only creates the destination
    folder, initializes git there (no commit unless -Commit), and proves the result builds, tests and packs.
    Switching this repo over to the packages is a separate step (see docs/WIDGET_COMPAT_DESIGN.md, section 11).

.EXAMPLE
    .\tools\extract-shared-repo.ps1
    .\tools\extract-shared-repo.ps1 -Destination C:\Work\AIUsage -Commit
#>
[CmdletBinding()]
param(
    [string]$Destination,
    # Where Dora.Widget.Abstractions/Runtime live (only needed when Destination is not a sibling of this repo).
    [string]$ModuleDockPath,
    [switch]$Commit,
    [switch]$SkipVerify,
    # Refresh an existing extracted repo (mirrors the project folders; keeps .git and anything else in it).
    [switch]$Update
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path (Split-Path -Parent $root) "AIUsage" }
if (-not $ModuleDockPath) { $ModuleDockPath = Join-Path (Split-Path -Parent $root) "ModuleDock" }
$ModuleDockPath = (Resolve-Path $ModuleDockPath).Path

$existing = (Test-Path $Destination) -and (Get-ChildItem $Destination -Force | Where-Object { $_.Name -ne ".git" })
if ($existing -and -not $Update) {
    throw "Destination '$Destination' is not empty. Refusing to overwrite (use -Update to refresh an extracted repo)."
}
if ($Update -and -not (Test-Path (Join-Path $Destination ".git"))) {
    throw "-Update needs an existing git repo at '$Destination'."
}
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$projects = "AIUsage.Core", "AIUsage.Presentation", "AIUsage.Widget", "tests"
foreach ($name in $projects) {
    $source = Join-Path $root $name
    $target = Join-Path $Destination $name
    # /XD keeps build output out; robocopy exit codes below 8 mean success.
    $mode = if ($Update) { "/MIR" } else { "/E" }
    robocopy $source $target $mode /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $name ($LASTEXITCODE)" }
}
Copy-Item (Join-Path $root "LICENSE") $Destination
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $Destination
New-Item -ItemType Directory -Force -Path (Join-Path $Destination "docs") | Out-Null
Copy-Item (Join-Path $root "docs\WIDGET_COMPAT_DESIGN.md") (Join-Path $Destination "docs")

# The Host contract projects are referenced by path; keep the references valid from the new location.
$sibling = ((Split-Path -Parent $Destination) -eq (Split-Path -Parent $ModuleDockPath))
if (-not $sibling) {
    foreach ($file in Get-ChildItem $Destination -Recurse -Filter *.csproj) {
        $text = Get-Content $file.FullName -Raw
        $updated = $text -replace '(\.\.\\)+ModuleDock\\', ($ModuleDockPath + '\')
        if ($updated -ne $text) { Set-Content -Path $file.FullName -Value $updated -Encoding utf8 -NoNewline }
    }
}

@'
<Project>
  <PropertyGroup>
    <Authors>leedoha</Authors>
    <Version>0.9.0</Version>
    <RepositoryType>git</RepositoryType>
    <PackageLicenseFile>LICENSE</PackageLicenseFile>
  </PropertyGroup>
  <ItemGroup Condition="'$(IsPackable)' != 'false'">
    <None Include="$(MSBuildThisFileDirectory)LICENSE" Pack="true" PackagePath="" Visible="false" />
  </ItemGroup>
</Project>
'@ | Set-Content -Path (Join-Path $Destination "Directory.Build.props") -Encoding utf8

# Only the shared libraries are packages; the widget DLL is loaded by the Host from a plugin folder.
$widgetProject = Join-Path $Destination "AIUsage.Widget\AIUsage.Widget.csproj"
(Get-Content $widgetProject -Raw) -replace '<RootNamespace>AIUsage.Widget</RootNamespace>', "<RootNamespace>AIUsage.Widget</RootNamespace>`r`n    <IsPackable>false</IsPackable>" |
    Set-Content -Path $widgetProject -Encoding utf8 -NoNewline

Push-Location $Destination
try {
    if (-not (Test-Path "AIUsage.sln")) {
        dotnet new sln -n AIUsage | Out-Null
        dotnet sln add AIUsage.Core/AIUsage.Core.csproj AIUsage.Presentation/AIUsage.Presentation.csproj AIUsage.Widget/AIUsage.Widget.csproj tests/AIUsage.Tests/AIUsage.Tests.csproj | Out-Null
    }
    "bin/`nobj/`nartifacts/`n*.user`n.vs/" | Set-Content -Path ".gitignore" -Encoding utf8
    if (-not (Test-Path ".git")) { git init -q }

    if (-not $SkipVerify) {
        Write-Host "Verifying the extracted repo (build, test, pack)..."
        dotnet build -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "build failed in the extracted repo" }
        dotnet test -c Release --no-build --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "tests failed in the extracted repo" }
        foreach ($packable in "AIUsage.Core", "AIUsage.Presentation") {
            dotnet pack "$packable/$packable.csproj" -c Release --no-build -o artifacts/packages --nologo -v q
            if ($LASTEXITCODE -ne 0) { throw "pack failed for $packable" }
        }
        Get-ChildItem artifacts/packages | ForEach-Object { Write-Host "  package: $($_.Name)" }
    }

    if ($Commit) {
        # git prints CRLF notices on stderr; only its exit code matters here.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        git add -A 2>$null
        if ($LASTEXITCODE -ne 0) { throw "git add failed" }
        git commit -q -m "Import AIUsage shared projects extracted from the app repo" 2>$null
        if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
        $ErrorActionPreference = $previous
    }
}
finally {
    Pop-Location
}
Write-Host "Extracted to $Destination"
