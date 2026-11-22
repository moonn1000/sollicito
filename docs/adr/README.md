# Décisions d'architecture (ADR)

Chaque décision d'architecture qui compte est consignée ici, avec son contexte, les options écartées et ses conséquences. Un ADR accepté ne se réécrit pas : une décision qui change fait l'objet d'un nouvel ADR, qui remplace l'ancien en tout ou en partie. Les points qu'un ADR laisse ouverts se règlent eux aussi par un nouvel ADR.

| N° | Titre | Statut | Date |
| --- | --- | --- | --- |
| [0001](0001-cadrage-architecture-v1.md) | Cadrage de l'architecture de la V1 | Acceptée ; D13 remplacée en partie par 0002 et 0004 ; D2 par 0003 ; D4, D5, D6, D7, D10 et D15 par 0004 ; D9 précisée par 0003 ; D5 précisée, et son esquisse remplacée, par 0005 ; D3 et D10 précisées par 0008 ; D5 et D8 précisées par 0011 ; D10 précisée par 0012 (restauration d'une sauvegarde) ; D5, D6 et D9 précisées par 0013 (offres collées) ; D5 précisée par 0014 (le lien d'une offre et de sa candidature) | 2026-11-13 |
| [0002](0002-budget-nul.md) | Budget nul | Acceptée ; mention de Photino.NET remplacée par 0003 ; installeur, Inno Setup, Forem et guide d'installation remplacés en partie par 0004 | 2026-11-13 |
| [0003](0003-hote-interface-webview2.md) | Hôte de l'interface : WebView2 dans une fenêtre WinForms | Acceptée ; installeur repoussé en V2 et deux points « À vérifier » tranchés par 0004 ; points « Navigation » et « Nouvelles fenêtres » précisés par 0006 ; « Durcissement de la fenêtre » précisé par 0008 (choix du dossier des sauvegardes), par 0011 (brouillon d'une relance) et par 0012 (restauration d'une sauvegarde, confirmée par un dialogue de l'hôte) | 2026-11-13 |
| [0004](0004-v1-resserree-paliers.md) | V1 resserrée, livrée en paliers | Acceptée ; sauvegardes de « Ce que livre la V1 » précisées par 0008 ; compression de l'exe (« À vérifier ») tranchée, et D13 précisée, par 0009 | 2026-11-18 |
| [0005](0005-modele-de-domaine.md) | Modèle de domaine, dédoublonnage et dates | Acceptée ; correction par ajout étendue aux étapes, nom affiché et entreprises proposées précisés par 0007 ; dédoublonnage des offres précisé par 0013 (empreinte, accord devant un doublon possible), puis par 0014 (« Postuler ») | 2026-11-18 |
| [0006](0006-requetes-et-liens-de-la-fenetre.md) | La fenêtre retient ses requêtes vers une autre origine | Acceptée | 2026-11-19 |
| [0007](0007-historique-en-vigueur-et-nom-corrige.md) | Étapes corrigées et historique en vigueur, nom d'entreprise corrigé et entreprises proposées | Acceptée ; « À vérifier » (les offres) tranché par 0013 ; nom corrigé précisé par 0014 (les offres comptées) | 2026-11-20 |
| [0008](0008-sauvegardes-de-la-base.md) | Sauvegardes de la base, migration sur une copie et premier réglage | Acceptée ; réglages de l'application précisés par 0011 (le délai des relances, lu et écrit à part) ; liste des copies, restes d'un arrêt et « La restauration attend #28 » précisés par 0012 | 2026-11-20 |
| [0009](0009-construction-et-publication-de-l-exe.md) | Construction et publication de l'exe | Acceptée ; textes de release-please et noms des fichiers joints à valider au palier 5 | 2026-11-21 |
| [0010](0010-nom-sollicito.md) | Le nom « Sollicito » | Proposée ; décision du mainteneur au palier 5 | 2026-11-21 |
| [0011](0011-relances-et-brouillon.md) | Relances, contact minimal, brouillon ouvert par l'hôte et délai par défaut | Acceptée | 2026-11-21 |
| [0012](0012-restauration-d-une-sauvegarde.md) | Restauration d'une sauvegarde, confirmée par l'hôte, vérifiée sur une copie, base copiée d'abord | Acceptée | 2026-11-21 |
| [0013](0013-offres-collees.md) | Offres collées : texte gardé, empreinte, doublon possible avec accord, entreprises proposées | Acceptée ; « Postuler » et la liste des offres précisés par 0014 | 2026-11-22 |
| [0014](0014-postuler-a-une-offre.md) | Postuler à une offre : le lien porté par la candidature, l'entreprise d'abord sur l'offre, la liste filtrée dans la page | Acceptée | 2026-11-22 |

## Format

Chaque ADR suit le même plan : statut et date, contexte, décision, alternatives écartées, conséquences, points à vérifier, sources. Fichier nommé `NNNN-titre-court.md`.
