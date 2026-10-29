@echo off
rem Builds FileWheel-Setup-<version>.exe (install / update / uninstall) and runs it.
rem Requires the .NET 10 SDK and NSIS: both are offered automatically through winget if missing.
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Build-Setup.ps1" -Lancer
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" (
  echo.
  echo Building the installer failed ^(code %CODE%^). The messages above give the cause.
  pause
)
endlocal & exit /b %CODE%
