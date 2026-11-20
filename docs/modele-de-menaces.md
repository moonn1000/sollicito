# Modèle de menaces

- **Mis à jour le** : 2026-11-20, avec la modification et la suppression d'une candidature (#25)
- **Décisions** : [ADR-0001](adr/0001-cadrage-architecture-v1.md), D9 et D10 ; [ADR-0003](adr/0003-hote-interface-webview2.md), « Règles d'accès » et « Durcissement de la fenêtre » ; [ADR-0006](adr/0006-requetes-et-liens-de-la-fenetre.md)
- **Périmètre** : l'API locale, ses journaux et sa base, la fenêtre hôte, et les textes saisis. Les offres et la sortie de l'IA, les clés d'IA, les sauvegardes et l'exe ajouteront leur section avec les stories qui les construisent.

## Ce qui est protégé

- **Les données de l'utilisateur** : ses candidatures, les entreprises et leurs contacts, ses relances, ses offres. Elles retracent sa recherche d'emploi, ses refus, parfois ses prétentions salariales. L'API locale les sert à l'interface : qui l'appelle à la place de l'interface peut les lire ou les modifier.
- **Le jeton de lancement** : il ouvre l'API, il est donc protégé comme les données.
- **La fenêtre** : elle n'affiche que l'interface, qui détient le jeton. Une page d'une autre origine qui s'y afficherait pourrait le recevoir, ou tromper l'utilisateur sous l'apparence de Sollicito.

## Qui peut appeler l'API

| Qui | Comment | Ce qui l'arrête |
| --- | --- | --- |
| Une page web ouverte dans un navigateur du poste | une requête vers `127.0.0.1` : formulaire, `fetch`, image ou script | le jeton ; l'en-tête `Origin` |
| La même page, par rebinding DNS | son nom de domaine se met à désigner `127.0.0.1` : pour le navigateur, ses requêtes vers l'API deviennent de même origine | l'en-tête `Host` ; le jeton |
| Un appareil du réseau local ou d'Internet | une connexion au port de l'API | l'écoute sur `127.0.0.1` seulement |
| Un autre compte Windows du même PC (session ouverte en parallèle, bureau à distance) | `127.0.0.1` est commun à toutes les sessions du PC : ce compte joint le port | le jeton |
| Un programme lancé sous le compte de l'utilisateur | tout ce que l'utilisateur peut faire | rien dans l'API : voir « Limites » |
| Qui lit un journal (rapport de bogue, outil de diagnostic) | les journaux de l'application | la règle des journaux |

## Protections de l'API locale

Chaque requête passe ces contrôles dans cet ordre (`src/Sollicito.Api/ApiLocale.cs`). Le premier qui échoue répond, sans corps, et la requête ne va pas plus loin. Toute réponse, refus compris, porte d'abord les en-têtes de sécurité (voir « Politique de sécurité du contenu et autres en-têtes »). En tête de tout, l'API répond elle-même à une exception (voir « Erreurs inattendues »).

1. **En-tête `Host`** (400), sur toute requête.
2. **Fichiers de l'interface** (`index.html`, scripts, styles) : servis sans jeton ni contrôle d'`Origin`, puisque la page les charge avant de recevoir le jeton. Ils sont publics et ne portent aucune donnée (ADR-0003). Seule une lecture (`GET`, `HEAD`) d'un fichier qui existe est servie, à un chemin fait de segments simples, et rien sous `/api` : toute autre adresse passe aux deux contrôles suivants. La fenêtre fournit ces fichiers, embarqués dans l'exe ; lancée seule, l'API n'en sert aucun.
3. **Jeton** (401), sur tout le reste : routes de l'API, documentation OpenAPI, adresses inconnues.
4. **En-tête `Origin`** (403), sur ce même reste.

Le routage ne vient qu'ensuite : une requête refusée n'est jamais routée (`tests/Sollicito.Api.Tests/OrdreDesControlesTests.cs`).

### Écoute sur 127.0.0.1

Kestrel n'écoute que sur `127.0.0.1`, sur un port libre choisi à chaque lancement ; aucune interface réseau n'est ouverte. L'écoute ne se règle que dans le code :

- les adresses données par la configuration (`urls`, section `Kestrel`) sont ignorées, et la section `Kestrel` n'est pas relue en cours de route ;
- une configuration qui demande à Kestrel de préférer ses propres adresses (`preferHostingUrls`) fait échouer le lancement avant toute écoute ;
- une fois Kestrel démarré, l'API relève l'adresse réellement ouverte. Si une autre écoute s'est ouverte, elle arrête Kestrel et le lancement échoue.

Le port n'est pas un secret : tout programme du poste le lit dans la liste des connexions, et une page web peut sonder les ports locaux. Il ne donne accès à rien sans le jeton.

*Tests* : `tests/Sollicito.Api.Tests/EcouteDeLApiTests.cs`. Une seule adresse est ouverte, en `127.0.0.1`. Aucune autre adresse du poste ne répond sur le port de l'API, ni celle d'une carte réseau, ni `[::1]`. Une adresse donnée par `urls` ou par `Kestrel:Endpoints` reste sans effet, et une écoute ajoutée à chaud dans la configuration ne s'ouvre pas. Avec `preferHostingUrls`, le lancement échoue sans qu'aucune écoute ne se soit ouverte, et l'échec paraît dans les journaux. Une écoute de plus, ouverte par le code, fait échouer le lancement. Ces essais d'écoute interdite visent `127.0.0.2`, une autre adresse locale : une écoute sur `0.0.0.0` ouvrirait l'invite du pare-feu de Windows sur le poste qui lance les tests.

### Jeton de lancement

