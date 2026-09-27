@echo off
setlocal
rem Builds the app, then launches it as its own process so this window can close without closing the app.
set "ROOT=%~dp0"
set "PROJECT=%ROOT%UsageMonitorWpf\UsageMonitorWpf.csproj"
set "EXE=%ROOT%UsageMonitorWpf\bin\Release\net8.0-windows\AIUsageMonitor.exe"
set "DOTNET=%ROOT%.dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

tasklist /FI "IMAGENAME eq AIUsageMonitor.exe" 2>nul | find /I "AIUsageMonitor.exe" >nul
if not errorlevel 1 (
  echo Usage Monitor is already running. Open it from the tray icon.
  echo To start the new build, exit it from the tray menu and run this again.
  rem Keep the message visible for a few seconds (works even without console input).
  ping -n 6 127.0.0.1 >nul
  exit /b 0
)

echo Building Usage Monitor...
"%DOTNET%" build "%PROJECT%" -c Release -nologo -v q
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

start "" "%EXE%"
exit /b 0
