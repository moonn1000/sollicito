using System.Data.Common;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Sollicito.Api.Export;
using Sollicito.Api.Relances;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Securite;
using Sollicito.Application.Export;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Fenetre;

/// <summary>
/// La fenêtre hôte de l'ADR-0003 : WebView2 dans une fenêtre WinForms, qui n'affiche que l'interface locale et ne
/// laisse rien sortir vers une autre origine (ADR-0001, D9, « Fenêtre »). Elle remet le jeton à l'interface par son
/// canal interne, lui ouvre le dialogue de dossier des sauvegardes (#26, ADR-0008), ouvre dans la messagerie le
/// brouillon d'une relance (#14, ADR-0011), ou le texte relu de son premier jet (#47, ADR-0019), restaure une copie
/// gardée après le oui de l'utilisateur dans son propre dialogue (#28, ADR-0012), et exporte toutes les données dans le
/// fichier que l'utilisateur choisit dans le dialogue « Enregistrer sous » (#49, ADR-0020). Le contrôle WebView2 n'est
/// touché que depuis le fil de la fenêtre, où arrivent tous ses événements.
/// </summary>
internal sealed class FenetreHote : Form
{
    /// <summary>Le temps que la fermeture laisse à un export interrompu pour effacer son fichier provisoire.</summary>
    public static readonly TimeSpan AttenteDeLExportALaFermeture = TimeSpan.FromSeconds(5);

    private readonly Uri _origine;
    private readonly JetonDeLancement _jeton;
    private readonly Lancement _lancement;
    private readonly SauvegardesDeLaBase _sauvegardes;
    private readonly BrouillonsDesRelances _brouillons;
    private readonly RestaurationDeLaBase _restauration;
    private readonly ExportDesDonnees _export;
    private readonly WebView2 _vue = new() { Dock = DockStyle.Fill };
    private CoreWebView2Environment? _environnement;
    // Les demandes de brouillon reçues pendant un autre, servies dans l'ordre ; au-delà, une demande est ignorée. Un
    // brouillon relu (#47) prend place dans la même file que celui de #14.
    private const int BrouillonsEnAttenteAuPlus = 4;
    private readonly Queue<(int Relance, bool Relu)> _brouillonsEnAttente = new();
    private bool _choixEnCours;
    private bool _brouillonEnCours;
    // Une restauration à la fois, du message jusqu'à son issue ; la fermeture de la fenêtre l'attend.
    private bool _restaurationEnCours;
    private Task? _remplacementEnCours;
    // Un export à la fois, du message jusqu'à son issue ; la fermeture de la fenêtre l'interrompt, puis l'attend un peu.
    private bool _exportEnCours;
    private FormatDExport _formatEnCours;
    private Task<IssueDeLExport>? _ecritureEnCours;
    private bool _fermetureSansAttendre;
    private readonly CancellationTokenSource _fermeture = new();

