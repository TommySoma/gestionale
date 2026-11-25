using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record CampoScheda(string Etichetta, string Valore);

public sealed record RigaGeneratore(Generatore Dati, string Titolo, string Dettaglio, bool Dismesso, string? Dismissione, bool MatricolaDuplicata);

public sealed record RigaRiferimento(RiferimentoCatasto Dati, string Sistema, string Codice, string? Registrazione);

public sealed record RigaStoricoIntervento(Guid Id, string Titolo, string Dettaglio, string Stato);

/// <summary>Scadenza aperta dell'impianto, con urgenza (icona e testo), periodicità e stato del richiamo.</summary>
public sealed record RigaScadenzaImpianto(Scadenza Dati, string Tipo, string Data, string Urgenza, LivelloScadenza Livello, string Dettaglio, string Richiamo)
{
    public bool Scaduta => Livello == LivelloScadenza.Scaduta;

    public bool InScadenza => Livello == LivelloScadenza.InScadenza;

    public bool InRegola => Livello == LivelloScadenza.InRegola;
}

/// <summary>Foto o PDF dell'impianto: il tipo è sempre scritto, non solo indicato dall'icona.</summary>
public sealed record RigaDocumento(Documento Dati, string Nome, string Dettaglio, bool Foto);

/// <summary>
/// Scheda di un impianto: dati tecnici, generatori, riferimenti al catasto e
/// interventi, dal più recente. In alto a destra la prossima azione: le scadenze aperte.
/// </summary>
public partial class SchedaImpiantoViewModel(ShellViewModel shell, Guid impiantoId) : ViewModelBase
{
    /// <summary>Lo stesso limite del server: il file troppo grande non parte nemmeno.</summary>
    public const long DimensioneMassimaDocumento = 20L * 1024 * 1024;

    private Impianto? _impianto;

    public ObservableCollection<CampoScheda> DatiTecnici { get; } = [];

    public ObservableCollection<RigaGeneratore> Generatori { get; } = [];

    public ObservableCollection<RigaRiferimento> Riferimenti { get; } = [];

    public bool PuoModificare => shell.ModificaImpianti;

    public bool PuoAprireCliente => shell.VedeClienti;

    public ObservableCollection<RigaStoricoIntervento> Interventi { get; } = [];

    public bool VedeInterventi => shell.VedeInterventi;

    public bool PuoCreareInterventi => shell.PianificaInterventi;

    public ObservableCollection<RigaScadenzaImpianto> Scadenze { get; } = [];

    public bool VedeScadenze => shell.VedeScadenze;

    public bool GestisceScadenze => shell.GestisceScadenze;

    public bool SenzaScadenze => Caricato && Scadenze.Count == 0;

    public ObservableCollection<RigaDocumento> Documenti { get; } = [];

    public bool VedeDocumenti => shell.Profilo.Ha("documenti.lettura");

    // Il Tecnico carica sugli impianti dei propri interventi (li vede solo se suoi); eliminare spetta all'ufficio.
    public bool CaricaDocumenti => shell.Profilo.Ha("documenti.scrittura");

    public bool EliminaDocumenti => shell.Profilo.Permessi.Any(p => p.Codice == "documenti.scrittura" && p.Ambito == "tutti");

