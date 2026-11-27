using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>Testi e conversioni dei rapporti di controllo, dei bollini e degli invii al SIERT.</summary>
public static class FormatoRapporti
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    // Sigle che restano maiuscole nelle etichette ricavate dai nomi del tracciato.
    private static readonly HashSet<string> s_sigle = new(StringComparer.Ordinal) { "ACS", "CO", "CO2", "O2", "UNI", "IVA", "ON/OFF", "ID" };

    public static string Modello(string modello) => modello switch
    {
        "tipo_1" => Testi.ModelloRapportoTipo1,
        "tipo_2" => Testi.ModelloRapportoTipo2,
        _ => modello,
    };

    public static string ModelloBreve(string modello) => modello switch
    {
        "tipo_1" => Testi.ModelloBreveTipo1,
        "tipo_2" => Testi.ModelloBreveTipo2,
        _ => modello,
    };

    public static string Stato(string stato) => stato switch
    {
        "bozza" => Testi.StatoRapportoBozza,
        "convalidato" => Testi.StatoRapportoConvalidato,
        "firmato" => Testi.StatoRapportoFirmato,
        "trasmesso" => Testi.StatoRapportoTrasmesso,
        "scartato" => Testi.StatoRapportoScartato,
        "annullato" => Testi.StatoRapportoAnnullato,
        _ => stato,
    };

    public static string StatoBollino(string stato) => stato switch
    {
        "disponibile" => Testi.StatoBollinoDisponibile,
        "assegnato" => Testi.StatoBollinoAssegnato,
        "usato" => Testi.StatoBollinoUsato,
        "annullato" => Testi.StatoBollinoAnnullato,
        _ => stato,
    };

    public static string StatoInvio(string stato) => stato switch
    {
        "preparato" => Testi.StatoInvioPreparato,
        "caricato" => Testi.StatoInvioCaricato,
        "elaborato" => Testi.StatoInvioElaborato,
        "annullato" => Testi.StatoInvioAnnullato,
        _ => stato,
    };

    public static string EsitoRiga(string esito) => esito switch
    {
        "in_attesa" => Testi.EsitoRigaInAttesa,
        "importato" => Testi.EsitoRigaImportato,
        "importato_bozza" => Testi.EsitoRigaImportatoBozza,
        "scartato" => Testi.EsitoRigaScartato,
        _ => esito,
    };

    /// <summary>Il termine è vicino (una settimana) o superato, per un rapporto non ancora trasmesso.</summary>
    public static bool TermineVicino(DateOnly? termine, string stato, DateOnly oggi) =>
        termine is { } t && stato is "bozza" or "convalidato" or "firmato" or "scartato" && t <= oggi.AddDays(7);

    /// <summary>"PRESENZA_RIFLUSSI" diventa "Presenza riflussi"; le sigle (CO2, ACS) restano maiuscole.</summary>
    public static string Etichetta(string nome)
    {
        var parole = nome.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => s_sigle.Contains(p) ? p : p.ToLower(s_italiano))
            .ToList();
        if (parole.Count == 0)
        {
            return nome;
        }

        if (!s_sigle.Contains(parole[0]))
        {
            parole[0] = char.ToUpper(parole[0][0], s_italiano) + parole[0][1..];
        }

        return string.Join(' ', parole);
    }

    /// <summary>
    /// Nome di un valore codificato. Sono note solo le codifiche ricorrenti della guida
    /// SIERT (SI=3, NO=4, NC=9, NON RICHIESTO=16; in sicurezza SI=21, NO=13): gli altri
    /// codici si mostrano come sono, senza inventarne il significato.
    /// </summary>
    public static string NomeValore(IReadOnlyList<string> ammessi, string valore)
    {
        ArgumentNullException.ThrowIfNull(ammessi);

        if (ammessi.All(a => a is "3" or "4" or "9" or "16"))
        {
            return valore switch
            {
                "3" => Testi.CodiceSi,
                "4" => Testi.CodiceNo,
                "9" => Testi.CodiceNc,
                "16" => Testi.CodiceNonRichiesto,
                _ => valore,
            };
        }

        if (ammessi.All(a => a is "21" or "13"))
        {
            return valore == "21" ? Testi.CodiceSi : Testi.CodiceNo;
        }

        return valore.All(char.IsAsciiDigit) ? string.Format(s_italiano, Testi.CodiceNumerico, valore) : valore;
    }

    /// <summary>Un numero del tracciato (punto decimale) mostrato all'italiana.</summary>
    public static string NumeroPerUtente(string valore) =>
        decimal.TryParse(valore, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero) ? numero.ToString("0.####", s_italiano) : valore;

    /// <summary>Un numero scritto all'italiana nella codifica del tracciato; null se non è un numero.</summary>
    public static string? NumeroPerTracciato(string testo, bool intero)
    {
        if (!FormatoImpianti.ProvaDecimale(testo, out var numero) || numero is not { } n)
        {
            return null;
        }

        return intero
            ? n == decimal.Truncate(n) ? decimal.ToInt64(n).ToString(CultureInfo.InvariantCulture) : null
            : n.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

/// <summary>Una voce del modulo del rapporto, costruita da una colonna del tracciato con origine "dati".</summary>
public partial class CampoModulo : ObservableObject
{
    public CampoModulo(CampoTracciato campo, string? valore)
    {
        ArgumentNullException.ThrowIfNull(campo);

        Campo = campo;
        Etichetta = $"{campo.NumeroCampo}. {FormatoRapporti.Etichetta(campo.Nome)}";
        if (campo.ValoriAmmessi is { Count: > 0 } ammessi)
        {
            Opzioni = [new Opzione("", Testi.NessunValore), .. ammessi.Select(a => new Opzione(a, FormatoRapporti.NomeValore(ammessi, a)))];
            Scelta = Opzioni.FirstOrDefault(o => o.Codice == valore) ?? Opzioni[0];
        }
        else if (campo.Tipo == "data")
        {
            Data = DateOnly.TryParseExact(valore, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToDateTime(TimeOnly.MinValue)
                : null;
        }
        else
        {
            Testo = valore is null ? "" : campo.Tipo is "decimale" or "intero" ? FormatoRapporti.NumeroPerUtente(valore) : valore;
        }
    }

    public CampoTracciato Campo { get; }

    public string Chiave => Campo.Chiave;

    public string Etichetta { get; }

    public bool Obbligatorio => Campo.Obbligatorio;

    public IReadOnlyList<Opzione>? Opzioni { get; }

    public bool ConOpzioni => Opzioni is not null;

    public bool ConData => !ConOpzioni && Campo.Tipo == "data";

    public bool ConTesto => !ConOpzioni && !ConData;

    public int Lunghezza => Campo.Lunghezza ?? 500;

    public bool Lungo => ConTesto && Campo.Lunghezza is > 200;

    public string? Suggerimento => Campo.Tipo switch
    {
        "ora" => Testi.SuggerimentoOra,
        "decimale" => Testi.SuggerimentoDecimale,
        _ => null,
    };

    [ObservableProperty]
    public partial Opzione? Scelta { get; set; }

    [ObservableProperty]
    public partial DateTime? Data { get; set; }

    [ObservableProperty]
    public partial string Testo { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    /// <summary>Il valore nella codifica del tracciato, null se vuoto; false se il numero non si legge.</summary>
    public bool ProvaValore(out string? valore)
    {
        valore = null;
        if (ConOpzioni)
        {
            valore = Scelta is { Codice.Length: > 0 } scelta ? scelta.Codice : null;
            return true;
        }

        if (ConData)
        {
            valore = Data is { } d ? DateOnly.FromDateTime(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
            return true;
        }

        var testo = Testo.Trim();
        if (testo.Length == 0)
        {
            return true;
        }

        if (Campo.Tipo is "decimale" or "intero")
        {
            valore = FormatoRapporti.NumeroPerTracciato(testo, Campo.Tipo == "intero");
            return valore is not null;
        }

        valore = testo;
        return true;
    }
}

public sealed record RigaElencoRapporto(
    Guid Id, string Data, string Cliente, string Luogo, string Modello, string Bollino, string Termine, bool TermineVicino, string Stato);

/// <summary>Rapporti di controllo con i filtri per stato; il Tecnico vede solo i propri.</summary>
public partial class RapportiViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 50;

    public ObservableCollection<RigaElencoRapporto> Righe { get; } = [];

    [ObservableProperty]
    public partial string? Stato { get; set; }

    [ObservableProperty]
    public partial bool DaTrasmettere { get; set; }

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
        ? Testi.ConteggioUnRapporto
        : string.Format(CultureInfo.CurrentCulture, Testi.ConteggioRapporti, Totale);

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public bool FiltroTutti { get => Stato is null && !DaTrasmettere; set { if (value) { Filtra(null, false); } } }

    public bool FiltroBozze { get => Stato == "bozza"; set { if (value) { Filtra("bozza", false); } } }

    public bool FiltroConvalidati { get => Stato == "convalidato"; set { if (value) { Filtra("convalidato", false); } } }

    public bool FiltroDaTrasmettere { get => DaTrasmettere; set { if (value) { Filtra(null, true); } } }

    public bool FiltroTrasmessi { get => Stato == "trasmesso"; set { if (value) { Filtra("trasmesso", false); } } }

    public bool FiltroScartati { get => Stato == "scartato"; set { if (value) { Filtra("scartato", false); } } }

    public async Task CaricaAsync()
    {
        Caricamento = true;
        Errore = null;
        try
        {
            var risposta = await shell.Api.CercaRapportiAsync(Stato, DaTrasmettere, null, null, NumeroPagina, DimensionePagina);
            if (risposta.Valore is not { } pagina)
            {
                Errore = risposta.Messaggio;
                return;
            }

            var oggi = DateOnly.FromDateTime(shell.Orologio.GetLocalNow().DateTime);
            Righe.Clear();
            foreach (var r in pagina.Elementi)
            {
                Righe.Add(new RigaElencoRapporto(
                    r.RapportoControlloId,
                    FormatoImpianti.Data(r.DataControllo),
                    r.Cliente ?? Testi.NonIndicato,
                    string.Join(", ", new[] { r.Indirizzo, r.Comune }.Where(v => !string.IsNullOrEmpty(v))),
                    FormatoRapporti.ModelloBreve(r.Modello),
                    r.CodiceBollino ?? Testi.SenzaBollino,
                    r.TermineTrasmissione is { } t ? FormatoImpianti.Data(t) : "",
                    FormatoRapporti.TermineVicino(r.TermineTrasmissione, r.Stato, oggi),
                    FormatoRapporti.Stato(r.Stato)));
            }

            Totale = pagina.Totale;
        }
        finally
        {
            Caricamento = false;
            OnPropertyChanged(nameof(Vuoto));
        }
    }

    [RelayCommand]
    private Task ApriAsync(RigaElencoRapporto riga) => shell.ApriRapportoAsync(riga.Id);

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

    private void Filtra(string? stato, bool daTrasmettere)
    {
        if (Stato == stato && DaTrasmettere == daTrasmettere)
        {
            return;
        }

        Stato = stato;
        DaTrasmettere = daTrasmettere;
        NumeroPagina = 1;
        foreach (var nome in new[] { nameof(FiltroTutti), nameof(FiltroBozze), nameof(FiltroConvalidati), nameof(FiltroDaTrasmettere), nameof(FiltroTrasmessi), nameof(FiltroScartati) })
        {
            OnPropertyChanged(nome);
        }

        _ = CaricaAsync();
    }
}

/// <summary>
/// Scheda del rapporto: il modulo nasce dal tracciato in vigore, gli errori arrivano
/// dalla validazione del server. Si compila in bozza; convalida, bollino e firma
/// seguono gli stati dell'ADR 0006. La firma e la responsabilità restano all'utente.
/// </summary>
public partial class SchedaRapportoViewModel(ShellViewModel shell, Guid rapportoId) : ViewModelBase
{
    private RapportoControllo? _rapporto;
    private int _versione;

    public ObservableCollection<CampoModulo> Campi { get; } = [];

    /// <summary>Errori bloccanti che non riguardano un campo del modulo (bollino, codice ditta, regole condizionate).</summary>
    public ObservableCollection<string> ErroriGenerali { get; } = [];

    public ObservableCollection<string> Avvisi { get; } = [];

    public ObservableCollection<CampoScheda> Dati { get; } = [];

    [ObservableProperty]
    public partial string Titolo { get; set; } = "";

    [ObservableProperty]
    public partial string Stato { get; set; } = "";

    [ObservableProperty]
    public partial string? Sottotitolo { get; set; }

    [ObservableProperty]
    public partial DateTime? DataControllo { get; set; }

    [ObservableProperty]
    public partial string? Bollino { get; set; }

    [ObservableProperty]
    public partial bool TermineVicino { get; set; }

    [ObservableProperty]
    public partial string? Termine { get; set; }

    [ObservableProperty]
    public partial string? Annullamento { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SolaLettura))]
    public partial bool PuoModificare { get; set; }

    public bool SolaLettura => !PuoModificare;

    [ObservableProperty]
    public partial bool PuoConvalidare { get; set; }

    [ObservableProperty]
    public partial bool PuoRiaprire { get; set; }

    [ObservableProperty]
    public partial bool PuoFirmare { get; set; }

    [ObservableProperty]
    public partial bool PuoAnnullare { get; set; }

    [ObservableProperty]
    public partial bool PuoAssegnareBollino { get; set; }

    [ObservableProperty]
    public partial bool PuoLiberareBollino { get; set; }

    [ObservableProperty]
    public partial bool HaIntervento { get; set; }

    [ObservableProperty]
    public partial bool InCorso { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; }

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    public bool SenzaErroriGenerali => ErroriGenerali.Count == 0;

    public bool HaErroriGenerali => ErroriGenerali.Count > 0;

    public bool HaAvvisi => Avvisi.Count > 0;

    public string Modello => _rapporto?.Modello ?? "";

    public async Task CaricaAsync()
    {
        var risposta = await shell.Api.RapportoAsync(rapportoId);
        if (risposta.Valore is not { } r)
        {
            Errore = risposta.Messaggio;
            return;
        }

        var tracciato = await shell.Api.TracciatoAsync(r.Modello);
        if (tracciato.Valore is not { } campi)
        {
            Errore = tracciato.Messaggio;
            return;
        }

        _rapporto = r;
        _versione = r.Versione;
        Titolo = string.Format(CultureInfo.CurrentCulture, Testi.TitoloRapporto, FormatoRapporti.Modello(r.Modello));
        Stato = FormatoRapporti.Stato(r.Stato);
        Sottotitolo = string.Join(" · ", new[] { Testo(r.Dati, "responsabile_impianto") ?? Testo(r.Dati, "ragione_sociale"), Testo(r.Dati, "indirizzo_impianto") }
            .Where(v => !string.IsNullOrEmpty(v)));
        DataControllo = FormatoImpianti.PerSelettore(r.DataControllo);
        Bollino = r.CodiceBollino is null ? Testi.SenzaBollino : $"{r.CodiceBollino} · {r.TipoBollino}";
        var oggi = DateOnly.FromDateTime(shell.Orologio.GetLocalNow().DateTime);
        TermineVicino = FormatoRapporti.TermineVicino(r.TermineTrasmissione, r.Stato, oggi);
        Termine = r.TermineTrasmissione is { } t && r.Stato is not ("trasmesso" or "annullato")
            ? string.Format(CultureInfo.CurrentCulture, Testi.TermineTrasmissioneValore, FormatoImpianti.Data(t))
            : null;
        Annullamento = r.Stato == "annullato"
            ? string.Format(CultureInfo.CurrentCulture, Testi.AnnullatoIlValore, FormatoInterventi.DataOra(r.AnnullatoIl), r.MotivoAnnullamento)
            : null;

        Dati.Clear();
        Dati.Add(new(Testi.Tecnico, r.Tecnico ?? Testi.NonIndicato));
        Dati.Add(new(Testi.CreatoIl, FormatoInterventi.DataOra(r.CreatoIl)));
        if (r.ConvalidatoIl is { } convalidato)
        {
            Dati.Add(new(Testi.ConvalidatoIl, FormatoInterventi.DataOra(convalidato)));
        }

        if (r.FirmatoIl is { } firmato)
        {
            Dati.Add(new(Testi.FirmatoIl, FormatoInterventi.DataOra(firmato)));
        }

        Campi.Clear();
        foreach (var c in campi.Where(c => c.Origine == "dati").OrderBy(c => c.Posizione))
        {
            Campi.Add(new CampoModulo(c, Testo(r.Dati, c.Chiave)));
        }

        // Gli errori della validazione arrivano con il nome della colonna: quelli del modulo
        // vanno sotto il campo, gli altri (bollino, codice ditta, regole) in cima.
        var perNome = Campi.ToDictionary(c => c.Campo.Nome, StringComparer.Ordinal);
        ErroriGenerali.Clear();
        Avvisi.Clear();
        foreach (var e in r.Errori)
        {
            if (!e.Bloccante)
            {
                Avvisi.Add(e.Campo is null ? e.Messaggio : $"{FormatoRapporti.Etichetta(e.Campo)}: {e.Messaggio}");
            }
            else if (e.Campo is { } nome && perNome.TryGetValue(nome, out var campo))
            {
                campo.Errore = campo.Errore is null ? e.Messaggio : $"{campo.Errore}; {e.Messaggio}";
            }
            else
            {
                ErroriGenerali.Add(e.Campo is null ? e.Messaggio : $"{FormatoRapporti.Etichetta(e.Campo)}: {e.Messaggio}");
            }
        }

        OnPropertyChanged(nameof(SenzaErroriGenerali));
        OnPropertyChanged(nameof(HaErroriGenerali));
        OnPropertyChanged(nameof(HaAvvisi));
        OnPropertyChanged(nameof(Modello));

        var compila = shell.CompilaRapporti;
        var firma = shell.FirmaRapporti;
        PuoModificare = compila && r.Stato == "bozza";
        PuoConvalidare = PuoModificare;
        PuoRiaprire = compila && r.Stato == "convalidato";
        PuoFirmare = firma && r.Stato == "convalidato";
        PuoAnnullare = (compila && r.Stato is "bozza" or "convalidato") || (firma && r.Stato is "firmato" or "scartato");
        PuoAssegnareBollino = compila && r.Stato is "bozza" or "convalidato" && r.BollinoId is null;
        PuoLiberareBollino = compila && r.Stato is "bozza" or "convalidato" && r.BollinoId is not null;
        HaIntervento = r.InterventoId is not null;
    }

    [RelayCommand]
    private Task IndietroAsync() => shell.ApriRapportiAsync();

    [RelayCommand]
    private Task ApriImpiantoAsync() => _rapporto is { } r ? shell.ApriImpiantoAsync(r.ImpiantoId) : Task.CompletedTask;

    [RelayCommand]
    private Task ApriInterventoAsync() => _rapporto?.InterventoId is { } id ? shell.ApriInterventoAsync(id) : Task.CompletedTask;

    [RelayCommand]
    private Task SalvaAsync() => EseguiAsync(async () => await SalvaBozzaAsync() is { } errore ? errore : null, Testi.RapportoSalvato);

    /// <summary>Convalida: prima salva il modulo, poi chiede al server di controllarlo contro il tracciato.</summary>
    [RelayCommand]
    private Task ConvalidaAsync() => EseguiAsync(async () =>
    {
        if (await SalvaBozzaAsync() is { } errore)
        {
            return errore;
        }

        var risposta = await shell.Api.PassaggioRapportoAsync(rapportoId, "convalida", _versione);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }, Testi.RapportoConvalidato);

    [RelayCommand]
    private Task RiapriAsync() => PassaggioAsync("riapertura", Testi.RapportoRiaperto);

    [RelayCommand]
    private Task FirmaAsync() => PassaggioAsync("firma", Testi.RapportoFirmato);

    [RelayCommand]
    private void Annulla()
    {
        if (_rapporto is not { } r)
        {
            return;
        }

        shell.MostraDialogo(new MotivoViewModel(
            shell, Testi.TitoloAnnullaRapporto, Testi.SpiegazioneAnnullaRapporto, Testi.AnnullaRapporto,
            async motivo =>
            {
                var risposta = await shell.Api.AnnullaRapportoAsync(r.RapportoControlloId, r.Versione, motivo);
                return risposta.Riuscita ? null : risposta.Messaggio;
            },
            () => _ = CaricaAsync()));
    }

    [RelayCommand]
    private async Task AssegnaBollinoAsync()
    {
        if (_rapporto is not { } r)
        {
            return;
        }

        var tipi = await shell.Api.TipiBollinoAsync();
        if (tipi.Valore is not { } elenco)
        {
            Errore = tipi.Messaggio;
            return;
        }

        // Il bollino è della categoria del modello: GT per i gruppi termici, GF per i gruppi frigo.
        var categoria = r.Modello == "tipo_2" ? "GF" : "GT";
        shell.MostraDialogo(new AssegnaBollinoViewModel(shell, r, [.. elenco.Where(t => t.Categoria == categoria)], () => _ = CaricaAsync()));
    }

    [RelayCommand]
    private Task LiberaBollinoAsync() => EseguiAsync(async () =>
    {
        var risposta = await shell.Api.LiberaBollinoAsync(rapportoId, _versione);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }, Testi.BollinoLiberato);

    private Task PassaggioAsync(string passaggio, string riuscito) => EseguiAsync(async () =>
    {
        var risposta = await shell.Api.PassaggioRapportoAsync(rapportoId, passaggio, _versione);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }, riuscito);

    // Salva la bozza; restituisce il messaggio d'errore, oppure null se è andata bene.
    private async Task<string?> SalvaBozzaAsync()
    {
        if (DataControllo is not { } data)
        {
            return Testi.DataControlloMancante;
        }

        var dati = new JsonObject();
        foreach (var campo in Campi)
        {
            if (!campo.ProvaValore(out var valore))
            {
                return string.Format(CultureInfo.CurrentCulture, Testi.NumeroCampoNonValido, campo.Etichetta);
            }

            if (valore is not null)
            {
                dati[campo.Chiave] = valore;
            }
        }

        var risposta = await shell.Api.AggiornaRapportoAsync(rapportoId, new DatiBozzaRapporto(DateOnly.FromDateTime(data), dati, _versione));
        if (risposta.Valore is { } scritto)
        {
            _versione = scritto.Versione;
        }

        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    private async Task EseguiAsync(Func<Task<string?>> lavoro, string riuscito)
    {
        if (InCorso || _rapporto is null)
        {
            return;
        }

        InCorso = true;
        Errore = null;
        Avviso = null;
        try
        {
            var errore = await lavoro();
            await CaricaAsync();
            Errore = errore;
            Avviso = errore is null ? riuscito : null;
        }
        finally
        {
            InCorso = false;
        }
    }

    private static string? Testo(JsonObject dati, string chiave) => dati[chiave] switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonNode n => n.ToJsonString(),
    };
}

/// <summary>Motivo obbligatorio per un'operazione che non si annulla: annullare un rapporto o un bollino.</summary>
public partial class MotivoViewModel(
    ShellViewModel shell, string titolo, string spiegazione, string pulsante, Func<string, Task<string?>> esegui, Action salvato) : DialogoViewModel(shell)
{
    public string Titolo { get; } = titolo;

    public string Spiegazione { get; } = spiegazione;

    public string Pulsante { get; } = pulsante;

    [ObservableProperty]
    public partial string Motivo { get; set; } = "";

    protected override Task<string?> EseguiAsync() =>
        string.IsNullOrWhiteSpace(Motivo) ? Task.FromResult<string?>(Testi.MotivoMancante) : esegui(Motivo.Trim());

    protected override void DopoIlSalvataggio() => salvato();
}

public sealed record OpzioneTipoBollino(Guid Id, string Nome, int Disponibili)
{
    public override string ToString() => Nome;
}

/// <summary>Bollino al rapporto: il server assegna il disponibile più vecchio del tipo scelto.</summary>
public partial class AssegnaBollinoViewModel : DialogoViewModel
{
    private readonly RapportoControllo _rapporto;
    private readonly Action _salvato;

    public AssegnaBollinoViewModel(ShellViewModel shell, RapportoControllo rapporto, IReadOnlyList<TipoBollino> tipi, Action salvato) : base(shell)
    {
        ArgumentNullException.ThrowIfNull(rapporto);
        ArgumentNullException.ThrowIfNull(tipi);

        _rapporto = rapporto;
        _salvato = salvato;
        Tipi = [.. tipi.Select(t => new OpzioneTipoBollino(
            t.TipoBollinoId,
            string.Format(CultureInfo.CurrentCulture, Testi.TipoBollinoConDisponibili, t.Nome, FormatoInterventi.Importo(t.Importo), t.Disponibili),
            t.Disponibili))];
        Tipo = Tipi.FirstOrDefault(t => t.Disponibili > 0);
    }

    public IReadOnlyList<OpzioneTipoBollino> Tipi { get; }

    public bool SenzaTipi => Tipi.Count == 0;

    [ObservableProperty]
    public partial OpzioneTipoBollino? Tipo { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        if (Tipo is not { } tipo)
        {
            return Testi.ScegliTipoBollino;
        }

        var risposta = await Shell.Api.AssegnaBollinoAsync(_rapporto.RapportoControlloId, tipo.Id, _rapporto.Versione);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}

/// <summary>Nuovo rapporto dalla scheda dell'impianto: generatore, modello e data; la bozza nasce precompilata.</summary>
public partial class NuovoRapportoViewModel : DialogoViewModel
{
    private readonly Guid _impiantoId;
    private readonly Action<Guid> _creato;
    private Guid _id;

    public NuovoRapportoViewModel(ShellViewModel shell, Guid impiantoId, IReadOnlyList<Generatore> generatori, Action<Guid> creato) : base(shell)
    {
        ArgumentNullException.ThrowIfNull(generatori);

        _impiantoId = impiantoId;
        _creato = creato;
        Generatori = [.. generatori.Select(g => new OpzioneGeneratore(g.GeneratoreId, string.Join(" · ", new[] { g.SiglaCatasto, g.TipoGeneratore, g.Marca, g.Modello }
            .Where(v => !string.IsNullOrEmpty(v)))))];
        Generatore = Generatori.Count == 1 ? Generatori[0] : null;
        Modello = Modelli[0];
        DataControllo = shell.Orologio.GetLocalNow().Date;
    }

    public IReadOnlyList<Opzione> Modelli { get; } =
    [
        new("tipo_1", Testi.ModelloRapportoTipo1),
        new("tipo_2", Testi.ModelloRapportoTipo2),
    ];

    public IReadOnlyList<OpzioneGeneratore> Generatori { get; }

    [ObservableProperty]
    public partial OpzioneGeneratore? Generatore { get; set; }

    [ObservableProperty]
    public partial Opzione Modello { get; set; }

    [ObservableProperty]
    public partial DateTime? DataControllo { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        if (Generatore is not { } generatore)
        {
            return Testi.ScegliGeneratoreRapporto;
        }

        if (DataControllo is not { } data)
        {
            return Testi.DataControlloMancante;
        }

        var risposta = await Shell.Api.CreaRapportoAsync(new DatiNuovoRapporto(_impiantoId, generatore.Id, null, Modello.Codice, DateOnly.FromDateTime(data)));
        _id = risposta.Valore?.Id ?? Guid.Empty;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _creato(_id);
}
