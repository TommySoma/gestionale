using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record RiepilogoTipoBollino(string Nome, string Importo, string Disponibili, bool Esaurito);

public sealed record RigaBollino(Bollino Dati, string Codice, string Tipo, string Acquistato, string Stato, bool Annullabile);

/// <summary>Registro dei bollini acquistati sul portale SIERT: disponibili per tipo, stato di ognuno.</summary>
public partial class BolliniViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 50;

    public ObservableCollection<RiepilogoTipoBollino> Tipi { get; } = [];

    public ObservableCollection<RigaBollino> Righe { get; } = [];

    public bool GestisceBollini => shell.GestisceBollini;

    [ObservableProperty]
    public partial string? Stato { get; set; }

    [ObservableProperty]
    public partial string Codice { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervallo), nameof(PuoPrecedente), nameof(PuoSuccessiva))]
    public partial int NumeroPagina { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervallo), nameof(PuoSuccessiva), nameof(Vuoto))]
    public partial int Totale { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; }

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    public bool Vuoto => Totale == 0 && !HaErrore;

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public bool FiltroTutti { get => Stato is null; set { if (value) { Filtra(null); } } }

    public bool FiltroDisponibili { get => Stato == "disponibile"; set { if (value) { Filtra("disponibile"); } } }

    public bool FiltroAssegnati { get => Stato == "assegnato"; set { if (value) { Filtra("assegnato"); } } }

    public bool FiltroUsati { get => Stato == "usato"; set { if (value) { Filtra("usato"); } } }

    public bool FiltroAnnullati { get => Stato == "annullato"; set { if (value) { Filtra("annullato"); } } }

    public async Task CaricaAsync()
    {
        Errore = null;
        var tipi = await shell.Api.TipiBollinoAsync();
        if (tipi.Valore is { } elenco)
        {
            Tipi.Clear();
            foreach (var t in elenco)
            {
                Tipi.Add(new RiepilogoTipoBollino(
                    t.Nome, FormatoInterventi.Importo(t.Importo),
                    t.Disponibili == 1 ? Testi.UnBollinoDisponibile : string.Format(CultureInfo.CurrentCulture, Testi.BolliniDisponibili, t.Disponibili),
                    t.Disponibili == 0));
            }
        }

        var risposta = await shell.Api.CercaBolliniAsync(Stato, Codice, NumeroPagina, DimensionePagina);
        if (risposta.Valore is not { } pagina)
        {
            Errore = risposta.Messaggio ?? tipi.Messaggio;
            return;
        }

        Righe.Clear();
        foreach (var b in pagina.Elementi)
        {
            Righe.Add(new RigaBollino(
                b, b.Codice, $"{b.TipoBollino} · {FormatoInterventi.Importo(b.Importo)}", FormatoImpianti.Data(b.AcquistatoIl),
                FormatoRapporti.StatoBollino(b.Stato), GestisceBollini && b.Stato == "disponibile"));
        }

        Totale = pagina.Totale;
        OnPropertyChanged(nameof(Vuoto));
    }

    [RelayCommand]
    private async Task CercaAsync()
    {
        NumeroPagina = 1;
        await CaricaAsync();
    }

    [RelayCommand]
    private async Task RegistraAsync()
    {
        var tipi = await shell.Api.TipiBollinoAsync();
        if (tipi.Valore is not { } elenco)
        {
            Errore = tipi.Messaggio;
            return;
        }

        shell.MostraDialogo(new RegistraBolliniViewModel(shell, elenco, numero =>
        {
            Avviso = numero == 1 ? Testi.UnBollinoRegistrato : string.Format(CultureInfo.CurrentCulture, Testi.BolliniRegistrati, numero);
            _ = CaricaAsync();
        }));
    }

    [RelayCommand]
    private void Annulla(RigaBollino riga) =>
        shell.MostraDialogo(new MotivoViewModel(
            shell, string.Format(CultureInfo.CurrentCulture, Testi.TitoloAnnullaBollino, riga.Codice), Testi.SpiegazioneAnnullaBollino, Testi.AnnullaBollino,
            async motivo =>
            {
                var risposta = await shell.Api.AnnullaBollinoAsync(riga.Dati.BollinoId, riga.Dati.Versione, motivo);
                return risposta.Riuscita ? null : risposta.Messaggio;
            },
            () => _ = CaricaAsync()));

    [RelayCommand]
    private async Task PrecedenteAsync()
    {
        NumeroPagina--;
        await CaricaAsync();
    }

    [RelayCommand]
    private async Task SuccessivaAsync()
    {
        NumeroPagina++;
        await CaricaAsync();
    }

    private void Filtra(string? stato)
    {
        if (Stato == stato)
        {
            return;
        }

        Stato = stato;
        NumeroPagina = 1;
        foreach (var nome in new[] { nameof(FiltroTutti), nameof(FiltroDisponibili), nameof(FiltroAssegnati), nameof(FiltroUsati), nameof(FiltroAnnullati) })
        {
            OnPropertyChanged(nome);
        }

        _ = CaricaAsync();
    }
}

