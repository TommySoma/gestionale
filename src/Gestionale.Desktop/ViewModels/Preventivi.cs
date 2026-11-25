using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record RigaElencoPreventivo(Guid Id, string Numero, string Cliente, string Oggetto, string Data, string Totale, string Stato, bool Scaduto);

/// <summary>Elenco dei preventivi, dal più recente, con ricerca e filtro per stato (anche "scaduti").</summary>
public partial class PreventiviViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 50;
    private static readonly TimeSpan s_attesaRicerca = TimeSpan.FromMilliseconds(300);
    private CancellationTokenSource? _ricerca;

    public ObservableCollection<RigaElencoPreventivo> Righe { get; } = [];

    public bool GestiscePreventivi => shell.GestiscePreventivi;

    [ObservableProperty]
    public partial string Testo { get; set; } = "";

    [ObservableProperty]
    public partial string? Stato { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervallo), nameof(PuoPrecedente), nameof(PuoSuccessiva))]
    public partial int NumeroPagina { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Conteggio), nameof(Intervallo), nameof(PuoSuccessiva), nameof(Vuoto))]
    public partial int Totale { get; set; }

    [ObservableProperty]
    public partial bool Caricamento { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool Vuoto => !Caricamento && Totale == 0 && !HaErrore;

    public string Conteggio => Totale == 1
        ? Testi.ConteggioUnPreventivo
        : string.Format(CultureInfo.CurrentCulture, Testi.ConteggioPreventivi, Totale);

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public bool FiltroTutti { get => Stato is null; set { if (value) { ImpostaStato(null); } } }

    public bool FiltroBozze { get => Stato == "bozza"; set { if (value) { ImpostaStato("bozza"); } } }

    public bool FiltroEmessi { get => Stato == "emesso"; set { if (value) { ImpostaStato("emesso"); } } }

    public bool FiltroScaduti { get => Stato == "scaduto"; set { if (value) { ImpostaStato("scaduto"); } } }

    public bool FiltroAccettati { get => Stato == "accettato"; set { if (value) { ImpostaStato("accettato"); } } }

    public async Task CaricaAsync(CancellationToken ct = default)
    {
        Caricamento = true;
        Errore = null;
        try
        {
            var risposta = await shell.Api.CercaPreventiviAsync(Testo, Stato, null, NumeroPagina, DimensionePagina, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (risposta.Valore is not { } pagina)
            {
                Errore = risposta.Messaggio;
                return;
            }

            Righe.Clear();
            foreach (var p in pagina.Elementi)
            {
                Righe.Add(new RigaElencoPreventivo(
                    p.PreventivoId,
                    FormatoCommerciale.Numero(p.Numero, p.Anno),
                    p.Cliente,
                    p.Oggetto,
                    p.EmessoIl is { } emesso ? FormatoImpianti.Data(emesso) : FormatoInterventi.DataOra(p.CreatoIl),
                    FormatoInterventi.Importo(p.Totale),
                    FormatoCommerciale.StatoPreventivo(p.Stato, p.Scaduto),
                    p.Scaduto));
            }

            Totale = pagina.Totale;
        }
        finally
        {
            Caricamento = false;
            OnPropertyChanged(nameof(Vuoto));
        }
    }

    partial void OnTestoChanged(string value)
    {
        _ricerca?.Cancel();
        _ricerca?.Dispose();
        _ricerca = new CancellationTokenSource();
        _ = CercaDopoUnaPausaAsync(_ricerca.Token);
    }

    private async Task CercaDopoUnaPausaAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(s_attesaRicerca, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        NumeroPagina = 1;
        await CaricaAsync(ct);
    }

    private void ImpostaStato(string? stato)
    {
        if (Stato == stato)
        {
            return;
        }

        Stato = stato;
        NumeroPagina = 1;
        foreach (var nome in new[] { nameof(FiltroTutti), nameof(FiltroBozze), nameof(FiltroEmessi), nameof(FiltroScaduti), nameof(FiltroAccettati) })
        {
            OnPropertyChanged(nome);
        }

        _ = CaricaAsync();
    }

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

    [RelayCommand]
    private Task ApriAsync(RigaElencoPreventivo riga) => shell.ApriPreventivoAsync(riga.Id);

    [RelayCommand]
    private async Task NuovoAsync() =>
        shell.MostraDialogo(await EditorPreventivoViewModel.CreaAsync(shell, null, null, id => _ = shell.ApriPreventivoAsync(id)));
}

/// <summary>
/// Scheda di un preventivo con le azioni che lo stato permette: la bozza si
/// modifica, si emette o si elimina; l'emesso si accetta, si rifiuta o si annulla;
/// dall'accettato nasce l'intervento da pianificare. Il PDF si scarica sempre.
/// </summary>
public partial class SchedaPreventivoViewModel(ShellViewModel shell, Guid preventivoId) : ViewModelBase
{
    private Preventivo? _preventivo;

    public ObservableCollection<CampoScheda> Dati { get; } = [];

    public ObservableCollection<RigaMostrata> Righe { get; } = [];

    public bool PuoAprireCliente => shell.VedeClienti;

    [ObservableProperty]
    public partial string Titolo { get; set; } = "";

    [ObservableProperty]
    public partial string Stato { get; set; } = "";

    [ObservableProperty]
    public partial string Cliente { get; set; } = "";

    [ObservableProperty]
    public partial string? Luogo { get; set; }

    [ObservableProperty]
    public partial string? Impianto { get; set; }

    [ObservableProperty]
    public partial string Oggetto { get; set; } = "";

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    public partial string Imponibile { get; set; } = "";

    [ObservableProperty]
    public partial string Iva { get; set; } = "";

    [ObservableProperty]
    public partial string Totale { get; set; } = "";

    [ObservableProperty]
    public partial bool Scaduto { get; set; }

    [ObservableProperty]
    public partial string? Annullamento { get; set; }

    [ObservableProperty]
    public partial bool PuoModificare { get; set; }

    [ObservableProperty]
    public partial bool PuoRispondere { get; set; }

    [ObservableProperty]
    public partial bool PuoAnnullare { get; set; }

    [ObservableProperty]
    public partial bool PuoGenerareIntervento { get; set; }

    [ObservableProperty]
    public partial bool HaIntervento { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; }

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    public bool SenzaRighe => Righe.Count == 0;

    /// <summary>Nome proposto per il file, come lo suggerisce anche il server.</summary>
    public string NomePdf => _preventivo is { Numero: { } numero } p ? $"preventivo-{p.Anno}-{numero:D4}.pdf" : "bozza-di-preventivo.pdf";

    public async Task CaricaAsync()
    {
        var risposta = await shell.Api.PreventivoAsync(preventivoId);
        if (risposta.Valore is not { } p)
        {
            Errore = risposta.Messaggio;
            return;
        }

        _preventivo = p;
        Titolo = p.Numero is { } numero
            ? string.Format(CultureInfo.CurrentCulture, Testi.NumeroPreventivo, numero, p.Anno)
            : Testi.TitoloBozzaPreventivo;
        Stato = FormatoCommerciale.StatoPreventivo(p.Stato, p.Scaduto);
        Scaduto = p.Scaduto;
        Cliente = p.Cliente;
        Luogo = p.Indirizzo is null ? null : FormatoImpianti.Indirizzo(p.Indirizzo, p.Civico, p.Cap, p.Comune ?? "", p.Provincia ?? "");
        Impianto = p.TipoImpianto;
        Oggetto = p.Oggetto;
        Note = p.Note;
        Imponibile = FormatoInterventi.Importo(p.Imponibile);
        Iva = FormatoInterventi.Importo(p.Iva);
        Totale = FormatoInterventi.Importo(p.Totale);
        Annullamento = p.Stato == "annullato"
            ? string.Format(CultureInfo.CurrentCulture, Testi.AnnullatoIlValore, FormatoInterventi.DataOra(p.AnnullatoIl), p.MotivoAnnullamento)
            : null;

        Dati.Clear();
        Dati.Add(new(Testi.DataEmissione, p.EmessoIl is { } emesso ? FormatoImpianti.Data(emesso) : Testi.NonEmesso));
        Dati.Add(new(Testi.ValidoFinoAl, p.ScadeIl is { } scade
            ? FormatoImpianti.Data(scade)
            : string.Format(CultureInfo.CurrentCulture, Testi.ValiditaGiorniValore, p.ValiditaGiorni)));
        if (p.AccettatoIl is { } accettato)
        {
            Dati.Add(new(Testi.AccettatoIl, FormatoInterventi.DataOra(accettato)));
        }

        if (p.RifiutatoIl is { } rifiutato)
        {
            Dati.Add(new(Testi.RifiutatoIl, FormatoInterventi.DataOra(rifiutato)));
        }

        Righe.Clear();
        foreach (var r in p.Righe)
        {
            Righe.Add(new RigaMostrata(
                FormatoCommerciale.Tipo(r.Tipo),
                r.Codice is null ? r.Descrizione : $"{r.Codice} · {r.Descrizione}",
                $"{FormatoInterventi.Quantita(r.Quantita)} {r.Unita}",
                FormatoInterventi.Importo(r.PrezzoUnitario),
                r.ScontoPercentuale == 0 ? "" : $"−{FormatoInterventi.Quantita(r.ScontoPercentuale)}%",
                FormatoInterventi.Importo(r.Importo)));
        }

        OnPropertyChanged(nameof(SenzaRighe));
        OnPropertyChanged(nameof(NomePdf));

        var scrive = shell.GestiscePreventivi;
        PuoModificare = scrive && p.Stato == "bozza";
        PuoRispondere = scrive && p.Stato == "emesso";
        PuoAnnullare = scrive && (p.Stato == "emesso" || (p.Stato == "accettato" && p.InterventoId is null));
        HaIntervento = p.InterventoId is not null;
        PuoGenerareIntervento = scrive && shell.PianificaInterventi && p.Stato == "accettato" && p.InterventoId is null;
    }

    /// <summary>Scarica il PDF; in caso di errore mostra il messaggio e restituisce null. Il salvataggio su disco lo fa la vista.</summary>
    public async Task<byte[]?> PdfAsync()
    {
        var risposta = await shell.Api.PdfPreventivoAsync(preventivoId);
        Errore = risposta.Valore is null ? risposta.Messaggio : null;
        return risposta.Valore;
    }

    public void SegnalaPdfSalvato(string percorso) =>
        Avviso = string.Format(CultureInfo.CurrentCulture, Testi.PdfPreventivoSalvato, percorso);

    [RelayCommand]
    private Task IndietroAsync() => shell.ApriPreventiviAsync();

    [RelayCommand]
    private async Task ApriClienteAsync()
    {
        if (_preventivo is { } p && shell.VedeClienti)
        {
            await shell.ApriClienteAsync(p.ClienteId);
        }
    }

    [RelayCommand]
    private async Task ApriInterventoAsync()
    {
        if (_preventivo?.InterventoId is { } intervento)
        {
            await shell.ApriInterventoAsync(intervento, () => shell.ApriPreventivoAsync(preventivoId));
        }
    }

    [RelayCommand]
    private async Task ModificaAsync()
    {
        if (_preventivo is { } p)
        {
            shell.MostraDialogo(await EditorPreventivoViewModel.CreaAsync(shell, p, null, salvato => _ = CaricaAsync()));
        }
    }

    [RelayCommand]
    private Task EmettiAsync() => CambiaStatoAsync("emissione");

    [RelayCommand]
    private Task AccettaAsync() => CambiaStatoAsync("accettazione");

    [RelayCommand]
    private void Rifiuta()
    {
        if (_preventivo is not { } p)
        {
            return;
        }

        shell.MostraDialogo(new ConfermaViewModel(
            shell, Testi.ConfermaRifiutoTitolo, string.Format(CultureInfo.CurrentCulture, Testi.ConfermaRifiuto, $"{p.Numero}/{p.Anno}"), Testi.SegnaRifiutato,
            async () =>
            {
                var risposta = await shell.Api.CambiaStatoPreventivoAsync(p.PreventivoId, "rifiuto", p.Versione);
                if (!risposta.Riuscita)
                {
                    return risposta.Messaggio;
                }

                await CaricaAsync();
                return null;
            }));
    }

    [RelayCommand]
    private void Elimina()
    {
        if (_preventivo is not { } p)
        {
            return;
        }

        shell.MostraDialogo(new ConfermaViewModel(
            shell, Testi.ConfermaEliminaBozzaTitolo, string.Format(CultureInfo.CurrentCulture, Testi.ConfermaEliminaBozza, p.Oggetto), Testi.Elimina,
            async () =>
            {
                var risposta = await shell.Api.EliminaPreventivoAsync(p.PreventivoId, p.Versione);
                if (!risposta.Riuscita)
                {
                    return risposta.Messaggio;
                }

                await shell.ApriPreventiviAsync();
                return null;
            }));
    }

    [RelayCommand]
    private void Annulla()
    {
        if (_preventivo is { } p)
        {
            shell.MostraDialogo(new AnnullamentoPreventivoViewModel(shell, p, () => _ = CaricaAsync()));
        }
    }

    [RelayCommand]
    private void GeneraIntervento()
    {
        if (_preventivo is { } p)
        {
            shell.MostraDialogo(new InterventoDaPreventivoViewModel(shell, p, id => _ = shell.ApriInterventoAsync(id, () => shell.ApriPreventivoAsync(preventivoId))));
        }
    }

    private async Task CambiaStatoAsync(string azione)
    {
        if (_preventivo is not { } p)
        {
            return;
        }

        var risposta = await shell.Api.CambiaStatoPreventivoAsync(p.PreventivoId, azione, p.Versione);
        Errore = risposta.Riuscita ? null : risposta.Messaggio;
        await CaricaAsync();
    }
}

/// <summary>
/// Bozza nuova o da modificare: cliente, luogo e impianto, oggetto, validità e
/// righe, a mano o dal listino (con i prezzi di oggi). I totali li ricalcola il
/// server; qui se ne mostra una stima con la stessa regola (IVA per aliquota).
/// </summary>
public partial class EditorPreventivoViewModel : DialogoViewModel
{
    private readonly Preventivo? _esistente;
    private readonly Action<Guid> _salvato;
    private IReadOnlyList<RiepilogoImpianto> _impianti = [];
    private Guid _id;
    private bool _inizializzazione = true;

    private EditorPreventivoViewModel(ShellViewModel shell, Preventivo? esistente, OpzioneCliente? cliente, Action<Guid> salvato) : base(shell)
    {
        _esistente = esistente;
        _salvato = salvato;
        Oggetto = esistente?.Oggetto ?? "";
        Note = esistente?.Note ?? "";
        Validita = (esistente?.ValiditaGiorni ?? 30).ToString(CultureInfo.InvariantCulture);

        var iniziale = esistente is { } e ? new OpzioneCliente(e.ClienteId, e.Cliente) : cliente;
        if (iniziale is not null)
        {
            Clienti.Add(iniziale);
            Cliente = iniziale;
        }

        Righe.CollectionChanged += (_, e) =>
        {
            foreach (var r in e.NewItems?.OfType<RigaModificabile>() ?? [])
            {
                r.PropertyChanged += RigaCambiata;
            }

            AggiornaTotali();
        };

        foreach (var r in esistente?.Righe ?? [])
        {
            var italiano = CultureInfo.GetCultureInfo("it-IT");
            Righe.Add(new RigaModificabile(r.Tipo)
            {
                VoceId = r.VoceId,
                Codice = r.Codice ?? "",
                Descrizione = r.Descrizione,
                Unita = r.Unita,
                Quantita = r.Quantita.ToString("0.###", italiano),
                Prezzo = r.PrezzoUnitario.ToString("0.00", italiano),
                Sconto = r.ScontoPercentuale == 0 ? "" : r.ScontoPercentuale.ToString("0.##", italiano),
                Iva = r.AliquotaIva?.ToString("0.##", italiano) ?? "",
            });
        }
    }

    /// <summary>Apre l'editor con sedi e impianti del cliente già caricati.</summary>
    public static async Task<EditorPreventivoViewModel> CreaAsync(ShellViewModel shell, Preventivo? esistente, OpzioneCliente? cliente, Action<Guid> salvato)
    {
        var editor = new EditorPreventivoViewModel(shell, esistente, cliente, salvato);
        await editor.CaricaLuoghiAsync();
        editor._inizializzazione = false;
        return editor;
    }

    public string Titolo => _esistente is null ? Testi.TitoloNuovoPreventivo : Testi.TitoloModificaPreventivo;

    public ObservableCollection<OpzioneCliente> Clienti { get; } = [];

    public ObservableCollection<OpzioneVoce> Sedi { get; } = [];

    public ObservableCollection<OpzioneVoce> Impianti { get; } = [];

    public ObservableCollection<RigaModificabile> Righe { get; } = [];

    public ObservableCollection<VoceListino> VociTrovate { get; } = [];

    [ObservableProperty]
    public partial OpzioneCliente? Cliente { get; set; }

    [ObservableProperty]
    public partial OpzioneVoce? Sede { get; set; }

    [ObservableProperty]
    public partial OpzioneVoce? Impianto { get; set; }

    [ObservableProperty]
    public partial string RicercaCliente { get; set; } = "";

    [ObservableProperty]
    public partial string RicercaListino { get; set; } = "";

    [ObservableProperty]
    public partial string Oggetto { get; set; }

    [ObservableProperty]
    public partial string Validita { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string Imponibile { get; set; } = FormatoInterventi.Importo(0);

    [ObservableProperty]
    public partial string Iva { get; set; } = FormatoInterventi.Importo(0);

    [ObservableProperty]
    public partial string Totale { get; set; } = FormatoInterventi.Importo(0);

    public bool SenzaRighe => Righe.Count == 0;

    public bool SenzaVociTrovate => VociTrovate.Count == 0;

    /// <summary>Sedi e impianti attivi del cliente scelto; la selezione attuale resta se c'è ancora.</summary>
    public async Task CaricaLuoghiAsync()
    {
        Sedi.Clear();
        Impianti.Clear();
        var nessuna = new OpzioneVoce(null, Testi.NonIndicato);
        Sedi.Add(nessuna);
        if (Cliente?.Id is not { } clienteId)
        {
            Sede = nessuna;
            AggiornaImpianti();
            return;
        }

        var cliente = (await Shell.Api.ClienteAsync(clienteId)).Valore;
        foreach (var s in cliente?.Sedi ?? [])
        {
            Sedi.Add(new OpzioneVoce(s.SedeId, FormatoImpianti.Indirizzo(s.Indirizzo, s.Civico, s.Cap, s.Comune, s.Provincia)));
        }

        _impianti = Shell.VedeImpianti
            ? (await Shell.Api.CercaImpiantiAsync(null, clienteId, "attivo", 1, 100)).Valore?.Elementi ?? []
            : [];

        var sedeVoluta = _inizializzazione ? _esistente?.SedeId : null;
        Sede = Sedi.FirstOrDefault(s => s.Id == sedeVoluta) ?? nessuna;
        AggiornaImpianti();
    }

    partial void OnClienteChanged(OpzioneCliente? value)
    {
        if (!_inizializzazione)
        {
            _ = CaricaLuoghiAsync();
        }
    }

    partial void OnSedeChanged(OpzioneVoce? value)
    {
        if (!_inizializzazione)
        {
            AggiornaImpianti();
        }
    }

    // Gli impianti della sede scelta, o di tutto il cliente se la sede non c'è.
    private void AggiornaImpianti()
    {
        var voluto = Impianto?.Id ?? (_inizializzazione ? _esistente?.ImpiantoId : null);
        Impianti.Clear();
        var nessuno = new OpzioneVoce(null, Testi.NonIndicato);
        Impianti.Add(nessuno);
        foreach (var i in _impianti.Where(i => Sede?.Id is not { } sede || i.SedeId == sede))
        {
            Impianti.Add(new OpzioneVoce(i.ImpiantoId, $"{i.TipoImpianto} · {i.Indirizzo}{(i.Civico is null ? "" : " " + i.Civico)}"));
        }

        Impianto = Impianti.FirstOrDefault(i => i.Id == voluto) ?? nessuno;
    }

    [RelayCommand]
    private async Task CercaClienteAsync()
    {
        if (string.IsNullOrWhiteSpace(RicercaCliente))
        {
            return;
        }

        var risposta = await Shell.Api.CercaClientiAsync(RicercaCliente, null, 1, 10);
        if (risposta.Valore is not { } pagina)
        {
            Errore = risposta.Messaggio;
            return;
        }

        Errore = pagina.Totale == 0 ? Testi.NessunClienteTrovato : null;
        foreach (var c in pagina.Elementi.Select(c => new OpzioneCliente(c.ClienteId, c.NomeVisualizzato)).Where(c => !Clienti.Contains(c)))
        {
            Clienti.Add(c);
        }

        if (Cliente is null && Clienti.Count > 0)
        {
            Cliente = Clienti[0];
        }
    }

    [RelayCommand]
    private async Task CercaListinoAsync()
    {
        var risposta = await Shell.Api.CercaListinoAsync(RicercaListino, false, 1, 10);
        Errore = risposta.Valore is null ? risposta.Messaggio : null;
        VociTrovate.Clear();
        foreach (var v in risposta.Valore?.Elementi ?? [])
        {
            VociTrovate.Add(v);
        }

        OnPropertyChanged(nameof(SenzaVociTrovate));
    }

    /// <summary>Una riga dalla voce del listino, con il prezzo di oggi: poi si può ritoccare.</summary>
    [RelayCommand]
    private void AggiungiVoce(VoceListino voce)
    {
        var italiano = CultureInfo.GetCultureInfo("it-IT");
        Righe.Add(new RigaModificabile(voce.Tipo)
        {
            VoceId = voce.VoceId,
            Codice = voce.Codice,
            Descrizione = voce.Descrizione,
            Unita = voce.Unita,
            Prezzo = voce.Prezzo?.ToString("0.00", italiano) ?? "",
            Iva = voce.AliquotaIva?.ToString("0.##", italiano) ?? "",
        });
    }

    [RelayCommand]
    private void AggiungiManodopera() => Righe.Add(new RigaModificabile("manodopera") { Unita = "h", Descrizione = Testi.ManodoperaPredefinita });

    [RelayCommand]
    private void AggiungiMateriale() => Righe.Add(new RigaModificabile("materiale"));

    [RelayCommand]
    private void RimuoviRiga(RigaModificabile riga)
    {
        riga.PropertyChanged -= RigaCambiata;
        Righe.Remove(riga);
    }

    protected override async Task<string?> EseguiAsync()
    {
        if (Cliente?.Id is not { } clienteId)
        {
            return Testi.ScegliCliente;
        }

        if (!FormatoCommerciale.ProvaIntero(Validita, out var validita) || validita is null)
        {
            return Testi.ValiditaNonValida;
        }

        var righe = new List<DatiRigaPreventivo>();
        foreach (var (riga, n) in Righe.Select((r, i) => (r, i + 1)))
        {
            if (!FormatoImpianti.ProvaDecimale(riga.Quantita, out var quantita) || quantita is null
                || !FormatoImpianti.ProvaDecimale(riga.Prezzo, out var prezzo)
                || !FormatoImpianti.ProvaDecimale(riga.Sconto, out var sconto)
                || !FormatoImpianti.ProvaDecimale(riga.Iva, out var iva))
            {
                return string.Format(CultureInfo.CurrentCulture, Testi.RigaNumeriNonValidi, n);
            }

            righe.Add(new DatiRigaPreventivo(
                riga.VoceId, riga.Tipo, string.IsNullOrWhiteSpace(riga.Codice) ? null : riga.Codice.Trim(), riga.Descrizione.Trim(),
                riga.Manodopera ? "h" : riga.Unita.Trim(), quantita.Value, prezzo, sconto, iva));
        }

        var dati = new DatiPreventivo(clienteId, Sede?.Id, Impianto?.Id, Oggetto.Trim(), Vuoto(Note), validita.Value, righe, _esistente?.Versione);
        var risposta = _esistente is null
            ? await Shell.Api.CreaPreventivoAsync(dati)
            : await Shell.Api.AggiornaPreventivoAsync(_esistente.PreventivoId, dati);

        _id = risposta.Valore?.Id ?? Guid.Empty;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato(_id);

    private void RigaCambiata(object? sender, PropertyChangedEventArgs e) => AggiornaTotali();

    // La stessa regola del server: IVA per aliquota sulla somma degli importi, arrotondata al centesimo.
    private void AggiornaTotali()
    {
        var righe = Righe.Select(r => (Importo: r.Importo() ?? 0, Iva: FormatoImpianti.ProvaDecimale(r.Iva, out var iva) ? iva : null)).ToList();
        var imponibile = righe.Sum(r => r.Importo);
        var imposta = righe
            .Where(r => r.Iva is not null)
            .GroupBy(r => r.Iva!.Value)
            .Sum(g => decimal.Round(g.Sum(r => r.Importo) * g.Key / 100, 2, MidpointRounding.AwayFromZero));
        Imponibile = FormatoInterventi.Importo(imponibile);
        Iva = FormatoInterventi.Importo(imposta);
        Totale = FormatoInterventi.Importo(imponibile + imposta);
        OnPropertyChanged(nameof(SenzaRighe));
    }
}

/// <summary>Annullamento di un preventivo emesso: resta in archivio con il motivo.</summary>
public partial class AnnullamentoPreventivoViewModel(ShellViewModel shell, Preventivo preventivo, Action salvato) : DialogoViewModel(shell)
{
    public string Titolo { get; } = string.Format(
        CultureInfo.CurrentCulture, Testi.TitoloAnnullaPreventivo, $"{preventivo.Numero}/{preventivo.Anno}");

    [ObservableProperty]
    public partial string Motivo { get; set; } = "";

    protected override async Task<string?> EseguiAsync()
    {
        var risposta = await Shell.Api.AnnullaPreventivoAsync(preventivo.PreventivoId, preventivo.Versione, Motivo.Trim());
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => salvato();
}

/// <summary>Dal preventivo accettato nasce l'intervento da pianificare, con le righe; il tipo lo sceglie chi lo crea.</summary>
public partial class InterventoDaPreventivoViewModel(ShellViewModel shell, Preventivo preventivo, Action<Guid> creato) : DialogoViewModel(shell)
{
    private Guid _intervento;

    public string Descrizione { get; } = $"{FormatoCommerciale.Numero(preventivo.Numero, preventivo.Anno)} · {preventivo.Oggetto}";

    public bool SenzaImpianto { get; } = preventivo.ImpiantoId is null;

    public IReadOnlyList<Opzione> Tipi { get; } =
        [.. new[] { "installazione", "riparazione", "manutenzione", "sopralluogo", "altro" }.Select(t => new Opzione(t, FormatoInterventi.Tipo(t)))];

    [ObservableProperty]
    public partial Opzione? Tipo { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        if (Tipo is null)
        {
            return Testi.ScegliTipoIntervento;
        }

        var risposta = await Shell.Api.GeneraInterventoAsync(preventivo.PreventivoId, preventivo.Versione, Tipo.Codice);
        _intervento = risposta.Valore?.InterventoId ?? Guid.Empty;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => creato(_intervento);
}
