using Sollicito.Api.IA;
using Sollicito.Application.IA;
using Sollicito.Domain.Relances;

namespace Sollicito.Api.Offres;

/// <summary>
/// L'extraction d'une offre collée, côté API (#46, ADR-0018) : le choix de l'adaptateur par le réglage de l'IA, une fois
/// le texte admis, la date du jour, et le journal. Elle ne lit ni n'écrit la base : la proposition ne s'enregistre que par
/// le collage, sur le clic de l'utilisateur. Le journal dit l'issue et ses motifs (des listes fermées), en plus de l'entrée
/// de l'adaptateur ; jamais le texte de l'offre, ni celui de l'IA, ni une valeur proposée, ni le nom d'une clé inconnue.
/// </summary>
public sealed partial class ExtractionDesOffres(ReglageDeLIA reglage, TimeProvider horloge, ILogger<ExtractionDesOffres> journal)
{
    /// <summary>
    /// Extrait les champs de <paramref name="texte"/>. Rend l'issue, et le fournisseur qui l'a servie : celui de
    /// l'adaptateur choisi, ou <see cref="FournisseurDIA.Aucun"/> si le texte a été refusé avant tout choix.
    /// </summary>
    public async Task<ExtractionFaite> ExtraireAsync(string? texte, CancellationToken annulation)
    {
        IFournisseurDIA? adaptateur = null;
        var aujourdhui = DelaiDeRelance.JourLocal(horloge.GetUtcNow().UtcDateTime, horloge.LocalTimeZone);
        var issue = await ExtractionDUneOffre.ExtraireAsync(() => adaptateur = reglage.Fournisseur(), texte, aujourdhui, annulation);
        Noter(issue);
        return new ExtractionFaite(issue, adaptateur?.Fournisseur ?? FournisseurDIA.Aucun);
    }

    private void Noter(IssueDeLExtraction issue)
    {
        switch (issue)
        {
            case IssueDeLExtraction.Proposee proposee:
                Proposee(proposee.Champs.Nombre, proposee.Ecartes.Count);
                foreach (var ecarte in proposee.Ecartes)
                {
                    ChampEcarte(ecarte.Champ, ecarte.Motif);
                }

                break;
            case IssueDeLExtraction.HorsSchema hors:
                HorsSchema(hors.Motif, hors.Champ?.ToString() ?? "aucun");
                break;
            case IssueDeLExtraction.Echec { Sorte: SorteDEchec.UneDemandeEnCours }:
                Refusee("une demande en cours");
                break;
            case IssueDeLExtraction.Echec echec:
                Echouee(echec.Sorte);
                break;
            case IssueDeLExtraction.TexteRefuse:
                Refusee("texte refusé");
                break;
            case IssueDeLExtraction.Indisponible indisponible:
                Refusee(indisponible.Raison switch
                {
                    RaisonDIndisponibilite.CleAbsente => "clé absente",
                    RaisonDIndisponibilite.CleIllisible => "clé illisible",
                    _ => "aucune IA prête",
                });
                break;
        }
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Extraction d'une offre : {Proposes} champ(s) proposé(s), {Ecartes} écarté(s).")]
    private partial void Proposee(int proposes, int ecartes);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Extraction d'une offre : un champ écarté ({Champ}, {Motif}).")]
    private partial void ChampEcarte(ChampDeLaProposition champ, MotifDeLEcart motif);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Extraction d'une offre : réponse hors schéma ({Motif}, champ {Champ}).")]
    private partial void HorsSchema(MotifHorsSchema motif, string champ);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "Extraction d'une offre : échec {Sorte}.")]
    private partial void Echouee(SorteDEchec sorte);

    [LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "Extraction d'une offre refusée sans requête ({Motif}).")]
    private partial void Refusee(string motif);
}

/// <summary>Une extraction faite : son issue, et le fournisseur qui l'a servie.</summary>
/// <param name="Issue">L'issue de l'extraction.</param>
/// <param name="Fournisseur">Le fournisseur de l'adaptateur choisi, ou aucun si le texte a été refusé avant.</param>
public sealed record ExtractionFaite(IssueDeLExtraction Issue, FournisseurDIA Fournisseur);