/// <summary>Bollini acquistati sul portale: un tipo, una data, i codici di 13 cifre uno per riga.</summary>
public partial class RegistraBolliniViewModel : DialogoViewModel
{
    private readonly Action<int> _registrati;
    private int _numero;

    public RegistraBolliniViewModel(ShellViewModel shell, IReadOnlyList<TipoBollino> tipi, Action<int> registrati) : base(shell)
    {
        ArgumentNullException.ThrowIfNull(tipi);

        _registrati = registrati;
        Tipi = [.. tipi.Select(t => new OpzioneTipoBollino(t.TipoBollinoId, $"{t.Nome} · {FormatoInterventi.Importo(t.Importo)}", t.Disponibili))];
        Tipo = Tipi.FirstOrDefault();
        AcquistatoIl = shell.Orologio.GetLocalNow().Date;
    }

    public IReadOnlyList<OpzioneTipoBollino> Tipi { get; }

    [ObservableProperty]
    public partial OpzioneTipoBollino? Tipo { get; set; }

    [ObservableProperty]
    public partial DateTime? AcquistatoIl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConteggioCodici))]
    public partial string Codici { get; set; } = "";

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    /// <summary>I codici scritti, separati da a capo, spazi, virgole o punti e virgola.</summary>
    public IReadOnlyList<string> CodiciLetti =>
        [.. Codici.Split(['\r', '\n', ' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];

    public string ConteggioCodici => CodiciLetti.Count == 1
        ? Testi.UnCodiceLetto
        : string.Format(CultureInfo.CurrentCulture, Testi.CodiciLetti, CodiciLetti.Count);

    protected override async Task<string?> EseguiAsync()
    {
        if (Tipo is not { } tipo)
        {
            return Testi.ScegliTipoBollino;
        }

        if (AcquistatoIl is not { } data)
        {
            return Testi.DataAcquistoMancante;
        }

        var codici = CodiciLetti;
        if (codici.Count == 0)
        {
            return Testi.CodiciBolliniMancanti;
        }

        var risposta = await Shell.Api.RegistraBolliniAsync(new DatiAcquistoBollini(tipo.Id, DateOnly.FromDateTime(data), codici, Vuoto(Note)));
        _numero = risposta.Valore?.Numero ?? 0;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _registrati(_numero);
}

public sealed record RigaElencoInvio(Guid Id, string Creato, string Modello, string NomeFile, string Righe, string Stato);

/// <summary>Invii al SIERT: un file di import per modello, preparato con i rapporti firmati o scartati.</summary>
public partial class InviiViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 50;

    public ObservableCollection<RigaElencoInvio> Righe { get; } = [];

    public bool GestisceBollini => shell.GestisceBollini;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervallo), nameof(PuoPrecedente), nameof(PuoSuccessiva))]
    public partial int NumeroPagina { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervallo), nameof(PuoSuccessiva), nameof(Vuoto))]
    public partial int Totale { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool Vuoto => Totale == 0 && !HaErrore;

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public async Task CaricaAsync()
    {
        Errore = null;
        var risposta = await shell.Api.CercaInviiAsync(NumeroPagina, DimensionePagina);
        if (risposta.Valore is not { } pagina)
        {
            Errore = risposta.Messaggio;
            return;
        }

        Righe.Clear();
        foreach (var i in pagina.Elementi)
        {
            Righe.Add(new RigaElencoInvio(
                i.InvioCatastoId, FormatoInterventi.DataOra(i.CreatoIl), FormatoRapporti.ModelloBreve(i.Modello), i.NomeFile,
                i.Righe.ToString(CultureInfo.CurrentCulture), FormatoRapporti.StatoInvio(i.Stato)));
        }

        Totale = pagina.Totale;
        OnPropertyChanged(nameof(Vuoto));
    }

    [RelayCommand]
    private Task PreparaTipo1Async() => PreparaAsync("tipo_1");

    [RelayCommand]
    private Task PreparaTipo2Async() => PreparaAsync("tipo_2");

    [RelayCommand]
    private Task ApriAsync(RigaElencoInvio riga) => shell.ApriInvioAsync(riga.Id, null);

    [RelayCommand]
    private async Task PrecedenteAsync()
    {
        NumeroPagina--;
        await CaricaAsync();
    }

    [RelayCommand]
    private async Task SuccessivaAsync()
    {
        NumeroPagina++;
        await CaricaAsync();
    }

    // Il server prende i rapporti firmati o scartati del modello, non già in un invio vivo.
    private async Task PreparaAsync(string modello)
    {
        Errore = null;
        var risposta = await shell.Api.PreparaInvioAsync(modello);
        if (risposta.Valore is not { } preparato)
        {
            Errore = risposta.Messaggio;
            return;
        }

        var avviso = string.Format(CultureInfo.CurrentCulture, Testi.InvioPreparato, preparato.Righe)
            + (preparato.Esclusi.Count > 0 ? " " + string.Format(CultureInfo.CurrentCulture, Testi.RapportiEsclusi, preparato.Esclusi.Count) : "");
        await shell.ApriInvioAsync(preparato.Id, avviso);
    }
}

