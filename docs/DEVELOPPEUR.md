# Documentation développeur

## Pile technique

- **C# / .NET 10**, interface **WPF écrite en C#** (pas de XAML : les styles sont construits en code dans `Ui/Theme.cs`).
- Thème Fluent de WPF (`ThemeMode = System`) pour les champs et listes, surchargé par la charte hybride.
- Aucune dépendance NuGet à l'exécution. Moteurs externes appelés comme programmes séparés (ou PDFium chargé dynamiquement).
- Manifeste : `asInvoker`, DPI `PerMonitorV2`, chemins longs.

## Arborescence

```
FileWheel/
├─ .github/workflows/build.yml          GitHub Actions : compilation Windows + installateur ; Release sur tag v*
├─ INSTALLER.bat                        construit FileWheel-Setup-<version>.exe puis le lance
├─ CREER-INSTALLATEUR.bat               construit seulement le .exe
├─ DESINSTALLER.bat                     lance Uninstall.exe de l'installation
├─ installer/
│  ├─ Build-Setup.ps1                   SDK .NET 10 + publication autonome win-x64 + NSIS → FileWheel-Setup-<version>.exe
│  └─ FileWheel.nsi                     installateur NSIS : installer / mettre à jour / réparer / désinstaller
├─ assets/                              filewheel.ico (16 à 256 px), filewheel.png, filewheel.svg, installer-side.bmp,
│                                       logo_gen.py / logo_icon.py (sources du logo)
├─ Directory.Build.props                version, options communes ; FwIsWindows selon l'OS
├─ build/
│  ├─ WindowsDesktop.props              net10.0-windows + UseWPF sous Windows ; vérification de types ailleurs
│  └─ ci-linux.sh                       compilation + tests hors Windows
├─ src/
│  ├─ FileWheel.Core/                   sans dépendance Windows, testé
│  │  ├─ Input/                         détecteur du geste (machine à états), combinaison, écrans/DPI, effet de dépôt
│  │  ├─ Wheel/                         éléments, géométrie, interaction (survol, sous-menus), construction du menu
│  │  ├─ Formats/                       catalogue des formats, détection par signature
│  │  ├─ FileOperations/                copie/déplacement/renommage sûrs, validation des noms
│  │  ├─ Actions/                       identifiants de commandes, actions personnalisées
│  │  ├─ Settings/ Logging/ Util/        paramètres JSON atomiques, journal, chemins
│  ├─ FileWheel.Conversion/             moteur de conversion, sans dépendance Windows, testé
│  │  ├─ Core/                          contrats, registre, gestionnaire, file de tâches, processus, sécurité archives,
│  │  │                                 planification et validation des sorties, historique, profils
│  │  ├─ Engines/                       catalogue des moteurs, téléchargement vérifié (SHA-256), winget
│  │  └─ Providers/                     Media (FFmpeg), Images (ImageMagick), Archives, Data, Text, Pdf (PDFium),
│  │                                    Office (LibreOffice), Ebooks (Calibre), Model3D, Fonts, Mail
│  ├─ FileWheel.Windows/                interop Windows : crochets, UIA, Shell, zone de notification, DWM, WIC
│  └─ FileWheel.App/                    exe : application résidente, roue, fenêtres, exécution des actions
│     ├─ Program.cs                     point d'entrée, instance unique, --arriere-plan / --parametres / --quitter
│     ├─ FileWheelApp.cs                assemblage : crochets → roue → actions, icône, notifications
│     ├─ ActionExecutor.cs              exécution de chaque commande de la roue
│     ├─ Tools/                         outils image : ImageIO (lecture/écriture sûres), ToolWindow (base),
│     │                                 CropWindow, BackgroundWindow, CompressWindow, AnnotateWindow, MetadataWindow
│     └─ Ui/                            Theme (charte), Ui (constructeurs), WheelWindow, TrayGlyph (icône de notification),
│                                       Dialogs, SettingsWindow, QuickConvertWindow, ConverterWindow, TasksWindow, CustomActionDialog
├─ tests/FileWheel.Tests/               harnais maison : moteurs réels, fichiers générés, sorties vérifiées
└─ docs/
```

## Flux principal

1. `InputHookService` (thread dédié) reçoit souris et clavier, classe la fenêtre sous le curseur (Explorateur/Bureau) et nourrit `DragGestureDetector`.
2. Le détecteur émet `ShowWheel(x, y)` quand : bouton enfoncé dans une zone autorisée + distance minimale + combinaison exacte, stables pendant le délai, et vérification UIA de l'élément non négative.
3. `WheelWindow` s'affiche (sans prendre le focus) et devient **cible de dépôt OLE** : à l'entrée du glisser, elle lit `CF_HDROP`, construit le menu (`WheelMenuBuilder`), suit le survol, et au dépôt renvoie `Copier` à l'Explorateur.
4. `ActionExecutor` exécute la commande ; les conversions passent par `ConversionEngine.Queue`, les opérations de fichiers par `OperationsCenter` (arrière-plan, progression, annulation).

