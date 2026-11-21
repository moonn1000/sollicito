# ADR-0010 : le nom « Sollicito »

- **Statut** : proposée ; la décision revient au mainteneur, au palier 5, avant la fusion de la pull request de version 1.0.0
- **Date** : 2026-11-21
- **Répond à** : [ADR-0001](0001-cadrage-architecture-v1.md), « À vérifier » (nom) ; il le tranchera une fois accepté

## Contexte

L'ADR-0001 demandait de vérifier que le nom « Sollicito » ne pose pas de problème de marque. #61, fusionnée dans #54, veut une recherche dans les registres de marques et parmi les projets existants, consignée dans un ADR, avant la première version : changer de nom après elle toucherait l'exe, le dépôt, le dossier de données (`%LOCALAPPDATA%\Sollicito`) et les étiquettes des versions.

Le nom évoque le français « solliciter » et le néerlandais « solliciteren », qui veut dire postuler à un emploi ; une « sollicitatie » est une candidature. Sollicito vise la recherche d'emploi en Belgique, en français comme en néerlandais.

Ce relevé n'est pas un avis juridique.

## Recherche, le 2026-11-21

### Registres de marques

- **Global Brand Database de l'OMPI**, qui couvre 90 sources : l'EUIPO, les enregistrements internationaux de Madrid, et des offices nationaux, dont ceux de France, d'Allemagne, d'Italie et du Royaume-Uni. Sa liste de couverture ne cite pas le BOIP. Aucune marque ne contient le mot « sollicito ». Pour les variantes :
  - « sollicit » : la marque SOLLICIT, au Royaume-Uni et en France, classes 1 et 5 (produits chimiques et phytosanitaires), enregistrée ;
  - « sollecito » : des marques japonaises, classes 24 et 25 (textile), expirées en 2008 ;
  - « solicito » : SOLICITO SCIENTIA, en Australie, classe 41, demande close, et une marque de classe 3 au Costa Rica.
- **Registre du BOIP** (Benelux). Sa recherche montre aussi les marques de l'Union européenne et les marques internationales qui visent le Benelux. Aucune marque « sollicito » ni « sollecito ». Sous « sollicit », dix marques, dont ces quatre marques du Benelux, enregistrées et toutes du domaine de l'emploi :
  - **BX 1347584, « Sollicitego »**, verbale, déposée le 31/01/2017, valable jusqu'au 31/01/2027 : classe 35 (recrutement, rédaction de CV, entretiens de candidature, conseil de carrière), classes 38 et 41 ;
  - **BX 1539584, « Solliciteeropmij.nl »**, verbale, déposée le 23/12/2025 et enregistrée le 19/03/2026 : classe 9 (logiciels, plateformes logicielles), classe 35 (recrutement) et classe 42 (développement de logiciels, hébergement de plateformes) ;
  - **BX 1365742, « SolliciteerZo! »**, figurative, verte, noire et blanche, déposée le 11/01/2018 : classes 9, 35, 41, 42, 44 et 45 ;
  - **BX 1465732**, figurative, déposée le 10/06/2022 : classe 41 (formations en ligne aux candidatures).

  Les autres sont refusées (« Sollicitatieschijf », 1996 ; « www.sollicitatienet.nl », 2001 ; une figurative de 2000), ou sans rapport : la marque de l'Union européenne SOLLICIT (017986175), classes 1 et 5, et deux marques internationales de cosmétiques.
- **TMview**, de l'EUIPO, refuse les requêtes automatiques (« Request Rejected »). Il n'a pas été consulté : les marques de l'EUIPO sont dans la Global Brand Database, et celles du Benelux dans le registre du BOIP.

### Projets et noms existants

- **GitHub** : aucun projet de ce nom, hors ce dépôt ; trois dépôts aux noms latins tirés au hasard (« …-sollicito-… »), sans rapport.
- **npm** : deux paquets aux noms latins tirés au hasard, sans rapport. **NuGet**, **winget** : aucun. **Microsoft Store** : aucune application de ce nom.
- **Domaines** :
  - `sollicito.com` est enregistré depuis le 24/08/1998, chez NameCheap, et redirige vers un autre domaine ;
  - `sollicito.be`, `sollicito.eu`, `sollicito.nl`, `sollicito.fr` et `sollicito.app` n'ont pas de serveurs DNS.

## Ce que la recherche montre

- **Aucune marque identique** n'a été trouvée, ni dans l'Union européenne, ni au Benelux, ni dans les enregistrements internationaux de Madrid.
- **Les marques les plus proches** sont au Benelux, et toutes visent l'emploi. Elles commencent par « sollicit- », le radical du verbe néerlandais « solliciteren » : « Sollicitego », « Solliciteeropmij.nl », « SolliciteerZo! ».
- **« Sollicitego »** (classe 35, recrutement) et **« Solliciteeropmij.nl »** (classes 9, 35 et 42 : logiciels et recrutement) couvrent des produits ou des services voisins de ceux de Sollicito, pour le même public néerlandophone.
- **Sollicito** est un logiciel libre et gratuit (AGPL-3.0), sans service commercial. Le projet n'a pas de budget pour déposer une marque (ADR-0002).

Dire si ces marques et Sollicito risquent d'être confondus est une question de droit des marques. Ce relevé ne la tranche pas.

## Les deux voies

- **Garder le nom « Sollicito »** :
  - aucun travail ;
  - un nom qui dit son objet aux néerlandophones ;
  - des marques voisines, enregistrées au Benelux, dans le même domaine.
- **Changer de nom avant la version 1.0.0** :
  - le dépôt, l'exe, le dossier de données, la documentation et les textes publics à renommer ;
  - une nouvelle recherche, sur le nouveau nom.

La décision revient au mainteneur, au palier 5, avant la fusion de la pull request de version 1.0.0. Elle remplacera cette section, et l'ADR passera alors au statut « acceptée ».

## À vérifier

- **Refaire la recherche juste avant la 1.0.0** : un dépôt de marque récent n'apparaît dans les registres qu'après quelques semaines.
- **TMview à la main** : le mainteneur peut le consulter dans son navigateur, pour un regard d'ensemble sur les offices de l'Union.

## Sources

Consultées le 2026-11-21 :

- OMPI, Global Brand Database, recherche par nom et liste de couverture : <https://branddb.wipo.int/en/similarname>, <https://branddb.wipo.int/en/coverage>
- BOIP, registre des marques : <https://www.boip.int/en/trademarks-register>
- EUIPO, TMview : <https://www.tmdn.org/tmview/>
- GitHub, recherche de dépôts ; npm, recherche du registre ; NuGet, recherche ; winget, dépôt `microsoft/winget-pkgs` ; Microsoft Store, recherche
- Verisign, RDAP de `sollicito.com` : <https://rdap.verisign.com/com/v1/domain/sollicito.com> ; serveurs DNS des autres domaines, par `nslookup`
