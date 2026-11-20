# ADR-0007 : nom d'entreprise corrigé par la modification, entreprises proposées

- **Statut** : proposée
- **Date** : 2026-11-20
- **Précise** : [ADR-0005](0005-modele-de-domaine.md), « Reconnaître une entreprise » (« Le nom affiché reste celui de la première saisie ») ; « Alternatives écartées » et « Conséquences » (l'écran de saisie propose les entreprises déjà enregistrées)

## Contexte

L'ADR-0005 reconnaît une entreprise à la clé de son nom. Le nom affiché y reste celui de la première saisie, et l'écran de saisie propose les entreprises déjà enregistrées, pour qu'un doublon se voie.

La modification d'une candidature (#25) reconnaît de nouveau le nom saisi. Telle quelle, la règle de l'ADR-0005 y laissait deux limites, que l'utilisateur verrait :

- un nom de même clé, écrit autrement, par exemple « Lumiere Verte » corrigé en « Lumière Verte SA », rattachait la candidature à la même entreprise sans changer son nom affiché : la correction ne se voyait pas ;
- une faute de frappe corrigée, qui changeait la clé, par exemple « Lumière Vert » corrigé en « Lumière Verte », créait une entreprise et laissait l'entreprise fautive, sans candidature, parmi les propositions de la saisie.

Le 2026-11-20, FRS a demandé de corriger ces deux limites dans #25.

## Décision

### Le nom affiché se corrige par l'écran de modification

Dans l'écran de modification d'une candidature, un nom de même clé qu'une entreprise enregistrée, mais écrit autrement, corrige le nom affiché de cette entreprise, pour toutes ses candidatures. La règle vaut aussi quand ce nom désigne une autre entreprise que celle de la candidature, qui la rejoint.

- **L'accord se donne à l'API** : la modification porte un champ d'accord (`corrigerLeNomDeLEntreprise`). Sans lui, l'API refuse la modification (409) sans rien changer, dans la même écriture exclusive que la modification elle-même. Son refus porte le nom enregistré, lu en base, et le nombre de candidatures concernées, celle qu'on modifie comprise ; il ne reprend jamais le texte saisi, et aucun journal n'écrit ces noms.
- **L'écran le dit avant l'enregistrement** : quel nom serait corrigé, en quoi, pour combien de candidatures, et si la candidature change d'entreprise. L'utilisateur corrige le nom, ce qui relance la modification avec l'accord, ou le garde : la candidature rejoint alors l'entreprise sous son nom enregistré, que rien ne renomme.

La saisie d'une candidature (#11) ne change jamais le nom affiché : elle reprend l'entreprise de même clé, sous le nom de sa première saisie, comme le dit l'ADR-0005.

### Les entreprises proposées sont celles qu'une candidature porte encore

Les écrans de saisie et de modification proposent, pendant qu'on tape, les entreprises enregistrées qu'une candidature porte encore. Une entreprise qu'aucune candidature ne porte plus, quittée par une modification ou dont la dernière candidature est supprimée, reste en base : sa clé la reconnaît toujours, et un nom de même clé, saisi de nouveau, la reprend. Elle n'est simplement plus proposée.

Les règles complètes sont dans le [modèle de domaine](../modele-de-domaine.md), « Reconnaître une entreprise » et « Modifier ou supprimer une candidature ».

## Alternatives écartées

- **Garder la règle de l'ADR-0005 telle quelle** : les deux limites restaient, et l'utilisateur ne pouvait corriger ni l'orthographe d'une entreprise ni une faute de frappe sans laisser de trace parmi les propositions.
- **Corriger le nom aussi à la saisie** : une saisie rapide, en minuscules ou sans accents, renommerait sans le dire l'entreprise de toutes les autres candidatures.
- **Corriger le nom sans avertir** : la correction change ce qu'affichent d'autres candidatures ; l'utilisateur doit la voir avant qu'elle ne se fasse.
- **Un aperçu par une route à part**, qui dirait avant l'envoi quel nom serait corrigé : une route de plus à protéger, et son nombre de candidatures pourrait changer avant l'enregistrement. Le refus de la modification elle-même porte un état lu dans l'écriture qui l'aurait faite.
- **Le nom saisi dans le refus** : l'écran le connaît déjà, et le modèle de menaces veut qu'un refus ne reprenne pas le texte de la requête.
- **Supprimer l'entreprise sans candidature** : la suppression ne se rattrape pas. Les offres (#36, #38) désigneront aussi des entreprises, sans candidature, et la clé doit continuer de les reconnaître.
- **Un écran de gestion des entreprises** (renommer, fusionner, retirer) : il règle tous les cas, mais ajoute un écran et ses règles à la V1 resserrée (ADR-0004) pour des corrections rares.

## Conséquences

- **Positives** : une orthographe corrigée se voit partout où l'entreprise s'affiche ; une faute de frappe corrigée ne laisse aucune entreprise fautive parmi les propositions ; aucune entreprise ne disparaît de la base.
- **Négatives** :
  - corriger le nom depuis une candidature change celui de toutes les autres : l'avertissement le dit, mais la correction ne se fait que depuis l'écran de modification ;
  - une entreprise sans candidature reste en base sans se voir ; seule sa clé la retrouve.

## À vérifier

- **Offres (#36, #38)** : une entreprise que seule une offre désigne n'est pas proposée selon cette règle. À trancher avec ces stories : la proposer aussi, ou non.

## Sources

- [ADR-0005](0005-modele-de-domaine.md), « Reconnaître une entreprise », « Alternatives écartées », « Conséquences »
- Story de la modification : <https://github.com/moonn1000/sollicito/issues/25>
- [Modèle de domaine](../modele-de-domaine.md)
