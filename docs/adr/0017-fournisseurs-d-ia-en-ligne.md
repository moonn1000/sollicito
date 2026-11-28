# ADR-0017 : les fournisseurs d'IA en ligne, en HTTP sans SDK, à leurs adresses fixées, sans redirection ni nouvelle tentative

- **Statut** : acceptée ; critères et décisions acceptés par le pilote le 2026-11-25 ; le modèle fixé gardé, une demande sans donnée refusée avant le port et la porte de la base tenue le temps de la lecture, précisés par l'[ADR-0019](0019-brouillon-de-relance-redige-par-l-ia.md) (le brouillon de relance rédigé par l'IA) ; la vie de la clé en mémoire corrigée le 2026-11-27 dans les conséquences négatives (#108, points 5 et 6) : des chaînes qui restent au-delà du temps de l'appel, et la lecture de l'état de l'IA, qui la déchiffre aussi ; la décision ne change pas
- **Date** : 2026-11-25
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D7 (« une clé, une adresse » tenue à l'envoi ; les fournisseurs en ligne de la V1) et D9 (« Secrets et journaux » : les redirections, les journaux d'un client HTTP) ; [ADR-0002](0002-budget-nul.md), « Décision » (les fournisseurs en ligne testés sur des réponses fictives : un faux transport) ; [ADR-0016](0016-fournisseur-d-ia-et-cle-chiffree.md), « Le port du fournisseur » (les issues d'une demande, l'essai de la clé qu'il laissait à #48, « pas encore branché » réservé à Ollama)

## Contexte

La story #48 branche Claude et OpenAI derrière le port du fournisseur de #43 (ADR-0016). Le mainteneur a fixé les fournisseurs en ligne de la V1 sur l'issue, le 2026-11-18 : Claude et OpenAI ; Mistral attend la V2, comme le dit l'ADR-0004 (D7). Ce sont les premières requêtes que le code de Sollicito envoie hors du poste : jusqu'ici, seul le runtime WebView2 avait ses propres échanges.

Plusieurs décisions les cadrent déjà : chaque clé est liée à l'adresse officielle de son fournisseur, fixée dans le code et appelée en HTTPS (ADR-0001, D7) ; aucun journal ne contient la clé (D9) ; les fournisseurs en ligne ne se testent que sur des réponses fictives (ADR-0002) ; le port ne donne à l'IA ni outil, ni adresse, ni en-tête, et sa réponse est un texte borné ou une issue fermée (ADR-0016).

Cinq faits, relevés le 2026-11-25, pèsent sur la façon de faire :

- **Une redirection suivie par .NET** ne retire de la requête que `Authorization` et `Proxy-Authorization`[^redirection]. L'en-tête `x-api-key`, qui porte la clé de Claude, suivrait une redirection vers un autre hôte en HTTPS.
- **Le SDK C# d'Anthropic** prend son adresse dans la variable d'environnement `ANTHROPIC_BASE_URL` quand le code n'en donne pas, refait deux tentatives par défaut (coupure, délai, 408, 409, 429, 5xx), et décompresse les réponses[^sdk].
- **Les journaux d'un client HTTP de .NET** ne viennent que de sa fabrique (`IHttpClientFactory`) ; un client construit à part n'en écrit aucun. Le texte d'une requête (`HttpRequestMessage.ToString()`) montre ses en-têtes.
- **.NET ajoute d'office un en-tête de traçage** (`traceparent`) à une requête envoyée pendant une activité, ce qui est le cas pendant une requête de l'API ; le réglage `ActivityHeadersPropagator` du gestionnaire le coupe.
- **OpenAI garde ses réponses** au moins 30 jours, sauf si la requête demande `store: false`[^store].

## Décision

### HTTP sans SDK, un transport réglé dans le code

Chaque adaptateur envoie sa requête par un transport à part : un `HttpMessageInvoker`, la base de `HttpClient`, sans son délai ni sa mise en tampon, sur un `SocketsHttpHandler` construit au premier usage, jamais au lancement, et sans la fabrique de .NET. Aucune configuration, aucune variable d'environnement ne règle l'adresse ni ce réglage :

- **aucune redirection suivie** : une réponse 3xx revient telle quelle, et vaut une réponse illisible ;
- **aucun cookie, aucun identifiant Windows**, proxy compris ;
- **aucune décompression** : aucun `Accept-Encoding` ne part, et la borne de taille compte les octets reçus ; un corps codé (gzip…) est illisible ;
- **aucune connexion réutilisée** : une connexion neuve par demande, donc aucune reprise de .NET sur une connexion que le serveur aurait fermée ;
- **aucun en-tête de traçage** : ni `traceparent`, ni `Request-Id` ;
- **la validation TLS du système**, sans rappel ;
- **le proxy du système**, avec ses réglages et ses variables d'environnement (`HTTPS_PROXY`…) : ils peuvent faire passer la connexion par un proxy, que le TLS traverse de bout en bout, mais jamais changer l'adresse.

### Les adresses et les requêtes, fixées dans le code

| | Claude | OpenAI |
| --- | --- | --- |
| Requête | `POST https://api.anthropic.com/v1/messages`[^messages] | `POST https://api.openai.com/v1/responses`[^responses] |
| La clé | en-tête `x-api-key`, et nulle part ailleurs | en-tête `Authorization: Bearer`, et nulle part ailleurs |
| Autres en-têtes | `anthropic-version: 2026-10-21`[^versions], `content-type: application/json` | `content-type: application/json` |
| Le modèle | `model: "claude-haiku-5-5"`[^modeles] | `model: "gpt-6-luna"`[^luna] |
| Les consignes | `system` | `instructions` |
| Les données | le seul message de l'utilisateur, `messages: [{"role": "user", "content": …}]` | `input`, une chaîne, au rôle de l'utilisateur |
| La borne | `max_tokens` | `max_output_tokens` (16 au moins, raisonnement compris) |
| Le raisonnement | `thinking: {"type": "disabled"}`[^thinking] | `reasoning: {"effort": "none"}` |
| La garde chez le fournisseur | — | `store: false` |

Rien d'autre ne part : ni en-tête d'organisation ou de projet, ni champ `user` ou `metadata`. Les données ne sont pas entourées de balises qu'une donnée piégée pourrait fermer : elles sont seules dans leur champ, échappées par l'écriture du JSON. Une demande sans données envoie une phrase fixe, « Aucune donnée. » : un fournisseur refuse un message vide.

### La clé, lue au moment de l'appel

L'adaptateur lit la clé dans `DemanderAsync`, par la garde de la clé, pour son seul fournisseur : la charge chiffrée porte son fournisseur (ADR-0016), et une clé d'un autre est illisible. Absente ou illisible, aucune requête ne part : le port rend « indisponible ». L'adaptateur ne garde pas la clé d'un appel à l'autre, et aucune requête n'est mise en texte ni écrite dans un journal. Enregistrer une clé ne déclenche aucun appel, et changer de fournisseur l'efface toujours (#43).

### Le modèle, fixé dans le code

Un modèle par fournisseur, une constante de son adaptateur : `claude-haiku-5-5` et `gpt-6-luna`, les plus rapides et les moins chers de chacun, au même prix (0,10 $ en entrée et 0,50 $ en sortie par million de jetons, relevé le 2026-11-25), raisonnement coupé : la borne de la réponse ne couvre que son texte. Une extraction d'offre d'environ 3 000 jetons coûte ainsi environ 0,0006 $ à l'utilisateur, et un essai de la clé environ 0,00001 $.

Quand le fournisseur retire le modèle, l'appel rend 404 chez Anthropic[^retraits-anthropic] (chez OpenAI, aucune page lue ne donne le statut : voir « À vérifier ») ; l'issue du 404 est « modèle indisponible », et l'écran dit d'installer la dernière version de Sollicito, qui change la constante. Anthropic annonce le retrait de `claude-haiku-5-5` pas avant le 7 octobre 2027 ; OpenAI prévient au moins six mois avant de retirer un modèle généralement disponible[^retraits].

### Un appel borné, une seule tentative

- **La durée** est celle de la demande (trois minutes au plus) : la connexion, les en-têtes et la lecture du corps. Son dépassement rend « délai dépassé ». Seule l'annulation par l'appelant lève une exception.
- **La taille** : un corps annoncé de plus de 256 Kio n'est pas lu ; la lecture s'arrête au premier octet de trop. Le texte rendu ne dépasse pas la borne de la demande. Les jetons demandés valent la moitié des caractères admis, entre 16 et 10 000.
- **Aucune nouvelle tentative** : ni sur 429, ni sur 5xx, ni sur un délai dépassé ; `Retry-After` n'est jamais suivi d'office. Chaque appel coûte.
- **Un JSON strict** : le type `application/json`, comparé sans ses paramètres (`charset=utf-8` admis) ; ni propriété répétée, ni commentaire, ni virgule finale ; une profondeur bornée ; chaque champ lu doit avoir le type attendu. Les champs inconnus sont ignorés : les fournisseurs en ajoutent sans prévenir.
- **Une réponse non fiable** : son texte revient brut à l'appelant, qui le valide (#46) ou l'affiche en texte brut (#47). Il n'entre dans aucun journal.

### Les issues, en liste fermée

L'ADR-0016 laissait aux fournisseurs en ligne le détail de l'échec. Les issues du port gagnent six valeurs : clé refusée, quota atteint, accès refusé, modèle indisponible, fournisseur en panne, et une demande déjà en cours ; « refus du fournisseur » ne couvre plus que la demande ou le contenu refusés. **Une erreur se classe par son seul statut HTTP : son corps n'est jamais lu**, ni son message, ni son identifiant de requête.

| Ce qui revient | Issue |
| --- | --- |
| 200, texte complet (`end_turn`, `stop_sequence` ; `completed`) | le texte |
| 200, réponse coupée (`max_tokens`, `model_context_window_exceeded` ; `incomplete` par `max_output_tokens`) | réponse trop longue |
| 200, contenu refusé (`refusal` ; une partie `refusal`, `incomplete` par `content_filter`) | refus du fournisseur |
| 200, autre fin (`failed` compris), aucun texte, autre forme ; un autre 2xx ; une redirection ; tout autre statut | réponse illisible |
| 400, 413, 422 | refus du fournisseur |
| 401 | clé refusée |
| 402, 429 (débit, crédit, plafond de dépense) | quota atteint |
| 403 | accès refusé |
| 404 | modèle indisponible |
| 408, 500, 502, 503, 504, 529 | fournisseur en panne |
| Nom introuvable, connexion ou TLS refusés, proxy en échec, connexion coupée | injoignable |
| Durée dépassée | délai dépassé |
| Corps ou texte au-delà des bornes | réponse trop longue |

Chez Anthropic, un plafond de dépense que l'utilisateur a fixé lui-même rend un 400 (le plafond mensuel de la plateforme rend un 429)[^messages] : il se classe, comme tout 400, avec le refus de la demande, et l'écran renvoie au compte de l'utilisateur chez Anthropic.

Le texte de chaque issue, pour chaque fournisseur, vit dans l'application : l'essai de la clé s'en sert, l'extraction d'une offre (#46) et le brouillon de relance (#47) le reprendront.

### Une demande à l'IA à la fois

Une porte commune aux fournisseurs en ligne : une demande qui arrive pendant une autre ne l'attend pas, elle rend aussitôt « une demande déjà en cours », sans requête. L'essai de la clé, l'extraction d'une offre et le brouillon de relance en héritent.

### L'essai de la clé

L'ADR-0016 laissait l'essai à #48 : un vrai appel ne pouvait venir qu'avec un adaptateur réel. Un bouton « Tester la clé » paraît dans la section « IA » quand une clé de Claude ou d'OpenAI est enregistrée. Son clic appelle `POST /api/reglages/ia/essai`, sans corps ; l'API pose une seule demande par le port : la consigne fixe « Réponds uniquement par le mot OK. », la donnée fixe « Essai de connexion de Sollicito. », 20 caractères et 20 secondes au plus. Aucune donnée de l'utilisateur ne part.

- L'essai réussit si un texte revient ; ce texte n'est ni montré ni comparé.
- Il ne part jamais tout seul : ni à l'enregistrement d'une clé, ni au lancement, ni à l'ouverture des réglages.
- La route rend 200, avec le résultat et son texte ; 409, sans requête, si aucune clé n'est prête ou si une demande est en cours ; 400 avec un corps. Elle passe par les protections de #10, figure dans OpenAPI, et ne touche pas la base : « sans la base », elle reste ouverte pendant une restauration.

### Les journaux

Une entrée par demande partie : le fournisseur et l'issue, deux valeurs de listes fermées, le statut HTTP (un nombre) et la durée ; le type de l'erreur d'un transport injoignable. Une demande refusée par la porte, ou sans clé prête, le dit aussi ; une demande que l'appelant annule n'écrit aucune entrée. Jamais la clé, sa longueur, son début ou son empreinte ; ni un en-tête, ni un corps, de la requête comme de la réponse ; ni l'adresse, ni le message ou l'identifiant de requête du fournisseur ; ni les consignes, ni les données, ni le texte de l'IA ; ni le message d'une exception. Sans fabrique, aucun journal de client HTTP n'existe.

### Les tests, sur des réponses fictives

- **Les adaptateurs**, sur un faux gestionnaire HTTP : l'adresse, les en-têtes et le corps de chaque requête, chaque ligne de la table des issues, les bornes, le délai, l'annulation, la porte, les journaux. Les deux passent le banc de contrat du port.
- **Le vrai transport** : le gestionnaire de production, contre des écoutes sur 127.0.0.1 qui écrivent leurs réponses à la main. Une redirection n'emporte pas la clé, aucun cookie ne repart, aucune compression n'est demandée, deux demandes ouvrent deux connexions, un corps sans fin et un serveur qui se tait sont coupés.
- **L'API lancée par les tests** pose toujours un faux à la place du transport. Sans réponse prévue, il échoue, et le test aussi : aucun test n'atteint le réseau.
- **La version de test de la fenêtre** remplace le transport par un faux fournisseur, que pilote un fichier du dossier de données. Il note chaque demande avec l'empreinte SHA-256 de la clé, jamais sa valeur. Il n'existe que dans la version de test, qui ne se publie pas.

## Alternatives écartées

- **Les SDK officiels** : une variable d'environnement d'adresse à neutraliser, des tentatives et une décompression à couper, et deux dépendances de plus à auditer.
- **La fabrique de clients HTTP de .NET** : ses journaux écrivent l'adresse de chaque requête, et masquer les en-têtes est un réglage de plus à tenir.
- **Suivre les redirections** : `x-api-key` partirait vers l'hôte de la redirection.
- **Un modèle réglable** dans `reglages.json` : il survit à un retrait sans nouvelle version, mais c'est un réglage, un champ et sa validation de plus, et un champ libre peut désigner un modèle coûteux.
- **Le modèle intermédiaire** (`claude-sonnet-5-5`, `gpt-6-sol`, 2 $ et 10 $ par million de jetons) : une meilleure rédaction, pour vingt fois le prix. #47 pourra le choisir pour le brouillon seul. **Le modèle par défaut d'Anthropic** (`claude-opus-5-5`, 4 $ et 20 $) : son raisonnement ne se coupe pas.
- **Chat Completions d'OpenAI** : OpenAI conseille l'API Responses aux nouveaux projets.
- **Lire le corps des erreurs** : son texte n'est pas fiable, et peut reprendre une partie de la demande ; le statut suffit à dire ce qui est arrivé.
- **Une nouvelle tentative automatique** : chaque appel coûte, et un quota épuisé ne revient pas en réessayant.
- **Attendre la porte** au lieu de refuser : les demandes s'empileraient, et chacune coûterait.
- **`GET /v1/models/<modèle>` pour l'essai** : gratuit, mais il ne prouve ni le crédit ni la lecture d'une vraie réponse ; un essai réussi pourrait précéder un vrai appel qui échoue.
- **Aucun proxy** : les réseaux qui en exigent un ne joindraient plus aucun fournisseur.

## Conséquences

- **Positives** : une clé ne part que vers l'adresse officielle de son fournisseur, dans son seul en-tête, jamais vers l'hôte d'une redirection ; aucun journal de client HTTP ; un échec se dit sans le texte du fournisseur ; un essai sur un clic, sans aucune donnée ; un port prêt pour l'extraction d'une offre (#46) et le brouillon de relance (#47).
- **Négatives** :
  - un modèle retiré prive l'utilisateur d'IA jusqu'à ce qu'il installe une nouvelle version : la V1 n'a pas de mise à jour automatique ;
  - une connexion neuve par demande ajoute une poignée de main TLS à chaque appel ;
  - sans nouvelle tentative, une panne passagère demande un nouveau clic ;
  - la clé vit en mémoire, dans des chaînes que .NET ne remet pas à zéro, au-delà du temps de l'appel : la lecture de l'état de l'IA, qui la déchiffre, en produit aussi (#108, points 5 et 6) ;
  - un script injecté qui aurait le jeton pourrait enchaîner les essais et dépenser le crédit de l'utilisateur : un à la fois, chacun borné, mais pas leur nombre dans le temps (modèle de menaces, « Limites ») ;
  - un programme lancé sous le compte de l'utilisateur peut poser un proxy et un certificat racine dans le magasin de ce compte, et lire la clé au passage : c'est la limite de DPAPI (ADR-0001, D9) ;
  - ce que le fournisseur fait de ce qu'il reçoit relève de ses conditions ; `store: false` retire seulement la garde des réponses d'OpenAI.

## À vérifier

- **Le premier vrai appel** se fera par « Tester la clé », après la fusion, si le mainteneur le veut. Les formes des requêtes sont recoupées avec la documentation de chaque fournisseur, jamais avec son API réelle : les faux ne prouvent que ce que Sollicito envoie.
- **L'en-tête de la clé de Claude** : `x-api-key`, que la page d'aperçu de l'API appelle un repli « legacy », toujours pris en charge, l'en-tête principal étant `Authorization: Bearer`. Si Anthropic retirait le repli, l'essai rendrait « clé refusée » (401) pour une clé bonne, et une nouvelle version de Sollicito changerait l'en-tête.
- **L'en-tête `anthropic-workspace-id`** : la page d'aperçu de l'API le dit requis avec une clé qui n'est pas limitée à un workspace, et facultatif sinon ; une clé créée pour un seul workspace s'en passe. Sollicito ne l'envoie pas (aucun en-tête d'organisation ou de projet) : une clé sans cette limite échouerait à l'essai. Le guide « Démarrer » devra dire de créer la clé pour un seul workspace.
- **Le statut d'un modèle retiré chez OpenAI** : aucune page lue ne le donne (la page des retraits dit seulement que le modèle n'accepte plus de requêtes). La table range le 404 sous « modèle indisponible », comme chez Anthropic, où la page des retraits le confirme.
- **Ce que chaque fournisseur garde** des demandes qu'il reçoit, à lire dans ses conditions pour le guide « Démarrer ».

## Sources

- Story : <https://github.com/moonn1000/sollicito/issues/48> ; le choix des fournisseurs, commentaire du mainteneur du 2026-11-18.
- [ADR-0001](0001-cadrage-architecture-v1.md), D7 et D9 ; [ADR-0002](0002-budget-nul.md) ; [ADR-0004](0004-v1-resserree-paliers.md), D7 ; [ADR-0016](0016-fournisseur-d-ia-et-cle-chiffree.md).

Documentation officielle, consultée le 2026-11-25 (aucune page n'affiche de date de mise à jour) :

[^redirection]: .NET, `RedirectHandler.cs` du dépôt `dotnet/runtime`, branche `main` : « Clear the authorization headers » (`Authorization`, `Proxy-Authorization`), et aucune redirection de HTTPS vers HTTP : <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/RedirectHandler.cs>
[^messages]: Anthropic, Create a Message (`POST /v1/messages` ; `system` admet une chaîne ; `thinking` de type `disabled` ; `content` en blocs `text` ; `stop_reason` : `end_turn`, `max_tokens`, `stop_sequence`, `tool_use`, `pause_turn`, `refusal`, `model_context_window_exceeded`) : <https://platform.claude.com/docs/en/api/messages/create.md>. Son exemple cURL donne l'hôte (`https://api.anthropic.com/v1/messages`) et `anthropic-version: 2026-10-21`, avec la clé en `Authorization: Bearer`. La page d'aperçu de l'API donne `Authorization: Bearer` comme en-tête d'authentification, et `x-api-key` comme repli « legacy », toujours pris en charge : <https://platform.claude.com/docs/en/api/overview.md>. Sollicito envoie `x-api-key`, comme les exemples cURL de la page Thinking (`curl https://api.anthropic.com/v1/messages`, en-têtes `x-api-key`, `anthropic-version`, `content-type`). La page des erreurs donne les codes (400, 401, 402 `billing_error`, 403, 404, 413, 429, 500, 529 `overloaded_error`), dont un 400 quand l'usage atteint un plafond de dépense que l'utilisateur a fixé, alors que le plafond mensuel rend un 429 `enforced_spend_limit_reached` : <https://platform.claude.com/docs/en/build-with-claude/thinking.md>, <https://platform.claude.com/docs/en/api/errors.md>
[^versions]: Anthropic, versions de l'API (`2026-10-21` en tête de l'historique, puis `2023-06-01`) : <https://platform.claude.com/docs/en/api/versioning.md>
[^modeles]: Anthropic, Models overview (`claude-haiku-5-5`, un instantané figé ; à partir de 0,10 $ et 0,50 $ par million de jetons ; sortie de 128 000 jetons au plus ; effort par défaut `medium` ; retrait « Not sooner than October 7, 2027 ») : <https://platform.claude.com/docs/en/about-claude/models/overview.md>
[^thinking]: Anthropic, Thinking (« Claude Haiku 5.5 also has thinking on by default and accepts `thinking: {type: "disabled"}` at effort `high` or below ») : <https://platform.claude.com/docs/en/build-with-claude/thinking.md>
[^responses]: OpenAI, Create a model response (`POST /responses` ; `instructions`, une chaîne, message du développeur ; `input`, une chaîne, au rôle de l'utilisateur ; `max_output_tokens`, 16 au moins, raisonnement compris ; `status` : `completed`, `failed`, `in_progress`, `cancelled`, `queued`, `incomplete` ; `incomplete_details.reason` : `max_output_tokens`, `max_messages`, `content_filter`, `steered` ; contenu `output_text` ou `refusal`) : <https://developers.openai.com/api/reference/resources/responses/methods/create>. L'hôte et l'en-tête de la clé viennent de l'exemple cURL du guide de migration (`https://api.openai.com/v1/responses`, `Authorization: Bearer`) : <https://developers.openai.com/api/docs/guides/migrate-to-responses> ; l'API Responses conseillée aux nouveaux projets : <https://developers.openai.com/api/docs/guides/text> ; les codes d'erreur (401, 403 pays non pris en charge, 429 `credit_balance_exhausted`, `organization_spend_limit_exceeded`, `slow_down`, 503 `server_is_overloaded`) : <https://developers.openai.com/api/docs/guides/error-codes>
[^luna]: OpenAI, GPT-6 Luna (`gpt-6-luna` ; `reasoning.effort` admet `none`, `low`, `medium` par défaut, `high`, `xhigh`, `max` ; 0,10 $ et 0,50 $ par million de jetons ; sortie de 128 000 jetons au plus) : <https://developers.openai.com/api/docs/models/gpt-6-luna>
[^store]: OpenAI, `store` (« Defaults to true when omitted. If set to true, response data will be stored for at least 30 days ») : <https://developers.openai.com/api/reference/resources/responses/methods/create> ; « To disable storage when using either API, set `store: false` » : <https://developers.openai.com/api/docs/guides/migrate-to-responses>
[^retraits-anthropic]: Anthropic, retraits des modèles (« Once the model is retired, requests to it fail » ; l'API Messages rend alors un 404 `not_found_error`) : <https://platform.claude.com/docs/en/about-claude/model-deprecations.md>
[^retraits]: OpenAI, retraits des modèles (« Generally available models: At least 6 months ») : <https://developers.openai.com/api/docs/deprecations>
[^sdk]: Anthropic, SDK C# (`anthropics/anthropic-sdk-csharp`), options du client : `BaseUrl` par défaut « `ANTHROPIC_BASE_URL` env var, else `https://api.anthropic.com` » ; `MaxRetries` par défaut 2, sur « connection errors, request timeouts, 408, 409, 429, 5xx » ; un gestionnaire par défaut en `AutomaticDecompression = DecompressionMethods.Available` : <https://github.com/anthropics/anthropic-sdk-csharp>