- 32 octets, soit 256 bits, tirés à chaque lancement par `RandomNumberGenerator`, générateur cryptographique de .NET[^generateur]. Le jeton ne vit qu'en mémoire : aucune route ne le rend, et il n'est écrit ni dans un fichier, ni sur la ligne de commande, ni dans un journal. Son texte (`ToString`) ne montre pas sa valeur. La fenêtre hôte le lit dans les services de l'API et le remet à l'interface par son canal interne (voir « Canal du jeton »).
- Il n'est lu que dans l'en-tête `Sollicito-Jeton`, présent une seule fois. Le navigateur joint de lui-même un cookie aux requêtes vers le site qui l'a posé, y compris, selon ses réglages, à celles qu'une autre page déclenche. Une adresse se retrouve dans l'historique et dans les journaux. Une page d'une autre origine, enfin, ne peut pas ajouter un en-tête à sa requête sans une autorisation CORS préalable, que l'API ne donne jamais.
- La comparaison se fait en temps constant (`CryptographicOperations.FixedTimeEquals`) : sa durée dépend de la longueur des deux valeurs, pas de leur contenu[^comparaison]. Seule la longueur, qui n'est pas secrète, peut l'écourter.
- Le jeton est exigé partout, en développement comme ailleurs. Aucune porte de service : sans la fenêtre hôte, on ne peut pas appeler l'API à la main, et les tests lisent le jeton dans les services de l'API lancée.

*Tests* : `tests/Sollicito.Api.Tests/ControleDuJetonTests.cs`. Le jeton fait 256 bits et change à chaque lancement. Un jeton absent, faux, presque juste ou répété est refusé, même pour une écriture venue de l'interface. Placé dans un cookie, dans l'adresse ou dans l'en-tête `Authorization`, il ne vaut rien. Le refus vaut pour la route de l'état, la documentation OpenAPI, une route inconnue, la racine et un fichier ; celui de la documentation OpenAPI est essayé en développement et en production. Aucune réponse ne contient le jeton. Aucun test ne mesure la durée de la comparaison, ni ne juge le générateur : de tels tests seraient instables ; le code les montre.

### En-tête `Host`

Le navigateur envoie dans `Host` le nom qu'il a joint. Une page piégée qui atteint l'API par rebinding DNS envoie donc son propre nom de domaine, et une navigation vers `http://localhost:<port>/` envoie `localhost` : ce n'est pas l'adresse d'écoute, la requête est refusée. L'API compare `Host` à l'adresse relevée au lancement, `127.0.0.1:<port>`, caractère pour caractère. Le contrôle intégré d'ASP.NET Core (`AllowedHosts`) ne convient pas : il se règle dans la configuration, avant que le port ne soit connu, et un réglage suffit à le désactiver.

Le prototype de #9 l'a montré : une navigation que la fenêtre annule envoie quand même sa requête, et celle vers `localhost` a atteint Kestrel (ADR-0003, « À vérifier »). La politique de sécurité du contenu ne l'arrête pas non plus. Ce contrôle est donc indispensable côté serveur.

*Tests* : `tests/Sollicito.Api.Tests/ControleDeLHoteTests.cs`. Dix valeurs de `Host` inattendues sont refusées, chacune sur quatre routes, même avec le bon jeton : `localhost` en minuscules et en capitales, un autre nom avec et sans port, l'adresse sans port, un autre port, `127.0.0.2`, `[::1]`, l'adresse écrite en décimal, `0.0.0.0`. Sont aussi refusées une écriture vers `localhost`, une requête HTTP/1.0 sans `Host`, et une adresse absolue vers un autre hôte, avec un `Host` assorti que Kestrel accepte.

### En-tête `Origin`

Le navigateur ajoute `Origin` aux requêtes d'une autre origine, et aux requêtes de même origine sauf `GET` et `HEAD`[^origin]. D'où la règle de l'ADR-0003 :

- une écriture, c'est-à-dire toute méthode autre que `GET` et `HEAD`, doit porter exactement l'origine de l'interface, `http://127.0.0.1:<port>` ;
- une lecture peut s'en passer, mais si elle en porte une, ce doit être celle-là ;
- l'origine `null`, qu'envoient notamment un cadre isolé (`sandbox`) ou un document local, est refusée, comme un en-tête répété.

Une lecture intersite simple (image, script) peut partir sans `Origin`[^origin] : contre elle, c'est le jeton qui protège. `Origin` double le jeton : il arrête une écriture ou une lecture intersite même si le jeton avait fui vers une autre origine.

*Tests* : `tests/Sollicito.Api.Tests/ControleDeLOrigineTests.cs`. Avec le bon jeton, une écriture (`POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`) sans origine ou d'origine `null` est refusée ; un `POST` ou une lecture d'une autre origine aussi, sur neuf origines essayées. Une écriture venue de l'interface passe les contrôles : la route de l'état n'accepte aucune écriture, d'où sa réponse 405.

### Aucune politique CORS

CORS assouplit la règle de même origine du navigateur ; il ne protège pas le serveur. L'API n'en déclare aucune : aucune réponse ne porte d'en-tête `Access-Control-*`, pas même la réponse à une requête de contrôle préalable. L'interface est servie à la même origine que l'API et n'en a pas besoin.

*Tests* : `tests/Sollicito.Api.Tests/AucuneOuvertureCorsTests.cs`.

### Fichiers de l'interface

