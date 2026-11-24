# ADR-0016 : le fournisseur d'IA, un réglage, un port, et une clé chiffrée hors de la base

- **Statut** : acceptée ; critères et décisions acceptés par le pilote le 2026-11-24
- **Date** : 2026-11-24
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D7 (l'interface de fournisseur, ce que #43 en branche ; « une clé, une adresse » : la clé liée à son fournisseur, effacée quand il change), D9 (« Secrets et journaux » : le trousseau, son dossier, DPAPI en portée utilisateur, ce que les journaux en disent) et D10 (la clé hors de la base, de ses copies et des exports ; ce qu'elle devient après une restauration) ; [ADR-0004](0004-v1-resserree-paliers.md), « Contexte » (le premier écran qui propose de connecter une IA) ; [ADR-0008](0008-sauvegardes-de-la-base.md), « Le premier réglage de l'application, hors de la base » (un troisième réglage, qui distingue « jamais choisi » d'« aucune IA »)

## Contexte

La story #43, qui absorbe #44, ouvre le palier 4 (ADR-0004) : choisir son fournisseur d'IA, ou aucun, et enregistrer sa clé. Les appels réels viennent ensuite : Claude et OpenAI avec #48, Ollama avec #45 ; l'extraction d'une offre (#46) et le brouillon de relance (#47) s'appuient sur eux. Plusieurs décisions la cadrent déjà :

