# ADR-0020 : l'export de toutes les données : le fichier choisi dans le dialogue de l'hôte, un seul instantané de la base, rien de partiel, un CSV neutralisé

- **Statut** : acceptée ; critères et décisions acceptés par le pilote le 2026-11-27 ; trois écarts de mise en œuvre acceptés par le pilote le 2026-11-27 (le silence réservé à une demande d'export du même format, les étapes d'un échec, la lecture en SQL sur une connexion en lecture seule) ; les deux textes de l'écran sur le contenu de l'export et son chiffrement validés mot pour mot par le mainteneur le 2026-11-27 ; deux points précisés par #118 le 2026-11-28 : pendant un export, un choix de dossier reçoit « occupé » au lieu de n'ouvrir rien (« Un seul dialogue de l'hôte à la fois », texte de l'écran validé mot pour mot par le mainteneur), et le oui à remplacer un fichier vaut pour le fichier vu au retour du dialogue, sa taille et ses dates comparées juste avant le renommage (« La destination »)
- **Date** : 2026-11-27
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D10 (l'export complet en JSON et en CSV, sans les clés d'IA ; la neutralisation du CSV, étendue aux onze débuts d'OWASP) ; [ADR-0003](0003-hote-interface-webview2.md), « Durcissement de la fenêtre » (une sixième demande de la page à l'hôte, l'export, dont seul le dialogue « Enregistrer sous » de l'hôte donne le fichier) ; [ADR-0012](0012-restauration-d-une-sauvegarde.md) (un seul dialogue de l'hôte à la fois : l'export rejoint la règle) ; [ADR-0015](0015-import-csv-des-offres.md), « Le contenu non fiable, et l'export à venir » (la neutralisation faite, dans toutes les cellules) ; [ADR-0016](0016-fournisseur-d-ia-et-cle-chiffree.md), « Hors de la base, des copies et du dossier des copies » (l'export ne lit que les six tables de la base : la clé n'y entre jamais)

## Contexte

La story #49 fait exporter toutes les données de l'utilisateur, en JSON et en CSV : c'est la portabilité de la D10 de l'ADR-0001, et le dernier geste sur les données de la V1 (ADR-0004, palier 5). Plusieurs décisions la cadrent déjà :

- **D10 de l'ADR-0001** : un export complet en JSON ou en CSV ; les clés d'IA rangées hors de la base, que ni les sauvegardes ni les exports n'emportent ; un export CSV qui neutralise les cellules commençant par `=`, `+`, `-` ou `@`.
- **ADR-0004** : le rechargement d'un export attend la V2 (D10), et l'export CSV est la première fonction que la règle de coupe ferait passer en V2.
- **ADR-0008 et ADR-0012** : aucune route ne reçoit un chemin ; seul un dialogue de l'hôte en donne un, et la page ne demande à l'hôte que par un message strict.
- **ADR-0015** : la syntaxe CSV de l'import (UTF-8, `;`, guillemets de la RFC 4180), qu'Excel ouvre sans assistant.

Six faits, relevés le 2026-11-27, pèsent sur la façon de faire :

- **Ce que la base garde** : six tables pour l'utilisateur (`Entreprises`, `Contacts`, `Offres`, `Candidatures`, `Relances`, et `EvenementsDeCandidature`, une table pour trois sortes), et trois tables techniques (`__EFMigrationsHistory`, `__EFMigrationsLock`, `sqlite_sequence`). Hors de la base, le dossier de données porte `reglages.json`, `sauvegardes.json`, `cle-ia.json`, `Trousseau`, `WebView2`, `sollicito.verrou` et `Sauvegardes`. Le jeton ne vit qu'en mémoire, et les journaux ne vont qu'à la console.
- **Le texte des instants dans SQLite** : `2026-11-02 08:00:00`, en UTC, sans `T` ni `Z`, avec une fraction de seconde quand l'horloge en donne une. Les dates : `2026-11-09`.
- **La page ne peut rien télécharger** : l'hôte annule les téléchargements, et aucune route ne reçoit un chemin.
- **La base est en mode WAL** (`PreparationDeLaBase`, `CopieDeLaBase`, `MiseAJourDeLaBase`, `RestaurationDeLaBase`) : une transaction de lecture y voit un instantané, sans retenir les écritures.
- **OWASP**, page « CSV Injection »[^owasp] : les débuts à neutraliser sont `=`, `+`, `-`, `@`, la tabulation, le retour chariot, le saut de ligne et les formes pleine chasse `＝`, `＋`, `－`, `＠`. Parmi ses méthodes, « Prepend each cell field with a single quote ». Elle avertit que ces méthodes « are not reliable in Microsoft Excel after saving and re-opening the CSV file ». Elle ne dit rien de ce qu'un tableur affiche de l'apostrophe.
- **Excel** : 32 767 caractères au plus dans une cellule[^excel]. Le texte d'une offre va jusqu'à 50 000.

