using System.Net;
using System.Net.Http.Json;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Tests.Unit;

/// <summary>Server finto: risponde con la funzione indicata e registra le richieste.</summary>
internal sealed class ServerFinto(Func<HttpRequestMessage, HttpResponseMessage> rispondi) : HttpMessageHandler
{
    public List<(HttpMethod Metodo, string Percorso, string? Token)> Richieste { get; } = [];

    public static HttpResponseMessage Json(object corpo, HttpStatusCode stato = HttpStatusCode.OK) =>
        new(stato) { Content = JsonContent.Create(corpo) };

    public static HttpResponseMessage Problema(HttpStatusCode stato, string dettaglio) =>
        new(stato) { Content = JsonContent.Create(new { title = "Errore", detail = dettaglio, status = (int)stato }) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Richieste.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter));
        return Task.FromResult(rispondi(request));
    }
}

internal sealed class OrologioMobile(DateTimeOffset adesso) : TimeProvider
{
    public DateTimeOffset Adesso { get; set; } = adesso;

    public override DateTimeOffset GetUtcNow() => Adesso;
}

public sealed class TestClientApi
{
    private static readonly DateTimeOffset s_adesso = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Uri s_server = new("http://gestionale.locale:5225");

    private static object Token(string accesso, string rinnovo, DateTimeOffset scadeIl) =>
        new { tokenAccesso = accesso, accessoScadeIl = scadeIl, tokenRinnovo = rinnovo, rinnovoScadeIl = scadeIl.AddHours(12) };

    private static (ClientApi Api, PortachiaviInMemoria Portachiavi, OrologioMobile Orologio) Crea(ServerFinto server)
    {
        var portachiavi = new PortachiaviInMemoria();
        var orologio = new OrologioMobile(s_adesso);
        var api = new ClientApi(portachiavi, orologio, indirizzo => new HttpClient(server) { BaseAddress = indirizzo });
        api.ImpostaServer(s_server);
        return (api, portachiavi, orologio);
    }

    [Fact]
    public async Task Dopo_l_accesso_il_token_di_rinnovo_sta_solo_nel_portachiavi()
    {
        var server = new ServerFinto(_ => ServerFinto.Json(new { esito = "completato", token = Token("accesso-1", "rinnovo-1", s_adesso.AddMinutes(15)) }));
        var (api, portachiavi, _) = Crea(server);

        var risposta = await api.AccediAsync("mario@alfa.example", "una-password-lunga", TestContext.Current.CancellationToken);

        Assert.True(risposta.Riuscita);
        Assert.True(api.Autenticato);
        Assert.Equal("rinnovo-1", portachiavi.Leggi("gestionale.locale:5225"));
    }

    [Fact]
    public async Task Il_token_di_accesso_si_rinnova_prima_che_scada()
    {
        var server = new ServerFinto(r => r.RequestUri!.AbsolutePath switch
        {
            "/api/accesso" => ServerFinto.Json(new { esito = "completato", token = Token("accesso-1", "rinnovo-1", s_adesso.AddMinutes(15)) }),
            "/api/sessione/rinnovo" => ServerFinto.Json(Token("accesso-2", "rinnovo-2", s_adesso.AddMinutes(30))),
            _ => ServerFinto.Json(new { utenteId = Guid.NewGuid(), aziendaId = Guid.NewGuid(), email = "m@a.example", nome = "Mario", cognome = "Rossi", ruoli = Array.Empty<string>(), permessi = Array.Empty<object>() }),
        });
        var (api, portachiavi, orologio) = Crea(server);
        await api.AccediAsync("mario@alfa.example", "una-password-lunga", TestContext.Current.CancellationToken);

        orologio.Adesso = s_adesso.AddMinutes(14).AddSeconds(30);
        var profilo = await api.ProfiloAsync(TestContext.Current.CancellationToken);

        Assert.True(profilo.Riuscita);
        Assert.Equal(["/api/accesso", "/api/sessione/rinnovo", "/api/io"], server.Richieste.Select(r => r.Percorso));
        Assert.Equal("accesso-2", server.Richieste[^1].Token);
        Assert.Equal("rinnovo-2", portachiavi.Leggi("gestionale.locale:5225"));
    }

    [Fact]
    public async Task Se_il_rinnovo_viene_rifiutato_la_sessione_si_chiude_e_il_portachiavi_si_svuota()
    {
        var server = new ServerFinto(r => r.RequestUri!.AbsolutePath switch
        {
            "/api/accesso" => ServerFinto.Json(new { esito = "completato", token = Token("accesso-1", "rinnovo-1", s_adesso.AddMinutes(15)) }),
            "/api/sessione/rinnovo" => ServerFinto.Problema(HttpStatusCode.Unauthorized, "La sessione è scaduta o è stata chiusa. Accedi di nuovo."),
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
        });
        var (api, portachiavi, _) = Crea(server);
        var terminata = false;
        api.SessioneTerminata += (_, _) => terminata = true;
        await api.AccediAsync("mario@alfa.example", "una-password-lunga", TestContext.Current.CancellationToken);

        var profilo = await api.ProfiloAsync(TestContext.Current.CancellationToken);

        Assert.False(profilo.Riuscita);
        Assert.True(terminata);
        Assert.False(api.Autenticato);
        Assert.Null(portachiavi.Leggi("gestionale.locale:5225"));
    }

    [Fact]
    public async Task Il_messaggio_d_errore_del_server_arriva_all_utente()
    {
        var server = new ServerFinto(_ => ServerFinto.Problema(HttpStatusCode.Unauthorized, "Email o password non corretti."));
        var (api, _, _) = Crea(server);

        var risposta = await api.AccediAsync("mario@alfa.example", "sbagliata-ma-lunga", TestContext.Current.CancellationToken);

        Assert.False(risposta.Riuscita);
        Assert.Equal("Email o password non corretti.", risposta.Messaggio);
    }

