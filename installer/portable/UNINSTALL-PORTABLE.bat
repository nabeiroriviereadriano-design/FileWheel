@echo off
rem FileWheel portable - desinstallation / uninstall
rem Arrete FileWheel, retire le demarrage automatique s'il pointe vers ce dossier, puis supprime ce dossier.
rem Stops FileWheel, removes the auto-start entry if it points to this folder, then deletes this folder.
setlocal
set "DIR=%~dp0"
set "DIR=%DIR:~0,-1%"
echo.
echo  FileWheel (portable) : desinstallation / uninstall
echo  Dossier / folder : %DIR%
echo.
choice /c ON /n /m " Continuer ? / Continue? [O = oui / yes, N = non / no] "
if errorlevel 2 goto :eof

if exist "%DIR%\FileWheel.exe" start "" /wait "%DIR%\FileWheel.exe" --quitter
timeout /t 3 /nobreak >nul

rem Demarrage automatique : seulement s'il lance CE FileWheel / only if it starts THIS FileWheel
set "RUNVAL="
for /f "tokens=2,*" %%a in ('reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FileWheel 2^>nul ^| findstr /i "FileWheel"') do set "RUNVAL=%%b"
if defined RUNVAL (
  echo "%RUNVAL%" | findstr /i /c:"%DIR%" >nul && reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FileWheel /f >nul
)

set "FILTER=$_.Name -ne 'FileWheel-data'"
choice /c ON /n /m " Supprimer aussi vos reglages (FileWheel-data) ? / Also delete your settings? [O/N] "
if not errorlevel 2 set "FILTER=$true"

echo.
echo  FileWheel est desinstalle. / FileWheel has been uninstalled.
timeout /t 2 /nobreak >nul
rem Suppression apres la fin de ce script (il ne peut pas effacer son propre dossier pendant qu'il tourne)
set "PSDIR=%DIR:'=''%"
start "" /min powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Sleep 2; $d='%PSDIR%'; Get-ChildItem -LiteralPath $d -Force | Where-Object { %FILTER% } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; if (-not (Get-ChildItem -LiteralPath $d -Force)) { Remove-Item -LiteralPath $d -Force }" & exit /b 0