Un chemin n'est servi comme fichier que s'il est fait de segments simples : lettres, chiffres, `-`, `_`, et des points entre eux. Les fournisseurs de fichiers lisent les autres formes à leur façon : un antislash y sépare deux dossiers, un point final disparaît sous Windows. Sans cette règle, `/api%5Cfichier.json` faisait servir sans jeton un fichier rangé sous `api/` (revue de #13). Antislash, segment vide (`//`), `.` et `..`, point final, deux-points et tilde ne désignent donc aucun fichier, encodés ou non : le filtre voit le chemin déjà décodé.

*Tests* : `tests/Sollicito.Api.Tests/FichiersDeLInterfaceTests.cs`, sur une interface fictive servie par les deux fournisseurs : embarquée dans l'assembly, comme dans l'exe, et lue sur le disque. La page, la racine et un script sont servis sans jeton, chacun sous son type. Sous un autre en-tête `Host`, ils sont refusés (400). Une adresse sans fichier, un dossier et une adresse de l'interface encore inconnue restent soumis au jeton (401). Un fichier rangé sous `api/` n'est jamais servi : 401 sans jeton, 404 avec ; quatorze autres écritures du chemin, envoyées telles quelles (antislash brut ou encodé, `//`, point final, `..`, flux de données `::$DATA`, nom court `~1`…), n'en font servir aucun. Une écriture vers un fichier reste soumise au jeton et à l'origine. `ControleDuJetonTests.cs` : lancée seule, l'API refuse `/` et `/index.html` sans jeton.

### Routes des candidatures

`GET /api/candidatures`, `POST /api/candidatures` et `GET /api/entreprises` (#11), les quatre routes de la fiche d'une candidature (#12), puis ses trois routes de modification, de suppression et de correction d'une étape (#25), passent par les mêmes contrôles que toute route, et figurent dans la documentation OpenAPI. Les routes de la fiche : `GET /api/candidatures/{id}`, et en `POST` `…/{id}/statut`, `…/{id}/changements/{changement}/correction` et `…/{id}/etapes`. Celles de #25 : `PUT /api/candidatures/{id}`, qui corrige l'entreprise, le poste et la date d'envoi, `DELETE /api/candidatures/{id}`, et en `POST` `…/{id}/etapes/{etape}/correction`. Comme toute écriture, elles exigent l'origine de l'interface ; le filtre des requêtes de la fenêtre ne juge que l'origine, quelle que soit la méthode.

- Aucune ne porte de donnée dans son adresse, seulement des identifiants de la base : les textes saisis voyagent dans le corps. Les entreprises que l'écran de saisie propose se lisent en une fois, et l'écran les filtre lui-même : une recherche par l'adresse écrirait le texte tapé dans une chaîne de requête.
- Le corps se lit strictement : un champ inconnu, comme un statut à l'enregistrement, rend la requête illisible (400) au lieu d'être ignoré ; un corps qui n'est pas du JSON reçoit un 415. Chaque champ est validé par l'API, qui rend ses refus champ par champ, sans reprendre le texte refusé (modèle de domaine, « Saisie d'une candidature » et « Noter une étape »).
- Un statut ou un type d'étape se lit par son nom exact : ni un nombre, que le convertisseur des énumérations admettrait, ni une autre casse, ni une liste de noms.
- Une action que l'état de la candidature ne permet pas (passage hors du diagramme d'états, correction d'un changement qui n'est plus le dernier, correction d'une étape qui n'est plus la version en vigueur) reçoit un 409, avec son motif, qui ne cite que des statuts ou l'état de l'étape. Chaque action lit la candidature et l'écrit dans une même écriture exclusive : deux demandes simultanées ne partent jamais du même état. La modification y relit aussi l'historique : une étape notée pendant qu'on corrige la date d'envoi ne passe jamais avant l'envoi.
- Le statut n'est pas un champ de la modification : un corps qui en porte un est illisible (400), comme à l'enregistrement.
- La suppression est définitive, et ne s'annule pas : l'interface la fait confirmer dans la page, jamais par une boîte du navigateur, et elle ne part que de l'interface, avec le jeton et l'origine. Elle n'emporte que la candidature et ses événements : son entreprise reste.

*Tests* : `tests/Sollicito.Api.Tests/ProtectionsDesCandidaturesTests.cs`, `ProtectionsDuSuiviTests.cs` et `ProtectionsDeLaModificationTests.cs`. Sur chacune des dix routes, une requête sans jeton (401), sous un autre `Host` (400) ou d'une autre origine (403) est refusée, et n'enregistre ni ne change rien ; une écriture sans origine ou d'origine `null` aussi (403). Les dix routes figurent dans la documentation OpenAPI. `tests/Sollicito.Api.Tests/CandidaturesTests.cs`, `SuiviDesCandidaturesTests.cs`, `ModificationDesCandidaturesTests.cs` et `CorrectionDesEtapesTests.cs` : les refus de chaque champ, et les corps illisibles ou étrangers (nombre à la place d'un statut, statut dans une modification, champ inconnu, JSON mal formé, corps vide), n'enregistrent ni ne changent rien ; dix changements de statut simultanés n'en gardent qu'un, cinq corrections simultanées du même changement n'en font qu'une, six corrections simultanées d'une même étape n'en font qu'une, huit modifications simultanées vers un même nouveau nom ne créent qu'une entreprise, et dix modifications de la date d'envoi lancées en même temps qu'une étape n'en laissent aucune avant l'envoi. La base refuse elle-même qu'une étape soit corrigée deux fois, ou qu'une correction désigne un événement absent. `MigrationDesCorrectionsDEtapeTests.cs` : une base peuplée avant #25 passe sa migration sans rien perdre, puis s'écrit. `FicheDUneCandidature.test.tsx` : la suppression attend sa confirmation dans la page, aucune boîte du navigateur ne s'ouvre, et « Annuler » ou Échap ne suppriment rien ; `fenetre.test.ts` : de même dans la vraie fenêtre, où aucune boîte ne s'ouvre.

### Erreurs inattendues

En développement, ASP.NET Core place sa page d'exception avant tout le reste du chemin des requêtes. Elle afficherait les en-têtes de la requête, jeton compris, et le message de l'exception, en HTML, en texte ou en JSON selon ce que la requête accepte. Aucune exception ne l'atteint : la première étape du chemin, `ErreursInattendues` (`src/Sollicito.Api/Erreurs/ErreursInattendues.cs`), répond elle-même, quel que soit l'environnement.

- Un corps illisible reçoit son statut, 400 le plus souvent, et toute autre exception un 500. La réponse est un problème sans détail (`application/problem+json`) : ni en-tête, ni message, ni pile. Les en-têtes de sécurité lui sont remis.
- En développement, un corps illisible lève une exception au lieu d'un simple 400 (`RouteHandlerOptions.ThrowOnBadRequest`) : l'API le règle à vrai partout, pour que tous les environnements passent par ce même chemin.
- Le journal ne note que le type de l'exception : son message peut reprendre ce que la requête contenait.
- La fenêtre impose de plus l'environnement de production (voir « L'API de la fenêtre »).