- **D7 de l'ADR-0001** : une IA facultative, derrière une interface de fournisseur ; sans elle, tout fonctionne. « Une clé, une adresse » : chaque clé est liée à l'adresse officielle de son fournisseur, et une adresse réglable qui change efface la clé.
- **D9** : la clé chiffrée par Data Protection, dont le trousseau est protégé par DPAPI en portée utilisateur, jamais machine ; aucun journal ne la contient.
- **D10** : toutes les données, trousseau compris, dans `%LOCALAPPDATA%\Sollicito` ; la clé rangée hors de la base, ni dans les sauvegardes ni dans les exports, et à ressaisir sur une autre machine.
- **ADR-0008 et ADR-0011** : les réglages vivent dans `reglages.json`, chacun lu et écrit à part, et le fournisseur d'IA y était annoncé.
- **La spec de la V1 (#5), l'ADR-0004 et le README** promettent qu'« un premier écran propose de connecter une IA, facultative ». Aucune story ne le portait.

Quatre faits, relevés le 2026-11-24, pèsent sur la façon de faire :

- **Data Protection a un dossier par défaut sous le profil Windows** : sans dépôt réglé, ses clés vont sous `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`. Un dépôt en fichiers retire en outre le chiffrement des clés au repos : DPAPI doit être demandé explicitement[^stockage][^format].
- **Les tests de l'API changent le dossier de la base dans ses services** : `ApiLocale.Construire` reçoit, lui, le vrai dossier de l'utilisateur. Tout ce qui s'en déduirait à la construction viserait le vrai profil.
- **Le dépôt en fichiers de Data Protection lit chaque fichier XML du trousseau** : un fichier abîmé y fait échouer toute la lecture, et aucune clé neuve ne s'enregistre plus.
- **Ses journaux** vont sous `Microsoft.AspNetCore.DataProtection.*`[^vue], et citeraient le chemin du trousseau, donc le nom du compte.

## Décision

### Le réglage : aucune IA par défaut, et « jamais choisi »

Le fournisseur est le troisième réglage de `reglages.json`, sous `fournisseurDIA`. Il vaut `Aucun`, `Claude`, `OpenAI` ou `Ollama`, lu par son nom exact : ni nombre, ni autre casse.

- **Absent ou nul**, aucun choix n'est fait, et aucune IA ne s'applique. « Aucune IA » choisie s'écrit, elle : c'est ce qui distingue la carte à proposer d'un choix fait.
- **Toute autre valeur** le rend illisible : aucune IA ne s'applique, et l'écran le dit. Il ne touche pas aux autres réglages, ni eux à lui.
- **Son écriture** ne change que lui, et garde le reste du fichier, réglages inconnus compris, sous le verrou commun des écrivains. Sur un fichier illisible en entier, elle est refusée (409), comme celle du délai (ADR-0011).
- **En V1, le modèle local ne prend aucune clé** : Ollama tourne sur le poste. Son adresse viendra avec #45, dans sa propre propriété.

### Le premier écran : une carte au tableau de bord

Tant qu'aucun choix n'est enregistré, une carte en tête du tableau de bord propose de connecter une IA, facultative. « Choisir une IA » mène à la section « IA » des réglages ; « Continuer sans IA » enregistre « Aucune IA », et la carte ne revient plus. Elle ne bloque rien. Un fichier des réglages illisible en entier ne la montre pas.

### La clé et son trousseau, hors de la base

- **Le trousseau** vit dans `%LOCALAPPDATA%\Sollicito\Trousseau\`, où la D10 le range. Ses clés sont protégées par DPAPI, en portée utilisateur.
- **La clé chiffrée** vit à part, dans `%LOCALAPPDATA%\Sollicito\cle-ia.json`, écrite d'un bloc : un fichier provisoire, puis un renommage. Une seule clé à la fois. Le fichier ne porte que la charge protégée : ni le fournisseur ni rien d'autre en clair.
- **Data Protection se construit à part** : un fournisseur autonome, construit au premier usage et réutilisé, jamais au lancement, jamais dans les services de l'API. Il n'a qu'un dépôt, celui du trousseau, déduit du dossier de la base des services : jamais le dossier par défaut. Il n'a aucun fournisseur de journaux. Tant qu'aucune clé n'est enregistrée, le trousseau n'existe pas.
- **Son dépôt est le nôtre** : un fichier XML par clé du trousseau, écrit d'un bloc. Un fichier illisible (abîmé, tenu, trop gros, ou qui déclare un DTD) est ignoré, comme une clé d'un autre compte : une nouvelle clé d'IA s'enregistre alors sur un trousseau renouvelé.
- **La charge protégée** porte, en JSON, le fournisseur et la clé, sous une fin (purpose) propre à la clé d'IA. Une charge relue pour un autre fournisseur que celui du réglage est illisible : elle ne sert jamais.
- **Une lecture** ne se fait que si `cle-ia.json` existe : pour dire « enregistrée » ou « illisible », l'API déchiffre, compare le fournisseur, revérifie la forme, puis oublie la valeur. Les octets du clair se remettent à zéro après usage.
- **Ce qui la rend illisible** : un trousseau absent, abîmé ou d'un autre compte, une charge abîmée, un autre fournisseur, un fichier tenu. L'application continue sans IA, et aucune exception ne sort de la lecture.
- **En mémoire**, la clé vit dans un type dont le texte ne la montre pas, et qui refuse de passer en JSON.
- **Hors de la base, des copies et du dossier des copies** : une copie ne prend que la base. L'export à venir (#49) n'a rien à en exclure.

### « Une clé, une adresse » : un ordre, un verrou, un lien

- **Un seul verrou**, celui des réglages, pour `reglages.json` et `cle-ia.json`.
- **Changer de fournisseur efface d'abord la clé, puis écrit le réglage.** Une clé qui ne s'efface pas, après les reprises, fait refuser le changement (409) sans rien changer. Choisir le fournisseur déjà réglé ne change rien, et la clé reste. #45 branchera la même règle au changement de l'adresse du modèle local.
- **La clé porte son fournisseur** : un arrêt entre deux écritures, ou un `reglages.json` réécrit hors de l'API, laissent au plus une clé illisible, jamais envoyée à un autre.
- **Enregistrer une clé** exige un réglage lisible et un fournisseur qui en prend une (Claude, OpenAI) ; sinon 409. La retirer reste permis sur un fichier de réglages illisible.

### La forme d'une clé

Ses blancs du bord retirés, une clé fait de 20 à 512 caractères ASCII imprimables, sans espace. Une clé de Claude commence par `sk-ant-` ; une clé d'OpenAI par `sk-`, mais pas par `sk-ant-`. Le préfixe garde « une clé, une adresse » : une clé d'OpenAI collée sous Claude partirait chez Anthropic. Un refus ne reprend jamais la saisie, ni sa longueur, ni son début.

### La saisie, dans la page, une fois

La clé se tape dans un champ masqué de la section « IA », sans saisie automatique ni correcteur. Il se vide après l'envoi, quelle qu'en soit l'issue. Aucune réponse ne rend la clé, ni sa longueur, ni son début, ni une empreinte : l'écran dit seulement qu'une clé est enregistrée, à remplacer ou à retirer. Changer de fournisseur, quand une clé est enregistrée, et retirer la clé passent par une confirmation dans la page, « Annuler » en premier.

### Les routes

`GET /api/reglages/ia` rend l'état, sans la clé : le fournisseur qui s'applique, s'il a été choisi, si le réglage et le fichier se lisent, et la clé (sans objet, aucune, enregistrée, illisible). `PUT /api/reglages/ia` choisit le fournisseur ; `PUT /api/reglages/ia/cle` enregistre la clé du fournisseur réglé ; `DELETE /api/reglages/ia/cle` la retire.

Toutes passent par les protections de l'API (#10), figurent dans OpenAPI, lisent leur corps strictement, le bornent à 4 Kio (413), et ne portent aucune donnée dans leur adresse. Elles ne touchent pas la base : marquées « sans la base », elles restent ouvertes pendant une restauration, comme le délai des relances.

### Le port du fournisseur

`IFournisseurDIA` n'a qu'un geste, demander. La demande sépare les consignes, écrites dans le code, des données, qui ne sont pas fiables et ne parviennent à l'IA que comme données à analyser. Elle borne la réponse, en caractères et en durée. Le port ne donne à l'IA ni outil, ni adresse, ni en-tête : rien ne lui permet d'agir. La réponse est un texte borné, ou une issue fermée : « indisponible », avec sa raison, ou un échec typé ; jamais une exception qui porterait le contenu, et aucun texte d'objet ne montre les données ni la réponse.

#43 ne branche que l'adaptateur « aucune IA », qui répond toujours « indisponible » avec sa raison : aucune IA, réglage illisible, clé absente ou illisible, fournisseur pas encore branché. Un faux sert aux tests. Un banc de contrat éprouve les deux, et éprouvera ceux de #48 et #45. Le JSON validé contre un schéma (#46) et le texte brut affiché (#47) se décident au-dessus du port, dans leurs stories. #43 n'a pas de bouton « Tester » : sans appel réel, il passerait pour un essai réussi.

### Après une restauration, et ailleurs

- **Sur le même compte**, une restauration ne change ni le réglage ni la clé : ni l'un ni l'autre n'est dans la base.
- **Sur un autre poste ou un autre compte**, une copie restaurée n'emporte pas la clé : elle est à ressaisir. Un dossier de données recopié en entier emporte le trousseau, que DPAPI n'ouvre pas sous un autre compte, ni en général sur un autre poste ; de même après la réinitialisation d'un mot de passe Windows par un administrateur. La section dit alors que la clé est illisible, à ressaisir, et Sollicito continue sans IA.

### Les journaux

L'API écrit le fournisseur choisi (une valeur d'une liste fermée), la clé enregistrée, remplacée, retirée, effacée au changement, ou illisible avec le type de l'erreur (au niveau Debug, parce que l'état se relit souvent), et le motif d'un refus. Jamais la clé, sa longueur, son début ou son empreinte ; jamais le chemin du trousseau ou de la clé ; jamais le message d'une exception de DPAPI ou de Data Protection. Data Protection, sans fournisseur de journaux, n'écrit rien.

## Alternatives écartées

- **La clé chiffrée dans `reglages.json`** : un fichier de moins, mais un fichier illisible y perdrait la clé, le choix d'un dossier qui le réécrit l'effacerait en silence, et le fichier, que l'on peut montrer pour un dépannage, porterait le secret.
- **`AddDataProtection` dans les services de l'API** : le lancement chargerait le trousseau, ses journaux passeraient par ceux de l'API, chemin compris, et un réglage manqué retomberait sur le dossier par défaut, sous le vrai profil.
- **Le dépôt en fichiers de Data Protection** : un seul fichier abîmé du trousseau bloquerait tout nouvel enregistrement de clé.
- **Écrire le réglage avant d'effacer la clé** : un arrêt entre les deux laisserait sur le disque une clé liée à l'ancien fournisseur, inutilisable mais présente.
- **Un dialogue de l'hôte pour saisir la clé** : la page ne la verrait jamais, mais c'est un dialogue natif de plus, avec son remplaçant dans la version de test, et un script injecté pourrait encore retirer la clé ou changer de fournisseur.
- **La forme seule, sans préfixe** : un format qui change chez un fournisseur ne serait pas refusé, mais une clé collée sous le mauvais fournisseur partirait chez l'autre.
- **Un « Tester » dès #43** : sans adaptateur réel, il ne vérifierait que la présence de la clé.
- **La section des réglages seule, sans carte** : la promesse du premier écran resterait à tenir.

## Conséquences

- **Positives** : aucune IA par défaut, et rien n'est écrit tant qu'aucune clé ne l'est ; une clé jamais en clair sur le disque, jamais rendue à la page, jamais dans un journal ; une clé qui ne part jamais vers un autre fournisseur que le sien ; un trousseau abîmé qui ne bloque pas une nouvelle clé ; un port prêt pour #45, #46, #47 et #48.
- **Négatives** :
  - la clé traverse la page une fois, à la saisie : un script injecté qui aurait le jeton pourrait la lire à ce moment, la remplacer, la retirer ou changer de fournisseur, sans jamais la relire ensuite (modèle de menaces, « Limites ») ;
  - DPAPI ne protège pas d'un programme lancé sous le compte de l'utilisateur (D9) : il déchiffre le trousseau comme Sollicito ;
  - la clé est à ressaisir sur un autre poste, sous un autre compte, ou après la réinitialisation d'un mot de passe Windows ;
  - le trousseau, une fois né, reste dans le dossier de données, même sans clé : il ne protège plus rien ;
  - le trousseau se garde en mémoire : un trousseau abîmé pendant une séance ne se voit qu'au lancement suivant ;
  - quand une clé est enregistrée mais que le trousseau n'a plus de clé valide (absent, abîmé, ou à son renouvellement de 90 jours), la simple lecture de l'état en crée une, comme Data Protection le fait d'office : seule l'absence de toute clé d'IA garantit qu'aucun trousseau ne s'écrit ;
  - après des réglages illisibles, réécrits par le choix d'un dossier des sauvegardes, choisir de nouveau un fournisseur efface la clé restée sur le disque, sans confirmation : l'écran ne la montre pas tant qu'aucun choix n'est fait. Elle se ressaisit.

## À vérifier

- **Un second compte Windows** : le dossier de données recopié sous un autre compte, la clé y est dite illisible, et se ressaisit. Les tests simulent ce cas par une enveloppe DPAPI que le compte n'ouvre pas ; le vrai second compte se vérifie à la main, sans bloquer.

## Sources

- Story : <https://github.com/moonn1000/sollicito/issues/43> ; #44, absorbée : <https://github.com/moonn1000/sollicito/issues/44>
- [ADR-0001](0001-cadrage-architecture-v1.md), D7, D9 et D10 ; [ADR-0004](0004-v1-resserree-paliers.md) ; [ADR-0008](0008-sauvegardes-de-la-base.md) ; [ADR-0011](0011-relances-et-brouillon.md)
- Relevé du 2026-11-24 : `ApiLancee` remplace le `DossierDeLaBase` des services, quand `ApiLocale.Construire(args)` reçoit le dossier de l'utilisateur.

Documentation Microsoft d'ASP.NET Core (dépôt `dotnet/aspnetcore.docs`), consultée le 2026-11-24 :

[^stockage]: Data Protection, fournisseurs de stockage des clés (`PersistKeysToFileSystem` retire le chiffrement des clés au repos) : <https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers>
[^format]: Data Protection, format de stockage des clés (dossier par défaut sous Windows : `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`) : <https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-format>
[^vue]: Data Protection, configuration (journaux sous `Microsoft.AspNetCore.DataProtection`) ; scénarios hors injection de dépendances (`DataProtectionProvider.Create`, avec DPAPI) ; gestion des clés (durée de vie de 90 jours, trousseau gardé en mémoire et relu environ toutes les 24 heures) : <https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview>, <https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/non-di-scenarios>, <https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management>
