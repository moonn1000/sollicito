// Hôte de la Web API locale (ADR-0001, D2 et D9 ; ADR-0003). Sa construction vit dans ApiLocale,
// que la fenêtre hôte (#13) reprendra pour démarrer l'API dans le même exe.
using Sollicito.Api;

ApiLocale.Construire(args).Run();