public sealed record RigaRapportoInvio(int Numero, Guid RapportoId, string Cliente, string Data, string Esito, string? Causa);

/// <summary>
/// Scheda dell'invio: si scarica il file, lo si carica sul portale SIERT (a mano: non
/// c'è un'API pubblica nota), si segna come caricato e poi si registrano gli esiti del report.
/// </summary>
public partial class SchedaInvioViewModel(ShellViewModel shell, Guid invioId, string? avvisoIniziale) : ViewModelBase
{
    private InvioCatasto? _invio;

    public ObservableCollection<RigaRapportoInvio> Righe { get; } = [];

    public ObservableCollection<CampoScheda> Dati { get; } = [];

    [ObservableProperty]
    public partial string Titolo { get; set; } = "";

    [ObservableProperty]
    public partial string Stato { get; set; } = "";

    [ObservableProperty]
    public partial string NomeFile { get; set; } = "";

    [ObservableProperty]
    public partial bool PuoSegnareCaricato { get; set; }

    [ObservableProperty]
    public partial bool PuoAnnullare { get; set; }

    [ObservableProperty]
    public partial bool PuoRegistrareEsiti { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; } = avvisoIniziale;

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    public async Task CaricaAsync()
    {
        var risposta = await shell.Api.InvioAsync(invioId);
        if (risposta.Valore is not { } invio)
        {
            Errore = risposta.Messaggio;
            return;
        }

        _invio = invio;
        var i = invio.Invio;
        Titolo = string.Format(CultureInfo.CurrentCulture, Testi.TitoloInvio, FormatoRapporti.Modello(i.Modello));
        Stato = FormatoRapporti.StatoInvio(i.Stato);
        NomeFile = i.NomeFile;

        Dati.Clear();
        Dati.Add(new(Testi.PreparatoIl, FormatoInterventi.DataOra(i.CreatoIl)));
        Dati.Add(new(Testi.VersioneTracciato, i.VersioneTracciato));
        if (i.CaricatoIl is { } caricato)
        {
            Dati.Add(new(Testi.CaricatoIl, FormatoInterventi.DataOra(caricato)));
        }

        if (i.ElaboratoIl is { } elaborato)
        {
            Dati.Add(new(Testi.ElaboratoIl, FormatoInterventi.DataOra(elaborato)));
        }

        Righe.Clear();
        foreach (var r in invio.Rapporti.OrderBy(r => r.Riga))
        {
            Righe.Add(new RigaRapportoInvio(
                r.Riga, r.RapportoControlloId, r.Cliente ?? Testi.NonIndicato, FormatoImpianti.Data(r.DataControllo), FormatoRapporti.EsitoRiga(r.Esito), r.Causa));
        }

        var gestisce = shell.GestisceBollini;
        PuoSegnareCaricato = gestisce && i.Stato == "preparato";
        PuoAnnullare = gestisce && i.Stato == "preparato";
        PuoRegistrareEsiti = gestisce && i.Stato == "caricato";
    }

