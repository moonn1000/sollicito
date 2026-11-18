namespace Sollicito.Domain;

// Preuve jetable : le domaine appelle le réseau.
public static class Sonde
{
    public static HttpClient Client() => new();
}
