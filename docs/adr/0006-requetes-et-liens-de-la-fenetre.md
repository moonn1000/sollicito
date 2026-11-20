# ADR-0006 : la fenêtre retient ses requêtes vers une autre origine

- **Statut** : acceptée
- **Date** : 2026-11-19
- **Précise** : [ADR-0003](0003-hote-interface-webview2.md), « Durcissement de la fenêtre », points « Navigation » et « Nouvelles fenêtres »

## Contexte

L'ADR-0003 fait annuler par `NavigationStarting` et `FrameNavigationStarting` toute navigation hors de l'origine de l'interface, et ajoute : « Une CSP stricte double ce filtre (`frame-src 'none'`, connexions limitées à l'API), car une requête peut partir avant l'annulation. »

Le prototype de #9 a montré qu'une navigation annulée envoie quand même sa requête (ADR-0003, « À vérifier »). La politique de sécurité du contenu (CSP) ne l'arrête pas non plus : ses directives visent les cadres, les connexions, les scripts, les images, les formulaires, pas la destination d'une navigation de premier niveau. La phrase de l'ADR-0003 vaut donc pour les cadres et les connexions, pas pour une navigation de premier niveau. Vers un site extérieur, une telle requête emporterait ce que contient son adresse.

Le 2026-11-19, la version de test de la fenêtre (#13), avec le runtime WebView2 157.0.4341.60, a lancé trente navigations de premier niveau vers un serveur témoin, sur une autre origine du poste :

- avec l'annulation seule, les trente requêtes sont arrivées au témoin ;
- avec, en plus, un filtre `WebResourceRequested` sur toutes les requêtes, qui répond lui-même à celles qui visent une autre origine, aucune n'est arrivée.

Pour les nouvelles fenêtres, l'ADR-0003 ouvre dans le navigateur par défaut tout lien en `http` ou `https`. Or WebView2 n'a pas le bloqueur de fenêtres surgissantes d'Edge : c'est à l'application de refuser celles que l'utilisateur n'a pas demandées[^utilisateur]. Un script injecté dans la page malgré la CSP pourrait donc faire sortir des données par l'adresse d'un `window.open`, hors de portée du filtre et de la CSP.

## Décision

### Les requêtes hors de l'origine ne partent pas

La fenêtre filtre toutes ses requêtes (`AddWebResourceRequestedFilter`, sur toutes les adresses, tous les contextes et toutes les sources de requête), avant la première navigation, comme l'exige l'API[^filtre]. Elle répond elle-même, par un 403, à toute requête hors de l'origine de l'interface : la requête se termine par cette réponse[^reponse], et l'essai montre qu'elle ne part pas sur le réseau. Les requêtes de l'origine passent sans changement.

Le point « Navigation » de l'ADR-0003 se lit désormais ainsi : `NavigationStarting` et `FrameNavigationStarting` annulent toute navigation hors de l'origine de l'interface ; le filtre des requêtes double cette annulation pour toute requête, navigation de premier niveau comprise ; la CSP la double pour les cadres, les connexions et les autres ressources de la page.

### Un lien ne s'ouvre au-dehors qu'à la demande de l'utilisateur

`NewWindowRequested` reste toujours traité, et le lien ne s'ouvre dans le navigateur par défaut qu'à trois conditions :

- la demande vient d'un geste de l'utilisateur (`IsUserInitiated`) : un clic, ou un script lancé pendant l'activation que ce geste ouvre, même par une minuterie. L'activation expire au bout d'environ cinq secondes (#78) : un `window.open` lancé ensuite, ou sans aucun geste, au chargement de la page par exemple, est ignoré ;
- l'adresse est absolue, en `http` ou `https` ;
- elle ne porte pas d'identifiants (`https://banque.exemple@autre.exemple/`), forme qui sert à tromper sur la destination.

Le shell de Windows reçoit l'adresse réécrite par .NET (`Uri.AbsoluteUri`). Tout autre lien est ignoré, comme le prévoit déjà l'ADR-0003.

Seul un lien qui ouvre une nouvelle fenêtre s'ouvre ainsi au-dehors. Un lien cliqué sur place, sans `target="_blank"`, est une navigation : elle est annulée, et le lien ne s'ouvre nulle part. L'interface écrit donc ses liens externes en nouvelle fenêtre.

## Alternatives écartées

- **L'annulation et la CSP seules** : la requête d'une navigation de premier niveau part, avec son adresse.
- **Ne filtrer que les documents** (contexte `Document`) : la CSP couvre déjà les autres ressources, mais un filtre sur tout ne coûte qu'un événement par requête et double aussi la CSP.
- **Ouvrir tout lien `http` ou `https` d'une nouvelle fenêtre**, comme l'écrivait l'ADR-0003 : un script pourrait ouvrir le navigateur par défaut sur une adresse de son choix, sans clic.
- **Ouvrir aussi au-dehors un lien cliqué sur place**, depuis `NavigationStarting` : la navigation deviendrait une sortie de plus, à garder par les mêmes conditions. L'annulation simple de l'ADR-0003 suffit, puisque l'interface écrit ses liens externes en nouvelle fenêtre.

## Conséquences

- Chaque requête de la page lève un événement sur le fil de la fenêtre. L'interface, servie en local, n'en fait que quelques-unes à son chargement.
- Le test Playwright de la vraie fenêtre (`interface/tests-fenetre`) refait l'essai à chaque pull request : trente navigations vers le serveur témoin, puis un `fetch`, une image, un script, un cadre, une balise (`sendBeacon`) et un formulaire ; aucune requête ne doit lui parvenir. Il suit donc le comportement du runtime, qui se met à jour seul (ADR-0003, « Conséquences »).
- Un `window.open` lancé sans geste de l'utilisateur, ou une fois son activation expirée, ne s'ouvre pas, et un lien externe cliqué sur place non plus : l'interface ouvre ses liens externes par un vrai lien, en nouvelle fenêtre, cliqué par l'utilisateur.
- Le [modèle de menaces](../modele-de-menaces.md) décrit ces protections et leurs limites.

## Sources

- Essai du 2026-11-19 : version de test de la fenêtre (#13), trente navigations vers un serveur témoin, avec et sans le filtre.

Consultées le 2026-11-19 :

[^filtre]: Microsoft Learn, `CoreWebView2.AddWebResourceRequestedFilter` (filtres à ajouter avant la navigation de la page ; surcharge à trois paramètres, la seule qui vaille pour les cadres) : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.addwebresourcerequestedfilter>
[^reponse]: Microsoft Learn, `CoreWebView2WebResourceRequestedEventArgs.Response` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourcerequestedeventargs.response>
[^utilisateur]: Microsoft Learn, `CoreWebView2NewWindowRequestedEventArgs.IsUserInitiated` (bloqueur de fenêtres surgissantes d'Edge coupé dans WebView2) : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2newwindowrequestedeventargs.isuserinitiated>
