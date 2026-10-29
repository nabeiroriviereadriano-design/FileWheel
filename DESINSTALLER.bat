@echo off
rem Desinstalle FileWheel (equivalent de Parametres Windows > Applications > FileWheel > Desinstaller).
setlocal
set "DIR=%LOCALAPPDATA%\Programs\FileWheel"
if exist "%DIR%\Uninstall.exe" (
  start "" "%DIR%\Uninstall.exe"
  goto :fin
)
rem Installation faite avec une ancienne version (0.1.x)
if exist "%DIR%\Uninstall-FileWheel.ps1" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%DIR%\Uninstall-FileWheel.ps1"
  goto :fin
)
echo FileWheel n'est pas installe pour ce compte.
pause
:fin
endlocal
