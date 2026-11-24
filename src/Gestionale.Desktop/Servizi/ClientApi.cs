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
        if (corpo is not null)
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
