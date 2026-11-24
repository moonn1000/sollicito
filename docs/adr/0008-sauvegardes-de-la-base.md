# ADR-0008 : sauvegardes de la base, migration sur une copie et premier réglage

- **Statut** : acceptée ; « Le premier réglage de l'application, hors de la base » précisé par l'[ADR-0011](0011-relances-et-brouillon.md) (le délai des relances, second réglage, lu et écrit à part) ; « La restauration attend #28 », la liste des copies, les restes d'un arrêt, et « Avant la troisième étape, la base n'est jamais écrite » (hors retrait d'un verrou des migrations abandonné) précisés par l'[ADR-0012](0012-restauration-d-une-sauvegarde.md), qui fait aussi avancer la troisième étape par tranches ; « Le premier réglage de l'application, hors de la base » précisé par l'[ADR-0016](0016-fournisseur-d-ia-et-cle-chiffree.md) (le fournisseur d'IA, troisième réglage, écrit même pour « aucune IA » : « jamais choisi » s'en distingue)
- **Date** : 2026-11-20
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D3 (les migrations s'appliquent à une copie de la base) et D10 (moment, rotation et dossier des sauvegardes, copies d'avant une mise à jour, réglages hors de la base) ; [ADR-0003](0003-hote-interface-webview2.md), « Durcissement de la fenêtre » (une seconde demande de la page à l'hôte, après le brouillon de relance) ; [ADR-0004](0004-v1-resserree-paliers.md), « Ce que livre la V1 », données (« les sept dernières » : les copies du jour des sept derniers jours d'utilisation, par installation ; « une avant chaque migration » : une par mise à jour, nommée d'après la dernière migration qui attend)

## Contexte

La D10 de l'ADR-0001 veut une sauvegarde automatique de la base, avec rotation (« par exemple les sept dernières »), dans un dossier que l'utilisateur peut placer sur un autre support, et un avertissement : ces copies ne sont pas chiffrées. L'ADR-0004 fixe pour la V1 « les sept dernières, plus une avant chaque migration », et renvoie la restauration au palier 3 (#28). #27, fusionnée dans #26, demande qu'une migration ratée laisse la base d'avant en place, et que l'erreur soit expliquée.

Trois faits, relevés le 2026-11-20, pèsent sur la façon de faire :

- **La base est en mode WAL**, ou devait l'être : Entity Framework Core la crée avec `PRAGMA journal_mode = 'wal'` (chaîne relevée dans `Microsoft.EntityFrameworkCore.Sqlite` 10.0.15). Ce qui est validé peut donc dormir dans `sollicito.db-wal`, et une copie du seul fichier `sollicito.db` le perdrait. Sur `main`, pourtant, la base se créait en mode `delete` : le retrait d'un verrou des migrations abandonné (#79) ouvrait la base avant les migrations, ce qui la créait vide, et EF Core ne passait plus par sa propre création.
- **Une migration n'est pas toujours une seule transaction** : sous SQLite, EF Core reconstruit une table en plusieurs transactions, les clés étrangères coupées entre deux (#85, point 1, sur le `Down()` de `CorrectionsDesEtapes`). Un arrêt au milieu laisse une base à moitié migrée.
- **Le dossier choisi ne peut pas venir de la page** : un script injecté dans l'interface appellerait l'API à sa place (modèle de menaces, « Limites »). Un chemin reçu par une route lui laisserait envoyer chaque copie vers un partage réseau de son choix, l'empreinte NTLM du compte avec.

## Décision

### Une copie par jour d'utilisation, qui suit la base jusqu'au soir

- **Au lancement**, une fois la base prête, en arrière-plan : la copie du jour, si le dossier n'en a pas encore.
- **À la fermeture**, la copie du jour est refaite : elle garde l'état de la base à la fin de la dernière séance. Elle ne retient pas la fermeture plus de dix secondes ; au-delà, elle est abandonnée, et celle d'avant reste. Abandonnée, elle s'interrompt et retire son fichier provisoire avant la sortie du processus, en deux secondes au plus. Un fichier resté malgré tout, qu'un autre programme tient, ou d'une copie qui ne s'est pas arrêtée à temps, se retire au lancement suivant, même si la copie du jour est déjà faite. Elle ne se fait qu'une fois, même quand l'hôte arrête deux fois ses services (relevé sous l'hôte des tests), et chaque arrêt l'attend.
- **Chaque heure**, par une minuterie dans le processus de l'API, sans autre processus : la copie du nouveau jour d'une séance restée ouverte après minuit, et un nouvel essai après un échec.
- **« Sauvegarder maintenant »**, et « Réessayer » dans l'alerte du tableau de bord, refont la copie du jour.
- Le dossier garde **les sept dernières copies du jour**, soit les sept derniers jours d'utilisation.

Une copie du jour se nomme `sollicito-<installation>-<AAAA-MM-JJ>.db`. L'installation est un identifiant de huit caractères, haché (SHA-256) à partir du nom du poste et du dossier de données, propre au compte Windows : il ne révèle ni l'un ni l'autre, et ne se range nulle part. Deux postes qui partagent un dossier (un NAS, une clé) ne s'écrasent pas leur copie du jour et ne s'effacent pas leurs copies. La rotation ne touche qu'aux copies du jour de l'installation : jamais un autre fichier du dossier.

### Copier une base ouverte : `VACUUM INTO`, puis vérifier

Chaque copie s'écrit par `VACUUM INTO` vers un nom provisoire du dossier de destination, se relit par `PRAGMA integrity_check`, puis prend son nom final par un renommage. `VACUUM INTO` écrit un instantané cohérent de la base, ce qui dort dans le journal WAL compris, dans un fichier autonome[^vacuum]. Son chemin passe en paramètre lié. Une copie manquée retire son fichier provisoire, et la rotation n'efface une ancienne copie qu'après une nouvelle copie vérifiée. Une opération sur un fichier qu'un autre programme tient un instant, comme un antivirus qui examine un fichier tout juste écrit, se reprend quelques fois. Les appels de SQLite ne rendent la main qu'une fois finis : la copie de la fermeture tourne donc sur un autre fil, pour que son délai de dix secondes coure dès son début. Ils ne regardent pas non plus l'annulation. Une copie annulée s'interrompt donc par `sqlite3_interrupt`[^interrupt], en pleine écriture comme pendant sa vérification. Une fois annulée, même vérifiée, elle ne prend plus son nom, reprises du renommage comprises : seul un renommage déjà lancé, qui est immédiat, va au bout. Une base qu'une autre connexion occupe se réessaie jusqu'à trente secondes, le délai que `Microsoft.Data.Sqlite` donne d'office à une commande, mais par tranches d'une seconde : ses propres essais ne regardent pas l'annulation, qui se voit entre deux tranches.

Le 2026-11-20, une sonde sur le SQLite de Python (3.50.4), puis les tests d'intégration sur celui de `Microsoft.Data.Sqlite` (3.53.3), l'ont vérifié. La copie emporte une ligne validée restée dans le WAL, et laisse une écriture non validée. Le fichier obtenu, en journal `delete`, se relit sain.

### Avant une migration : une copie, et la migration sur une autre copie

Au lancement, quand des migrations attendent sur une base existante :

1. **La copie d'avant**, vérifiée, dans le dossier par défaut, sur le disque de la base : une mise à jour n'attend jamais un support amovible. Elle se nomme `sollicito-avant-<migration>.db`, d'après la dernière migration qui attend. Les copies d'avant se gardent toutes, à part des sept copies du jour, une par mise à jour. Un nouvel essai de la même mise à jour remplace la sienne, car la base a pu changer entre deux essais.
2. **Les migrations s'appliquent à une seconde copie**, dans le dossier de données, qui se vérifie à son tour.
3. **La copie migrée remplace le contenu de la base** par l'API de sauvegarde de SQLite[^sauvegarde][^backupdatabase], en une transaction de la base, qui garde son mode WAL : aucun renommage qu'un antivirus pourrait bloquer, aucun journal laissé à côté de la base qui serait rejoué sur un autre fichier. Depuis #28, ce pas avance par tranches de huit pages, toujours en une seule transaction, qui ne se valide qu'à la dernière ([ADR-0012](0012-restauration-d-une-sauvegarde.md)).

Avant la troisième étape, la base n'est jamais écrite. Une migration qui échoue, ou un arrêt brutal, la laisse telle qu'avant, et la version précédente de Sollicito l'ouvre toujours. Le lancement s'arrête alors sur un message pour l'utilisateur, sans chemin : la base n'a pas changé, et une copie en a été faite dans `%LOCALAPPDATA%\Sollicito\Sauvegardes`, écrit en toutes lettres. Sans migration en attente, ni copie ni attente. Sollicito ne lance jamais `Down()` : revenir à une version précédente passe par la copie d'avant la mise à jour, que #28 saura restaurer.

La seconde copie pèse autant que la base, et son nom porte une marque tirée au hasard : `sollicito.db.<marque>.migration`. Celle qu'un arrêt brutal a laissée, et son journal, se retirent au lancement suivant, sous le verrou du dossier, avant toute mise à jour, comme les fichiers provisoires de `reglages.json` et de `sauvegardes.json`. La base et ses journaux n'y répondent jamais.

**La base se crée en mode WAL** : une base neuve n'a ni verrou abandonné ni rien à copier, et les migrations la créent elles-mêmes. Une base créée avant #26, restée en mode `delete`, passe en WAL au lancement suivant. Le mode WAL ne vaut pas sur un système de fichiers en réseau[^wal] : la base vit dans `%LOCALAPPDATA%`, et une copie, en journal `delete`, peut aller sur un partage. La version embarquée de SQLite (3.53.3) corrige le défaut de remise à zéro du WAL décrit par SQLite, présent jusqu'à la 3.51.2[^wal].

### Le dossier, choisi par le seul dialogue de l'hôte

- **Le dossier par défaut** est `%LOCALAPPDATA%\Sollicito\Sauvegardes`, dans le dossier de données (D10), créé au besoin. L'écran dit qu'il est sur le disque de la base, donc sans protection contre la panne de ce disque.
- **Choisir un autre dossier** : la page demande à l'hôte, par un message (`postMessage`), d'ouvrir le dialogue de dossier de Windows[^dialogue]. L'hôte contrôle ce message comme celui du canal du jeton : sa source est l'origine de l'interface, et il a la forme attendue. Le message ne porte aucun chemin. L'hôte ouvre le dialogue après la fin du gestionnaire de l'événement, jamais deux à la fois, puis copie aussitôt la base dans le dossier choisi. Réussie, cette copie prouve qu'on peut y écrire, et le dossier devient celui des sauvegardes ; manquée, l'ancien reste. L'hôte rend à la page l'issue (choisi, annulé, refusé), sans chemin, et la page relit l'état par l'API.
- **Aucune route de l'API ne reçoit un chemin.** Un script injecté peut ouvrir le dialogue, où l'utilisateur choisit, ou ramener les copies au dossier par défaut, par une route sans chemin : il ne peut pas les envoyer ailleurs.
- **Un dossier en réseau ou synchronisé** (NAS, OneDrive) reste le choix de l'utilisateur, fait dans le dialogue de Windows : les copies partent alors où ce dossier les envoie.
- Changer de dossier ne déplace ni n'efface les copies de l'ancien.

### Le premier réglage de l'application, hors de la base

Le dossier choisi est le premier réglage de l'application. Il vit dans `reglages.json`, dans le dossier de données, hors de la base, écrit d'un bloc : un fichier provisoire, puis un renommage. Le délai des relances (#14) et le fournisseur d'IA (#43) s'y ajouteront ; une écriture garde les réglages qu'elle ne connaît pas. Les réglages vivent hors de la base pour quatre raisons :

- ils se lisent avant que la base ne soit migrée ;
- une restauration (#28) ne doit pas changer le dossier des sauvegardes ;
- une copie ne doit pas emporter un chemin propre à ce poste ;
- la D10 met déjà l'import « sans effet sur les réglages ».

Un fichier absent vaut des réglages par défaut. Un fichier illisible n'arrête pas Sollicito : les copies vont dans le dossier par défaut, et l'alerte le dit, jusqu'au prochain choix d'un dossier, qui réécrit le fichier.

### Un dossier qui manque : ni copie, ni repli

Si le dossier choisi manque (clé retirée, partage débranché), Sollicito ne fait aucune copie, n'en efface aucune, et ne se replie pas ailleurs. Il ne recrée jamais le dossier : une autre clé a pu prendre la même lettre. Le tableau de bord affiche une alerte : depuis quand aucune copie n'a réussi, et quoi faire. L'instant de la dernière copie réussie se garde dans `sauvegardes.json`, à côté des réglages, sans chemin : relancé alors que le dossier manque, Sollicito le dit encore. Sollicito réessaie chaque heure, à la fermeture, au lancement suivant et sur « Réessayer » ; l'alerte part dès qu'une copie réussit. Elle se lit dans l'état que l'API tient en mémoire, sans toucher au dossier, qu'un partage débranché pourrait bloquer plusieurs secondes. Le tableau de bord relit cet état toutes les trente secondes tant qu'il est affiché, et chaque seconde pendant la vérification du lancement : l'alerte vient et part sans qu'on change d'écran, et l'état lu remplace l'ancien sans rien effacer entre-temps. Une relecture partie avant un « Réessayer » réussi ne la ramène pas.

### La restauration attend #28

L'écran des réglages liste les copies gardées, sans proposer de restaurer ; #28 y ajoutera « Restaurer », sur toutes les copies de Sollicito du dossier. Après une panne et une réinstallation de Windows, c'est d'une autre installation qu'on restaure. Aucun texte ne présente une copie comme le moyen de rendre une candidature supprimée : la suppression reste définitive (#25).

### Journaux

Aucun journal ne cite le dossier de données, le dossier des sauvegardes ni une copie, qui portent en général le nom du compte Windows : seulement ce qui s'est passé, et le type d'une erreur. Une exception qui sortirait du service des copies serait écrite en entier par l'hôte, qui s'arrêterait[^service] : le service attrape donc tout, hors annulation. L'échec de la préparation de la base se relance sans l'exception d'origine, que l'hôte écrirait avec son message, chemin compris (#80, point 1).

## Alternatives écartées

- **Une copie à chaque lancement seulement** : la dernière copie retarde d'une séance, et plusieurs lancements par jour raccourcissent l'historique gardé.
- **Une copie après chaque écriture** : des centaines de copies, et la rotation se vide en une journée.
- **Copier le fichier de la base** : il manquerait ce qui dort dans le journal WAL, et une page à moitié écrite pourrait passer.
- **L'API de sauvegarde de SQLite pour chaque copie** : aussi sûre, mais la copie garde le mode WAL de la base, et des fichiers de journal se créent à côté d'elle à chaque ouverture.
- **Migrer sur place, puis remettre la copie d'avant en cas d'échec** : un arrêt brutal pendant une étape hors transaction laisserait une base à moitié migrée.
- **Garder seulement les trois dernières copies d'avant migration** : l'ADR-0004 en veut une avant chaque migration.
- **Une route de l'API qui reçoit le dossier choisi** : un script injecté y enverrait les copies où il veut.
- **Une table de réglages dans la base** : une restauration la ramènerait avec les données, et elle ne se lit pas avant la migration.
- **Se replier sur le dossier par défaut quand le dossier choisi manque** : la copie ne protège plus de la panne du disque, s'écrit là où l'utilisateur n'en voulait plus, et deux rotations se mêlent.

## Conséquences

- **Positives** : une copie par jour, qui garde la fin de la séance ; aucune mise à jour qui laisse une base à moitié migrée ; un dossier que seul l'utilisateur choisit ; des copies d'un poste que la rotation d'un autre ne touche pas ; une base en mode WAL, comme EF Core la crée ; un fichier de réglages prêt pour #14 et #43.
- **Négatives** :
  - les copies ne sont pas chiffrées : toute personne qui accède à leur dossier les lit, et une clé formatée en FAT32 ou exFAT n'a pas de permissions (#51 étudiera le chiffrement) ;
  - une copie de fermeture abandonnée qui ne s'arrête pas en deux secondes, ou dont le fichier provisoire ne s'efface pas, le laisse jusqu'au lancement suivant ;
  - une séance qui se termine par un arrêt brutal ne refait pas la copie du jour : la copie du lancement reste ;
  - les copies d'avant une mise à jour restent sur le disque de la base, quel que soit le dossier choisi ;
  - un fichier de réglages perdu fait revenir au dossier par défaut sans alerte, et le dossier choisi ne reçoit plus de copie ;
  - un changement du nom du poste, ou une réinstallation, change l'identifiant de l'installation : les anciennes copies du jour restent, hors de la rotation.

## À vérifier

- **Dialogue de Windows** : le dialogue de la version publiée ne se pilote pas par le protocole CDP ; la version de test le remplace par un dossier écrit dans un fichier. À vérifier à la main sur l'exe publié : le dialogue s'ouvre sur le dossier courant, admet un dossier en réseau, et son annulation ne change rien.

## Sources

- Story : <https://github.com/moonn1000/sollicito/issues/26> ; #27 : <https://github.com/moonn1000/sollicito/issues/27>
- [ADR-0001](0001-cadrage-architecture-v1.md), D3 et D10 ; [ADR-0004](0004-v1-resserree-paliers.md), « Ce que livre la V1 » et « Paliers »
- Constats de #25, point 1 (`Down()` de `CorrectionsDesEtapes`) : <https://github.com/moonn1000/sollicito/issues/85>
- Relevés du 2026-11-20 : chaîne `PRAGMA journal_mode = 'wal';` dans `Microsoft.EntityFrameworkCore.Sqlite.dll` 10.0.15 ; version 3.53.3 (source du 2026-06-26) dans `e_sqlite3.dll` de `SQLitePCLRaw.lib.e_sqlite3` 2.1.13 ; mode `delete` de la base créée par `main`
- Relevé du 2026-11-21 : `Microsoft.Data.Sqlite` (dépôt `dotnet/efcore`, branche `release/10.0`, `SqliteDataReader.cs` et `SqliteCommand.cs`) réessaie une base occupée par `Thread.Sleep(150)` jusqu'au délai de la commande, trente secondes d'office, sans regarder l'annulation, que ses méthodes asynchrones ne lisent qu'au départ

Consultées le 2026-11-20 :

[^vacuum]: SQLite, `VACUUM INTO` (instantané cohérent, nom en expression, fichier cible absent ou vide, sortie incomplète si interrompue) : <https://www.sqlite.org/lang_vacuum.html>
[^sauvegarde]: SQLite, API de sauvegarde en ligne : <https://www.sqlite.org/backup.html>
[^backupdatabase]: Microsoft Learn, `Microsoft.Data.Sqlite`, sauvegarde par `BackupDatabase` : <https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup>. Depuis #28, `CopieDeLaBase.RemplacerAsync` n'appelle plus `BackupDatabase`, qui copie tout en un pas, mais `sqlite3_backup_step` par tranches de huit pages, la même API de SQLite : un arrêt pendant le remplacement s'essaie entre deux tranches ([ADR-0012](0012-restauration-d-une-sauvegarde.md), « Dans l'ordre », 6).
[^interrupt]: SQLite, `sqlite3_interrupt()` (s'appelle d'un autre fil, sur une connexion qui ne se ferme pas avant son retour ; sans instruction en cours, il est sans effet), consultée le 2026-11-21 : <https://www.sqlite.org/c3ref/interrupt.html>
[^wal]: SQLite, mode WAL (persistant ; dernière connexion qui reprend et efface le journal ; pas de système de fichiers en réseau ; défaut de remise à zéro du WAL, versions 3.7.0 à 3.51.2) : <https://www.sqlite.org/wal.html>
[^dialogue]: Microsoft Learn, `FolderBrowserDialog` : <https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.folderbrowserdialog>
[^service]: Microsoft Learn, .NET 6, exception non gérée d'un `BackgroundService` (écrite au journal, hôte arrêté par défaut) : <https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling>
