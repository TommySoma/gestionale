using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>Voce di una lookup; Id nullo significa "non indicato".</summary>
public sealed record OpzioneVoce(Guid? Id, string Nome)
{
    public override string ToString() => Nome;

    public static IReadOnlyList<OpzioneVoce> Da(IEnumerable<VoceLookup> voci, Guid? attuale, string? nomeAttuale, bool conNessuna)
    {
        var opzioni = new List<OpzioneVoce>();
        if (conNessuna)
        {
            opzioni.Add(new OpzioneVoce(null, Testi.NonIndicato));
        }

        opzioni.AddRange(voci.Select(v => new OpzioneVoce(v.Id, v.Nome)));

        // Una voce chiusa non si propone più, ma l'impianto che la usa la mostra ancora.
        if (attuale is { } id && opzioni.All(o => o.Id != id))
        {
            opzioni.Add(new OpzioneVoce(id, nomeAttuale ?? id.ToString()));
        }

        return opzioni;
    }
}

/// <summary>Cliente scelto come proprietario o responsabile; Id nullo significa "non indicato".</summary>
public sealed record OpzioneCliente(Guid? Id, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>Nuovo impianto su una sede, oppure modifica di uno esistente.</summary>
public partial class EditorImpiantoViewModel : DialogoViewModel
{
    private readonly Guid _sedeId;
    private readonly Impianto? _esistente;
    private readonly Action<Guid> _salvato;
    private Guid _id;

    public EditorImpiantoViewModel(
        ShellViewModel shell, VociImpianti voci, Guid sedeId, OpzioneCliente clienteSede, Impianto? esistente, Action<Guid> salvato) : base(shell)
    {
        ArgumentNullException.ThrowIfNull(voci);

        _sedeId = sedeId;
        _esistente = esistente;
        _salvato = salvato;

        Tipi = OpzioneVoce.Da(voci.TipiImpianto, esistente?.TipoImpiantoId, esistente?.TipoImpianto, conNessuna: false);
        Combustibili = OpzioneVoce.Da(voci.Combustibili, esistente?.CombustibileId, esistente?.Combustibile, conNessuna: true);

        var nessuno = new OpzioneCliente(null, Testi.NonIndicato);
        Clienti = [nessuno, clienteSede];
        var proprietario = esistente?.ProprietarioClienteId is { } p ? new OpzioneCliente(p, esistente.Proprietario ?? "") : nessuno;
        var responsabile = esistente?.ResponsabileClienteId is { } r ? new OpzioneCliente(r, esistente.Responsabile ?? "") : nessuno;
        foreach (var c in new[] { proprietario, responsabile }.Where(c => !Clienti.Contains(c)))
        {
            Clienti.Add(c);
        }

        Tipo = Tipi.FirstOrDefault(t => t.Id == esistente?.TipoImpiantoId) ?? Tipi.FirstOrDefault();
        Combustibile = Combustibili.First(c => c.Id == esistente?.CombustibileId);
        Stato = Stati.First(s => s.Codice == (esistente?.Stato ?? "attivo"));
        DismessoIl = FormatoImpianti.PerSelettore(esistente?.DismessoIl);
        Anno = esistente?.AnnoInstallazione?.ToString(CultureInfo.InvariantCulture) ?? "";
        Potenza = esistente?.PotenzaUtileKw?.ToString("0.##", CultureInfo.GetCultureInfo("it-IT")) ?? "";
        Pdr = esistente?.Pdr ?? "";
        Note = esistente?.Note ?? "";
        Proprietario = proprietario;
        Responsabile = responsabile;
    }

    public string Titolo => _esistente is null ? Testi.TitoloNuovoImpianto : Testi.TitoloModificaImpianto;

    public IReadOnlyList<OpzioneVoce> Tipi { get; }

    public IReadOnlyList<OpzioneVoce> Combustibili { get; }

    public IReadOnlyList<Opzione> Stati { get; } = [new("attivo", Testi.StatoAttivo), new("dismesso", Testi.StatoDismesso)];

    public ObservableCollection<OpzioneCliente> Clienti { get; }

    public bool PuoCercareClienti => Shell.VedeClienti;

    [ObservableProperty]
    public partial OpzioneVoce? Tipo { get; set; }

    [ObservableProperty]
    public partial OpzioneVoce Combustibile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConDismissione))]
    public partial Opzione Stato { get; set; }

    public bool ConDismissione => Stato.Codice == "dismesso";

    [ObservableProperty]
    public partial DateTime? DismessoIl { get; set; }

    [ObservableProperty]
    public partial string Anno { get; set; }

    [ObservableProperty]
    public partial string Potenza { get; set; }

    [ObservableProperty]
    public partial string Pdr { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial OpzioneCliente Proprietario { get; set; }

    [ObservableProperty]
    public partial OpzioneCliente Responsabile { get; set; }

    [ObservableProperty]
    public partial string RicercaCliente { get; set; } = "";

    /// <summary>Aggiunge alle scelte di proprietario e responsabile i clienti trovati.</summary>
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
    }

    protected override async Task<string?> EseguiAsync()
    {
        if (Tipo?.Id is not { } tipo)
        {
            return Testi.ScegliTipoImpianto;
        }

        int? anno = null;
        if (!string.IsNullOrWhiteSpace(Anno))
        {
            if (!int.TryParse(Anno.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var valore))
            {
                return Testi.AnnoNonNumerico;
            }

            anno = valore;
        }

        if (!FormatoImpianti.ProvaDecimale(Potenza, out var potenza))
        {
            return Testi.PotenzaNonNumerica;
        }

        var dati = new DatiImpianto(
            _sedeId,
            tipo,
            Stato.Codice,
            anno,
            potenza,
            Combustibile.Id,
            Vuoto(Pdr),
            Proprietario.Id,
            Responsabile.Id,
            Vuoto(Note),
            ConDismissione ? FormatoImpianti.DaSelettore(DismessoIl) : null,
            _esistente?.Versione);

        var risposta = _esistente is null
            ? await Shell.Api.CreaImpiantoAsync(dati)
            : await Shell.Api.AggiornaImpiantoAsync(_esistente.ImpiantoId, dati);

        _id = risposta.Valore?.Id ?? Guid.Empty;
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato(_id);
}

