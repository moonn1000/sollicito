# Le format de l'export

- **Mis à jour le** : 2026-11-27, avec #49 ; 2026-11-28, avec #118 (le oui à remplacer un fichier)
- **Décision** : [ADR-0020](adr/0020-export-des-donnees.md)

La section « Exporter vos données » des réglages enregistre toutes les données de la base dans un fichier, en JSON ou en CSV. Sollicito ouvre le dialogue « Enregistrer sous » de Windows, où l'utilisateur choisit le fichier, puis l'écrit. Ce document est la référence du format ; l'écran en reprend l'essentiel.

## Ce que contient l'export

- **Toutes les lignes des six tables de la base** : les entreprises, les contacts, les offres, les candidatures, les relances, et l'historique des candidatures (les événements).
- **Tous leurs champs**, y compris ceux que les écrans ne montrent pas : les identifiants, les liens d'une ligne à l'autre (`entrepriseId`, `offreId`, `candidatureId`, `destinataireId`, `corrigeId`, `relanceId`) et la sorte d'un événement.
- **Les champs que Sollicito calcule** : la clé d'une entreprise, le poste normalisé et l'empreinte d'une offre, tels qu'en base. L'empreinte d'une offre importée puis corrigée ne se recalcule pas ([import CSV](import-csv.md), « Les doublons ») : elle s'exporte telle quelle.

## Ce qu'il ne contient jamais

L'export ne lit que les six tables. Il ne contient donc jamais :