*Tests* : `tests/Sollicito.Api.Tests/PageDExceptionTests.cs`, en développement puis en production, en acceptant `text/html`, `application/json`, `text/plain` ou rien. Une route dont le dépôt échoue, avec un message qui cite une donnée fictive, répond 500 ; un corps mal formé, 400. Les deux rendent un problème sans détail, qui ne contient ni le jeton, ni le nom de son en-tête, ni la donnée, ni le mot « Exception », ni de HTML, et gardent les en-têtes de sécurité. Sans `ErreursInattendues`, les seize cas échouent (essai du 19/11).

## Protections de la fenêtre

La fenêtre (`src/Sollicito.Fenetre`) n'affiche que l'interface locale (ADR-0001, D9, « Fenêtre » ; ADR-0003, « Durcissement de la fenêtre » ; ADR-0006). Elle remet le jeton à l'interface, et rien d'elle ne doit sortir vers une autre origine.

| Ce qui pourrait sortir | Par où | Ce qui l'arrête |
| --- | --- | --- |
| La page, remplacée par une autre | une navigation vers une autre origine : lien, script, formulaire | l'annulation de la navigation ; le filtre des requêtes ; `form-action 'none'` |
| Une donnée, dans une adresse | une image, un script, un `fetch`, un cadre, une balise (`sendBeacon`) | la politique de sécurité du contenu ; le filtre des requêtes |
| Une donnée, par le navigateur par défaut | une nouvelle fenêtre ouverte par un script | l'exigence d'un geste de l'utilisateur |
| Le poste | un schéma externe (`mailto:`, `ms-settings:`…), un téléchargement | leur annulation |
| La caméra, le micro, la position, les notifications | une demande de permission | son refus |
| Le jeton | une autre page qui le demanderait | le contrôle de la source du message |

### Navigations et requêtes

