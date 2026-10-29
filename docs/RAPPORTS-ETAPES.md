# Rapports d'étape

Chaque étape suit la méthode demandée : ce qui a été fait, comment c'est vérifié, ce qui reste incertain.

## Étape 1 — Faisabilité du déclenchement « glisser + touche »

**Question** : peut-on afficher une roue pendant un glisser natif de l'Explorateur sans le casser ?

**Réponse : oui, avec une architecture précise.**
- Pendant un glisser OLE, l'Explorateur garde la capture de la souris : impossible de « voler » le glisser. FileWheel l'**observe** (crochets bas niveau `WH_MOUSE_LL` / `WH_KEYBOARD_LL`, sans rien bloquer) et affiche une fenêtre au-dessus.
- Cette fenêtre est une **cible de dépôt OLE** : Windows lui transmet les fichiers (`CF_HDROP`) quand le curseur la survole — c'est la seule source fiable de la liste des fichiers.
- Elle répond **Copier** au dépôt : l'Explorateur ne supprime donc jamais l'original, même si l'utilisateur faisait un déplacement.
- Le relâchement d'Alt déclencherait la barre de menus de l'Explorateur : il est précédé d'une touche neutre injectée (technique standard).
- Contrainte découverte : une application élevée ne reçoit pas les dépôts d'un Explorateur non élevé (UIPI) → manifeste `asInvoker`, avertissement dans l'installeur.

**Vérification** : la logique de décision (22 cas : combinaisons exactes, AltGr, Alt+Tab, délais, zone vide…) est une machine à états testée sans Windows. Les crochets eux-mêmes sont à valider sur PC.

## Étapes 2–3 — Architecture, roue et actions sur les fichiers

- Séparation en 4 projets : `Core` (logique pure, testée), `Conversion` (moteur, testé), `Windows` (interop), `App` (interface).
- Roue : géométrie et interaction (survol, sous-menus à temporisation, mode clic après dépôt sur une branche) dans `Core/Wheel`, testées ; placement multi-écrans et DPI testé (100 %, 125 %, 200 %, coordonnées négatives, bords).
- Opérations de fichiers sûres : copie/déplacement via fichier temporaire vérifié, jamais d'écrasement implicite, renommage avec validation des noms Windows (réservés, caractères interdits, longueurs).

## Étapes 4–5 — Moteur de conversion et modules

- Contrat unique `IConversionProvider` : chaque fournisseur **déclare** ses conversions réelles (sources, cible, options, pertes, limites, moteur). Le menu, le convertisseur et la liste `CONVERSIONS.md` sont générés depuis ces déclarations : rien n'est annoncé qui ne soit implémenté.
- Fournisseurs intégrés écrits pour FileWheel : documents (Markdown, HTML, texte, PDF, EPUB), données (CSV/TSV/JSON/XML/YAML/XLSX), 3D (STL/OBJ/PLY/3MF/glTF/GLB), polices (WOFF↔TTF/OTF), e-mails/contacts/agendas, archives ZIP/TAR/GZ.
- Moteurs externes : FFmpeg (remuxage sans perte quand possible, réencodage sinon, progression, nouvel essai automatique), ImageMagick, 7-Zip, PDFium, LibreOffice, Calibre ; codecs Windows (WIC).
- File de tâches : concurrence réglable, priorité basse, annulation (arbre de processus tué, fichier partiel supprimé), nouvel essai, temps restant.

## Étape 6 — Sécurité et tests réels

- Archives : zip slip, liens, noms réservés, bombes de décompression, mots de passe.
- Processus : arguments séparés, aucun shell ; moteurs vérifiés par SHA-256.
- Tests : **195 réussis, 0 échec**, 2 non exécutés faute d'échantillon (archive 7z piégée impossible à fabriquer ici, pas d'outil RAR), plus 8 points de la partie Windows listés comme non exécutés. Détail : RESULTATS-TESTS.md.

## Étape 7 — Interface, livraison

- Interface WPF complète : roue, conversion rapide, convertisseur avancé, tâches, paramètres (9 sections), dialogues (conflits, renommage en lot, compression, consentement moteur, bienvenue), actions personnalisées.
- **Charte graphique hybride Windows 11 + macOS** (CHARTE-GRAPHIQUE.md), clair/sombre en direct.
- Installeur (`INSTALLER.bat`) et **désinstalleur** (`DESINSTALLER.bat`, entrée « Applications installées »), sans droits administrateur.

**Incertitude restante** : l'exécution réelle sous Windows (crochets, OLE, Shell, WPF) n'a pas pu être faite ici ; elle est cadrée par VERIFICATION-WINDOWS.md.

## Version 0.2.0 — roue Express, outils image, installateur .exe

- **Retours du PC** (0.1.1 → 0.1.5) : 38 erreurs de compilation propres à WPF (`System.IO` implicite retiré), détection de l'Explorateur renforcée, touches synthétiques ignorées, installeur qui attend la fermeture, et surtout la roue qui n'apparaissait pas : le signal « image de glisser visible » déclenchait l'affichage mais son résultat était jeté. Corrigé ; roue validée sur le PC en 0.1.5.
- **Logo et icône de notification** (0.1.6) : logo A choisi, icône monochrome à états (grisée, progression, pastille).
- **Roue Express** : inspirée de la vidéo de référence — formats cibles directement dans la roue, outils pour les images, actions de fichier regroupées. 9 nouveaux tests (204 réussis au total).
- **Outils image** : recadrer, fond, compresser, annoter / masquer / flouter, métadonnées. Compilés, à valider sur Windows.
- **Installateur** : `FileWheel-Setup-<version>.exe` (NSIS) — installation, mise à jour avec reprise des options, réparation, désinstallation. Testé sous Wine : installation, mise à jour (version, options, nettoyage), désinstallation (réglages conservés), écrans Bienvenue / Mise à jour / Maintenance.

## Dépôt public open source

- Publié sur GitHub sous licence MIT : https://github.com/nabeiroriviereadriano-design/FileWheel
- **GitHub Actions** compile la version Windows sur une vraie machine Windows (`windows-latest`) et fabrique l'installateur à chaque modification ; un tag `v<version>` publie `FileWheel-Setup-<version>.exe` dans les Releases.
- Données d'exemple des tests anonymisées ; aucun programme tiers ni binaire de référence dans le dépôt.