## Décision

### Qui écrit, et par où passe le chemin

- **Le message de la page** : `{"type":"exporterLesDonnees","format":"json"}`, ou `"csv"`, sous le contrôle de la source du canal du jeton. Deux champs exactement, et un format de la liste fermée. Un champ de plus, répété compris, un format inconnu, un message mal formé ou d'un autre type n'ouvre rien. Jamais un chemin ni un nom de fichier.
- **Le dialogue de l'hôte** : la fenêtre ouvre le dialogue « Enregistrer sous » de Windows après le gestionnaire du message, sous le titre « Exporter vos données ». Il propose `sollicito-export-AAAA-MM-JJ.json`, ou `.zip`, au jour local, et un seul filtre : « Export JSON (*.json) », ou « Archive ZIP de fichiers CSV (*.zip) ».
- **L'API écrit** : la fenêtre passe le fichier choisi et le oui à le remplacer, en mémoire et hors du fil de la fenêtre, au service `ExportDesDonnees` de l'API, résolu comme les brouillons des relances. C'est l'API qui lit la base et écrit le fichier. Aucune route n'est ajoutée, et OpenAPI ne change pas.
- **La réponse** : `{"type":"export","format":"json","issue":"enCours"}` une fois le fichier choisi, puis l'issue : `exporte`, `annule`, `occupe`, `existe`, `extension`, `dossierDeDonnees`, `accesRefuse`, `placeManquante`, `dossierIntrouvable` ou `impossible`. Jamais un chemin ni un nom.

### Ce que l'export contient