/// <summary>Nuovo generatore, oppure modifica (anche la dismissione, con la data) di uno esistente.</summary>
public partial class EditorGeneratoreViewModel : DialogoViewModel
{
    private readonly Guid _impiantoId;
    private readonly Generatore? _esistente;
    private readonly Action _salvato;

    public EditorGeneratoreViewModel(ShellViewModel shell, VociImpianti voci, Guid impiantoId, Generatore? esistente, Action salvato) : base(shell)
    {
        ArgumentNullException.ThrowIfNull(voci);

        _impiantoId = impiantoId;
        _esistente = esistente;
        _salvato = salvato;

        Tipi = OpzioneVoce.Da(voci.TipiGeneratore, esistente?.TipoGeneratoreId, esistente?.TipoGeneratore, conNessuna: false);
        Combustibili = OpzioneVoce.Da(voci.Combustibili, esistente?.CombustibileId, esistente?.Combustibile, conNessuna: true);
        Tipo = Tipi.FirstOrDefault(t => t.Id == esistente?.TipoGeneratoreId) ?? Tipi.FirstOrDefault();
        Combustibile = Combustibili.First(c => c.Id == esistente?.CombustibileId);
        Marca = esistente?.Marca ?? "";
        Modello = esistente?.Modello ?? "";
        Matricola = esistente?.Matricola ?? "";
        Potenza = esistente?.PotenzaKw?.ToString("0.##", CultureInfo.GetCultureInfo("it-IT")) ?? "";
        InstallatoIl = FormatoImpianti.PerSelettore(esistente?.InstallatoIl);
        DismessoIl = FormatoImpianti.PerSelettore(esistente?.DismessoIl);
        Note = esistente?.Note ?? "";
    }

    public string Titolo => _esistente is null ? Testi.TitoloNuovoGeneratore : Testi.TitoloModificaGeneratore;

    public IReadOnlyList<OpzioneVoce> Tipi { get; }

    public IReadOnlyList<OpzioneVoce> Combustibili { get; }

    [ObservableProperty]
    public partial OpzioneVoce? Tipo { get; set; }

    [ObservableProperty]
    public partial OpzioneVoce Combustibile { get; set; }

    [ObservableProperty]
    public partial string Marca { get; set; }

    [ObservableProperty]
    public partial string Modello { get; set; }

    [ObservableProperty]
    public partial string Matricola { get; set; }

    [ObservableProperty]
    public partial string Potenza { get; set; }

    [ObservableProperty]
    public partial DateTime? InstallatoIl { get; set; }

    [ObservableProperty]
    public partial DateTime? DismessoIl { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        if (Tipo?.Id is not { } tipo)
        {
            return Testi.ScegliTipoGeneratore;
        }

        if (!FormatoImpianti.ProvaDecimale(Potenza, out var potenza))
        {
            return Testi.PotenzaNonNumerica;
        }

        var dati = new DatiGeneratore(
            tipo, Vuoto(Marca), Vuoto(Modello), Vuoto(Matricola), potenza, Combustibile.Id,
            FormatoImpianti.DaSelettore(InstallatoIl), FormatoImpianti.DaSelettore(DismessoIl), Vuoto(Note), _esistente?.Versione);

        var risposta = _esistente is null
            ? await Shell.Api.CreaGeneratoreAsync(_impiantoId, dati)
            : await Shell.Api.AggiornaGeneratoreAsync(_impiantoId, _esistente.GeneratoreId, dati);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}

/// <summary>Codice catasto (SIERT, Toscana) o targa (CURIT, Lombardia) dell'impianto.</summary>
public partial class EditorRiferimentoCatastoViewModel : DialogoViewModel
{
    private readonly Guid _impiantoId;
    private readonly RiferimentoCatasto? _esistente;
    private readonly Action _salvato;

    public EditorRiferimentoCatastoViewModel(ShellViewModel shell, Guid impiantoId, RiferimentoCatasto? esistente, Action salvato) : base(shell)
    {
        _impiantoId = impiantoId;
        _esistente = esistente;
        _salvato = salvato;
        Regione = Regioni.First(r => r.Codice == (esistente?.Regione ?? "toscana"));
        Codice = esistente?.Codice ?? "";
        RegistratoIl = FormatoImpianti.PerSelettore(esistente?.RegistratoIl);
    }

    public string Titolo => _esistente is null ? Testi.TitoloNuovoRiferimento : Testi.TitoloModificaRiferimento;

    public IReadOnlyList<Opzione> Regioni { get; } = [new("toscana", Testi.CatastoToscana), new("lombardia", Testi.CatastoLombardia)];

    [ObservableProperty]
    public partial Opzione Regione { get; set; }

    [ObservableProperty]
    public partial string Codice { get; set; }

    [ObservableProperty]
    public partial DateTime? RegistratoIl { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        var dati = new DatiRiferimentoCatasto(Regione.Codice, Codice.Trim(), FormatoImpianti.DaSelettore(RegistratoIl), _esistente?.Versione);
        var risposta = _esistente is null
            ? await Shell.Api.CreaRiferimentoCatastoAsync(_impiantoId, dati)
            : await Shell.Api.AggiornaRiferimentoCatastoAsync(_impiantoId, _esistente.RiferimentoCatastoId, dati);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}
