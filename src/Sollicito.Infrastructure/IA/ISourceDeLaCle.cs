using Sollicito.Application.IA;

namespace Sollicito.Infrastructure.IA;

/// <summary>
/// Là où un adaptateur en ligne lit sa clé (#48), au moment de l'appel, pour son seul fournisseur : la garde de la clé
/// (<see cref="GardeDeLaCle"/>), ou un faux dans les tests.
/// </summary>
public interface ISourceDeLaCle
{
    /// <summary>La clé enregistrée pour <paramref name="fournisseur"/> : absente, lisible, ou illisible avec sa cause.</summary>
    LectureDeLaCle Lire(FournisseurDIA fournisseur);
}
