# Démarrer avec Sollicito

Sollicito est un seul fichier `.exe`, pour un PC Windows 64 bits. Rien à installer : vous le téléchargez, vous le vérifiez, vous le lancez.

Le fichier n'est pas signé, car le projet n'a pas de budget pour un certificat ([ADR-0002](adr/0002-budget-nul.md)). Windows ne sait donc pas qui l'a publié, et peut vous avertir ou le bloquer. C'est une raison de plus pour le vérifier.

**Avant de télécharger**, sous Windows 11 : une protection appelée Smart App Control peut bloquer Sollicito, sans exception possible. Si elle est activée sur votre PC, lisez d'abord [Si Smart App Control bloque Sollicito](#si-smart-app-control-bloque-sollicito).

Dans les noms de fichiers et les commandes, `X.Y.Z` désigne le numéro de la version, par exemple `1.0.0`. Remplacez-le partout.

## Télécharger

1. Ouvrez la [page des versions de Sollicito](https://github.com/moonn1000/sollicito/releases). Téléchargez Sollicito seulement depuis cette page : ailleurs, un fichier du même nom peut être un faux. Si la page ne propose aucune version, aucune n'est encore publiée.
2. Prenez la version la plus récente. Dans sa partie « Assets », téléchargez ces deux fichiers :
   - `Sollicito-X.Y.Z-win-x64.exe` : l'application ;
   - `Sollicito-X.Y.Z-win-x64.exe.sigstore.json` : il sert à [vérifier l'origine](#vérifier-lorigine).

   Les archives « Source code » contiennent le code source, pas l'application : vous n'en avez pas besoin.
3. Votre navigateur peut demander une confirmation. Microsoft Edge, par exemple, peut avertir ou annoncer que le téléchargement est bloqué. Pour garder le fichier : ouvrez le menu « … » à côté de lui, choisissez « Conserver », puis, s'il le faut, « Afficher plus » et « Conserver quand même »[^edge].

N'ouvrez pas encore le fichier : vérifiez-le d'abord.

Les deux vérifications qui suivent sont conseillées. Elles sont facultatives si vous ne savez pas ouvrir PowerShell, la fenêtre de commandes livrée avec Windows.

## Vérifier l'empreinte

L'empreinte est une longue suite de chiffres et de lettres, calculée à partir du fichier. Un fichier modifié, même très peu, donne une autre empreinte.

1. Ouvrez le menu Démarrer, tapez `Windows PowerShell`, puis ouvrez-le[^powershell].
2. Placez-vous dans le dossier de vos téléchargements. Copiez cette ligne, collez-la dans la fenêtre, puis appuyez sur Entrée. Si vous avez rangé le fichier ailleurs, indiquez ce dossier.

   ```powershell
   cd "$HOME\Downloads"
   ```

3. Calculez l'empreinte[^empreinte] :

   ```powershell
   Get-FileHash .\Sollicito-X.Y.Z-win-x64.exe -Algorithm SHA256
   ```

4. Sur la page de la version, à côté de `Sollicito-X.Y.Z-win-x64.exe`, figure son empreinte, après `sha256:`[^condense]. La page peut n'en montrer que le début : le bouton de copie, juste à côté, la copie en entier, précédée de `sha256:`. Collez-la dans le Bloc-notes, puis comparez ce qui suit `sha256:`, en entier, à la suite affichée sous `Hash`. PowerShell l'écrit en majuscules et la page en minuscules : cela ne change rien. La même empreinte se trouve aussi dans le fichier `Sollicito-X.Y.Z-win-x64.exe.sha256` de la page, que le Bloc-notes sait ouvrir.

**Si les deux suites diffèrent, ne lancez pas le fichier.** Supprimez-le et téléchargez-le de nouveau depuis la page des versions. Si elles diffèrent encore, [signalez-le](../SECURITY.md).

Des empreintes égales montrent que votre fichier est bien celui de la page. Elles ne disent pas comment il a été fabriqué : c'est l'objet de la vérification suivante.

## Vérifier l'origine

Chaque version est accompagnée d'une **attestation** : une preuve signée qui dit où le fichier a été construit.

- **Elle prouve** que l'exe a été construit par la chaîne de publication du dépôt de Sollicito (`publication.yml`), à partir du code marqué `vX.Y.Z`.
- **Elle ne prouve pas** que ce code, ou les composants d'autres auteurs qu'il embarque, sont sans danger. Un composant piégé serait attesté comme le reste.

Le détail est dans le [modèle de menaces](modele-de-menaces.md#lempreinte-et-lattestation-dune-version).

La vérification se fait avec GitHub CLI (`gh`), un outil gratuit de GitHub. Les étapes ci-dessous ne demandent pas de compte GitHub, mais une connexion à Internet.

1. Installez GitHub CLI. Dans PowerShell[^gh] :

   ```powershell
   winget install --id GitHub.cli --source winget
   ```

   `winget` est l'installeur de logiciels en ligne de commande de Windows[^winget]. Sans lui, prenez l'outil sur [cli.github.com](https://cli.github.com/).
2. Fermez PowerShell, ouvrez une nouvelle fenêtre, puis revenez dans le dossier de vos téléchargements :

   ```powershell
   cd "$HOME\Downloads"
   ```

3. Vérifiez que la version de l'outil est **2.102.0 ou plus**. Les versions plus anciennes contrôlaient moins strictement l'origine d'un fichier ([ADR-0009](adr/0009-construction-et-publication-de-l-exe.md#vérifier-lexe)).

   ```powershell
   gh --version
   ```

4. Téléchargez les références qui permettent de contrôler la signature (celles de Sigstore, le service qui l'a émise). Copiez la ligne telle quelle : avec le signe `>` à la place de `Out-File`, Windows PowerShell écrit un fichier que `gh` refuse.

   ```powershell
   gh attestation trusted-root | Out-File -Encoding ascii trusted_root.jsonl
   ```

5. Vérifiez l'attestation. La commande tient sur une seule ligne, et `X.Y.Z` y figure trois fois.

   ```powershell
   gh attestation verify Sollicito-X.Y.Z-win-x64.exe --repo moonn1000/sollicito --signer-workflow moonn1000/sollicito/.github/workflows/publication.yml --source-ref refs/tags/vX.Y.Z --bundle Sollicito-X.Y.Z-win-x64.exe.sigstore.json --custom-trusted-root trusted_root.jsonl
   ```

La réponse attendue contient `Verification succeeded!`. **Toute autre réponse veut dire que l'origine n'est pas vérifiée : ne lancez pas le fichier.** Relisez la commande et le numéro de version. Si l'échec persiste, [signalez-le](../SECURITY.md).

Si vous avez un compte GitHub et que vous y avez connecté l'outil (`gh auth login`), vous pouvez aussi vérifier sans le fichier `.sigstore.json` ni `trusted_root.jsonl` :

```powershell
gh attestation verify Sollicito-X.Y.Z-win-x64.exe --repo moonn1000/sollicito --signer-workflow moonn1000/sollicito/.github/workflows/publication.yml --source-ref refs/tags/vX.Y.Z
```

## Lancer

Double-cliquez sur le fichier. Windows peut afficher un avertissement de SmartScreen, sa protection contre les fichiers inconnus : « Windows a protégé votre PC » (en anglais, « Windows protected your PC »).

**Pourquoi ?** Le fichier n'est pas signé. SmartScreen le juge alors sur sa réputation, c'est-à-dire sur l'historique de ses téléchargements, et une version neuve n'en a pas encore[^smartscreen].

**Pour lancer ce fichier**, une fois vérifié :

1. cliquez sur « Plus d'informations » (« More info ») ;
2. puis sur « Exécuter de toute façon » (« Run anyway »)[^passer].

Les mots peuvent différer un peu selon votre Windows. Pour passer cet avertissement, vous n'avez rien à désactiver.

Il peut cesser quand cette version a acquis assez de réputation ; Microsoft ne donne pas de seuil. Chaque nouvelle version repart de zéro. Sur un PC géré par un employeur ou une école, il peut être interdit de passer outre[^smartscreen].

### Si Smart App Control bloque Sollicito

Smart App Control est une protection de Windows 11 qui bloque les applications malveillantes ou non fiables. Elle n'existe pas sous Windows 10[^sac-securite]. Un fichier non signé, comme Sollicito, peut être bloqué[^sac-faq][^sac-technique].

Pour voir son état : ouvrez l'application « Sécurité Windows », puis « Application et contrôle du navigateur », puis les paramètres de Smart App Control (Microsoft l'appelle aussi « Contrôle de l'application intelligente »)[^sac-securite][^sac-technique]. Les mots peuvent différer un peu selon votre Windows. Trois états sont possibles[^sac-securite] :

- **Désactivé** : il ne bloque rien.
- **Évaluation** : il ne bloque rien pour l'instant, mais Windows peut ensuite l'activer de lui-même.
- **Activé** : il peut bloquer Sollicito.

Microsoft indique qu'on ne peut pas faire d'exception pour une seule application[^sac-faq]. Vous avez donc deux choix :

- **Garder Smart App Control et ne pas utiliser Sollicito sur ce PC.**
- **Désactiver Smart App Control**, dans ces mêmes paramètres. Vous perdez alors, **pour tout le PC**, le blocage des applications malveillantes ou inconnues[^sac-securite].

**Pourrez-vous le réactiver ?** Les pages de Microsoft ne s'accordent pas. Ses questions fréquentes annoncent que des mises à jour récentes de Windows permettent de le réactiver sans réinstaller[^sac-faq]. D'autres pages disent qu'une fois désactivé, il faut parfois réinitialiser le PC ou réinstaller Windows pour le retrouver[^sac-securite][^sac-technique]. Ne le désactivez que si vous acceptez ce risque.

## Au premier lancement

**Si un composant de Windows manque.** Sollicito affiche son interface avec le runtime WebView2 de Microsoft. Il est livré avec Windows 11, et déjà présent sur la grande majorité des PC sous Windows 10[^webview2]. S'il manque, Sollicito affiche ce message :

> Sollicito a besoin du runtime WebView2 de Microsoft, introuvable sur ce poste. Il est livré avec Windows 11 ; sous Windows 10, installez-le depuis le site de Microsoft.

Ouvrez alors la [page de téléchargement de WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/consumer/), chez Microsoft, cliquez sur « Download WebView2 »[^webview2-page], lancez le fichier obtenu, puis relancez Sollicito.

**Vos données.** Sollicito crée sa base tout seul, dans le dossier `%LOCALAPPDATA%\Sollicito` de votre compte Windows. Vous n'avez rien à configurer.

**L'IA, facultative.** Le tableau de bord vous demande « Connecter une IA ? ». « Continuer sans IA » suffit pour commencer, et la question ne revient plus. « Choisir une IA » vous mène aux réglages, où vous pourrez aussi changer d'avis plus tard.

Avec Claude, créez une clé limitée à un seul workspace : Sollicito n'envoie pas l'identifiant de workspace que la documentation d'Anthropic demande pour les autres clés[^workspace]. Ce qu'Anthropic ou OpenAI gardent de vos demandes relève de leurs conditions : lisez-les avant de choisir.

## Installer une nouvelle version

Sollicito ne se met pas à jour tout seul. Pour changer de version :

1. fermez Sollicito ;
2. téléchargez, vérifiez et lancez le nouvel exe, comme la première fois. L'avertissement de SmartScreen peut revenir.

Vos données restent en place. Quand une nouvelle version doit modifier la structure de votre base, elle en fait d'abord une copie, dans `%LOCALAPPDATA%\Sollicito\Sauvegardes`. Si cette copie échoue, la base n'est pas touchée. Vous pouvez ensuite supprimer l'ancien exe.

## Tout retirer

1. Si vous voulez garder une trace de vos données, exportez-les d'abord : « Réglages », puis « Exporter vos données ». Le fichier d'export n'est pas chiffré : rangez-le en lieu sûr.
2. Fermez Sollicito et supprimez l'exe. **Vos données restent sur le PC.**
3. Pour les effacer aussi : appuyez sur la touche Windows et R, saisissez `%LOCALAPPDATA%`, validez par « OK »[^executer], puis supprimez le dossier `Sollicito`. Il contient la base, les sauvegardes qui y sont rangées, vos réglages et votre clé d'IA. **Une fois ce dossier supprimé pour de bon, Sollicito ne peut rien retrouver.**

Deux choses restent ailleurs :

- vos copies hors de ce dossier : les sauvegardes d'un autre dossier que vous auriez choisi, et vos exports ;
- des fichiers techniques, sans vos données, que l'exe a décompressés dans `%TEMP%\.net`, dans un dossier au nom de chaque exe lancé (`Sollicito-X.Y.Z-win-x64`)[^extraction]. Vous pouvez supprimer ces dossiers.

## Sources

Pages consultées le 2026-11-28. Les pages en français de Microsoft ne s'accordent pas toujours sur les libellés.

[^edge]: Microsoft Learn, SmartScreen dans Microsoft Edge (« Conserver », « Afficher plus », « Conserver quand même ») : <https://learn.microsoft.com/fr-fr/deployedge/microsoft-edge-security-smartscreen> ; Microsoft Learn, interruptions de téléchargement dans Edge (menu « … ») : <https://learn.microsoft.com/en-us/deployedge/microsoft-edge-security-downloads-interruptions>
[^powershell]: Microsoft Learn, démarrer Windows PowerShell : <https://learn.microsoft.com/fr-fr/powershell/scripting/windows-powershell/starting-windows-powershell>
[^empreinte]: Microsoft Learn, `Get-FileHash` : <https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/get-filehash>
[^condense]: GitHub Changelog, les empreintes des fichiers d'une version (03/06/2025) : <https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/> ; forme `sha256:`, coupe à l'écran et bouton de copie relevés le 2026-11-28 sur une page de version publique : <https://github.com/cli/cli/releases/tag/v2.105.0>
[^gh]: GitHub CLI, installation sous Windows : <https://github.com/cli/cli/blob/trunk/docs/install_windows.md>
[^winget]: Microsoft Learn, WinGet : <https://learn.microsoft.com/fr-fr/windows/package-manager/winget/>
[^smartscreen]: Microsoft Learn, la réputation SmartScreen : <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation>
[^passer]: Microsoft Learn, publier sa première application Windows (« Plus d'informations », « Exécuter de toute façon », « Windows a protégé votre PC ») : <https://learn.microsoft.com/fr-fr/windows/apps/package-and-deploy/publish-first-app> ; en anglais (« More info », « Run anyway ») : <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app>
[^sac-securite]: Support Microsoft, contrôle des applications et du navigateur dans Sécurité Windows : <https://support.microsoft.com/fr-fr/windows/app-browser-control-in-the-windows-security-app-8f68fb65-ebb4-3cfb-4bd7-ef0f376f3dc3>
[^sac-faq]: Support Microsoft, questions fréquentes sur Smart App Control : <https://support.microsoft.com/fr-fr/topic/what-is-smart-app-control-285ea03d-fa88-4d56-882e-6698afdb7003>
[^sac-technique]: Microsoft Learn, Smart App Control : <https://learn.microsoft.com/fr-fr/windows/apps/develop/smart-app-control/overview> ; essayer une application avec Smart App Control : <https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/test-your-app-with-smart-app-control>
[^webview2]: Microsoft Learn, distribuer le runtime WebView2 : <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution>
[^webview2-page]: Microsoft, télécharger WebView2 : <https://developer.microsoft.com/en-us/microsoft-edge/webview2/consumer/>
[^workspace]: Anthropic, aperçu de l'API, en-tête `anthropic-workspace-id` : <https://platform.claude.com/docs/en/api/overview>
[^executer]: Microsoft Learn, la boîte « Exécuter » (touche Windows et R) avec un chemin à variable, dans un autre contexte : <https://learn.microsoft.com/fr-fr/troubleshoot/microsoftteams/teams-administration/clear-teams-cache>
[^extraction]: Microsoft Learn, .NET, déploiement en un seul fichier (bibliothèques décompressées sous `%TEMP%\.net`) : <https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview> ; nom du dossier, d'après le code de .NET : <https://github.com/dotnet/runtime/blob/v10.0.0/src/native/corehost/bundle/extractor.cpp>
