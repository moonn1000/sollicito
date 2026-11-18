# Prototype de #9 : la fenêtre hôte dans un exe d'un seul fichier

Prototype jetable de la story #9. Il vérifie que le montage de l'[ADR-0003](../../docs/adr/0003-hote-interface-webview2.md) fonctionne en .NET 10 et se publie en exe autonome d'un seul fichier.

Il ne rejoint pas la solution livrée : aucun projet de Sollicito ne le référence. Ses fichiers `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `nuget.config` et `.editorconfig` l'isolent de ceux de la racine du dépôt.

## Ce qu'il fait

- Une fenêtre WinForms héberge WebView2 et affiche une page servie par Kestrel, dans le même exe, sur `127.0.0.1` et un port libre choisi au lancement.
- La page lit, par l'API locale, une base SQLite créée dans un dossier temporaire. Les données sont fictives.
- Un message va de la page à l'hôte (`postMessage`, source contrôlée), puis de l'hôte à la page (`PostWebMessageAsJson`), qui renvoie un accusé de réception.
- `NavigationStarting` annule toute navigation hors de l'origine de la page. L'autotest en essaie deux : vers `https://autre-origine.invalid/`, un domaine réservé qui ne se résout jamais, et vers `http://localhost:<port>/`, le même serveur sous une autre origine.
- En mode `--autotest`, il écrit un rapport JSON et une capture de la page, puis se ferme. Il rend 0 si tous les contrôles passent.

Le reste du durcissement de la fenêtre (permissions, nouvelles fenêtres, outils de développement, CSP) n'est pas prototypé : il revient à la story de la fenêtre.

## Vérifier

Depuis ce dossier, sous Windows, avec le SDK .NET 10.0.404 et le runtime WebView2 :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verifier.ps1
```

Le script restaure les paquets en mode verrouillé, publie l'exe et vérifie que la publication ne contient qu'un fichier. Il le copie ensuite seul dans un dossier temporaire vide, le lance deux fois et affiche chaque contrôle. Une fenêtre s'ouvre environ une seconde à chaque lancement.

- `-Compresse` publie avec la compression du fichier unique.
- `-Preuves` copie le rapport et la capture du premier lancement dans `preuves/`, avec un résumé de la publication.

## Résultat du 18/11/2026

Réussi, en exe normal comme compressé : les dix contrôles passent à chaque lancement. Détail dans l'ADR-0003, point « À vérifier » sur .NET 10, et dans `preuves/` :

- `rapport-autotest.json` : contrôles, versions chargées, modules, requêtes reçues par Kestrel, étapes datées ;
- `fenetre.png` : la page dans la fenêtre, à la fin de l'autotest ;
- `publication-normal.json` et `publication-compresse.json` : taille de l'exe et durées des lancements.

Après la fermeture de la fenêtre, le processus attend encore quelques secondes la fin du processus du navigateur WebView2, qui garde le dossier de profil verrouillé.
