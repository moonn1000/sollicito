# Modèle de menaces

- **Mis à jour le** : 2026-11-18, avec l'API locale (#10)
- **Décisions** : [ADR-0001](adr/0001-cadrage-architecture-v1.md), D9 ; [ADR-0003](adr/0003-hote-interface-webview2.md), « Règles d'accès »
- **Périmètre** : l'API locale et ses journaux. La fenêtre hôte (#13) ajoutera sa section ; le contenu non fiable, les clés d'IA, les sauvegardes et l'exe ajouteront les leurs avec les stories qui les construisent.

## Ce qui est protégé

- **Les données de l'utilisateur** : ses candidatures, les entreprises et leurs contacts, ses relances, ses offres. Elles retracent sa recherche d'emploi, ses refus, parfois ses prétentions salariales. L'API locale les sert à l'interface : qui l'appelle à la place de l'interface peut les lire ou les modifier.
- **Le jeton de lancement** : il ouvre l'API, il est donc protégé comme les données.

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

Chaque requête passe ces contrôles dans cet ordre (`src/Sollicito.Api/ApiLocale.cs`). Le premier qui échoue répond, sans corps, et la requête ne va pas plus loin.

1. **En-tête `Host`** (400), sur toute requête.
2. *Fichiers de l'interface, à venir avec #13* : servis sans jeton ni contrôle d'`Origin`, puisque la page les charge avant de recevoir le jeton. Ils sont publics et ne portent aucune donnée (ADR-0003). D'ici là, aucune requête n'échappe aux deux contrôles suivants.
3. **Jeton** (401), sur tout le reste : routes de l'API, documentation OpenAPI, adresses inconnues.
4. **En-tête `Origin`** (403), sur ce même reste.

Le routage ne vient qu'ensuite : une requête refusée n'est jamais routée.

### Écoute sur 127.0.0.1

Kestrel n'écoute que sur `127.0.0.1`, sur un port libre choisi à chaque lancement ; aucune interface réseau n'est ouverte. L'écoute ne se règle que dans le code :

- les adresses données par la configuration (`urls`, section `Kestrel`) sont ignorées, et la section `Kestrel` n'est pas relue en cours de route ;
- une configuration qui demande à Kestrel de préférer ses propres adresses (`preferHostingUrls`) fait échouer le lancement avant toute écoute ;
- une fois Kestrel démarré, l'API relève l'adresse réellement ouverte. Si une autre écoute s'est ouverte, elle arrête Kestrel et le lancement échoue.

Le port n'est pas un secret : tout programme du poste le lit dans la liste des connexions, et une page web peut sonder les ports locaux. Il ne donne accès à rien sans le jeton.

*Tests* : `tests/Sollicito.Api.Tests/EcouteDeLApiTests.cs`. Une seule adresse est ouverte, en `127.0.0.1`. Aucune autre adresse du poste ne répond sur le port de l'API, ni celle d'une carte réseau, ni `[::1]`. Une adresse donnée par `urls` ou par `Kestrel:Endpoints` reste sans effet, et une écoute ajoutée à chaud dans la configuration ne s'ouvre pas. Avec `preferHostingUrls`, le lancement échoue sans qu'aucune écoute ne se soit ouverte, et l'échec paraît dans les journaux. Une écoute de plus, ouverte par le code, fait échouer le lancement. Ces essais d'écoute interdite visent `127.0.0.2`, une autre adresse locale : une écoute sur `0.0.0.0` ouvrirait l'invite du pare-feu de Windows sur le poste qui lance les tests.

### Jeton de lancement

