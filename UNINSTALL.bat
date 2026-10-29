@echo off
rem Uninstalls FileWheel (same as Windows Settings > Apps > FileWheel > Uninstall).
setlocal
set "DIR=%LOCALAPPDATA%\Programs\FileWheel"
if exist "%DIR%\Uninstall.exe" (
  start "" "%DIR%\Uninstall.exe"
  goto :end
)
rem Installation made with an old version (0.1.x)
if exist "%DIR%\Uninstall-FileWheel.ps1" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%DIR%\Uninstall-FileWheel.ps1"
  goto :end
)
echo FileWheel is not installed for this account.
pause
:end
endlocal
