# Diagrammes de séquence de la V1

- **Mis à jour le** : 2026-11-28, avec #59
- **Décisions** : [ADR-0001](adr/0001-cadrage-architecture-v1.md), D12 (diagrammes UML dans `docs/`) ; [ADR-0003](adr/0003-hote-interface-webview2.md) (hôte de l'interface, canal du jeton) ; [ADR-0004](adr/0004-v1-resserree-paliers.md) (V1 resserrée, livrée en paliers) ; [ADR-0011](adr/0011-relances-et-brouillon.md) (brouillon d'une relance) ; [ADR-0015](adr/0015-import-csv-des-offres.md) (import d'un fichier CSV) ; [ADR-0019](adr/0019-brouillon-de-relance-redige-par-l-ia.md) (brouillon rédigé par l'IA)
- **Périmètre** : quatre parcours clés de la V1, tels que le code les fait. De quoi Sollicito est fait : [vues d'architecture](vues-d-architecture.md), et sa [vue des composants](vues-d-architecture.md#vue-des-composants)

| Parcours | Palier |
| --- | --- |
| [La remise du jeton](#la-remise-du-jeton) | P1 |
| [L'enregistrement d'une candidature](#lenregistrement-dune-candidature) | P2 |
| [L'import d'offres d'un fichier CSV](#limport-doffres-dun-fichier-csv) | P3 |
| [Le brouillon d'une relance](#le-brouillon-dune-relance) | P3, P4 |

## Lire les diagrammes

Un participant porte le nom d'une classe, d'une fonction ou d'un fichier du code, ou désigne une personne, un système extérieur ou des données. Un message nomme un appel, une route ou un message entre la page et la fenêtre, ou décrit un geste, une réponse ou un effet. Seuls les refus qui changent le parcours sont dessinés, et les classes d'aide n'y sont pas. Chaque diagramme a sa légende. Les paliers sont ceux des [vues d'architecture](vues-d-architecture.md) : P1 à P5.

## La remise du jeton

La page ne reçoit le jeton que de la fenêtre, et seulement après avoir dit qu'elle est prête (ADR-0003, « Canal du jeton » ; [modèle de menaces](modele-de-menaces.md), « Canal du jeton »).

```mermaid
sequenceDiagram
    autonumber
    box rgba(107,107,107,0.2) Système extérieur, P1
        participant Vue as WebView2
    end
    box rgba(17,104,189,0.18) Interface, P1
        participant App as App<br/>App.tsx
        participant Cnx as seConnecter<br/>connexion.ts
        participant Canal as demanderLeJeton<br/>canalDeLHote.ts
        participant Client as creerClientDeLApi<br/>clientDeLApi.ts
    end
    box rgba(17,104,189,0.18) Fenêtre hôte, P1
        participant Fenetre as FenetreHote
        participant Regles as ReglesDeLaFenetre
    end
    box rgba(17,104,189,0.18) API locale, P1
        participant Api as ApiLocale
        participant Jeton as JetonDeLancement
    end

    Fenetre->>Vue: Navigate(origine de l'API)
    Vue->>Api: demande index.html et les scripts, sans jeton
    Api-->>Vue: FichiersDeLInterface sert la page et ses scripts
    Vue->>App: exécute la page : main.tsx rend App
    App->>Cnx: connecter()
    Cnx->>Canal: demanderLeJeton(canal)
    Canal->>Fenetre: message {type: "prete"}
    Fenetre->>Regles: DemandeLeJeton(source, message, origine)
    alt source de l'origine, message d'un seul champ
        Fenetre->>Jeton: Valeur()
        Jeton-->>Fenetre: le jeton du lancement
        Fenetre-->>Canal: message {type: "jeton", jeton}
        Canal-->>Cnx: le jeton
        Cnx->>Client: creerClientDeLApi(jeton)
        Client-->>App: le client, rendu par seConnecter
        App->>Client: lireEtat()
        Client->>Api: GET /api/etat, en-tête Sollicito-Jeton
        Api-->>Client: 200, l'application est prête
        Client-->>App: l'écran s'ouvre
    else autre source, ou autre forme
        Note over Fenetre: la fenêtre ne remet rien
        Canal-->>Cnx: échec après 10 secondes
        Cnx-->>App: la connexion échoue
    end
```

| Légende | Sens |
| --- | --- |
| Cadre bleuté | un conteneur de Sollicito, avec le palier du parcours |
| Cadre gris | un système extérieur |
| Flèche pleine | un appel, une requête ou un message |
| Flèche pointillée | une réponse |
| Numéro | l'ordre des messages |
| Bloc `alt` | une branche parmi plusieurs |
| Encadré posé sur des lignes verticales | une précision sur ces participants |

La fenêtre fait naviguer le contrôle WebView2, dont le runtime demande la page puis l'exécute. Le jeton naît avec l'API, à chaque lancement, et la page ne le garde qu'en mémoire.

## L'enregistrement d'une candidature

De l'écran à la base : la requête passe les contrôles d'`ApiLocale` dans leur ordre, puis les couches de l'API. Les mêmes contrôles reçoivent toutes les requêtes des parcours suivants.

```mermaid
sequenceDiagram
    autonumber
    box rgba(17,104,189,0.18) Interface, P2
        participant Ecran as SaisieDUneCandidature<br/>FormulaireDUneCandidature
        participant Client as clientDeLApi.ts
    end
    box rgba(17,104,189,0.18) API locale, P2
        participant Api as ApiLocale<br/>chaîne des contrôles
        participant Route as RoutesDesCandidatures
        participant Cas as EnregistrementDesCandidatures
        participant Domaine as Entreprise, Candidature<br/>DateDEnvoi
        participant Depot as DepotDesCandidatures
    end
    box rgba(17,104,189,0.18) Dossier de données, P2
        participant Base as Base SQLite
    end

    Ecran->>Client: enregistrerUneCandidature(saisie)
    Client->>Api: POST /api/candidatures, en-tête Sollicito-Jeton
    Api->>Api: ErreursInattendues enveloppe la suite
    Api->>Api: EnTetesDeSecurite pose les en-têtes
    Api->>Api: ControleDeLHote
    break en-tête Host inattendu
        Api-->>Client: 400
    end
    Api->>Api: FichiersDeLInterface laisse passer /api
    Api->>Api: ControleDuJeton
    break jeton absent ou faux
        Api-->>Client: 401
    end
    Api->>Api: ControleDeLOrigine
    break origine absente ou étrangère
        Api-->>Client: 403
    end
    Api->>Api: routage, UseRouting
    Api->>Api: PorteDesRequetes entre dans PorteDeLaBase (P3)
    break restauration en cours (P3)
        Api-->>Client: 503
    end
    break corps illisible ou trop grand
        Api-->>Client: 400 ou 413, par ErreursInattendues
    end
    Api->>Route: EnregistrerAsync(saisie)
    Route->>Cas: EnregistrerAsync(saisie)
    Cas->>Domaine: lit et valide le nom, le poste, la date d'envoi
    break un champ refusé
        Cas-->>Route: Refusee
        Route-->>Client: 400, les refus champ par champ
    end
    Cas->>Depot: CommencerUneEcritureAsync()
    Depot->>Base: ouvre une transaction exclusive
    Cas->>Depot: TrouverLEntrepriseAsync(clé)
    Depot->>Base: lit l'entreprise de cette clé
    opt entreprise inconnue
        Cas->>Domaine: Entreprise.Nouvelle(nom)
        Cas->>Depot: AjouterAsync(entreprise)
        Depot->>Base: écrit l'entreprise
    end
    Cas->>Domaine: Candidature.Enregistrer(…)
    Cas->>Depot: AjouterAsync(candidature)
    Depot->>Base: écrit la candidature
    Cas->>Depot: ValiderAsync()
    Depot->>Base: valide la transaction
    Cas-->>Route: Enregistree
    Route-->>Client: 201, la candidature
    Client-->>Ecran: enregistrée
```

| Légende | Sens |
| --- | --- |
| Cadre bleuté | un conteneur de Sollicito, ou son dossier de données, avec le palier du parcours |
| Flèche pleine | un appel, une requête ou un message ; vers son départ, une étape interne |
| Flèche pointillée | une réponse |
| Numéro | l'ordre des messages |
| Bloc `opt` | une étape qui n'a lieu que sous sa condition |
| Bloc `break` | un refus ou un échec : le parcours s'arrête là |

`EnregistrementDesCandidatures` écrit par le port `IDepotDesCandidatures` et son écriture, `IEcritureDesCandidatures`, que `DepotDesCandidatures` réalise sur `BaseDeSollicito`. La porte de la base est venue avec la restauration, au palier 3.

## L'import d'offres d'un fichier CSV

En V1, les offres entrent par le collage et par l'import d'un fichier CSV (#40). L'import se fait en deux requêtes, sur deux gestes : un aperçu, qui n'écrit rien, puis l'import, précédé d'une copie de la base.

```mermaid
sequenceDiagram
    autonumber
    actor Personne
    box rgba(17,104,189,0.18) Interface, P3
        participant Ecran as ImportDOffres
        participant Client as clientDeLApi.ts
    end
    box rgba(17,104,189,0.18) API locale, P3
        participant Route as ApiLocale<br/>RoutesDeLImport
        participant Coeur as ImportDesOffres
        participant Depot as DepotDesOffres
        participant Copies as SauvegardesDeLaBase
    end
    box rgba(17,104,189,0.18) Dossier de données, P3
        participant Base as Base SQLite
        participant Dossier as Sauvegardes
    end

    Personne->>Ecran: choisit un fichier CSV
    break fichier trop gros
        Ecran-->>Personne: refus, sans appel à l'API
    end
    Ecran->>Client: examinerUnFichierCsv(contenu)
    Client->>Route: POST /api/offres/import/apercu, le fichier en base64
    Route->>Route: LectureDUnFichierCsv.Lire(contenu)
    break fichier hors du format
        Route-->>Client: 400, le motif
    end
    Route->>Route: ConnecteurDuFichierCsv.RecevoirLesLignes(fichier)
    Route->>Coeur: ExaminerUnLotAsync(offres)
    Coeur->>Depot: CommencerUneEcritureAsync()
    Depot->>Base: lit les empreintes, les entreprises, les doublons
    Note over Coeur,Base: transaction jamais validée, rien n'est écrit
    Route-->>Client: 200, le sort de chaque ligne
    Client-->>Ecran: l'aperçu
    Ecran-->>Personne: affiche l'aperçu
    Personne->>Ecran: garde des doublons possibles, importe
    Ecran->>Client: importerUnFichierCsv(contenu, gardées)
    Client->>Route: POST /api/offres/import, le fichier et les lignes gardées
    Route->>Route: LectureDUnFichierCsv.Lire(contenu)
    break fichier hors du format
        Route-->>Client: 400, le motif
    end
    Route->>Route: ConnecteurDuFichierCsv.RecevoirLesLignes(fichier)
    break lignes gardées mal désignées
        Route-->>Client: 400
    end
    Route->>Copies: FaireLaCopieDAvantLImportAsync
    Copies->>Base: lit la base
    Copies->>Dossier: écrit la copie, dans le dossier par défaut
    break copie impossible
        Route-->>Client: 409, rien n'est importé
    end
    Route->>Coeur: ImporterUnLotAsync(offres, gardées)
    Coeur->>Depot: CommencerUneEcritureAsync()
    Depot->>Base: ouvre une transaction exclusive
    Coeur->>Depot: refait l'examen dans cette écriture
    Coeur->>Depot: AjouterAsync, les entreprises et les offres
    Coeur->>Depot: ValiderAsync()
    Depot->>Base: valide la transaction, la seule de l'import
    Route-->>Client: 200, le compte rendu
    Client-->>Ecran: le compte rendu
```

| Légende | Sens |
| --- | --- |
| Silhouette | la personne qui se sert de Sollicito |
| Cadre bleuté | un conteneur de Sollicito, ou son dossier de données, avec le palier du parcours |
| Flèche pleine | un appel, une requête ou un message ; vers son départ, une étape interne |
| Flèche pointillée | une réponse |
| Numéro | l'ordre des messages |
| Bloc `break` | un refus ou un échec : le parcours s'arrête là |
| Encadré posé sur des lignes verticales | une précision sur ces participants |

Les deux requêtes passent les contrôles du parcours précédent. `ImportDesOffres` écrit par les ports `IDepotDesOffres` et `IEcritureDesOffres` ; son format : [import CSV](import-csv.md).

L'import des offres du Forem, en tâche de fond, attend la V2 (ADR-0004, D4). La seule tâche de fond de la V1 est la sauvegarde automatique (`SauvegardesAutomatiques`, #26, [ADR-0008](adr/0008-sauvegardes-de-la-base.md)) : au lancement, la copie du jour si elle manque ; toutes les heures, celle d'un nouveau jour, ou un nouvel essai après un échec ; à la fermeture, la copie du jour refaite.

## Le brouillon d'une relance

La V1 a deux brouillons : le brouillon simple (P3) et celui dont le texte part du premier jet de l'IA (P4), relu avant l'ouverture. Les deux finissent de la même façon : la fenêtre ouvre le brouillon dans la messagerie, et l'utilisateur l'envoie lui-même. Il se lit en deux diagrammes, coupés à la réponse au dépôt du texte relu.

### Le premier jet de l'IA et son dépôt

```mermaid
sequenceDiagram
    autonumber
    actor Personne
    box rgba(17,104,189,0.18) Interface, P4
        participant Ecran as PremierJetParLIA
        participant Client as clientDeLApi.ts
    end
    box rgba(17,104,189,0.18) API locale, P4
        participant Route as ApiLocale<br/>RoutesDesRelances
        participant Jets as PremiersJetsDesRelances
        participant Brouillons as BrouillonsRelus
        participant Depot as DepotDesCandidatures
        participant Adaptateur as FournisseurEnLigne
    end
    box rgba(107,107,107,0.2) Système extérieur, P4
        participant IA as IA en ligne
    end

    Personne->>Ecran: demande un premier jet
    Ecran->>Client: demanderUnPremierJet(candidature, relance)
    Client->>Route: POST …/premier-jet, sans corps
    Note over Route: route « sans la base »
    Route->>Jets: RedigerAsync
    Jets->>Jets: ReglageDeLIA.Fournisseur()
    break aucune IA prête
        Route-->>Client: 409
    end
    Jets->>Jets: PorteDeLaBase.EssayerDEntrer()
    break restauration en cours
        Route-->>Client: 503
    end
    Jets->>Depot: TrouverAsync(candidature)
    Jets->>Jets: PorteDeLaBase.Sortir()
    break candidature inconnue, relance plus à faire, ou donnée absente
        Route-->>Client: 404 ou 409
    end
    Jets->>Adaptateur: DemanderAsync, poste, entreprise, date d'envoi
    break une autre demande à l'IA en cours
        Route-->>Client: 409
    end
    Adaptateur->>IA: HTTPS, les trois données, les consignes et la clé
    IA-->>Adaptateur: la réponse, ou un échec
    Adaptateur-->>Jets: texte ou échec
    break échec du fournisseur
        Route-->>Client: 200, l'échec et son message
    end
    Jets->>Jets: PremierJetDeRelance.Nettoyer(réponse)
    break plus rien après le nettoyage
        Route-->>Client: 200, l'échec et son message
    end
    Jets->>Jets: PremierJetDeRelance.Assembler
    Route-->>Client: 200, le texte
    Client-->>Ecran: le texte à relire
    Personne->>Ecran: relit, corrige, ouvre dans la messagerie
    Ecran->>Client: deposerLeBrouillonRelu(…, texte)
    Client->>Route: PUT …/brouillon-relu, le texte
    Route->>Jets: DeposerAsync
    break texte refusé par sa règle
        Route-->>Client: 400
    end
    Jets->>Depot: TrouverAsync(candidature)
    break candidature inconnue, relance plus à faire, ou lien trop long
        Route-->>Client: 404, 409 ou 400
    end
    Jets->>Brouillons: Deposer(relance, texte)
    Route-->>Client: 204
```

| Légende | Sens |
| --- | --- |
| Silhouette | la personne qui se sert de Sollicito |
| Cadre bleuté | un conteneur de Sollicito, avec le palier du parcours |
| Cadre gris | un système extérieur |
| Flèche pleine | un appel, une requête ou un message ; vers son départ, une étape interne |
| Flèche pointillée | une réponse |
| Numéro | l'ordre des messages |
| Bloc `break` | un refus ou un échec : le parcours s'arrête là |
| Encadré posé sur des lignes verticales | une précision sur ces participants |

Avant de montrer le bouton du premier jet, l'écran lit l'état de l'IA (`GET /api/reglages/ia`). La porte de la base encadre la lecture de la relance ; le dépôt du texte relu, lui, la traverse par `PorteDesRequetes`. L'adaptateur lit la clé et prend `PorteDesDemandes`, qui ne laisse passer qu'une demande à la fois. `FournisseurEnLigne` est la base de `ClaudeEnLigne` et d'`OpenAIEnLigne`, derrière le port `IFournisseurDIA`.

### L'ouverture du brouillon dans la messagerie

```mermaid
sequenceDiagram
    autonumber 32
    actor Personne
    box rgba(17,104,189,0.18) Interface, P3 et P4
        participant Ecran as PremierJetParLIA<br/>PreparerLeBrouillon
        participant Client as canalDeLHote.ts
    end
    box rgba(17,104,189,0.18) Fenêtre hôte, P3 et P4
        participant Fenetre as FenetreHote
        participant Lien as LienDuBrouillon<br/>OuvreurDeBrouillonsParLeShell
    end
    box rgba(17,104,189,0.18) API locale, P3 et P4
        participant Brouillons as BrouillonsDesRelances
        participant Depot as DepotDesCandidatures
    end
    box rgba(107,107,107,0.2) Système extérieur, P3
        participant Messagerie
    end

    alt texte relu d'un premier jet (P4)
        Ecran->>Client: demanderLeBrouillonRelu(relance)
        Client->>Fenetre: message {type: "ouvrirLeBrouillonRelu", relance}
        Fenetre->>Brouillons: PreparerLeBrouillonReluAsync(relance)
        Brouillons->>Brouillons: BrouillonsRelus.Prendre(relance)
        Brouillons->>Depot: LireLaRelancePourLeBrouillonAsync
        Brouillons-->>Fenetre: PremierJetDeRelance.BrouillonRelu, ou rien
    else brouillon simple (P3)
        Personne->>Ecran: prépare le brouillon
        Ecran->>Client: demanderLeBrouillon(relance)
        Client->>Fenetre: message {type: "ouvrirLeBrouillon", relance}
        Fenetre->>Brouillons: PreparerAsync(relance)
        Brouillons->>Depot: LireLaRelancePourLeBrouillonAsync
        Brouillons-->>Fenetre: BrouillonDeRelance.De, ou rien
    end
    opt un brouillon préparé, et son lien construit
        Fenetre->>Lien: Construire(brouillon), puis Ouvrir(lien)
        Lien->>Messagerie: lien mailto, par le shell de Windows
    end
    Fenetre-->>Client: message {type: "brouillonDeRelance" ou "brouillonRelu", relance, issue}
    Client-->>Ecran: ouvert, refusé ou impossible
    Note over Personne,Messagerie: l'utilisateur relit et envoie lui-même
```

| Légende | Sens |
| --- | --- |
| Silhouette | la personne qui se sert de Sollicito |
| Cadre bleuté | un conteneur de Sollicito, avec le palier du parcours |
| Cadre gris | un système extérieur |
| Flèche pleine | un appel, une requête ou un message ; vers son départ, une étape interne |
| Flèche pointillée | une réponse |
| Numéro | l'ordre des messages, à la suite du diagramme précédent |
| Bloc `alt` | une branche parmi plusieurs |
| Bloc `opt` | une étape qui n'a lieu que sous sa condition |
| Encadré posé sur des lignes verticales | une précision sur ces participants |

Avant d'ouvrir, la fenêtre vérifie la source et la forme de chaque message (`ReglesDeLaFenetre`), comme pour le jeton ; sans réponse en 15 secondes, la page cesse d'attendre. Les deux messages qui demandent un brouillon ne portent que l'identifiant de la relance, jamais le texte ni l'adresse. `BrouillonsDesRelances` prend le texte relu dans `BrouillonsRelus`, et la porte de la base encadre sa préparation. `OuvreurDeBrouillonsParLeShell` implémente `IOuvreurDeBrouillons`.

Ce que reçoit la messagerie, et quand cela quitte le poste : [vues d'architecture](vues-d-architecture.md#ce-qui-quitte-le-poste), échange ③ ; ce que reçoit l'IA en ligne : échange ①.
