# Sécurité dans FileWheel

## Principes

| Règle | Mise en œuvre |
|---|---|
| Ne jamais casser le glisser-déposer natif | La roue est une cible de dépôt OLE ordinaire : elle ne bloque aucun clic, n'injecte aucun mouvement de souris, et répond toujours « Copier » (jamais « Déplacer ») pour que l'Explorateur source ne supprime pas les originaux. Lâcher ailleurs = dépôt Windows normal. |
| Originaux intacts | Toute conversion écrit dans un fichier temporaire `.~fw-*.tmp` du dossier cible, le valide, puis le renomme. Une sortie ne peut jamais viser un fichier d'entrée. |
| Aucun écrasement implicite | Conflit de nom = nouveau nom « (2) » par défaut ; « Remplacer » n'existe que sur choix explicite. |
| Local par défaut | Aucune conversion n'envoie de données sur le réseau. Les seuls accès réseau sont les téléchargements de moteurs que vous lancez vous-même. |
| Pas d'exécutable inconnu | Les moteurs viennent de dépôts officiels listés dans le code, sont vérifiés par SHA-256 et installés dans `%LOCALAPPDATA%\FileWheel\engines`. Aucun téléchargement automatique. |
| Pas de shell | Tous les programmes sont lancés avec une liste d'arguments (`ProcessStartInfo.ArgumentList`) : aucun nom de fichier n'est interprété par `cmd.exe` ou PowerShell. |
| Pas d'élévation | `asInvoker` : FileWheel ne demande jamais de droits administrateur. |

## Archives

- **Zip slip** : chaque entrée est résolue et doit rester dans le dossier d'extraction ; chemins absolus, `..`, lettres de lecteur, flux NTFS (`:`) et noms réservés (CON, NUL…) sont ignorés.
- **Liens** symboliques et matériels : ignorés.
- **Bombes de décompression** : plafonds de taille totale (20 Gio), de nombre d'entrées (200 000) et de taux de compression (×250), réglables.
- **Jamais d'écrasement** : extraction dans un nouveau dossier numéroté si le nom existe.
- Les entrées ignorées sont signalées à la fin et détaillées dans le journal.

## Actions personnalisées

- Types limités : lancer un programme, copier/déplacer vers un dossier, convertir.
- « Lancer un programme » : chemin absolu obligatoire, **empreinte SHA-256 mémorisée** à l'enregistrement ; si le programme change, FileWheel demande confirmation avant de le lancer.
- Avertissement à l'enregistrement pour les interpréteurs (cmd, PowerShell, wscript, cscript, mshta, bash, WSL).
- Les jetons `{path}`, `{name}`… deviennent des arguments distincts ; aucun shell n'interprète la ligne.

## Fichiers dangereux

- Programmes et scripts (`.exe`, `.bat`, `.ps1`, `.vbs`, `.js`, `.msi`…) ne sont jamais convertis ; les ouvrir depuis la roue demande une confirmation.
- Le format réel est détecté par signature (contenu), pas seulement par l'extension.
- E-mails convertis en HTML : scripts, gestionnaires d'événements et images distantes retirés.
- SVG : un fichier qui déclare des entités XML ou référence une ressource externe (href, src, url()) est refusé avant tout rendu par ImageMagick.

## Données personnelles

- Historique : noms de fichiers, conversion, date et résultat — jamais le contenu. Désactivable et effaçable.
- Journal : le dossier du profil utilisateur est masqué (`%USERPROFILE%`), le contenu des fichiers n'y figure jamais ; conservation 14 jours.
