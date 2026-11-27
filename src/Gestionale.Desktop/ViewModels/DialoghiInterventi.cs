using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record OpzioneImpianto(Guid Id, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>Tecnico a cui assegnare; Id nullo significa "da assegnare".</summary>
public sealed record OpzioneTecnico(Guid? Id, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>
/// Nuovo intervento o pianificazione di uno esistente. Senza data resta da
/// pianificare; con la data servono orari e tecnico. La finestra si sceglie in un
/// giorno solo: gli interventi di più giorni si pianificano giorno per giorno.
/// </summary>
public partial class EditorInterventoViewModel : DialogoViewModel
{
    private readonly Intervento? _esistente;
    private readonly Action<Guid> _salvato;
    private Guid _id;

    public EditorInterventoViewModel(
        ShellViewModel shell, OpzioneImpianto? impianto, Intervento? esistente, IReadOnlyList<TecnicoAssegnabile> tecnici, Action<Guid> salvato) : base(shell)
    {
        ArgumentNullException.ThrowIfNull(tecnici);

        _esistente = esistente;
        _salvato = salvato;

        impianto ??= esistente is null ? null : new OpzioneImpianto(esistente.ImpiantoId, $"{esistente.TipoImpianto} · {esistente.Cliente} · {esistente.Comune}");
        if (impianto is not null)
        {
            Impianti.Add(impianto);
        }

        Impianto = impianto;
        ImpiantoFisso = impianto is not null && esistente is null;

        var daAssegnare = new OpzioneTecnico(null, Testi.DaAssegnare);
        Tecnici = [daAssegnare, .. tecnici.Select(t => new OpzioneTecnico(t.UtenteId, t.Nome))];
        if (esistente?.TecnicoUtenteId is { } id && Tecnici.All(t => t.Id != id))
        {
            Tecnici.Add(new OpzioneTecnico(id, esistente.Tecnico ?? ""));
        }

        Tecnico = Tecnici.First(t => t.Id == esistente?.TecnicoUtenteId);
        Tipo = Tipi.First(t => t.Codice == (esistente?.Tipo ?? "manutenzione"));
        Descrizione = esistente?.Descrizione ?? "";

        if (esistente is { Inizio: { } inizio, Fine: { } fine })
        {
            var (li, lf) = (FormatoInterventi.Locale(inizio), FormatoInterventi.Locale(fine));
            Data = li.Date;
            OraInizio = li.TimeOfDay;
            OraFine = lf.TimeOfDay;
        }
    }

    public string Titolo => _esistente is null ? Testi.TitoloNuovoIntervento : Testi.TitoloPianificaIntervento;

    public ObservableCollection<OpzioneImpianto> Impianti { get; } = [];

    /// <summary>Aperto dalla scheda di un impianto: l'impianto è già scelto.</summary>
    public bool ImpiantoFisso { get; }

    public IReadOnlyList<Opzione> Tipi { get; } =
        [.. new[] { "manutenzione", "riparazione", "installazione", "sopralluogo", "altro" }.Select(c => new Opzione(c, FormatoInterventi.Tipo(c)))];

    public ObservableCollection<OpzioneTecnico> Tecnici { get; }

    [ObservableProperty]
    public partial OpzioneImpianto? Impianto { get; set; }

    [ObservableProperty]
    public partial string RicercaImpianto { get; set; } = "";

    [ObservableProperty]
    public partial Opzione Tipo { get; set; }

    [ObservableProperty]
    public partial string Descrizione { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConData))]
    public partial DateTime? Data { get; set; }

    public bool ConData => Data is not null;

    [ObservableProperty]
    public partial TimeSpan? OraInizio { get; set; } = new TimeSpan(9, 0, 0);

    [ObservableProperty]
    public partial TimeSpan? OraFine { get; set; } = new TimeSpan(11, 0, 0);

    [ObservableProperty]
    public partial OpzioneTecnico Tecnico { get; set; }

    [RelayCommand]
    private void TogliData() => Data = null;

    /// <summary>Propone gli impianti attivi trovati per cliente, indirizzo, PDR o codice.</summary>
    [RelayCommand]
    private async Task CercaImpiantoAsync()
    {
        if (string.IsNullOrWhiteSpace(RicercaImpianto))
        {
            return;
        }

        var risposta = await Shell.Api.CercaImpiantiAsync(RicercaImpianto, null, "attivo", 1, 10);
        if (risposta.Valore is not { } pagina)
        {
            Errore = risposta.Messaggio;
            return;
        }

        Errore = pagina.Elementi.Count == 0 ? Testi.NessunImpiantoTrovato : null;
        foreach (var i in pagina.Elementi.Where(i => Impianti.All(o => o.Id != i.ImpiantoId)))
        {
            Impianti.Add(new OpzioneImpianto(i.ImpiantoId, $"{i.TipoImpianto} · {i.Cliente} · {i.Comune}"));
        }

        Impianto ??= Impianti.FirstOrDefault();
    }

    protected override async Task<string?> EseguiAsync()
    {
        if (Impianto is not { } impianto)
        {
            return Testi.ScegliImpianto;
        }

        DateTimeOffset? inizio = null;
        DateTimeOffset? fine = null;
        if (Data is { } data)
        {
            if (OraInizio is not { } oraInizio || OraFine is not { } oraFine || oraFine <= oraInizio)
            {
                return Testi.OrarioNonValido;
            }

            if (Tecnico.Id is null)
            {
                return Testi.ScegliTecnico;
            }

            var giorno = DateOnly.FromDateTime(data);
            (inizio, fine) = (FormatoInterventi.DaLocale(giorno, oraInizio), FormatoInterventi.DaLocale(giorno, oraFine));
        }

        var dati = new DatiIntervento(
            impianto.Id, _esistente?.GeneratoreId, Tipo.Codice, string.IsNullOrWhiteSpace(Descrizione) ? null : Descrizione.Trim(),
            inizio, fine, Tecnico.Id, _esistente?.Versione);
        var risposta = _esistente is null
            ? await Shell.Api.CreaInterventoAsync(dati)
            : await Shell.Api.AggiornaInterventoAsync(_esistente.InterventoId, dati);
        _id = risposta.Valore?.Id ?? Guid.Empty;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato(_id);
}

/// <summary>Riga di materiale o manodopera in fase di chiusura; i numeri si scrivono con la virgola.</summary>
public partial class RigaModificabile : ObservableObject
{
    public RigaModificabile(string tipo) => Tipo = tipo;

    public string Tipo { get; }

    /// <summary>Voce del listino da cui viene la riga (solo nei preventivi).</summary>
    public Guid? VoceId { get; init; }

    public bool Manodopera => Tipo == "manodopera";

    public string NomeTipo => Manodopera ? Testi.RigaManodopera : Testi.RigaMateriale;

    [ObservableProperty]
    public partial string Codice { get; set; } = "";

    [ObservableProperty]
    public partial string Descrizione { get; set; } = "";

    [ObservableProperty]
    public partial string Unita { get; set; } = "pz";

    [ObservableProperty]
    public partial string Quantita { get; set; } = "1";

    [ObservableProperty]
    public partial string Prezzo { get; set; } = "";

    [ObservableProperty]
    public partial string Sconto { get; set; } = "";

    [ObservableProperty]
    public partial string Iva { get; set; } = "";

    /// <summary>Importo della riga, se quantità, prezzo e sconto sono numeri.</summary>
    public decimal? Importo() =>
        FormatoImpianti.ProvaDecimale(Quantita, out var q) && FormatoImpianti.ProvaDecimale(Prezzo, out var p) && FormatoImpianti.ProvaDecimale(Sconto, out var s)
            ? decimal.Round((q ?? 0) * (p ?? 0) * (1 - ((s ?? 0) / 100)), 2)
            : null;
}

/// <summary>Chiusura: esito, note e righe insieme, in una sola operazione sul server.</summary>
public partial class ChiusuraInterventoViewModel : DialogoViewModel
{
    private readonly Intervento _intervento;
    private readonly Action<Guid?> _salvato;
    private Guid? _rapportoId;

    // generatori: quelli attivi dell'impianto, per la bozza del rapporto; vuoto se chi chiude non compila rapporti.
    public ChiusuraInterventoViewModel(ShellViewModel shell, Intervento intervento, Action<Guid?> salvato, IReadOnlyList<Generatore>? generatori = null)
        : base(shell)
    {
        ArgumentNullException.ThrowIfNull(intervento);

        _intervento = intervento;
        _salvato = salvato;
        Esito = Esiti[0];
        Generatori = [.. (generatori ?? []).Select(g => new OpzioneGeneratore(g.GeneratoreId, NomeGeneratore(g)))];
        Generatore = Generatori.FirstOrDefault(g => g.Id == intervento.GeneratoreId) ?? (Generatori.Count == 1 ? Generatori[0] : null);
        Modello = Modelli[0];
        Righe.CollectionChanged += (_, e) =>
        {
            foreach (var r in e.NewItems?.OfType<RigaModificabile>() ?? [])
            {
                r.PropertyChanged += RigaCambiata;
            }

            AggiornaTotale();
        };
    }

    public string Titolo => string.Format(CultureInfo.CurrentCulture, Testi.TitoloChiusuraIntervento, FormatoInterventi.Numero(_intervento.Numero, _intervento.Anno));

    public IReadOnlyList<Opzione> Esiti { get; } =
        [.. new[] { "risolto", "da_completare", "non_risolto" }.Select(c => new Opzione(c, FormatoInterventi.Esito(c)))];

    public ObservableCollection<RigaModificabile> Righe { get; } = [];

    [ObservableProperty]
    public partial Opzione Esito { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    /// <summary>Solo per una manutenzione: con esito risolto chiude anche la scadenza del controllo di efficienza.</summary>
    public bool PuoSegnareControllo => _intervento.Tipo == "manutenzione";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostraBozza))]
    public partial bool ControlloEfficienza { get; set; }

    /// <summary>La bozza del rapporto si propone a chi compila i rapporti, se l'impianto ha generatori.</summary>
    public bool PuoCreareBozza => PuoSegnareControllo && Generatori.Count > 0;

    public bool MostraBozza => PuoCreareBozza && ControlloEfficienza;

    public IReadOnlyList<Opzione> Modelli { get; } =
    [
        new("", Testi.NessunaBozzaRapporto),
        new("tipo_1", Testi.ModelloRapportoTipo1),
        new("tipo_2", Testi.ModelloRapportoTipo2),
    ];

    [ObservableProperty]
    public partial Opzione Modello { get; set; }

    public IReadOnlyList<OpzioneGeneratore> Generatori { get; }

    [ObservableProperty]
    public partial OpzioneGeneratore? Generatore { get; set; }

    [ObservableProperty]
    public partial string Totale { get; set; } = FormatoInterventi.Importo(0);

    public bool SenzaRighe => Righe.Count == 0;

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
        var righe = new List<DatiRiga>();
        foreach (var (riga, n) in Righe.Select((r, i) => (r, i + 1)))
        {
            if (!FormatoImpianti.ProvaDecimale(riga.Quantita, out var quantita) || quantita is null
                || !FormatoImpianti.ProvaDecimale(riga.Prezzo, out var prezzo)
                || !FormatoImpianti.ProvaDecimale(riga.Sconto, out var sconto)
                || !FormatoImpianti.ProvaDecimale(riga.Iva, out var iva))
            {
                return string.Format(CultureInfo.CurrentCulture, Testi.RigaNumeriNonValidi, n);
            }

            righe.Add(new DatiRiga(
                riga.Tipo, string.IsNullOrWhiteSpace(riga.Codice) ? null : riga.Codice.Trim(), riga.Descrizione.Trim(),
                riga.Manodopera ? "h" : riga.Unita.Trim(), quantita.Value, prezzo, sconto, iva));
        }

        var modello = MostraBozza && Modello.Codice.Length > 0 ? Modello.Codice : null;
        if (modello is not null && Generatore is null)
        {
            return Testi.ScegliGeneratoreRapporto;
        }

        var risposta = await Shell.Api.ChiudiInterventoAsync(
            _intervento.InterventoId,
            new DatiChiusura(
                _intervento.Versione, Esito.Codice, string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(), righe, PuoSegnareControllo && ControlloEfficienza,
                modello, modello is null ? null : Generatore?.Id));
        _rapportoId = risposta.Valore?.RapportoId;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato(_rapportoId);

    private static string NomeGeneratore(Generatore g) =>
        string.Join(" · ", new[] { g.SiglaCatasto, g.TipoGeneratore, string.Join(" ", new[] { g.Marca, g.Modello }.Where(v => v is not null)) }
            .Where(v => !string.IsNullOrEmpty(v)));

    private void RigaCambiata(object? sender, PropertyChangedEventArgs e) => AggiornaTotale();

    private void AggiornaTotale()
    {
        Totale = FormatoInterventi.Importo(Righe.Sum(r => r.Importo() ?? 0));
        OnPropertyChanged(nameof(SenzaRighe));
    }
}

/// <summary>Annullamento: l'intervento resta, con il motivo, e libera il calendario.</summary>
public partial class AnnullamentoInterventoViewModel(ShellViewModel shell, Intervento intervento, Action salvato) : DialogoViewModel(shell)
{
    public string Titolo => string.Format(CultureInfo.CurrentCulture, Testi.TitoloAnnullaIntervento, FormatoInterventi.Numero(intervento.Numero, intervento.Anno));

    [ObservableProperty]
    public partial string Motivo { get; set; } = "";

    protected override async Task<string?> EseguiAsync()
    {
        if (string.IsNullOrWhiteSpace(Motivo))
        {
            return Testi.MotivoAnnullamentoObbligatorio;
        }

        var risposta = await Shell.Api.AnnullaInterventoAsync(intervento.InterventoId, intervento.Versione, Motivo.Trim());
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => salvato();
}

public sealed record OpzioneGeneratore(Guid Id, string Nome)
{
    public override string ToString() => Nome;
}
