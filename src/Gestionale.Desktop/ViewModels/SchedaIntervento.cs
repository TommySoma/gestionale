using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record RigaMostrata(string Tipo, string Descrizione, string Quantita, string Prezzo, string Sconto, string Importo);

/// <summary>
/// Scheda di un intervento: quando, chi, dove, e le azioni che lo stato permette.
/// Pianificare e annullare spetta a chi ha l'ambito su tutto; avviare e chiudere
/// anche al Tecnico, sui propri.
/// </summary>
public partial class SchedaInterventoViewModel(ShellViewModel shell, Guid interventoId, Func<Task>? indietro = null) : ViewModelBase
{
    private Intervento? _intervento;

    public ObservableCollection<CampoScheda> Dati { get; } = [];

    public ObservableCollection<RigaMostrata> Righe { get; } = [];

    public bool PuoAprireImpianto => shell.VedeImpianti;

    [ObservableProperty]
    public partial string Titolo { get; set; } = "";

    [ObservableProperty]
    public partial string Stato { get; set; } = "";

    [ObservableProperty]
    public partial string Sottotitolo { get; set; } = "";

    [ObservableProperty]
    public partial string Impianto { get; set; } = "";

    [ObservableProperty]
    public partial string Indirizzo { get; set; } = "";

    [ObservableProperty]
    public partial string? Descrizione { get; set; }

    [ObservableProperty]
    public partial bool Chiuso { get; set; }

    [ObservableProperty]
    public partial bool Annullato { get; set; }

    [ObservableProperty]
    public partial string Esito { get; set; } = "";

    [ObservableProperty]
    public partial string? NoteChiusura { get; set; }

    [ObservableProperty]
    public partial string? Annullamento { get; set; }

    [ObservableProperty]
    public partial string Totale { get; set; } = "";

    [ObservableProperty]
    public partial bool PuoPianificare { get; set; }

    [ObservableProperty]
    public partial bool PuoAvviare { get; set; }

    [ObservableProperty]
    public partial bool PuoChiudere { get; set; }

    [ObservableProperty]
    public partial bool PuoAnnullare { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool SenzaRighe => Righe.Count == 0;

    public async Task CaricaAsync()
    {
        var risposta = await shell.Api.InterventoAsync(interventoId);
        if (risposta.Valore is not { } intervento)
        {
            Errore = risposta.Messaggio;
            return;
        }

        _intervento = intervento;
        Titolo = string.Format(CultureInfo.CurrentCulture, Testi.TitoloIntervento, FormatoInterventi.Tipo(intervento.Tipo), FormatoInterventi.Numero(intervento.Numero, intervento.Anno));
        Stato = FormatoInterventi.Stato(intervento.Stato);
        Sottotitolo = FormatoInterventi.Finestra(intervento.Inizio, intervento.Fine);
        Impianto = $"{intervento.TipoImpianto} · {intervento.Cliente}";
        Indirizzo = FormatoImpianti.Indirizzo(intervento.Indirizzo, intervento.Civico, intervento.Cap, intervento.Comune, intervento.Provincia);
        Descrizione = intervento.Descrizione;

        Dati.Clear();
        Dati.Add(new(Testi.Tecnico, intervento.Tecnico ?? Testi.NessunTecnico));
        Dati.Add(new(Testi.Generatore, intervento.Generatore ?? Testi.TuttoLImpianto));

        Chiuso = intervento.Stato == "completato";
        Annullato = intervento.Stato == "annullato";
        Esito = FormatoInterventi.Esito(intervento.Esito);
        NoteChiusura = intervento.NoteChiusura;
        Annullamento = Annullato
            ? string.Format(CultureInfo.CurrentCulture, Testi.AnnullatoIlValore, FormatoInterventi.DataOra(intervento.AnnullatoIl), intervento.MotivoAnnullamento)
            : null;
        if (intervento.ChiusoIl is { } chiuso)
        {
            Dati.Add(new(Testi.ChiusoIl, FormatoInterventi.DataOra(chiuso)));
        }

        Righe.Clear();
        foreach (var r in intervento.Righe)
        {
            Righe.Add(new RigaMostrata(
                r.Tipo == "manodopera" ? Testi.RigaManodopera : Testi.RigaMateriale,
                r.Codice is null ? r.Descrizione : $"{r.Codice} · {r.Descrizione}",
                $"{FormatoInterventi.Quantita(r.Quantita)} {r.Unita}",
                FormatoInterventi.Importo(r.PrezzoUnitario),
                r.ScontoPercentuale == 0 ? "" : $"−{FormatoInterventi.Quantita(r.ScontoPercentuale)}%",
                FormatoInterventi.Importo(r.Importo)));
        }

        Totale = FormatoInterventi.Importo(intervento.Totale);

        // Il rapporto di controllo nato da questo intervento, se chi guarda può vederlo.
        RapportoId = shell.VedeRapporti
            ? (await shell.Api.CercaRapportiAsync(null, false, null, interventoId, 1, 1)).Valore?.Elementi.FirstOrDefault()?.RapportoControlloId
            : null;
        OnPropertyChanged(nameof(SenzaRighe));
        OnPropertyChanged(nameof(PuoScaricareRapportino));

        var aperto = intervento.Stato is "da_pianificare" or "pianificato" or "in_corso";
        PuoPianificare = aperto && shell.PianificaInterventi;
        PuoAnnullare = aperto && shell.PianificaInterventi;
        PuoAvviare = intervento.Stato == "pianificato" && shell.ScriveInterventi;
        PuoChiudere = intervento.Stato is "pianificato" or "in_corso" && shell.ScriveInterventi;
    }

    /// <summary>Il rapportino si scarica solo da completato; il salvataggio su disco lo fa la vista.</summary>
    public bool PuoScaricareRapportino => Chiuso;

    /// <summary>Nome proposto per il file, come lo suggerisce anche il server.</summary>
    public string NomeRapportino => _intervento is { } i ? $"rapportino-{i.Anno}-{i.Numero:D4}.pdf" : "rapportino.pdf";

    /// <summary>Scarica il PDF; in caso di errore mostra il messaggio e restituisce null.</summary>
    public async Task<byte[]?> RapportinoAsync()
    {
        var risposta = await shell.Api.RapportinoAsync(interventoId);
        Errore = risposta.Valore is null ? risposta.Messaggio : null;
        return risposta.Valore;
    }

    public void SegnalaRapportinoSalvato(string percorso) =>
        Avviso = string.Format(CultureInfo.CurrentCulture, Testi.RapportinoSalvato, percorso);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; }

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    [RelayCommand]
    private Task IndietroAsync() => indietro?.Invoke() ?? shell.ApriCalendarioAsync(Giorno());

