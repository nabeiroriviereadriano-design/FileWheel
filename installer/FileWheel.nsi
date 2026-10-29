; =====================================================================================
;  FileWheel — programme d'installation (NSIS 3, Unicode)
;
;  Un seul .exe pour :
;    - installer FileWheel (pour l'utilisateur courant, sans droits administrateur) ;
;    - mettre a jour une version installee (reglages, historique et moteurs conserves) ;
;    - reparer ou desinstaller (mode maintenance si la meme version est deja installee).
;  Le desinstalleur (Uninstall.exe) est enregistre dans Parametres Windows > Applications.
;
;  Construit par installer\Build-Setup.ps1 (INSTALL.bat) :
;    makensis /DVERSION=0.2.0 /DPUBLISH_DIR=...\publish /DOUTFILE=...\FileWheel-Setup-0.2.0.exe FileWheel.nsi
; =====================================================================================

!ifdef TARGET_AMD64
  Target amd64-unicode   ; variante 64 bits (utilisee pour les tests automatiques)
!else
  Unicode true
!endif
ManifestDPIAware true
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"
!include "nsDialogs.nsh"
!include "StrFunc.nsh"
${StrStr}
${UnStrStr}

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\FileWheel-Setup-${VERSION}.exe"
!endif
!ifndef ICON
  !define ICON "..\assets\filewheel.ico"
!endif

!define APP        "FileWheel"
!define EXE        "FileWheel.exe"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP}"
!define RUN_KEY    "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APP}"
Caption "${APP} ${VERSION} — installation"
UninstallCaption "${APP} — désinstallation"
OutFile "${OUTFILE}"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\${APP}"
BrandingText "${APP} ${VERSION}"
ShowInstDetails nevershow
ShowUninstDetails nevershow

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1036 "ProductName" "${APP}"
VIAddVersionKey /LANG=1036 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1036 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1036 "FileDescription" "Installation de ${APP}"
VIAddVersionKey /LANG=1036 "CompanyName" "${APP}"
VIAddVersionKey /LANG=1036 "LegalCopyright" "FileWheel — licence MIT"

; ------------------------------------------------------------------------------- interface
!ifndef SIDE_BITMAP
  !define SIDE_BITMAP "..\assets\installer-side.bmp"
!endif
!define MUI_ICON "${ICON}"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${SIDE_BITMAP}"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${SIDE_BITMAP}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING
!define MUI_COMPONENTSPAGE_NODESC

Var OldVersion      ; version deja installee ("" si aucune)
Var Mode            ; "install" | "update" | "repair" | "downgrade"
Var MaintUninstall  ; 1 si l'utilisateur choisit « Désinstaller » dans le mode maintenance
Var RadioRepair
Var RadioUninstall

!define MUI_WELCOMEPAGE_TITLE "FileWheel ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeText)"
!define MUI_PAGE_CUSTOMFUNCTION_PRE WelcomePre
!define MUI_PAGE_CUSTOMFUNCTION_SHOW WelcomeShow
!insertmacro MUI_PAGE_WELCOME
Page custom MaintenancePage MaintenanceLeave
!define MUI_PAGE_CUSTOMFUNCTION_PRE ComponentsPre
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TITLE "$(FinishTitle)"
!define MUI_FINISHPAGE_TEXT "$(FinishText)"
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Lancer FileWheel maintenant"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_COMPONENTS
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "French"

LangString WelcomeText ${LANG_FRENCH} "Cet assistant installe FileWheel ${VERSION} pour votre compte Windows (aucun droit administrateur nécessaire).$\r$\n$\r$\nPendant un glisser de fichier, maintenez Alt : une roue apparaît pour convertir, recadrer, compresser, déplacer…$\r$\n$\r$\nCliquez sur Suivant pour continuer."
LangString UpdateText ${LANG_FRENCH} "FileWheel $OldVersion est installé. Cet assistant le met à jour vers la version ${VERSION}.$\r$\n$\r$\nVos réglages, votre historique et les moteurs de conversion téléchargés sont conservés.$\r$\n$\r$\nFileWheel sera fermé pendant la mise à jour puis relancé."
LangString DowngradeText ${LANG_FRENCH} "Une version plus récente de FileWheel ($OldVersion) est déjà installée.$\r$\n$\r$\nContinuer remplacera cette version par la ${VERSION}, plus ancienne. Vos réglages sont conservés."
LangString FinishTitle ${LANG_FRENCH} "FileWheel est prêt"
LangString FinishText ${LANG_FRENCH} "FileWheel ${VERSION} est installé.$\r$\n$\r$\nCommencez à glisser un fichier dans l'Explorateur et maintenez Alt.$\r$\n$\r$\nPour mettre à jour : lancez simplement un FileWheel-Setup plus récent. Pour désinstaller : Paramètres Windows › Applications, ou relancez ce programme."