    /// <summary>Scarica il file; in caso di errore mostra il messaggio e restituisce null. Il salvataggio su disco lo fa la vista.</summary>
    public async Task<byte[]?> FileAsync()
    {
        var risposta = await shell.Api.FileInvioAsync(invioId);
        Errore = risposta.Valore is null ? risposta.Messaggio : null;
        return risposta.Valore;
    }

    public void SegnalaFileSalvato(string percorso) => Avviso = string.Format(CultureInfo.CurrentCulture, Testi.FileInvioSalvato, percorso);

    [RelayCommand]
    private Task IndietroAsync() => shell.ApriInviiAsync();

    [RelayCommand]
    private Task ApriRapportoAsync(RigaRapportoInvio riga) => shell.ApriRapportoAsync(riga.RapportoId);

    [RelayCommand]
    private Task SegnaCaricatoAsync() => EseguiAsync(v => shell.Api.SegnaInvioCaricatoAsync(invioId, v), Testi.InvioSegnatoCaricato);

    [RelayCommand]
    private Task AnnullaAsync() => EseguiAsync(v => shell.Api.AnnullaInvioAsync(invioId, v), Testi.InvioAnnullato);

    [RelayCommand]
    private void RegistraEsiti()
    {
        if (_invio is { } invio)
        {
            shell.MostraDialogo(new EsitiInvioViewModel(shell, invio, () =>
            {
                Avviso = Testi.EsitiRegistrati;
                _ = CaricaAsync();
            }));
        }
    }

    private async Task EseguiAsync(Func<int, Task<Risposta<EsitoScritturaRapporto>>> lavoro, string riuscito)
    {
        if (_invio is not { } invio)
        {
            return;
        }

        var risposta = await lavoro(invio.Invio.Versione);
        await CaricaAsync();
        Errore = risposta.Riuscita ? null : risposta.Messaggio;
        Avviso = risposta.Riuscita ? riuscito : null;
    }
}

/// <summary>Esito di una riga del report SIERT, come lo si legge sul PDF del portale.</summary>
public partial class EsitoModificabile(RigaInvio riga, IReadOnlyList<Opzione> esiti) : ObservableObject
{
    public RigaInvio Riga { get; } = riga;

    public string Descrizione { get; } = $"{riga.Riga}. {riga.Cliente} · {FormatoImpianti.Data(riga.DataControllo)}";

    public IReadOnlyList<Opzione> Esiti { get; } = esiti;

    [ObservableProperty]
    public partial Opzione Esito { get; set; } = esiti.FirstOrDefault(e => e.Codice == riga.Esito) ?? esiti[0];

    [ObservableProperty]
    public partial string Causa { get; set; } = riga.Causa ?? "";
}

/// <summary>Esiti del report: uno per riga; importato e importato in bozza valgono come trasmesso, scartato torna da correggere.</summary>
public partial class EsitiInvioViewModel : DialogoViewModel
{
    private readonly InvioCatasto _invio;
    private readonly Action _salvato;

    public EsitiInvioViewModel(ShellViewModel shell, InvioCatasto invio, Action salvato) : base(shell)
    {
        ArgumentNullException.ThrowIfNull(invio);

        _invio = invio;
        _salvato = salvato;
        IReadOnlyList<Opzione> esiti = [.. new[] { "importato", "importato_bozza", "scartato" }.Select(e => new Opzione(e, FormatoRapporti.EsitoRiga(e)))];
        Righe = [.. invio.Rapporti.OrderBy(r => r.Riga).Select(r => new EsitoModificabile(r, esiti))];
    }

    public string Titolo => string.Format(CultureInfo.CurrentCulture, Testi.TitoloEsitiInvio, _invio.Invio.NomeFile);

    public IReadOnlyList<EsitoModificabile> Righe { get; }

    protected override async Task<string?> EseguiAsync()
    {
        if (Righe.FirstOrDefault(r => r.Esito.Codice == "scartato" && string.IsNullOrWhiteSpace(r.Causa)) is { } senzaCausa)
        {
            return string.Format(CultureInfo.CurrentCulture, Testi.CausaScartoMancante, senzaCausa.Riga.Riga);
        }

        var esiti = Righe.Select(r => new EsitoRigaInvio(r.Riga.RapportoControlloId, r.Esito.Codice, Vuoto(r.Causa))).ToList();
        var risposta = await Shell.Api.RegistraEsitiInvioAsync(_invio.Invio.InvioCatastoId, _invio.Invio.Versione, esiti);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}
