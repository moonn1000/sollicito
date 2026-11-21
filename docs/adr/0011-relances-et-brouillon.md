# ADR-0011 : relances, contact minimal, brouillon ouvert par l'hôte et délai par défaut

- **Statut** : proposée
- **Date** : 2026-11-21
- **Précise** : [ADR-0001](0001-cadrage-architecture-v1.md), D5 (le contact minimal et la règle de son adresse) et D8 (le brouillon : quels champs, quel encodage, aucun autre en-tête) ; [ADR-0003](0003-hote-interface-webview2.md), « Durcissement de la fenêtre », point « Brouillon de relance » (un troisième message de la page à l'hôte, qui ne porte que l'identifiant de la relance) ; [ADR-0004](0004-v1-resserree-paliers.md), « Ce qui quitte la machine » (une messagerie en ligne reçoit le brouillon avant tout envoi) ; [ADR-0008](0008-sauvegardes-de-la-base.md), « Le premier réglage de l'application, hors de la base » (un second réglage, lu et écrit à part)

## Contexte

La V1 veut qu'une relance soit proposée à son échéance et ouvre un brouillon dans la messagerie de l'utilisateur, adressé au contact de l'entreprise s'il y en a un (#14, qui absorbe #30). Plusieurs décisions la cadrent déjà :

- **D8 de l'ADR-0001** : Sollicito n'envoie jamais rien. Le brouillon s'ouvre par un lien `mailto` que l'application construit à partir de champs validés.
- **ADR-0003** : la page n'ouvre jamais ce lien elle-même. Elle demande l'ouverture à l'hôte par un message, sous le contrôle de la source du canal du jeton. L'hôte construit le lien à partir des champs validés par l'API, puis le lance par le shell de Windows.
- **Modèle de domaine** (#6, PR #76) : une relance sur une candidature en cours, une seule à faire à la fois ; une échéance qui part de la date locale de l'envoi, calculée sur la date et enregistrée ; un destinataire parmi les contacts de l'entreprise de la candidature ; faite ou abandonnée, pour de bon, avec une clôture dans l'historique ; abandonnée quand la candidature se clôt ; sans destinataire quand la candidature change d'entreprise. Il renvoie à #14 le détail de la règle de l'adresse et la valeur du délai par défaut.
- **ADR-0008** : le délai par défaut est un réglage de l'application, dans `reglages.json`, hors de la base.

Trois faits, relevés le 2026-11-21, pèsent sur la façon de faire :

- **Aucun signal de geste sur un message** : `WebMessageReceived` ne dit pas si un clic précède le message, contrairement à `NewWindowRequested`, qui porte `IsUserInitiated` (ADR-0006). Un script injecté dans la page, qui aurait le jeton, pourrait donc demander un brouillon sans clic.
- **Un lien `mailto` mal encodé change de sens** : un `?`, un `&` ou un `#` d'un texte couperait le lien ou y ajouterait un en-tête (copie cachée, autre destinataire). La RFC 6068 exige d'encoder en pourcentage certains caractères d'une adresse, et les fins de ligne du corps en `%0D%0A`. Elle demande aussi à la messagerie de ne pas envoyer un message tiré d'un lien `mailto` sans l'avoir montré à l'utilisateur, ni d'en créer un dont un en-tête est dangereux[^rfc6068].
- **Une messagerie en ligne reçoit le lien entier** : une application web peut s'enregistrer auprès du navigateur pour ouvrir les liens `mailto`, et le navigateur insère alors le lien dans l'adresse de cette application[^protocole]. Le destinataire, l'objet et le corps partent ainsi chez le fournisseur de messagerie dès l'ouverture du brouillon. Une application de messagerie qui range ses brouillons sur un serveur, comme le dossier des brouillons d'un compte IMAP ou Exchange, les y envoie à son tour, à l'enregistrement du brouillon.

## Décision

### Le contact minimal

Un contact a un nom, une adresse électronique et son entreprise, tous trois requis. Le nom est un texte d'une ligne, aux règles du poste.

L'adresse suit la règle des navigateurs pour un champ `type=email`[^whatwg], en plus stricte :

- ses blancs du début et de la fin sont retirés, et il reste au plus 254 caractères, en ASCII, avec une seule `@` ;
- sa partie locale, de 1 à 64 caractères, ne prend que des lettres, des chiffres et ``. _ + - ' ! # $ & * / = ^ ` { | } ~``, sans point au début, à la fin, ni deux à la suite ;
- son domaine a au moins deux parties séparées par des points, chacune de 1 à 63 lettres, chiffres ou tirets, sans tiret au bord.

L'adresse n'a donc ni espace, ni virgule, ni point-virgule (pas de liste), ni `?` (pas de paramètre), ni `%`, qui sert à l'encodage. Un domaine accentué se saisit sous sa forme `xn--`. Le refus ne reprend jamais l'adresse.

On ajoute, modifie et supprime les contacts d'une entreprise depuis la fiche d'une candidature. Modifier un contact change ce qu'affichent ses relances, closes comprises. Supprimer un contact, après une confirmation dans la page, le retire de toutes ses relances : la base met leur destinataire à rien (`ON DELETE SET NULL`), et l'historique dit alors « sans destinataire ».

### La relance

- **Planifier**, sur une candidature en cours qui n'a pas de relance à faire : un délai de 1 à 365 jours, compté depuis la date locale de l'envoi, et un destinataire facultatif, contact de l'entreprise de la candidature. Sans délai, l'API prend le délai par défaut. Une échéance déjà passée est admise : une candidature saisie après coup a sa relance aussitôt échue.
- **Le délai proposé** par l'écran : le délai par défaut ; après une relance close, son délai plus le délai par défaut (7, puis 14, puis 21 jours…). La relance suivante part aussi de la date d'envoi. Le contact unique de l'entreprise est proposé d'office.
- **Changer** la relance à faire : son délai et son destinataire.
- **Marquer faite ou abandonner** : pour de bon. Une clôture de relance, datée par l'horloge, entre dans l'historique, avec l'échéance et le destinataire de la relance. Comme tout événement en vigueur, elle borne la date d'envoi corrigée (#25).
- **Une seule relance à faire** : chaque action passe par l'écriture exclusive de la candidature, et un index unique, filtré sur l'état, la garantit dans la base.
- **Échue** : une échéance tombe aujourd'hui ou avant. **De la semaine** : elle tombe dans les sept jours qui suivent. L'API le calcule, en date locale, par l'horloge du poste.

La relance suit sa candidature. Supprimer la candidature emporte ses relances et leurs clôtures. La rattacher à une autre entreprise retire le destinataire de la relance à faire, et les relances closes gardent le leur. La clore abandonne la relance à faire, avec sa clôture, et corriger ensuite le dernier changement ne la rouvre pas. Corriger la date d'envoi ne déplace pas l'échéance.

### Le délai par défaut, second réglage de l'application

Le délai par défaut vaut 7 jours d'usine, de 1 à 365. Il vit dans `reglages.json`, sous `delaiDesRelances`, et se règle dans une section « Relances » de l'écran des réglages. Il ne sert qu'aux relances planifiées ensuite.

- **Chaque réglage se lit à part** : un délai illisible (pas un entier, ou hors bornes) vaut 7 jours, et la section le dit ; il ne rend pas illisible le dossier des sauvegardes, ni l'inverse. Seul un fichier illisible en entier (JSON mal formé, propriété répétée) rend illisibles tous les réglages.
- **Chaque écriture ne change que son réglage**, et garde le reste du fichier, réglages inconnus compris. Les écrivains, sauvegardes et relances, relisent le fichier sous un verrou commun.
- **Sur un fichier illisible en entier**, le délai ne s'écrit pas (409). Le réécrire avec le seul délai ferait oublier le dossier des sauvegardes perdu, et tomber l'alerte de l'ADR-0008 au lancement suivant. Seul le choix d'un dossier des sauvegardes réécrit ce fichier, et l'écran le dit.

### Le brouillon, ouvert par l'hôte à partir de la base

- **La page** envoie à l'hôte `{"type":"ouvrirLeBrouillon","relance":<id>}`. Le message ne porte que l'identifiant de la relance, ni adresse ni texte, comme le choix du dossier des sauvegardes ne porte aucun chemin (ADR-0008). Sa source est contrôlée comme celle du canal du jeton. `relance` doit être un entier positif, et un autre champ n'est jamais lu. Un message d'une autre source ou d'une autre forme n'ouvre rien.
- **L'hôte** agit après la fin du gestionnaire du message, un brouillon à la fois. Il demande les champs au service des relances, en mémoire et non par une route. Ce service lit la relance en base, et rend l'adresse du destinataire, l'objet et le corps. Seule une relance à faire a un brouillon.
- **Le lien** est construit par l'hôte seul :
  - `mailto:`, puis l'adresse, revérifiée par la règle du contact et encodée : tout sauf les caractères non réservés de la RFC 3986 et l'`@` ;
  - puis `?subject=…&body=…`, en UTF-8 encodé de même, chaque fin de ligne en `%0D%0A` ;
  - aucun autre en-tête : ni copie, ni copie cachée, ni pièce jointe.

  Seul un lien qui commence par `mailto:` va au shell de Windows (`UseShellExecute`). Une adresse lue en base qui ne passe plus la règle fait refuser le brouillon.
- **L'issue** revient à la page, sans rien du brouillon : ouvert ; refusé (relance inconnue, close, ou brouillon qui ne se construit pas) ; impossible (aucune messagerie associée aux liens `mailto`). La page relit les relances quand il est refusé. Sans réponse de l'hôte dans les quinze secondes, elle cesse d'attendre.
- **La page n'ouvre jamais rien** : la navigation vers `mailto:` et `LaunchingExternalUriScheme` restent annulés (ADR-0003, ADR-0006).
- **La version de test** note le lien dans `brouillons-ouverts.txt`, dans son dossier de données, au lieu de le lancer. Cet écart n'est pas compilé dans la version publiée.

Le brouillon tient en un minimum de mots. Il ne porte ni le nom de l'entreprise, que le destinataire connaît, ni de signature : la V1 n'a pas de profil, et la messagerie ajoute la sienne.

- **Objet** : « Candidature au poste de {poste} »
- **Corps** :

  > Bonjour {nom du contact},
  >
  > Je me permets de revenir vers vous au sujet de ma candidature au poste de {poste}, envoyée le {jour de l'envoi en toutes lettres}. Ce poste m'intéresse toujours, et je reste à votre disposition pour en parler.
  >
  > Bien cordialement,

Sans destinataire, la première ligne devient « Bonjour, ». La date est le jour local de l'envoi, en français (« 1er octobre 2026 »), sans dépendre de la langue de Windows.

### Ce qui quitte la machine

Sollicito ne contacte aucun serveur pour un brouillon. Mais la messagerie par défaut reçoit, à son ouverture, l'adresse du contact, l'objet et le corps : le poste, la date d'envoi et le nom du contact. Si elle est en ligne, ou si elle range ses brouillons sur un serveur, ce contenu part chez son fournisseur avant tout envoi, même si l'utilisateur renonce (voir « Contexte »). Une application qui garde ses brouillons sur le poste ne fait rien sortir avant l'envoi. Le README le dit, dans sa section « Ce qui quitte votre machine ».

## Alternatives écartées

- **Un lien ouvert par `window.open`** vers une adresse de l'origine, que l'hôte intercepterait dans `NewWindowRequested` : il apporterait le signal de geste (`IsUserInitiated`), mais mêlerait deux sorties, liens externes et brouillons, sous des règles différentes.
- **La page envoie l'adresse et le texte, que l'hôte revalide** : plus de surface, et un script choisirait le texte du brouillon.
- **Une route de l'API qui rend le lien `mailto`** : la page tiendrait alors le lien, et rien ne l'empêcherait d'en bâtir un autre ; l'hôte seul construit le lien.
- **Une relance planifiée d'office à l'enregistrement d'une candidature** : elle aurait changé la saisie (#11) et le corps de son enregistrement, pour un geste que la fiche fait en un clic.
- **Toujours proposer le délai par défaut** : une deuxième relance, comptée depuis l'envoi, serait souvent échue dès sa planification.
- **Refuser la suppression d'un contact qu'une relance close désigne** : l'historique garderait son destinataire, mais un contact saisi par erreur dans la mauvaise entreprise ne se retirerait plus.
- **Réécrire un fichier de réglages illisible avec le seul délai** : l'alerte des sauvegardes tomberait au lancement suivant, et le dossier perdu ne recevrait plus rien, sans avertissement.
- **Un gabarit de brouillon modifiable** dans les réglages : un écran et des règles de plus. Le brouillon rédigé par l'IA (#47) viendra au palier 4.

## Conséquences

- **Positives** : la page ne donne jamais au lien ni son adresse ni son texte ; aucun en-tête ne s'ajoute au brouillon, quoi que contienne un champ ; la base garantit une seule relance à faire ; un délai mal écrit ne touche pas aux sauvegardes ; Sollicito n'envoie rien, et l'utilisateur relit le brouillon dans sa messagerie.
- **Négatives** :
  - aucun geste n'est exigé avant un brouillon. Un script injecté qui aurait le jeton pourrait ouvrir le brouillon d'une relance sans clic, après avoir créé par l'API un contact à son adresse et choisi le poste de la candidature. L'envoi reste à l'utilisateur, qui voit le brouillon dans sa messagerie (D8). Contre ce script, restent l'affichage échappé et la politique de sécurité du contenu (modèle de menaces, « Limites ») ;
  - une messagerie en ligne reçoit le brouillon avant tout envoi (voir « Ce qui quitte la machine ») ;
  - un contact supprimé quitte aussi les relances closes : leur historique dit « sans destinataire » ;
  - le lien d'un brouillon ordinaire fait environ 500 caractères (sonde du 2026-11-21, encodage ci-dessus), mais peut dépasser 4 000 avec des champs longs et accentués. La longueur qu'accepte chaque messagerie n'est pas connue (voir « À vérifier »).

## À vérifier

Sur l'exe publié, à la main, le protocole CDP ne pilotant pas la messagerie :

- une messagerie de bureau et une messagerie en ligne reçoivent le brouillon entier, destinataire, accents et sauts de ligne compris ;
- un brouillon aux champs longs s'ouvre entier ;
- un poste sans messagerie associée : Windows peut y proposer de choisir une application au lieu d'échouer, et l'issue rendue à la page serait alors « ouvert ».

## Sources

- Story : <https://github.com/moonn1000/sollicito/issues/14> ; #30, absorbée : <https://github.com/moonn1000/sollicito/issues/30>
- [ADR-0001](0001-cadrage-architecture-v1.md), D5 et D8 ; [ADR-0003](0003-hote-interface-webview2.md), « Durcissement de la fenêtre » ; [ADR-0006](0006-requetes-et-liens-de-la-fenetre.md) ; [ADR-0008](0008-sauvegardes-de-la-base.md)
- Le contact minimal et les règles des relances : [modèle de domaine](../modele-de-domaine.md), « Relances » et « Contact », et la PR #76, section « Pour #14 »
- Sonde du 2026-11-21 : longueur du lien `mailto` du gabarit, selon l'encodage ci-dessus, pour des champs ordinaires, au maximum en ASCII, en lettres accentuées, et au pire (200 « € »)

Consultées le 2026-11-21 :

[^rfc6068]: RFC 6068, le schéma d'URI `mailto` (caractères d'une adresse à encoder en pourcentage, fins de ligne du corps en `%0D%0A` ; un message tiré d'un lien `mailto` ne part pas sans avoir été montré à l'utilisateur, et ne se crée pas si un en-tête est dangereux) : <https://www.rfc-editor.org/rfc/rfc6068>
[^protocole]: MDN, `Navigator.registerProtocolHandler()` (une application web de messagerie peut ouvrir les liens `mailto:` ; le navigateur insère le lien activé dans l'adresse du gestionnaire) : <https://developer.mozilla.org/en-US/docs/Web/API/Navigator/registerProtocolHandler>
[^whatwg]: WHATWG, HTML, « valid email address » du champ `type=email` : <https://html.spec.whatwg.org/multipage/input.html#valid-e-mail-address>
