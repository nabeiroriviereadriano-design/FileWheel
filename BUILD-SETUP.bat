@echo off
rem Only builds FileWheel-Setup-<version>.exe (to copy or share), without running it.
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Build-Setup.ps1"
set "CODE=%ERRORLEVEL%"
pause
endlocal & exit /b %CODE%
