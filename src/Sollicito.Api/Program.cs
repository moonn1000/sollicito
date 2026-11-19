// Hôte de la Web API locale, lancée seule (ADR-0001, D2 et D9 ; ADR-0003) : sans fenêtre, elle ne sert pas
// l'interface. Sa construction vit dans ApiLocale ; la fenêtre hôte (src/Sollicito.Fenetre) construit la sienne
// par ApiLocale.ConstruirePourLaFenetre, dans le même exe que la fenêtre.
using Sollicito.Api;

ApiLocale.Construire(args).Run();
