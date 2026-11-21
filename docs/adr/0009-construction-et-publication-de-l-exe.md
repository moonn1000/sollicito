# ADR-0009 : construction et publication de l'exe

- **Statut** : acceptée
- **Date** : 2026-11-21
- **Précise** : [ADR-0004](0004-v1-resserree-paliers.md), « À vérifier » (compression de l'exe, tranchée ici) et « Ce qui change dans l'ADR-0001 », D13 (l'exe construit par l'intégration continue)

## Contexte

L'ADR-0004 publie la V1 en exe autonome d'un seul fichier, construit par l'intégration continue avec son empreinte SHA-256 et son attestation de provenance. Il laissait ouverte la compression de l'exe : 62,2 Mo au lieu de 140,6 Mo au prototype de #9, avec une page prête en moins d'une seconde dans les deux cas.

Faits relevés le 2026-11-21 sur l'exe de #54 (SDK 10.0.404, interface factice ; un mégaoctet vaut ici 10⁶ octets) :

- **Taille** : 154,0 Mo sans compression, 67,3 Mo avec.
- **Chemins de symboles** : hors de l'intégration continue, les DLL des projets que la fenêtre référence gardent le chemin de leur fichier de symboles, donc le dossier de compilation et le nom du compte Windows. `ContinuousIntegrationBuild`, posé par `Directory.Build.props` quand `CI=true`, les réécrit en `/_/…`.
- **Fichiers superflus** : le paquet de l'exe embarquait l'hôte natif de l'API (`Sollicito.Api.exe`, 162 Ko) et le module d'hébergement sous IIS d'ASP.NET Core (`aspnetcorev2_inprocess.dll`, 392 Ko). Ce sont deux fichiers natifs, que l'exe extrait sous `%TEMP%\.net` au premier lancement, et dont il ne se sert pas : l'API tourne dans le processus de la fenêtre, sous Kestrel.
- **Verrou NuGet** : la publication ordinaire ne touche pas aux fichiers de verrouillage, même en mode verrouillé. Une publication éclatée (`-p:PublishSingleFile=false`), qui sert à lister les fichiers publiés, réécrit celui de la fenêtre et en retire `Microsoft.NET.ILLink.Tasks`. Sans fichier unique, le SDK n'active plus l'analyseur du fichier unique, seul usage de ce paquet.
- **Reproductibilité** : deux publications du même commit, depuis deux dossiers, donnent le même SHA-256, avec ou sans compression. L'adresse du dépôt d'origine y entre (SourceLink l'écrit dans les symboles), et le commit aussi (version informative).

## Décision

### Un exe compressé, qui n'embarque que ce qui sert

- La publication compresse le paquet (`EnableCompressionInSingleFile`) : l'exe se télécharge 2,3 fois plus vite. La compression est déterministe, comme le reste.
- Le paquet n'embarque ni fichier de symboles, ni documentation XML, ni l'assembly WPF de WebView2, ni l'hôte et les fichiers de lancement de `Sollicito.Api`, ni `aspnetcorev2_inprocess.dll`. Les fichiers natifs extraits au lancement se réduisent à `e_sqlite3.dll` et aux deux copies de `WebView2Loader.dll` (voir « À vérifier »).

### Construit à chaque pull request comme à la publication

Le workflow réutilisable `.github/workflows/exe.yml` construit l'exe. La CI l'appelle à chaque pull request et à chaque fusion sur `main`, et « Verdict » l'attend. La publication reprendra le même workflow. Son job n'a que la lecture du dépôt, aucun secret et aucun cache : rien d'un autre run n'entre dans l'exe. Il enchaîne :

1. la construction de l'interface, depuis son fichier de verrouillage ;
2. la publication en Release, en mode CI : restauration verrouillée et `ContinuousIntegrationBuild` ;
3. un contrôle que la publication n'a réécrit aucun fichier suivi, verrous compris ;
4. l'empreinte SHA-256 de l'exe ;
5. `.github/scripts/verifier-exe.ps1`, qui lit le paquet décompressé : chercher les chemins dans les octets de l'exe ne prouverait rien, puisque ses assemblies y sont compressées. Il vérifie qu'aucun fichier superflu n'y entre, et qu'aucun chemin de la machine de construction n'y reste (dossier du dépôt, profil Windows, dossier temporaire, nom du compte, en UTF-8 comme en UTF-16). Il vérifie aussi que les chemins de symboles commencent par `/_/`. Un exe construit hors du mode CI le fait échouer ;
6. `.github/scripts/lancer-exe.ps1`, un lancement de fumée : l'exe, seul dans un dossier vide, crée sa base et le profil de son navigateur intégré, lance ce navigateur et affiche sa fenêtre, sans aucune boîte de dialogue, et vit encore au bout de 15 s ; puis l'arbre de ses processus est arrêté. Une boîte de dialogue serait une erreur ou le choix d'un dossier, qui ne s'ouvre qu'à la demande de l'utilisateur (#26) : le lancement échoue alors, au lieu de réussir pour une mauvaise raison. Le script refuse de tourner si le dossier de données de Sollicito existe déjà, pour ne jamais toucher aux données d'un poste ;
7. l'exe et son empreinte (`Sollicito.exe.sha256`, au format de `sha256sum`), gardés 7 jours en artefact du run, sous le nom `exe-non-publie-<commit>`.

Une seconde construction du même commit, dans un autre dossier, doit donner la même empreinte. Sinon, « Verdict » échoue.

### Reconstruire l'exe

Une reconstruction donne le même exe aux conditions suivantes :

- le même commit, et une copie du dépôt dont l'adresse d'origine est celle de GitHub ;
- `CI=true` ;
- le SDK fixé par `global.json`, Node.js et pnpm fixés par `interface/package.json` ;
- la commande du workflow : `dotnet publish src/Sollicito.Fenetre/Sollicito.Fenetre.csproj -c Release`, après `pnpm install --frozen-lockfile` et `pnpm run build` dans `interface/`.

## Alternatives écartées

- **Un exe sans compression** : 154,0 Mo à télécharger, pour un démarrage que le prototype de #9 mesurait aussi en moins d'une seconde avec la compression.
- **Construire l'exe à la seule publication** : la construction et ses contrôles ne tourneraient pour la première fois qu'à la première version.
- **Chercher les chemins dans l'exe tel quel** : une fois le paquet compressé, les chemins restés dans une assembly n'y sont plus lisibles.
- **Retirer l'une des deux copies de `WebView2Loader.dll`** : savoir laquelle le navigateur intégré charge demande un essai (voir « À vérifier »).
- **Mettre en cache les paquets NuGet et pnpm** : un cache se partage entre les runs, et ce qu'un run y laisse pourrait entrer dans l'exe d'un autre.

## Conséquences

- **Positives** :
  - un exe 2,3 fois plus léger ;
  - chaque pull request prouve que l'exe se construit, ne garde aucun chemin de la machine de construction, se lance et se reconstruit à l'identique ;
  - un exe d'essai à télécharger pour les contrôles à la main.
- **Négatives** :
  - trois jobs de plus par pull request, dont deux constructions sous Windows ;
  - l'artefact est un exe d'essai, ni publié ni attesté, que tout compte GitHub connecté peut télécharger pendant 7 jours ;
  - le lancement de fumée ne prouve pas que la page s'affiche : la version publiée n'ouvre pas le protocole CDP ;
  - au lancement, l'exe décompresse ses assemblies en mémoire.

## À vérifier

- **`WebView2Loader.dll`** : le paquet en garde deux copies, à la racine et sous `runtimes/win-x64/native/`. Laquelle se charge ? L'autre pourrait sortir du paquet.
- **Reconstruction par un tiers** : la reproductibilité n'est prouvée que sur les runners de la CI, d'une même image. Sur une autre machine, les fins de ligne de la copie du dépôt (il n'a pas de `.gitattributes`) et son adresse d'origine peuvent changer l'empreinte.

## Sources

Consultées le 2026-11-21 :

- Microsoft Learn, déploiement en fichier unique (compression, extraction des bibliothèques natives) : <https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview>
- SDK .NET 10.0.404, `Microsoft.NET.Sdk.Analyzers.targets` (analyseur du fichier unique) et `Microsoft.NET.Publish.targets` (paquet `Microsoft.NET.ILLink.Tasks`, compression)
- Mesures de #54 : [#89](https://github.com/moonn1000/sollicito/pull/89)
