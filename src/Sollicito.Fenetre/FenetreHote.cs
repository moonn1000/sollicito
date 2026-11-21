using System.Data.Common;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Sollicito.Api.Relances;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Securite;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Fenetre;

/// <summary>
/// La fenêtre hôte de l'ADR-0003 : WebView2 dans une fenêtre WinForms, qui n'affiche que l'interface locale et ne
/// laisse rien sortir vers une autre origine (ADR-0001, D9, « Fenêtre »). Elle remet le jeton à l'interface par son
/// canal interne, lui ouvre le dialogue de dossier des sauvegardes (#26, ADR-0008), et ouvre dans la messagerie le
/// brouillon d'une relance (#14, ADR-0011). Le contrôle WebView2 n'est touché que depuis le fil de la fenêtre, où
/// arrivent tous ses événements.
/// </summary>
internal sealed class FenetreHote : Form
{
    private readonly Uri _origine;
    private readonly JetonDeLancement _jeton;
    private readonly Lancement _lancement;
    private readonly SauvegardesDeLaBase _sauvegardes;
    private readonly BrouillonsDesRelances _brouillons;
    private readonly WebView2 _vue = new() { Dock = DockStyle.Fill };
    private CoreWebView2Environment? _environnement;
    // Les demandes de brouillon reçues pendant un autre, servies dans l'ordre ; au-delà, une demande est ignorée.
    private const int BrouillonsEnAttenteAuPlus = 4;
    private readonly Queue<int> _brouillonsEnAttente = new();
    private bool _choixEnCours;
    private bool _brouillonEnCours;

    public FenetreHote(Uri origine, JetonDeLancement jeton, Lancement lancement, SauvegardesDeLaBase sauvegardes, BrouillonsDesRelances brouillons)
    {
        _origine = origine;
        _jeton = jeton;
        _lancement = lancement;
        _sauvegardes = sauvegardes;
        _brouillons = brouillons;
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
    // du dossier des sauvegardes (#26) et le brouillon d'une relance (#14) passent par le même contrôle.
    private void SurMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (ReglesDeLaFenetre.DemandeLeJeton(args.Source, args.WebMessageAsJson, _origine))
        {
            _vue.CoreWebView2.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDuJeton(_jeton.Valeur()));
        }
        else if (ReglesDeLaFenetre.DemandeLeChoixDuDossier(args.Source, args.WebMessageAsJson, _origine) && !_choixEnCours)
        {
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
            _brouillonsEnAttente.Enqueue(relance);
            if (!_brouillonEnCours)
            {
                OuvrirLeBrouillonSuivant();
            }
        }
    }

    private void OuvrirLeBrouillonSuivant()
    {
        if (_brouillonsEnAttente.TryDequeue(out var relance))
        {
            _brouillonEnCours = true;
            BeginInvoke(() => OuvrirLeBrouillon(relance));
        }
    }

    // Le brouillon de la relance, construit à partir des champs que l'API lit en base, puis ouvert dans la messagerie.
    // La page ne reçoit que l'issue, sans rien du brouillon.
    private async void OuvrirLeBrouillon(int relance)
    {
        var issue = IssueDuBrouillon.Refuse;
        try
        {
            if (await _brouillons.PreparerAsync(relance, CancellationToken.None) is { } brouillon
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
            navigateur.PostWebMessageAsJson(ReglesDeLaFenetre.MessageDuBrouillon(relance, issue));
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
        }

        base.Dispose(disposing);
    }
}
