# ADR-0002 : budget nul

- **Statut** : acceptée
- **Date** : 2026-11-13
- **Remplace en partie** : [ADR-0001](0001-cadrage-architecture-v1.md), D13 (signature de code)

## Contexte

Le projet n'a pas de budget : aucune dépense n'y est prévue. L'ADR-0001 (D13) écartait la signature de code « en V1 », ce qui laissait entendre l'achat d'un certificat plus tard.

## Décision

- Aucun service ni outil payant : ni certificat de signature de code, ni hébergement, ni API payante.
- L'installeur est publié sans signature. Seule une voie de signature gratuite pourrait changer cela (voir « À vérifier »).
- Ce que la V1 utilise est gratuit pour un dépôt public : GitHub Actions, GitHub Releases et les attestations de provenance, Inno Setup, Photino.NET, .NET, SQLite, les données ouvertes du Forem.
- Le module IA se développe et se teste sans clé payante : sur un modèle local (Ollama) et sur des réponses synthétiques. Les fournisseurs en ligne restent pris en charge, avec la clé de l'utilisateur.

## Alternatives écartées

- **Certificat de signature de code (OV ou EV)** : payant chaque année.
- **Service de signature de Microsoft (Artifact Signing)** : payant.

## Conséquences

- SmartScreen avertira à chaque nouvelle version. Là où Smart App Control est actif, l'installeur sera bloqué : ces utilisateurs ne pourront pas installer Sollicito tant qu'il n'est pas signé. Le guide d'installation le dira clairement.
- Les fournisseurs d'IA en ligne ne sont testés que sur des réponses synthétiques, jamais sur leur API réelle, par les tests du projet.

## À vérifier

Voies de signature gratuites, relevées le 2026-11-13, à évaluer avant la première version publiée :

- **SignPath Foundation** : signature gratuite pour les projets open source. Le certificat est au nom de la fondation, qui vérifie que le binaire est construit depuis le dépôt ; ses conditions d'éligibilité ne figurent pas sur sa page d'accueil.
- **Microsoft Store** : compte de développeur individuel sans frais d'inscription, avec vérification d'identité (pièce d'identité et photo). Restent à vérifier la publication d'un installeur Inno Setup et la signature des paquets par le Store.

## Sources

Consultées le 2026-11-13 :

- SignPath Foundation : <https://signpath.org/>
- Microsoft Learn, ouvrir un compte de développeur du Microsoft Store : <https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account>
