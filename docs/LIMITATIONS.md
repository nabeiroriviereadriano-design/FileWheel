# Limites connues de FileWheel 0.2.0

Ce document liste honnêtement ce qui n'est pas fait, ce qui n'a pas pu être vérifié, et pourquoi.

## Ce qui n'a pas été exécuté sur Windows

FileWheel a été développé et testé dans un environnement Linux : le moteur de conversion, la logique de la roue (déclenchement, écrans multiples, menus, interaction) et les opérations de fichiers y ont été testés **réellement** (195 tests réussis, voir RESULTATS-TESTS.md).

Les parties propres à Windows sont **compilées et vérifiées par typage** contre les assemblées WPF de référence, mais **n'ont pas pu être lancées** :
- crochets clavier/souris, masquage du menu Alt, glisser-déposer OLE au-dessus de l'Explorateur ;
- API Shell (Corbeille, Propriétés, Ouvrir avec, Partager, raccourcis) ;
- icône de notification, démarrage automatique, instance unique ;
- toutes les fenêtres WPF et la charte graphique (aucune capture d'écran réelle n'a pu être faite) ;
- conversions WIC ;
- outils image (recadrer, fond, compresser, annoter, métadonnées) et nouvelle roue Express : compilés, jamais exécutés ;
- l'installateur `FileWheel-Setup.exe` a été testé ici sous Wine (installation, mise à jour, désinstallation silencieuses, écrans Bienvenue / Mise à jour / Maintenance) ; le script de construction `Build-Setup.ps1` n'a pas pu être exécuté hors Windows.

La roue elle-même (déclenchement, affichage, dépôt) a été validée sur le PC de l'utilisateur en version 0.1.5.

La compilation réelle se fait sur votre PC au moment de `INSTALLER.bat`. Si une erreur apparaît à cette étape ou au premier lancement, le message et le journal (`%LOCALAPPDATA%\FileWheel\logs`) permettent de la corriger. Liste de contrôle : VERIFICATION-WINDOWS.md.

## Déclenchement

- Une application lancée **en administrateur** ne reçoit pas les fichiers glissés depuis un Explorateur non élevé (règle de Windows, UIPI). FileWheel doit tourner au même niveau que l'Explorateur.
- Dans les fenêtres de l'Explorateur, l'élément sous le curseur est vérifié via l'Automatisation de l'interface (UIA) avec un délai maximal ; si Windows ne répond pas à temps, le mode « tolérant » (par défaut) affiche la roue quand même.
- Les glisser depuis d'autres applications (navigateur, Outlook, archives ouvertes dans l'Explorateur, téléphone MTP) ne fournissent pas toujours de vrais chemins de fichiers : la roue l'indique et ne propose rien.
- Sur certains claviers, Alt peut entrer en conflit avec des outils tiers ; changez la combinaison dans les paramètres.

## Interface

- **Flou d'arrière-plan** de la roue : expérimental (API non documentée de Windows), désactivé par défaut.
- **Partager** : utilise la feuille de partage Windows quand elle est disponible ; sinon FileWheel ouvre l'Explorateur sur les fichiers.
- Les animations de l'interrupteur à bascule ne sont pas animées (changement d'état instantané).
- Les boutons de fenêtre restent ceux de Windows (pas de « feux tricolores » macOS), volontairement.

## Conversion

Non pris en charge (et donc jamais proposé) :
- **OCR** : un PDF scanné ou une image ne donne pas de texte ;
- **CAO** : STEP, IGES, DXF/DWG (reconnus, non convertis) ; **FBX** (format propriétaire) ;
- **WOFF2** (reconnu, non converti) ;
- **création d'archives RAR** (format propriétaire) ; l'extraction RAR nécessite 7-Zip ;
- e-mails **MSG/PST** d'Outlook ;
- livres protégés par **DRM** (refusés avec un message).

Pertes à connaître (toujours annoncées avant de convertir) :
- PDF → DOCX/ODT : reconstruction approximative ;
- 3D : géométrie seule (matériaux, textures, animations perdus) ;
- texte → PDF intégré : polices standard PDF, caractères hors alphabet latin occidental remplacés par « ? » (utilisez LibreOffice pour les autres alphabets) ;
- formats avec perte (JPEG, MP3, AAC, H.264…) : perte de qualité à chaque réencodage ; FileWheel copie sans réencoder quand c'est possible (remuxage vidéo).

## Outils image

- Ils utilisent les codecs de Windows : WebP, HEIC et AVIF ne sont lisibles que si les extensions correspondantes sont installées (Microsoft Store) ; PSD, RAW, SVG, EXR passent d'abord par « Convertir › PNG ».
- Windows ne sait pas écrire le WebP, le HEIC ni l'AVIF : le résultat d'un outil est alors enregistré en PNG.
- Compresser en PNG est sans perte : le gain vient surtout de la taille maximale. Le JPEG est réencodé (léger effet à chaque passage).
- « Copie sans métadonnées » réécrit les pixels : pour un JPEG, c'est un réencodage en qualité 95.
- Annoter : le texte est saisi dans une petite fenêtre puis placé au clic (pas d'édition sur place).

## Tâches

- **Pause** : aucun moteur actuel ne permet de suspendre proprement une conversion en cours ; seule l'annulation est proposée (le fichier partiel est supprimé). La file d'attente, elle, peut être mise en pause.
- Le temps restant est estimé à partir de la progression rapportée par le moteur ; certains moteurs (LibreOffice, Calibre) n'en rapportent pas : la barre est alors indéterminée.

## Moteurs

- Les empreintes des moteurs ne sont pas figées dans le code : FileWheel exige l'empreinte SHA-256 publiée par GitHub pour chaque fichier téléchargé et refuse l'installation si elle manque ou ne correspond pas.
- LibreOffice et Calibre s'installent via winget (Windows 10 récent / Windows 11) ; sans winget, installez-les à la main, FileWheel les détectera.
