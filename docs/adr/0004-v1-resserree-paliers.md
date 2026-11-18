# ADR-0004 : V1 resserrée, livrée en paliers

- **Statut** : acceptée
- **Date** : 2026-11-18
- **Remplace en partie** : [ADR-0001](0001-cadrage-architecture-v1.md), D4 (modules livrés), D5 (entités en base), D6 (sources d'offres), D7 (fournisseurs d'IA), D10 (rechargement d'un export et désinstallation), D13 (distribution) et D15 (méthode) ; [ADR-0003](0003-hote-interface-webview2.md), l'installeur de « Runtime et tests » et de « Conséquences », repoussé en V2
- **Tranche** : [ADR-0003](0003-hote-interface-webview2.md), deux points de sa section « À vérifier » : le mode de publication, et la télémétrie et les mises à jour du runtime

## Contexte

L'ADR-0001 cadre une V1 complète. Le backlog publié le 2026-11-14 l'étalait sur douze sprints de deux semaines (D15), jusqu'au 30 avril 2027. La V1 doit maintenant être finie le mardi 1er décembre 2026 au soir : elle ne garde que le cœur de ce cadrage.

Elle doit aussi marcher dès le téléchargement : on lance Sollicito sans rien installer ni régler, la base se crée seule, et un premier écran propose de connecter une IA, facultative.

Le 2026-11-18, le prototype de #9 a levé le risque que la spec de la V1 désignait : publié en exe autonome d'un seul fichier, l'hôte de l'ADR-0003 se lance, ouvre sa fenêtre et lit une base SQLite (ADR-0003, « À vérifier », point .NET 10).

## Décision

L'architecture reste celle des ADR-0001 à 0003, et la V1 la documente en entier en UML. Son code n'en livre que le cœur ; le reste attend la V2.

### Ce que livre la V1

- **Candidatures** : les enregistrer et les retrouver dans un tableau de bord ; faire avancer leur statut, noter un entretien ou un échange, relire leur historique daté ; les modifier ou les supprimer.
- **Relances** : une relance proposée à son échéance, dont le brouillon s'ouvre dans la messagerie de l'utilisateur, adressé au contact de l'entreprise s'il y en a un.
- **Offres** : le texte d'une offre collé, ou un fichier CSV d'offres, puis une offre transformée en candidature.
- **IA facultative** : Claude ou OpenAI, par une clé chiffrée, ou Ollama en local. L'IA remplit la fiche d'une offre collée et rédige un brouillon de relance. Sans elle, tout le reste fonctionne.
- **Données** : une sauvegarde automatique (les sept dernières, plus une avant chaque migration), la restauration d'une sauvegarde, un export complet en JSON et en CSV.
- **Sécurité** : l'API locale et la fenêtre que fixent la D9 et l'ADR-0003.
- **Distribution** : un exe autonome d'un seul fichier, publié en version 1.0.0 (voir D13 ci-dessous).
- **Documentation** : l'architecture de la V1 en UML (classes, états, cas d'utilisation, vues C4, séquences), son modèle de menaces et un guide « Démarrer ».

Cinq de ces fonctions peuvent encore passer en V2, selon la règle de coupe ci-dessous.

### Ce qui change dans l'ADR-0001

- **D4** : la V1 livre les modules Candidatures, Offres et connecteurs, Relances et IA. Le module Profil attend la V2, comme l'ingestion des offres en tâche de fond, qui n'a pas de source sans le Forem.
- **D5** : le modèle garde ses huit entités, documentées en UML. Six entrent en base en V1 : Entreprise, Contact, Offre, Candidature, Événement de candidature, Relance. Profil et Document restent modélisés seulement, et entrent en base en V2. La V1 tient ainsi l'exigence d'au moins cinq entités en base.
- **D6** : en V1, les offres arrivent par leur texte collé ou par un fichier CSV, les deux premiers adaptateurs du connecteur commun. Le jeu de données du Forem et l'import d'une offre par son adresse passent en V2, chacun après son ADR (licence des données, collecte) ; l'API du VDAB y était déjà. La règle des sources autorisées ne change pas.
- **D7** : en V1, les fournisseurs d'IA sont Claude et OpenAI, par la clé de l'utilisateur, et Ollama, en local. Les autres, dont Mistral, attendent la V2.
- **D10** : le dossier de données, les sauvegardes, la restauration et l'export valent pour la V1. Le rechargement d'un export attend la V2, comme la désinstallation, qui arrive avec l'installeur : en V1, supprimer l'exe laisse les données en place.
- **D13** : la V1 se publie en exe autonome d'un seul fichier, sur GitHub Releases, construit par l'intégration continue avec son empreinte SHA-256 et son attestation de provenance. release-please numérote les versions. Pas de mise à jour automatique : l'utilisateur télécharge la nouvelle version depuis la page des versions. L'exe est publié sans signature (ADR-0002). L'installeur Inno Setup passe en V2 ; en V1, un guide « Démarrer » remplace le guide d'installation.
- **D15** : Scrum en enchaînement continu, par paliers. Le backlog est ordonné dans le projet GitHub ; une story part dès qu'une autre est fusionnée, deux au plus en cours. Chaque palier, un jalon GitHub, a son objectif, et se clôt par une revue et une rétrospective écrites dans son issue. Ce que l'ADR-0001 renvoyait « au premier sprint » se règle au palier 1.