- 32 octets, soit 256 bits, tirés à chaque lancement par `RandomNumberGenerator`, générateur cryptographique de .NET[^generateur]. Le jeton ne vit qu'en mémoire : aucune route ne le rend, et il n'est écrit ni dans un fichier, ni sur la ligne de commande, ni dans un journal. Son texte (`ToString`) ne montre pas sa valeur. La fenêtre hôte le lira dans les services de l'API et le remettra à l'interface par son canal interne (#13).
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

*Tests* : `tests/Sollicito.Api.Tests/ControleDeLOrigineTests.cs`. Avec le bon jeton, une écriture (`POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`) sans origine ou d'origine `null` est refusée ; un `POST` ou une lecture d'une autre origine aussi, sur neuf origines essayées. Une écriture venue de l'interface passe les contrôles : aucune route n'accepte encore d'écriture, d'où la réponse 405 de la route de l'état.

### Aucune politique CORS

CORS assouplit la règle de même origine du navigateur ; il ne protège pas le serveur. L'API n'en déclare aucune : aucune réponse ne porte d'en-tête `Access-Control-*`, pas même la réponse à une requête de contrôle préalable. L'interface est servie à la même origine que l'API et n'en a pas besoin.

*Tests* : `tests/Sollicito.Api.Tests/AucuneOuvertureCorsTests.cs`.

## Règle des journaux

Un journal dit ce qui s'est passé, jamais ce qu'une requête contenait. Il n'écrit ni le jeton, ni une clé, ni le contenu des offres, des candidatures ou du profil, ni aucune autre donnée personnelle : ni chaîne de requête, ni en-tête, ni cookie, ni corps. Il peut écrire une étape, un refus, une erreur, un identifiant, un nombre, une durée.

Ce que l'API en applique dès maintenant (`src/Sollicito.Api/Journaux/ReglesDesJournaux.cs`) :

- **La console seule.** Par défaut, ASP.NET Core écrit aussi vers Debug, EventSource et le journal d'événements de Windows[^fournisseurs]. Ce dernier garde ses entrées sur le disque, dans le journal Application, que tout compte connecté au poste peut lire : sa liste d'accès donne la lecture et l'écriture aux utilisateurs interactifs[^journal-application]. Les trois sont retirés.
- **Deux catégories muettes**, quels que soient les réglages, joker ou réglage propre à un fournisseur compris : la fabrique des journaux leur donne un journal qui n'écrit rien.
  - `Microsoft.AspNetCore.Hosting.Diagnostics` écrit l'adresse complète de chaque requête, chaîne de requête comprise (« Request starting », « Request finished »), et attache le chemin de la requête à chaque entrée écrite pendant celle-ci.
  - `Microsoft.AspNetCore.Server.Kestrel.BadRequests` recopie la ligne brute d'une requête mal formée, en-têtes compris.
- **Pas de routage avant les refus.** Le routage écrit le chemin des requêtes dans ses journaux de mise au point ; une requête refusée n'y arrive pas.
- **Des refus sans valeurs.** L'API note chaque refus par son motif seul, sans la valeur reçue ni l'adresse de la requête.

Pour les stories suivantes : le chemin d'une requête acceptée, lui, peut paraître dans les journaux de mise au point du routage ; une route ne porte donc qu'un identifiant, jamais une donnée. Un message d'exception ne reprend pas le contenu qui l'a provoquée. La journalisation des données sensibles d'Entity Framework Core reste coupée.

*Tests* : `tests/Sollicito.Api.Tests/ReglesDesJournauxTests.cs`. Les journaux ne vont qu'à la console. L'API est lancée avec chaque catégorie au niveau Trace, y compris par un joker qui vise les catégories muettes et par un réglage propre au fournisseur de test, qui garde tout ce qu'un journal pourrait écrire : message, valeurs, exception, portées. Elle reçoit le jeton dans son en-tête, dans la chaîne de requête, dans le chemin, dans un cookie et dans `Authorization`, et une donnée fictive dans la chaîne de requête, le chemin, un en-tête, un cookie, le corps, `Host` et `Origin`. Elle reçoit aussi trois requêtes mal formées qui portent l'un et l'autre : ligne de requête, ligne d'en-tête, `Host`. Aucune entrée ne contient ni le jeton ni la donnée. Le test échoue dès qu'on rend la parole à l'une des deux catégories muettes, ou qu'on remet le routage avant les refus.

## Limites

- **Un programme lancé sous le compte de l'utilisateur** n'est arrêté par rien de ce qui précède. Il peut lire la mémoire du processus, jeton compris, et le dossier de données. Il peut lancer Sollicito avec ses propres arguments ou variables d'environnement : `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` suffit à ouvrir le débogage distant de la fenêtre (ADR-0003, « Conséquences »). Il peut aussi écrire dans le dossier où l'exe extrait ses bibliothèques natives, sous `%TEMP%\.net`, qui reste d'un lancement à l'autre (prototype de #9, ADR-0003, « À vérifier »). Seule la frontière du compte Windows le tient à distance ; la D9 le dit déjà de DPAPI.
- **Un administrateur du poste** peut tout voir, trafic de `127.0.0.1` compris. Le pilote de capture Npcap, qu'installe notamment Wireshark, ne réserve l'accès à ses périphériques de capture aux administrateurs que si l'option `/admin_only` est choisie à l'installation, ce qui n'est pas le cas par défaut[^npcap] : sur un poste où il est installé sans elle, un autre compte pourrait lire ce trafic, jeton compris.
- **Le trafic n'est pas chiffré** : HTTP sur `127.0.0.1`, qui ne quitte pas la machine.
- **Un client qui n'est pas un navigateur** écrit `Host` et `Origin` comme il veut. Ces deux contrôles arrêtent les pages web ; devant un programme, seul le jeton compte.
- **Le jeton dans l'interface**, une fois remis par la fenêtre (#13), vit dans la mémoire de la page. Un script injecté dans la page pourrait le lire et appeler l'API : d'où l'affichage échappé du contenu et la politique de sécurité du contenu (ADR-0001, D9, « Contenu non fiable » et « Fenêtre »).
- **Aucune limite au nombre d'essais** : deviner 256 bits est hors de portée. Une avalanche de requêtes qui ralentirait l'application n'est pas traitée.
- **Une connexion WebSocket**, que la V1 n'a pas, ne pourrait pas porter l'en-tête du jeton : le navigateur ne permet pas d'en ajouter un à l'ouverture d'un WebSocket. Il lui faudrait sa propre règle, avec le contrôle d'`Origin` que demande la D9.

## À reprendre

- **Fenêtre hôte (#13)** : servir les fichiers de l'interface entre le contrôle de `Host` et celui du jeton, à l'endroit marqué dans `ApiLocale.cs`, et ajouter ici la section de la fenêtre. La fenêtre lit l'adresse (`AdresseDEcoute.Origine`) et le jeton (`JetonDeLancement.Valeur()`) dans les services de l'API. Le serveur de développement de l'interface (Vite) sert une autre origine : l'API refusera ses écritures tant qu'elles ne passent pas par l'origine de l'API.
- **Premières routes qui lisent un corps (#11), et fenêtre (#13)** : en développement, ASP.NET Core place sa page d'exception en tête du chemin des requêtes. Si une route lève une exception, cette page affiche les en-têtes de la requête, jeton compris, dans la réponse. Aucune route n'en lève aujourd'hui : « aucune réponse ne contient le jeton » ne tient qu'à cela.
- **Exe** : le dossier d'extraction `%TEMP%\.net` (voir « Limites »), et un exe construit par la CI, dont les symboles ne portent pas le chemin d'un poste de développement (prototype de #9).

## Sources

Consultées le 2026-11-18 :

[^generateur]: Microsoft Learn, `RandomNumberGenerator.GetBytes(Int32)` (« cryptographically strong random sequence of values ») : <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getbytes>
[^comparaison]: Microsoft Learn, `CryptographicOperations.FixedTimeEquals` : <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals>
[^origin]: MDN, en-tête `Origin` : <https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Origin>
[^fournisseurs]: Microsoft Learn, journalisation dans ASP.NET Core, fournisseurs par défaut de `WebApplication.CreateBuilder` : <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/>
[^journal-application]: Relevé sur le poste de développement par `wevtutil gl Application` : fichier `%SystemRoot%\System32\Winevt\Logs\Application.evtx`, liste d'accès `(A;;0x3;;;IU)`, soit lecture et écriture pour les utilisateurs interactifs.
[^npcap]: Npcap, guide de l'utilisateur, options de l'installeur (`/admin_only`) : <https://npcap.com/guide/npcap-users-guide.html>
