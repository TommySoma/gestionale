var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/salute");

app.Run();

/// <summary>Punto d'ingresso dell'API, esposto per i test con WebApplicationFactory.</summary>
public partial class Program;
