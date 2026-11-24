# ADR-0005 : modèle de domaine, dédoublonnage et dates

- **Statut** : acceptée ; la correction par ajout de « Corriger un statut » étendue aux étapes, le nom affiché de « Reconnaître une entreprise », et les entreprises que l'écran de saisie propose (« Alternatives écartées », « Conséquences »), précisés par l'[ADR-0007](0007-historique-en-vigueur-et-nom-corrige.md) (étapes corrigées et historique en vigueur, nom d'entreprise corrigé et entreprises proposées) ; « Dédoublonner les offres » précisé par l'[ADR-0013](0013-offres-collees.md) (l'empreinte pas à pas, l'accord devant un doublon possible, le poste normalisé), puis par l'[ADR-0014](0014-postuler-a-une-offre.md) (« Postuler » : l'entreprise et le poste repris de l'offre, l'offre sans entreprise, le doublon possible déjà postulé montré), puis par l'[ADR-0015](0015-import-csv-des-offres.md) (le sort d'une ligne de CSV : la règle 2 écartée sans accord, le doublon possible gardé ligne à ligne, l'empreinte sur les champs lus)
- **Date** : 2026-11-18
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D5 : la reconnaissance d'une entreprise, et les deux cas de dédoublonnage des offres que la D5 renvoyait au premier sprint
- **Remplace en partie** : [ADR-0001](0001-cadrage-architecture-v1.md), D5, son esquisse du modèle, que remplace le [modèle de domaine](../modele-de-domaine.md)

## Contexte

