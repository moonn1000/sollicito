# ADR-0013 : offres collées, texte gardé, empreinte, doublon possible avec accord, entreprises proposées

- **Statut** : acceptée ; « Pour #38 » (« Les entreprises proposées ») et la liste des offres (« Conséquences ») précisés par l'[ADR-0014](0014-postuler-a-une-offre.md) : le lien porté par la candidature, l'entreprise d'abord sur l'offre, la liste filtrée dans la page ; « Le connecteur commun, en forme minimale » (les champs de l'empreinte, le geste par lot) et l'alternative « Enregistrer d'abord, puis signaler le doublon possible, comme le fera l'import CSV » précisés par l'[ADR-0015](0015-import-csv-des-offres.md) ; l'écran de saisie précisé par l'[ADR-0018](0018-extraction-d-une-offre-collee.md) : ses champs se proposent aussi par l'IA, sur un clic, aux règles du collage, et se relisent avant l'enregistrement
- **Date** : 2026-11-22
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D5 (l'identifiant externe d'une offre attend une source qui en fournit un), D6 (le connecteur commun, en forme minimale) et D9 (le lien de l'annonce d'une offre) ; [ADR-0005](0005-modele-de-domaine.md), « Dédoublonner les offres » (l'empreinte pas à pas, l'accord devant un doublon possible, le poste normalisé) ; [ADR-0007](0007-historique-en-vigueur-et-nom-corrige.md), « À vérifier » (une entreprise que seule une offre désigne est proposée)

## Contexte

La story #36 fait entrer les premières offres dans Sollicito : un texte trouvé ailleurs, collé, dont l'utilisateur remplit lui-même les champs. Elle absorbe #34, le connecteur commun, dont l'offre collée est le premier adaptateur ; l'import CSV (#40) sera le second. L'ADR-0005 fixe les règles du dédoublonnage : le couple (source, empreinte) reconnaît une offre sans identifiant, et une offre de même entreprise et de même poste qu'une offre enregistrée est un doublon possible, que l'utilisateur garde ou abandonne. Plusieurs points restaient ouverts, relevés le 2026-11-22 :

- **Le texte « gardé tel quel »** : le modèle de domaine le dit, sans dire ce que deviennent les fins de ligne, les blancs de bord, ni les caractères qu'un texte saisi refuse (#11). Un texte copié d'un PDF porte souvent des sauts de page (U+000C).
- **L'empreinte** : sa normalisation est écrite (NFKC, minuscules, blancs réduits), pas son encodage ni sa forme.
- **Recoller le même texte est refusé** (« ni recréée ni écrasée ») : sans correction des champs, une faute de frappe dans le poste ou l'entreprise d'une offre ne se réparerait jamais.
- **L'ADR-0007 laisse « À vérifier »** : une entreprise que seule une offre désigne est-elle proposée par les écrans de saisie ?
- **Un champ répété passe sur toutes les routes** : `AllowDuplicateProperties` vaut `true` par défaut dans `System.Text.Json` (.NET 10.0.15), et l'API ne le réglait pas. Une sonde du 2026-11-22, sur une API minimale hors du dépôt, l'a montré : `{"a":"x","a":"y"}` rendait 200 et « y » ; avec `false`, 400.
- **Le lien de l'annonce** : la fenêtre ouvre déjà un lien `http` ou `https` en nouvelle fenêtre, sur un geste de l'utilisateur (ADR-0006), mais l'interface n'en avait aucun.

## Décision

### Le texte collé, gardé tel quel à deux retouches près

