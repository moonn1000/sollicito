# Cas d'utilisation de la V1

- **Mis à jour le** : 2026-11-21, avec #14
- **Décisions** : [ADR-0004](adr/0004-v1-resserree-paliers.md) (ce que livre la V1, ses paliers, sa règle de coupe) ; [ADR-0001](adr/0001-cadrage-architecture-v1.md), D7 et D8 (IA facultative, l'humain valide chaque envoi) ; [ADR-0011](adr/0011-relances-et-brouillon.md) (relances, contacts, brouillon et délai par défaut)
- **Périmètre** : ce que la personne en recherche d'emploi fait dans Sollicito en V1, palier par palier. Les règles de chaque cas sont dans le [modèle de domaine](modele-de-domaine.md), leurs protections dans le [modèle de menaces](modele-de-menaces.md) ; les systèmes extérieurs sont ceux des [vues d'architecture](vues-d-architecture.md).

Chaque cas porte le palier qui le livre, de P1 à P5 (ADR-0004, « Paliers ») ; l'avancement se suit dans les [jalons](https://github.com/moonn1000/sollicito/milestones). Le diagramme suit la syntaxe bêta des cas d'utilisation de Mermaid 12 ; le tableau qui le suit fait foi.

## Diagramme

```mermaid
---
config:
  layout: dagre
---
usecase-beta
direction LR
actor personne("Personne en recherche d'emploi")
actor messagerie("Messagerie de l'utilisateur")
actor fournisseur("Fournisseur d'IA : IA en ligne ou Ollama")
systemBoundary lancement["Lancement"]@{ type: package }
  lancer("Lancer Sollicito sans rien installer") <<P1>>
  telecharger("Télécharger l'exe et vérifier son origine") <<P3, P5>>
end
systemBoundary candidatures["Candidatures"]@{ type: package }
  enregistrer("Enregistrer une candidature") <<P2>>
  tableau("Retrouver ses candidatures au tableau de bord") <<P2>>
  avancer("Faire avancer le statut d'une candidature") <<P2>>
  noter("Noter un entretien ou un échange ‡") <<P2>>
  historique("Relire l'historique daté d'une candidature") <<P2>>
  modifier("Modifier une candidature") <<P2>>
  supprimer("Supprimer une candidature") <<P2>>
end
systemBoundary relances["Relances et contacts"]@{ type: package }
  suivre("Suivre ses relances à échéance") <<P3>>
  brouillon("Ouvrir le brouillon d'une relance") <<P3>>
  contact("Tenir les contacts d'une entreprise") <<P3>>
  delai("Régler le délai par défaut des relances") <<P3>>
end
systemBoundary offres["Offres"]@{ type: package }
  coller("Coller le texte d'une offre") <<P3>>
  csv("Importer un fichier CSV d'offres ‡") <<P3>>
  postuler("Faire d'une offre une candidature") <<P3>>
end
systemBoundary iaf["IA facultative"]@{ type: package }
  choisir("Choisir son fournisseur d'IA, ou aucun : Claude, OpenAI ‡, Ollama") <<P4>>
  remplir("Faire remplir la fiche d'une offre collée") <<P4>>
  rediger("Faire rédiger un brouillon de relance ‡") <<P4>>
end
systemBoundary donnees["Données"]@{ type: package }
  sauvegarder("Sauvegarder la base automatiquement") <<P2>>
  restaurer("Restaurer une sauvegarde") <<P3>>
  exporter("Exporter toutes ses données : JSON, CSV ‡") <<P5>>
end
personne -- lancer
personne -- telecharger
personne -- enregistrer
personne -- tableau
personne -- avancer
personne -- noter
personne -- historique
personne -- modifier
personne -- supprimer
personne -- suivre
personne -- brouillon
personne -- contact
personne -- delai
personne -- coller
personne -- csv
personne -- postuler
personne -- choisir
personne -- remplir
personne -- rediger
personne -- sauvegarder
personne -- restaurer
personne -- exporter
brouillon -- messagerie
remplir -- fournisseur
rediger -- fournisseur
remplir ..> : extend coller
rediger ..> : extend brouillon
postuler ..> : include enregistrer
```

| Légende | Sens |
| --- | --- |
| Bonhomme | un acteur : la personne en recherche d'emploi, qui se sert de Sollicito, ou un système extérieur qui prend part au cas |
| Ellipse | un cas d'utilisation |
| Paquetage | un domaine de Sollicito ; ensemble, ils forment le système |
| «P1» à «P5» | le palier qui livre le cas |
| ‡ | une des cinq fonctions que la règle de coupe peut faire passer en V2 (ADR-0004, « Règle de coupe ») |
| Trait plein | un acteur prend part au cas |
| Trait pointillé «extend» | un cas facultatif, qui en complète un autre ; la flèche va vers le cas complété |
| Trait pointillé «include» | un cas qui en contient toujours un autre ; la flèche va vers le cas contenu |

## Palier par palier