    [Fact]
    public async Task Un_server_spento_produce_un_messaggio_comprensibile()
    {
        var server = new ServerFinto(_ => throw new HttpRequestException("connessione rifiutata"));
        var (api, _, _) = Crea(server);

        var risposta = await api.AccediAsync("mario@alfa.example", "una-password-lunga", TestContext.Current.CancellationToken);

        Assert.Equal(Testi.ServerNonRaggiungibile, risposta.Messaggio);
    }
}

public sealed class TestAccessoViewModel
{
    private sealed class NavigazioneFinta : INavigazione
    {
        public List<string> Passi { get; } = [];

        public void VaiAdAccesso(string? messaggio = null) => Passi.Add("accesso");

        public void VaiAVerificaMfa(string tokenPassaggio, string email) => Passi.Add($"verifica:{tokenPassaggio}:{email}");

        public void VaiAConfiguraMfa(string tokenPassaggio, string email) => Passi.Add($"configura:{tokenPassaggio}");

        public void VaiACodiciRecupero(IReadOnlyList<string> codici) => Passi.Add("codici");

        public Task VaiAllApplicazioneAsync()
        {
            Passi.Add("app");
            return Task.CompletedTask;
        }
    }

    private static (AccessoViewModel Vm, NavigazioneFinta Navigazione, ArchivioImpostazioniInMemoria Archivio) Crea(ServerFinto server)
    {
        var api = new ClientApi(new PortachiaviInMemoria(), TimeProvider.System, indirizzo => new HttpClient(server) { BaseAddress = indirizzo });
        var navigazione = new NavigazioneFinta();
        var archivio = new ArchivioImpostazioniInMemoria();
        return (new AccessoViewModel(api, archivio, navigazione), navigazione, archivio);
    }

    [Fact]
    public async Task Senza_email_e_password_non_parte_nessuna_richiesta()
    {
        var server = new ServerFinto(_ => throw new InvalidOperationException("Non doveva chiamare il server."));
        var (vm, navigazione, _) = Crea(server);

        await vm.AccediCommand.ExecuteAsync(null);

        Assert.Equal(Testi.CompilaEmailPassword, vm.Errore);
        Assert.Empty(server.Richieste);
        Assert.Empty(navigazione.Passi);
    }

    [Fact]
    public async Task Un_indirizzo_del_server_sbagliato_apre_il_campo_per_correggerlo()
    {
        var (vm, _, _) = Crea(new ServerFinto(_ => throw new InvalidOperationException()));
        vm.Email = "mario@alfa.example";
        vm.Password = "una-password-lunga";
        vm.IndirizzoServer = "gestionale senza protocollo";

        await vm.AccediCommand.ExecuteAsync(null);

        Assert.Equal(Testi.IndirizzoServerNonValido, vm.Errore);
        Assert.True(vm.ModificaServer);
    }

    [Fact]
    public async Task Con_il_secondo_fattore_attivo_si_passa_alla_verifica_e_si_ricorda_l_email()
    {
        var (vm, navigazione, archivio) = Crea(new ServerFinto(_ => ServerFinto.Json(new { esito = "mfa_richiesto", tokenPassaggio = "passaggio-1" })));
        vm.Email = " mario@alfa.example ";
        vm.Password = "una-password-lunga";

        await vm.AccediCommand.ExecuteAsync(null);

        Assert.Equal(["verifica:passaggio-1:mario@alfa.example"], navigazione.Passi);
        Assert.Equal("", vm.Password);
        Assert.Equal("mario@alfa.example", archivio.Carica().UltimaEmail);
    }

    [Fact]
    public async Task Il_ruolo_che_richiede_il_secondo_fattore_porta_alla_configurazione()
    {
        var (vm, navigazione, _) = Crea(new ServerFinto(_ => ServerFinto.Json(new { esito = "mfa_da_configurare", tokenPassaggio = "passaggio-2" })));
        vm.Email = "mario@alfa.example";
        vm.Password = "una-password-lunga";

        await vm.AccediCommand.ExecuteAsync(null);

        Assert.Equal(["configura:passaggio-2"], navigazione.Passi);
    }
}

public sealed class TestPortachiavi
{
    [Fact]
    public void Il_portachiavi_in_memoria_salva_legge_ed_elimina()
    {
        var portachiavi = new PortachiaviInMemoria();

        portachiavi.Salva("server", "segreto");
        Assert.Equal("segreto", portachiavi.Leggi("server"));

        portachiavi.Elimina("server");
        Assert.Null(portachiavi.Leggi("server"));
    }

    /// <summary>Usa il portachiavi vero del sistema (Credential Manager o Keychain), con un account di prova.</summary>
    [Fact]
    public void Il_portachiavi_del_sistema_salva_sostituisce_ed_elimina()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), "Portachiavi di sistema disponibile solo su Windows e macOS.");
        var portachiavi = Portachiavi.DelSistema();
        var account = $"test-{Guid.NewGuid():N}";

        try
        {
            Assert.Null(portachiavi.Leggi(account));

            portachiavi.Salva(account, "primo-segreto");
            portachiavi.Salva(account, "secondo-segreto-àèé");
            Assert.Equal("secondo-segreto-àèé", portachiavi.Leggi(account));
        }
        finally
        {
            portachiavi.Elimina(account);
        }

        Assert.Null(portachiavi.Leggi(account));
        portachiavi.Elimina(account);
    }
}
