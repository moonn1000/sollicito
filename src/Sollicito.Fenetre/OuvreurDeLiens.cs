using System.ComponentModel;
using System.Diagnostics;

namespace Sollicito.Fenetre;

/// <summary>Ce qui ouvre un lien externe, hors de la fenêtre.</summary>
internal interface IOuvreurDeLiens
{
    void Ouvrir(Uri lien);
}

/// <summary>
/// Ouvre un lien dans le navigateur par défaut, par le shell de Windows (ADR-0003, « Nouvelles fenêtres ») ; la
/// fenêtre ne lui passe que des adresses absolues en <c>http</c> ou <c>https</c> (<see cref="ReglesDeLaFenetre"/>).
/// </summary>
internal sealed class OuvreurParLeShell : IOuvreurDeLiens
{
    public void Ouvrir(Uri lien)
    {
        try
        {
            using var navigateur = Process.Start(new ProcessStartInfo(lien.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // Aucun navigateur associé au schéma : rien ne s'ouvre.
        }
    }
}