- la clé d'IA (`cle-ia.json`), ni le trousseau qui la chiffre (`Trousseau`) ;
- le jeton de lancement, qui ne vit qu'en mémoire ;
- les journaux, qui ne vont qu'à la console ;
- les copies de la base ;
- les réglages (`reglages.json` : le dossier des sauvegardes, le délai des relances, le fournisseur d'IA), ni `sauvegardes.json` ;
- le profil WebView2 ;
- les tables techniques de la base (`__EFMigrationsHistory`, `__EFMigrationsLock`, `sqlite_sequence`).

## Deux formes

- **JSON** : un seul fichier, `.json`, qui garde chaque valeur telle qu'en base. C'est la forme qui fait foi.
- **CSV** : une archive ZIP, `.zip`, d'un fichier CSV par sorte de données, à ouvrir dans un tableur. Une cellule qui commencerait comme une formule y reçoit une apostrophe en tête : le CSV ne garde donc pas chaque valeur telle quelle.

Recharger un export dans Sollicito attend la V2 (ADR-0004, D10).

## Un exemple

Un export JSON, aux données fictives, réduit à une ligne par entité et à un événement de chaque sorte :

```json
{
  "format": "sollicito-export",
  "version": 1,
  "exporteLe": "2026-11-27T14:09:35.5713768Z",
  "entreprises": [
    {
      "id": 1,
      "nom": "Atelier Éole & Fils",
      "cle": "atelier eole fils"
    }
  ],
  "contacts": [
    {
      "id": 1,
      "entrepriseId": 1,
      "nom": "Zoé Lefèvre-Dupré",
      "email": "zoe.lefevre@exemple.invalid"
    }
  ],
  "offres": [
    {
      "id": 1,
      "source": "TexteColle",
      "entrepriseId": 1,
      "poste": "Comptable « senior »",
      "posteNormalise": "comptable senior",
      "lieu": "Liège",
      "publieeLe": "2026-11-01",
      "adresse": "https://emploi.exemple.invalid/offres/1?source=colle&id=42",
      "texte": "Comptable « senior » (H/F/X)\nÀ Liège, temps plein.\n\nVous tiendrez la comptabilité ; un « + » apprécié.",
      "empreinte": "313cd7fe0abfc693655dfebfde0f90b4a6babea8e0912f1cd62f4aaa7286fe10",
      "ajouteeLe": "2026-11-18T15:42:00.6234567Z"
    }
  ],
  "candidatures": [
    {
      "id": 1,
      "entrepriseId": 1,
      "offreId": 1,
      "poste": "Comptable « senior »",
      "envoyeeLe": "2026-11-02T08:15:00Z",
      "statut": "Entretien"
    }
  ],
  "relances": [
    {
      "id": 1,
      "candidatureId": 1,
      "destinataireId": 1,
      "echeance": "2026-11-09",
      "etat": "Faite"
    }
  ],
  "evenements": [
    {
      "id": 1,
      "candidatureId": 1,
      "sorte": "ClotureDeRelance",
      "survenuLe": "2026-11-09T13:31:00.1234567Z",
      "relanceId": 1
    },
    {
      "id": 2,
      "candidatureId": 1,
      "sorte": "ChangementDeStatut",
      "survenuLe": "2026-11-10T08:00:00Z",
      "ancien": "Envoyee",
      "nouveau": "Entretien",
      "corrigeId": null
    },
    {
      "id": 3,
      "candidatureId": 1,
      "sorte": "Etape",
      "survenuLe": "2026-11-10T13:00:00Z",
      "type": "Entretien",
      "note": "Premier entretien : accueil chaleureux.\nÀ suivre.",
      "corrigeId": null
    }
  ]
}
```

## Le JSON

- **L'encodage** : UTF-8 sans BOM, indenté de deux espaces, les lignes finies par un saut de ligne (LF). Les lettres accentuées et les signes s'écrivent tels quels. S'échappent en `\uXXXX` ce que JSON impose (le guillemet, la barre oblique inverse, les caractères de contrôle de U+0000 à U+001F, comme `\n` ou `\t`), et ce que l'encodeur de .NET échappe toujours : les autres caractères de contrôle (U+007F à U+009F) ; les espaces autres que l'espace ordinaire, dont les espaces insécables (U+00A0, U+202F) qu'un texte en français porte souvent ; les séparateurs de ligne et de paragraphe (U+2028, U+2029) ; l'indicateur d'ordre des octets (U+FEFF) ; les caractères d'usage privé ou non assignés ; et les caractères hors du plan de base d'Unicode, comme un émoji, écrits en deux `\uXXXX`. La valeur relue est toujours celle de la base.
- **L'en-tête** : `format` vaut `sollicito-export`, `version` vaut `1`, et `exporteLe` est l'instant de l'export.
- **Les tableaux** : un par entité, dans l'ordre des liens : `entreprises`, `contacts`, `offres`, `candidatures`, `relances`, `evenements`. Chacun est trié par identifiant croissant. Les identifiants sont ceux de la base, et les liens les désignent.
- **Les valeurs** :
  - un instant en ISO 8601, en UTC, suivi de `Z` : `2026-11-02T08:15:00Z`, avec sa fraction de seconde, jusqu'à sept chiffres, quand elle n'est pas nulle (`2026-11-18T13:31:00.1234567Z`) ;
  - un jour du calendrier en `AAAA-MM-JJ` ;
  - une énumération en toutes lettres, telle que la base la nomme ;
  - une valeur absente en `null`.
- **Rien n'est neutralisé** : une valeur qui commence par `=` ou `@` reste telle quelle, comme dans la base.

Dans les tables qui suivent, un champ marqué « calculé » est calculé par Sollicito : il ne se saisit pas.

### Les entreprises

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `id` | nombre | non | `Entreprises.Id` | l'identifiant de l'entreprise |
| `nom` | texte | non | `Entreprises.Nom` | le nom affiché |
| `cle` | texte, calculé | non | `Entreprises.Cle` | la clé qui reconnaît l'entreprise ([modèle de domaine](modele-de-domaine.md), « Reconnaître une entreprise ») |

### Les contacts

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `id` | nombre | non | `Contacts.Id` | l'identifiant du contact |
| `entrepriseId` | nombre | non | `Contacts.EntrepriseId` | son entreprise |
| `nom` | texte | non | `Contacts.Nom` | son nom |
| `email` | texte | non | `Contacts.Email` | son adresse électronique |

### Les offres

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `id` | nombre | non | `Offres.Id` | l'identifiant de l'offre |
| `source` | énumération | non | `Offres.Source` | d'où elle vient : `TexteColle` (collée) ou `FichierCsv` (importée d'un fichier CSV) |
| `entrepriseId` | nombre | oui | `Offres.EntrepriseId` | l'entreprise qui la publie, si l'offre la nomme |
| `poste` | texte | non | `Offres.Poste` | le poste |
| `posteNormalise` | texte, calculé | non | `Offres.PosteNormalise` | le poste normalisé, qui sert au doublon possible ([modèle de domaine](modele-de-domaine.md), « Dédoublonner les offres ») |
| `lieu` | texte | oui | `Offres.Lieu` | le lieu |
| `publieeLe` | jour | oui | `Offres.PublieeLe` | la date de publication |
| `adresse` | texte | oui | `Offres.Adresse` | l'adresse de l'annonce |
| `texte` | texte | oui | `Offres.Texte` | le texte de l'offre, sur plusieurs lignes |
| `empreinte` | texte, calculé | non | `Offres.Empreinte` | l'empreinte de l'offre à son entrée, qui reconnaît une offre déjà enregistrée |
| `ajouteeLe` | instant | non | `Offres.AjouteeLe` | l'instant où elle est entrée dans Sollicito |

