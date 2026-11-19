namespace Sollicito.Fenetre.Tests;

/// <summary>
/// Le dossier de données et le profil du navigateur intégré vivent sous <c>%LOCALAPPDATA%\Sollicito</c>, hors du
/// dossier de l'exe (ADR-0001, D10 ; ADR-0003, « Dossier de profil »).
/// </summary>
public sealed class DossierDeDonneesTests
{
    [Fact]
    public void Le_profil_du_navigateur_vit_dans_le_dossier_de_donnees_de_l_utilisateur()
    {
        var localDesApplications = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var dossier = DossierDeDonnees.DeLUtilisateur();

        Assert.Equal(Path.Combine(localDesApplications, "Sollicito"), dossier);
        Assert.Equal(Path.Combine(localDesApplications, "Sollicito", "WebView2"), DossierDeDonnees.ProfilDuNavigateur(dossier));
        Assert.False(dossier.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AppData\\Local")]
    public void Sans_dossier_local_absolu_le_lancement_s_arrete(string localDesApplications)
    {
        Assert.Throws<InvalidOperationException>(() => DossierDeDonnees.Sous(localDesApplications));
    }
}
