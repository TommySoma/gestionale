using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gestionale.Desktop.Risorse;

namespace Gestionale.Desktop.Servizi;

/// <summary>Esito di una chiamata: il valore se è andata bene, altrimenti un messaggio per l'utente.</summary>
public sealed record Risposta<T>(HttpStatusCode Stato, T? Valore, string? Messaggio)
{
    public bool Riuscita => (int)Stato is >= 200 and < 300;
}

/// <summary>
/// Unico punto di contatto con il server: il client non parla mai con il database.
/// Tiene il token di accesso in memoria, lo rinnova prima che scada e salva il
/// token di rinnovo solo nel portachiavi di sistema.
/// </summary>
public sealed class ClientApi : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan s_anticipoRinnovo = TimeSpan.FromMinutes(1);

    private readonly IPortachiavi _portachiavi;
    private readonly TimeProvider _orologio;
    private readonly Func<Uri, HttpClient> _creaClient;
    private readonly SemaphoreSlim _rinnovo = new(1, 1);
    private HttpClient? _http;
    private TokenSessione? _sessione;

    public ClientApi(IPortachiavi portachiavi, TimeProvider orologio, Func<Uri, HttpClient>? creaClient = null)
    {
        _portachiavi = portachiavi;
        _orologio = orologio;
        _creaClient = creaClient ?? (indirizzo => new HttpClient { BaseAddress = indirizzo, Timeout = TimeSpan.FromSeconds(30) });
    }

    /// <summary>Si chiama all'uscita o quando il server rifiuta la sessione.</summary>
    public event EventHandler? SessioneTerminata;

    public Uri? Server { get; private set; }

    public bool Autenticato => _sessione is not null;

    public void ImpostaServer(Uri indirizzo)
    {
        ArgumentNullException.ThrowIfNull(indirizzo);
        if (Server == indirizzo && _http is not null)
        {
            return;
        }

        _http?.Dispose();
        _http = _creaClient(indirizzo);
        Server = indirizzo;
        _sessione = null;
    }

    // --- Accesso -----------------------------------------------------------

    public async Task<Risposta<RispostaAccesso>> AccediAsync(string email, string password, CancellationToken ct = default)
    {
        var risposta = await InviaAsync<RispostaAccesso>(HttpMethod.Post, "api/accesso", new { email, password, dispositivo = Dispositivo }, null, ct);
        if (risposta.Valore?.Token is { } token)
        {
            AvviaSessione(token);
        }

        return risposta;
    }

    public async Task<Risposta<RispostaAccesso>> VerificaMfaAsync(string tokenPassaggio, string codice, CancellationToken ct = default)
    {
        var risposta = await InviaAsync<RispostaAccesso>(HttpMethod.Post, "api/accesso/mfa", new { codice, dispositivo = Dispositivo }, tokenPassaggio, ct);
        if (risposta.Valore?.Token is { } token)
        {
            AvviaSessione(token);
        }

        return risposta;
    }

    public Task<Risposta<ConfigurazioneMfa>> ConfiguraMfaAsync(string tokenPassaggio, CancellationToken ct = default) =>
        InviaAsync<ConfigurazioneMfa>(HttpMethod.Post, "api/mfa/configurazione", null, tokenPassaggio, ct);

    public async Task<Risposta<AttivazioneMfa>> AttivaMfaAsync(string tokenPassaggio, string codice, CancellationToken ct = default)
    {
        var risposta = await InviaAsync<AttivazioneMfa>(HttpMethod.Post, "api/mfa/attivazione", new { codice, dispositivo = Dispositivo }, tokenPassaggio, ct);
        if (risposta.Valore?.Token is { } token)
        {
            AvviaSessione(token);
        }

        return risposta;
    }

    /// <summary>Riprende la sessione salvata nel portachiavi, se c'è ed è ancora valida.</summary>
    public async Task<bool> RiprendiSessioneAsync(CancellationToken ct = default)
    {
        if (Server is null || _portachiavi.Leggi(Account) is not { } tokenRinnovo)
        {
            return false;
        }

        return await RinnovaAsync(tokenRinnovo, ct);
    }

    public async Task EsciAsync(CancellationToken ct = default)
    {
        if (_sessione is { } sessione)
        {
            // Anche se il server non risponde, sul computer la sessione si chiude.
            try
            {
                await InviaAsync<object>(HttpMethod.Post, "api/sessione/uscita", new { tokenRinnovo = sessione.TokenRinnovo }, sessione.TokenAccesso, ct);
            }
            catch (HttpRequestException)
            {
            }
        }

        TerminaSessione();
    }

    public Task<Risposta<Profilo>> ProfiloAsync(CancellationToken ct = default) =>
        AutenticataAsync<Profilo>(HttpMethod.Get, "api/io", null, ct);

    // --- Anagrafiche -------------------------------------------------------

    public Task<Risposta<Pagina<RiepilogoCliente>>> CercaClientiAsync(string? testo, string? tipo, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/clienti?pagina={pagina}&dimensione={dimensione}";
        if (!string.IsNullOrWhiteSpace(testo))
        {
            query += "&cerca=" + Uri.EscapeDataString(testo.Trim());
        }

        if (!string.IsNullOrWhiteSpace(tipo))
        {
            query += "&tipo=" + Uri.EscapeDataString(tipo);
        }

        return AutenticataAsync<Pagina<RiepilogoCliente>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<Cliente>> ClienteAsync(Guid clienteId, CancellationToken ct = default) =>
        AutenticataAsync<Cliente>(HttpMethod.Get, $"api/clienti/{clienteId}", null, ct);

    public Task<Risposta<EsitoScrittura>> CreaClienteAsync(DatiCliente dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Post, "api/clienti", dati, ct);

    public Task<Risposta<EsitoScrittura>> AggiornaClienteAsync(Guid clienteId, DatiCliente dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Put, $"api/clienti/{clienteId}", dati, ct);

    public Task<Risposta<object>> EliminaClienteAsync(Guid clienteId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Delete, $"api/clienti/{clienteId}?versione={versione}", null, ct);

    public Task<Risposta<object>> AnonimizzaClienteAsync(Guid clienteId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Post, $"api/clienti/{clienteId}/anonimizzazione?versione={versione}", null, ct);

    public Task<Risposta<EsitoScrittura>> CreaSedeAsync(Guid clienteId, DatiSede dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Post, $"api/clienti/{clienteId}/sedi", dati, ct);

    public Task<Risposta<EsitoScrittura>> AggiornaSedeAsync(Guid clienteId, Guid sedeId, DatiSede dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Put, $"api/clienti/{clienteId}/sedi/{sedeId}", dati, ct);

    public Task<Risposta<Azienda>> AziendaAsync(CancellationToken ct = default) =>
        AutenticataAsync<Azienda>(HttpMethod.Get, "api/azienda", null, ct);

    public Task<Risposta<EsitoScrittura>> AggiornaAziendaAsync(DatiAzienda dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Put, "api/azienda", dati, ct);

    public Task<Risposta<object>> EliminaSedeAsync(Guid clienteId, Guid sedeId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Delete, $"api/clienti/{clienteId}/sedi/{sedeId}?versione={versione}", null, ct);

    public Task<Risposta<EsitoScrittura>> CreaContattoAsync(Guid clienteId, DatiContatto dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Post, $"api/clienti/{clienteId}/contatti", dati, ct);

    public Task<Risposta<object>> EliminaContattoAsync(Guid clienteId, Guid contattoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Delete, $"api/clienti/{clienteId}/contatti/{contattoId}?versione={versione}", null, ct);

    public Task<Risposta<IReadOnlyList<FinalitaPrivacy>>> FinalitaAsync(CancellationToken ct = default) =>
        AutenticataAsync<IReadOnlyList<FinalitaPrivacy>>(HttpMethod.Get, "api/finalita-privacy", null, ct);

    public Task<Risposta<EsitoScrittura>> RegistraConsensoAsync(Guid clienteId, DatiConsenso dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Post, $"api/clienti/{clienteId}/consensi", dati, ct);

    // --- Impianti ----------------------------------------------------------

    public Task<Risposta<Pagina<RiepilogoImpianto>>> CercaImpiantiAsync(
        string? testo, Guid? clienteId, string? stato, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/impianti?pagina={pagina}&dimensione={dimensione}";
        if (!string.IsNullOrWhiteSpace(testo))
        {
            query += "&cerca=" + Uri.EscapeDataString(testo.Trim());
        }

        if (clienteId is { } cliente)
        {
            query += "&cliente=" + cliente;
        }

        if (!string.IsNullOrWhiteSpace(stato))
        {
            query += "&stato=" + Uri.EscapeDataString(stato);
        }

        return AutenticataAsync<Pagina<RiepilogoImpianto>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<Impianto>> ImpiantoAsync(Guid impiantoId, CancellationToken ct = default) =>
        AutenticataAsync<Impianto>(HttpMethod.Get, $"api/impianti/{impiantoId}", null, ct);

    public Task<Risposta<VociImpianti>> VociImpiantiAsync(CancellationToken ct = default) =>
        AutenticataAsync<VociImpianti>(HttpMethod.Get, "api/impianti/voci", null, ct);

    public Task<Risposta<EsitoScritturaImpianto>> CreaImpiantoAsync(DatiImpianto dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaImpianto>(HttpMethod.Post, "api/impianti", dati, ct);

    public Task<Risposta<EsitoScritturaImpianto>> AggiornaImpiantoAsync(Guid impiantoId, DatiImpianto dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaImpianto>(HttpMethod.Put, $"api/impianti/{impiantoId}", dati, ct);

    public Task<Risposta<object>> EliminaImpiantoAsync(Guid impiantoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Delete, $"api/impianti/{impiantoId}?versione={versione}", null, ct);

    public Task<Risposta<EsitoScritturaImpianto>> CreaGeneratoreAsync(Guid impiantoId, DatiGeneratore dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaImpianto>(HttpMethod.Post, $"api/impianti/{impiantoId}/generatori", dati, ct);

    public Task<Risposta<EsitoScritturaImpianto>> AggiornaGeneratoreAsync(Guid impiantoId, Guid generatoreId, DatiGeneratore dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaImpianto>(HttpMethod.Put, $"api/impianti/{impiantoId}/generatori/{generatoreId}", dati, ct);

    public Task<Risposta<EsitoScritturaImpianto>> CreaRiferimentoCatastoAsync(Guid impiantoId, DatiRiferimentoCatasto dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaImpianto>(HttpMethod.Post, $"api/impianti/{impiantoId}/riferimenti-catasto", dati, ct);

    public Task<Risposta<EsitoScritturaImpianto>> AggiornaRiferimentoCatastoAsync(
        Guid impiantoId, Guid riferimentoId, DatiRiferimentoCatasto dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaImpianto>(HttpMethod.Put, $"api/impianti/{impiantoId}/riferimenti-catasto/{riferimentoId}", dati, ct);

    public Task<Risposta<object>> EliminaRiferimentoCatastoAsync(Guid impiantoId, Guid riferimentoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Delete, $"api/impianti/{impiantoId}/riferimenti-catasto/{riferimentoId}?versione={versione}", null, ct);

    // --- Interventi --------------------------------------------------------

    /// <summary>Con da e a è il calendario: gli interventi la cui finestra tocca l'intervallo.</summary>
    public Task<Risposta<Pagina<RiepilogoIntervento>>> CercaInterventiAsync(
        DateTimeOffset? da, DateTimeOffset? a, Guid? impiantoId, string? stato, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/interventi?pagina={pagina}&dimensione={dimensione}";
        if (da is { } inizio)
        {
            query += "&da=" + Uri.EscapeDataString(inizio.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (a is { } fine)
        {
            query += "&a=" + Uri.EscapeDataString(fine.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (impiantoId is { } impianto)
        {
            query += "&impianto=" + impianto;
        }

        if (!string.IsNullOrWhiteSpace(stato))
        {
            query += "&stato=" + Uri.EscapeDataString(stato);
        }

        return AutenticataAsync<Pagina<RiepilogoIntervento>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<Intervento>> InterventoAsync(Guid interventoId, CancellationToken ct = default) =>
        AutenticataAsync<Intervento>(HttpMethod.Get, $"api/interventi/{interventoId}", null, ct);

    /// <summary>PDF del rapportino di un intervento completato.</summary>
    public Task<Risposta<byte[]>> RapportinoAsync(Guid interventoId, CancellationToken ct = default) =>
        AutenticataAsync<byte[]>(HttpMethod.Get, $"api/interventi/{interventoId}/rapportino", null, ct);

    public Task<Risposta<IReadOnlyList<TecnicoAssegnabile>>> TecniciAsync(CancellationToken ct = default) =>
        AutenticataAsync<IReadOnlyList<TecnicoAssegnabile>>(HttpMethod.Get, "api/interventi/tecnici", null, ct);

    public Task<Risposta<EsitoScritturaIntervento>> CreaInterventoAsync(DatiIntervento dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaIntervento>(HttpMethod.Post, "api/interventi", dati, ct);

    public Task<Risposta<EsitoScritturaIntervento>> AggiornaInterventoAsync(Guid interventoId, DatiIntervento dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaIntervento>(HttpMethod.Put, $"api/interventi/{interventoId}", dati, ct);

    public Task<Risposta<EsitoScritturaIntervento>> AvviaInterventoAsync(Guid interventoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaIntervento>(HttpMethod.Post, $"api/interventi/{interventoId}/avvio", new { versione }, ct);

    public Task<Risposta<EsitoScritturaIntervento>> ChiudiInterventoAsync(Guid interventoId, DatiChiusura dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaIntervento>(HttpMethod.Post, $"api/interventi/{interventoId}/chiusura", dati, ct);

    public Task<Risposta<EsitoScritturaIntervento>> AnnullaInterventoAsync(Guid interventoId, int versione, string motivo, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaIntervento>(HttpMethod.Post, $"api/interventi/{interventoId}/annullamento", new { versione, motivo }, ct);

    // --- Scadenze ----------------------------------------------------------

    /// <summary>Scadute e in arrivo entro i giorni indicati; tipo e stato del richiamo sono filtri facoltativi.</summary>
    public Task<Risposta<Pagina<ScadenzaInArrivo>>> ScadenzeInArrivoAsync(
        int giorni, string? tipo, string? richiamo, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/scadenze?giorni={giorni}&pagina={pagina}&dimensione={dimensione}";
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            query += "&tipo=" + Uri.EscapeDataString(tipo);
        }

        if (!string.IsNullOrWhiteSpace(richiamo))
        {
            query += "&richiamo=" + Uri.EscapeDataString(richiamo);
        }

        return AutenticataAsync<Pagina<ScadenzaInArrivo>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<Scadenza>> ScadenzaAsync(Guid scadenzaId, CancellationToken ct = default) =>
        AutenticataAsync<Scadenza>(HttpMethod.Get, $"api/scadenze/{scadenzaId}", null, ct);

    /// <summary>Scadenze dell'impianto: prima le aperte, poi lo storico.</summary>
    public Task<Risposta<IReadOnlyList<Scadenza>>> ScadenzeImpiantoAsync(Guid impiantoId, CancellationToken ct = default) =>
        AutenticataAsync<IReadOnlyList<Scadenza>>(HttpMethod.Get, $"api/impianti/{impiantoId}/scadenze", null, ct);

    /// <summary>Data corretta a mano: da quel momento il server non la ricalcola più.</summary>
    public Task<Risposta<EsitoScrittura>> CorreggiDataScadenzaAsync(Guid scadenzaId, DateOnly data, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Put, $"api/scadenze/{scadenzaId}/data", new { data, versione }, ct);

    public Task<Risposta<EsitoScrittura>> RegistraRichiamoAsync(Guid scadenzaId, DatiRichiamo dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Post, $"api/scadenze/{scadenzaId}/richiami", dati, ct);

    // --- Listino ------------------------------------------------------------

    public Task<Risposta<Pagina<VoceListino>>> CercaListinoAsync(string? testo, bool ancheDisattivate, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/listino?pagina={pagina}&dimensione={dimensione}&tutte={(ancheDisattivate ? "true" : "false")}";
        if (!string.IsNullOrWhiteSpace(testo))
        {
            query += "&cerca=" + Uri.EscapeDataString(testo);
        }

        return AutenticataAsync<Pagina<VoceListino>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<VoceListino>> VoceListinoAsync(Guid voceId, CancellationToken ct = default) =>
        AutenticataAsync<VoceListino>(HttpMethod.Get, $"api/listino/{voceId}", null, ct);

    public Task<Risposta<EsitoScrittura>> CreaVoceAsync(DatiVoce dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Post, "api/listino", dati, ct);

    public Task<Risposta<EsitoScrittura>> AggiornaVoceAsync(Guid voceId, DatiVoce dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Put, $"api/listino/{voceId}", dati, ct);

    public Task<Risposta<EsitoScrittura>> AggiungiPrezzoAsync(Guid voceId, DatiPrezzo dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScrittura>(HttpMethod.Post, $"api/listino/{voceId}/prezzi", dati, ct);

    // --- Preventivi ---------------------------------------------------------

    /// <summary>Stato accetta anche "scaduto": gli emessi oltre la validità.</summary>
    // --- Rapporti di controllo, bollini e invii (M4) -----------------------

    public Task<Risposta<Pagina<RiepilogoRapporto>>> CercaRapportiAsync(
        string? stato, bool daTrasmettere, Guid? impiantoId, Guid? interventoId, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/rapporti-controllo?pagina={pagina}&dimensione={dimensione}";
        if (!string.IsNullOrWhiteSpace(stato))
        {
            query += "&stato=" + Uri.EscapeDataString(stato);
        }

        if (daTrasmettere)
        {
            query += "&daTrasmettere=true";
        }

        if (impiantoId is { } impianto)
        {
            query += $"&impianto={impianto}";
        }

        if (interventoId is { } intervento)
        {
            query += $"&intervento={intervento}";
        }

        return AutenticataAsync<Pagina<RiepilogoRapporto>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<RapportoControllo>> RapportoAsync(Guid rapportoId, CancellationToken ct = default) =>
        AutenticataAsync<RapportoControllo>(HttpMethod.Get, $"api/rapporti-controllo/{rapportoId}", null, ct);

    public Task<Risposta<IReadOnlyList<CampoTracciato>>> TracciatoAsync(string modello, CancellationToken ct = default) =>
        AutenticataAsync<IReadOnlyList<CampoTracciato>>(HttpMethod.Get, $"api/rapporti-controllo/tracciato/{Uri.EscapeDataString(modello)}", null, ct);

    public Task<Risposta<EsitoScritturaRapporto>> CreaRapportoAsync(DatiNuovoRapporto dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, "api/rapporti-controllo", dati, ct);

    public Task<Risposta<EsitoScritturaRapporto>> AggiornaRapportoAsync(Guid rapportoId, DatiBozzaRapporto dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Put, $"api/rapporti-controllo/{rapportoId}", dati, ct);

    public Task<Risposta<EsitoScritturaRapporto>> AssegnaBollinoAsync(Guid rapportoId, Guid tipoBollinoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, $"api/rapporti-controllo/{rapportoId}/bollino", new { tipoBollinoId, versione }, ct);

    public Task<Risposta<EsitoScritturaRapporto>> LiberaBollinoAsync(Guid rapportoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Delete, $"api/rapporti-controllo/{rapportoId}/bollino?versione={versione}", null, ct);

    /// <summary>Convalida, riapertura o firma: il passaggio è il nome dell'azione nell'API.</summary>
    public Task<Risposta<EsitoScritturaRapporto>> PassaggioRapportoAsync(Guid rapportoId, string passaggio, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, $"api/rapporti-controllo/{rapportoId}/{passaggio}", new { versione }, ct);

    public Task<Risposta<EsitoScritturaRapporto>> AnnullaRapportoAsync(Guid rapportoId, int versione, string motivo, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, $"api/rapporti-controllo/{rapportoId}/annullamento", new { versione, motivo }, ct);

    public Task<Risposta<IReadOnlyList<TipoBollino>>> TipiBollinoAsync(CancellationToken ct = default) =>
        AutenticataAsync<IReadOnlyList<TipoBollino>>(HttpMethod.Get, "api/bollini/tipi", null, ct);

    public Task<Risposta<Pagina<Bollino>>> CercaBolliniAsync(string? stato, string? codice, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/bollini?pagina={pagina}&dimensione={dimensione}";
        if (!string.IsNullOrWhiteSpace(stato))
        {
            query += "&stato=" + Uri.EscapeDataString(stato);
        }

        if (!string.IsNullOrWhiteSpace(codice))
        {
            query += "&codice=" + Uri.EscapeDataString(codice);
        }

        return AutenticataAsync<Pagina<Bollino>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<EsitoBolliniRegistrati>> RegistraBolliniAsync(DatiAcquistoBollini dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoBolliniRegistrati>(HttpMethod.Post, "api/bollini", dati, ct);

    public Task<Risposta<EsitoScritturaRapporto>> AnnullaBollinoAsync(Guid bollinoId, int versione, string motivo, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, $"api/bollini/{bollinoId}/annullamento", new { versione, motivo }, ct);

    public Task<Risposta<Pagina<RiepilogoInvio>>> CercaInviiAsync(int pagina, int dimensione, CancellationToken ct = default) =>
        AutenticataAsync<Pagina<RiepilogoInvio>>(HttpMethod.Get, $"api/invii-catasto?pagina={pagina}&dimensione={dimensione}", null, ct);

    public Task<Risposta<InvioCatasto>> InvioAsync(Guid invioId, CancellationToken ct = default) =>
        AutenticataAsync<InvioCatasto>(HttpMethod.Get, $"api/invii-catasto/{invioId}", null, ct);

    /// <summary>Il file di import così come è stato preparato: non cambia dopo la creazione.</summary>
    public Task<Risposta<byte[]>> FileInvioAsync(Guid invioId, CancellationToken ct = default) =>
        AutenticataAsync<byte[]>(HttpMethod.Get, $"api/invii-catasto/{invioId}/file", null, ct);

    public Task<Risposta<EsitoInvioPreparato>> PreparaInvioAsync(string modello, CancellationToken ct = default) =>
        AutenticataAsync<EsitoInvioPreparato>(HttpMethod.Post, "api/invii-catasto", new { modello }, ct);

    public Task<Risposta<EsitoScritturaRapporto>> SegnaInvioCaricatoAsync(Guid invioId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, $"api/invii-catasto/{invioId}/caricamento", new { versione }, ct);

    public Task<Risposta<EsitoScritturaRapporto>> AnnullaInvioAsync(Guid invioId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, $"api/invii-catasto/{invioId}/annullamento", new { versione }, ct);

    public Task<Risposta<EsitoScritturaRapporto>> RegistraEsitiInvioAsync(
        Guid invioId, int versione, IReadOnlyList<EsitoRigaInvio> esiti, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaRapporto>(HttpMethod.Post, $"api/invii-catasto/{invioId}/esiti", new { versione, esiti }, ct);

    public Task<Risposta<Pagina<RiepilogoPreventivo>>> CercaPreventiviAsync(
        string? testo, string? stato, Guid? clienteId, int pagina, int dimensione, CancellationToken ct = default)
    {
        var query = $"api/preventivi?pagina={pagina}&dimensione={dimensione}";
        if (!string.IsNullOrWhiteSpace(testo))
        {
            query += "&cerca=" + Uri.EscapeDataString(testo);
        }

        if (!string.IsNullOrWhiteSpace(stato))
        {
            query += "&stato=" + Uri.EscapeDataString(stato);
        }

        if (clienteId is { } cliente)
        {
            query += "&cliente=" + cliente;
        }

        return AutenticataAsync<Pagina<RiepilogoPreventivo>>(HttpMethod.Get, query, null, ct);
    }

    public Task<Risposta<Preventivo>> PreventivoAsync(Guid preventivoId, CancellationToken ct = default) =>
        AutenticataAsync<Preventivo>(HttpMethod.Get, $"api/preventivi/{preventivoId}", null, ct);

    public Task<Risposta<byte[]>> PdfPreventivoAsync(Guid preventivoId, CancellationToken ct = default) =>
        AutenticataAsync<byte[]>(HttpMethod.Get, $"api/preventivi/{preventivoId}/pdf", null, ct);

    public Task<Risposta<EsitoScritturaCommerciale>> CreaPreventivoAsync(DatiPreventivo dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaCommerciale>(HttpMethod.Post, "api/preventivi", dati, ct);

    public Task<Risposta<EsitoScritturaCommerciale>> AggiornaPreventivoAsync(Guid preventivoId, DatiPreventivo dati, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaCommerciale>(HttpMethod.Put, $"api/preventivi/{preventivoId}", dati, ct);

    public Task<Risposta<object>> EliminaPreventivoAsync(Guid preventivoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Delete, $"api/preventivi/{preventivoId}?versione={versione}", null, ct);

    /// <summary>Passaggio di stato senza altri dati: "emissione", "accettazione" o "rifiuto".</summary>
    public Task<Risposta<EsitoScritturaCommerciale>> CambiaStatoPreventivoAsync(Guid preventivoId, string azione, int versione, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaCommerciale>(HttpMethod.Post, $"api/preventivi/{preventivoId}/{azione}", new { versione }, ct);

    public Task<Risposta<EsitoScritturaCommerciale>> AnnullaPreventivoAsync(Guid preventivoId, int versione, string motivo, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaCommerciale>(HttpMethod.Post, $"api/preventivi/{preventivoId}/annullamento", new { versione, motivo }, ct);

    public Task<Risposta<EsitoScritturaCommerciale>> GeneraInterventoAsync(Guid preventivoId, int versione, string tipo, CancellationToken ct = default) =>
        AutenticataAsync<EsitoScritturaCommerciale>(HttpMethod.Post, $"api/preventivi/{preventivoId}/intervento", new { versione, tipo }, ct);

    // --- Documenti degli impianti -----------------------------------------

    public Task<Risposta<IReadOnlyList<Documento>>> DocumentiAsync(Guid impiantoId, CancellationToken ct = default) =>
        AutenticataAsync<IReadOnlyList<Documento>>(HttpMethod.Get, $"api/impianti/{impiantoId}/documenti", null, ct);

    public Task<Risposta<byte[]>> ContenutoDocumentoAsync(Guid impiantoId, Guid documentoId, CancellationToken ct = default) =>
        AutenticataAsync<byte[]>(HttpMethod.Get, $"api/impianti/{impiantoId}/documenti/{documentoId}/contenuto", null, ct);

    public Task<Risposta<Documento>> CaricaDocumentoAsync(Guid impiantoId, ModuloFile file, CancellationToken ct = default) =>
        AutenticataAsync<Documento>(HttpMethod.Post, $"api/impianti/{impiantoId}/documenti", file, ct);

    public Task<Risposta<object>> EliminaDocumentoAsync(Guid impiantoId, Guid documentoId, int versione, CancellationToken ct = default) =>
        AutenticataAsync<object>(HttpMethod.Delete, $"api/impianti/{impiantoId}/documenti/{documentoId}?versione={versione}", null, ct);

    public void Dispose()
    {
        _http?.Dispose();
        _rinnovo.Dispose();
    }

    // --- Dettagli ----------------------------------------------------------

    private static string Dispositivo => $"{Environment.MachineName} ({Environment.OSVersion.Platform})";

    private string Account => Server?.Authority ?? "";

    /// <summary>Chiamata con il token di accesso: se scade lo rinnova, se il server lo rifiuta riprova una volta.</summary>
    private async Task<Risposta<T>> AutenticataAsync<T>(HttpMethod metodo, string percorso, object? corpo, CancellationToken ct)
    {
        if (_sessione is null)
        {
            return new Risposta<T>(HttpStatusCode.Unauthorized, default, Testi.SessioneScaduta);
        }

        if (_sessione.AccessoScadeIl - _orologio.GetUtcNow() < s_anticipoRinnovo)
        {
            await RinnovaAsync(_sessione.TokenRinnovo, ct);
        }

        if (_sessione is null)
        {
            return new Risposta<T>(HttpStatusCode.Unauthorized, default, Testi.SessioneScaduta);
        }

        var risposta = await InviaAsync<T>(metodo, percorso, corpo, _sessione.TokenAccesso, ct);
        if (risposta.Stato != HttpStatusCode.Unauthorized)
        {
            return risposta;
        }

        return _sessione is { } attuale && await RinnovaAsync(attuale.TokenRinnovo, ct)
            ? await InviaAsync<T>(metodo, percorso, corpo, _sessione!.TokenAccesso, ct)
            : risposta;
    }

    private async Task<bool> RinnovaAsync(string tokenRinnovo, CancellationToken ct)
    {
        await _rinnovo.WaitAsync(ct);
        try
        {
            // Un'altra chiamata potrebbe aver già rinnovato mentre questa aspettava.
            if (_sessione is { } attuale && attuale.TokenRinnovo != tokenRinnovo)
            {
                return true;
            }

            var risposta = await InviaAsync<TokenSessione>(HttpMethod.Post, "api/sessione/rinnovo", new { tokenRinnovo, dispositivo = Dispositivo }, null, ct);
            if (risposta.Valore is { } token)
            {
                AvviaSessione(token);
                return true;
            }

            if (risposta.Stato == HttpStatusCode.Unauthorized)
            {
                TerminaSessione();
            }

            return false;
        }
        finally
        {
            _rinnovo.Release();
        }
    }

    private void AvviaSessione(TokenSessione token)
    {
        _sessione = token;
        _portachiavi.Salva(Account, token.TokenRinnovo);
    }

    private void TerminaSessione()
    {
        var eraAttiva = _sessione is not null;
        _sessione = null;
        _portachiavi.Elimina(Account);
        if (eraAttiva)
        {
            SessioneTerminata?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<Risposta<T>> InviaAsync<T>(HttpMethod metodo, string percorso, object? corpo, string? token, CancellationToken ct)
    {
        if (_http is null)
        {
            return new Risposta<T>(0, default, Testi.ServerNonConfigurato);
        }

        using var richiesta = new HttpRequestMessage(metodo, percorso);
        if (corpo is ModuloFile file)
        {
            // Il tipo lo riconosce il server dal contenuto: qui basta un tipo generico.
            var parte = new ByteArrayContent(file.Dati);
            parte.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var modulo = new MultipartFormDataContent { { parte, "file", file.Nome } };
            if (file.Descrizione is { } descrizione)
            {
                modulo.Add(new StringContent(descrizione), "descrizione");
            }

            richiesta.Content = modulo;
        }
        else if (corpo is not null)
        {
            richiesta.Content = JsonContent.Create(corpo, options: s_json);
        }

        if (token is not null)
        {
            richiesta.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage risposta;
        try
        {
            risposta = await _http.SendAsync(richiesta, ct);
        }
        catch (HttpRequestException)
        {
            return new Risposta<T>(0, default, Testi.ServerNonRaggiungibile);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Risposta<T>(0, default, Testi.ServerNonRaggiungibile);
        }

        using (risposta)
        {
            if (risposta.IsSuccessStatusCode && typeof(T) == typeof(byte[]))
            {
                // File (per esempio un PDF): i byte così come arrivano.
                return new Risposta<T>(risposta.StatusCode, (T)(object)await risposta.Content.ReadAsByteArrayAsync(ct), null);
            }

            if (risposta.IsSuccessStatusCode)
            {
                var valore = risposta.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object)
                    ? default
                    : await risposta.Content.ReadFromJsonAsync<T>(s_json, ct);
                return new Risposta<T>(risposta.StatusCode, valore, null);
            }

            return new Risposta<T>(risposta.StatusCode, default, await MessaggioAsync(risposta, ct));
        }
    }

    private static async Task<string> MessaggioAsync(HttpResponseMessage risposta, CancellationToken ct)
    {
        try
        {
            var problema = await risposta.Content.ReadFromJsonAsync<ProblemaApi>(s_json, ct);
            if (!string.IsNullOrWhiteSpace(problema?.Detail))
            {
                return problema.Detail;
            }
        }
        catch (JsonException)
        {
        }

        return risposta.StatusCode switch
        {
            HttpStatusCode.Unauthorized => Testi.SessioneScaduta,
            HttpStatusCode.Forbidden => Testi.OperazioneNonConsentita,
            HttpStatusCode.TooManyRequests => Testi.TroppeRichieste,
            _ => Testi.ErroreInatteso,
        };
    }
}
