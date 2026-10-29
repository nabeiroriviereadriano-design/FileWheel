@echo off
rem Construit seulement FileWheel-Setup-<version>.exe (a copier ou partager), sans le lancer.
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Build-Setup.ps1"
set "CODE=%ERRORLEVEL%"
pause
endlocal & exit /b %CODE%
