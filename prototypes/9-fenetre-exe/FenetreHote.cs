using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Prototype9;

/// <summary>Fenêtre hôte de l'ADR-0003 : WebView2 dans une fenêtre WinForms, limitée à l'origine locale.</summary>
sealed class FenetreHote : Form
{
    // Domaine réservé (RFC 2606) : il ne se résout jamais. Au pire, une requête DNS part vers le résolveur du poste,
    // mais aucun serveur n'est joint, même si la requête de navigation partait malgré l'annulation.
    const string AutreSite = "https://autre-origine.invalid/";
    const string TitrePage = "Prototype #9";

    readonly Contexte contexte;
    readonly WebView2 vue = new() { Dock = DockStyle.Fill };
    readonly TaskCompletionSource accuse = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<bool>? navigationAttendue;
    TaskCompletionSource<CoreWebView2WebErrorStatus>? finNavigationAttendue;
    Uri? cibleAttendue;
    ulong? idNavigationAttendue;
    string? valeurEnvoyee;

    public FenetreHote(Contexte contexte)
    {
        this.contexte = contexte;
        Text = "Sollicito : prototype #9";
        ClientSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(vue);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await InitialiserAsync();
            if (contexte.Autotest)
            {
                await DeroulerAutotestAsync();
            }
        }
        catch (Exception ex)
        {
            contexte.Rapport.Erreur("fenêtre", ex);
            if (!contexte.Autotest)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        if (contexte.Autotest)
        {
            Close();
        }
    }

    async Task InitialiserAsync()
    {
        contexte.Rapport.Etape("création de l'environnement WebView2");
        // Dossier de profil explicite : par défaut, WebView2 l'écrirait à côté de l'exe.
        var environnement = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: contexte.DossierProfil);
        await vue.EnsureCoreWebView2Async(environnement);
        var core = vue.CoreWebView2;
        contexte.IdProcessusNavigateur = core.BrowserProcessId;
        contexte.Rapport.Section("webview2", new
        {
            versionRuntime = environnement.BrowserVersionString,
            versionSdk = typeof(CoreWebView2Environment).Assembly.GetName().Version?.ToString(),
            dossierProfil = Rapport.Masquer(contexte.DossierProfil),
        });

