# ADR-0002 : budget nul

- **Statut** : acceptée
- **Date** : 2026-11-13
- **Remplace en partie** : [ADR-0001](0001-cadrage-architecture-v1.md), D13 (signature de code)

## Contexte

Le projet n'a pas de budget : aucune dépense n'y est prévue. L'ADR-0001 (D13) écartait la signature de code « en V1 », ce qui laissait entendre l'achat d'un certificat plus tard.

## Décision

- Aucun service ni outil payant : ni certificat de signature de code, ni hébergement, ni API payante.
- L'installeur est publié sans signature. Seule une voie de signature gratuite pourrait changer cela (voir « À vérifier »).
- La V1 ne s'appuie que sur des outils et services gratuits pour ce projet :
  - GitHub Actions, dont les exécuteurs standard hébergés par GitHub sont gratuits pour un dépôt public[^actions] ;
  - les attestations de provenance, prises en charge pour les dépôts publics[^attestations] ;
  - Inno Setup, dont la licence permet à chacun de l'utiliser pour tout usage[^inno] ;
  - des briques libres : .NET (licence MIT)[^dotnet], Photino.NET (Apache-2.0)[^photino], SQLite (domaine public)[^sqlite] ;
  - les données ouvertes du Forem (voir ADR-0001).
- Le module IA se développe et se teste sans clé payante : sur un modèle local (Ollama) et sur des réponses synthétiques. Les fournisseurs en ligne restent pris en charge, avec la clé de l'utilisateur.

## Alternatives écartées

D'après la comparaison publiée par Microsoft[^signature] :

- **Certificat OV** d'une autorité de certification : environ 150 à 300 dollars par an.
- **Certificat EV** : 400 dollars par an ou plus, sans avantage sur l'OV pour SmartScreen depuis 2024.
- **Azure Artifact Signing** : environ 9,99 dollars par mois, et ouvert aux développeurs individuels des États-Unis et du Canada seulement.

## Conséquences

- **SmartScreen** : sans signature, Microsoft annonce un blocage fort par SmartScreen, et une entreprise peut interdire l'installeur entièrement. Signer chaque version avec la même identité permet de cumuler une réputation d'une version à l'autre[^signature], ce qu'un installeur non signé ne peut pas faire. Le guide d'installation expliquera comment passer l'avertissement.
- **Smart App Control** : là où il est actif, il bloque une application non signée que le service de Microsoft ne peut pas juger sûre. L'utilisateur peut le désactiver dans la Sécurité Windows, et les mises à jour récentes permettent de le réactiver sans réinstaller Windows[^sac]. Le guide d'installation le dira.
- **IA** : les fournisseurs en ligne ne sont testés que sur des réponses synthétiques, jamais sur leur API réelle, par les tests du projet.

## À vérifier

Voies de signature gratuites, relevées le 2026-11-13 :

- **SignPath Foundation** : signature gratuite pour les projets open source[^signpath]. Le certificat est délivré à la fondation[^signpath-conditions], dont les conditions demandent notamment une licence approuvée par l'OSI sans double licence commerciale, un projet déjà publié sous la forme à signer, une politique de signature sur la page du projet, la double authentification des membres et l'approbation manuelle de chaque version. La toute première version ne pourra donc pas être signée par cette voie : à évaluer après elle.
- **Microsoft Store** : compte de développeur individuel sans frais d'inscription, avec vérification d'identité (pièce d'identité et photo)[^store-compte]. Le Store signe gratuitement les paquets MSIX, mais pas un installeur EXE ou MSI, qui doit déjà être signé par une autorité reconnue et s'installer sans interface[^signature][^store-msi]. Cette voie supposerait donc un paquet MSIX distribué par le Store, ce qui réviserait l'ADR-0001.

## Sources

Consultées le 2026-11-13 :

[^actions]: GitHub Docs, GitHub Actions billing : <https://docs.github.com/en/billing/managing-billing-for-your-products/managing-billing-for-github-actions/about-billing-for-github-actions>
[^attestations]: GitHub Docs, Artifact attestations : <https://docs.github.com/en/actions/concepts/security/artifact-attestations>
[^inno]: Inno Setup, licence : <https://jrsoftware.org/files/is/license.txt>
[^dotnet]: .NET, dépôt `dotnet/runtime` : <https://github.com/dotnet/runtime>
[^photino]: Photino.NET, dépôt du projet : <https://github.com/tryphotino/photino.NET>
[^sqlite]: SQLite, statut juridique : <https://www.sqlite.org/copyright.html>
[^signature]: Microsoft Learn, Code signing options for Windows app developers : <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options>
[^sac]: Microsoft Support, Smart App Control Frequently Asked Questions : <https://support.microsoft.com/en-us/topic/what-is-smart-app-control-285ea03d-fa88-4d56-882e-6698afdb7003>
[^signpath]: SignPath Foundation : <https://signpath.org/>
[^signpath-conditions]: SignPath Foundation, conditions pour les projets open source : <https://signpath.org/terms>
[^store-compte]: Microsoft Learn, ouvrir un compte de développeur du Microsoft Store : <https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account>
[^store-msi]: Microsoft Learn, App package requirements for MSI/EXE app : <https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements>
