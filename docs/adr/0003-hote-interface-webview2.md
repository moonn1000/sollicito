# ADR-0003 : hôte de l'interface, WebView2 dans une fenêtre WinForms

- **Statut** : acceptée
- **Date** : 2026-11-13
- **Remplace en partie** : [ADR-0001](0001-cadrage-architecture-v1.md), D2 (Photino.NET) ; [ADR-0002](0002-budget-nul.md), la mention de Photino.NET parmi les briques gratuites
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D9 (jeton et en-tête `Origin` pour les fichiers de l'interface, voir « Règles d'accès »)

## Contexte

L'ADR-0001 (D2) affichait l'interface React dans une fenêtre Photino.NET, et laissait sa maintenance à vérifier. Le 2026-11-13, quatre voies ont été comparées aux exigences de la D9 (navigation, nouvelles fenêtres, permissions, outils de développement, CSP, liens externes, canal du jeton, dossier de profil), à leur maintenance et à leur prise en charge de .NET 10 : Photino.NET, le contrôle WebView2 hébergé directement depuis .NET, Tauri avec l'API .NET lancée à côté, et les autres voies gratuites.

Photino.NET ne passe pas. Sa couche Windows ne s'abonne à aucun événement de navigation ni de nouvelle fenêtre, ne sait pas refuser une permission (au mieux, une invite) et ne contrôle pas l'origine des messages reçus[^photino-code]. Aucune version n'est sortie depuis le 23/01/2025[^photino-versions], et le paquet ne cible que net8.0 et net9.0[^photino-cibles].

## Décision

L'interface s'affiche dans le contrôle Microsoft Edge WebView2, hébergé directement dans une fenêtre WinForms en .NET 10 (paquet NuGet `Microsoft.Web.WebView2`), sans couche intermédiaire.

### Montage

- Un seul exécutable porte la fenêtre WinForms et la Web API ASP.NET Core (Kestrel). Le moteur du navigateur tourne à part, dans les processus du runtime WebView2[^processus].
- Kestrel écoute sur 127.0.0.1, sur un port libre choisi au lancement[^kestrel], et sert à la même origine l'API et l'interface React (fichiers statiques). Aucune règle CORS n'est à ouvrir, et la CSP part en en-tête.
- Le port change à chaque lancement, donc l'origine aussi : l'interface ne garde rien dans le stockage du navigateur, et l'état vit dans l'API.
- Le contrôle WebView2 n'est manipulé que depuis le thread de la fenêtre. Aucun gestionnaire HTTP de Kestrel n'y touche directement[^threads].

### Règles d'accès

La D9 de l'ADR-0001 exige le jeton sur toute route et refuse toute requête dont l'en-tête `Origin` n'est pas celui de l'interface. Servir l'interface depuis Kestrel oblige à préciser ces deux règles :

- **Fichiers de l'interface** (`index.html`, scripts, styles) : servis sans jeton, puisque la page les charge avant de le recevoir. Ils sont publics et ne portent aucune donnée. Seul l'en-tête `Host` y est contrôlé.
- **Routes de l'API**, documentation OpenAPI comprise : le jeton est exigé, dans son en-tête dédié.
- **En-tête `Origin`** : le navigateur ne l'envoie pas sur une lecture de même origine. Il est donc exigé, et doit valoir l'origine de l'interface, sur toute requête qui modifie un état (`POST`, `PUT`, `PATCH`, `DELETE`). Sur une lecture, s'il est présent, il doit aussi valoir cette origine.

### Durcissement de la fenêtre

Ce durcissement traduit la D9 de l'ADR-0001 en API WebView2.

