# Documentation d'architecture

- **Mis à jour le** : 2026-11-27, avec #7, puis avec #49
- **Décisions** : [ADR-0001](adr/0001-cadrage-architecture-v1.md), D12 (la documentation vit avec le code) ; [ADR-0004](adr/0004-v1-resserree-paliers.md) (la V1 documentée en entier en UML)
- **Périmètre** : les pages de `docs/`, et ce qui viendra au palier 5

L'architecture de la V1 de Sollicito, telle que la fixe l'ADR-0004 : ce qui est décidé et pourquoi, de quoi Sollicito est fait, ce que l'utilisateur y fait, son modèle de domaine et ses protections. Elle vit avec le code (ADR-0001, D12) : une pull request qui change ce qu'une page décrit la met à jour, comme le veut la définition de fini de la V1 (#5). Les diagrammes sont écrits en Mermaid, que GitHub affiche.

| Page | Ce qu'elle montre | Diagrammes |
| --- | --- | --- |
| [Vues d'architecture](vues-d-architecture.md) | où se place Sollicito, ce qui quitte le poste, de quoi il est fait | vue de contexte et vue de conteneurs (C4, niveaux 1 et 2) |
| [Cas d'utilisation](cas-d-utilisation.md) | ce que l'utilisateur fait dans Sollicito, palier par palier | diagramme de cas d'utilisation (UML) |
| [Modèle de domaine](modele-de-domaine.md) | les entités, leurs liens et leurs règles | diagramme de classes, diagrammes d'états des candidatures et des relances (UML) |
| [Modèle de menaces](modele-de-menaces.md) | les protections de l'API locale et de la fenêtre, et leurs limites | tableaux des menaces et des parades |
| [Format de l'export](export.md) | ce que contient l'export de toutes les données, en JSON et en CSV, champ par champ | tableaux des champs |
| [Décisions d'architecture](adr/README.md) | chaque décision, ses raisons, ses alternatives écartées | index des ADR |

Pour une première lecture : les vues d'architecture, puis les cas d'utilisation, puis le modèle de domaine ; le modèle de menaces et les ADR pour le détail. Le [README du dépôt](../README.md) présente Sollicito et dit comment le construire et le tester.

## À venir

Au palier 5, d'après l'ADR-0004 :

- les diagrammes de séquence et la vue des composants (C4, niveau 3) : [#59](https://github.com/moonn1000/sollicito/issues/59) ;
- le modèle de menaces complet : [#58](https://github.com/moonn1000/sollicito/issues/58) ;
- le guide « Démarrer » : télécharger, vérifier et lancer l'exe : [#60](https://github.com/moonn1000/sollicito/issues/60).