### Les candidatures

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `id` | nombre | non | `Candidatures.Id` | l'identifiant de la candidature |
| `entrepriseId` | nombre | non | `Candidatures.EntrepriseId` | son entreprise |
| `offreId` | nombre | oui | `Candidatures.OffreId` | l'offre d'où elle vient ; une offre a au plus une candidature |
| `poste` | texte | non | `Candidatures.Poste` | le poste |
| `envoyeeLe` | instant | non | `Candidatures.EnvoyeeLe` | l'instant de l'envoi |
| `statut` | énumération | non | `Candidatures.Statut` | `Envoyee`, `Entretien`, `Proposition`, `Acceptee`, `Refusee`, `Retiree` ou `SansReponse` |

### Les relances

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `id` | nombre | non | `Relances.Id` | l'identifiant de la relance |
| `candidatureId` | nombre | non | `Relances.CandidatureId` | sa candidature |
| `destinataireId` | nombre | oui | `Relances.DestinataireId` | le contact à qui l'adresser |
| `echeance` | jour | non | `Relances.Echeance` | son échéance |
| `etat` | énumération | non | `Relances.Etat` | `AFaire`, `Faite` ou `Abandonnee` |

### Les événements

Un événement de l'historique porte quatre champs communs, puis ceux de sa sorte, et seulement ceux-là. Les trois sortes vivent dans une seule table de la base, `EvenementsDeCandidature`.

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `id` | nombre | non | `Id` | l'identifiant de l'événement |
| `candidatureId` | nombre | non | `CandidatureId` | sa candidature |
| `sorte` | énumération | non | `Sorte` | `ChangementDeStatut`, `Etape` ou `ClotureDeRelance` |
| `survenuLe` | instant | non | `SurvenuLe` | l'instant où il est survenu |

Un changement de statut :

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `ancien` | énumération | non | `Ancien` | le statut d'avant |
| `nouveau` | énumération | non | `Nouveau` | le statut d'après |
| `corrigeId` | nombre | oui | `CorrigeId` | le changement de statut qu'il corrige |

Une étape :

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `type` | énumération | non | `Type` | `Appel`, `Entretien`, `Echange` ou `Autre` |
| `note` | texte | oui | `Note` | la note de l'étape, sur plusieurs lignes |
| `corrigeId` | nombre | oui | `EtapeCorrigeeId` | l'étape qu'elle corrige |

Une clôture de relance :

| Champ | Type | Facultatif | Colonne de la base | Ce qu'il dit |
| --- | --- | --- | --- | --- |
| `relanceId` | nombre | non | `RelanceId` | la relance close |

Le champ `corrigeId` d'une étape vient de la colonne `EtapeCorrigeeId` : un seul nom pour les deux corrections, que la sorte distingue. Un événement d'une sorte que Sollicito n'écrit pas fait échouer l'export, et rien n'est enregistré.

## Le CSV

- **L'archive** : un seul fichier `.zip`, qui porte sept fichiers, dans cet ordre : `export.csv`, `entreprises.csv`, `contacts.csv`, `offres.csv`, `candidatures.csv`, `relances.csv` et `evenements.csv`. Ses entrées portent l'instant de l'export, à l'heure du poste.
- **`export.csv`** porte trois colonnes, `format`, `version` et `exporteLe`, et une ligne : `sollicito-export-csv`, `1`, et l'instant de l'export.
- **Les colonnes** de chaque autre fichier sont les champs du JSON, sous les mêmes noms et dans le même ordre ; l'en-tête les nomme. `evenements.csv` a une colonne par champ des trois sortes, vide hors de sa sorte : `id;candidatureId;sorte;survenuLe;ancien;nouveau;corrigeId;type;note;relanceId`.
- **Les valeurs** s'écrivent comme dans le JSON : les instants, les jours et les énumérations. Une valeur absente donne une cellule vide.

### L'encodage, le séparateur, les guillemets

La syntaxe est celle de l'[import CSV des offres](import-csv.md), mais pas ses colonnes :

- **UTF-8 avec BOM**, qu'Excel lit sans assistant ;
- **le séparateur** `;`, celui d'Excel en Belgique et en France ;
- **les guillemets** droits (`"`) autour d'une cellule qui contient `;`, un guillemet, un retour chariot ou un saut de ligne ; un guillemet s'y écrit `""` (RFC 4180) ;
- **les fins de ligne** : CRLF entre les lignes ; les sauts de ligne d'une cellule restent tels quels.

### Une cellule qui commencerait comme une formule

Une cellule dont le premier caractère est l'un des onze débuts qu'OWASP demande de neutraliser reçoit une apostrophe (`'`) en tête : `=`, `+`, `-`, `@`, leurs formes pleine chasse (`＝`, `＋`, `－`, `＠`), la tabulation, le retour chariot et le saut de ligne. La règle des guillemets s'applique ensuite : la valeur `=1+2";=1+2` s'écrit `"'=1+2"";=1+2"`.

