# Sollicito

> **État : en conception.** Aucune ligne de code n'est encore écrite. Ce dépôt contient le cadrage et les décisions d'architecture ; l'implémentation commence au premier sprint. Tout ce qui suit décrit ce que l'application **fera**.

Sollicito sera une application de bureau libre et gratuite pour mener une recherche d'emploi en Belgique : suivre ses candidatures, ne plus oublier une relance, importer des offres, avec une IA en option. Toutes les données resteront sur l'ordinateur de l'utilisateur.

En Belgique, « solliciter » veut dire postuler. D'où le nom.

## Pourquoi

Une recherche d'emploi, ce sont des dizaines de candidatures, autant de dates à retenir et des relances qui se perdent. Beaucoup d'outils de suivi sont des services en ligne, payants, pensés pour d'autres marchés, et demandent de confier à un tiers des données sensibles : CV, prétentions salariales, refus. Sollicito prend le chemin inverse : une application locale, centrée sur le marché belge, au code ouvert.

## Ce que fera la V1

- **Tableau de bord des candidatures** : entreprise, poste, dates et heures, statut, historique des étapes (envoi, entretien, réponse).
- **Relances** : échéance calculée, rappel, brouillon prêt à envoyer depuis sa propre messagerie.
- **Import d'offres** : depuis les données ouvertes du Forem, ou en collant le texte ou l'adresse d'une offre.
- **IA en option** : avec sa propre clé d'API ou un modèle local, extraction des informations d'une offre collée et rédaction d'un brouillon de relance. Sans IA, tout le reste fonctionne.
- **Sauvegardes automatiques** et export complet des données, rechargeable par import.

Prévu ensuite : d'autres sources d'offres, un score de correspondance entre une offre et son profil, l'aide à la rédaction des CV et des lettres.

## Garde-fous

Sollicito ne fera jamais :

- **envoyer quoi que ce soit à votre place** : aucune candidature ni aucun e-mail ne part sans votre validation explicite ;
- **collecter des offres là où ce n'est pas permis** : seules sont utilisées des sources dont les conditions autorisent la réutilisation ;
- **héberger vos données** : Sollicito n'a pas de serveur. Ne quittent votre machine que les appels que vous déclenchez : la récupération des offres auprès de leur source et, si vous l'activez, l'IA de votre choix ;
- **embarquer des données réelles dans ce dépôt** : les jeux d'essai sont fictifs.

## Architecture en bref

- Application de bureau Windows, mono-utilisateur.
- Web API ASP.NET Core (C#) qui n'écoute que sur la machine (127.0.0.1), interface React affichée dans une fenêtre Photino.NET.
- Base SQLite, accédée par Entity Framework Core.
- Monolithe modulaire en Clean Architecture : Candidatures, Offres et sources, Relances, Profil, IA.
- Sources d'offres branchées derrière une interface unique (ports et adaptateurs) : ajouter une source ne touche pas au cœur de l'application.

Les décisions et leurs raisons : [docs/adr](docs/adr/README.md).

## Sécurité

Les protections prévues dès la conception, détaillées dans l'[ADR-0001](docs/adr/0001-cadrage-architecture-v1.md) :

- l'API locale refusera les appels venus d'autres sites ouverts dans le navigateur : écoute sur 127.0.0.1, jeton aléatoire à chaque lancement, contrôle des origines et de l'en-tête `Host` ;
- les clés d'API d'IA seront chiffrées par la Data Protection API de Windows, jamais stockées en clair ;
- le contenu des offres sera traité comme non fiable : affichage échappé, et une IA qui ne peut rien déclencher d'elle-même ;
- aucun secret dans ce dépôt.

Pour signaler une vulnérabilité : [SECURITY.md](SECURITY.md).

## Méthode

Scrum, en sprints de deux semaines. Chaque user story est une issue GitHub, traitée dans sa propre branche et fusionnée par pull request après passage des tests.

## Licence

[AGPL-3.0](LICENSE). Vous pouvez utiliser, étudier, modifier et redistribuer Sollicito. Une version modifiée que vous distribuez, ou que vous proposez comme service en ligne, doit être publiée sous la même licence, avec son code source.
