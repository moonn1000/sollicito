namespace Sollicito.Fenetre;

/// <summary>Ce que Sollicito dit à l'utilisateur quand il ne peut pas s'ouvrir.</summary>
internal static class Signalement
{
    public static void Afficher(string message, IWin32Window? fenetre = null)
    {
#if VERSION_DE_TEST
        // Aucune boîte de dialogue dans la version de test : elle bloquerait le test jusqu'à son délai.
        _ = fenetre;
        Console.Error.WriteLine(message);
#else
        MessageBox.Show(fenetre, message, "Sollicito", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
    }
}