- **Ses fins de ligne s'écrivent U+000A.** Comptent comme fins de ligne : CRLF, CR, U+000B, U+000C (le saut de page d'un PDF), U+0085, U+2028 et U+2029.
- **Ses blancs du début et de la fin sont retirés.** Rien d'autre ne change : ni HTML ni Markdown n'y sont lus, aucune ligne n'est recoupée, rien n'est tronqué.
- **Requis, de 50 000 caractères au plus** (unités UTF-16, comme les compte le navigateur), soit une quinzaine de pages. Le corps de la route est borné à 1 Mio, assez pour 50 000 caractères même tous échappés en `\uXXXX` ; au-delà, 413.
- **Les caractères refusés le sont comme pour un texte saisi (#11)** : tout autre caractère de contrôle que le saut de ligne et la tabulation, les contrôles de la direction du texte, les non-caractères, les moitiés isolées d'une paire de substitution. Le refus donne le numéro de la ligne du premier caractère refusé, jamais le texte.
- **L'écran ne borne pas la saisie** : un `maxlength` tronquerait le collage sans le dire. L'API refuse, et l'écran le dit.

### L'empreinte, pas à pas

1. Chaque partie perd ses caractères de contrôle autres que les blancs, U+001F compris.
2. Elle se décompose puis se recompose en Unicode NFKC (`string.Normalize(NormalizationForm.FormKC)`) : la ligature « ﬁ » devient « fi », l'espace insécable une espace ; les accents restent.
3. Elle passe en minuscules, culture invariante.
4. Toute suite de blancs au sens de `char.IsWhiteSpace` se réduit à une espace, sans blanc au début ni à la fin.
5. Les parties se joignent par U+001F, qui n'est pas un blanc.
6. Le tout, codé en UTF-8, se hache par SHA-256, et s'écrit en 64 chiffres hexadécimaux minuscules.

L'offre collée n'a qu'une partie, son texte. #40 passera les champs d'une ligne, dans l'ordre de son format : la règle du modèle de domaine. L'empreinte ne se calcule jamais sur un texte déjà joint. Elle se calcule une fois, à l'enregistrement, et se garde ; le couple `(Source, Empreinte)` est unique en base.

### Le même texte recollé (règle 2)

Dans l'écriture exclusive de l'enregistrement, une offre de même source et de même empreinte se cherche d'abord. Trouvée, rien ne s'enregistre : ni l'offre, ni une entreprise, et les champs saisis n'écrasent rien. L'API répond 409, motif `dejaEnregistree`, avec l'identifiant et la date d'ajout de l'offre trouvée, lus en base ; l'écran propose de l'ouvrir. L'index unique fait refuser par la base une seconde offre.

### Le doublon possible, et l'accord (règle 3)

Sur le modèle de l'accord de l'ADR-0007 :

- **La comparaison** porte, dans la même écriture exclusive, sur toute offre de même entreprise (même clé) et de même poste normalisé, quelle que soit sa source. Une offre sans entreprise n'est pas comparée. Le poste normalisé suit les étapes 1 à 4 de la clé d'une entreprise, sans retirer de forme juridique ; il se garde en base, indexé avec l'entreprise. Un poste sans lettre ni chiffre, dont la forme normalisée serait vide, est refusé.
- **Sans l'accord**, rien ne s'enregistre : l'API répond 409, motif `doublonPossible`, avec les offres trouvées (identifiant, poste, entreprise, lieu, date d'ajout), lues en base, jamais le texte saisi. L'écran les montre, avec « Voir cette offre », « Garder les deux », qui renvoie la saisie avec l'accord (`garderLesDeux`), et « Renoncer à la nouvelle ».
- **Avec l'accord**, l'offre s'enregistre. La règle 2 passe toujours avant : un texte déjà enregistré reste refusé.
- **La fiche** d'une offre montre ses doublons possibles, relus à chaque ouverture. Jamais de fusion.

### Corriger une offre

Ses champs se corrigent depuis sa fiche (`PUT /api/offres/{id}`) : l'entreprise, le poste, le lieu, la date de publication et l'adresse de l'annonce. Jamais son texte ni sa source, qui ne changent plus après l'import : un corps qui porte un texte est illisible. L'entreprise saisie de nouveau se reconnaît par sa clé, ou se crée, sans qu'aucune entreprise ne soit renommée : le nom affiché ne se corrige que par la modification d'une candidature (ADR-0007). La règle 3 se refait quand l'entreprise ou le poste normalisé change, avec le même accord.

### Les entreprises proposées

Une entreprise reste proposée tant qu'une candidature **ou une offre** la désigne, par les écrans de saisie et de modification des candidatures comme des offres. À la seconde offre d'une même entreprise, l'écran propose son nom, et la règle 3 la retrouve. Une faute de frappe se répare par la correction de l'offre, et l'entreprise fautive n'est alors plus proposée. Pour #38 : « Postuler » reprend l'entreprise de l'offre, déjà proposée ; supprimer la candidature laisse l'offre, et l'entreprise reste proposée tant que l'offre la désigne.

### L'adresse de l'annonce, et le lien externe

- **La règle de l'API**, alignée sur celle de la fenêtre et plus stricte : blancs de bord retirés, 2 000 caractères au plus, ni blanc ni caractère de contrôle, de direction ou invalide ; elle commence par `http://` ou `https://`, et c'est une adresse absolue, avec un hôte, sans identifiants. Le refus ne reprend jamais l'adresse.
- **Un seul composant de lien externe** dans l'interface, `LienExterne` : un lien en `target="_blank"`, `rel="noopener noreferrer"`, qui revalide l'adresse au rendu par l'analyseur du navigateur (`http:` ou `https:`, un hôte, ni nom ni mot de passe) ; sinon, l'adresse s'affiche en texte, sans lien, car une base restaurée peut porter n'importe quoi. Son texte est l'adresse elle-même.
- **Sollicito ne lit jamais cette adresse** : seul le navigateur par défaut l'ouvre, sur le clic de l'utilisateur (ADR-0006). L'import d'une offre par son adresse attend la V2.

### Le connecteur commun, en forme minimale (#34)

- **Le modèle commun** (`OffreRecue`) : la source, les parties de l'empreinte, les champs tels que reçus.
- **Le port du cœur** (`IImportDesOffres`), que `ImportDesOffres` implémente : valider les champs par les règles du domaine, calculer l'empreinte sur les parties, appliquer les règles 2 et 3, reconnaître l'entreprise, enregistrer, dans une écriture exclusive.
- **Les adaptateurs** implémentent une seule interface, `IConnecteurDOffres` : une source, et les offres reçues de ce qu'elle donne. Le premier, celui de l'offre collée (`ConnecteurDuTexteColle`), fait de la saisie de la route une offre reçue, d'une seule partie.
- **Un banc de tests de contrat** (`ContratDUnConnecteurDOffres`), dont chaque adaptateur hérite : sa source, ses parties, des champs passés tels quels, une entrée vide ou piégée. #40 en héritera.