- Elle vaut pour toutes les cellules. Les identifiants, les instants et les jours ne commencent jamais ainsi.
- Le reste de la valeur ne change pas.

### Ses limites

- **Un tableur qui réenregistre le fichier en CSV** peut retirer l'apostrophe, ou les guillemets (OWASP) : le fichier réenregistré n'est plus protégé.
- **Le CSV ne se relit pas à l'identique** : une valeur qui commençait déjà par une apostrophe ne se distingue plus d'une valeur neutralisée, ni une valeur absente d'un texte vide. Il n'est pas non plus au format de l'import d'offres. Le JSON fait foi.
- **Excel ne garde que 32 767 caractères par cellule**, et le texte d'une offre peut en compter 50 000 : il ne s'y lit pas toujours en entier.
- **Un tableur réglé sur un autre séparateur** : l'export sépare ses cellules par `;`. Lu avec la virgule, ou en coupant aussi à la tabulation, il fait une nouvelle cellule de ce qui suit une virgule ou une tabulation, au milieu d'une cellule que Sollicito n'a pas mise entre guillemets. Une formule qui la suit (`texte,=1+2`) peut alors s'exécuter : la neutralisation ne vise que le début d'une cellule.
- **Ce qu'Excel et LibreOffice affichent de l'apostrophe** reste à vérifier (ADR-0020, « À vérifier »).

## La version

Le format est à sa version 1, la même pour ses deux formes. Toute modification du format, un champ ajouté, retiré, renommé ou écrit autrement, fait passer à la version suivante.

## Le fichier choisi

- **Le dialogue de Windows** propose `sollicito-export-AAAA-MM-JJ.json`, ou `.zip`, au jour local, et un seul type de fichier. La page de Sollicito ne choisit jamais le fichier, et n'en reçoit jamais le nom.
- **L'extension** : le nom doit finir par `.json`, ou par `.zip` pour le CSV ; sinon, rien n'est enregistré.
- **Le dossier de données de Sollicito** (`%LOCALAPPDATA%\Sollicito`) est refusé, avec ses sous-dossiers, dont le dossier des sauvegardes par défaut. La règle le reconnaît sous une forme à lettre de lecteur ou UNC, écrite telle quelle ou derrière `\\?\` ou `\\.\`, nom court compris : par ces chemins, un export n'y remplace pas la base, les réglages, `sauvegardes.json` ou la clé d'IA. Un chemin qui y mène autrement, par une jonction, un lien, un partage réseau ou une autre forme de périphérique (`\\?\Volume{…}\`, `\\?\GLOBALROOT\`, `\??\`), n'est pas reconnu : seul le oui du dialogue, avant de remplacer un fichier, protège alors. Un dossier des sauvegardes choisi ailleurs n'est pas refusé.
- **Un fichier existant** n'est remplacé qu'après le oui de l'utilisateur dans le dialogue, et s'il est encore celui que le dialogue a vu : Sollicito note sa taille et ses dates au retour du dialogue, et les compare juste avant de le remplacer. Un fichier apparu ou changé entre-temps sous le même nom n'est pas remplacé.
- **Rien de partiel** : Sollicito écrit d'abord un fichier provisoire dans le dossier choisi, `sollicito-export-<huit caractères hexadécimaux>.partiel`, le pousse sur le disque, puis lui donne le nom choisi. Un échec, une annulation, ou la fermeture de Sollicito pendant l'export l'effacent : rien ne reste sous le nom choisi, et un fichier qu'on allait remplacer reste intact.
- **Après un arrêt brutal** (une coupure de courant, Sollicito arrêté de force), ou si la fermeture n'a pas pu l'effacer à temps, ce provisoire peut rester dans le dossier choisi : Sollicito ne garde ce dossier nulle part, et ne peut pas l'y retrouver. Il porte une partie des données, non chiffrée, et s'efface à la main.
- **Un état cohérent** : l'export lit la base en un seul instantané. Ce qui s'enregistre pendant l'export n'y paraît pas, et l'export ne retient pas l'utilisateur. Pendant une restauration, il est refusé, et rien n'est enregistré.

## Un fichier non chiffré

L'export n'est pas chiffré : toute personne qui accède au fichier peut le lire. Enregistré dans un dossier synchronisé, comme OneDrive, il part là où ce dossier l'envoie. Sollicito ne l'envoie nulle part lui-même : il l'écrit dans le fichier choisi.

## Le Forem

Sans objet en V1 : aucune offre n'en vient. Son connecteur arrive en V2, avec la décision sur la licence de ses données (CC BY-SA 4.0, ADR-0001, « À vérifier ») : elle dira ce que l'export doit en porter.
