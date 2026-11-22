# ADR-0014 : postuler à une offre, le lien porté par la candidature, l'entreprise d'abord sur l'offre, la liste filtrée dans la page

- **Statut** : acceptée
- **Date** : 2026-11-22
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D5 (une offre fait l'objet d'au plus une candidature : le lien en base) ; [ADR-0004](0004-v1-resserree-paliers.md), « Ce qui attend la V2 » (la liste des offres se filtre dès la V1 ; la recherche et les filtres des candidatures restent en V2) ; [ADR-0005](0005-modele-de-domaine.md) et [ADR-0013](0013-offres-collees.md) (« Postuler » : l'entreprise et le poste de l'offre, l'offre sans entreprise, le doublon possible déjà postulé) ; [ADR-0007](0007-historique-en-vigueur-et-nom-corrige.md) (le nom corrigé d'une entreprise, qui s'affiche aussi dans ses offres)

## Contexte

La story #38 fait d'une offre une candidature, et liste les offres. Le [modèle de domaine](../modele-de-domaine.md) de #36 prévoyait déjà « Postuler » : la candidature reprend l'entreprise et le poste de l'offre, puis vit sa vie ; une offre sans entreprise en demande une avant ; une offre a au plus une candidature ; un doublon possible déjà postulé se montre ; supprimer la candidature laisse l'offre. Plusieurs faits, relevés le 2026-11-22, pèsent sur la façon de faire :

- **Le lien n'existait pas en base**, et la candidature, racine de son agrégat, désigne ce qu'elle vise par son identifiant, comme son entreprise.
- **Sous SQLite, Entity Framework Core ajoute une clé étrangère en reconstruisant la table**, en plusieurs transactions, les clés étrangères coupées entre deux. #25 et #14 ont évité cette reconstruction : la colonne porte sa clé dès son ajout, en une instruction écrite dans la migration.
- **La spec de la V1 (#5) range « la recherche et les filtres » en V2**, mais #38 demande une liste filtrable par source, par date et par texte. Une adresse peut finir dans un journal ou un historique : le texte cherché ne doit pas y passer.
- **La date de publication d'une offre est facultative**, sa date d'ajout toujours présente.
- **L'avertissement du nom corrigé** (ADR-0007) ne comptait que les candidatures, alors que les offres qui désignent l'entreprise affichent aussi son nom (#98, point 6).

## Décision

### Le lien, porté par la candidature

- Une colonne `Candidatures.OffreId`, nulle, avec sa clé étrangère vers `Offres` dans la même instruction `ALTER TABLE … ADD … REFERENCES`, et un index unique : une offre fait l'objet d'au plus une candidature, et la base elle-même refuse la seconde. SQLite admet plusieurs `NULL` dans un index unique : les candidatures saisies sans offre ne se gênent pas.
- **Une seule transaction, aucune table reconstruite** : les lignes des candidatures ne sont pas réécrites. Un test génère le script SQL de chaque migration et n'y trouve qu'une transaction, aucune table temporaire, et jamais les clés étrangères coupées.
- **`ON DELETE SET NULL`** : une offre supprimée, ce que la V1 ne fait jamais, laisserait sa candidature, sans offre. SQLite fige cette action dans le texte de la table : la changer demanderait la reconstruction évitée ici.
- **Supprimer une candidature emporte sa ligne, donc le lien** : l'offre reste, et peut en recevoir une nouvelle.

### « Postuler »

- `POST /api/offres/{id}/candidature`, dont le corps ne porte que la date et l'heure d'envoi (`envoyeeLe`), aux règles de #11. L'entreprise et le poste se lisent sur l'offre, en base, dans l'écriture exclusive qui vérifie le lien et crée la candidature : rien de périmé ne vient de l'écran. Un corps qui porte un autre champ, l'entreprise, le poste ou le statut compris, est illisible (400). La candidature part d'Envoyée.
- **Les refus**, dans cet ordre : l'offre inconnue (404) ; l'offre qui a déjà sa candidature (409, `dejaPostulee`, avec l'identifiant de cette candidature, lu en base) ; l'offre sans entreprise (409, `entrepriseManquante`) ; un poste d'offre que la candidature ne peut pas reprendre (409, `posteARevoir`), ce qu'une base restaurée, retouchée hors de Sollicito, peut seule porter ; puis la date refusée (400). Une date corrigée n'aiderait pas une offre déjà postulée. Aucun refus ne crée de candidature ni d'entreprise.
- **Deux « Postuler » simultanés** passent l'un après l'autre, par l'écriture exclusive : le second trouve la candidature du premier. L'index unique est le second rempart.
- **Le corps est borné à 16 Kio** (413), pour cette seule route ; les autres routes gardent la leur.
- **Ensuite, chacun vit sa vie** : corriger l'offre ne change pas sa candidature, modifier la candidature (#25) ne change pas son offre, et ni l'un ni l'autre ne défait le lien.
- **La fiche d'une candidature** porte l'identifiant de son offre, et y mène ; celle d'une offre montre sa candidature, et y mène.

### L'offre sans entreprise

Elle reçoit d'abord son entreprise par sa correction (#36, ADR-0013) : reconnue par sa clé ou créée, règle 3 refaite avec son accord. Sa fiche le dit à la place de « Postuler », et mène à la correction. L'offre et sa candidature désignent ainsi la même entreprise, et « Postuler » n'a qu'un champ.

### Le doublon possible déjà postulé

Chaque doublon possible porte sa candidature, s'il en a une. La fiche d'une offre le marque comme déjà postulé, et l'écran « Postuler » le rappelle avant l'envoi, sans bloquer : postuler deux fois dans une même entreprise, pour deux offres semblables, reste permis.

### La liste des offres, filtrée dans la page

- **`GET /api/offres`**, sans aucun paramètre, rend toutes les offres, sans leur texte, avec leur candidature : les plus récemment ajoutées d'abord, puis par ordre d'enregistrement. Aucune borne : une borne sous un filtre fait dans la page ferait manquer des offres sans le dire, et une ligne sans texte pèse quelques centaines d'octets.
- **Les filtres se font dans l'interface, sur la liste déjà lue** : rien de la recherche ne quitte la page, ni dans une adresse, ni dans un corps, ni dans un journal.
  - le texte cherché, de 200 caractères au plus, porte sur le poste, l'entreprise et le lieu, sans tenir compte des accents, des majuscules ni des blancs ; chacun de ses mots doit se trouver dans l'un des trois ;
  - la source : toutes, ou l'une d'elles ;
  - la date d'ajout : toutes, ou les 7, 30 ou 90 derniers jours, en date locale, aujourd'hui compris.
- **Les filtres restent le temps de la session**, en mémoire, quand on ouvre une fiche puis qu'on revient ; ils ne s'écrivent nulle part.

### Le nom corrigé d'une entreprise

Le refus de la modification qui corrigerait le nom d'une entreprise (409, ADR-0007) compte aussi les offres qui la désignent, et l'avertissement les nomme : « … pour ses 3 candidatures, celle-ci comprise, et pour les 2 offres qui la désignent. »

## Alternatives écartées

- **Le lien porté par l'offre** (`Offres.CandidatureId`) : la candidature désigne ce qu'elle vise, comme son entreprise, et la colonne aurait demandé de même une clé ajoutée sans reconstruire la table des offres.
- **Une table de liens** entre offres et candidatures : une table de plus, et deux index uniques, pour un lien qu'une colonne porte.
- **La clé ajoutée par Entity Framework Core** (`AddForeignKey`) : la table des candidatures reconstruite, en plusieurs transactions, les clés étrangères coupées entre deux ; un arrêt au milieu laisserait une table temporaire.
- **`RESTRICT`** : une offre qui a sa candidature ne pourrait pas se supprimer sans la détacher d'abord. La V1 ne supprime aucune offre ; la candidature, elle, vit sa vie.
- **Un formulaire « Postuler » prérempli et modifiable**, entreprise et poste compris : un préremplissage peut être périmé, l'entreprise de la candidature différerait de celle de l'offre dès sa création, et le corps strict aurait trois champs à garder au lieu d'un.
- **L'entreprise saisie dans « Postuler », pour la seule candidature** : l'offre resterait anonyme, et la liste la montrerait « Entreprise non indiquée » alors que sa candidature nomme une entreprise.
- **L'entreprise saisie dans « Postuler » et posée aussi sur l'offre** : la règle 3 se rejouerait dans « Postuler », avec son accord, soit un refus et un « Garder les deux » de plus.
- **Un accord devant un doublon possible déjà postulé** : un clic de plus à chaque fois, pour un cas que l'écran montre déjà.
- **Une recherche par l'API**, `POST /api/offres/recherche`, critères dans le corps, qui aurait aussi cherché dans le texte collé : une route d'écriture pour une lecture, une recherche sans accents à faire sur tous les textes, une borne des résultats, des journaux de plus à garder.
- **Des paramètres dans l'adresse de la liste** : une adresse peut finir dans un journal ou un historique.
- **Une borne de la liste** : sous un filtre fait dans la page, elle ferait manquer des offres sans le dire.
- **Filtrer sur la date de publication** : facultative, elle cacherait toutes les offres qui n'en ont pas.

## Conséquences

- **Positives** : une offre ne reçoit jamais deux candidatures, même sous deux clics simultanés ; la migration ne réécrit aucune ligne et tient en une transaction ; la candidature naît dans l'entreprise de l'offre ; rien de la recherche ne sort de la page ; l'avertissement du nom corrigé dit tout ce qui change.
- **Négatives** :
  - la recherche ne lit pas le texte collé, seulement le poste, l'entreprise et le lieu ;
  - sans borne, la liste se lit en entier à chaque ouverture ;
  - une offre anonyme demande deux gestes de plus avant de postuler ;
  - une candidature saisie sans offre ne se rattache pas, après coup, à l'offre qu'on colle ensuite ;
  - l'action `ON DELETE SET NULL` est figée dans le texte de la table.

## À vérifier

- **La recherche dans le texte collé** : l'usage la demande-t-il ? À juger à la revue du palier 3 (#68).
- **Rattacher une candidature déjà saisie à une offre** : hors de la V1, à juger avec la recherche.

## Sources

- [ADR-0001](0001-cadrage-architecture-v1.md), D5 ; [ADR-0005](0005-modele-de-domaine.md) ; [ADR-0007](0007-historique-en-vigueur-et-nom-corrige.md) ; [ADR-0013](0013-offres-collees.md)
- Story : <https://github.com/moonn1000/sollicito/issues/38> ; suites de #36 : <https://github.com/moonn1000/sollicito/issues/98>
- Migrations de #25 (`CorrectionsDesEtapes`) et de #14 (`RelancesEtContacts`) : la colonne qui porte sa clé dès son ajout.