- `NavigationStarting` et `FrameNavigationStarting` annulent toute navigation hors de l'origine de l'interface : même schéma, même hôte, même port, et sans identifiants dans l'adresse. Une adresse `about:`, `data:`, `blob:` ou `file:` n'en est pas. `FrameNavigationStarting` est une seconde barrière : la politique de sécurité du contenu (`frame-src 'none'`) arrête le cadre avant lui, et aucun test ne l'atteint.
- Une navigation annulée envoie quand même sa requête (prototype de #9). Le 19/11, trente navigations de premier niveau vers un serveur témoin sont toutes arrivées. La politique de sécurité du contenu ne l'arrête pas : la phrase de l'ADR-0003 « une CSP stricte double ce filtre » vaut pour les cadres et les connexions, pas pour une navigation de premier niveau.
- D'où le filtre des requêtes (ADR-0006) : la fenêtre répond elle-même, par un 403, à toute requête hors de l'origine, qui ne part donc pas sur le réseau. Avec lui, aucune des trente requêtes n'est arrivée. La politique de sécurité du contenu double ce filtre pour les ressources de la page, et le filtre la double si elle venait à manquer.

*Tests* : `interface/tests-fenetre/fenetre.test.ts`, sur la vraie fenêtre. Trente navigations vers un serveur témoin, sur une autre origine du poste, puis un `fetch`, une image, un script, un cadre, une balise et un formulaire : le témoin ne reçoit aucune requête, et la page reste celle de l'interface. La fenêtre a annulé chacune de ces navigations, et celles vers `localhost`, vers `https://exemple.invalid`, vers `mailto:` et vers `ms-settings:` (la version de test note ses annulations). Celle vers `localhost` n'a pas atteint l'API, qui l'aurait refusée en le notant dans son journal. `tests/Sollicito.Fenetre.Tests/ReglesDeLaFenetreTests.cs` : les adresses admises et refusées.

### Nouvelles fenêtres et liens externes

- `NewWindowRequested` est toujours traité : aucune seconde fenêtre ne s'ouvre. Un lien s'ouvre dans le navigateur par défaut, par le shell de Windows, à trois conditions (ADR-0006). La demande vient d'un geste de l'utilisateur (`IsUserInitiated`) : un clic, ou un script lancé pendant l'activation que ce geste ouvre, même par une minuterie. L'activation expire au bout d'environ cinq secondes (#78). L'adresse est absolue, en `http` ou `https`. Elle ne porte pas d'identifiants. Tout autre lien est ignoré.
- Seul un lien qui ouvre une nouvelle fenêtre s'ouvre ainsi : un lien cliqué sur place est une navigation, annulée, et il ne s'ouvre nulle part. L'interface écrit ses liens externes en nouvelle fenêtre (`target="_blank"`).
- Ce qui reste : un lien cliqué emporte son adresse, telle qu'elle est écrite, vers le navigateur par défaut ; c'est voulu, un lien d'offre s'ouvre ainsi (D9). Un script injecté qui attendrait un clic de l'utilisateur pourrait encore ouvrir une adresse de son choix : la politique de sécurité du contenu, qui interdit les scripts injectés, reste la première barrière.

*Tests* : `fenetre.test.ts` : un clic sur un lien `https` et sur un lien `http`, en nouvelle fenêtre, les ouvre (la version de test les note au lieu de lancer le navigateur) ; `mailto:`, `ftp:` et `ms-settings:` ne s'ouvrent pas ; un lien cliqué sur place est annulé et ne s'ouvre pas ; un `window.open` lancé six secondes après le dernier geste, une fois l'activation expirée, ne s'ouvre pas ; aucune page de plus. `ReglesDeLaFenetreTests.cs` : un lien ouvert sans geste de l'utilisateur, ou porteur d'identifiants, ne s'ouvre pas.

### Schémas externes et téléchargements

`LaunchingExternalUriScheme` et `DownloadStarting` sont annulés : aucun programme ne se lance depuis la page, aucun fichier ne s'enregistre sur le poste. `LaunchingExternalUriScheme` est une seconde barrière : l'annulation des navigations arrête `mailto:` et `ms-settings:` avant lui, et aucun test ne l'atteint. Au début d'un téléchargement, avant que la fenêtre ne l'annule, le navigateur intégré crée et efface aussitôt un fichier temporaire (`<identifiant>.tmp`) dans le dossier Téléchargements de l'utilisateur ; rien n'y reste (relevé le 19/11).

*Tests* : `fenetre.test.ts` : deux téléchargements, d'un fichier de l'interface et d'un `blob:`, n'écrivent rien dans le dossier de téléchargement de la version de test. Branché par CDP, Playwright prend les téléchargements à son compte, et la fenêtre ne verrait pas les siens : le test rend d'abord au navigateur sa propre gestion, celle de la version publiée.

### Permissions

`PermissionRequested` refuse toute demande : notifications, position, caméra, micro et les autres.

*Tests* : `fenetre.test.ts` : notifications, position, caméra et micro refusés. La version de test simule caméra et micro, pour que le refus ne dépende pas du matériel du poste.

### Canal du jeton

- La page signale qu'elle est prête par `postMessage`. La fenêtre vérifie que la source du message est l'origine de l'interface et que le message est celui attendu, puis remet le jeton par `PostWebMessageAsJson`. Le jeton ne passe ni par une route, ni par un fichier, ni par la ligne de commande.
- L'interface le garde dans la fermeture de son client de l'API, en mémoire : ni stockage du navigateur, ni cookie, ni adresse. Elle l'envoie dans l'en-tête `Sollicito-Jeton`, vers la même origine. Une page rechargée le redemande et le reçoit de nouveau.

*Tests* : `fenetre.test.ts` : après un rechargement, l'appel de l'interface à `/api/etat` porte le jeton dans son en-tête, et ce jeton ouvre l'API. Ni l'adresse de la page, ni le stockage local et de session, ni les cookies, ni IndexedDB, ni le cache, ni le document, ni les journaux de l'API ne le contiennent ; après la fermeture, aucun fichier du dossier de données non plus. `ReglesDeLaFenetreTests.cs` : un message d'une autre source, mal formé ou d'un autre type ne reçoit rien. `interface/src/hote/canalDeLHote.test.ts` et `interface/src/connexion.test.ts` : l'interface ignore un message qui n'a pas la forme du jeton, et n'écrit le jeton dans aucun stockage.

### Politique de sécurité du contenu et autres en-têtes

Chaque réponse, refus compris, porte (`src/Sollicito.Api/Securite/EnTetesDeSecurite.cs`) :

- la politique de sécurité du contenu, envoyée en en-tête : `default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; frame-src 'none'; worker-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`. Scripts, styles, images, polices et connexions viennent de l'origine de l'interface seulement ; aucun script en ligne ni évalué ; aucun cadre, objet ou worker ; aucun formulaire envoyé. Sans `worker-src`, un worker se rabattrait sur `script-src`, donc sur l'origine (revue de #13). La construction de l'interface n'inline aucun fichier en adresse `data:` (`assetsInlineLimit: 0` dans `interface/vite.config.ts`), que cette politique refuserait ;
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` et `Cache-Control: no-store` : rien ne se garde dans le cache du profil, ni un fichier, ni une réponse de l'API.

*Tests* : `tests/Sollicito.Api.Tests/EnTetesDeSecuriteTests.cs`, sur sept réponses, dont chaque refus ; `fenetre.test.ts` : un script injecté dans la page ne s'exécute pas.

### Réglages de la version publiée

- Outils de développement, menu contextuel par défaut et raccourcis du navigateur coupés (`AreDevToolsEnabled`, `AreDefaultContextMenusEnabled`, `AreBrowserAcceleratorKeysEnabled`). Ils ne s'ouvrent qu'en développement (configuration `Debug`).
- Aucun objet de l'hôte exposé aux scripts (`AreHostObjectsAllowed`), aucune saisie gardée dans le profil, hors de la base (`IsGeneralAutofillEnabled`, `IsPasswordAutosaveEnabled`), aucune connexion par le compte Windows (`AllowSingleSignOnUsingOSPrimaryAccount`).
- Vérification de réputation des navigations coupée (`IsReputationCheckingRequired = false`), avant la première navigation : la fenêtre ne navigue que vers l'origine locale, et cette vérification ferait appel à SmartScreen, le service de réputation de Microsoft. Si elle restait active, Microsoft impose de prévenir chaque utilisateur que le logiciel envoie ses informations à Microsoft[^smartscreen].

*Vérifiés à la main* sur l'exe publié, et listés dans la pull request de #13 : la version de test a les mêmes réglages, mais le protocole CDP ne prouve pas ce que font les touches et le clic droit.

### Profil et dossier de données

Le profil du navigateur intégré vit dans `%LOCALAPPDATA%\Sollicito\WebView2`, fixé à la création de l'environnement ; par défaut, WebView2 l'écrirait à côté de l'exe. Si le dossier `%LOCALAPPDATA%` de l'utilisateur est introuvable, le lancement s'arrête plutôt que d'écrire dans le dossier courant.

La base SQLite vit dans le même dossier (`sollicito.db`), que la fenêtre donne à l'API qu'elle construit ; l'API lancée seule y range aussi la sienne, développement compris. Au lancement, avant que Kestrel n'écoute, les migrations versionnées la créent ou la mettent à jour. Les connexions ne sont pas regroupées : une fois la base refermée, plus rien ne tient son fichier ouvert. Les tests créent chacun leur base dans un dossier temporaire, jamais dans le dossier de données de l'utilisateur.

Un lancement finit toujours, par la fenêtre ou par le message « Sollicito n'a pas pu démarrer son API locale. » :

- **Un seul lancement à la fois** sur un dossier de données : l'API y ouvre le fichier `sollicito.verrou` sans partage, pour toute sa vie. Windows le referme et l'efface quand le processus s'arrête, même tué : ce verrou ne reste jamais pris. Un second lancement sur le même dossier est refusé aussitôt, au lieu de migrer la même base.
- **Le verrou des migrations d'EF Core** est une ligne de la table `__EFMigrationsLock`, insérée et validée avant les migrations, retirée après. Un processus arrêté entre les deux la laisse, et EF Core l'attend sans fin à chaque lancement suivant, même sans migration à appliquer (relevé le 19/11 : toujours en attente après 40 s). Sous le verrou du dossier, aucun autre lancement ne migre la base : une ligne présente est donc abandonnée, et l'API la retire avant les migrations.
- Ce qui reste : un programme qui migrerait la base sans passer par l'API, comme `dotnet ef database update` lancé par un développeur pendant que Sollicito tourne, verrait son verrou retiré. Une base verrouillée par un autre programme fait échouer le lancement au bout du délai des commandes de SQLite (relevé le 19/11 sur une transaction d'écriture laissée ouverte : échec au bout de 30,3 s, « database is locked »).

*Tests* : `tests/Sollicito.Fenetre.Tests/DossierDeDonneesTests.cs` ; `fenetre.test.ts` : le profil se crée dans le dossier de données donné à la version de test, la base aussi, et une candidature saisie dans la fenêtre s'y enregistre ; après la fermeture, aucun fichier du dossier ne contient le jeton. `tests/Sollicito.Api.Tests/ApiDeLaFenetreTests.cs` : l'API de la fenêtre crée sa base dans le dossier donné ; l'API seule range la sienne sous `%LOCALAPPDATA%\Sollicito`. `CandidaturesTests.cs` : la base des tests vit dans le dossier temporaire, et les migrations appliquées sont toutes celles du code. `tests/Sollicito.Api.Tests/LancementDeLaBaseTests.cs` : avec une ligne laissée dans `__EFMigrationsLock`, l'API relancée démarre et la retire (sans le correctif, le lancement attend toujours au bout des 30 s du test) ; un second lancement sur le même dossier est refusé pendant que le premier répond, puis passe une fois le premier arrêté.

### L'API de la fenêtre

- Un seul exe démarre l'API, puis ouvre la fenêtre sur l'interface ; fermer la fenêtre arrête l'API.
- La fenêtre construit l'API par `ApiLocale.ConstruirePourLaFenetre`. L'environnement de production y est imposé, quelles que soient les variables `ASPNETCORE_ENVIRONMENT` et `DOTNET_ENVIRONMENT` : la page d'exception du développement afficherait les en-têtes d'une requête, jeton compris. La ligne de commande ne lui est pas transmise. Aucun fichier de configuration n'est lu (`appsettings*.json`), ni à côté de l'exe, où un téléchargement peut en déposer un, ni dans le dossier courant ; la racine du contenu est le dossier de données, et aucun observateur de fichiers ne la surveille (`hostBuilder:reloadConfigOnChange` à `false`). Les variables d'environnement gardent leur effet ordinaire sur le reste (journaux, limites de Kestrel) : voir « Limites ».
- Les fichiers de l'interface sont embarqués dans l'exe : rien n'est lu à côté de lui.

*Tests* : `tests/Sollicito.Api.Tests/ApiDeLaFenetreTests.cs` : l'API de la fenêtre sert l'interface et garde ses contrôles ; elle reste en production malgré les deux variables, et ignore trois fichiers `appsettings` posés dans sa racine. `fenetre.test.ts` : l'interface s'affiche à l'origine où l'API écoute ; après la fermeture, l'exe rend 0 et l'API ne répond plus. L'absence d'observateur de fichiers a été vérifiée par une sonde ponctuelle (revue de #13), sans test automatique.

### Version de test

Le test Playwright pilote la vraie fenêtre par le protocole CDP, ouvert dans la seule version de test (configuration `Test`, `src/Sollicito.Fenetre/VersionDeTest.cs`), sur un port de `127.0.0.1` choisi par le test. Elle reprend les réglages de la version publiée, et ne s'en écarte que sur ces points : le port CDP ; un dossier de données donné par le test, sans lequel elle refuse de se lancer ; les liens externes notés dans un fichier au lieu d'être ouverts, et les navigations annulées notées dans un autre ; le dossier de téléchargement placé dans ce dossier, quand la version publiée garde le dossier Téléchargements de l'utilisateur ; une caméra et un micro simulés ; ses messages d'erreur écrits sur la sortie d'erreur, sans boîte de dialogue. Le code de ces écarts n'est pas compilé dans la version publiée, et `dotnet publish -c Test` échoue.

## Textes saisis

Les textes que l'utilisateur saisit (une entreprise, un poste, la note d'une étape) s'affichent comme du texte, jamais comme du HTML : l'interface les écrit par React, qui les échappe, et n'emploie nulle part `dangerouslySetInnerHTML` (ADR-0001, D9, « Contenu non fiable »). Les sauts de ligne d'une note s'affichent par le style (`white-space: pre-wrap`), sans balise. Ces textes ne paraissent dans aucun journal (voir « Règle des journaux »). Un script qui passerait par eux lirait le jeton dans la page (voir « Limites ») : la politique de sécurité du contenu double l'échappement.

*Tests* : `interface/src/candidatures/TableauDeBord.test.tsx`, `SaisieDUneCandidature.test.tsx` et `FicheDUneCandidature.test.tsx` : un nom d'entreprise, un poste et une note porteurs de balises (`<img onerror>`, `<b>`, `<script>`) s'affichent tels quels, et aucune de ces balises ne se crée, ni dans la liste, ni parmi les entreprises proposées, ni dans la fiche. `fenetre.test.ts` : de même dans la vraie fenêtre, après un enregistrement, puis pour la note d'une étape.

## Règle des journaux

Un journal dit ce qui s'est passé, jamais ce qu'une requête contenait. Il n'écrit ni le jeton, ni une clé, ni le contenu des offres, des candidatures ou du profil, ni aucune autre donnée personnelle : ni chaîne de requête, ni en-tête, ni cookie, ni corps. Il peut écrire une étape, un refus, une erreur, un identifiant, un nombre, une durée.

Ce que l'API en applique dès maintenant (`src/Sollicito.Api/Journaux/ReglesDesJournaux.cs`) :

- **La console seule.** Par défaut, ASP.NET Core écrit aussi vers Debug, EventSource et le journal d'événements de Windows[^fournisseurs]. Ce dernier garde ses entrées sur le disque, dans le journal Application, que tout compte connecté au poste peut lire : sa liste d'accès donne la lecture et l'écriture aux utilisateurs interactifs[^journal-application]. Les trois sont retirés.
- **Sept catégories muettes**, quels que soient les réglages, joker ou réglage propre à un fournisseur compris : la fabrique des journaux leur donne un journal qui n'écrit rien.
  - `Microsoft.AspNetCore.Hosting.Diagnostics` écrit l'adresse complète de chaque requête, chaîne de requête comprise (« Request starting », « Request finished »), et attache le chemin de la requête à chaque entrée écrite pendant celle-ci.
  - `Microsoft.AspNetCore.Server.Kestrel.BadRequests` recopie la ligne brute d'une requête mal formée, en-têtes compris.
  - `Microsoft.AspNetCore.Routing.Matching.DfaMatcher` écrit le chemin de chaque requête routée, dans ses journaux de mise au point.
  - `Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware` écrit le chemin de chaque fichier de l'interface servi ou cherché, dès le niveau Information.
  - `Microsoft.EntityFrameworkCore.Database.Connection`, `Microsoft.EntityFrameworkCore.Database.Command` et `Microsoft.EntityFrameworkCore.Migrations` écrivent le chemin du fichier de la base, sous le profil Windows, donc le nom du compte : à l'ouverture et à la fermeture de chaque connexion, à la fermeture de chaque lecture, au début des migrations (niveau Debug). Les requêtes SQL qu'écrirait la deuxième ne portent pas les valeurs : la journalisation des données sensibles reste coupée.
- **Des refus sans valeurs.** L'API note chaque refus par son motif seul, sans la valeur reçue ni l'adresse de la requête.
- **Aucun chemin du poste.** Les messages d'état de l'hôte sont coupés (`SuppressStatusMessages`) : « Content root path » écrirait un chemin du profil Windows, donc le nom du compte. « Now listening on », qui vient du serveur web, reste écrit. Un échec de la préparation de la base ne note que le type de l'erreur, dont le message citerait le chemin du fichier.

Pour les stories suivantes : une route ne porte qu'un identifiant, jamais une donnée. Une nouvelle route, ou un nouveau composant d'ASP.NET Core, peut amener une catégorie qui recopie la requête : elle rejoint alors les catégories muettes, et le test des journaux essaie la nouvelle route. Un message d'exception ne reprend pas le contenu qui l'a provoquée. La journalisation des données sensibles d'Entity Framework Core reste coupée.

*Tests* : `tests/Sollicito.Api.Tests/ReglesDesJournauxTests.cs`. Les journaux ne vont qu'à la console. L'API est lancée avec chaque catégorie au niveau Trace, y compris par un joker qui vise les catégories muettes et par un réglage propre au fournisseur de test, qui garde tout ce qu'un journal pourrait écrire : message, valeurs, exception, portées. Elle reçoit le jeton dans son en-tête, dans la chaîne de requête, dans le chemin, dans un cookie et dans `Authorization`, et une donnée fictive dans la chaîne de requête, le chemin, un en-tête, un cookie, le corps, `Host` et `Origin`, sur des requêtes acceptées comme refusées, fichiers de l'interface compris. Elle reçoit aussi trois requêtes mal formées qui portent l'un et l'autre : ligne de requête, ligne d'en-tête, `Host`. Les routes des candidatures reçoivent la donnée dans l'entreprise et le poste d'une candidature enregistrée, puis relue par les deux listes, et dans des enregistrements refusés : par la validation, par la lecture du corps (JSON mal formé, champ inconnu, type inattendu), par le type du corps et par le contrôle de l'origine. Les routes de la fiche la reçoivent dans la note d'une étape admise, puis relue par la fiche, et dans un statut, une étape et des corps refusés ; elles répondent aussi à un passage et à une correction refusés (409), et à une candidature inconnue (404). Les trois routes de #25 la reçoivent dans l'entreprise et le poste d'une modification admise, qui crée une entreprise, et dans la note d'une correction d'étape admise, puis sur tous leurs chemins d'erreur : refus de la validation, date d'envoi après le premier événement, étape déjà corrigée (409), corps illisibles (JSON mal formé, champ inconnu, statut, type inattendu), type du corps, origine absente, candidature inconnue (404) ; la suppression, admise (204), puis répétée (404). Leurs identifiants viennent des réponses. Aucune entrée ne contient ni le jeton ni la donnée. Le test échoue dès qu'on rend la parole à l'une des sept catégories muettes. Aucune entrée ne contient non plus la racine du contenu, qu'écrirait « Content root path », ni le dossier de la base. Un second test fait échouer une route par une exception dont le message cite la donnée : seul son type paraît dans les journaux ; un troisième fait de même pour la modification et la correction d'une étape, dont le corps porte la donnée, et pour la suppression.

## Limites

- **Un programme lancé sous le compte de l'utilisateur** n'est arrêté par rien de ce qui précède. Il peut lire la mémoire du processus, jeton compris, et le dossier de données. Il peut lancer Sollicito avec ses propres arguments ou variables d'environnement : `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` suffit à ouvrir le débogage distant de la fenêtre (ADR-0003, « Conséquences »). Il peut aussi écrire dans le dossier où l'exe extrait ses bibliothèques natives, sous `%TEMP%\.net`, qui reste d'un lancement à l'autre (prototype de #9, ADR-0003, « À vérifier »). Seule la frontière du compte Windows le tient à distance ; la D9 le dit déjà de DPAPI.
- **Un administrateur du poste** peut tout voir, trafic de `127.0.0.1` compris. Le pilote de capture Npcap, qu'installe notamment Wireshark, ne réserve l'accès à ses périphériques de capture aux administrateurs que si l'option `/admin_only` est choisie à l'installation, ce qui n'est pas le cas par défaut[^npcap] : sur un poste où il est installé sans elle, un autre compte pourrait lire ce trafic, jeton compris.
- **Le trafic n'est pas chiffré** : HTTP sur `127.0.0.1`, qui ne quitte pas la machine.
- **Un client qui n'est pas un navigateur** écrit `Host` et `Origin` comme il veut. Ces deux contrôles arrêtent les pages web ; devant un programme, seul le jeton compte.
- **Le jeton dans l'interface**, une fois remis par la fenêtre, vit dans la mémoire de la page. Un script injecté dans la page pourrait le lire et appeler l'API : d'où l'affichage échappé du contenu et la politique de sécurité du contenu (ADR-0001, D9, « Contenu non fiable » et « Fenêtre »).
- **Aucune limite au nombre d'essais** : deviner 256 bits est hors de portée. Une avalanche de requêtes qui ralentirait l'application n'est pas traitée.
- **Les arguments ajoutés au navigateur intégré** par une variable d'environnement (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`) ou une stratégie de Windows valent aussi pour la version publiée : ils peuvent ouvrir le débogage distant de la fenêtre, donc lui prendre le jeton. Il faut pour cela agir déjà sous le compte de l'utilisateur (premier point). Une fenêtre qui ignore ces réglages attend la V2.
- **WebRTC**, à confirmer : ni la politique de sécurité du contenu, qui n'a pas de directive pour lui dans Chromium, ni probablement le filtre des requêtes ne voient les échanges d'une `RTCPeerConnection` avec un serveur STUN ou TURN. Un script injecté qui aurait passé la politique pourrait s'en servir pour faire sortir le jeton, sans clic. Parade à évaluer : retirer `RTCPeerConnection` par un script posé à la création du document (revue de #13).
- **Le runtime WebView2 a ses propres échanges** avec Microsoft (données de diagnostic, mises à jour ; ADR-0003, « À vérifier ») : ils ne viennent pas de la page, et ni le filtre des requêtes ni la politique de sécurité du contenu ne les voient.
- **Une connexion WebSocket**, que la V1 n'a pas, ne pourrait pas porter l'en-tête du jeton : le navigateur ne permet pas d'en ajouter un à l'ouverture d'un WebSocket. Il lui faudrait sa propre règle, avec le contrôle d'`Origin` que demande la D9.

