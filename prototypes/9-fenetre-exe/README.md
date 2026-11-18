# Prototype de #9 : la fenêtre hôte dans un exe d'un seul fichier

Prototype jetable de la story #9. Il vérifie que le montage de l'[ADR-0003](../../docs/adr/0003-hote-interface-webview2.md) fonctionne en .NET 10 et se publie en exe autonome d'un seul fichier.

## Hors de la solution livrée

Aucun projet de Sollicito ne le référence, et il ne doit pas être ajouté à la solution. Ses propres fichiers l'isolent de ceux que le socle (#8) pose à la racine du dépôt :

| Fichier du prototype | Effet |
| --- | --- |
| `global.json` | SDK 10.0.404, sans montée de version |
| `Directory.Build.props`, `Directory.Build.targets` | arrêtent la recherche des fichiers parents : ni `TreatWarningsAsErrors`, ni `net10.0` hérités |
| `Directory.Packages.props` | gestion centrale des versions coupée : les versions restent dans `Prototype9.csproj`, sans NU1008 |
| `nuget.config` | nuget.org seul |
| `.editorconfig` | `root = true` : aucune règle héritée |

Essai du 18/11/2026 : une copie du prototype, posée sous les quatre fichiers racine de la branche de #8, restaure en mode verrouillé et compile sans avertissement ni erreur. La CI de #8 ne construit que `Sollicito.slnx` et `interface/`, elle ignore donc le prototype.

## Ce qu'il fait

- Une fenêtre WinForms héberge WebView2 et affiche une page servie par Kestrel, dans le même exe, sur `127.0.0.1` et un port libre choisi au lancement.
- La page lit, par l'API locale, une base SQLite créée dans un dossier temporaire. Les données sont fictives.
- Un message va de la page à l'hôte (`postMessage`, source contrôlée), puis de l'hôte à la page (`PostWebMessageAsJson`), qui renvoie un accusé de réception.
- `NavigationStarting` annule toute navigation hors de l'origine de la page. L'autotest en essaie deux : vers `https://autre-origine.invalid/`, un domaine réservé qui ne se résout jamais, et vers `http://localhost:<port>/`, le même serveur sous une autre origine.
- En mode `--autotest`, il écrit un rapport JSON et une capture de la page, puis se ferme. Il rend 0 si tous les contrôles passent.

Le reste du durcissement (jeton, contrôle de l'en-tête `Host`, permissions, nouvelles fenêtres, outils de développement, CSP) n'est pas prototypé : il revient aux stories de l'API locale et de la fenêtre. Lancé à la main, sans `--autotest`, le prototype laisse donc son API lisible par les autres programmes du poste tant que sa fenêtre est ouverte ; ses données sont fictives.

## Vérifier

Depuis ce dossier, sous Windows, avec le SDK .NET 10.0.404 et le runtime WebView2 :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verifier.ps1
```

Le script travaille dans un dossier d'essai neuf sous `%TEMP%`. Il restaure les paquets en mode verrouillé, publie l'exe et vérifie que la publication ne contient qu'un fichier. Il copie ensuite l'exe seul dans un dossier vide, le lance deux fois et affiche chaque contrôle, puis supprime le dossier d'essai. Une fenêtre s'ouvre environ une seconde à chaque lancement.

- `-Compresse` publie avec la compression du fichier unique.
- `-Preuves` copie dans `preuves/` le rapport et la capture du premier lancement, et un résumé de la publication.
- `-Garder` garde le dossier d'essai.

Ce qui reste sur le poste : .NET extrait les bibliothèques natives de l'exe sous `%TEMP%\.net\Prototype9`, dans un dossier de 2,6 Mo par version du code publié, qui reste après la fermeture.

## Résultat du 18/11/2026

Réussi, en exe normal comme compressé : les dix contrôles passent à chaque lancement. Détail dans l'ADR-0003, point « À vérifier » sur .NET 10, et dans `preuves/`, pour chaque variante (`normal`, `compresse`) :

- `rapport-<variante>.json` : contrôles, versions chargées, modules, fichiers extraits, requêtes reçues par Kestrel, étapes datées ;
- `fenetre-<variante>.png` : la page dans la fenêtre, à la fin de l'autotest ;
- `publication-<variante>.json` : taille de l'exe, durées des deux lancements, extraction neuve ou réutilisée.

Après la fermeture de la fenêtre, le processus attend encore quelques secondes la fin du processus du navigateur WebView2, qui garde le dossier de profil verrouillé.
