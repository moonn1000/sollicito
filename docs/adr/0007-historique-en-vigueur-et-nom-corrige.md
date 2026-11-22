# ADR-0007 : étapes corrigées et historique en vigueur, nom d'entreprise corrigé et entreprises proposées

- **Statut** : acceptée ; « À vérifier » (les offres) tranché par l'[ADR-0013](0013-offres-collees.md) : une entreprise que seule une offre désigne est proposée
- **Date** : 2026-11-20
- **Précise** : [ADR-0005](0005-modele-de-domaine.md), « Corriger un statut » (la correction par ajout, étendue aux étapes) ; « Reconnaître une entreprise » (« Le nom affiché reste celui de la première saisie ») ; « Alternatives écartées » et « Conséquences » (l'écran de saisie propose les entreprises déjà enregistrées)

## Contexte

La modification d'une candidature (#25) corrige son entreprise, son poste et sa date d'envoi. Deux familles de règles l'encadraient.

**L'historique et sa date de départ.** L'ADR-0005 corrige un statut par ajout : la correction désigne le changement corrigé, qui reste dans l'historique ; une correction ne se corrige pas. Une étape, elle, ne se corrigeait pas du tout. Le [modèle de domaine](../modele-de-domaine.md) de #12 posait aussi :

- « L'envoi n'est pas un événement : la candidature le date elle-même, et son historique commence là » ;
- « une étape peut être passée ou à venir, mais elle ne précède pas l'envoi » ;
- pour #25, une date d'envoi corrigée qui ne passerait pas après une étape déjà notée.

Une étape mal datée bornait donc la date d'envoi pour de bon : rien ne la corrigeait, et seule la suppression de la candidature levait la borne. #82, point 3, relevait en outre qu'un changement de statut pouvait lui aussi précéder une date d'envoi corrigée.

**Le nom des entreprises.** L'ADR-0005 reconnaît une entreprise à la clé de son nom. Le nom affiché y reste celui de la première saisie, et l'écran de saisie propose les entreprises déjà enregistrées, pour qu'un doublon se voie. Reconnu de nouveau par la modification, un nom laissait deux limites :

- un nom de même clé, écrit autrement, par exemple « Lumiere Verte » corrigé en « Lumière Verte SA », rattachait la candidature à la même entreprise sans changer son nom affiché : la correction ne se voyait pas ;
- une faute de frappe corrigée, qui changeait la clé, par exemple « Lumière Vert » corrigé en « Lumière Verte », créait une entreprise et laissait l'entreprise fautive, sans candidature, parmi les propositions de la saisie.

Le 2026-11-20, FRS a demandé de lever ces limites dans #25.

## Décision

### Une étape se corrige, par ajout

Une étape se corrige depuis la fiche, comme un statut : l'historique ne perd rien.

- La correction est une étape à son tour, aux mêmes règles et aux mêmes champs (type, date et heure, note facultative). Elle s'ajoute à l'historique, à sa date, et désigne l'étape qu'elle corrige, qui y reste, marquée comme corrigée. Le statut ne change pas.
- Une correction d'étape se corrige à son tour, contrairement à celle d'un statut. On corrige toujours la version en vigueur, la dernière de la chaîne ; une demande sur une version déjà corrigée, ou sur une étape qui n'existe pas, est refusée (409), et ne corrige rien de plus.
- La correction se range dans la même table que les autres événements, avec une colonne à part qui désigne l'étape corrigée : unique, puisqu'une étape n'est corrigée qu'une fois. La V1 garde ses six entités en base.

### L'historique en vigueur commence à l'envoi

- **En vigueur** : un événement qu'aucune correction ne désigne. Un changement de statut corrigé, ou une étape corrigée, reste dans l'historique, marqué comme corrigé, mais n'est plus en vigueur.
- **La date d'envoi corrigée** ne passe pas après le seul premier événement en vigueur de l'historique, quelle que soit sa sorte : étape, changement de statut ou correction. Un événement corrigé ne compte plus ; à l'instant même du premier événement en vigueur, la date est admise. Renvoyée inchangée, à la minute près, elle garde l'instant enregistré, sans repasser par les règles.
- **Une étape, ou sa correction,** n'est jamais datée avant l'envoi : notée ou corrigée, elle le suit, comme le voulait #12.
- **La fiche range l'envoi à son instant** dans l'historique, au premier rang à égalité. Seuls des événements corrigés peuvent le précéder, quand une date d'envoi corrigée est passée après eux.

Ces règles remplacent, dans le modèle de domaine, « son historique commence là » et la borne d'une étape déjà notée, corrigée ou non.

### Le nom affiché se corrige par l'écran de modification

Dans l'écran de modification d'une candidature, un nom de même clé qu'une entreprise enregistrée, mais écrit autrement, corrige le nom affiché de cette entreprise, pour toutes ses candidatures. La règle vaut aussi quand ce nom désigne une autre entreprise que celle de la candidature, qui la rejoint.

- **L'accord se donne à l'API** : la modification porte un champ d'accord (`corrigerLeNomDeLEntreprise`). Sans lui, l'API refuse la modification (409) sans rien changer, dans la même écriture exclusive que la modification elle-même. Son refus porte le nom enregistré, lu en base, et le nombre de candidatures concernées, celle qu'on modifie comprise ; il ne reprend jamais le texte saisi, et aucun journal n'écrit ces noms.
- **L'écran le dit avant l'enregistrement** : quel nom serait corrigé, en quoi, pour combien de candidatures, et si la candidature change d'entreprise. L'utilisateur corrige le nom, ce qui relance la modification avec l'accord, ou le garde : la candidature rejoint alors l'entreprise sous son nom enregistré, que rien ne renomme.

La saisie d'une candidature (#11) ne change jamais le nom affiché : elle reprend l'entreprise de même clé, sous le nom de sa première saisie, comme le dit l'ADR-0005.

### Les entreprises proposées sont celles qu'une candidature porte encore

Les écrans de saisie et de modification proposent, pendant qu'on tape, les entreprises enregistrées qu'une candidature porte encore. Une entreprise qu'aucune candidature ne porte plus, quittée par une modification ou dont la dernière candidature est supprimée, reste en base : sa clé la reconnaît toujours, et un nom de même clé, saisi de nouveau, la reprend. Elle n'est simplement plus proposée.

Les règles complètes sont dans le [modèle de domaine](../modele-de-domaine.md) : « Événement de candidature », « Corriger une étape », « Reconnaître une entreprise » et « Modifier ou supprimer une candidature ».

## Alternatives écartées

- **Garder les règles telles quelles** : une étape mal datée bornait la date d'envoi pour de bon, et l'utilisateur ne pouvait corriger ni l'orthographe d'une entreprise ni une faute de frappe sans laisser de trace parmi les propositions.
- **Modifier une étape en place, ou la supprimer** : plus simple, mais l'historique perdrait ce qui a été noté, alors qu'il se fait par ajouts seulement (ADR-0005).
- **Laisser les événements corrigés borner la date d'envoi** : une étape corrigée, gardée dans l'historique pour mémoire, continuerait d'empêcher la correction qu'elle appelle.
- **Garder l'envoi en tête de l'historique** quel que soit son instant : une étape corrigée plus ancienne s'afficherait après lui, et les dates de la fiche reculeraient.
- **Corriger le nom aussi à la saisie** : une saisie rapide, en minuscules ou sans accents, renommerait sans le dire l'entreprise de toutes les autres candidatures.
- **Corriger le nom sans avertir** : la correction change ce qu'affichent d'autres candidatures ; l'utilisateur doit la voir avant qu'elle ne se fasse.
- **Un aperçu par une route à part**, qui dirait avant l'envoi quel nom serait corrigé : une route de plus à protéger, et son nombre de candidatures pourrait changer avant l'enregistrement. Le refus de la modification elle-même porte un état lu dans l'écriture qui l'aurait faite.
- **Le nom saisi dans le refus** : l'écran le connaît déjà, et le modèle de menaces veut qu'un refus ne reprenne pas le texte de la requête.
- **Supprimer l'entreprise sans candidature** : la suppression ne se rattrape pas. Les offres (#36, #38) désigneront aussi des entreprises, sans candidature, et la clé doit continuer de les reconnaître.
- **Un écran de gestion des entreprises** (renommer, fusionner, retirer) : il règle tous les cas, mais ajoute un écran et ses règles à la V1 resserrée (ADR-0004) pour des corrections rares.

## Conséquences

- **Positives** : une étape mal notée se corrige sans rien perdre, et ne borne plus la date d'envoi ; une orthographe corrigée se voit partout où l'entreprise s'affiche ; une faute de frappe corrigée ne laisse aucune entreprise fautive parmi les propositions ; aucune entreprise ne disparaît de la base.
- **Négatives** :
  - une étape corrigée peut s'afficher avant l'envoi : elle n'est plus en vigueur, et la fiche range l'envoi à son instant ;
  - la correction d'un changement de statut, datée par l'horloge et qui ne se corrige pas, borne toujours la date d'envoi ;
  - chaque correction d'étape ajoute une ligne à l'historique, comme celle d'un statut ;
  - corriger le nom depuis une candidature change celui de toutes les autres : l'avertissement le dit, mais la correction ne se fait que depuis l'écran de modification ;
  - une entreprise sans candidature reste en base sans se voir ; seule sa clé la retrouve.

## À vérifier

- **Offres (#36, #38)** : une entreprise que seule une offre désigne n'est pas proposée selon cette règle. À trancher avec ces stories : la proposer aussi, ou non.

## Sources

- [ADR-0005](0005-modele-de-domaine.md), « Corriger un statut », « Reconnaître une entreprise », « Alternatives écartées », « Conséquences »
- Story de la modification : <https://github.com/moonn1000/sollicito/issues/25>
- Constats de #12, point 3 (date d'envoi corrigée et historique) : <https://github.com/moonn1000/sollicito/issues/82>
- [Modèle de domaine](../modele-de-domaine.md)
