@echo off
setlocal
set "DOTNET=%~dp0.dotnet\dotnet.exe"
if exist "%DOTNET%" (
  "%DOTNET%" run --project "%~dp0UsageMonitorWpf\UsageMonitorWpf.csproj"
) else (
  dotnet run --project "%~dp0UsageMonitorWpf\UsageMonitorWpf.csproj"
)