        core.NavigationStarting += SurNavigation;
        core.NavigationCompleted += SurFinNavigation;
        core.WebMessageReceived += SurMessage;
        contexte.Rapport.Etape("navigation vers " + contexte.Origine);
        core.Navigate(contexte.Origine.AbsoluteUri);
    }

    // Toute navigation hors de l'origine de la page locale est annulée (ADR-0001, D9).
    void SurNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var permise = Origines.Identiques(e.Uri, contexte.Origine);
        e.Cancel = !permise;
        contexte.Rapport.Etape($"NavigationStarting {e.Uri} : {(permise ? "permise" : "annulée")}");
        if (cibleAttendue is not null && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri == cibleAttendue)
        {
            idNavigationAttendue = e.NavigationId;
            navigationAttendue?.TrySetResult(e.Cancel);
        }
    }

    // Une navigation annulée se termine par NavigationCompleted, statut OperationCanceled.
    void SurFinNavigation(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        contexte.Rapport.Etape($"NavigationCompleted n° {e.NavigationId} : {(e.IsSuccess ? "réussie" : e.WebErrorStatus.ToString())}");
        if (idNavigationAttendue == e.NavigationId)
        {
            finNavigationAttendue?.TrySetResult(e.WebErrorStatus);
        }
    }

    // Canal interne : on ne lit que les messages dont la source est la page locale.
    void SurMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Origines.Identiques(e.Source, contexte.Origine))
        {
            contexte.Rapport.Etape("message ignoré, source inattendue : " + e.Source);
            return;
        }
        using var document = JsonDocument.Parse(e.WebMessageAsJson);
        var message = document.RootElement;
        switch (message.GetProperty("type").GetString())
        {
            case "prete":
                var lignes = message.GetProperty("candidatures").GetInt32();
                using (var processus = Process.GetCurrentProcess())
                {
                    contexte.Rapport.Section("durees", new
                    {
                        msDepuisMainJusquaPagePrete = contexte.Lancement.ElapsedMilliseconds,
                        msDepuisDemarrageProcessusJusquaPagePrete = (long)(DateTime.Now - processus.StartTime).TotalMilliseconds,
                    });
                }
                contexte.Rapport.Controler("message-page-vers-hote", true, new { type = "prete", source = e.Source });
                contexte.Rapport.Controler("sqlite-lue-par-la-page", lignes == contexte.LignesEnBase, new
                {
                    lignesAffichees = lignes,
                    lignesEnBase = contexte.LignesEnBase,
                    versionSqlite = message.GetProperty("sqlite").GetString(),
                });
                valeurEnvoyee = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                Envoyer(new { type = "bonjour", valeur = valeurEnvoyee });
                break;

            case "accuse":
                var identique = valeurEnvoyee is not null && message.GetProperty("valeur").GetString() == valeurEnvoyee;
                contexte.Rapport.Controler("message-hote-vers-page", identique, new
                {
                    type = "bonjour",
                    accuseRecu = true,
                    valeurRenvoyeeIdentique = identique,
                });
                Envoyer(new { type = "journal", texte = "Accusé de réception reçu par l'hôte : le message a fait l'aller-retour." });
                accuse.TrySetResult();
                break;

            default:
                contexte.Rapport.Etape("message de type inconnu");
                break;
        }
    }

    async Task DeroulerAutotestAsync()
    {
        await accuse.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var core = vue.CoreWebView2;
        var pageAvant = await LireAsync("location.href");
        contexte.Rapport.Controler("page-locale", Origines.Identiques(pageAvant, contexte.Origine), new
        {
            page = pageAvant,
            origineAttendue = contexte.Origine.GetLeftPart(UriPartial.Authority),
        });

        (string Controle, Uri Cible)[] essais =
        [
            ("navigation-autre-site-annulee", new Uri(AutreSite)),
            // Même serveur, autre hôte : une autre origine, que Kestrel servirait si l'annulation échouait.
            ("navigation-meme-serveur-autre-origine-annulee", new Uri($"http://localhost:{contexte.Origine.Port}/")),
        ];
        foreach (var (controle, cible) in essais)
        {
            navigationAttendue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            finNavigationAttendue = new TaskCompletionSource<CoreWebView2WebErrorStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            idNavigationAttendue = null;
            cibleAttendue = cible;
            await core.ExecuteScriptAsync($"location.href = {JsonSerializer.Serialize(cible.AbsoluteUri)};");
            var annulee = await navigationAttendue.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var statut = await finNavigationAttendue.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var pageApres = await LireAsync("location.href");
            contexte.Rapport.Controler(controle,
                annulee && statut == CoreWebView2WebErrorStatus.OperationCanceled && pageApres == pageAvant,
                new
                {
                    cible = cible.AbsoluteUri,
                    navigationStartingAnnulee = annulee,
                    navigationCompleted = statut.ToString(),
                    pageAvant,
                    pageApres,
                });
            Envoyer(new { type = "journal", texte = $"Navigation vers {cible.AbsoluteUri} annulée par l'hôte." });
        }
        cibleAttendue = null;

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var titre = await LireAsync("document.title");
        contexte.Rapport.Controler("fenetre-ouverte", IsHandleCreated && Visible && titre == TitrePage, new
        {
            titreFenetre = Text,
            titrePage = titre,
            largeur = ClientSize.Width,
            hauteur = ClientSize.Height,
        });

        await using (var fichier = File.Create(contexte.CheminCapture))
        {
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fichier);
        }
        var octets = new FileInfo(contexte.CheminCapture).Length;
        var (largeur, hauteur, partEncree) = Analyser(contexte.CheminCapture);
        // Une fenêtre blanche (ou noire) donne une part de pixels non blancs nulle (ou proche de 1).
        contexte.Rapport.Controler("capture", octets > 0 && partEncree is > 0.005 and < 0.95, new
        {
            fichier = Path.GetFileName(contexte.CheminCapture),
            octets,
            largeur,
            hauteur,
            partDePixelsNonBlancs = Math.Round(partEncree, 4),
        });
    }

    // Part des pixels qui ne sont pas blancs, sur un pixel sur quatre.
    static (int Largeur, int Hauteur, double PartEncree) Analyser(string chemin)
    {
        using var image = new Bitmap(chemin);
        long encres = 0, total = 0;
        for (var y = 0; y < image.Height; y += 2)
        {
            for (var x = 0; x < image.Width; x += 2)
            {
                var couleur = image.GetPixel(x, y);
                total++;
                if (couleur.R < 200 || couleur.G < 200 || couleur.B < 200)
                {
                    encres++;
                }
            }
        }
        return (image.Width, image.Height, total == 0 ? 0 : (double)encres / total);
    }

    void Envoyer(object message) => vue.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    async Task<string?> LireAsync(string expression) =>
        JsonSerializer.Deserialize<string>(await vue.CoreWebView2.ExecuteScriptAsync(expression));
}

static class Origines
{
    /// <summary>Même schéma, même hôte, même port que l'origine de la page locale.</summary>
    public static bool Identiques(string? adresse, Uri origine) =>
        Uri.TryCreate(adresse, UriKind.Absolute, out var uri)
        && uri.Scheme == origine.Scheme
        && uri.Host == origine.Host
        && uri.Port == origine.Port;
}