### Mode de publication

L'exe est autonome : il embarque les runtimes .NET. Le poste n'a besoin que du runtime WebView2 Evergreen, livré avec Windows 11[^evergreen] et déjà installé sur la plupart des Windows 10[^distribution]. Ce choix écarte la publication dépendante du framework, et le zip à décompresser que la spec de la V1 gardait en repli.

Sans installeur en V1, la vérification du runtime par l'installeur (ADR-0003, « Runtime et tests ») et le sort du dossier de profil à la désinstallation (ADR-0003, « Conséquences ») attendent la V2.

### Ce qui quitte la machine

Sollicito n'a pas de serveur distant. En V1, qui n'a ni le Forem ni l'import par adresse, les échanges prévus qui quittent la machine sont :

- ce que l'utilisateur confie à l'IA en ligne, Claude ou OpenAI, s'il en configure une ; un modèle Ollama qui tourne sur le poste ne fait rien sortir ;
- les données de diagnostic que le runtime WebView2 envoie à Microsoft : les facultatives selon le réglage « Données de diagnostic » de Windows, les obligatoires quel que soit ce réglage, et par défaut un rapport à chaque plantage de l'un de ses processus[^donnees] ;
- les mises à jour du runtime, qu'il télécharge seul[^distribution].

Les deux derniers échanges viennent du runtime, un composant de Windows, et non du code de Sollicito, qui ne les contrôle pas, hormis les rapports de plantage (voir « À vérifier »). La vérification de réputation des navigations, qui ferait appel à SmartScreen, reste coupée (ADR-0003). Une navigation annulée peut quand même envoyer sa requête (ADR-0003, « À vérifier ») : contre une fuite vers un site extérieur, restent l'échappement du contenu et la CSP. Le README décrit ces échanges, dans sa section « Ce qui quitte votre machine ».

### Paliers

| Palier | Objectif | Cible |
| --- | --- | --- |
| 1 | Le squelette tourne : l'API locale refuse tout appel étranger, la fenêtre n'affiche que l'interface, et un exe d'un seul fichier se lance. | 23 novembre 2026 |
| 2 | Je suis mes candidatures : on les enregistre, on les fait avancer, on les annote, on les modifie ou les supprime, et la base se sauvegarde seule. | 25 novembre 2026 |
| 3 | Je relance et j'importe : relances et contacts, offres collées ou en CSV, restauration d'une sauvegarde, exe construit par la CI. | 27 novembre 2026 |
| 4 | Mon IA m'aide : clé Claude ou OpenAI chiffrée, ou modèle local Ollama ; l'IA remplit la fiche d'une offre et rédige un brouillon de relance. | 29 novembre 2026 |
| 5 | Prêt à télécharger : export, diagrammes de séquence et de composants, modèle de menaces, guide « Démarrer », version 1.0.0. | 1er décembre 2026 |

Les stories de chaque palier sont rangées dans son jalon.

### Règle de coupe

Un palier en retard de plus d'un jour fait passer en V2 la première fonction de cette liste : l'export CSV, l'import CSV, OpenAI, les notes d'entretien, le brouillon de relance par l'IA. Ne tombent jamais : la documentation UML, l'exe, la sécurité de l'API et de la fenêtre, les sauvegardes, et au moins cinq entités en base. La revue de chaque palier applique cette règle.

### Ce qui attend la V2

Entre parenthèses, la décision qui le prévoyait :