; ------------------------------------------------------------------------------- sections
Section "FileWheel (obligatoire)" SecMain
  SectionIn RO
  Call CloseFileWheel
  SetOutPath "$INSTDIR"

  ; Restes de l'ancien installeur PowerShell (versions 0.1.x)
  Delete "$INSTDIR\Uninstall-FileWheel.ps1"
  Delete "$INSTDIR\DESINSTALLER.bat"
  Delete "$INSTDIR\*.ancien-*"
  ; Binaires de la version precedente : remplaces integralement (les reglages sont ailleurs)
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\*.exe"
  Delete "$INSTDIR\*.json"
  Delete "$INSTDIR\*.pdb"
  RMDir /r "$INSTDIR\docs"

  File /r "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Menu Demarrer
  CreateShortCut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0 SW_SHOWNORMAL "" "Roue d'actions et de conversion pour vos fichiers"

  ; Parametres Windows > Applications
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${EXE},0"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
  ${GetTime} "" "L" $0 $1 $2 $3 $4 $5 $6
  WriteRegStr HKCU "${UNINST_KEY}" "InstallDate" "$2$1$0"
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" $0
SectionEnd

Section "Lancer FileWheel au démarrage de Windows" SecStartup
  WriteRegStr HKCU "${RUN_KEY}" "${APP}" '"$INSTDIR\${EXE}" --arriere-plan'
SectionEnd

Section /o "Raccourci sur le Bureau" SecDesktop
  CreateShortCut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
SectionEnd

; ------------------------------------------------------------------------------- desinstallation
Section "un.Programme FileWheel" UnSecMain
  SectionIn RO
  Call un.CloseFileWheel
  DeleteRegValue HKCU "${RUN_KEY}" "${APP}"
  Delete "$SMPROGRAMS\${APP}.lnk"
  Delete "$DESKTOP\${APP}.lnk"
  DeleteRegKey HKCU "${UNINST_KEY}"
  ; Garde-fou : on ne supprime recursivement que le dossier d'installation de FileWheel
  ${GetFileName} "$INSTDIR" $0
  ${If} $0 == "${APP}"
    RMDir /r "$INSTDIR"
  ${Else}
    Delete "$INSTDIR\Uninstall.exe"
  ${EndIf}
SectionEnd

Section "un.Moteurs téléchargés, journaux et fichiers temporaires" UnSecData
  RMDir /r "$LOCALAPPDATA\${APP}"
SectionEnd

Section /o "un.Mes réglages, historique et actions personnalisées" UnSecSettings
  RMDir /r "$APPDATA\${APP}"
SectionEnd

; ------------------------------------------------------------------------------- fonctions
Function .onInit
  StrCpy $MaintUninstall 0
  ReadRegStr $OldVersion HKCU "${UNINST_KEY}" "DisplayVersion"
  ReadRegStr $0 HKCU "${UNINST_KEY}" "InstallLocation"
  ${If} $0 != ""
    StrCpy $INSTDIR $0
  ${EndIf}
  ${If} $OldVersion == ""
    StrCpy $Mode "install"
  ${Else}
    ${VersionCompare} "${VERSION}" "$OldVersion" $1
    ${If} $1 == 0
      StrCpy $Mode "repair"
    ${ElseIf} $1 == 1
      StrCpy $Mode "update"
    ${Else}
      StrCpy $Mode "downgrade"
    ${EndIf}
  ${EndIf}
  ; Mise a jour : les options de la premiere installation sont reprises telles quelles
  ${If} $Mode != "install"
    ReadRegStr $2 HKCU "${RUN_KEY}" "${APP}"
    ${If} $2 == ""
      SectionSetFlags ${SecStartup} 0
    ${EndIf}
    ${If} ${FileExists} "$DESKTOP\${APP}.lnk"
      SectionSetFlags ${SecDesktop} ${SF_SELECTED}
    ${EndIf}
  ${EndIf}
