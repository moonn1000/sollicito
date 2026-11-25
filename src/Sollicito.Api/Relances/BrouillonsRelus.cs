namespace Sollicito.Api.Relances;

/// <summary>
/// Le texte relu d'un premier jet (#47, ADR-0019), déposé par la page pour la fenêtre hôte, qui l'ouvre dans la messagerie :
/// le message de la page à l'hôte ne porte que l'identifiant de la relance (ADR-0011). Une seule place, en mémoire, jamais
/// en base : un nouveau dépôt la remplace, l'hôte la prend à l'ouverture, une restauration la vide, et elle se perd à la
/// fermeture de Sollicito. Son texte (<see cref="ToString"/>) ne montre rien du brouillon.
/// </summary>
public sealed class BrouillonsRelus
{
    private readonly Lock _verrou = new();
    private int _relance;
    private string? _texte;

    /// <summary>Dépose le texte relu de cette relance, à la place de tout autre.</summary>
    public void Deposer(int relance, string texte)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(relance);
        ArgumentNullException.ThrowIfNull(texte);
        lock (_verrou)
        {
            _relance = relance;
            _texte = texte;
        }
    }

    /// <summary>Prend le texte déposé pour cette relance, qui quitte alors la place ; rien si aucun ne l'est.</summary>
    public string? Prendre(int relance)
    {
        lock (_verrou)
        {
            if (_texte is not { } texte || _relance != relance)
            {
                return null;
            }

            _relance = 0;
            _texte = null;
            return texte;
        }
    }

    /// <summary>Vide la place : une restauration change ce que désignent les identifiants des relances.</summary>
    public void Vider()
    {
        lock (_verrou)
        {
            _relance = 0;
            _texte = null;
        }
    }

    /// <summary>Un texte qui ne montre rien du brouillon.</summary>
    public override string ToString() => "Brouillons relus";
}
