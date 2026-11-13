# Sollicito

> **État : en conception.** Aucune ligne de code n'est encore écrite. Ce dépôt contient le cadrage et les décisions d'architecture ; l'implémentation commence au premier sprint. Tout ce qui suit décrit ce que l'application **fera**.

Sollicito sera une application de bureau libre pour mener une recherche d'emploi en Belgique : suivre ses candidatures, ne plus oublier une relance, importer des offres, avec une IA en option. Les données seront stockées sur l'ordinateur de l'utilisateur, et nulle part ailleurs.

En Belgique, « solliciter » veut dire postuler. D'où le nom.

## Pourquoi

Une recherche d'emploi, ce sont des dizaines de candidatures, autant de dates à retenir et des relances qui se perdent. Beaucoup d'outils de suivi sont des services en ligne, payants, pensés pour d'autres marchés, et demandent de confier à un tiers des données sensibles : CV, prétentions salariales, refus. Sollicito prend le chemin inverse : une application locale, centrée sur le marché belge, au code ouvert.

## Ce que fera la V1

- **Tableau de bord des candidatures** : entreprise, poste, dates et heures, statut, historique des étapes (envoi, entretien, réponse).
- **Relances** : échéance calculée, rappel, brouillon ouvert dans sa propre messagerie, que l'on envoie soi-même.
- **Import d'offres** : depuis les données ouvertes du Forem, en collant le texte ou l'adresse d'une offre, ou en important un fichier CSV.
- **IA en option** : avec sa propre clé d'API ou un modèle local, extraction des informations d'une offre collée et rédaction d'un brouillon de relance. Sans IA, tout le reste fonctionne.
- **Sauvegardes automatiques** et export complet des données, rechargeable par import.

Prévu en V2 : l'API du VDAB comme nouvelle source, un score de correspondance entre une offre et son profil, l'aide à la rédaction des CV et des lettres, la construction du profil, et le dépôt des brouillons de relance dans Gmail ou Outlook, jamais envoyés.

## Garde-fous

Sollicito ne fera jamais :

- **envoyer quoi que ce soit à votre place** : aucune candidature ni aucun e-mail ne part sans votre validation explicite ;
- **collecter automatiquement des offres sur un site qui ne le permet pas** : seules sont intégrées des sources dont les conditions autorisent la réutilisation ;
- **héberger vos données** : Sollicito n'a pas de serveur distant. Ne quittent votre machine que les échanges des fonctions que vous activez. En V1 : la récupération des offres auprès des sources choisies, y compris en tâche de fond, et, si vous en configurez une, l'IA en ligne de votre choix ;
- **embarquer des données réelles dans ce dépôt** : les jeux d'essai seront fictifs.

## Architecture en bref

- Application de bureau Windows, mono-utilisateur.
- Web API ASP.NET Core (C#) qui n'écoute que sur la machine (127.0.0.1), interface React affichée par WebView2 dans une fenêtre WinForms.
- Base SQLite, accédée par Entity Framework Core.
- Monolithe modulaire en Clean Architecture : Candidatures, Offres et connecteurs, Relances, Profil, IA.
- Des connecteurs d'offres branchés derrière une interface unique (ports et adaptateurs) : ajouter une source ne touche pas au cœur de l'application.

Les décisions et leurs raisons : [docs/adr](docs/adr/README.md).

## Sécurité

Les protections prévues dès la conception, détaillées dans l'[ADR-0001](docs/adr/0001-cadrage-architecture-v1.md) (D9 et D10) :

- l'API locale refusera les appels venus d'autres pages web ou d'autres comptes du PC : écoute sur 127.0.0.1, jeton aléatoire à chaque lancement transmis par un en-tête, refus par le serveur des origines et des en-têtes `Host` inattendus ;
- la fenêtre n'affichera que l'interface de Sollicito, sous une politique de sécurité du contenu stricte ; les liens externes s'ouvriront dans votre navigateur ;
- les clés d'API d'IA seront chiffrées par l'API Data Protection d'ASP.NET Core, protégée par DPAPI et liée à votre compte Windows ; jamais stockées en clair, jamais dans les sauvegardes ni les exports ;
- le contenu des offres sera traité comme non fiable : affichage échappé, et une IA qui ne peut rien déclencher d'elle-même ;
- aucun secret dans ce dépôt.

Pour signaler une vulnérabilité : [SECURITY.md](SECURITY.md).

## Méthode

Scrum, en sprints de deux semaines. Chaque user story est une issue GitHub, traitée dans sa propre branche et fusionnée par pull request après passage des tests. Le backlog sera tenu dans GitHub Projects.

## Licence

[AGPL-3.0](LICENSE). Vous pouvez utiliser, étudier, modifier et redistribuer Sollicito. Si vous en distribuez une copie, modifiée ou non, elle reste sous la même licence et vous fournissez son code source à ceux qui la reçoivent. Si vous modifiez Sollicito et que d'autres utilisent votre version à distance, par le réseau, vous devez leur offrir son code source (article 13).
