using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record Opzione(string Codice, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>
/// Base dei dialoghi che salvano qualcosa: messaggio d'errore dell'API, stato di
/// invio e chiusura. La validazione vera la fa il server, che spiega cosa correggere.
/// </summary>
public abstract partial class DialogoViewModel(ShellViewModel shell) : ViewModelBase
{
    protected ShellViewModel Shell { get; } = shell;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    [ObservableProperty]
    public partial bool InCorso { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [RelayCommand]
    private void Annulla() => Shell.ChiudiDialogo();

    [RelayCommand]
    private async Task SalvaAsync()
    {
        if (InCorso)
        {
            return;
        }

        InCorso = true;
        Errore = null;
        try
        {
            Errore = await EseguiAsync();
            if (Errore is null)
            {
                Shell.ChiudiDialogo();
                DopoIlSalvataggio();
            }
        }
        finally
        {
            InCorso = false;
        }
    }

    /// <summary>Salva e restituisce null se è andata bene, altrimenti il messaggio da mostrare.</summary>
    protected abstract Task<string?> EseguiAsync();

    protected virtual void DopoIlSalvataggio()
    {
    }

    protected static string? Vuoto(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : valore.Trim();
}

public partial class EditorClienteViewModel : DialogoViewModel
{
    private readonly Cliente? _esistente;
    private readonly Action<Guid> _salvato;
    private Guid _id;

    public EditorClienteViewModel(ShellViewModel shell, Cliente? esistente, Action<Guid> salvato) : base(shell)
    {
        _esistente = esistente;
        _salvato = salvato;
        Tipo = Tipi.First(t => t.Codice == (esistente?.Tipo ?? "privato"));
        Denominazione = esistente?.Denominazione ?? "";
        Nome = esistente?.Nome ?? "";
        Cognome = esistente?.Cognome ?? "";
        CodiceFiscale = esistente?.CodiceFiscale ?? "";
        PartitaIva = esistente?.PartitaIva ?? "";
        Pec = esistente?.Pec ?? "";
        Note = esistente?.Note ?? "";
    }

    public IReadOnlyList<Opzione> Tipi { get; } =
    [
        new("privato", Testi.TipoPrivato),
        new("condominio", Testi.TipoCondominio),
        new("azienda", Testi.TipoAzienda),
    ];

    public string Titolo => _esistente is null ? Testi.TitoloNuovoCliente : Testi.TitoloModificaCliente;

    public bool Nuovo => _esistente is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Privato), nameof(ConDenominazione))]
    public partial Opzione Tipo { get; set; }

    public bool Privato => Tipo.Codice == "privato";

    public bool ConDenominazione => !Privato;

    [ObservableProperty]
    public partial string Denominazione { get; set; }

    [ObservableProperty]
    public partial string Nome { get; set; }

    [ObservableProperty]
    public partial string Cognome { get; set; }

    [ObservableProperty]
    public partial string CodiceFiscale { get; set; }

    [ObservableProperty]
    public partial string PartitaIva { get; set; }

    [ObservableProperty]
    public partial string Pec { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        var dati = new DatiCliente(
            Tipo.Codice,
            Privato ? null : Vuoto(Denominazione),
            Privato ? Vuoto(Nome) : null,
            Privato ? Vuoto(Cognome) : null,
            Vuoto(CodiceFiscale),
            Vuoto(PartitaIva),
            Vuoto(Pec),
            Vuoto(Note),
            _esistente?.Versione);

        var risposta = _esistente is null
            ? await Shell.Api.CreaClienteAsync(dati)
            : await Shell.Api.AggiornaClienteAsync(_esistente.ClienteId, dati);

        _id = risposta.Valore?.Id ?? Guid.Empty;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato(_id);
}

/// <summary>Nuova sede, oppure modifica di una esistente (ad esempio per aggiungere il codice ISTAT).</summary>
public partial class EditorSedeViewModel : DialogoViewModel
{
    private readonly Guid _clienteId;
    private readonly Sede? _esistente;
    private readonly Action _salvata;

    public EditorSedeViewModel(ShellViewModel shell, Guid clienteId, Action salvata, Sede? esistente = null) : base(shell)
    {
        _clienteId = clienteId;
        _esistente = esistente;
        _salvata = salvata;

        Descrizione = esistente?.Descrizione ?? "";
        Indirizzo = esistente?.Indirizzo ?? "";
        Civico = esistente?.Civico ?? "";
        Cap = esistente?.Cap ?? "";
        Comune = esistente?.Comune ?? "";
        Provincia = esistente?.Provincia ?? "";
        CodiceIstat = esistente?.CodiceIstatComune ?? "";
        NoteAccesso = esistente?.NoteAccesso ?? "";
    }

    public string Titolo => _esistente is null ? Testi.TitoloNuovaSede : Testi.TitoloModificaSede;

    [ObservableProperty]
    public partial string Descrizione { get; set; }

    [ObservableProperty]
    public partial string Indirizzo { get; set; }

    [ObservableProperty]
    public partial string Civico { get; set; }

    [ObservableProperty]
    public partial string Cap { get; set; }

    [ObservableProperty]
    public partial string Comune { get; set; }

    [ObservableProperty]
    public partial string Provincia { get; set; }

    [ObservableProperty]
    public partial string CodiceIstat { get; set; }

    [ObservableProperty]
    public partial string NoteAccesso { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        var dati = new DatiSede(
            Vuoto(Descrizione), Indirizzo.Trim(), Vuoto(Civico), Cap.Trim(), Comune.Trim(), Provincia.Trim(), Vuoto(NoteAccesso),
            _esistente?.Versione, Vuoto(CodiceIstat));
        var risposta = _esistente is null
            ? await Shell.Api.CreaSedeAsync(_clienteId, dati)
            : await Shell.Api.AggiornaSedeAsync(_clienteId, _esistente.SedeId, dati);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvata();
}

public partial class EditorContattoViewModel : DialogoViewModel
{
    private readonly Guid _clienteId;
    private readonly Action _salvato;

    public EditorContattoViewModel(ShellViewModel shell, Guid clienteId, Action salvato) : base(shell)
    {
        _clienteId = clienteId;
        _salvato = salvato;
        Tipo = Tipi[1];
    }

    public IReadOnlyList<Opzione> Tipi { get; } =
    [
        new("telefono", Testi.Telefono),
        new("cellulare", Testi.Cellulare),
        new("email", Testi.EmailContatto),
    ];

    [ObservableProperty]
    public partial Opzione Tipo { get; set; }

    [ObservableProperty]
    public partial string Valore { get; set; } = "";

    [ObservableProperty]
    public partial string Etichetta { get; set; } = "";

    [ObservableProperty]
    public partial bool Principale { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        var risposta = await Shell.Api.CreaContattoAsync(_clienteId, new DatiContatto(Tipo.Codice, Valore.Trim(), Vuoto(Etichetta), Principale, null));
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}

public partial class EditorConsensoViewModel : DialogoViewModel
{
    private readonly Guid _clienteId;
    private readonly Action _salvato;

    public EditorConsensoViewModel(ShellViewModel shell, Guid clienteId, IReadOnlyList<FinalitaPrivacy> finalita, Action salvato) : base(shell)
    {
        _clienteId = clienteId;
        _salvato = salvato;
        Finalita = [.. finalita.Select(f => new Opzione(f.Codice, f.Nome))];
        FinalitaScelta = Finalita.FirstOrDefault();
        Fonte = Fonti[0];
    }

    public IReadOnlyList<Opzione> Finalita { get; }

    public IReadOnlyList<Opzione> Fonti { get; } =
    [
        new("modulo_firmato", Testi.FonteModulo),
        new("email", Testi.FonteEmail),
        new("telefono", Testi.FonteTelefono),
        new("di_persona", Testi.FonteDiPersona),
        new("altro", Testi.FonteAltro),
    ];

    [ObservableProperty]
    public partial Opzione? FinalitaScelta { get; set; }

    [ObservableProperty]
    public partial bool Concesso { get; set; } = true;

    [ObservableProperty]
    public partial Opzione Fonte { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    protected override async Task<string?> EseguiAsync()
    {
        if (FinalitaScelta is not { } finalita)
        {
            return Testi.ErroreInatteso;
        }

        var risposta = await Shell.Api.RegistraConsensoAsync(_clienteId, new DatiConsenso(finalita.Codice, Concesso, Fonte.Codice, Vuoto(Note)));
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}

/// <summary>
/// Conferma di un'azione che non si annulla con un clic. Per le azioni irreversibili
/// si indica una parola da scrivere: il pulsante si attiva solo quando coincide.
/// </summary>
public partial class ConfermaViewModel(
    ShellViewModel shell, string titolo, string messaggio, string azione, Func<Task<string?>> esegui, string? parolaConferma = null)
    : DialogoViewModel(shell)
{
    public string Titolo { get; } = titolo;

    public string Messaggio { get; } = messaggio;

    public string Azione { get; } = azione;

    public bool RichiedeParola => parolaConferma is not null;

    public string IstruzioneParola => parolaConferma is null
        ? ""
        : string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.ScriviPerConfermare, parolaConferma);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuoConfermare))]
    public partial string ParolaScritta { get; set; } = "";

    public bool PuoConfermare => parolaConferma is null || string.Equals(ParolaScritta.Trim(), parolaConferma, StringComparison.Ordinal);

    // Il pulsante è già disattivato, ma anche Invio non deve scavalcare la parola.
    protected override Task<string?> EseguiAsync() => PuoConfermare ? esegui() : Task.FromResult<string?>(IstruzioneParola);
}
