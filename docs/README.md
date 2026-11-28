# Documentation d'architecture

- **Mis à jour le** : 2026-11-27, avec #7, puis avec #49 ; 2026-11-28, avec #58, puis avec #59, puis avec #60
- **Décisions** : [ADR-0001](adr/0001-cadrage-architecture-v1.md), D12 (la documentation vit avec le code) ; [ADR-0004](adr/0004-v1-resserree-paliers.md) (la V1 documentée en entier en UML)
- **Périmètre** : les pages de `docs/`

L'architecture de la V1 de Sollicito, telle que la fixe l'ADR-0004 : ce qui est décidé et pourquoi, de quoi Sollicito est fait, ce que l'utilisateur y fait, son modèle de domaine et ses protections. Elle vit avec le code (ADR-0001, D12) : une pull request qui change ce qu'une page décrit la met à jour, comme le veut la définition de fini de la V1 (#5). Les diagrammes sont écrits en Mermaid, que GitHub affiche.

| Page | Ce qu'elle montre | Diagrammes |
| --- | --- | --- |
| [Démarrer](demarrer.md) | télécharger Sollicito, le vérifier, le lancer, puis le retirer | aucun : un guide pas à pas |
| [Vues d'architecture](vues-d-architecture.md) | où se place Sollicito, ce qui quitte le poste, de quoi il est fait | vue de contexte, vue de conteneurs et vue des composants (C4, niveaux 1 à 3) |
| [Diagrammes de séquence](diagrammes-de-sequence.md) | comment les parties s'enchaînent sur quatre parcours : la remise du jeton, une candidature enregistrée, un import CSV, un brouillon de relance | diagrammes de séquence (UML) |
| [Cas d'utilisation](cas-d-utilisation.md) | ce que l'utilisateur fait dans Sollicito, palier par palier | diagramme de cas d'utilisation (UML) |
| [Modèle de domaine](modele-de-domaine.md) | les entités, leurs liens et leurs règles | diagramme de classes, diagrammes d'états des candidatures et des relances (UML) |
| [Modèle de menaces](modele-de-menaces.md) | les menaces de toute la V1, domaine par domaine : chaque parade, sa limite et le test qui la prouve | synthèse par domaine, tableaux des menaces et des parades |
| [Format de l'import CSV](import-csv.md) | le fichier CSV d'offres que lit l'import : ses colonnes, son encodage, ses bornes et ses doublons | tableau des colonnes |
| [Format de l'export](export.md) | ce que contient l'export de toutes les données, en JSON et en CSV, champ par champ | tableaux des champs |
| [Décisions d'architecture](adr/README.md) | chaque décision, ses raisons, ses alternatives écartées | index des ADR |

Pour une première lecture : les vues d'architecture, puis les cas d'utilisation, puis le modèle de domaine ; le modèle de menaces et les ADR pour le détail. Le [README du dépôt](../README.md) présente Sollicito et dit comment le construire et le tester.
