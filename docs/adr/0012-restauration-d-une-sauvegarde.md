# ADR-0012 : restauration d'une sauvegarde, confirmée par l'hôte, vérifiée sur une copie, base copiée d'abord

- **Statut** : acceptée
- **Date** : 2026-11-21
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D10 (la restauration depuis l'application) ; [ADR-0003](0003-hote-interface-webview2.md), « Durcissement de la fenêtre » (une quatrième demande de la page à l'hôte, et un dialogue de l'hôte pour la confirmer) ; [ADR-0008](0008-sauvegardes-de-la-base.md) (la liste des copies, la copie d'avant une restauration, les restes d'un arrêt, et la phrase « Avant la troisième étape, la base n'est jamais écrite », hors retrait d'un verrou des migrations abandonné)

## Contexte

L'ADR-0008 renvoie la restauration à #28 : « Restaurer », sur toutes les copies de Sollicito du dossier, puisqu'après une panne et une réinstallation de Windows, c'est d'une autre installation qu'on restaure. Une restauration remplace toute la base : une erreur peut faire perdre à l'utilisateur ses données. Plusieurs faits, relevés le 2026-11-21, pèsent sur la façon de faire :

- **Un oui donné dans la page ne retient pas un script injecté** qui aurait le jeton : il appellerait une route sans clic (modèle de menaces, « Limites »).
- **La liste de #26 ne montre que les copies du jour de l'installation**, plus les copies d'avant une mise à jour du dossier par défaut : `PlanDesCopies.CopiesDuJour` filtre sur l'installation.
- **L'API de sauvegarde de SQLite écrit par-dessus une base abîmée.** Essai du 2026-11-21, sur SQLite 3.53.3 : cinq abîmages d'une page qui n'est pas l'en-tête (racine d'une table, page du milieu, racine d'un index, page mise à zéro, dernière page). `PRAGMA quick_check`, en lecture seule, voit chacun ; `VACUUM INTO` échoue sur chacun (`SQLITE_CORRUPT`), même quand seul un index est abîmé ; le remplacement réussit, en un appel comme par tranches, et `integrity_check` rend ensuite « ok ».
- **`PRAGMA integrity_check` évalue des expressions du schéma.** Essai du 2026-11-21 : une fonction placée dans un index sur expression et dans un index partiel est appelée quatre fois par `integrity_check`, jamais par `quick_check`. Avec `trusted_schema` coupé, un schéma qui appelle une fonction non sûre ne se charge même pas (« unsafe use of »). La page de SQLite sur les fichiers qui ne sont pas sûrs[^securite] conseille de couper `trusted_schema`, les déclencheurs et les vues inutilisés, puis, en défense de plus, `integrity_check` ou `quick_check` en premier, `cell_size_check` et aucune projection en mémoire.
- **`BackupDatabase` est un seul appel natif.** `sqlite3_backup_step` par tranches tient une transaction d'écriture ouverte sur la base pendant toute la copie, et `sqlite3_backup_finish` la défait si la copie n'est pas finie[^sauvegarde].
- **Aucune connexion n'est regroupée** (`Pooling = false`), et l'hôte lit la base en mémoire pour bâtir un brouillon (ADR-0011), sans passer par HTTP.

## Décision

### La demande, et le oui de l'utilisateur dans un dialogue de l'hôte

- La page envoie à l'hôte `{"type":"restaurerUneSauvegarde","copie":"<identifiant>"}`, sous le contrôle de la source du canal du jeton. Deux champs exactement : un champ de plus, répété compris, un identifiant d'une autre forme, un message d'une autre forme ou d'une autre source n'ouvre rien.
- L'hôte retrouve la copie dans l'état des sauvegardes tenu en mémoire, lit l'état de la base en cours par `PRAGMA quick_check`, en lecture seule, puis ouvre un dialogue de Windows (`TaskDialog`) : la date de la copie, ce qui sera perdu, « Restaurer » ou « Annuler », ce dernier par défaut. Un script de la page ne clique pas ce dialogue. Le dialogue ne promet un retour (« vous pourrez y revenir ») que si la copie d'avant se restaurera, donc devant une base saine.
- Sur le oui, l'hôte appelle le service de la restauration en mémoire, hors du fil de la fenêtre, qui reste vivante. Aucune route de l'API ne restaure. L'hôte rend à la page « en cours », puis l'issue, sans chemin ni donnée.
- Un seul dialogue de l'hôte à la fois : une demande de restauration pendant un choix de dossier reçoit « occupé » ; une demande pendant une autre restauration ne reçoit rien, pour que la page qui attend la sienne en reçoive la vraie issue ; un choix de dossier pendant une restauration n'ouvre rien.
- Les messages du jeton et du choix du dossier ne portent plus que leur champ `type` (#88).

### La liste, et l'identifiant d'une copie

- **La liste** montre toutes les copies de Sollicito, reconnues à leur nom, du dossier des copies et du dossier par défaut : copies du jour de cette installation, copies du jour d'une autre installation, copies d'avant une restauration, copies d'avant une mise à jour. Un dossier choisi peut garder lui aussi des copies d'avant : le dossier des sauvegardes d'une installation d'avant, recopié sur une clé, par exemple. La page les classe de la plus récente à la plus ancienne. La copie d'une autre installation se restaure comme les autres, mais n'entre ni dans la dernière copie, donc ni dans l'alerte ni dans `sauvegardes.json`, ni dans la rotation : l'installation se tire du nom du poste et du dossier de données, jamais de la base.
- **L'identifiant** : seize caractères hexadécimaux en minuscules, tirés par SHA-256 du lieu (dossier des copies ou par défaut), du nom et de l'instant d'écriture de la copie. Il ne contient aucun chemin. La restauration relit le dossier, et ne prend la copie que si elle porte encore le nom et l'instant que l'utilisateur a vus : réécrite entre-temps, elle est « changée » ; disparue, ou son support débranché, « introuvable ».

### Dans l'ordre, sous le sémaphore des copies

1. **La copie de travail** : la copie choisie se recopie, octet pour octet, dans le dossier de données, sous `sollicito.db.<marque>.restauration`. SQLite n'ouvre qu'elle : la sauvegarde n'est jamais ouverte, donc jamais écrite, et aucun journal resté à côté d'elle ne s'y rejoue. Puis la copie se relit : si son instant a changé pendant la recopie, elle a été réécrite, et la restauration est refusée (« a changé »).
2. **La vérification**, sur la copie de travail (voir plus bas), bornée à deux minutes. La borne et la fermeture l'interrompent même au milieu d'une instruction de SQLite (`sqlite3_interrupt`), `integrity_check` compris.
3. **La porte de la base se ferme.** Toute route de `/api` passe par elle, sauf celles marquées « sans la base » : l'état de l'application, l'état des sauvegardes (que l'alerte relit), le délai des relances, la documentation OpenAPI, et les deux routes des copies (« Sauvegarder maintenant », le retour au dossier par défaut), qui attendent déjà la fin de la restauration sous le sémaphore des copies : dans la porte, elles la retiendraient pendant qu'elle les attend. Une route ajoutée plus tard est donc gardée d'office, et un futur service de fond qui écrit la base, comme l'import d'offres de la V2, passera par la même porte. Porte fermée, l'API répond 503, « Une restauration de la base est en cours : réessayez dans un instant. », et l'hôte refuse tout brouillon. Les requêtes déjà entrées finissent d'abord, dix secondes au plus ; au-delà, « occupé », et rien n'a changé. Une écriture admise avant la fermeture entre ainsi dans la copie d'avant.
4. **L'état de la base se relit** par `quick_check`. S'il n'est plus celui que le dialogue annonçait, la restauration est refusée, et l'utilisateur la redemande.
5. **La copie d'avant** de la base en cours, dans le dossier par défaut, sur le disque de la base : elle n'attend jamais une clé, et ne part jamais vers un partage. Saine, la base se copie comme toute copie (`VACUUM INTO`, `integrity_check`, renommage), sous `sollicito-avant-restauration-<AAAAMMJJ-HHMMSSmmm>.db`, l'instant en UTC, puis se pousse sur le disque. Abîmée, ses fichiers (la base, et son journal WAL s'il en reste un) se copient tels quels, sous un nom provisoire, puis sous `sollicito-avant-restauration-<instant>-brute.db` : cette copie brute ne se liste pas et ne se restaure pas, mais un outil de récupération peut s'en servir. Un nom déjà pris décale l'instant d'une milliseconde : aucune copie d'avant n'en remplace une autre. Manquée, rien n'est remplacé.
6. **Le remplacement** par l'API de sauvegarde de SQLite, par tranches de huit pages, en une transaction de la base qui ne se valide qu'à la dernière, et qui garde son mode WAL. Un remplacement commencé va au bout.
7. **La réouverture** : la porte se rouvre quoi qu'il arrive, la copie de travail s'efface, et l'état des sauvegardes se republie, copie d'avant comprise.

