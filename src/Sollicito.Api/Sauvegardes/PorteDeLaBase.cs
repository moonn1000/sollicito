namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// La porte de la base (#28, ADR-0012) : tout ce qui lit ou écrit la base passe par elle, les routes de l'API
/// (<see cref="PorteDesRequetes"/>) comme les appels de la fenêtre hôte (le brouillon d'une relance). Une restauration la
/// ferme : plus rien n'entre, et elle attend que ce qui est entré soit ressorti, avant de copier la base puis de la
/// remplacer. Elle se rouvre aussitôt après. Un futur service de fond qui écrit la base, comme l'import d'offres de la V2,
/// passera par elle aussi.
/// </summary>
public sealed class PorteDeLaBase
{
    /// <summary>Le titre du refus d'une route pendant une restauration (503).</summary>
    public const string TitreDuRefus = "Une restauration de la base est en cours : réessayez dans un instant.";

    private readonly Lock _verrou = new();
    private int _dedans;
    private bool _fermee;
    private TaskCompletionSource? _vide;

    /// <summary>Vrai pendant une restauration, porte fermée.</summary>
    public bool EstFermee
    {
        get
        {
            lock (_verrou)
            {
                return _fermee;
            }
        }
    }

    /// <summary>Entre, si la porte est ouverte ; à refermer par <see cref="Sortir"/>. Rend faux, porte fermée.</summary>
    public bool EssayerDEntrer()
    {
        lock (_verrou)
        {
            if (_fermee)
            {
                return false;
            }

            _dedans++;
            return true;
        }
    }

    /// <summary>Ressort, après une entrée réussie.</summary>
    public void Sortir()
    {
        lock (_verrou)
        {
            _dedans--;
            if (_dedans == 0)
            {
                _vide?.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Ferme la porte, puis attend que tout ce qui était entré soit ressorti, <paramref name="delai"/> au plus. Rend faux,
    /// porte rouverte, si ce délai passe : rien n'a changé.
    /// </summary>
    public async Task<bool> FermerAsync(TimeSpan delai, TimeProvider horloge, CancellationToken annulation)
    {
        Task attente;
        lock (_verrou)
        {
            if (_fermee)
            {
                return false;
            }

            _fermee = true;
            if (_dedans == 0)
            {
                return true;
            }

            _vide = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            attente = _vide.Task;
        }

        try
        {
            await attente.WaitAsync(delai, horloge, annulation);
            return true;
        }
        catch (TimeoutException)
        {
            Ouvrir();
            return false;
        }
        catch (OperationCanceledException)
        {
            Ouvrir();
            throw;
        }
    }

    /// <summary>Rouvre la porte.</summary>
    public void Ouvrir()
    {
        lock (_verrou)
        {
            _fermee = false;
            _vide = null;
        }
    }
}
