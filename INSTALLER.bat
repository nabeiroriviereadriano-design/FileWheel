@echo off
rem Construit FileWheel-Setup-<version>.exe (installer / mettre a jour / desinstaller) puis le lance.
rem Necessite le SDK .NET 10 et NSIS : tous deux proposes automatiquement via winget s'ils manquent.
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Build-Setup.ps1" -Lancer
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" (
  echo.
  echo La creation de l'installateur a echoue ^(code %CODE%^). Les messages ci-dessus indiquent la cause.
  pause
)
endlocal & exit /b %CODE%