Les copies d'avant une restauration se gardent toutes, comme celles d'avant une mise à jour : leur nom sort du motif des copies du jour, et la rotation ne les voit jamais. Restaurer celle d'avant une restauration est le retour en arrière.

### La vérification d'une copie qui n'est pas sûre

La copie peut venir d'un dossier partagé (un NAS, une clé passée de main en main). Chaque connexion à la copie de travail se règle, avant sa première instruction, par `SQLitePCLRaw` sur la poignée de `Microsoft.Data.Sqlite` : mode défensif, `trusted_schema` coupé, déclencheurs et vues coupés, aucune base attachée, un texte ou un blob de 10 Mo au plus, `cell_size_check` et aucune projection en mémoire. Puis, dans l'ordre :

1. ni vue, ni déclencheur, ni table virtuelle, sans quoi la copie est « étrangère » : ce refus passe avant `integrity_check`, qui évalue les expressions du schéma et appellerait la vérification d'une table virtuelle ;
2. `PRAGMA integrity_check`, sans quoi elle est « abîmée » ;
3. une taille de page égale à celle de la base, sans quoi l'API de sauvegarde refuserait une base en WAL, et une table `__EFMigrationsHistory` ;
4. l'historique des migrations (voir plus bas) ;
5. aucune clé étrangère sans sa ligne (`foreign_key_check`) ;
6. le verrou des migrations de la copie de travail, qui n'est qu'à nous, en est retiré, quel qu'il soit : une ligne restée là, ou une contrainte glissée dans la table, ferait attendre `MigrateAsync` sans fin (#79) ; EF Core le recrée tel qu'il l'écrit ;
7. la migration, si la copie est plus ancienne, puis un nouvel `integrity_check` ;
8. son schéma comparé à celui de l'exe, que ses migrations construisent dans une base en mémoire : texte des tables (une contrainte ou une collation ajoutée ne se voit pas dans les colonnes), colonnes, clés étrangères, texte des index ; puis, de nouveau, `foreign_key_check`. Seuls s'écartent les tables que SQLite tient lui-même (`sqlite_sequence`, `sqlite_stat1` à `sqlite_stat4`), ses index sans texte (`sqlite_autoindex_…`) et le verrou des migrations : un autre objet nommé `sqlite_…`, que seul `writable_schema` peut écrire, se compare comme les autres.

