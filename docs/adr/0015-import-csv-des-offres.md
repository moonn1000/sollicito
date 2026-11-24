# ADR-0015 : import d'offres d'un fichier CSV, format fermé, aperçu puis import en une transaction, doublon possible gardé ligne à ligne

- **Statut** : acceptée ; la copie avant l'import (« À trancher ») attend la décision du mainteneur
- **Date** : 2026-11-24
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D6 (le fichier CSV, second adaptateur du connecteur commun ; le geste par lot) et D10 (le format pour schéma, les bornes, l'entrée non fiable) ; [ADR-0005](0005-modele-de-domaine.md), « Dédoublonner les offres » (le sort d'une ligne de CSV) ; [ADR-0013](0013-offres-collees.md), « Le connecteur commun, en forme minimale » (les champs de l'empreinte, le geste par lot) et son alternative « Enregistrer d'abord, puis signaler le doublon possible, comme le fera l'import CSV »

## Contexte

La story #40 fait entrer des offres par un fichier CSV, pour reprendre une liste tenue ailleurs, dans un tableur le plus souvent. Elle s'appuie sur le connecteur commun de #36 (ADR-0013) et sur le dédoublonnage du [modèle de domaine](../modele-de-domaine.md). Plusieurs points restaient ouverts, relevés le 2026-11-24 :

- **Le modèle de domaine disait déjà le sort des doublons** : « une ligne reconnue par la règle 1 ou 2 est écartée et signalée dans le compte rendu ; un doublon possible (règle 3) est importé, et signalé comme tel ». L'ADR-0013 écartait ce chemin pour l'offre collée : « Renoncer » demanderait une route de suppression, et la V1 ne supprime aucune offre.
- **Le port n'avait qu'un geste unitaire** : une offre par écriture exclusive, sans voie pour un refus du fichier, une ligne mal formée ni un numéro de ligne. Le banc de contrat n'essayait pas ce qu'il nommait, et le cœur ne validait pas les parties de l'empreinte qu'un connecteur lui donnait (#98, point 16).
- **Une cellule `texte` vide** aurait été lue comme un texte manquant, que seule l'offre collée exige.
- **Les tableurs**, non vérifié (forums seulement) : Excel en français enregistrerait « CSV UTF-8 » avec un BOM et le séparateur `;`, son « CSV (séparateur : point-virgule) » en Windows-1252, et une date sous sa forme affichée, `JJ/MM/AAAA`.
- **Aucune migration n'est nécessaire** : la source s'écrit en toutes lettres, sur 20 caractères au plus, et `Offres.Texte` admettait déjà `NULL` (#36 l'avait prévu). Le modèle de la base ne change pas (`HasPendingModelChanges`, et `dotnet ef migrations has-pending-model-changes`).

## Décision

### Le format, fermé et documenté

La référence est [`docs/import-csv.md`](../import-csv.md) ; l'écran « Importer des offres » en reprend l'essentiel, car l'exe ne mène pas au dépôt.

- **L'en-tête**, la première ligne, nomme les colonnes : `poste`, obligatoire ; `entreprise`, `lieu`, `publieeLe`, `adresse` et `texte`, facultatives ; sans tenir compte de la casse ni des blancs de bord, dans l'ordre qu'on veut. Une colonne inconnue, vide ou répétée, ou un en-tête sans `poste`, refuse le fichier ; le refus désigne la colonne par son rang, jamais par son nom, qui vient du fichier.
- **UTF-8**, avec ou sans BOM, décodé strictement : tout autre encodage refuse le fichier, BOM UTF-16 compris. Un fichier Windows-1252 sans accent passe : ses octets sont aussi de l'UTF-8.
- **Le séparateur**, `;` ou `,`, se lit sur l'en-tête, dont les noms n'en contiennent aucun ; les deux à la fois refusent le fichier.
- **Les guillemets** suivent la RFC 4180 : une cellule entre guillemets droits porte le séparateur, des sauts de ligne, et `""` pour un guillemet. Un guillemet dans une cellule sans guillemets, autre chose qu'un séparateur ou une fin de ligne après le guillemet fermant, ou un guillemet jamais fermé, refusent le fichier, avec la ligne.
- **Les fins de ligne** : CRLF, LF ou CR seul ; la dernière est facultative. Une ligne vide, ou sans cellule remplie, est ignorée et comptée ; une ligne qui n'a pas autant de cellules que l'en-tête est invalide.
- **Le numéro d'une ligne** est celui d'un tableur : l'en-tête est la ligne 1, et une cellule sur plusieurs lignes ne décale pas la suite.
- **Chaque cellule** suit la règle de son champ dans #36, avec ses messages. Une date s'écrit `AAAA-MM-JJ` ou, pour une ligne de CSV seulement, `JJ/MM/AAAA` : le jour d'abord, sur un ou deux chiffres comme le mois, l'année sur quatre ; jamais le mois en premier. L'offre collée garde `AAAA-MM-JJ` seule.

### Les bornes : une borne dépassée refuse tout le fichier

Le fichier fait 2 Mo au plus (2 097 152 octets), et porte 1 000 lignes au plus sous l'en-tête, vides comprises. Chaque cellule a la borne de son champ dans #36, mesurée comme le fait sa règle : 200 caractères pour le poste, l'entreprise et le lieu, 2 000 pour l'adresse, 50 000 pour le texte, fins de ligne ramenées. Au-delà, le fichier est refusé, et rien n'est importé : un guillemet mal fermé qui retrouve un guillemet plus loin avale plusieurs lignes dans une seule cellule, et le reste ne s'importe pas en taisant les lignes avalées. Le corps des deux routes est borné à 3 Mio (413), le fichier en base64 et son enveloppe ; l'écran refuse avant de le lire un fichier de plus de 2 Mo.

### Le transport : la page lit le fichier, l'API n'en reçoit que les octets

La page lit les octets du fichier choisi et les envoie en base64, dans un corps JSON strict (`contenu`). Ni chemin, ni nom de fichier, ni adresse : un champ de plus est refusé comme tout champ inconnu. L'API décode elle-même : l'encodage, le BOM et le format vivent en .NET, et se testent sur des octets piégés.

### Un aperçu, puis l'import, sans état dans l'API

- `POST /api/offres/import/apercu` n'écrit rien : il rend, ligne par ligne, ce que l'import ferait.
- L'utilisateur garde ou non chaque doublon possible, puis confirme. `POST /api/offres/import` reçoit le même fichier et les numéros des lignes gardées (`garder`) : l'API refait tout l'examen dans une écriture exclusive, puis importe en une seule transaction, et rend le compte rendu final.
- Si la base a changé depuis l'aperçu, l'import fait foi : une ligne devenue déjà enregistrée est écartée, une ligne devenue doublon possible et non gardée ne s'importe pas, et l'écran dit que le compte rendu diffère de l'aperçu.
- Le compte rendu dit le sort de chaque ligne, sans jamais le contenu d'une ligne invalide : importée, ou à importer ; déjà enregistrée ; en double dans le fichier ; doublon possible ; invalide, avec ses motifs par colonne. Une ligne admise montre son poste, son entreprise et son lieu tels que lus.

### Le sort des doublons

- **Règle 2**, même source et même empreinte, contre la base comme dans le fichier : la ligne est écartée et comptée ; aucun accord ne l'admet, comme pour un texte recollé.
- **Règle 3**, même entreprise et même poste normalisé, quelle que soit la source, contre la base et contre les lignes précédentes du fichier : la ligne ne s'importe qu'avec l'accord, donné ligne par ligne dans l'aperçu, case décochée d'office. Sans lui, elle est écartée et comptée. Jamais de fusion. Le sort d'une ligne ne dépend pas de l'accord donné aux autres : l'aperçu et l'import d'un même fichier, sur une même base, disent la même chose.
- Ce choix remplace, dans le modèle de domaine, « un doublon possible (règle 3) est importé, et signalé comme tel », et l'alternative de l'ADR-0013, « comme le fera l'import CSV ».

### L'empreinte d'une ligne, sur les champs lus

Le connecteur n'annonce plus les valeurs des parties de l'empreinte, mais les champs qui la font, dans l'ordre de son format ; le cœur les prend tels qu'il les a lus et validés.

- Une ligne de CSV : six champs, toujours dans l'ordre du format (poste, entreprise, lieu, publieeLe, adresse, texte), quel que soit l'ordre des colonnes du fichier ; une colonne absente ou une cellule vide donne une partie vide. La date entre sous sa forme `AAAA-MM-JJ` : une même ligne datée `18/11/2026` puis `2026-11-18` n'a qu'une empreinte.
- L'offre collée : son seul texte, lu. L'empreinte des offres déjà enregistrées ne change pas : le texte lu ne diffère du texte collé que par ses fins de ligne et ses blancs de bord, que la normalisation ramène de toute façon à une espace ou retire.
- La validation passe toujours avant l'empreinte : un non-caractère, de l'UTF-8 valide, ferait lever la normalisation. Un connecteur qui n'annonce aucun champ, ou un champ deux fois, est une erreur de programmation, pas un refus (#98, point 16).
- La source `FichierCsv` : une offre collée et la même offre importée ne se reconnaissent pas par la règle 2 ; la règle 3 les rapproche. Réimporter un fichier complété n'importe que ses lignes nouvelles.

### Le connecteur commun : le geste par lot

- **Un lecteur pur** (`LectureDUnFichierCsv`), sans entrée-sortie : des octets, l'en-tête et les lignes numérotées, ou le refus du fichier entier, d'une sorte fixée par le code (vide, taille, encodage, en-tête, structure, lignes, aucune offre, borne).
- **Le second connecteur** (`ConnecteurDuFichierCsv`), sur `IConnecteurDOffres`, inchangé : une offre reçue par ligne bien formée, aux cellules telles que lues, guillemets décodés, sans rien retoucher. Il passe le banc de contrat, qui vérifie désormais par le cœur qu'une entrée vide ne fait aucune offre admise.
- **Un pas de lecture pur** du cœur (`LectureDUneOffre`), partagé par l'offre collée et par le lot : les champs, le texte (vide, il vaut absence quand la source ne l'exige pas), puis l'empreinte.
- **Le port gagne l'examen et l'import d'un lot**. Les numéros de ligne restent hors du cœur : l'API relie chacun au rang de son offre.
- **L'entreprise** est reconnue par sa clé ou créée, sans renommage ; une entreprise neuve se crée une fois par fichier, sous le nom de la première ligne importée qui la désigne.

### Une seule transaction

L'examen, les entreprises neuves et les offres d'un import se valident ensemble, dans une seule écriture exclusive (`BEGIN IMMEDIATE`), ou rien ne l'est. Deux lignes de journal, de comptes seulement, marquent le début et la fin de l'écriture.

### Le contenu non fiable, et l'export à venir

Rien n'est interprété : une cellule qui commence par `=`, `+`, `-`, `@` ou `＠` reste du texte ; ni HTML, ni Markdown, ni formule. Tout s'affiche par React, échappé. Sollicito ne lit jamais l'adresse d'une annonce. L'export CSV à venir (#49) devra neutraliser, dans les cellules venues d'une offre importée, une tête en `=`, `+`, `-`, `@`, leurs formes pleine chasse (`＝ ＋ － ＠`), une tabulation ou un retour chariot (ADR-0001, D10).

### Les journaux

Ils ne notent que des comptes (lignes lues, vides, importées, écartées par sorte, invalides) et la sorte d'un refus du fichier : jamais le contenu, le nom du fichier, un poste, une entreprise, un lieu, une adresse, un texte, une empreinte ni une forme normalisée.

## À trancher

**Une copie avant l'import** (ADR-0001, D10, « L'import est une entrée non fiable : validé contre un schéma, borné en taille, précédé d'une sauvegarde ») : la décision revient au mainteneur. Les faits relevés le 2026-11-24 :

- la V1 ne supprime aucune offre : un import fait par erreur ne se défait que par une restauration (#28) ;
- la copie du jour se fait au premier lancement du jour, et se refait à la fermeture et à « Sauvegarder maintenant ». Avant la fermeture de la séance de l'import, elle porte l'état du début de la séance ; après, l'import. Reste alors la copie du jour d'utilisation précédent, gardée six jours d'utilisation de plus. Un import le premier jour, suivi d'une fermeture, ne laisse aucune copie d'avant lui ;
- la copie d'avant une mise à jour ne peut pas servir : elle est une par migration ;
- le mécanisme de la copie d'avant une restauration (#94) peut servir tel quel, mais l'écran de restauration la nommerait « Avant une restauration ». Avec son propre libellé, c'est une quatrième sorte de copie, gardée hors de la rotation.

## Alternatives écartées

- **Les intitulés « Poste », « Entreprise »… dans l'en-tête**, ou les six colonnes exigées dans cet ordre : deux vocabulaires pour les mêmes champs, ou un fichier à remanier en entier.
- **Windows-1252 admis aussi** : il faudrait deviner l'encodage, et un fichier UTF-8 abîmé passerait pour du Windows-1252, en silence.
- **Une ligne mal formée refusée seule**, quand ses limites restent sûres : un guillemet mal placé dit souvent que d'autres lignes sont touchées.
- **Une cellule trop longue qui ne refuse que sa ligne** : les lignes avalées par un guillemet mal fermé disparaîtraient sans un mot.
- **La page qui décode le fichier** (`TextDecoder` strict) : l'API ne verrait plus l'encodage, que seuls des tests de composants garderaient.
- **Une seule route**, avec un champ `confirmer` ; **un aperçu gardé en mémoire par l'API**, désigné par un jeton : un état de plus, à borner et à faire expirer.
- **Le doublon possible écarté sans accord** : une ligne sans texte n'aurait aucune voie d'entrée, ni par l'import, qui l'écarterait à chaque fois, ni par « Coller une offre », qui exige un texte.
- **Le doublon possible importé, puis signalé**, comme le disait le modèle de domaine : rien ne le retirerait, la V1 ne supprimant aucune offre.
- **Un seul accord pour tous les doublons possibles du fichier** : il couvrirait des lignes que l'utilisateur n'a pas regardées.
- **Les valeurs des parties de l'empreinte données par le connecteur**, la date telle que reçue : la même ligne, datée sous ses deux formes, ferait deux offres ; et le cœur devrait valider des parties qu'il n'a pas lues.
- **Un lot fait d'imports unitaires** : une transaction par offre, et un arrêt au milieu laisserait la moitié du fichier.

## Conséquences

- **Positives** : une liste tenue dans un tableur entre en une fois, après un aperçu qui n'écrit rien ; un fichier réimporté n'importe que ses lignes nouvelles ; un doublon possible ne s'importe qu'avec l'accord, ligne par ligne ; un arrêt brutal ne laisse jamais la moitié d'un fichier ; aucune migration ; le banc de contrat et le cœur gardent désormais ce qu'ils nomment.
- **Négatives** :
  - un fichier hors du format, ou une seule cellule trop longue, est refusé en entier : l'utilisateur le corrige dans son tableur ;
  - les noms de colonnes de l'API (`publieeLe`) ne sont pas ceux d'un tableur ;
  - un fichier Windows-1252 accentué est refusé, et se réenregistre en « CSV UTF-8 » ;
  - le fichier voyage deux fois sur 127.0.0.1, à l'aperçu puis à l'import ;
  - sans copie avant l'import, un import fait par erreur ne se défait que par une restauration, qui défait aussi le reste de la séance.

## À vérifier

- **Un fichier enregistré par un vrai tableur** s'importe, dates comprises : Excel en français au format « CSV UTF-8 », ou LibreOffice (contrôle à la main, non bloquant).
- **Le dialogue de Windows** s'ouvre sur l'exe publié, au choix du fichier (contrôle à la main, non bloquant) ; la vraie fenêtre de test pose le fichier par CDP, sans dialogue.

## Sources

- [ADR-0001](0001-cadrage-architecture-v1.md), D6 et D10 ; [ADR-0005](0005-modele-de-domaine.md) ; [ADR-0013](0013-offres-collees.md) ; [ADR-0012](0012-restauration-d-une-sauvegarde.md)
- Story : <https://github.com/moonn1000/sollicito/issues/40> ; suites de #36 : <https://github.com/moonn1000/sollicito/issues/98>
- RFC 4180, « Common Format and MIME Type for Comma-Separated Values (CSV) Files » : <https://www.rfc-editor.org/rfc/rfc4180>

Consultés le 2026-11-24, des forums seulement, sans page de Microsoft :

- Microsoft Tech Community, séparateur `;` à l'export « CSV UTF-8 » : <https://techcommunity.microsoft.com/discussions/excelgeneral/semi-colon--column-separator-instead-of-comma--when-we-export-as-csv-utf-8-file-/414983/replies/485060>
- Peter Hilton, « CSV and Excel » (le BOM d'un CSV UTF-8) : <https://hilton.org.uk/blog/csv-excel>