La D5 de l'ADR-0001 fixe huit entités et en esquisse les liens. Le couple (source, identifiant externe) y dédoublonne les offres d'une même source ; deux cas restaient à préciser au premier sprint : une même offre vue sur deux sources, et une offre collée sans identifiant. La spec de la V1 (#5) remplace les sprints par des paliers : ces points se règlent au palier 1, celui de #6.

Les stories qui suivent demandent davantage :

- l'enregistrement d'une candidature (#11) reprend une entreprise déjà enregistrée, « sans doublon, selon la règle de #6 » ;
- une candidature se saisit sans offre (#11), et une offre collée ou importée ne dit pas toujours son entreprise (#36, #40) : deux liens de l'esquisse sont trop stricts ;
- l'échéance d'une relance se calcule depuis la date d'envoi (#14), et #6 demande de fixer la façon de garder les dates et les heures, passage à l'heure d'été compris.

En V1, les offres n'arrivent que par deux adaptateurs du connecteur commun, le texte collé (#36) et le fichier CSV (#40) ; le Forem attend la V2. Six entités entrent en base ; Profil et Document attendent la V2 (spec de la V1, « Offres » et « Entités en base »).

## Décision

### Le modèle de domaine remplace l'esquisse de la D5

Le [modèle de domaine](../modele-de-domaine.md) garde les huit entités et les liens de l'esquisse, sauf trois :

- **Candidature et Entreprise** : un lien direct, que l'esquisse n'avait pas. Une candidature a toujours son entreprise, offre ou pas.
- **Candidature et Offre** : une candidature a zéro ou une offre, et non exactement une. Une offre fait toujours l'objet d'au plus une candidature.
- **Offre et Entreprise** : une offre a zéro ou une entreprise, et non exactement une.

Le modèle fixe aussi les statuts d'une candidature et leurs passages, les trois sortes d'événement de candidature, gardées dans une seule table, et les règles des relances.

### Reconnaître une entreprise

Une entreprise se reconnaît à une clé tirée de son nom : accents retirés, minuscules, points retirés, autre ponctuation remplacée par des espaces, espaces réduits. Les formes juridiques se retirent ensuite à la fin du nom (SA, NV, SRL, BV, ASBL, VZW…) et, pour les seules formes belges qui ne commencent jamais une marque (ASBL, VZW, SRL, SPRL, BVBA…), au début. Même clé, même entreprise : la saisie reprend l'entreprise enregistrée. Le nom affiché reste celui de la première saisie. L'algorithme complet et ses exemples sont dans le modèle de domaine, « Reconnaître une entreprise ».

### Dédoublonner les offres

1. **Identifiant sûr** : le couple (source, identifiant externe) de la D5 reste la règle d'une source qui en fournit un. Aucune ne le fait en V1 ; le Forem le fera en V2.
2. **Offre sans identifiant**, le second cas de la D5 : le couple (source, empreinte) la reconnaît. L'empreinte est le SHA-256 de son texte normalisé (Unicode NFKC, minuscules, blancs réduits). Recoller le même texte retrouve l'offre enregistrée. Une ligne de CSV se traite de même : chaque champ, privé de ses caractères de contrôle autres que les blancs, se normalise à part, puis les champs se joignent par U+001F, un séparateur qui n'est pas un blanc.
3. **Même offre vue sur deux sources**, le premier cas : jamais de fusion automatique. Une offre qui a la même entreprise et le même poste normalisé qu'une offre enregistrée, quelle que soit sa source, est signalée comme doublon possible ; l'utilisateur garde les deux, ou renonce à la nouvelle.

### Corriger un statut

Une candidature ne change de statut que par les passages du diagramme d'états, ou en corrigeant son dernier changement de statut. L'historique se fait par ajouts seulement : la correction ajoute un changement de statut daté, qui ramène la candidature au statut d'avant et désigne le changement corrigé. Celui-ci reste dans l'historique, marqué comme corrigé. Répétée, la correction remonte jusqu'à Envoyée ; une correction ne se corrige pas. Elle répare une erreur de clic, et reprend une candidature close, par exemple quand une réponse arrive après « Sans réponse ». Elle se range dans la même table que les autres événements : la V1 garde ses six entités en base.

### Garder les dates et les heures

- Un instant (envoi, événement, import) se garde en UTC ; une date du calendrier (échéance, date de publication), sans heure ni fuseau.
- Le fuseau est celui de Windows. L'API convertit seule les heures saisies : une heure qui n'existe pas, au passage à l'heure d'été, est refusée ; une heure qui existe deux fois, au retour à l'heure d'hiver, se lit à l'heure d'hiver[^dotnet-fuseaux].
- Les calculs en jours, dont l'échéance d'une relance, se font sur la date locale, jamais en heures.

L'exemple chiffré est dans le modèle de domaine, « Dates et heures ».

## Alternatives écartées

- **Rapprocher les noms d'entreprise de façon approchée** (distance d'édition, phonétique) : « Lumière Verte » et « Lumière Verte Belgique » se rejoindraient, mais aussi deux entreprises voisines. Une fusion à tort mêle les candidatures de deux entreprises sans que rien ne la montre ; un doublon se voit, et l'écran de saisie propose les entreprises déjà enregistrées.
- **Le numéro d'entreprise (BCE) comme clé** : on le connaît rarement en saisissant une candidature, et un texte d'offre le donne rarement.
- **Retirer toutes les formes juridiques au début du nom** : un nom qui commence par des initiales, comme « AG Logistique » ou « SC Durand », perdrait son début.
- **Fusionner automatiquement une même offre vue sur deux sources** : une entreprise publie souvent le même intitulé pour deux postes, dans deux villes ou deux équipes ; une fusion à tort ferait disparaître une offre. Il faudrait aussi choisir quel texte garder.
- **Une entité « annonce »**, une offre et ses parutions, une par source : c'est le bon modèle quand des sources automatiques publieront les mêmes offres. En V1, où chaque offre arrive à la main, elle ajouterait une neuvième entité et ses écrans pour un cas rare.
- **L'adresse de l'offre comme identifiant** : une même offre a une adresse par site, et une adresse change avec ses paramètres de suivi.
- **Aucun dédoublonnage des offres collées** : coller deux fois la même offre en créerait deux.
- **Rouvrir une candidature close par un passage de plus**, vers son statut d'avant : la trace resterait, mais une erreur sur un passage qui fait avancer (Envoyée vers Entretien) ne se corrigerait pas, et dix flèches de retour, une par passage vers un statut clos, rendraient le diagramme d'états illisible.
- **Des passages en arrière entre statuts en cours** : autant de flèches et de boutons de plus, pour des erreurs rares.
- **Effacer le changement corrigé** : plus simple, mais l'historique perdrait une étape de la démarche, alors qu'il se fait par ajouts seulement.
- **L'heure locale, sans fuseau** : l'heure qui existe deux fois à l'automne resterait ambiguë, et les heures changeraient de sens si le fuseau du poste changeait.
- **`DateTimeOffset`** : avec SQLite, EF Core ne traduit ni ses comparaisons ni ses tris en SQL, qu'il faudrait faire dans l'application ; sa documentation recommande un `DateTime` converti en UTC[^ef-sqlite].
- **Une échéance en instant**, l'envoi plus 7 × 24 heures : elle tombe un jour trop tard quand le passage à l'heure d'été se place entre les deux.
- **Convertir les heures dans l'interface** : la règle vivrait en deux endroits, et le navigateur en applique une autre. Relevé le 2026-11-18 dans Chromium : il avance d'une heure, sans erreur, une heure qui n'existe pas (le 29/03/2026 à 02:30 devient 03:30), et lit à l'heure d'été une heure qui existe deux fois.

## Conséquences

- **Positives** : des règles déterministes, sans réglage, que les tests unitaires du domaine vérifient (#11, #12, #36) ; aucune fusion à tort ; des heures justes de part et d'autre des changements d'heure ; le couple de la D5 prêt pour le Forem.
- **Négatives** :
  - deux noms différents d'une même entreprise font deux entreprises (« Lumière Verte » et « Lumière Verte Belgique », « NV Lumière Verte SA » et « Lumière Verte ») : l'écran de saisie doit proposer les entreprises déjà enregistrées (#11) ;
  - un doublon possible se tranche à la main, et deux postes de même intitulé dans une même entreprise seront signalés à tort ;
  - une offre recollée depuis un autre site, au texte différent, n'est pas reconnue : seule la règle 3 la signale ;
  - une erreur de clic corrigée laisse deux lignes dans l'historique : le changement, marqué comme corrigé, et sa correction ;
  - une candidature saisie après coup prend ses statuts à la date de la saisie : la vraie date d'un entretien passé se garde dans une étape ;
  - une heure saisie dans le trou du passage à l'heure d'été est refusée, et l'utilisateur doit la corriger ;
  - le texte d'une offre ne change plus après l'import, sans quoi son empreinte ne vaudrait plus.

## À vérifier

Points relevés le 2026-11-18, à régler par un nouvel ADR si besoin :

- **Doublons possibles** : la paire (entreprise, poste) signale-t-elle trop souvent à tort ? À juger à l'usage, à la revue du palier 3 (#68). Y ajouter le lieu est la piste.
- **Forem (V2)** : quand une source automatique publiera les offres que l'on colle aussi, revoir l'entité « annonce » écartée ici, et le numéro d'entreprise, si ses données le portent.

## Sources

- [ADR-0001](0001-cadrage-architecture-v1.md), D5 et D6
- Spec de la V1 : <https://github.com/moonn1000/sollicito/issues/5>
- Story du modèle de domaine : <https://github.com/moonn1000/sollicito/issues/6>
- Stories qui s'appuient sur ces règles : [#11](https://github.com/moonn1000/sollicito/issues/11), [#14](https://github.com/moonn1000/sollicito/issues/14), [#36](https://github.com/moonn1000/sollicito/issues/36), [#38](https://github.com/moonn1000/sollicito/issues/38), [#40](https://github.com/moonn1000/sollicito/issues/40)

Consultées le 2026-11-18 :

[^ef-sqlite]: Microsoft Learn, EF Core, limites du fournisseur SQLite : <https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations>
[^dotnet-fuseaux]: Microsoft Learn, .NET, vue d'ensemble des fuseaux horaires (heures invalides et ambiguës) : <https://learn.microsoft.com/en-us/dotnet/standard/datetime/time-zone-overview> ; résoudre une heure ambiguë : <https://learn.microsoft.com/en-us/dotnet/standard/datetime/resolve-ambiguous-times>