- **Navigation** : `NavigationStarting` et `FrameNavigationStarting` annulent toute navigation hors de l'origine de l'interface[^navigation]. Une CSP stricte double ce filtre (`frame-src 'none'`, connexions limitées à l'API), car une requête peut partir avant l'annulation[^navigation-annulation].
- **Nouvelles fenêtres** : `NewWindowRequested` est toujours marqué traité[^nouvelle-fenetre]. Un lien en `http` ou `https` s'ouvre dans le navigateur par défaut, par le shell de Windows (`UseShellExecute`)[^shell] ; tout autre schéma est ignoré.
- **Autres sorties** : `LaunchingExternalUriScheme` et `DownloadStarting` sont annulés[^schema-externe][^telechargement].
- **Brouillon de relance** (D8 de l'ADR-0001) : la page n'ouvre jamais un lien `mailto` elle-même. Elle demande l'ouverture à l'hôte par un message, soumis au même contrôle de la source que le canal du jeton. L'hôte construit le lien `mailto` à partir des champs validés par l'API, puis le lance par le shell de Windows. Toute autre ouverture de schéma externe reste annulée.
- **Permissions** : `PermissionRequested` refuse toute demande (caméra, micro, géolocalisation, notifications…)[^permissions][^etat-permission].
- **Réglages de la version publiée** : outils de développement, menu contextuel par défaut et raccourcis du navigateur coupés (`AreDevToolsEnabled`, `AreDefaultContextMenusEnabled`, `AreBrowserAcceleratorKeysEnabled`)[^outils][^raccourcis].
- **Canal du jeton** : la page signale qu'elle est prête par `postMessage`. L'hôte vérifie la source du message (`WebMessageReceived`, propriété `Source`), puis remet le jeton par `PostWebMessageAsJson`[^securite][^source-message]. Le jeton reste en mémoire, jamais dans le stockage web.
- **Dossier de profil** : `%LOCALAPPDATA%\Sollicito\WebView2`, fixé à la création de l'environnement. Par défaut, WebView2 l'écrirait à côté de l'exécutable[^profil].
- **Vérification de réputation des navigations** coupée (`IsReputationCheckingRequired`) : la fenêtre ne navigue que vers l'origine locale, et cette vérification fait appel à SmartScreen, le service de réputation de Microsoft[^reputation].

### Runtime et tests

- **Runtime** : la version Evergreen de WebView2, partagée par les applications et mise à jour par Microsoft. L'installeur Inno Setup vérifie sa présence et lance au besoin le petit programme d'installation de Microsoft, qui demande une connexion à Internet[^distribution].
- **Tests** : l'interface se teste dans la vraie fenêtre avec Playwright, par le protocole CDP ouvert dans les seules versions de test[^playwright][^playwright-webview2].

## Alternatives écartées

- **Photino.NET** : voir le contexte.
- **Tauri 2, avec l'API .NET à côté** : il couvre les exigences, mais ajoute Rust comme troisième langage et une Web API forcément lancée en processus séparé, dont il faut coder le cycle de vie[^tauri-sidecar]. Sa ligne 3 est en préversion[^tauri-versions].
- **CefSharp** : il couvre les exigences, mais embarque son propre Chromium, un runtime livré dans un paquet de 177 Mo compressés[^cef-runtime], qu'il faut republier à chaque version de Chromium pour ne pas en garder les failles.
- **WPF** : les mêmes API WebView2, avec un hôte plus lourd et des contraintes d'affichage[^wpf], sans gain pour une seule fenêtre.
- **WinUI 3 ou MAUI** : le même contrôle WebView2[^winui][^maui], avec un cadre plus lourd à livrer.
- **Ouvrir l'interface dans le navigateur par défaut** : aucune prise sur la fenêtre (navigation, permissions, outils de développement).

## Conséquences

- **Windows uniquement** : un portage macOS ou Linux demanderait de réécrire l'hôte de la fenêtre sur un autre moteur. L'interface React et l'API resteraient. L'hôte reste donc mince, derrière une interface. Piste à réévaluer si le portage revient : Avalonia et sa WebView[^avalonia].
- **Runtime hors de notre contrôle** : Microsoft le met à jour, et l'application ne peut pas exiger une version précise[^evergreen] ; une mise à jour peut introduire un changement qui casse l'application[^distribution].
- **Surcharges externes** : des variables d'environnement et des stratégies Windows peuvent ajouter des arguments au navigateur embarqué, et une application non élevée les honore[^securite] ; la variable `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` suffit à ouvrir le débogage distant[^playwright-webview2]. Le modèle de menaces le traitera : ce scénario suppose un attaquant déjà présent dans la session de l'utilisateur.
- **Taille** : le trimming est désactivé pour WinForms[^trimming]. Une publication autonome embarque donc les runtimes .NET complets ; une publication dépendante du framework demande d'installer les runtimes .NET Desktop et ASP.NET Core.
- **Licences** : SDK WebView2 sous une licence Microsoft de type BSD ; runtime sous une licence Microsoft propriétaire, sans redevance mentionnée, qui prévoit l'envoi de données d'usage à Microsoft[^eula] ; .NET et WinForms sous MIT[^dotnet][^winforms]. Rien de payant : compatible avec l'ADR-0002.
- **Désinstallation** : le runtime ne supprime pas le dossier de profil[^profil]. L'installeur le traite comme les autres données de la D10 de l'ADR-0001 : gardé par défaut, effacé si l'utilisateur le demande.

## À vérifier

Points relevés le 2026-11-13, à régler par un nouvel ADR si besoin :

- **.NET 10** : le paquet WebView2 n'a pas de cible net10 et fonctionne par compatibilité (« .NET 5 or later »)[^webview2-intro]. À confirmer au premier sprint par un prototype.
- **Mode de publication** : autonome ou dépendant du framework, selon la taille de l'installeur et ce qu'on demande d'installer.
- **Accès au réseau local de Chromium** (Local Network Access) : désactivé dans WebView2 d'après l'équipe de Microsoft (décembre 2025)[^lna], à revérifier ; sans effet tant que l'interface et l'API partagent la même origine.
- **Variables `WEBVIEW2_*`** : les effacer du processus avant de créer l'environnement est une piste non vérifiée.
- **Télémétrie et mises à jour du runtime** : elles quittent la machine, ce que le garde-fou du README (« ne quittent votre machine que les échanges des fonctions que vous activez ») ne dit pas. À concilier, par exemple avec les réglages de diagnostic de Windows, puis à écrire dans le README.

## Sources

Consultées le 2026-11-13 :

[^photino-code]: Photino.Native, couche Windows (v4.0.22) : <https://github.com/tryphotino/photino.Native/blob/v4.0.22/Photino.Native/Photino.Windows.cpp>
[^photino-versions]: Photino.NET, versions publiées : <https://github.com/tryphotino/photino.NET/releases>
[^photino-cibles]: Photino.NET, fichier projet (v4.0.16) : <https://github.com/tryphotino/photino.NET/blob/v4.0.16/Photino.NET/Photino.NET.csproj>
[^processus]: Microsoft Learn, modèle de processus de WebView2 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-model>
[^kestrel]: Microsoft Learn, points de terminaison de Kestrel (port 0) : <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints>
[^threads]: Microsoft Learn, modèle de threads de WebView2 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model>
[^navigation]: Microsoft Learn, `CoreWebView2.NavigationStarting` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.navigationstarting> ; `FrameNavigationStarting` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.framenavigationstarting>
[^navigation-annulation]: Microsoft Learn, `CoreWebView2NavigationStartingEventArgs.Cancel` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2navigationstartingeventargs.cancel>
[^nouvelle-fenetre]: Microsoft Learn, `CoreWebView2.NewWindowRequested` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.newwindowrequested>
[^shell]: Microsoft Learn, `ProcessStartInfo.UseShellExecute` : <https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute>
[^schema-externe]: Microsoft Learn, `CoreWebView2.LaunchingExternalUriScheme` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.launchingexternalurischeme>
[^telechargement]: Microsoft Learn, `CoreWebView2.DownloadStarting` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.downloadstarting>
[^permissions]: Microsoft Learn, `CoreWebView2.PermissionRequested` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.permissionrequested>
[^etat-permission]: Microsoft Learn, `CoreWebView2PermissionState` (valeur `Deny`) : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2permissionstate>
[^outils]: Microsoft Learn, `CoreWebView2Settings.AreDevToolsEnabled` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.aredevtoolsenabled> ; `AreDefaultContextMenusEnabled` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.aredefaultcontextmenusenabled>
[^raccourcis]: Microsoft Learn, `CoreWebView2Settings.AreBrowserAcceleratorKeysEnabled` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.arebrowseracceleratorkeysenabled>
[^securite]: Microsoft Learn, bonnes pratiques de sécurité de WebView2 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security>
[^source-message]: Microsoft Learn, `CoreWebView2WebMessageReceivedEventArgs.Source` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webmessagereceivedeventargs.source>
[^profil]: Microsoft Learn, dossier de données utilisateur de WebView2 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder>
[^reputation]: Microsoft Learn, `CoreWebView2Settings.IsReputationCheckingRequired` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.isreputationcheckingrequired>
[^distribution]: Microsoft Learn, distribution d'une application et du runtime WebView2 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution>
[^playwright]: Microsoft Learn, automatiser WebView2 avec Playwright : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/playwright>
[^playwright-webview2]: Playwright, WebView2 (connexion par CDP, variable `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`) : <https://playwright.dev/docs/webview2>
[^tauri-sidecar]: Tauri, embarquer un binaire externe (sidecar) : <https://v2.tauri.app/develop/sidecar/>
[^tauri-versions]: Tauri, versions publiées : <https://github.com/tauri-apps/tauri/releases>
[^cef-runtime]: NuGet, paquet `chromiumembeddedframework.runtime.win-x64` : <https://www.nuget.org/packages/chromiumembeddedframework.runtime.win-x64>
[^wpf]: Microsoft Learn, `WebView2CompositionControl` : <https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.wpf.webview2compositioncontrol>
[^winui]: Microsoft Learn, WebView2 dans une application WinUI 3 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winui>
[^maui]: Microsoft Learn, contrôle HybridWebView de .NET MAUI : <https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/hybridwebview>
[^dotnet]: .NET, dépôt `dotnet/runtime` (licence MIT) : <https://github.com/dotnet/runtime>
[^winforms]: Windows Forms, dépôt `dotnet/winforms` (licence MIT) : <https://github.com/dotnet/winforms>
[^avalonia]: Avalonia, dépôt de la WebView : <https://github.com/AvaloniaUI/Avalonia.Controls.WebView>
[^evergreen]: Microsoft Learn, runtime Evergreen ou version fixe : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/evergreen-vs-fixed-version>
[^trimming]: Microsoft Learn, incompatibilités connues du trimming : <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities>
[^eula]: Microsoft, licence du runtime WebView2 : <https://developer.microsoft.com/microsoft-edge/api/eula/webview2> ; paquet NuGet du SDK et sa licence : <https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35>
[^webview2-intro]: Microsoft Learn, présentation de WebView2 (environnements pris en charge) : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/>
[^lna]: WebView2Feedback, issue #5456 : <https://github.com/MicrosoftEdge/WebView2Feedback/issues/5456>