- **Toutes les lignes des six tables, avec toutes leurs colonnes**, lues telles qu'en base, colonne par colonne, sans passer par le domaine : les identifiants, les liens, la sorte d'un événement, et les champs que Sollicito calcule (la clé d'une entreprise, le poste normalisé et l'empreinte d'une offre). L'empreinte d'une offre importée puis corrigée ne se recalcule pas : elle s'exporte telle quelle.
- **Jamais** : la clé d'IA, le trousseau, le jeton, les journaux, les copies de la base, `reglages.json`, `sauvegardes.json`, le profil WebView2, ni les tables techniques. Les réglages restent hors de l'export de la V1, comme ils restent hors de la base (ADR-0008) : ils portent un chemin propre au poste, le dossier des sauvegardes.
- **Un test de garde** compare les colonnes que l'export lit au schéma que les migrations construisent : une table ou une colonne ajoutée, et que l'export n'écrirait pas, le fait échouer.

### Le JSON, qui fait foi

- **Un seul fichier**, UTF-8 sans BOM, indenté. Son nom et sa version d'abord (`"format": "sollicito-export"`, `"version": 1`, `"exporteLe"`), puis un tableau par entité, dans l'ordre des liens : entreprises, contacts, offres, candidatures, relances, événements. Chacun est trié par identifiant croissant, identifiants gardés.
- **Les noms du domaine**, en camelCase comme l'API : les liens en `…Id`, les énumérations en toutes lettres (`Envoyee`, `AFaire`, `TexteColle`…). Un événement porte `id`, `candidatureId`, `sorte` et `survenuLe`, puis les champs de sa sorte : `ancien`, `nouveau` et `corrigeId` ; `type`, `note` et `corrigeId` ; ou `relanceId`. Les deux colonnes de correction de la base, `CorrigeId` et `EtapeCorrigeeId`, deviennent ainsi un seul champ, que la sorte distingue. Une sorte que Sollicito n'écrit pas fait échouer l'export.
- **Les valeurs** : les instants en ISO 8601, en UTC, suivis de `Z`, avec leur fraction de seconde quand elle n'est pas nulle ; les dates en `AAAA-MM-JJ` ; une valeur absente en `null`. Aucune neutralisation : chaque valeur telle qu'en base. L'encodeur relâché de .NET écrit les lettres accentuées et les signes tels quels : le fichier ne s'insère jamais dans une page.
- **La version** vaut 1, et toute modification du format fait passer à la suivante. Le format se décrit dans [`docs/export.md`](../export.md), champ par champ, avec la colonne de la base d'où chacun vient ; l'écran en reprend l'essentiel, puisque l'exe ne mène pas au dépôt.

### Le CSV, pour un tableur

- **Une archive ZIP**, donc un seul fichier : `export.csv`, qui porte `format` (`sollicito-export-csv`), `version` (1) et `exporteLe`, puis `entreprises.csv`, `contacts.csv`, `offres.csv`, `candidatures.csv`, `relances.csv` et `evenements.csv`. Un seul renommage tient ainsi « rien de partiel » et « pas de remplacement sans le oui », comme pour le JSON. L'archive s'écrit au fil de la lecture, une entrée à la fois.
- **La syntaxe de l'import** (ADR-0015) : UTF-8 avec BOM ; le séparateur `;` ; les guillemets de la RFC 4180 autour d'une cellule qui contient `;`, `"`, CR ou LF, avec `""` pour un guillemet ; CRLF entre les lignes, et les sauts de ligne d'une cellule gardés tels quels ; les noms des champs du JSON en en-tête ; une cellule vide pour une valeur absente. L'import de #40 en lit la syntaxe, mais pas les colonnes.
- **La neutralisation** : une cellule dont le premier caractère est l'un des onze débuts d'OWASP reçoit une apostrophe en tête ; la règle des guillemets s'applique ensuite (l'exemple d'OWASP `=1+2";=1+2` devient `"'=1+2"";=1+2"`). Elle vise toutes les cellules, pas seulement celles d'une offre importée ; les identifiants et les dates ne commencent jamais ainsi. La valeur entière reste lisible, derrière l'apostrophe.
- **Il peut tomber seul** : son écrivain, sa valeur `csv` du message, son bouton et ses tests vivent dans un commit à part (règle de coupe de l'ADR-0004). Il a été livré.

### La destination

- **Le oui à remplacer un fichier vient du dialogue de Windows**, qui le demande lui-même (`OverwritePrompt`) ; un test vérifie que le dialogue le demande. Le choix porte ce oui : vrai si le fichier existait au retour du dialogue. Le renommage ne remplace un fichier qu'avec lui ; un fichier apparu entre-temps n'est pas remplacé (issue `existe`).
- **Refusé avant toute écriture**, dans cet ordre : un nom qui ne finit pas par l'extension du format, `.json` ou `.zip` (issue `extension`) ; un chemin relatif (issue `impossible`) ; un fichier dans le dossier de données de Sollicito, sous-dossiers compris, après normalisation (issue `dossierDeDonnees`). Sinon, un oui à l'écrasement remplacerait la base, les réglages ou la clé. La règle compare les chemins complets, sans tenir compte de la casse, et un chemin de périphérique à lettre de lecteur ou UNC (`\\?\C:\…`, `\\.\C:\…`, `\\?\UNC\…`) sans son préfixe ; elle ne suit pas les jonctions, puisque le chemin vient de l'utilisateur, dans le dialogue.
- **Le dossier des sauvegardes** : celui par défaut est refusé, puisqu'il est sous le dossier de données ; un dossier choisi ailleurs, sur un NAS ou dans OneDrive, ne l'est pas. Les copies y sont toutes en `.db` : le contrôle de l'extension les protège.

### L'écriture : rien de partiel

- **Un fichier provisoire dans le dossier choisi**, donc sur le même volume, sous un nom reconnaissable : `sollicito-export-<huit caractères hexadécimaux>.partiel`. Il s'ouvre en création seule et sans partage, reçoit l'export, se pousse sur le disque, puis prend le nom choisi.
- **Un échec, une annulation ou la fermeture de la fenêtre** effacent le provisoire : rien ne reste sous le nom choisi, et un fichier qu'on allait remplacer reste intact.
- **Après un arrêt brutal**, le provisoire reste dans le dossier choisi, que Sollicito ne garde nulle part : c'est une limite, écrite dans `docs/export.md` et le modèle de menaces.
- **Les échecs** se reconnaissent à leur type et à leur code d'erreur de Windows : un accès refusé, au dossier ou au fichier à remplacer ; la place qui manque (disque plein, quota) ; un dossier introuvable (support débranché, partage perdu) ; un fichier apparu au renommage. Toute autre erreur, de la base comme du disque, vaut « impossible ». L'écran dit chacune par un texte de Sollicito, jamais par le message d'une exception.

### Un état cohérent, et la porte de la base

- **Une seule transaction de lecture différée**, sur une connexion en lecture seule (`BeginTransaction(deferred: true)`[^differee]) : en mode WAL, toutes les lectures de l'export voient le même instantané, pris à la première, même si l'utilisateur écrit en même temps, et l'export ne prend aucun verrou d'écriture.
- **Du SQL, table par table**, plutôt que les entités d'Entity Framework Core : l'export lit chaque colonne telle qu'en base, y compris celles que le domaine ne montre pas, et convertit lui-même les instants de leur texte SQLite en ISO 8601.
- **La porte de la base** : l'export y entre avant d'ouvrir le provisoire. Pendant une restauration, il est refusé (« occupé ») et n'écrit rien.

### Un seul dialogue de l'hôte à la fois

- Une demande d'export pendant un choix de dossier ou une restauration reçoit « occupé ».
- Une demande du même format pendant un export ne reçoit rien, pour que la page qui attend le sien en reçoive la vraie issue ; une demande de l'autre format reçoit « occupé ».
- Pendant un export, une restauration reçoit « occupée », et un choix de dossier n'ouvre rien.

### La fermeture de la fenêtre

Fermer la fenêtre interrompt l'export, puis attend qu'il efface son provisoire, cinq secondes au plus. Au-delà, la fenêtre se ferme quand même, et le journal dit que le provisoire a pu rester.

### Une mémoire bornée

La lecture se fait en flux, ligne à ligne, et l'écriture suit la lecture, par un tampon de 64 Kio : ni une table ni le fichier ne sont tenus en mémoire. Un test le prouve : un lecteur factice et un flux qui note ses écritures montrent que la première écriture tombe avant la dernière lecture, et qu'aucune ne dépasse le tampon plus une ligne.

### Les journaux

Les événements 130 à 136 disent un export fait (le format, le nombre de lignes par entité, la durée, un fichier remplacé ou non), annulé dans le dialogue, refusé (son motif), impossible (son étape : l'ouverture du provisoire, l'écriture, qui lit la base au fil de l'eau, ou le renommage ; et le type de l'erreur, qui départage une erreur de la base d'une erreur du disque), interrompu par la fermeture, un provisoire qui ne s'efface pas, et une fenêtre fermée avant sa fin. Jamais un chemin, un nom de fichier ou de dossier, une donnée, ni le message d'une exception.

### L'écran

La section « Exporter vos données » paraît dans les réglages, après « Sauvegardes ». Deux de ses textes promettent ce que contient le fichier et ce qu'il devient : le mainteneur les a validés mot pour mot le 2026-11-27.

> L'export rassemble toutes les données de votre base : entreprises, contacts, offres, candidatures, leur historique et leurs relances. Il ne contient ni votre clé d'IA ni vos réglages.

> L'export n'est pas chiffré : toute personne qui accède au fichier peut le lire. Enregistré dans un dossier synchronisé, comme OneDrive, il part là où ce dossier l'envoie.

Suivent ce que donne chaque forme, les boutons « Exporter en JSON… » et « Exporter en CSV… », puis, pendant l'export, l'attente du dialogue, l'export en cours et son issue. Hors de la fenêtre, les boutons n'y sont pas, comme celui du choix d'un dossier.

### La version de test et la base de référence

- **La version de test** remplace le dialogue par un fichier de son dossier de données, `export-a-choisir.txt` : le chemin sur la première ligne, et `remplacer` sur la seconde pour le oui. Sans ce fichier, ou devant un fichier existant sans ce oui, l'export est annulé, comme dans le vrai dialogue. Chaque demande se note dans `exports-demandes.txt`, avec le format et le nom proposé. `verifier-exe.ps1` vérifie que l'exe publié ne porte pas la marque `export-a-choisir.txt`.
- **Une base figée, produite par le code de `main`** (`9ec81fa`), sert de référence : `tests/Sollicito.Api.Tests/Reference/base-de-main-9ec81fa.db`, comme la copie de référence de #28. Elle a été peuplée par ses seules routes, dans une extraction de `main` hors du dépôt, puis copiée par « Sauvegarder maintenant ». Ses données sont fictives, et remplissent chaque entité et chaque champ : chaque sorte d'événement, des corrections, des clôtures, une offre collée et des offres importées, une candidature tirée d'une offre, un destinataire, des accents, des sauts de ligne et des débuts de formule. Le test l'exporte, puis compare les comptes et les champs à des valeurs écrites en dur, et chaque colonne de chaque ligne à une lecture SQL directe de la base.

## Alternatives écartées

- **Une route `GET /api/export`** qui rendrait le fichier à la page : la page ne peut pas l'enregistrer, puisque l'hôte annule les téléchargements, et tout le contenu traverserait sa mémoire.
- **La fenêtre qui écrit elle-même** : la même sécurité, mais l'écriture vivrait dans le projet de la fenêtre, que les tests de l'API n'atteignent pas.
- **Six fichiers CSV dans un dossier choisi** : un arrêt au troisième laisse un export partiel, et le oui à l'écrasement se demande fichier par fichier.
- **Un seul CSV aplati** : il perd les liens, et l'export ne serait plus complet.
- **Des guillemets autour de chaque cellule**, la méthode générique d'OWASP : la même protection contre un séparateur ou un guillemet, pour un fichier moins lisible.
- **Le séparateur `,`** : un Excel réglé pour la Belgique mettrait tout dans une colonne.
- **La tabulation en tête**, la variante d'OWASP pour Excel : elle reste dans la donnée, et la tabulation est elle-même un début dangereux.
- **Doubler aussi une apostrophe de tête**, pour un CSV réversible : une règle de plus, pour une forme qui ne sert pas au rechargement.
- **Les noms des colonnes de la base**, tels quels : le format suivrait chaque migration, et `CorrigeId` et `EtapeCorrigeeId` resteraient deux champs.
- **Sans les champs calculés** : un fichier plus court, mais « tous les champs » ne tiendrait plus, et l'empreinte d'une offre corrigée serait perdue.
- **Une copie `VACUUM INTO`**, puis l'export depuis elle : la même cohérence, pour la place d'une base de plus et un reste à nettoyer après un arrêt.
- **Notre propre question avant de remplacer un fichier**, après celle de Windows : un texte de plus à faire valider, pour la même garantie.
- **Aucune règle pour le dossier de données** : seule la question du dialogue protégerait alors la base, les réglages et la clé.
- **Le délai des relances et le choix du fournisseur dans l'export**, sans le chemin : le rechargement de la V2 doit pouvoir se faire sans toucher aux réglages.
- **Un schéma JSON publié dès la V1** : une dépendance de test de plus pour le vérifier, avant que le rechargement en ait besoin.
- **La base de référence peuplée par les routes, dans le test même** : les routes ne changent pas, mais ce ne serait plus le code de `main`.

## Conséquences

- **Positives** : toutes les données de l'utilisateur, dans un format documenté et versionné, dont le JSON garde chaque valeur telle qu'en base ; jamais la clé, le jeton ni les réglages ; un fichier que seul l'utilisateur choisit, dans le dialogue de Windows ; rien de partiel sous le nom choisi ; un instantané cohérent, qui ne retient pas l'utilisateur ; une mémoire bornée ; un CSV qu'un tableur réglé sur le point-virgule ouvre sans exécuter de formule ; aucune route, aucune migration.
- **Négatives** :
  - l'export n'est pas chiffré : qui accède au fichier le lit, et, rangé dans un dossier synchronisé, il part là où ce dossier l'envoie ;
  - après un arrêt brutal, ou au-delà des cinq secondes de la fermeture, le provisoire, partiel et non chiffré, peut rester dans le dossier choisi, où Sollicito ne le retrouve pas ; son nom le fait reconnaître, et il s'efface à la main ;
  - la règle du dossier de données compare les chemins tels qu'ils s'écrivent, après normalisation : elle reconnaît une forme à lettre de lecteur ou UNC, telle quelle ou derrière `\\?\` ou `\\.\`, nom court compris, mais pas un chemin qui y mène par une jonction, un lien, un partage ou une autre forme de périphérique (`\\?\Volume{…}\`, `\\?\GLOBALROOT\`, `\??\`), alors que `reglages.json`, `sauvegardes.json` et `cle-ia.json` ont l'extension admise. Là, seul le oui du dialogue protège avant de remplacer un fichier ;
  - un script injecté qui aurait le jeton peut rouvrir le dialogue après chaque annulation ; il ne choisit pas le fichier, et ne reçoit ni son chemin ni son contenu ;
  - le CSV ne se relit pas à l'identique, un tableur qui le réenregistre peut retirer l'apostrophe, et une cellule de plus de 32 767 caractères ne se lit pas entière dans Excel ;
  - un tableur réglé sur la virgule, ou qui coupe aussi à la tabulation, fait une cellule de ce qui suit une virgule ou une tabulation au milieu d'une cellule sans guillemets : une formule qui la suit peut s'exécuter, puisque la neutralisation ne vise que le début d'une cellule ;
  - recharger un export attend la V2.

## À vérifier

- **Ce qu'Excel et LibreOffice affichent de l'apostrophe**, à la main, sur un fichier fictif : les exports JSON et CSV de la base de référence, faits par l'exe de test. Ce point passe à la revue du palier 5 (#70), avec le mainteneur.
- **Le vrai dialogue « Enregistrer sous »** sur l'exe publié : sa question avant de remplacer un fichier, son filtre et le nom proposé. Les tests vérifient sa construction, pas son affichage.

## Sources

- Story : <https://github.com/moonn1000/sollicito/issues/49>.
- [ADR-0001](0001-cadrage-architecture-v1.md), D10 ; [ADR-0003](0003-hote-interface-webview2.md) ; [ADR-0004](0004-v1-resserree-paliers.md) ; [ADR-0008](0008-sauvegardes-de-la-base.md) ; [ADR-0012](0012-restauration-d-une-sauvegarde.md) ; [ADR-0015](0015-import-csv-des-offres.md) ; [ADR-0016](0016-fournisseur-d-ia-et-cle-chiffree.md).

Consultées le 2026-11-27 :

[^owasp]: OWASP, « CSV Injection » : <https://owasp.org/www-community/attacks/CSV_Injection>, qui redirige vers <https://community.owasp.org/attacks/CSV_Injection>
[^excel]: Microsoft Support, « Excel specifications and limits », « Total number of characters that a cell can contain » : <https://support.microsoft.com/en-us/office/excel-specifications-and-limits-1672b34d-7043-467e-8e27-269d656771c3>
[^differee]: Microsoft Learn, « Transactions - Microsoft.Data.Sqlite », « Deferred transactions » : <https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions>
