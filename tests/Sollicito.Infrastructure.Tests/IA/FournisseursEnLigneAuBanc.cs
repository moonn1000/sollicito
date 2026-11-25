using System.Net;
using Sollicito.Application.IA;
using Sollicito.Tests.Commun;
using static Sollicito.Infrastructure.Tests.IA.OutilsDesFournisseurs;

namespace Sollicito.Infrastructure.Tests.IA;

/// <summary>L'adaptateur de Claude au banc de contrat du port (#43, #48), sur une réponse fictive.</summary>
public sealed class ClaudeEnLigneAuBanc : ContratDUnFournisseurDIA
{
    protected override IFournisseurDIA Creer() =>
        Adaptateur(FournisseurDIA.Claude, new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseDeClaude("Une réponse fictive."))));
}

/// <summary>L'adaptateur d'OpenAI au banc de contrat du port (#43, #48), sur une réponse fictive.</summary>
public sealed class OpenAIEnLigneAuBanc : ContratDUnFournisseurDIA
{
    protected override IFournisseurDIA Creer() =>
        Adaptateur(FournisseurDIA.OpenAI, new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseDOpenAI("Une réponse fictive."))));
}
