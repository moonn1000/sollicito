// Hôte de la Web API locale (ADR-0001, D2 et D9 ; ADR-0003).
// Il n'expose encore aucune route : la première arrive avec l'API locale,
// en même temps que son écoute sur 127.0.0.1, son jeton et ses refus.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();