Ce que `Microsoft.Data.Sqlite` ne permet pas, ou que Sollicito n'emploie pas : compiler SQLite avec `SQLITE_TRUSTED_SCHEMA=0` (le paquet `e_sqlite3` vient compilé) ; `sqlite3_hard_heap_limit64`, que `SQLitePCLRaw` expose mais qui borne la mémoire de tout le processus, base en cours comprise ; marquer des fonctions `SQLITE_DIRECTONLY`, Sollicito n'en déclarant aucune ; les autres limites que la page conseille pour du SQL qui n'est pas sûr, puisque le SQL qui touche la copie est celui de Sollicito et d'EF Core.

### Les versions du schéma

- Même historique que l'exe : aucune migration.
- Un historique plus court, début exact de celui de l'exe : la copie de travail se migre, comme au lancement.
- Un historique plus long, qui prolonge celui de l'exe : refusé, la copie vient d'une version plus récente.
- Tout autre historique (une migration inconnue au milieu, une migration manquante, aucun) : étrangère.

Revenir à une version précédente de Sollicito : c'est elle qui restaure la copie d'avant sa mise à jour, qui a son schéma ; une version plus récente la remettrait à jour. La V1 sera la première version qui restaure.

### Les journaux `-wal` et `-shm` de la base

Ni renommés, ni effacés, ni remplacés : la base reste le même fichier, et le remplacement est une transaction de cette base, que SQLite écrit dans son propre journal. Arrêtée avant d'être validée, la transaction se défait au lancement suivant ; validée, elle se rejoue. Aucun journal ne peut se rejouer sur un autre fichier, puisqu'aucun fichier ne change de nom sous lui. Les journaux de la copie de travail s'effacent avec elle.