## À reprendre

- **Brouillon de relance (#14)** : la page demande l'ouverture du lien `mailto` à l'hôte par un message, soumis au même contrôle de la source que le canal du jeton (ADR-0003) ; tout autre schéma externe reste annulé.
- **Interface en développement** : le serveur de développement de Vite sert une autre origine. L'API refuse ses écritures, et l'interface ne reçoit le jeton que dans la fenêtre.
- **Exe** : le dossier d'extraction `%TEMP%\.net` (voir « Limites »), et un exe construit par la CI, dont les symboles ne portent pas le chemin d'un poste de développement (prototype de #9).

## Sources

Consultées le 2026-11-18 :

[^generateur]: Microsoft Learn, `RandomNumberGenerator.GetBytes(Int32)` (« cryptographically strong random sequence of values ») : <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getbytes>
[^comparaison]: Microsoft Learn, `CryptographicOperations.FixedTimeEquals` : <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals>
[^origin]: MDN, en-tête `Origin` : <https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Origin>
[^fournisseurs]: Microsoft Learn, journalisation dans ASP.NET Core, fournisseurs par défaut de `WebApplication.CreateBuilder` : <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/>
[^journal-application]: Relevé sur le poste de développement par `wevtutil gl Application` : fichier `%SystemRoot%\System32\Winevt\Logs\Application.evtx`, liste d'accès `(A;;0x3;;;IU)`, soit lecture et écriture pour les utilisateurs interactifs.
[^npcap]: Npcap, guide de l'utilisateur, options de l'installeur (`/admin_only`) : <https://npcap.com/guide/npcap-users-guide.html>

Consultée le 2026-11-19 :

[^smartscreen]: Microsoft Learn, données et confidentialité de WebView2, « SmartScreen » : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/data-privacy>
