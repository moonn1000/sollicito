using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Fenetre.Tests;

/// <summary>
/// Ce que la fenêtre dit d'un lancement qui a échoué (#26) : une mise à jour de la base ratée a son message, qui dit que
/// la base n'a pas changé et où est la copie d'avant ; toute autre erreur, le message général. Aucun ne cite de chemin.
/// </summary>
public sealed class MessagesDuLancementTests
{
    public static TheoryData<Exception> EchecsDeLaMiseAJour => new()
    {
        new MiseAJourDeLaBaseEchouee(RaisonDeLEchec.Migration, "SqliteException"),
        new InvalidOperationException("Enveloppe", new MiseAJourDeLaBaseEchouee(RaisonDeLEchec.Migration, "SqliteException")),
        new AggregateException(new IOException("autre"), new MiseAJourDeLaBaseEchouee(RaisonDeLEchec.Migration, "SqliteException")),
    };

    [Theory]
    [MemberData(nameof(EchecsDeLaMiseAJour))]
    public void Une_migration_ratee_dit_que_la_base_n_a_pas_change_et_ou_est_la_copie_d_avant(Exception erreur)
    {
        var message = MessagesDuLancement.Pour(erreur);

        Assert.Equal(MiseAJourDeLaBaseEchouee.MessageDeLaMigration, message);
        Assert.Contains("elle est restée telle qu'avant", message, StringComparison.Ordinal);
        Assert.Contains(@"%LOCALAPPDATA%\Sollicito\Sauvegardes", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_copie_d_avant_impossible_dit_que_la_base_n_a_pas_ete_touchee()
    {
        var message = MessagesDuLancement.Pour(new MiseAJourDeLaBaseEchouee(RaisonDeLEchec.CopieDAvant, "IOException"));

        Assert.Equal(MiseAJourDeLaBaseEchouee.MessageDeLaCopieDAvant, message);
        Assert.Contains("il ne l'a pas touchée", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Toute_autre_erreur_donne_le_message_general_sans_reprendre_le_sien()
    {
        var message = MessagesDuLancement.Pour(new IOException(@"Cannot create 'C:\Users\Compte.Fictif\AppData\Local\Sollicito'"));

        Assert.Equal(MessagesDuLancement.ApiNonDemarree, message);
    }

    [Fact]
    public void Aucun_message_ne_cite_un_chemin_developpe()
    {
        foreach (var message in new[] { MiseAJourDeLaBaseEchouee.MessageDeLaMigration, MiseAJourDeLaBaseEchouee.MessageDeLaCopieDAvant, MessagesDuLancement.ApiNonDemarree })
        {
            Assert.DoesNotContain(@":\", message, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.UserName, message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