### Un arrêt brutal

| Arrêt pendant | Ce qui reste | Au lancement suivant |
| --- | --- | --- |
| la copie de travail, sa vérification | la copie de travail et ses journaux | retirés ; base d'avant |
| la copie d'avant | son fichier provisoire, dans le dossier par défaut | retiré ; base d'avant |
| entre la copie d'avant et le remplacement | la copie d'avant, entière, et la copie de travail | copie de travail retirée ; base d'avant ; copie d'avant listée |
| le remplacement | des pages non validées dans le journal WAL de la base | défaites par SQLite ; base d'avant |
| la réouverture | la transaction validée, la copie de travail | base restaurée ; copie de travail retirée |

Ces restes se retirent au lancement, sous le verrou du dossier, avant toute mise à jour : la copie de travail `.restauration` et ses journaux, les fichiers provisoires d'une copie d'avant du dossier par défaut (le nom final, une marque de huit chiffres hexadécimaux, puis `.partielle`), et le journal d'une copie brute resté sans elle quand l'arrêt tombe entre ses deux renommages, à chaque lancement et plus seulement avant une migration. Une copie finie, et tout fichier qui n'est pas à Sollicito, restent. L'API construite sans être démarrée ne retire rien.

### Fermer la fenêtre pendant une restauration

La fermeture attend la fin de la restauration : la préparation, la vérification et la copie d'avant s'interrompent ; un remplacement commencé va au bout, il dure un instant. Puis la copie de fermeture se fait, comme d'habitude.

### Après la restauration

La page revient au tableau de bord, relit tout (ses identifiants de candidatures sont périmés) et annonce la restauration. `reglages.json` et `sauvegardes.json` ne changent pas (ADR-0008). Aucune copie du jour n'est refaite sur-le-champ : celle de la fermeture suivra.

### Ce que les journaux en disent

La restauration faite (la sorte de la copie, le nombre de migrations appliquées à la copie de travail), refusée (le motif), impossible (l'étape, le type de l'erreur), la copie d'avant faite (vérifiée ou brute), une requête refusée pendant une restauration (sans son adresse). Ni chemin, ni nom de copie, ni identifiant d'installation, ni donnée.

### La version de test

Deux écarts, sous `#if VERSION_DE_TEST`, jamais compilés dans la version publiée, et que `verifier-exe.ps1` cherche dans l'exe publié :

- le dialogue de l'hôte remplacé par un fichier du dossier de données, `restauration-a-confirmer.txt`, qui vaut le oui s'il contient `restaurer` ; la question du dialogue est notée dans `restaurations-demandees.txt` ;
- un point d'arrêt : `SOLLICITO_TEST_ARRET_RESTAURATION=<étape>` fait écrire `restauration-arretee.txt` à cette étape, puis attendre. Le test tue alors l'exe par son PID, le relance, et vérifie la base et les copies.

## Alternatives écartées