    public bool SenzaDocumenti => Caricato && Documenti.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; }

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    [ObservableProperty]
    public partial string Titolo { get; set; } = "";

    [ObservableProperty]
    public partial string Stato { get; set; } = "";

    [ObservableProperty]
    public partial bool Dismesso { get; set; }

    [ObservableProperty]
    public partial string Indirizzo { get; set; } = "";

    [ObservableProperty]
    public partial string Cliente { get; set; } = "";

    [ObservableProperty]
    public partial bool VociDaVerificare { get; set; }

    [ObservableProperty]
    public partial bool IndirizzoDuplicato { get; set; }

    [ObservableProperty]
    public partial bool Caricato { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool SenzaGeneratori => Caricato && Generatori.Count == 0;

    public bool SenzaRiferimenti => Caricato && Riferimenti.Count == 0;

    public bool SenzaInterventi => Caricato && Interventi.Count == 0;

    public async Task CaricaAsync()
    {
        var risposta = await shell.Api.ImpiantoAsync(impiantoId);
        if (risposta.Valore is not { } impianto)
        {
            Errore = risposta.Messaggio;
            return;
        }

        _impianto = impianto;
        Titolo = impianto.TipoImpianto;
        Stato = FormatoImpianti.Stato(impianto.Stato);
        Dismesso = impianto.Stato == "dismesso";
        Indirizzo = FormatoImpianti.Indirizzo(impianto.Indirizzo, impianto.Civico, impianto.Cap, impianto.Comune, impianto.Provincia);
        Cliente = impianto.Cliente;
        IndirizzoDuplicato = impianto.IndirizzoDuplicato;
        VociDaVerificare = impianto.TipoDaVerificare;

        DatiTecnici.Clear();
        DatiTecnici.Add(new(Testi.AnnoInstallazione, impianto.AnnoInstallazione?.ToString(CultureInfo.InvariantCulture) ?? Testi.NonIndicato));
        DatiTecnici.Add(new(Testi.PotenzaUtile, FormatoImpianti.Potenza(impianto.PotenzaUtileKw)));
        DatiTecnici.Add(new(Testi.Combustibile, impianto.Combustibile ?? Testi.NonIndicato));
        DatiTecnici.Add(new(Testi.Manutenzione, impianto.ManutenzioneMesi is { } mesi ? FormatoScadenze.Periodicita(mesi) : Testi.ManutenzioneNonIndicata));
        DatiTecnici.Add(new(Testi.Pdr, impianto.Pdr ?? Testi.NonIndicato));
        DatiTecnici.Add(new(Testi.Proprietario, impianto.Proprietario ?? Testi.NonIndicato));
        DatiTecnici.Add(new(Testi.Responsabile, impianto.Responsabile ?? Testi.NonIndicato));
        if (impianto.DismessoIl is { } dismesso)
        {
            DatiTecnici.Add(new(Testi.DismessoIl, FormatoImpianti.Data(dismesso)));
        }

        if (impianto.Note is { } note)
        {
            DatiTecnici.Add(new(Testi.Note, note));
        }

        Generatori.Clear();
        foreach (var g in impianto.Generatori)
        {
            var titolo = string.Join(" · ", new[] { g.TipoGeneratore, string.Join(" ", new[] { g.Marca, g.Modello }.Where(v => v is not null)) }
                .Where(v => !string.IsNullOrEmpty(v)));
            var dettaglio = string.Join(" · ", new[]
            {
                g.Matricola is null ? null : string.Format(CultureInfo.CurrentCulture, Testi.MatricolaValore, g.Matricola),
                g.PotenzaKw is null ? null : FormatoImpianti.Potenza(g.PotenzaKw),
                g.Combustibile,
                g.InstallatoIl is null ? null : string.Format(CultureInfo.CurrentCulture, Testi.InstallatoIlValore, FormatoImpianti.Data(g.InstallatoIl)),
            }.Where(v => v is not null));
            var dismissione = g.DismessoIl is null ? null : string.Format(CultureInfo.CurrentCulture, Testi.DismessoIlValore, FormatoImpianti.Data(g.DismessoIl));
            // Il dettaglio può cominciare da "installato il": l'iniziale va maiuscola.
            dettaglio = dettaglio.Length == 0 ? Testi.NessunDettaglio : char.ToUpper(dettaglio[0], CultureInfo.CurrentCulture) + dettaglio[1..];
            Generatori.Add(new RigaGeneratore(g, titolo, dettaglio, g.DismessoIl is not null, dismissione, g.MatricolaDuplicata));
        }

        Riferimenti.Clear();
        foreach (var r in impianto.RiferimentiCatasto)
        {
            var registrazione = r.RegistratoIl is null ? null : string.Format(CultureInfo.CurrentCulture, Testi.RegistratoIlValore, FormatoImpianti.Data(r.RegistratoIl));
            Riferimenti.Add(new RigaRiferimento(r, FormatoImpianti.Regione(r.Regione), r.Codice, registrazione));
        }

        await CaricaScadenzeAsync();
        await CaricaInterventiAsync();
        await CaricaDocumentiAsync();

        Caricato = true;
        OnPropertyChanged(nameof(SenzaGeneratori));
        OnPropertyChanged(nameof(SenzaRiferimenti));
        OnPropertyChanged(nameof(SenzaInterventi));
        OnPropertyChanged(nameof(SenzaDocumenti));
        OnPropertyChanged(nameof(SenzaScadenze));
    }

    [RelayCommand]
    private Task IndietroAsync() => shell.ApriImpiantiAsync();

    [RelayCommand]
    private Task ApriInterventoAsync(Guid interventoId) =>
        shell.ApriInterventoAsync(interventoId, () => shell.ApriImpiantoAsync(impiantoId));

    [RelayCommand]
    private async Task NuovoInterventoAsync()
    {
        if (_impianto is not { } impianto)
        {
            return;
        }

        var tecnici = await shell.TecniciPerDialogoAsync();
        var opzione = new OpzioneImpianto(impianto.ImpiantoId, $"{impianto.TipoImpianto} · {impianto.Cliente} · {impianto.Comune}");
        shell.MostraDialogo(new EditorInterventoViewModel(shell, opzione, null, tecnici, id => _ = shell.ApriInterventoAsync(id, () => shell.ApriImpiantoAsync(impiantoId))));
    }

    /// <summary>Carica un file scelto dall'utente; dimensione e tipo li controlla anche il server.</summary>
    public async Task CaricaDocumentoAsync(byte[] dati, string nome)
    {
        ArgumentNullException.ThrowIfNull(dati);

        Avviso = null;
        if (dati.LongLength > DimensioneMassimaDocumento)
        {
            Errore = string.Format(CultureInfo.CurrentCulture, Testi.DocumentoTroppoGrande, nome);
            return;
        }

        var risposta = await shell.Api.CaricaDocumentoAsync(impiantoId, new ModuloFile(dati, nome, null));
        Errore = risposta.Riuscita ? null : risposta.Messaggio;
        if (risposta.Valore is { } documento)
        {
            Avviso = string.Format(CultureInfo.CurrentCulture, Testi.DocumentoCaricato, documento.NomeOriginale);
        }

        await CaricaDocumentiAsync();
        OnPropertyChanged(nameof(SenzaDocumenti));
    }

    /// <summary>Contenuto del file per aprirlo o salvarlo; null (con il messaggio) se non arriva.</summary>
    public async Task<byte[]?> ContenutoDocumentoAsync(RigaDocumento riga)
    {
        ArgumentNullException.ThrowIfNull(riga);

        var risposta = await shell.Api.ContenutoDocumentoAsync(impiantoId, riga.Dati.DocumentoId);
        Errore = risposta.Valore is null ? risposta.Messaggio : null;
        return risposta.Valore;
    }

    public void SegnalaFileTroppoGrande(string nome)
    {
        Avviso = null;
        Errore = string.Format(CultureInfo.CurrentCulture, Testi.DocumentoTroppoGrande, nome);
    }

    public void SegnalaDocumentoSalvato(string percorso) =>
        Avviso = string.Format(CultureInfo.CurrentCulture, Testi.DocumentoSalvato, percorso);

    [RelayCommand]
    private void EliminaDocumento(RigaDocumento riga) =>
        shell.MostraDialogo(new ConfermaViewModel(
            shell,
            Testi.ConfermaEliminaDocumentoTitolo,
            string.Format(CultureInfo.CurrentCulture, Testi.ConfermaEliminaDocumento, riga.Nome),
            Testi.Elimina,
            async () =>
            {
                var risposta = await shell.Api.EliminaDocumentoAsync(impiantoId, riga.Dati.DocumentoId, riga.Dati.Versione);
                if (!risposta.Riuscita)
                {
                    return risposta.Messaggio;
                }

                await CaricaDocumentiAsync();
                OnPropertyChanged(nameof(SenzaDocumenti));
                return null;
            }));

    [RelayCommand]
    private void RegistraRichiamo(RigaScadenzaImpianto riga) =>
        shell.MostraDialogo(new RichiamoViewModel(shell, riga.Dati.ScadenzaId, impiantoId, riga.Dati.Versione, $"{riga.Tipo} · {Cliente}", null, () => _ = CaricaAsync()));

    [RelayCommand]
    private void CorreggiData(RigaScadenzaImpianto riga) =>
        shell.MostraDialogo(new DataScadenzaViewModel(
            shell, riga.Dati.ScadenzaId, riga.Dati.Versione, riga.Dati.DataScadenza, $"{riga.Tipo} · {Cliente}", () => _ = CaricaAsync()));

    // Solo le aperte: sono la prossima azione. Lo storico resta sul server per i report.
    private async Task CaricaScadenzeAsync()
    {
        Scadenze.Clear();
        if (!VedeScadenze)
        {
            return;
        }

        var oggi = FormatoInterventi.Oggi(shell.Orologio);
        var risposta = await shell.Api.ScadenzeImpiantoAsync(impiantoId);
        foreach (var s in (risposta.Valore ?? []).Where(s => s.Stato == "aperta").OrderBy(s => s.DataScadenza))
        {
            var giorni = s.DataScadenza.DayNumber - oggi.DayNumber;
            var dettaglio = string.Join(" · ", new[]
            {
                FormatoScadenze.Periodicita(s.PeriodicitaMesi),
                s.DataManuale ? Testi.DataCorrettaAMano : null,
                s.RegolaDaVerificare ? Testi.RegolaDaVerificare : null,
            }.Where(v => v is not null));
            Scadenze.Add(new RigaScadenzaImpianto(
                s, FormatoScadenze.Tipo(s.Tipo), FormatoImpianti.Data(s.DataScadenza), FormatoScadenze.Urgenza(giorni),
                FormatoScadenze.Livello(giorni), dettaglio, FormatoScadenze.StatoRichiamo(s.StatoRichiamo)));
        }
    }

    // Foto e documenti, dal più recente.
    private async Task CaricaDocumentiAsync()
    {
        Documenti.Clear();
        if (!VedeDocumenti)
        {
            return;
        }

        var risposta = await shell.Api.DocumentiAsync(impiantoId);
        foreach (var d in risposta.Valore ?? [])
        {
            var foto = d.Categoria == "foto";
            var dettaglio = string.Join(" · ", new[]
            {
                foto ? Testi.Foto : Testi.DocumentoPdf,
                FormatoDocumenti.Dimensione(d.Dimensione),
                FormatoInterventi.DataOra(d.CreatoIl),
                d.CreatoDa,
            }.Where(v => !string.IsNullOrEmpty(v)));
            Documenti.Add(new RigaDocumento(d, d.Descrizione is null ? d.NomeOriginale : $"{d.NomeOriginale} — {d.Descrizione}", dettaglio, foto));
        }
    }

    // Gli ultimi interventi dell'impianto; il Tecnico vede solo i propri.
    private async Task CaricaInterventiAsync()
    {
        Interventi.Clear();
        if (!shell.VedeInterventi)
        {
            return;
        }

        var risposta = await shell.Api.CercaInterventiAsync(null, null, impiantoId, null, 1, 20);
        foreach (var i in risposta.Valore?.Elementi ?? [])
        {
            Interventi.Add(new RigaStoricoIntervento(
                i.InterventoId,
                $"{FormatoInterventi.Tipo(i.Tipo)} {FormatoInterventi.Numero(i.Numero, i.Anno)}",
                i.Tecnico is null ? FormatoInterventi.Finestra(i.Inizio, i.Fine) : $"{FormatoInterventi.Finestra(i.Inizio, i.Fine)} · {i.Tecnico}",
                FormatoInterventi.Stato(i.Stato)));
        }
    }

    [RelayCommand]
    private async Task ApriClienteAsync()
    {
        if (_impianto is { } impianto && shell.VedeClienti)
        {
            await shell.ApriClienteAsync(impianto.ClienteId);
        }
    }

    [RelayCommand]
    private async Task ModificaAsync()
    {
        if (_impianto is not { } impianto || await VociAsync() is not { } voci)
        {
            return;
        }

        shell.MostraDialogo(new EditorImpiantoViewModel(
            shell, voci, impianto.SedeId, new OpzioneCliente(impianto.ClienteId, impianto.Cliente), impianto, id => { _ = CaricaAsync(); }));
    }

    [RelayCommand]
    private void Elimina()
    {
        if (_impianto is not { } impianto)
        {
            return;
        }

        shell.MostraDialogo(new ConfermaViewModel(
            shell,
            Testi.ConfermaEliminaImpiantoTitolo,
            string.Format(CultureInfo.CurrentCulture, Testi.ConfermaEliminaImpianto, Titolo, Indirizzo),
            Testi.EliminaImpianto,
            async () =>
            {
                var risposta = await shell.Api.EliminaImpiantoAsync(impianto.ImpiantoId, impianto.Versione);
                if (!risposta.Riuscita)
                {
                    return risposta.Messaggio;
                }

                await shell.ApriImpiantiAsync();
                return null;
            }));
    }

    [RelayCommand]
    private async Task AggiungiGeneratoreAsync()
    {
        if (await VociAsync() is { } voci)
        {
            shell.MostraDialogo(new EditorGeneratoreViewModel(shell, voci, impiantoId, null, () => _ = CaricaAsync()));
        }
    }

    [RelayCommand]
    private async Task ModificaGeneratoreAsync(RigaGeneratore riga)
    {
        if (await VociAsync() is { } voci)
        {
            shell.MostraDialogo(new EditorGeneratoreViewModel(shell, voci, impiantoId, riga.Dati, () => _ = CaricaAsync()));
        }
    }

    [RelayCommand]
    private void AggiungiRiferimento() =>
        shell.MostraDialogo(new EditorRiferimentoCatastoViewModel(shell, impiantoId, null, () => _ = CaricaAsync()));

    [RelayCommand]
    private void ModificaRiferimento(RigaRiferimento riga) =>
        shell.MostraDialogo(new EditorRiferimentoCatastoViewModel(shell, impiantoId, riga.Dati, () => _ = CaricaAsync()));

    [RelayCommand]
    private async Task EliminaRiferimentoAsync(RigaRiferimento riga)
    {
        var risposta = await shell.Api.EliminaRiferimentoCatastoAsync(impiantoId, riga.Dati.RiferimentoCatastoId, riga.Dati.Versione);
        Errore = risposta.Riuscita ? null : risposta.Messaggio;
        await CaricaAsync();
    }

    private async Task<VociImpianti?> VociAsync()
    {
        var risposta = await shell.VociImpiantiAsync();
        Errore = risposta.Valore is null ? risposta.Messaggio : null;
        return risposta.Valore;
    }
}

public static class FormatoDocumenti
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public static string Dimensione(long byte_) => byte_ switch
    {
        < 1024 => $"{byte_} B",
        < 1024 * 1024 => $"{(byte_ / 1024m).ToString("0", s_italiano)} KB",
        _ => $"{(byte_ / (1024m * 1024)).ToString("0.#", s_italiano)} MB",
    };
}
