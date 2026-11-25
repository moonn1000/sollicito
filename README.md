# Sollicito

> **État (novembre 2026) : en construction.** La solution .NET 10, l'interface React, l'intégration continue et l'API locale sont en place. La V1 se construit en cinq paliers jusqu'au 1er décembre 2026 ; aucune version n'est encore publiée. Ce qui suit décrit la V1 telle qu'elle sera livrée, et l'avancement se suit dans les [jalons](https://github.com/moonn1000/sollicito/milestones).

Sollicito est une application de bureau libre pour mener une recherche d'emploi en Belgique : suivre ses candidatures, ne plus oublier une relance, importer des offres, avec une IA en option. Les données sont stockées sur l'ordinateur de l'utilisateur, et Sollicito n'a aucun serveur distant.

En Belgique, « solliciter » veut dire postuler. D'où le nom.

## Pourquoi

Une recherche d'emploi, ce sont des dizaines de candidatures, autant de dates à retenir et des relances qui se perdent. Beaucoup d'outils de suivi sont des services en ligne, payants, pensés pour d'autres marchés, et demandent de confier à un tiers des données sensibles : CV, prétentions salariales, refus. Sollicito prend le chemin inverse : une application locale, centrée sur le marché belge, au code ouvert.

## Ce que fait la V1

Un exe Windows d'un seul fichier, publié sur la page des versions : vous le téléchargez et le lancez, sans rien installer. Il lui faut le runtime WebView2 de Microsoft, livré avec Windows 11 et déjà présent sur la plupart des Windows 10. L'exe n'est pas signé : SmartScreen affiche un avertissement pour chaque nouvelle version, tant qu'elle n'a pas acquis de réputation, et Smart App Control peut le bloquer là où il est actif. La base se crée seule, et un premier écran propose de connecter une IA, facultative.

- **Candidatures** : entreprise, poste, date et heure d'envoi, statut, dans un tableau de bord. Vous faites avancer le statut, notez un entretien ou un échange, relisez l'historique daté ; vous corrigez ou supprimez une candidature.
- **Relances** : une relance vous est proposée à son échéance et ouvre un brouillon dans votre messagerie, adressé au contact de l'entreprise s'il y en a un. C'est vous qui l'envoyez.
- **Offres** : vous collez le texte d'une offre ou importez un fichier CSV, puis faites d'une offre une candidature.
- **IA en option** : votre clé Claude ou OpenAI, chiffrée, ou un modèle local Ollama. L'IA remplit la fiche d'une offre collée et rédige un brouillon de relance. Sans IA, tout le reste fonctionne.
- **Données** : sauvegarde automatique (les sept dernières, plus une avant chaque migration de la base), restauration, et export complet en JSON et en CSV.

Si le calendrier glisse, cinq de ces fonctions peuvent passer en V2, dans un ordre fixé d'avance : l'export CSV, l'import CSV, OpenAI, les notes d'entretien, puis le brouillon de relance par l'IA. Ces choix sont consignés dans l'[ADR-0004](docs/adr/0004-v1-resserree-paliers.md).

## Ce qui attend la V2

- Les offres du Forem et du VDAB, et l'import d'une offre par son adresse, une fois réglées la licence des données et la collecte.
- Le score de correspondance entre une offre et son profil, l'aide à la rédaction des CV et des lettres, la construction du profil et les documents joints.
- D'autres fournisseurs d'IA, dont Mistral.
- La recherche et les filtres, la notification Windows d'une relance échue, le rechargement d'un export et l'import d'un suivi tenu dans career-ops, un autre outil de recherche d'emploi.
- Un installeur, l'étude du chiffrement des sauvegardes, une fenêtre qui ignore les réglages extérieurs capables de l'ouvrir au débogage, le détail des réglages de Windows qui limitent ce qui quitte la machine, et les guides complets.
- Le dépôt des brouillons de relance dans Gmail ou Outlook, jamais envoyés, après son propre ADR.

## Garde-fous

Sollicito ne fera jamais :

- **envoyer quoi que ce soit à votre place** : aucune candidature ni aucun e-mail ne part sans votre validation explicite ;
- **collecter automatiquement des offres sur un site qui ne le permet pas** : seules sont intégrées des sources dont les conditions autorisent la réutilisation ;
- **héberger vos données** : Sollicito n'a pas de serveur distant. Ce qui quitte votre machine est décrit ci-dessous ;
- **embarquer des données réelles dans ce dépôt** : les jeux d'essai seront fictifs.

## Ce qui quitte votre machine

Les échanges prévus en V1 :