- **Une route de l'API, et une confirmation dans la page** : plus simple à essayer, mais un script qui a le jeton restaurerait sans clic.
- **Renommer la copie à la place de la base** : un antivirus peut bloquer un renommage, un arrêt entre deux renommages laisse l'utilisateur sans base, et un journal resté se rejouerait sur l'autre fichier.
- **Ouvrir la sauvegarde elle-même** : SQLite l'écrirait (un journal à reprendre), et un fichier piégé s'exécuterait là où il est rangé.
- **Fermer la porte dès le oui** : 503 plus longtemps, sans rien de plus sûr, puisque la copie d'avant se fait porte fermée.
- **Ne garder que les routes qui écrivent** : un oubli laisserait passer une écriture.
- **L'historique seul, sans comparer le schéma** : une table retouchée passerait, et l'index unique d'une seule relance à faire pourrait manquer.
- **Refuser de remplacer une base abîmée** : la story veut réparer une base abîmée, et l'essai montre que SQLite écrit par-dessus.
- **Ne garder que les trois dernières copies d'avant une restauration** : chacune pèse la base, mais une restauration est un geste rare, qui passe par un clic dans le dialogue de l'hôte.
- **`Environment.FailFast` comme point d'arrêt de la version de test** : il écrit dans le journal Application de Windows, chemin de l'exe compris, et peut produire un rapport d'erreur Windows.

## Conséquences

- **Positives** : rien n'est remplacé sans le oui de l'utilisateur ; une copie abîmée, étrangère ou plus récente n'est jamais restaurée ; la base d'avant se garde toujours, et se restaure quand elle était saine ; une base abîmée se répare ; un arrêt, à toute étape, laisse une base entière ; les copies d'une autre installation se restaurent sans toucher à la rotation de celle-ci.
- **Négatives** :
  - une restauration pèse trois fois la base sur le disque, le temps de l'opération : la copie de travail, la copie d'avant, et le journal du remplacement ;
  - les copies d'avant une restauration restent sur le disque de la base, quel que soit le dossier choisi, et Sollicito ne les efface jamais ;
  - un script injecté qui a le jeton peut rouvrir le dialogue après chaque « Annuler », comme celui du choix d'un dossier (#88) ; il ne peut pas dire oui ;
  - une base qui ne s'ouvre plus du tout fait échouer le lancement : la fenêtre ne s'ouvre pas, et l'application ne peut pas restaurer (story à part) ;
  - la copie brute d'une base abîmée ne se restaure pas depuis l'application ;
  - la comparaison lit le texte des tables et des index tel que les migrations de l'exe l'écrivent : une mise à jour d'EF Core ou de SQLite qui changerait ce texte ferait refuser, comme étrangères, toutes les copies faites avant elle ; rien ne serait remplacé, mais la restauration ne marcherait plus. Un test restaure une copie de référence, faite par la version de #28 et figée dans les tests : une telle mise à jour fait échouer la CI.

## À vérifier

- **Le vrai dialogue de Windows**, sur l'exe publié, à la main : le protocole CDP ne le pilote pas. Il montre la date et la perte, « Annuler » est le bouton par défaut, et l'annuler, ou le fermer, ne change rien.
- **Un disque plein**, que les essais ne créent pas sans droits d'administrateur : ils injectent l'erreur `ERROR_DISK_FULL` à la copie de travail, à la copie d'avant et pendant le remplacement.

## Sources

- Story : <https://github.com/moonn1000/sollicito/issues/28> ; suites de #26 : <https://github.com/moonn1000/sollicito/issues/88>
- [ADR-0001](0001-cadrage-architecture-v1.md), D10 ; [ADR-0003](0003-hote-interface-webview2.md) ; [ADR-0008](0008-sauvegardes-de-la-base.md) ; [ADR-0011](0011-relances-et-brouillon.md)
- Essais du 2026-11-21, sur le SQLite de `Microsoft.Data.Sqlite` 10.0.15 (3.53.3) : une base abîmée de cinq façons, puis remplacée ; une fonction du schéma appelée par `integrity_check` ; les réglages défensifs posés par `sqlite3_db_config`

Consultées le 2026-11-21 :

[^securite]: SQLite, « Defense Against The Dark Arts », fichiers de base qui ne sont pas sûrs (`trusted_schema`, déclencheurs et vues, `integrity_check` ou `quick_check` en premier, `cell_size_check`, `mmap_size`) : <https://www.sqlite.org/security.html> ; options de `sqlite3_db_config` : <https://www.sqlite.org/c3ref/c_dbconfig_defensive.html>
[^sauvegarde]: SQLite, `sqlite3_backup_step` et `sqlite3_backup_finish` (transaction tenue pendant la copie, défaite si elle n'est pas finie ; taille de page d'une base en WAL) : <https://www.sqlite.org/c3ref/backup_finish.html>
