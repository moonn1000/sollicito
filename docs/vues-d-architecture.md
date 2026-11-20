# Vues d'architecture de la V1

- **Mis à jour le** : 2026-11-20, avec #7
- **Décisions** : [ADR-0001](adr/0001-cadrage-architecture-v1.md) (D1 à D4, D6 à D10, D12), en partie remplacé par l'[ADR-0003](adr/0003-hote-interface-webview2.md) (hôte de l'interface) et l'[ADR-0004](adr/0004-v1-resserree-paliers.md) (V1 resserrée, livrée en paliers) ; [ADR-0006](adr/0006-requetes-et-liens-de-la-fenetre.md) (requêtes et liens de la fenêtre)
- **Périmètre** : la V1 que fixe l'ADR-0004, en deux vues du modèle C4 : le contexte (niveau 1) et les conteneurs (niveau 2). La vue des composants (niveau 3) et les diagrammes de séquence s'y ajouteront au palier 5 (#59). Ce que l'utilisateur fait dans Sollicito : [cas d'utilisation](cas-d-utilisation.md).

Le modèle C4 ne prescrit pas de notation ; ces vues sont écrites en Mermaid, et chacune a sa légende. Les paliers sont ceux de l'ADR-0004 : P1, le squelette tourne ; P2, je suis mes candidatures ; P3, je relance et j'importe ; P4, mon IA m'aide ; P5, prêt à télécharger. L'avancement se suit dans les [jalons](https://github.com/moonn1000/sollicito/milestones).

## Vue de contexte

Où se place Sollicito : qui s'en sert, avec quels systèmes il échange, et ce qui quitte le poste.

```mermaid
flowchart TB
    subgraph poste["Poste de l'utilisateur, sous Windows"]
        personne["<b>Personne en recherche d'emploi</b><br>[Personne]<br>Mène sa recherche d'emploi en Belgique"]
        sollicito["<b>Sollicito</b><br>[Système logiciel]<br>Candidatures, relances, offres, IA facultative :<br>un exe d'un seul fichier, ses données sur le poste<br>P1 à P5"]
        runtime["<b>Runtime WebView2</b><br>[Système extérieur : composant de Windows]<br>Affiche l'interface de Sollicito<br>P1"]
        navigateur["<b>Navigateur par défaut</b><br>[Système extérieur]<br>Ouvre un lien externe cliqué par l'utilisateur<br>P1"]
        messagerie["<b>Messagerie de l'utilisateur</b><br>[Système extérieur]<br>Reçoit le brouillon d'une relance,<br>que l'utilisateur envoie lui-même<br>P3"]
        ollama["<b>Ollama</b><br>[Système extérieur, facultatif]<br>Modèle d'IA local<br>P4"]
    end

    ia["<b>IA en ligne</b><br>[Système extérieur, facultatif]<br>Claude (Anthropic) ou OpenAI,<br>par la clé de l'utilisateur<br>P4"]
    microsoft["<b>Microsoft</b><br>[Système extérieur]<br>Reçoit les données de diagnostic du runtime,<br>lui livre ses mises à jour<br>P1"]
    forem["<b>Le Forem</b><br>[Système extérieur, V2]<br>Offres d'emploi en données ouvertes (ODWB)<br>V2"]

    personne -- "suit ses candidatures, ses relances et ses offres" --> sollicito
    personne -- "relit et envoie le brouillon" --> messagerie
    sollicito -- "affiche son interface par" --> runtime
    sollicito -- "ouvre un lien cliqué (http, https)" --> navigateur
    sollicito -- "ouvre le brouillon d'une relance (mailto), P3" --> messagerie
    sollicito -- "confie un texte à analyser, P4" --> ollama
    sollicito -- "① confie le texte d'une offre ou d'une relance (HTTPS), P4" --> ia
    runtime -- "② données de diagnostic" --> microsoft
    runtime -- "③ télécharge ses mises à jour" --> microsoft
    sollicito -. "offres ouvertes, V2" .-> forem

    classDef personne fill:#08427b,stroke:#052e56,color:#ffffff
    classDef systeme fill:#1168bd,stroke:#0b4884,color:#ffffff
    classDef exterieur fill:#6b6b6b,stroke:#4d4d4d,color:#ffffff
    classDef v2 fill:#ffffff,stroke:#6b6b6b,color:#3d3d3d,stroke-dasharray:6 4
    class personne personne
    class sollicito systeme
    class runtime,navigateur,messagerie,ollama,ia,microsoft exterieur
    class forem v2
    style poste fill:none,stroke:#888888,stroke-dasharray:6 4
```

| Légende | Sens |
| --- | --- |
| Boîte bleu foncé | la personne qui se sert de Sollicito |
| Boîte bleue | Sollicito, le système décrit |
| Boîte grise | un système extérieur : Sollicito s'en sert, mais ne le contient pas |
| Boîte blanche à bord pointillé | un système de la V2 |
| Cadre pointillé | la limite du poste de l'utilisateur |
| Trait plein, trait pointillé | un échange de la V1, un échange de la V2 |
| ①, ②, ③ | les trois échanges qui quittent le poste |
| P1 à P5 | le palier qui amène un élément ; sur un trait, celui qui amène l'échange, s'il vient après le palier 1 |

### Ce qui quitte le poste

Sollicito n'a pas de serveur distant. Trois échanges seulement franchissent la limite du poste, ceux que décrivent l'[ADR-0004](adr/0004-v1-resserree-paliers.md) (« Ce qui quitte la machine ») et le [README](../README.md) (« Ce qui quitte votre machine ») :

1. ce que l'utilisateur confie à l'IA en ligne, Claude ou OpenAI, s'il en configure une (palier 4) ; un modèle Ollama, qui tourne sur le poste, ne fait rien sortir ;
2. les données de diagnostic que le runtime WebView2 envoie à Microsoft ;
3. les mises à jour du runtime, qu'il télécharge seul.

Les deux derniers viennent du runtime, un composant de Windows, et non du code de Sollicito. Le Forem, en pointillé, n'arrive qu'en V2 (ADR-0004, « Ce qui attend la V2 »).

Le navigateur et la messagerie reçoivent sur le poste un lien ou un brouillon. Ce qu'ils en font ensuite ne vient pas de Sollicito : c'est l'utilisateur qui suit le lien ou envoie le message (ADR-0001, D8 ; ADR-0006).

Hors du diagramme :

- **GitHub Releases**, d'où l'on télécharge l'exe : Sollicito ne le contacte pas, puisqu'il n'a pas de mise à jour automatique (ADR-0004, D13).
- **En V2**, d'autres systèmes s'ajouteront : l'API du VDAB, la page d'une offre importée par son adresse, et Gmail ou Outlook, où déposer un brouillon sans l'envoyer, après son propre ADR (ADR-0004, « Ce qui attend la V2 »).

## Vue de conteneurs

De quoi Sollicito est fait. Tout tient dans un seul exe : la fenêtre hôte construit et démarre l'API locale dans son propre processus, et l'interface s'exécute dans les processus du runtime WebView2 (ADR-0003, « Montage »). Fermer la fenêtre arrête l'API.

```mermaid
---
config:
  flowchart:
    nodeSpacing: 30
    rankSpacing: 45
---
flowchart TB
    subgraph poste["Poste de l'utilisateur, sous Windows"]
        personne["<b>Personne en recherche d'emploi</b><br>[Personne]"]
        subgraph sollicito["Sollicito"]
            interface["<b>Interface</b><br>[Conteneur : React, TypeScript]<br>Exécutée dans le runtime WebView2<br>P1"]
            subgraph processus["Processus de l'exe"]
                fenetre["<b>Fenêtre hôte</b><br>[Conteneur : C#, WinForms, WebView2]<br>P1"]
                api["<b>API locale</b><br>[Conteneur : C#, ASP.NET Core, EF Core]<br>N'écoute que sur 127.0.0.1<br>P1"]
            end
            subgraph donnees["Dossier de données<br>%LOCALAPPDATA%\Sollicito"]
                base[("<b>Base SQLite</b><br>[Conteneur : SQLite]<br>P2")]
                cles[("<b>Clés d'IA chiffrées</b><br>[Fichiers]<br>P4")]
                profil[("<b>Profil WebView2</b><br>[Fichiers du runtime]<br>P1")]
            end
            sauvegardes[("<b>Sauvegardes</b><br>[Fichiers, dossier réglable]<br>P2")]
        end
        runtime["<b>Runtime WebView2</b><br>[Système extérieur]<br>P1"]
        navigateur["<b>Navigateur par défaut</b><br>[Système extérieur]<br>P1"]
        messagerie["<b>Messagerie de l'utilisateur</b><br>[Système extérieur]<br>P3"]
        ollama["<b>Ollama</b><br>[Système extérieur, facultatif]<br>P4"]
    end

    ia["<b>IA en ligne</b><br>[Système extérieur, facultatif]<br>Claude (Anthropic) ou OpenAI<br>P4"]
    microsoft["<b>Microsoft</b><br>[Système extérieur]<br>P1"]
    forem["<b>Le Forem</b><br>[Système extérieur, V2]<br>V2"]

    personne -- "se sert des écrans" --> interface
    fenetre -- "la démarre" --> api
    fenetre -- "affiche, remet le jeton" --> interface
    interface -- "HTTP, 127.0.0.1, jeton" --> api
    api -- "sert ses fichiers" --> interface
    fenetre -- "crée, filtre les requêtes" --> runtime
    runtime -- "l'exécute" --> interface
    api -- "lit, écrit" --> base
    api -- "chiffre, P4" --> cles
    api -- "sauvegarde, P2" --> sauvegardes
    fenetre -- "lien cliqué" --> navigateur
    fenetre -- "brouillon (mailto), P3" --> messagerie
    api -- "texte à analyser, P4" --> ollama
    api -- "① HTTPS, P4" --> ia
    runtime -- "② diagnostic, ③ mises à jour" --> microsoft
    api -. "ingestion en tâche de fond, V2" .-> forem
    base ~~~ cles ~~~ profil
    navigateur ~~~ messagerie

    classDef personne fill:#08427b,stroke:#052e56,color:#ffffff
    classDef conteneur fill:#1168bd,stroke:#0b4884,color:#ffffff
    classDef exterieur fill:#6b6b6b,stroke:#4d4d4d,color:#ffffff
    classDef v2 fill:#ffffff,stroke:#6b6b6b,color:#3d3d3d,stroke-dasharray:6 4
    class personne personne
    class fenetre,api,interface,base,profil,cles,sauvegardes conteneur
    class runtime,navigateur,messagerie,ollama,ia,microsoft exterieur
    class forem v2
    style poste fill:none,stroke:#888888,stroke-dasharray:6 4
    style sollicito fill:none,stroke:#1168bd,stroke-dasharray:6 4
    style processus fill:none,stroke:#888888,stroke-dasharray:2 3
    style donnees fill:none,stroke:#888888,stroke-dasharray:2 3
```

| Légende | Sens |
| --- | --- |
| Boîte bleu foncé | la personne qui se sert de Sollicito |
| Boîte bleue | un conteneur de Sollicito ; en cylindre, des données |
| Boîte grise | un système extérieur |
| Boîte blanche à bord pointillé | un système de la V2 |
| Cadre pointillé gris | la limite du poste |
| Cadre pointillé bleu | la limite de Sollicito |
| Cadre en pointillé fin | un processus, ou un dossier |
| Trait plein, trait pointillé | un échange de la V1, un échange de la V2 |
| ①, ②, ③ | les trois échanges qui quittent le poste (voir la vue de contexte) |
| P1 à P5 | le palier qui amène un élément ; sur un trait, celui qui amène l'échange, s'il vient après le palier 1 |

### Les conteneurs

| Conteneur | Technologie | Responsabilités | Dans le dépôt | Palier |
| --- | --- | --- | --- | --- |
| Fenêtre hôte | C#, .NET 10, WinForms, contrôle WebView2 | construire et démarrer l'API, l'arrêter à la fermeture ; créer l'environnement du runtime et son profil ; remettre le jeton à l'interface ; annuler les navigations et retenir les requêtes hors de l'origine de l'interface ; ouvrir au-dehors un lien cliqué et, au palier 3, le brouillon d'une relance | `src/Sollicito.Fenetre` | P1 (#13) |
| API locale | C#, ASP.NET Core, Kestrel, EF Core | écouter sur 127.0.0.1 seulement, sur un port choisi au lancement ; exiger le jeton, contrôler les en-têtes `Host` et `Origin` ; servir les fichiers de l'interface ; porter les règles des candidatures, puis des relances, des offres et de l'IA ; lire et écrire la base, la sauvegarder | `src/Sollicito.Api`, avec `src/Sollicito.Application`, `src/Sollicito.Domain` et `src/Sollicito.Infrastructure` | P1 (#10) |
| Interface | React, TypeScript, construite par Vite | les écrans ; garder le jeton en mémoire et l'envoyer à chaque appel ; afficher tout texte échappé | `interface/` | P1 (#8, #13) |
| Base SQLite | SQLite, schéma par migrations EF Core | les données de l'utilisateur : six entités en base au terme de la V1 ([modèle de domaine](modele-de-domaine.md)) | migrations : `src/Sollicito.Infrastructure/Persistance/Migrations` | P2 (#11) |

L'API se découpe en couches (Clean Architecture) et en modules : Candidatures, Offres et connecteurs, Relances, IA (ADR-0004, D4). Leur détail est l'affaire de la vue des composants (#59).

### Les données

Les données vivent sous le profil de l'utilisateur, dans le dossier de données `%LOCALAPPDATA%\Sollicito`, hors du dossier de l'exe et hors du dépôt (ADR-0001, D10) :

- la base, `sollicito.db`, que l'API crée ou met à jour au lancement, avant d'écouter ;
- le profil du runtime WebView2, `WebView2\`, que la fenêtre fixe à la création de l'environnement ;
- au palier 4, les clés d'IA, chiffrées par Data Protection et DPAPI, rangées hors de la base : ni les sauvegardes ni les exports ne les emportent.

Les sauvegardes (palier 2) font exception : leur dossier se règle, et l'utilisateur peut le placer sur un autre support, puisqu'une copie sur le même disque ne protège pas de sa panne (ADR-0001, D10). Supprimer l'exe laisse toutes ces données en place (ADR-0004, « Conséquences »).

### Les échanges

- **La fenêtre et l'interface** : la page signale qu'elle est prête, la fenêtre vérifie la source du message et lui remet le jeton, qui ne vit qu'en mémoire ([modèle de menaces](modele-de-menaces.md), « Canal du jeton »).
- **L'interface et l'API** : l'API sert l'interface et ses routes à la même origine, `http://127.0.0.1:<port>`. Elle n'écoute sur aucune interface réseau, et refuse tout appel qui ne vient pas de l'interface : jeton, `Host`, `Origin` ([modèle de menaces](modele-de-menaces.md), « Qui peut appeler l'API » et « Protections de l'API locale »).
- **Les sorties de la fenêtre** : la fenêtre annule toute navigation et retient toute requête de la page vers une autre origine (ADR-0006). Seule la fenêtre ouvre au-dehors, par le shell de Windows, un lien `http` ou `https` cliqué par l'utilisateur (ADR-0006) et, au palier 3, le brouillon d'une relance, construit à partir de champs validés par l'API (ADR-0003, « Brouillon de relance »). Le détail et les limites : [modèle de menaces](modele-de-menaces.md), « Protections de la fenêtre » et « Limites ».
- **L'IA** (palier 4) : c'est l'API, pas la page, qui appelle le fournisseur : chaque clé va à l'adresse officielle de son fournisseur, fixée dans le code, en HTTPS ; l'adresse d'Ollama se règle (ADR-0001, D7).

### Pas de conteneur d'ingestion

La vue n'a aucun conteneur d'ingestion des offres. L'ingestion en tâche de fond attend la V2, avec le Forem (ADR-0004, D4) : seul le trait pointillé « API locale → Le Forem » la représente. Une tâche de fond tournerait dans le processus de l'API, sans conteneur à elle (ADR-0001, D4). La sauvegarde automatique (#26) est une responsabilité de l'API ; son déclenchement relève de #26.