    [RelayCommand]
    private async Task ApriImpiantoAsync()
    {
        if (_intervento is { } intervento && shell.VedeImpianti)
        {
            await shell.ApriImpiantoAsync(intervento.ImpiantoId);
        }
    }

    [RelayCommand]
    private async Task PianificaAsync()
    {
        if (_intervento is { } intervento)
        {
            var tecnici = await shell.TecniciPerDialogoAsync();
            shell.MostraDialogo(new EditorInterventoViewModel(shell, null, intervento, tecnici, salvato => { _ = CaricaAsync(); }));
        }
    }

    [RelayCommand]
    private async Task AvviaAsync()
    {
        if (_intervento is not { } intervento)
        {
            return;
        }

        var risposta = await shell.Api.AvviaInterventoAsync(intervento.InterventoId, intervento.Versione);
        Errore = risposta.Riuscita ? null : risposta.Messaggio;
        await CaricaAsync();
    }

    [RelayCommand]
    private async Task ChiudiAsync()
    {
        if (_intervento is not { } intervento)
        {
            return;
        }

        // A chi compila i rapporti la chiusura propone la bozza: servono i generatori attivi dell'impianto.
        IReadOnlyList<Generatore> generatori = [];
        if (intervento.Tipo == "manutenzione" && shell.Profilo.Ha("rapporti_controllo.scrittura")
            && (await shell.Api.ImpiantoAsync(intervento.ImpiantoId)).Valore is { } impianto)
        {
            generatori = [.. impianto.Generatori.Where(g => g.DismessoIl is null)];
        }

        shell.MostraDialogo(new ChiusuraInterventoViewModel(shell, intervento, rapportoId =>
        {
            BozzaRapportoCreata = rapportoId is not null;
            _ = CaricaAsync();
        }, generatori));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaRapporto))]
    public partial Guid? RapportoId { get; set; }

    public bool HaRapporto => RapportoId is not null;

    [RelayCommand]
    private Task ApriRapportoAsync() => RapportoId is { } id ? shell.ApriRapportoAsync(id) : Task.CompletedTask;

    /// <summary>Dopo la chiusura con il controllo: la bozza del rapporto è pronta da completare.</summary>
    [ObservableProperty]
    public partial bool BozzaRapportoCreata { get; set; }

    [RelayCommand]
    private void AnnullaIntervento()
    {
        if (_intervento is { } intervento)
        {
            shell.MostraDialogo(new AnnullamentoInterventoViewModel(shell, intervento, () => _ = CaricaAsync()));
        }
    }

    // Si torna alla settimana dell'intervento; se non è ancora pianificato, a quella di oggi.
    private DateOnly Giorno() =>
        _intervento?.Inizio is { } inizio
            ? DateOnly.FromDateTime(FormatoInterventi.Locale(inizio))
            : FormatoInterventi.Oggi(shell.Orologio);
}