### Le corps strict, champ répété compris

`AllowDuplicateProperties = false` règle désormais la lecture JSON de toute l'API : un champ répété rend le corps illisible (400), comme un champ inconnu. Sans ce réglage, le dernier l'emporterait, et le corps strict de #10 se contournerait. Un test l'essaie sur une route de chaque famille : candidatures, fiche, contacts, relances, réglages, offres.

### En base

Une table `Offres`, sa clé vers `Entreprises` (`RESTRICT`), un index unique `(Source, Empreinte)` et un index `(EntrepriseId, PosteNormalise)` : une seule transaction, aucune table existante touchée. L'identifiant externe de la D5 n'entre pas en V1 : aucune source n'en fournit (règle 1) ; il viendra avec le Forem, en V2, avec sa migration. Le lien entre une offre et sa candidature attend #38.

## Alternatives écartées

- **Le texte strictement comme collé**, sans aucune retouche : l'empreinte n'en dépend pas, mais un texte venu d'un PDF serait refusé pour ses sauts de page.
- **Retirer les caractères refusés au lieu de refuser le texte** : le texte ne serait plus tel quel, et l'utilisateur ne le saurait pas.
- **Une borne de 20 000 caractères** : une offre longue, avec la présentation de l'entreprise, y touche déjà.
- **Rendre 200 avec l'offre trouvée** pour un texte recollé : l'écran ne distinguerait plus une offre enregistrée d'une offre retrouvée.
- **Enregistrer d'abord, puis signaler le doublon possible**, comme le fera l'import CSV (#40) : « Renoncer » supprimerait alors la nouvelle, par une route de suppression de plus.
- **La correction des champs confiée à #38** : d'ici là, une faute de frappe restait, et l'entreprise fautive restait proposée.
- **Seules les entreprises qu'une candidature porte**, comme avant : à la seconde offre d'une même entreprise, l'écran ne proposerait rien, et un autre nom ferait deux entreprises sans signal.
- **Pas d'adresse en #36** : l'offre perdrait le chemin de son annonce, que l'utilisateur colle en même temps que son texte.
- **Pas de port**, l'offre collée en direct : #40 aurait dû le construire, et défaire le chemin de l'offre collée.
- **Le seul corps des offres lu sans champ répété** : une lecture à part du corps, plus de code, et les autres routes resteraient contournables.
- **L'identifiant externe tout de suite**, toujours vide en V1 : une colonne morte jusqu'en V2.

## Conséquences

- **Positives** : un texte collé deux fois ne fait qu'une offre ; un doublon possible se voit avant d'entrer ; une faute de frappe se corrige ; les entreprises d'offre aident la saisie des candidatures ; aucune route n'admet plus un champ répété ; #40 hérite d'un port et d'un banc de contrat.
- **Négatives** :
  - un seul caractère refusé fait refuser tout le collage, que l'utilisateur nettoie ailleurs ; le refus dit sa ligne ;
  - une version d'Unicode plus récente dans .NET pourrait normaliser autrement un caractère rare : l'offre recollée ne serait pas reconnue, un doublon de plus, jamais une fusion à tort ;
  - deux postes de même intitulé dans une même entreprise demandent l'accord à chaque fois ;
  - entre #36 et #38, une offre ne se rouvre qu'après son enregistrement, ou depuis un signal : la liste des offres vient avec #38 ;
  - le nom affiché d'une entreprise ne se corrige pas depuis une offre ;
  - un corps qui portait un champ répété, et passait, est désormais refusé : l'interface n'en envoie jamais.

## À vérifier

- **Doublons possibles** (ADR-0005, « À vérifier ») : la paire (entreprise, poste) signale-t-elle trop souvent à tort ? À juger à la revue du palier 3 (#68) ; y ajouter le lieu reste la piste.
- **Le lien sur l'exe publié** : le clic ouvre le vrai navigateur par défaut (contrôle à la main, non bloquant).

## Sources

- [ADR-0001](0001-cadrage-architecture-v1.md), D5, D6 et D9 ; [ADR-0005](0005-modele-de-domaine.md) ; [ADR-0006](0006-requetes-et-liens-de-la-fenetre.md) ; [ADR-0007](0007-historique-en-vigueur-et-nom-corrige.md)
- Story : <https://github.com/moonn1000/sollicito/issues/36> ; connecteur commun, absorbé : <https://github.com/moonn1000/sollicito/issues/34>
- Sonde du 2026-11-22 : une API minimale ASP.NET Core 10.0.15, hors du dépôt, sur 127.0.0.1 ; un champ répété, puis une borne de corps posée sur une seule route (413 au-delà, les autres routes inchangées).

Consultée le 2026-11-22 :

- Microsoft Learn, `JsonSerializerOptions.AllowDuplicateProperties` (« The default is true ») : documentation de référence livrée avec .NET 10.0.15 (`System.Text.Json.xml`).