- **l'IA en ligne, si vous en configurez une** : seulement quand vous le demandez, ce que vous confiez à l'IA, le texte d'une offre ou les éléments d'une relance, part chez Anthropic (Claude) ou chez OpenAI, avec votre clé et les consignes de Sollicito ; ce fournisseur le traite selon ses propres conditions. « Tester la clé » n'envoie qu'une courte question fixe, sans aucune de vos données. Un modèle Ollama qui tourne sur votre ordinateur ne fait rien sortir ;
- **vos sauvegardes, si vous les rangez dans un dossier en réseau ou synchronisé** (un NAS, OneDrive…) : chaque copie de votre base, non chiffrée, part là où ce dossier l'envoie ;
- **le brouillon d'une relance, si votre messagerie est en ligne ou range vos brouillons sur un serveur** : son destinataire, son objet et son texte partent chez votre fournisseur de messagerie dès l'ouverture du brouillon ou à son enregistrement, même si vous ne l'envoyez pas ;
- **la télémétrie du runtime WebView2**, le composant de Windows qui affiche l'interface dans la fenêtre de Sollicito : il envoie à Microsoft des données de diagnostic, les facultatives seulement si vous les autorisez dans le réglage « Données de diagnostic » de Windows, les obligatoires quel que soit ce réglage, et par défaut un rapport à chaque plantage de l'un de ses processus ;
- **les mises à jour de ce runtime**, qu'il télécharge seul auprès de Microsoft.

Ces deux derniers échanges ne viennent pas du code de Sollicito : le runtime, partagé par les applications de Windows qui s'en servent, les fait aussi pour elles. Le détail est dans l'[ADR-0004](docs/adr/0004-v1-resserree-paliers.md).

## Architecture en bref

- Application de bureau Windows, mono-utilisateur, livrée en un exe autonome d'un seul fichier.
- Web API ASP.NET Core (C#) qui n'écoute que sur la machine (127.0.0.1), interface React affichée par WebView2 dans une fenêtre WinForms.
- Base SQLite, accédée par Entity Framework Core.
- Monolithe modulaire en Clean Architecture : Candidatures, Offres et connecteurs, Relances, IA ; le module Profil arrive en V2.
- Des connecteurs d'offres branchés derrière une interface unique (ports et adaptateurs) : ajouter une source ne touche pas au cœur de l'application. La V1 en a deux : le texte collé et le fichier CSV.

L'architecture de la V1 est documentée en UML dans [docs/](docs/README.md), au fil des paliers, avec son modèle de menaces. Les décisions et leurs raisons : [docs/adr](docs/adr/README.md).

## Construire et tester

Il faut le SDK .NET 10.0.404 et pnpm 11.22.0. pnpm installe lui-même, dans le projet, la version de Node.js qu'attend l'interface.

- Solution .NET : `dotnet test --solution Sollicito.slnx` construit et teste.
- Interface : `pnpm --dir interface run check` installe les dépendances, construit et teste.

L'intégration continue construit et teste ces deux parties de la même façon, à chaque pull request vers `main`, puis après chaque fusion.

## Sécurité

Les protections prévues dès la conception, détaillées dans l'[ADR-0001](docs/adr/0001-cadrage-architecture-v1.md) (D9 et D10) :

- l'API locale refuse les appels venus d'autres pages web, d'autres appareils ou d'autres comptes du PC (voir le [modèle de menaces](docs/modele-de-menaces.md)) : écoute sur 127.0.0.1 seulement, jeton aléatoire à chaque lancement transmis par un en-tête, refus par le serveur des origines et des en-têtes `Host` inattendus ; ses journaux ne contiennent ni le jeton ni vos données ;
- la fenêtre n'affiche que l'interface de Sollicito, sous une politique de sécurité du contenu stricte ; les liens externes s'ouvrent dans votre navigateur ;
- les clés d'API d'IA sont chiffrées par l'API Data Protection d'ASP.NET Core, protégée par DPAPI et liée à votre compte Windows ; jamais stockées en clair, jamais dans les sauvegardes, et l'export ne les emportera pas ; chaque clé ne part que vers l'adresse officielle de son fournisseur, en HTTPS ;
- le contenu des offres sera traité comme non fiable : affichage échappé, et une IA qui ne peut rien déclencher d'elle-même ;
- aucun secret dans ce dépôt.

Le [modèle de menaces](docs/modele-de-menaces.md) détaille les protections en place et leurs limites. Pour signaler une vulnérabilité : [SECURITY.md](SECURITY.md).

## Méthode

Scrum, en enchaînement continu, par paliers : une story part dès qu'une autre est fusionnée, deux au plus en cours. Chaque user story est une issue GitHub, traitée dans sa propre branche et fusionnée par pull request après passage des tests. Le backlog est ordonné dans [GitHub Projects](https://github.com/users/moonn1000/projects/3).

La V1 se livre en cinq paliers, chacun clos par une revue et une rétrospective écrites dans son issue :

1. Le squelette tourne (cible : 23 novembre) ;
2. Je suis mes candidatures (25 novembre) ;
3. Je relance et j'importe (27 novembre) ;
4. Mon IA m'aide (29 novembre) ;
5. Prêt à télécharger (1er décembre).

## Licence

[AGPL-3.0](LICENSE). Vous pouvez utiliser, étudier, modifier et redistribuer Sollicito. Si vous en distribuez une copie, modifiée ou non, elle reste sous la même licence et vous fournissez son code source à ceux qui la reçoivent. Si vous modifiez Sollicito et que d'autres utilisent votre version à distance, par le réseau, vous devez leur offrir son code source (article 13).
