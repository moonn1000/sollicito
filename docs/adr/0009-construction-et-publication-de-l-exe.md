# ADR-0009 : construction et publication de l'exe

- **Statut** : acceptée ; les textes de release-please et les noms des fichiers joints attendent la validation du mainteneur, au palier 5
- **Date** : 2026-11-21
- **Précise** : [ADR-0004](0004-v1-resserree-paliers.md), « À vérifier » (compression de l'exe, tranchée ici) et « Ce qui change dans l'ADR-0001 », D13 (l'exe construit par l'intégration continue, numéroté par release-please, avec son empreinte et son attestation)

## Contexte

L'ADR-0004 publie la V1 en exe autonome d'un seul fichier, construit par l'intégration continue avec son empreinte SHA-256 et son attestation de provenance, et numéroté par release-please. Il laissait ouverte la compression de l'exe : 62,2 Mo au lieu de 140,6 Mo au prototype de #9, avec une page prête en moins d'une seconde dans les deux cas. La version 1.0.0 sort au palier 5, sur GO du mainteneur.

Faits relevés le 2026-11-21 sur l'exe de #54 (SDK 10.0.404 ; un mégaoctet vaut ici 10⁶ octets) :

- **Taille** : 154,0 Mo sans compression, 67,3 Mo avec.
- **Chemins de symboles** : hors de l'intégration continue, les DLL des projets que la fenêtre référence gardent le chemin de leur fichier de symboles, donc le dossier de compilation et le nom du compte Windows. `ContinuousIntegrationBuild`, posé par `Directory.Build.props` quand `CI=true`, les réécrit en `/_/…`.
- **Fichiers superflus** : le paquet de l'exe embarquait l'hôte natif de l'API (`Sollicito.Api.exe`, 162 Ko) et le module d'hébergement sous IIS d'ASP.NET Core (`aspnetcorev2_inprocess.dll`, 392 Ko). Ce sont deux fichiers natifs, que l'exe extrait sous `%TEMP%\.net` au premier lancement, et dont il ne se sert pas : l'API tourne dans le processus de la fenêtre, sous Kestrel.
- **Verrou NuGet** : la publication ordinaire ne touche pas aux fichiers de verrouillage, même en mode verrouillé. Une publication éclatée (`-p:PublishSingleFile=false`), qui sert à lister les fichiers publiés, réécrit celui de la fenêtre et en retire `Microsoft.NET.ILLink.Tasks`. Sans fichier unique, le SDK n'active plus l'analyseur du fichier unique, seul usage de ce paquet.
- **Reproductibilité** : deux publications du même commit, depuis deux dossiers, donnent le même SHA-256, avec ou sans compression, sur le poste comme sur deux runners de la CI avec la vraie interface. L'adresse du dépôt d'origine y entre (SourceLink l'écrit dans les symboles), et le commit aussi (version informative).
- **release-please** : une pull request ouverte par le jeton du workflow ne lance sa CI qu'une fois ses runs approuvés par une personne qui a l'écriture, ou fermée puis rouverte. Un run lancé par `workflow_dispatch` sur sa branche ne compte pas pour un contrôle obligatoire. Le jeton du workflow ne peut pas créer une version dont le commit diffère de la branche par défaut sous `.github/workflows/`. Ses textes par défaut sont anglais : titre, en-tête, pied et labels de la pull request de version.

## Décision

### Un exe compressé, qui n'embarque que ce qui sert

- La publication compresse le paquet (`EnableCompressionInSingleFile`) : l'exe se télécharge 2,3 fois plus vite. La compression est déterministe, comme le reste.
- Le paquet n'embarque ni fichier de symboles, ni documentation XML, ni l'assembly WPF de WebView2, ni l'hôte et les fichiers de lancement de `Sollicito.Api`, ni `aspnetcorev2_inprocess.dll`. Les fichiers natifs extraits au lancement se réduisent à `e_sqlite3.dll` et aux deux copies de `WebView2Loader.dll` (voir « À vérifier »).

### Construit à chaque pull request comme à la publication

Le workflow réutilisable `.github/workflows/exe.yml` construit l'exe. La CI l'appelle à chaque pull request et à chaque fusion sur `main`, et « Verdict » l'attend ; la publication l'appelle sur l'étiquette d'une version. Son job n'a que la lecture du dépôt, aucun secret et aucun cache : rien d'un autre run n'entre dans l'exe. Il enchaîne :

1. la construction de l'interface, depuis son fichier de verrouillage ;
2. la publication en Release, en mode CI : restauration verrouillée et `ContinuousIntegrationBuild` ;
3. un contrôle que la publication n'a réécrit aucun fichier suivi, verrous compris ;
4. l'empreinte SHA-256 de l'exe ;
5. `.github/scripts/verifier-exe.ps1`, qui lit le paquet décompressé : chercher les chemins dans les octets de l'exe ne prouverait rien, puisque ses assemblies y sont compressées. Il vérifie qu'aucun fichier superflu n'y entre, et qu'aucun chemin de la machine de construction n'y reste (dossier du dépôt, profil Windows, dossier temporaire, nom du compte, en UTF-8 comme en UTF-16). Il vérifie aussi que les chemins de symboles commencent par `/_/`. Un exe construit hors du mode CI le fait échouer ;
6. `.github/scripts/lancer-exe.ps1`, un lancement de fumée : l'exe, seul dans un dossier vide, crée sa base et le profil de son navigateur intégré, lance ce navigateur et affiche sa fenêtre, sans aucune boîte de dialogue, et vit encore au bout de 15 s ; puis l'arbre de ses processus est arrêté. Une boîte de dialogue serait une erreur ou le choix d'un dossier, qui ne s'ouvre qu'à la demande de l'utilisateur (#26) : le lancement échoue alors, au lieu de réussir pour une mauvaise raison. Le script refuse de tourner si le dossier de données de Sollicito existe déjà, pour ne jamais toucher aux données d'un poste ;
7. la préparation, en essai, des fichiers d'une version 0.0.0, par le script de la publication (`preparer-les-fichiers.sh`) ;
8. un artefact du run, 7 jours : l'exe et son empreinte (`Sollicito.exe.sha256`, au format de `sha256sum`). Il porte le nom `exe-non-publie-<commit>` sur un push vers `main`, et `exe-candidat-vX.Y.Z` dans la publication. Une pull request n'en produit aucun : venue d'un fork, elle ferait héberger sous le nom du projet un exe construit avec son propre code.

Une seconde construction du même commit, dans un autre dossier, doit donner la même empreinte. Sinon, « Verdict » échoue.

### Numérotée par release-please

- **Configuration** : mode manifeste (`release-please-config.json`, `.release-please-manifest.json`), type `simple`, première version 1.0.0 (`initial-version`), étiquettes `vX.Y.Z`. release-please-action v5.0.0 embarque release-please 17.6.0.
- **Version de l'exe** : elle vit dans `Directory.Build.props`, sur une ligne annotée `x-release-please-version`. Le fichier est cité dans `extra-files`, et la pull request de version réécrit cette ligne : un exe reconstruit depuis l'étiquette porte la même version. Elle vaut 0.0.0 tant qu'aucune version n'est sortie.
- **Journal** : `CHANGELOG.md` naît dans la pull request de version. Il ne reprend que les commits `feat`, `fix`, `perf` et `revert`.
- **Brouillon** : release-please crée chaque version en brouillon (`draft`), et son étiquette aussitôt (`force-tag-creation`). Sans cette seconde option, GitHub ne crée l'étiquette d'un brouillon qu'à sa publication, et release-please ne retrouverait plus la version précédente.

### Les textes de release-please, à valider au palier 5

Ces textes deviennent publics au premier lancement du workflow de version. Le mainteneur les valide mot pour mot au palier 5, avant ce lancement.

| Réglage | Texte proposé |
| --- | --- |
| `pull-request-title-pattern` | `chore${scope}: version${component} ${version}`, soit « chore(main): version 1.0.0 » |
| `pull-request-header` | « Version préparée par release-please. La fusionner crée l'étiquette et la version en brouillon, puis lance la construction de l'exe ; le mainteneur publie ensuite le brouillon. » |
| `pull-request-footer` | « Pull request ouverte par release-please, à partir des commits fusionnés sur main. » |
| `label` | « version : en attente » |
| `release-label` | « version : étiquetée » |
| `changelog-sections` | « Nouveautés » (`feat`), « Corrections » (`fix`), « Performances » (`perf`), « Retours en arrière » (`revert`) |
| Fichiers joints | `Sollicito-X.Y.Z-win-x64.exe`, `Sollicito-X.Y.Z-win-x64.exe.sha256`, `Sollicito-X.Y.Z-win-x64.exe.sigstore.json` |

Deux textes de release-please ne se règlent pas et restent en anglais : le commentaire « Created releases: », qu'il laisse sur la pull request fusionnée, et le titre « BREAKING CHANGES », pour un commit marqué d'un `!` (aucun à ce jour).

### Le workflow de version ne se lance qu'à la main

`version.yml` n'a qu'un déclencheur, `workflow_dispatch`, et ne tourne que depuis `main`. Il ne connaît qu'un groupe de concurrence, sans annulation : deux runs à la fois créeraient deux fois la même version. Chaque run a un effet public. Personne ne le lance avant le GO du mainteneur, au palier 5. La sortie d'une version se déroule ainsi :

1. Le mainteneur lance le workflow de version : release-please ouvre la pull request de version.
2. Le mainteneur approuve ses runs (« Approve workflows to run »), ou la ferme puis la rouvre : « Verdict » s'y affiche alors.
3. Le mainteneur la fusionne, puis relance le workflow de version. release-please crée l'étiquette `vX.Y.Z` et la version en brouillon. Un job à `actions: write` seul lance ensuite `publication.yml` sur `refs/tags/vX.Y.Z` : une étiquette créée par le jeton du workflow ne déclenche aucun workflow.
4. La publication joint au brouillon l'exe, son empreinte et le paquet de son attestation.
5. Le mainteneur essaie l'exe, relit les notes de version, puis publie le brouillon.

**La fusion de la pull request de version et le run qui la suit forment l'acte public.** Sont alors publics : l'étiquette, avec les archives des sources que GitHub en tire ; l'attestation, pour toujours dans le journal de transparence public de Sigstore ; l'exe candidat, en artefact du run de publication. La publication du brouillon n'en est que le second geste. Rien ne se fusionne pendant la sortie d'une version : sinon, le jeton du workflow pourrait ne plus avoir le droit de créer la version.

### La publication, sur l'étiquette

`publication.yml` tourne avec la copie des workflows que porte l'étiquette. Un rattrapage (`gh workflow run publication.yml --ref refs/tags/vX.Y.Z`) ne répare donc qu'une panne passagère : **un défaut des workflows se corrige par une nouvelle version, jamais en déplaçant l'étiquette.** Ses jobs :

- **Garde** (`contents: read`) : `github.ref` entier, passé par `env:`, doit valoir `refs/tags/vX.Y.Z` (motif `^refs/tags/v[0-9]+\.[0-9]+\.[0-9]+$`), et son commit doit être sur `main` (`garder-la-publication.sh`). Aucun `run:` ne reçoit `github.ref_name`, une entrée ou une sortie de release-please, que par `env:`.
- **Exe** : le workflow réutilisable, artefact `exe-candidat-vX.Y.Z`.
- **Attester** (`id-token: write` et `attestations: write`, rien d'autre) : il télécharge l'exe candidat, en vérifie l'empreinte, le nomme `Sollicito-X.Y.Z-win-x64.exe` et l'atteste par `actions/attest` (provenance SLSA). Il ne récupère pas le dépôt. Il est écrit dans `publication.yml` même : `--signer-workflow` vérifie le workflow où se définit le job qui atteste.
- **Joindre** (`contents: write`) : il vérifie que le brouillon de l'étiquette existe, puis l'empreinte et l'attestation, hors ligne. Ensuite seulement, il joint les trois fichiers et compare le condensé que GitHub affiche à l'empreinte (`joindre-au-brouillon.sh`).

### Vérifier l'exe

- **Empreinte** : `Get-FileHash .\Sollicito-X.Y.Z-win-x64.exe -Algorithm SHA256`, à comparer au fichier `.sha256` ou au condensé que la page de la version affiche pour l'exe.
- **Attestation, en ligne** : `gh attestation verify Sollicito-X.Y.Z-win-x64.exe --repo moonn1000/sollicito --signer-workflow moonn1000/sollicito/.github/workflows/publication.yml --source-ref refs/tags/vX.Y.Z`.
- **Attestation, hors ligne** : d'abord, en ligne, `gh attestation trusted-root > trusted_root.jsonl`. Ensuite, la même commande, avec `--bundle Sollicito-X.Y.Z-win-x64.exe.sigstore.json --custom-trusted-root trusted_root.jsonl`.

Le guide « Démarrer » (#60) l'expliquera aux utilisateurs.

### Une répétition sans trace

La CI de chaque pull request passe par la publication en mode d'essai, sans `id-token` ni `contents: write` :

- `tester-la-garde.sh` éprouve la garde sur 13 refs admises et refusées : branche nommée comme une version, injection, commit absent de `main`…
- `tester-la-version.sh` contrôle la configuration de release-please et le seul déclencheur du workflow de version.
- Le job « Exe » prépare les fichiers d'une version d'essai.

La répétition s'arrête avant l'attestation et l'envoi. Ne tournent pour la première fois qu'au palier 5 : release-please contre le dépôt, le lancement de la publication, l'attestation et l'envoi au brouillon.

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
- **release-please à chaque push sur `main`** : il ouvrirait la pull request de version dès la fusion de #54, avec des textes publics que le mainteneur n'a pas validés.
- **Lancer la CI de la pull request de version par `workflow_dispatch`** : ses contrôles ne comptent pas pour un contrôle obligatoire.
- **Un jeton d'app GitHub ou un jeton personnel** : la CI de la pull request de version tournerait sans geste, mais il faudrait garder un secret à longue durée de vie.
- **Publier la version sans brouillon, ou la laisser publier par la CI** : la page de la version serait publique avant que le mainteneur n'ait essayé l'exe.
- **Attester dans le workflow réutilisable** : `--signer-workflow` n'y reconnaîtrait plus `publication.yml`, et la CI devrait recevoir `id-token`.
- **Répéter avec une version de test** : son étiquette et son attestation resteraient publiques.

## Conséquences

- **Positives** :
  - un exe 2,3 fois plus léger ;
  - chaque pull request prouve que l'exe se construit, ne garde aucun chemin de la machine de construction, se lance et se reconstruit à l'identique ;
  - un exe d'essai à télécharger, sur `main`, pour les contrôles à la main ;
  - une version dont l'origine se vérifie, en ligne comme hors ligne, sans secret dans le dépôt.
- **Négatives** :
  - quatre jobs de plus par pull request, dont deux constructions sous Windows ;
  - l'artefact de `main` est un exe d'essai, ni publié ni attesté, que tout compte GitHub connecté peut télécharger pendant 7 jours ;
  - le lancement de fumée ne prouve pas que la page s'affiche : la version publiée n'ouvre pas le protocole CDP ;
  - au lancement, l'exe décompresse ses assemblies en mémoire ;
  - chaque version demande au mainteneur cinq gestes : deux lancements du workflow de version, l'approbation des runs de la pull request, sa fusion, la publication du brouillon ;
  - le chemin de publication ne tourne en entier qu'à la première version ;
  - `gh`, qui lance la publication, vérifie l'attestation et joint les fichiers, vient de l'image du runner, sans version fixée.

## À vérifier

- **`WebView2Loader.dll`** : le paquet en garde deux copies, à la racine et sous `runtimes/win-x64/native/`. Laquelle se charge ? L'autre pourrait sortir du paquet.
- **Reconstruction par un tiers** : la reproductibilité n'est prouvée que sur ce poste et sur les runners de la CI. Sur une autre machine, les fins de ligne des sources et l'adresse d'origine de la copie du dépôt peuvent changer l'empreinte : le `.gitattributes` ne fixe que celles des scripts bash.
- **Ruleset des étiquettes** : rien n'empêche aujourd'hui une personne qui a l'écriture de poser une étiquette `v*` sur un commit non fusionné (voir le modèle de menaces). Un ruleset « Restrict updates », « Restrict deletions » et « Block force pushes » sur `v*` est un réglage du mainteneur, à proposer au palier 5. « Restrict creations » pourrait bloquer l'étiquette de release-please : à essayer.
- **release-please 17.6.0**, embarqué par l'action v5.0.0 : des correctifs de ses dépendances sont parus depuis (17.7.0, 17.10.2). À revoir au palier 5.
- **Versions immuables** : compatibles avec le brouillon, puisque les fichiers se joignent avant la publication. C'est un réglage du mainteneur.

## Sources

Consultées le 2026-11-21 :

- Microsoft Learn, déploiement en fichier unique (compression, extraction des bibliothèques natives) : <https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview>
- SDK .NET 10.0.404, `Microsoft.NET.Sdk.Analyzers.targets` (analyseur du fichier unique) et `Microsoft.NET.Publish.targets` (paquet `Microsoft.NET.ILLink.Tasks`, compression)
- GitHub Docs, contrôles obligatoires et `workflow_dispatch` : <https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks>
- GitHub Changelog, runs des pull requests ouvertes par un robot, à approuver (11/06/2026) : <https://github.blog/changelog/2026-06-11-bot-created-pull-requests-can-run-workflows-if-approved/>
- GitHub Docs, API des versions (brouillons, fichiers de `.github/workflows/`) : <https://docs.github.com/en/rest/releases/releases>
- GitHub Docs, vérifier une attestation hors ligne : <https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/verify-attestations-offline>
- release-please 17.6.0 : `docs/manifest-releaser.md` et `docs/customizing.md`, et son code, `src/strategies/base.ts`, `src/strategies/simple.ts`, `src/updaters/generic.ts`
- `actions/attest` v4.2.2 : README et `src/attest.ts`
- Mesures de #54 : [#89](https://github.com/moonn1000/sollicito/pull/89)