    public FenetreHote(Uri origine, JetonDeLancement jeton, Lancement lancement, SauvegardesDeLaBase sauvegardes, BrouillonsDesRelances brouillons, RestaurationDeLaBase restauration, ExportDesDonnees export)
    {
        _origine = origine;
        _jeton = jeton;
        _lancement = lancement;
        _sauvegardes = sauvegardes;
        _brouillons = brouillons;
        _restauration = restauration;
        _export = export;
        Text = "Sollicito";
        ClientSize = new Size(1200, 800);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_vue);
    }

    /// <summary>0 si la fenêtre s'est ouverte puis a été fermée ; 1 si elle n'a pas pu s'ouvrir.</summary>
    public int CodeDeSortie { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await OuvrirAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Abandonner("Sollicito a besoin du runtime WebView2 de Microsoft, introuvable sur ce poste. "
                + "Il est livré avec Windows 11 ; sous Windows 10, installez-le depuis le site de Microsoft.");
        }
        catch (Exception erreur) when (erreur is InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Abandonner("La fenêtre de Sollicito n'a pas pu s'ouvrir.");
        }
    }

    private async Task OuvrirAsync()
    {
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = _lancement.ArgumentsDuNavigateur ?? "",
            AllowSingleSignOnUsingOSPrimaryAccount = false,
        };
        // Profil dans le dossier de données (ADR-0001, D10) : par défaut, WebView2 l'écrirait à côté de l'exe.
        _environnement = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: DossierDeDonnees.ProfilDuNavigateur(_lancement.DossierDeDonnees),
            options);
        await _vue.EnsureCoreWebView2Async(_environnement);

        var navigateur = _vue.CoreWebView2;
        Regler(navigateur.Settings);
        if (_lancement.DossierDesTelechargements is { } telechargements)
        {
            // Un dossier absent ferait retomber le navigateur sur le dossier Téléchargements de l'utilisateur.
            Directory.CreateDirectory(telechargements);
            navigateur.Profile.DefaultDownloadFolderPath = telechargements;
        }

        // Une navigation hors de l'origine est annulée, lien cliqué sur place compris : seul un lien qui ouvre une
        // nouvelle fenêtre s'ouvre dans le navigateur par défaut (ADR-0006).
        navigateur.NavigationStarting += (_, args) =>
        {
            if (!ReglesDeLaFenetre.EstDeLOrigine(args.Uri, _origine))
            {
                args.Cancel = true;
                _lancement.NoterUneNavigationAnnulee?.Invoke(args.Uri);
            }
        };
        navigateur.FrameNavigationStarting += (_, args) => args.Cancel = !ReglesDeLaFenetre.EstDeLOrigine(args.Uri, _origine);
        navigateur.NewWindowRequested += SurNouvelleFenetre;
        navigateur.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
        navigateur.DownloadStarting += (_, args) =>
        {
            args.Cancel = true;
            args.Handled = true;
        };
        navigateur.PermissionRequested += (_, args) =>
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.Handled = true;
        };
        navigateur.WebMessageReceived += SurMessage;

        // Toute requête vers une autre origine reçoit une réponse de la fenêtre elle-même, sans partir sur le réseau :
        // elle double l'annulation des navigations, qui laisse partir sa requête (ADR-0003, « À vérifier »), et la
        // politique de sécurité du contenu.
        navigateur.AddWebResourceRequestedFilter(
            "*",
            CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        navigateur.WebResourceRequested += SurRequete;

        navigateur.Navigate(_origine.AbsoluteUri);
    }

    private static void Regler(CoreWebView2Settings reglages)
    {
        // Version publiée : outils de développement, menu contextuel par défaut et raccourcis du navigateur coupés.
        reglages.AreDevToolsEnabled = ReglagesDeLaVersion.OutilsDuNavigateur;
        reglages.AreDefaultContextMenusEnabled = ReglagesDeLaVersion.OutilsDuNavigateur;
        reglages.AreBrowserAcceleratorKeysEnabled = ReglagesDeLaVersion.OutilsDuNavigateur;
        // Aucun objet de l'hôte exposé aux scripts ; aucune saisie gardée dans le profil, hors de la base.
        reglages.AreHostObjectsAllowed = false;
        reglages.IsGeneralAutofillEnabled = false;
        reglages.IsPasswordAutosaveEnabled = false;
        // La fenêtre ne navigue que vers l'origine locale : pas de vérification de réputation par SmartScreen.
        reglages.IsReputationCheckingRequired = false;
        reglages.IsWebMessageEnabled = true;
    }

    // Jamais de seconde fenêtre WebView2 : un lien http ou https cliqué par l'utilisateur s'ouvre dans le navigateur
    // par défaut ; tout le reste est ignoré.
    private void SurNouvelleFenetre(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (ReglesDeLaFenetre.LienPourLeNavigateur(args.Uri, args.IsUserInitiated) is { } lien)
        {
            _lancement.OuvreurDeLiens.Ouvrir(lien);
        }
    }

    private void SurRequete(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_environnement is not null && !ReglesDeLaFenetre.EstDeLOrigine(args.Request.Uri, _origine))
        {
            args.Response = _environnement.CreateWebResourceResponse(null, 403, "Forbidden", "");
        }
    }

    // Canal du jeton : seul un message dont la source est l'interface le reçoit (ADR-0003, « Canal du jeton »). Le choix
    // du dossier des sauvegardes (#26) et le brouillon d'une relance (#14), relu ou non (#47), passent par le même contrôle.
    private void SurMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (ReglesDeLaFenetre.DemandeLeJeton(args.Source, args.WebMessageAsJson, _origine))
        {
            _vue.CoreWebView2.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDuJeton(_jeton.Valeur()));
        }
        else if (ReglesDeLaFenetre.DemandeLeChoixDuDossier(args.Source, args.WebMessageAsJson, _origine))
        {
            if (_choixEnCours)
            {
                // Un seul choix à la fois. Pas de réponse : la page qui attend le sien recevra sa vraie issue, et une
                // réponse ici la clorait trop tôt.
                return;
            }

            if (_restaurationEnCours || _exportEnCours)
            {
                // Un seul dialogue de l'hôte à la fois : la page apprend que Sollicito est occupé (#118).
                _vue.CoreWebView2.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDuChoixDuDossier(IssueDuChoixDuDossier.Occupe));
                return;
            }

            // Un seul dialogue à la fois, ouvert après ce gestionnaire : un dialogue modal ouvert pendant un événement de
            // WebView2 relancerait la boucle des messages au milieu de cet événement (modèle de threads de WebView2).
            _choixEnCours = true;
            BeginInvoke(ChoisirLeDossierDesSauvegardes);
        }
        else if (ReglesDeLaFenetre.DemandeLeBrouillon(args.Source, args.WebMessageAsJson, _origine, out var relance)
            && _brouillonsEnAttente.Count < BrouillonsEnAttenteAuPlus)
        {
            // Un seul brouillon à la fois, ouvert après ce gestionnaire, comme le dialogue : le shell de Windows peut
            // afficher sa propre fenêtre. Une demande faite pendant un brouillon attend son tour, et reçoit sa réponse.
            MettreEnAttente(relance, relu: false);
        }
        else if (ReglesDeLaFenetre.DemandeLeBrouillonRelu(args.Source, args.WebMessageAsJson, _origine, out var relanceRelue)
            && _brouillonsEnAttente.Count < BrouillonsEnAttenteAuPlus)
        {
            // Le texte relu d'un premier jet (#47), dans la même file.
            MettreEnAttente(relanceRelue, relu: true);
        }
        else if (ReglesDeLaFenetre.DemandeLaRestauration(args.Source, args.WebMessageAsJson, _origine, out var copie))
        {
            if (_restaurationEnCours)
            {
                // Une seule restauration à la fois. Pas de réponse : la page qui attend la sienne recevra sa vraie issue,
                // et une réponse ici, du même identifiant, la clorait trop tôt.
                return;
            }

            if (_choixEnCours || _exportEnCours)
            {
                // Un seul dialogue de l'hôte à la fois : la page apprend que Sollicito est occupé.
                _vue.CoreWebView2.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDeLaRestauration(copie, IssueDansLaFenetre.Occupee));
                return;
            }

            // Le dialogue s'ouvre après ce gestionnaire, comme celui du choix d'un dossier.
            _restaurationEnCours = true;
            BeginInvoke(() => RestaurerLaCopie(copie));
        }
        else if (ReglesDeLaFenetre.DemandeLExport(args.Source, args.WebMessageAsJson, _origine, out var format))
        {
            if (_exportEnCours)
            {
                // Un seul export à la fois. Une demande du même format ne reçoit rien : la page qui attend le sien en
                // recevra la vraie issue, et une réponse ici la clorait trop tôt. Une demande de l'autre format n'attend que
                // sa propre réponse : elle apprend que Sollicito est occupé.
                if (format != _formatEnCours)
                {
                    _vue.CoreWebView2.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDeLExport(format, IssueDeLExport.Occupe));
                }

                return;
            }

            if (_choixEnCours || _restaurationEnCours)
            {
                // Un seul dialogue de l'hôte à la fois : la page apprend que Sollicito est occupé.
                _vue.CoreWebView2.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDeLExport(format, IssueDeLExport.Occupe));
                return;
            }

            // Le dialogue s'ouvre après ce gestionnaire, comme celui du choix d'un dossier.
            _exportEnCours = true;
            _formatEnCours = format;
            BeginInvoke(() => Exporter(format));
        }
    }

    // L'export de toutes les données (#49) : le dialogue « Enregistrer sous », où l'utilisateur seul choisit le fichier,
    // puis l'export, hors du fil de la fenêtre, qui reste vivante. La page reçoit « en cours », puis l'issue, sans chemin ni
    // nom de fichier.
    private async void Exporter(FormatDExport format)
    {
        var issue = IssueDeLExport.Impossible;
        try
        {
            var nom = FormatDeLExport.NomPropose(format, DateOnly.FromDateTime(DateTime.Now));
            var choisi = _lancement.ChoixDUnFichierDExport.Choisir(this, format, nom);
            if (choisi is null)
            {
                _export.NoterLAnnulation(format);
                issue = IssueDeLExport.Annule;
            }
            else if (IsDisposed || _fermeture.IsCancellationRequested)
            {
                // La fenêtre se ferme : rien ne s'écrit, et le journal le dit.
                _export.NoterLInterruption(format);
                issue = IssueDeLExport.Annule;
            }
            else
            {
                PosterLExport(ReglesDeLaFenetre.MessageDeLExportEnCours(format));
                var ecriture = Task.Run(() => _export.ExporterAsync(format, choisi.Chemin, choisi.ARemplacer, _fermeture.Token));
                _ecritureEnCours = ecriture;
                issue = await ecriture;
            }
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or ExternalException or System.Security.SecurityException)
        {
            // Rien ne s'écrit : le dialogue de Windows qui échoue ne ferme pas Sollicito. ObjectDisposedException, une
            // fenêtre fermée entre-temps, est une InvalidOperationException.
        }
        finally
        {
            _exportEnCours = false;
        }

        PosterLExport(ReglesDeLaFenetre.MessageDeLExport(format, issue));
    }

    private void PosterLExport(string message)
    {
        if (!IsDisposed && _vue.CoreWebView2 is { } navigateur)
        {
            navigateur.PostWebMessageAsJson(message);
        }
    }

    // La restauration d'une copie : sa préparation, hors du fil de la fenêtre ; le dialogue, où l'utilisateur seul dit oui ;
    // puis la restauration, hors du fil de la fenêtre aussi, qui reste vivante. La page reçoit « en cours », puis l'issue,
    // sans chemin ni donnée.
    private async void RestaurerLaCopie(string copie)
    {
        var issue = IssueDansLaFenetre.Impossible;
        try
        {
            var preparation = await Task.Run(() => _restauration.PreparerAsync(copie, _fermeture.Token));
            if (preparation.Demande is not { } demande)
            {
                issue = ReglesDeLaFenetre.DansLaFenetre(preparation.Refus ?? IssueDeLaRestauration.Impossible);
            }
            else if (IsDisposed || _fermeture.IsCancellationRequested)
            {
                // La fenêtre s'est fermée pendant la préparation : aucun dialogue, rien ne change.
                issue = IssueDansLaFenetre.Annulee;
            }
            else if (!_lancement.ConfirmationDeLaRestauration.Confirmer(this, TextesDeLaRestauration.Question(demande, TimeZoneInfo.Local)))
            {
                issue = IssueDansLaFenetre.Annulee;
            }
            else
            {
                Poster(copie, IssueDansLaFenetre.EnCours);
                var restauration = Task.Run(() => _restauration.RestaurerAsync(demande, _fermeture.Token));
                _remplacementEnCours = restauration;
                issue = ReglesDeLaFenetre.DansLaFenetre(await restauration);
            }
        }
        catch (OperationCanceledException)
        {
            // La fenêtre se ferme : la préparation s'est interrompue, et la base n'a pas changé.
            issue = IssueDansLaFenetre.Annulee;
        }
        catch (Exception erreur) when (erreur is DbException or InvalidOperationException or IOException or UnauthorizedAccessException
            or ArgumentException or ExternalException)
        {
            // Rien ne se ferme : la restauration n'a pas eu lieu, ou s'est défaite. ObjectDisposedException, une fenêtre
            // fermée entre-temps, est une InvalidOperationException.
        }
        finally
        {
            _restaurationEnCours = false;
        }

        Poster(copie, issue);
    }

    private void Poster(string copie, IssueDansLaFenetre issue)
    {
        if (!IsDisposed && _vue.CoreWebView2 is { } navigateur)
        {
            navigateur.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDeLaRestauration(copie, issue));
        }
    }

    // Fermer pendant une restauration attend sa fin : la préparation s'interrompt, un remplacement commencé va au bout.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_remplacementEnCours is { IsCompleted: false } restauration)
        {
            e.Cancel = true;
            _fermeture.Cancel();
            restauration.ContinueWith(_ => BeginInvoke(Close), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }

        // Un export en cours s'interrompt, et la fermeture attend qu'il efface son fichier provisoire, quelques secondes au
        // plus ; au-delà, la fenêtre se ferme quand même, et le journal dit que le provisoire a pu rester.
        if (_ecritureEnCours is { IsCompleted: false } export && !_fermetureSansAttendre)
        {
            e.Cancel = true;
            _fermeture.Cancel();
            var format = _formatEnCours;
            export.WaitAsync(AttenteDeLExportALaFermeture).ContinueWith(
                attente =>
                {
                    if (attente.IsFaulted)
                    {
                        _fermetureSansAttendre = true;
                        _export.NoterLaFermetureAvantLaFin(format);
                    }

                    BeginInvoke(Close);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            return;
        }

        // Une préparation en cours, avant le dialogue, s'arrête : la restauration n'aura pas lieu.
        _fermeture.Cancel();
        base.OnFormClosing(e);
    }

    private void MettreEnAttente(int relance, bool relu)
    {
        _brouillonsEnAttente.Enqueue((relance, relu));
        if (!_brouillonEnCours)
        {
            OuvrirLeBrouillonSuivant();
        }
    }

    private void OuvrirLeBrouillonSuivant()
    {
        if (_brouillonsEnAttente.TryDequeue(out var demande))
        {
            _brouillonEnCours = true;
            BeginInvoke(() => OuvrirLeBrouillon(demande.Relance, demande.Relu));
        }
    }

    // Le brouillon de la relance, construit à partir des champs que l'API lit en base, puis ouvert dans la messagerie ;
    // relu (#47), son corps est le texte que la page a déposé, revérifié. La page ne reçoit que l'issue, sans rien du
    // brouillon.
    private async void OuvrirLeBrouillon(int relance, bool relu)
    {
        var issue = IssueDuBrouillon.Refuse;
        try
        {
            var preparation = relu
                ? _brouillons.PreparerLeBrouillonReluAsync(relance, CancellationToken.None)
                : _brouillons.PreparerAsync(relance, CancellationToken.None);
            if (await preparation is { } brouillon
                && LienDuBrouillon.Construire(brouillon) is { } lien)
            {
                issue = _lancement.OuvreurDeBrouillons.Ouvrir(lien) ? IssueDuBrouillon.Ouvert : IssueDuBrouillon.Impossible;
            }
        }
        catch (Exception erreur) when (erreur is DbException or InvalidOperationException or IOException
            or UnauthorizedAccessException or FormatException or ArgumentException or ExternalException)
        {
            // Rien ne s'ouvre : une base occupée ou illisible, un texte que l'encodage refuse, un shell qui échoue ne
            // ferment pas Sollicito.
        }
        finally
        {
            _brouillonEnCours = false;
        }

        if (!IsDisposed && _vue.CoreWebView2 is { } navigateur)
        {
            navigateur.PostWebMessageAsJson(relu ? ReglesDeLaFenetre.MessageDuBrouillonRelu(relance, issue) : ReglesDeLaFenetre.MessageDuBrouillon(relance, issue));
            OuvrirLeBrouillonSuivant();
        }
    }

    // Le dialogue de dossier, puis une copie dans le dossier choisi, qui en fait celui des sauvegardes si elle réussit. La
    // page reçoit l'issue, sans chemin, et relit l'état par l'API.
    private async void ChoisirLeDossierDesSauvegardes()
    {
        var issue = IssueDuChoixDuDossier.Refuse;
        try
        {
            var choisi = _lancement.ChoixDUnDossier.Choisir(this, _sauvegardes.Etat.Dossier);
            issue = choisi is null
                ? IssueDuChoixDuDossier.Annule
                : await _sauvegardes.ChoisirLeDossierAsync(choisi, CancellationToken.None) == IssueDuChoix.Choisi
                    ? IssueDuChoixDuDossier.Choisi
                    : IssueDuChoixDuDossier.Refuse;
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Runtime.InteropServices.ExternalException or System.Security.SecurityException)
        {
            // Rien ne change : l'ancien dossier reste. ExternalException couvre les erreurs du dialogue de Windows
            // (COMException, Win32Exception) : elles ne ferment pas Sollicito.
        }
        finally
        {
            _choixEnCours = false;
        }

        if (!IsDisposed && _vue.CoreWebView2 is { } navigateur)
        {
            navigateur.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDuChoixDuDossier(issue));
        }
    }

    private void Abandonner(string message)
    {
        CodeDeSortie = 1;
        Signalement.Afficher(message, this);
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _vue.Dispose();
            _fermeture.Dispose();
        }

        base.Dispose(disposing);
    }
}