## Ajouter un format de conversion

1. Déclarer le format dans `FormatCatalog` (et sa signature dans `FormatDetector` si besoin).
2. Dans un fournisseur (`ProviderBase`), déclarer la capacité avec `Cap(...)` : sources, cible, type, options, perte, limites, moteur.
3. Implémenter `ConvertAsync` : écrire dans `request.TempOutput`, signaler la progression, lever `ConversionException` avec un type d'erreur clair.
4. Ajouter un test dans `tests/FileWheel.Tests/ConversionTests.cs` qui vérifie le **contenu** de la sortie.
5. Régénérer la liste : `dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md`.

## Compiler et créer l'installateur

Sous Windows : `INSTALLER.bat` (ou `CREER-INSTALLATEUR.bat`) exécute `installer/Build-Setup.ps1` :

```
dotnet publish src\FileWheel.App\FileWheel.App.csproj -c Release -r win-x64 --self-contained true -o publish
makensis /DVERSION=<version> /DPUBLISH_DIR=<...>\publish /DOUTFILE=<...>\FileWheel-Setup-<version>.exe installer\FileWheel.nsi
```

La version vient de `Directory.Build.props`. L'installateur :
- s'installe dans `%LOCALAPPDATA%\Programs\FileWheel` (HKCU, aucun droit administrateur) ;
- détecte la version installée (`HKCU\...\Uninstall\FileWheel\DisplayVersion`) : installation, mise à jour, retour arrière ou maintenance (réparer / désinstaller) ;
- ferme FileWheel (`--quitter`, puis arrêt forcé après 10 s) avant de remplacer les fichiers ;
- reprend les choix de la première installation (démarrage avec Windows, raccourci Bureau) lors d'une mise à jour ;
- écrit `Uninstall.exe` et l'entrée « Applications installées » ; la désinstallation garde les réglages sauf si l'on coche la case ; `/S` = silencieux.

### Intégration continue et publication (GitHub Actions)

`.github/workflows/build.yml` tourne sur `windows-latest` à chaque push sur `main` et chaque Pull request : vraie compilation WPF, publication autonome win-x64, puis `makensis`. L'installateur est joint à l'exécution (*Actions › exécution › Artifacts*).

Publier une version :
1. changer `<Version>` dans `Directory.Build.props` (et `AssemblyVersion` / `FileVersion`) ;
2. compléter `docs/RAPPORTS-ETAPES.md` ;
3. `git tag v<version> && git push origin v<version>` : le workflow crée la Release GitHub avec `FileWheel-Setup-<version>.exe`.

Test hors Windows : `makensis -DTARGET_AMD64 ...` produit une variante 64 bits exécutable sous Wine (`wine64 Setup.exe /S`).

Hors Windows : `build/ci-linux.sh` compile le moteur, lance les tests, et compile les projets Windows en **vérification de types** contre une assemblée de référence WPF (`build/wpfref/WpfRefAll.dll`, non distribuée). Le symbole `FW_TYPECHECK` est défini et, comme sous Windows avec `UseWPF`, `System.IO` n'est pas importé implicitement.

## Tests

```
dotnet run --project tests/FileWheel.Tests -c Release            # tout
dotnet run --project tests/FileWheel.Tests -c Release -- Archives # filtre sur le nom
```

Le harnais génère ses fichiers d'essai avec les vrais outils (ffmpeg, ImageMagick, LibreOffice, Python), lance les vraies conversions et vérifie les sorties (dimensions, durée, pistes, texte relu, empreintes, contenu d'archive…). Un test dont l'outil manque est marqué « non exécuté » avec la raison. Le rapport est écrit dans `RESULTATS-TESTS.md` à côté du binaire (ou dans `FILEWHEEL_TEST_OUTPUT`).

Les tests sont conçus pour l'environnement de référence Linux (ils utilisent aussi `pdftotext`, `pgrep`, `python3` et ses modules pour les vérifications) ; la partie Windows se vérifie avec `docs/VERIFICATION-WINDOWS.md`.

## Données et emplacements

| Quoi | Où |
|---|---|
| Programme | `%LOCALAPPDATA%\Programs\FileWheel` |
| Paramètres, historique, actions, profils, chemins de moteurs | `%APPDATA%\FileWheel\*.json` (écriture atomique, sauvegarde `.corrompu-*` si illisible) |
| Moteurs, journaux, téléchargements, temporaires | `%LOCALAPPDATA%\FileWheel\` |
| Démarrage automatique | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\FileWheel` = `"…\FileWheel.exe" --arriere-plan` |
| Entrée « Applications installées » | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\FileWheel` |

`FILEWHEEL_HOME` (variable d'environnement) redirige paramètres et données vers un autre dossier (tests, version portable).