- le jeu de données du Forem et son ingestion en tâche de fond (D4, D6), l'API du VDAB, et l'import d'une offre par son adresse (D6) ;
- le module Profil (D4), avec le score de correspondance, l'aide aux CV et aux lettres (D7) et les documents joints (D5) ;
- les autres fournisseurs d'IA, dont Mistral (D7) ;
- la recherche et les filtres, la notification Windows d'une relance échue, le rechargement d'un export (D10), la passerelle depuis career-ops ;
- l'installeur (D13), qui vérifiera la présence du runtime WebView2 (ADR-0003) et gardera les données à la désinstallation (D10) ;
- l'étude du chiffrement des sauvegardes (D10), le durcissement avancé de la fenêtre (ADR-0003, « À vérifier »), le détail des réglages de Windows qui limitent ce qui quitte la machine, et les guides complets ;
- le dépôt des brouillons de relance dans Gmail ou Outlook, après son propre ADR (D8).

## Alternatives écartées

- **Garder le cadrage entier** et ses douze sprints : il finit le 30 avril 2027, cinq mois après l'échéance.
- **Garder le Forem et l'import par adresse en V1** : la licence des données du Forem et la collecte par adresse restent ouvertes (ADR-0001, « À vérifier »), et chacune demande son ADR avant le premier code.
- **Garder l'installeur en V1** : l'exe seul suffit à télécharger et lancer Sollicito ; l'installeur ajouterait sa construction, la vérification du runtime et une désinstallation qui garde les données.
- **Publier en dépendant du framework** : l'exe serait plus léger, mais l'utilisateur devrait installer les runtimes .NET Desktop et ASP.NET Core (ADR-0003, « Conséquences »).
- **Livrer un zip à décompresser** : le repli prévu si l'exe d'un seul fichier échouait. Le prototype de #9 l'a rendu inutile.

## Conséquences

- **Positives** : une V1 qu'on télécharge et qu'on lance ; une architecture documentée en entier dès la V1 ; une règle de coupe fixée d'avance, qui dit ce qui tombe si le temps manque.
- **Négatives** :
  - un exe lourd : celui du prototype pesait 140,6 Mo, ou 62,2 Mo compressé ;
  - le runtime WebView2 reste requis, et aucun installeur ne vérifie sa présence ;
  - au premier lancement, l'exe extrait ses bibliothèques natives sous `%TEMP%\.net`, où elles restent, et où tout programme du même compte Windows peut écrire : le modèle de menaces le traitera ;
  - rien ne se désinstalle : supprimer l'exe laisse les données dans `%LOCALAPPDATA%\Sollicito` (D10) ;
  - l'exe non signé déclenche l'avertissement de SmartScreen, et Smart App Control peut le bloquer là où il est actif (ADR-0002) ;
  - sans le Forem, chaque offre se colle ou s'importe à la main.

## À vérifier

Points relevés le 2026-11-18, à régler par un nouvel ADR si besoin :

- **Compression de l'exe** : 62,2 Mo au lieu de 140,6 Mo au prototype, avec une page prête en moins d'une seconde dans les deux cas (ADR-0003). À trancher avec la construction de l'exe dans l'intégration continue (#54).
- **Runtime WebView2 absent** : ce que fait l'exe s'il ne le trouve pas, puisqu'aucun installeur ne l'installe en V1.
- **Rapports de plantage du runtime** : envoyés à Microsoft par défaut ; la propriété `IsCustomCrashReportingEnabled` les garde sur le poste[^donnees].

## Sources

- Spec de la V1 : <https://github.com/moonn1000/sollicito/issues/5>
- Jalons des paliers et de la V2 : <https://github.com/moonn1000/sollicito/milestones>
- Backlog ordonné, projet GitHub n°3 : <https://github.com/users/moonn1000/projects/3>
- Prototype de #9 : `prototypes/9-fenetre-exe/`, et ses résultats dans l'ADR-0003, « À vérifier »

Consultées le 2026-11-18 :

[^evergreen]: Microsoft Learn, runtime Evergreen ou version fixe (appareils Windows 11 et Windows 10) : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/evergreen-vs-fixed-version>
[^distribution]: Microsoft Learn, distribution d'une application et du runtime WebView2 (Windows 10, mises à jour du runtime Evergreen) : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution>
[^donnees]: Microsoft Learn, données et confidentialité dans WebView2 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/data-privacy>
