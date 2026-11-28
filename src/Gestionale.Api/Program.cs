using Gestionale.Api.Autenticazione;
using Gestionale.Api.Endpoint;

var builder = WebApplication.CreateBuilder(args);

builder.AggiungiGestionale();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/salute").AllowAnonymous();

var api = app.MapGroup("/api");
EndpointAutenticazione.Mappa(api);
EndpointUtenti.Mappa(api);
EndpointClienti.Mappa(api);
EndpointImpianti.Mappa(api);
EndpointInterventi.Mappa(api);
EndpointDocumenti.Mappa(api);
EndpointScadenze.Mappa(api);
EndpointCommerciale.Mappa(api);
EndpointRapporti.Mappa(api);
EndpointImportazione.Mappa(api);
EndpointPromemoria.Mappa(api);
EndpointFgas.Mappa(api);

app.Run();

/// <summary>Punto d'ingresso dell'API, esposto per i test con WebApplicationFactory.</summary>
public partial class Program;
