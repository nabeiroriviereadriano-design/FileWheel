# Contribuer à FileWheel

Merci de votre intérêt ! FileWheel est un projet libre (MIT). Les échanges se font en français ou en anglais.

## Signaler un problème

Ouvrez une *Issue* avec :
- la version de FileWheel (*Paramètres › À propos*) et de Windows ;
- ce que vous faisiez (type de fichier, Explorateur ou Bureau, touche utilisée) et ce qui s'est passé ;
- le journal : menu de l'icône FileWheel › *Dépannage › Ouvrir le dossier des journaux*. Le mode diagnostic (*Dépannage › Mode diagnostic*) ajoute le détail du geste.

Relisez le journal avant de le joindre : il contient des noms de fichiers et de dossiers.

Faille de sécurité : ne l'exposez pas dans une Issue publique, utilisez *Security › Report a vulnerability* du dépôt.

## Proposer une modification

1. Créez une branche depuis `main`.
2. Respectez l'architecture ([docs/DEVELOPPEUR.md](docs/DEVELOPPEUR.md)) :
   - logique pure (geste, roue, opérations de fichiers, paramètres) → `src/FileWheel.Core`, **avec un test** ;
   - conversions → un fournisseur dans `src/FileWheel.Conversion/Providers`, qui **déclare** ses capacités réelles (pertes et limites comprises) et un test qui vérifie le **contenu** de la sortie ;
   - interop Windows → `src/FileWheel.Windows` ; interface → `src/FileWheel.App` (WPF en C#, styles de `Ui/Theme.cs`, voir [docs/CHARTE-GRAPHIQUE.md](docs/CHARTE-GRAPHIQUE.md)).
3. Vérifiez :
   - sous Windows : `CREER-INSTALLATEUR.bat` compile tout et produit l'installateur ;
   - sous Linux : `build/ci-linux.sh` compile le moteur et lance les tests ;
   - GitHub Actions compile la version Windows et l'installateur pour chaque Pull request.
4. Si vous ajoutez une conversion : `dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md`.

## Règles du projet

- Ne jamais casser le glisser-déposer natif de Windows.
- Ne jamais modifier ni écraser un original ; écrire via un fichier temporaire puis renommer vers un nom libre.
- Rien n'est envoyé sur Internet ; un moteur externe n'est téléchargé qu'avec l'accord explicite de l'utilisateur, depuis sa source officielle, avec vérification SHA-256.
- Aucune conversion annoncée qui ne soit réellement implémentée et testée.
- L'installateur est toujours livré avec son désinstalleur (`Uninstall.exe`, entrée « Applications installées »).
- Aucun programme tiers n'est ajouté au dépôt.

En proposant une contribution, vous acceptez qu'elle soit publiée sous licence MIT.