FunctionEnd

Function WelcomePre
  ${If} $Mode == "repair"
    Abort ; page de maintenance a la place
  ${EndIf}
FunctionEnd

Function WelcomeShow
  ${If} $Mode == "update"
    ${NSD_SetText} $mui.WelcomePage.Title "Mise à jour de FileWheel"
    ${NSD_SetText} $mui.WelcomePage.Text "$(UpdateText)"
  ${ElseIf} $Mode == "downgrade"
    ${NSD_SetText} $mui.WelcomePage.Text "$(DowngradeText)"
  ${EndIf}
FunctionEnd

Function MaintenancePage
  ${If} $Mode != "repair"
    Abort
  ${EndIf}
  !insertmacro MUI_HEADER_TEXT "FileWheel ${VERSION} est déjà installé" "Que voulez-vous faire ?"
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 24u "Cette version est déjà installée sur cet ordinateur."
  Pop $0
  ${NSD_CreateRadioButton} 10u 34u 100% 14u "Réparer : réinstaller les fichiers du programme (réglages conservés)"
  Pop $RadioRepair
  ${NSD_CreateRadioButton} 10u 54u 100% 14u "Désinstaller FileWheel"
  Pop $RadioUninstall
  ${NSD_Check} $RadioRepair
  nsDialogs::Show
FunctionEnd

Function MaintenanceLeave
  ${NSD_GetState} $RadioUninstall $0
  ${If} $0 == ${BST_CHECKED}
    ; Lance le desinstalleur en place et attend la fin, puis ferme cet assistant
    ${If} ${FileExists} "$INSTDIR\Uninstall.exe"
      HideWindow
      ExecWait '"$INSTDIR\Uninstall.exe" _?=$INSTDIR' $1
      ${If} $1 == 0
        Delete "$INSTDIR\Uninstall.exe"
        RMDir "$INSTDIR"
      ${EndIf}
    ${Else}
      MessageBox MB_ICONEXCLAMATION "Le désinstalleur est introuvable. Supprimez FileWheel depuis Paramètres Windows › Applications."
    ${EndIf}
    Quit
  ${EndIf}
FunctionEnd

Function ComponentsPre
  ; Mise a jour / reparation : les choix precedents sont repris, pas besoin de les redemander
  ${If} $Mode == "update"
  ${OrIf} $Mode == "repair"
    Abort
  ${EndIf}
FunctionEnd

Function LaunchApp
  ; L'assistant tourne sans elevation : FileWheel demarre au meme niveau que l'Explorateur
  Exec '"$INSTDIR\${EXE}" --apres-installation'
FunctionEnd

; Ferme FileWheel : demande polie (--quitter), attente jusqu'a 10 s, puis arret force.
; (Deux copies : NSIS separe les fonctions de l'installeur et du desinstalleur.)
Function CloseFileWheel
  ${If} ${FileExists} "$INSTDIR\${EXE}"
    ExecWait '"$INSTDIR\${EXE}" --quitter'
  ${EndIf}
  StrCpy $R1 0
  ${Do}
    nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq ${EXE}" /NH'
    Pop $0
    Pop $1
    ${StrStr} $2 $1 "${EXE}"
    ${If} $2 == ""
      ${Break}
    ${EndIf}
    ${If} $R1 >= 20
      nsExec::Exec 'taskkill /F /IM ${EXE}'
      Pop $0
      Sleep 1000
      ${Break}
    ${EndIf}
    Sleep 500
    IntOp $R1 $R1 + 1
  ${Loop}
FunctionEnd

Function un.CloseFileWheel
  ${If} ${FileExists} "$INSTDIR\${EXE}"
    ExecWait '"$INSTDIR\${EXE}" --quitter'
  ${EndIf}
  StrCpy $R1 0
  ${Do}
    nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq ${EXE}" /NH'
    Pop $0
    Pop $1
    ${UnStrStr} $2 $1 "${EXE}"
    ${If} $2 == ""
      ${Break}
    ${EndIf}
    ${If} $R1 >= 20
      nsExec::Exec 'taskkill /F /IM ${EXE}'
      Pop $0
      Sleep 1000
      ${Break}
    ${EndIf}
    Sleep 500
    IntOp $R1 $R1 + 1
  ${Loop}
FunctionEnd

Function un.onInit
  ; Silencieux (/S) : programme + moteurs, reglages conserves
FunctionEnd
