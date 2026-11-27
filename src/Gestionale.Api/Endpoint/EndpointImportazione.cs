using System.Security.Claims;
using System.Text;
using Gestionale.Api.Autenticazione;
using Gestionale.Api.Risorse;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Importazione;
using Gestionale.Domain.Importazione;
using Gestionale.Domain.Sicurezza;
using Microsoft.AspNetCore.Mvc;

namespace Gestionale.Api.Endpoint;

public sealed record SegnalazioneImportazione(int Riga, string Codice, string Messaggio);

public sealed record RispostaImportazione(
    bool Importata,
    int Righe,
    int ClientiNuovi,
    int ClientiEsistenti,
    int SediNuove,
    int ImpiantiNuovi,
    int ImpiantiSaltati,
    int Generatori,
    IReadOnlyList<SegnalazioneImportazione> Errori,
    IReadOnlyList<SegnalazioneImportazione> Avvisi);

/// <summary>
/// Importazione di clienti, sedi e impianti da CSV (ADR 0007). Il file arriva
/// multipart nel campo <c>file</c>. L'anteprima fa tutto il lavoro e lo annulla;
/// l'importazione scrive solo se il file non ha errori. Serve poter scrivere
/// clienti e impianti su tutta l'azienda.
/// </summary>
internal static class EndpointImportazione
{
    private const long LimiteRichiesta = RegoleImportazione.DimensioneMassimaByte + (256 * 1024);

    public static void Mappa(RouteGroupBuilder api)
    {
        var importazioni = api.MapGroup("/importazioni");

        importazioni.MapGet("/modello", Modello)
            .RichiedePermesso(CodiciPermesso.ClientiScrittura);
        foreach (var (percorso, conferma) in new[] { ("/anteprima", false), ("/", true) })
        {
            importazioni.MapPost(percorso, (IFormFile? file, ClaimsPrincipal utente, ServizioImportazione servizio, CancellationToken ct) =>
                    EseguiAsync(file, conferma, utente, servizio, ct))
                .RichiedePermesso(CodiciPermesso.ClientiScrittura)
                .RichiedePermesso(CodiciPermesso.ImpiantiScrittura)
                // L'API usa token Bearer, non cookie: il controllo antiforgery dei form non serve.
                .DisableAntiforgery()
                .WithMetadata(new RequestSizeLimitAttribute(LimiteRichiesta), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = LimiteRichiesta });
        }
    }

    private static async Task<IResult> EseguiAsync(IFormFile? file, bool conferma, ClaimsPrincipal utente, ServizioImportazione servizio, CancellationToken ct)
    {
        // Un'importazione tocca clienti e impianti di tutta l'azienda: l'ambito "propri" non basta.
        if (utente.AmbitoPermesso(CodiciPermesso.ClientiScrittura) != AmbitoPermesso.Tutti
            || utente.AmbitoPermesso(CodiciPermesso.ImpiantiScrittura) != AmbitoPermesso.Tutti)
        {
            return Problemi.Vietato(Messaggi.ImportazioneVietata);
        }

        if (file is null)
        {
            return Problemi.RichiestaNonValida(Messaggi.FileMancante);
        }

        if (file.Length > RegoleImportazione.DimensioneMassimaByte)
        {
            return Problemi.RichiestaNonValida(Messaggi.FileTroppoGrande);
        }

        using var memoria = new MemoryStream();
        await file.CopyToAsync(memoria, ct);

        var esito = await servizio.EseguiAsync(new Contesto(utente.AziendaId(), utente.UtenteId()), memoria.ToArray(), conferma, ct);
        return Results.Ok(new RispostaImportazione(
            esito.Importata, esito.Righe, esito.ClientiNuovi, esito.ClientiEsistenti, esito.SediNuove, esito.ImpiantiNuovi,
            esito.ImpiantiSaltati, esito.Generatori, [.. esito.Errori.Select(Testo)], [.. esito.Avvisi.Select(Testo)]));
    }

    // File di esempio: intestazione con le colonne riconosciute e una riga fittizia con le voci di prova.
    private static IResult Modello()
    {
        var esempio = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RegoleImportazione.Colonne.TipoCliente] = "privato",
            [RegoleImportazione.Colonne.Nome] = "Giulia",
            [RegoleImportazione.Colonne.Cognome] = "Esempio",
            [RegoleImportazione.Colonne.Email] = "giulia.esempio@example.com",
            [RegoleImportazione.Colonne.Indirizzo] = "Via delle Prove",
            [RegoleImportazione.Colonne.Civico] = "1",
            [RegoleImportazione.Colonne.Cap] = "50100",
            [RegoleImportazione.Colonne.Comune] = "Firenze",
            [RegoleImportazione.Colonne.Provincia] = "FI",
            [RegoleImportazione.Colonne.TipoImpianto] = "prova_riscaldamento",
            [RegoleImportazione.Colonne.AnnoInstallazione] = "2018",
            [RegoleImportazione.Colonne.PotenzaKw] = "24,5",
            [RegoleImportazione.Colonne.Combustibile] = "prova_gas_naturale",
            [RegoleImportazione.Colonne.ManutenzioneMesi] = "12",
            [RegoleImportazione.Colonne.TipoGeneratore] = "prova_caldaia",
            [RegoleImportazione.Colonne.Marca] = "Marca di prova",
            [RegoleImportazione.Colonne.Matricola] = "PROVA-0001",
            [RegoleImportazione.Colonne.InstallatoIl] = "15/03/2018",
        };
        var colonne = RegoleImportazione.ColonneModello;
        var testo = string.Join(';', colonne) + "\r\n" + string.Join(';', colonne.Select(c => esempio.GetValueOrDefault(c, ""))) + "\r\n";

        // Con il BOM Excel apre il file in UTF-8 e mostra bene gli accenti.
        var contenuto = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(testo)).ToArray();
        return Results.File(contenuto, "text/csv; charset=utf-8", "modello-importazione.csv");
    }

    // Ogni codice ha il suo testo nel file di traduzione, con lo stesso nome; il dettaglio (valore, colonna) va in coda.
    private static SegnalazioneImportazione Testo(SegnalazioneRiga s)
    {
        var testo = Messaggi.ResourceManager.GetString(s.Codice, Messaggi.Culture) ?? s.Codice;
        return new SegnalazioneImportazione(s.Riga, s.Codice, s.Dettaglio is null ? testo : $"{testo}: {s.Dettaglio}");
    }
}