| Domaine | Cas d'utilisation | Story | Palier |
| --- | --- | --- | --- |
| Lancement | Lancer Sollicito sans rien installer : la fenêtre s'ouvre sur l'interface, et le dossier de données se crée seul ; la base s'y crée à son tour au palier 2 (#11) | [#9](https://github.com/moonn1000/sollicito/issues/9), [#13](https://github.com/moonn1000/sollicito/issues/13) | P1 |
| Lancement | Télécharger l'exe et vérifier son origine : empreinte SHA-256 et attestation de provenance, puis le guide « Démarrer » | [#54](https://github.com/moonn1000/sollicito/issues/54), [#60](https://github.com/moonn1000/sollicito/issues/60) | P3, P5 |
| Candidatures | Enregistrer une candidature : entreprise, poste, date et heure d'envoi | [#11](https://github.com/moonn1000/sollicito/issues/11) | P2 |
| Candidatures | Retrouver ses candidatures au tableau de bord | [#11](https://github.com/moonn1000/sollicito/issues/11) | P2 |
| Candidatures | Faire avancer le statut d'une candidature, et corriger le dernier changement | [#12](https://github.com/moonn1000/sollicito/issues/12) | P2 |
| Candidatures | Noter un entretien ou un échange ‡, et corriger une étape | [#12](https://github.com/moonn1000/sollicito/issues/12), [#25](https://github.com/moonn1000/sollicito/issues/25) | P2 |
| Candidatures | Relire l'historique daté d'une candidature | [#12](https://github.com/moonn1000/sollicito/issues/12) | P2 |
| Candidatures | Modifier une candidature : entreprise, poste, date d'envoi, et le nom d'une entreprise | [#25](https://github.com/moonn1000/sollicito/issues/25) | P2 |
| Candidatures | Supprimer une candidature | [#25](https://github.com/moonn1000/sollicito/issues/25) | P2 |
| Relances et contacts | Suivre ses relances à échéance : planifier une relance, la changer, voir celles qui sont échues ou de la semaine, la marquer faite ou abandonnée | [#14](https://github.com/moonn1000/sollicito/issues/14) | P3 |
| Relances et contacts | Ouvrir le brouillon d'une relance dans sa messagerie, que l'utilisateur envoie lui-même | [#14](https://github.com/moonn1000/sollicito/issues/14) | P3 |
| Relances et contacts | Tenir les contacts d'une entreprise : nom, adresse e-mail ; les ajouter, les corriger, les supprimer | [#14](https://github.com/moonn1000/sollicito/issues/14) | P3 |
| Relances et contacts | Régler le délai par défaut des relances, dans les réglages | [#14](https://github.com/moonn1000/sollicito/issues/14) | P3 |
| Offres | Coller le texte d'une offre, compléter sa fiche et la corriger, son texte gardé tel quel | [#36](https://github.com/moonn1000/sollicito/issues/36) | P3 |
| Offres | Importer un fichier CSV d'offres ‡ | [#40](https://github.com/moonn1000/sollicito/issues/40) | P3 |
| Offres | Parcourir les offres et faire d'une offre une candidature | [#38](https://github.com/moonn1000/sollicito/issues/38) | P3 |
| IA facultative | Choisir son fournisseur d'IA, ou aucun : une clé Claude ou OpenAI ‡, chiffrée, ou un modèle local Ollama | [#43](https://github.com/moonn1000/sollicito/issues/43), [#48](https://github.com/moonn1000/sollicito/issues/48), [#45](https://github.com/moonn1000/sollicito/issues/45) | P4 |
| IA facultative | Faire remplir par l'IA la fiche d'une offre collée, relue avant l'enregistrement | [#46](https://github.com/moonn1000/sollicito/issues/46) | P4 |
| IA facultative | Faire rédiger par l'IA un brouillon de relance ‡, relu avant l'ouverture dans la messagerie | [#47](https://github.com/moonn1000/sollicito/issues/47) | P4 |
| Données | Sauvegarder la base automatiquement : les sept dernières sauvegardes, plus une avant chaque migration | [#26](https://github.com/moonn1000/sollicito/issues/26) | P2 |
| Données | Restaurer une sauvegarde | [#28](https://github.com/moonn1000/sollicito/issues/28) | P3 |
| Données | Exporter toutes ses données, en JSON ou en CSV ‡ | [#49](https://github.com/moonn1000/sollicito/issues/49) | P5 |

Sans IA configurée, tous les cas hors du domaine « IA facultative » fonctionnent (ADR-0001, D7). Aucun cas n'envoie quoi que ce soit à la place de l'utilisateur : le brouillon d'une relance s'ouvre dans sa messagerie, et c'est lui qui l'envoie (ADR-0001, D8).

## Ce qui attend la V2

Ces cas ne sont pas dessinés : ils attendent la V2 (ADR-0004, « Ce qui attend la V2 »).

- Recevoir les offres du Forem, de l'API du VDAB, ou importer une offre par son adresse.
- Construire son profil, obtenir un score de correspondance entre une offre et son profil, se faire aider pour ses CV et ses lettres, joindre des documents à une candidature.
- Choisir un autre fournisseur d'IA, dont Mistral.
- Rechercher et filtrer ses candidatures, être prévenu d'une relance échue par une notification Windows, recharger un export, importer son suivi depuis career-ops.
- Installer et désinstaller Sollicito sans perdre ses données.
- Déposer le brouillon d'une relance dans Gmail ou Outlook, sans l'envoyer.
