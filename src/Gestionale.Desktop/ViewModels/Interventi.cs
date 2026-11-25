using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>
/// Formati degli interventi. Le ore si mostrano nel fuso italiano, qualunque sia
/// quello del computer: le imprese lavorano in Italia e il server numera per anno
/// con lo stesso fuso.
/// </summary>
public static class FormatoInterventi
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public static TimeZoneInfo Fuso { get; } = TrovaFuso();

    public static DateTime Locale(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Fuso);

    /// <summary>Data e ora italiane, con lo scostamento dall'UTC di quel giorno (ora solare o legale).</summary>
    public static DateTimeOffset DaLocale(DateOnly giorno, TimeSpan ora)
    {
        var locale = giorno.ToDateTime(TimeOnly.FromTimeSpan(ora), DateTimeKind.Unspecified);
        return new DateTimeOffset(locale, Fuso.GetUtcOffset(locale));
    }

    public static DateOnly Oggi(TimeProvider orologio) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(orologio.GetUtcNow(), Fuso).DateTime);

    public static DateOnly Lunedi(DateOnly giorno) => giorno.AddDays(-(((int)giorno.DayOfWeek + 6) % 7));

    public static string Ora(DateTime utc) => Locale(utc).ToString("HH:mm", s_italiano);

    public static string Orario(DateTime? inizio, DateTime? fine) =>
        inizio is { } i && fine is { } f ? $"{Ora(i)}–{Ora(f)}" : Testi.DaPianificare;

    public static string Finestra(DateTime? inizio, DateTime? fine)
    {
        if (inizio is not { } i || fine is not { } f)
        {
            return Testi.DaPianificare;
        }

        var (li, lf) = (Locale(i), Locale(f));
        return li.Date == lf.Date
            ? string.Format(s_italiano, Testi.FinestraStessoGiorno, li.ToString("dddd d MMMM yyyy", s_italiano), li.ToString("HH:mm", s_italiano), lf.ToString("HH:mm", s_italiano))
            : $"{li.ToString("d MMM yyyy HH:mm", s_italiano)} – {lf.ToString("d MMM yyyy HH:mm", s_italiano)}";
    }

    public static string Giorno(DateOnly giorno) => giorno.ToString("ddd d", s_italiano);

    public static string DataOra(DateTime? utc) => utc is { } u ? Locale(u).ToString("d MMMM yyyy, HH:mm", s_italiano) : Testi.NonIndicato;

    public static string Periodo(DateOnly inizio, int giorni)
    {
        var fine = inizio.AddDays(giorni - 1);
        if (giorni == 1)
        {
            return inizio.ToString("dddd d MMMM yyyy", s_italiano);
        }

        if (inizio.Year != fine.Year)
        {
            return $"{inizio.ToString("d MMMM yyyy", s_italiano)} – {fine.ToString("d MMMM yyyy", s_italiano)}";
        }

        return inizio.Month == fine.Month
            ? $"{inizio.Day}–{fine.ToString("d MMMM yyyy", s_italiano)}"
            : $"{inizio.ToString("d MMMM", s_italiano)} – {fine.ToString("d MMMM yyyy", s_italiano)}";
    }

    public static string Numero(int numero, int anno) => string.Format(CultureInfo.CurrentCulture, Testi.NumeroIntervento, numero, anno);

    public static string Tipo(string tipo) => Voce("TipoIntervento_", tipo);

    public static string Stato(string stato) => Voce("StatoIntervento_", stato);

    public static string Esito(string? esito) => esito is null ? Testi.NonIndicato : Voce("EsitoIntervento_", esito);

    public static string Importo(decimal importo) => importo.ToString("C", s_italiano);

    public static string Quantita(decimal quantita) => quantita.ToString("0.###", s_italiano);

    // Ogni codice ha il suo testo nel file di traduzione; un codice sconosciuto si mostra com'è.
    private static string Voce(string prefisso, string codice) =>
        Testi.ResourceManager.GetString(prefisso + codice, Testi.Culture) ?? codice;

    private static TimeZoneInfo TrovaFuso()
    {
        foreach (var id in new[] { "Europe/Rome", "W. Europe Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var fuso))
            {
                return fuso;
            }
        }

        return TimeZoneInfo.Local;
    }
}

/// <summary>Un intervento nel calendario: il colore segue lo stato, che è sempre scritto. Si trascina solo se pianificato e da chi pianifica.</summary>
public sealed record VoceCalendario(Guid Id, string Orario, string Cliente, string Dettaglio, string Stato, bool InCorso, bool Completato, bool Trascinabile);

/// <summary>Un giorno di un tecnico: è anche il punto dove si lascia un intervento trascinato.</summary>
public sealed record CellaCalendario(Guid TecnicoUtenteId, DateOnly Giorno, bool Oggi, IReadOnlyList<VoceCalendario> Interventi);

public sealed record RigaCalendario(string Tecnico, IReadOnlyList<CellaCalendario> Celle);

public sealed record IntestazioneGiorno(string Nome, bool Oggi);

public sealed record VoceDaPianificare(Guid Id, string Numero, string Cliente, string Dettaglio);

/// <summary>
/// Calendario degli interventi per tecnico, a settimana o a giorno. Chi pianifica
/// vede tutti i tecnici e gli interventi ancora da pianificare; il Tecnico vede la
/// propria riga. Gli annullati non occupano il calendario e non si mostrano.
/// </summary>
public partial class CalendarioViewModel(ShellViewModel shell, DateOnly giorno) : ViewModelBase
{
    private const int MassimoInterventi = 200;

    private readonly DateOnly _oggi = FormatoInterventi.Oggi(shell.Orologio);

    public ObservableCollection<IntestazioneGiorno> Giorni { get; } = [];

    public ObservableCollection<RigaCalendario> Righe { get; } = [];

    public ObservableCollection<VoceDaPianificare> DaPianificare { get; } = [];

    public bool PuoPianificare => shell.PianificaInterventi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaSettimana), nameof(VistaGiorno), nameof(NumeroGiorni))]
    public partial bool Settimanale { get; set; } = true;

    public bool VistaSettimana { get => Settimanale; set { if (value) { CambiaVista(true); } } }

    public bool VistaGiorno { get => !Settimanale; set { if (value) { CambiaVista(false); } } }

    public int NumeroGiorni => Settimanale ? 7 : 1;

    [ObservableProperty]
    public partial DateOnly Giorno { get; set; } = giorno;

    [ObservableProperty]
    public partial string Periodo { get; set; } = "";

    [ObservableProperty]
    public partial bool Caricamento { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [ObservableProperty]
    public partial bool SenzaTecnici { get; set; }

    [ObservableProperty]
    public partial bool SenzaDaPianificare { get; set; }

    public DateOnly Inizio => Settimanale ? FormatoInterventi.Lunedi(Giorno) : Giorno;

    public async Task CaricaAsync()
    {
        Caricamento = true;
        Errore = null;
        try
        {
            var inizio = Inizio;
            var giorni = NumeroGiorni;
            Periodo = FormatoInterventi.Periodo(inizio, giorni);

            var da = FormatoInterventi.DaLocale(inizio, TimeSpan.Zero);
            var a = FormatoInterventi.DaLocale(inizio.AddDays(giorni), TimeSpan.Zero);
            var risposta = await shell.Api.CercaInterventiAsync(da, a, null, null, 1, MassimoInterventi);
            if (risposta.Valore is not { } pagina)
            {
                Errore = risposta.Messaggio;
                return;
            }

            var tecnici = await TecniciAsync();
            // Il server manda al Tecnico solo i propri; il filtro qui tiene la riga coerente comunque.
            var interventi = pagina.Elementi
                .Where(i => i.Stato != "annullato" && i.Inizio is not null)
                .Where(i => PuoPianificare || i.TecnicoUtenteId == shell.Profilo.UtenteId)
                .ToList();

            // Anche chi non è più assegnabile (per esempio disattivato) resta con i suoi interventi.
            foreach (var i in interventi.Where(i => i.TecnicoUtenteId is { } id && tecnici.All(t => t.UtenteId != id)))
            {
                tecnici.Add(new TecnicoAssegnabile(i.TecnicoUtenteId!.Value, i.Tecnico ?? ""));
            }

            Giorni.Clear();
            for (var g = 0; g < giorni; g++)
            {
                var data = inizio.AddDays(g);
                Giorni.Add(new IntestazioneGiorno(FormatoInterventi.Giorno(data), data == _oggi));
            }

            Righe.Clear();
            foreach (var tecnico in tecnici)
            {
                var celle = new List<CellaCalendario>();
                for (var g = 0; g < giorni; g++)
                {
                    var data = inizio.AddDays(g);
                    var voci = interventi
                        .Where(i => i.TecnicoUtenteId == tecnico.UtenteId && Tocca(i, data))
                        .OrderBy(i => i.Inizio)
                        .Select(i => Voce(i, PuoPianificare))
                        .ToList();
                    celle.Add(new CellaCalendario(tecnico.UtenteId, data, data == _oggi, voci));
                }

                Righe.Add(new RigaCalendario(tecnico.Nome, celle));
            }

            SenzaTecnici = Righe.Count == 0;

            if (PuoPianificare)
            {
                await CaricaDaPianificareAsync();
            }

            if (pagina.Totale > MassimoInterventi)
            {
                Errore = string.Format(CultureInfo.CurrentCulture, Testi.CalendarioTroncato, MassimoInterventi);
            }
        }
        finally
        {
            Caricamento = false;
        }
    }

    [RelayCommand]
    private Task PrecedenteAsync()
    {
        Giorno = Giorno.AddDays(-NumeroGiorni);
        return CaricaAsync();
    }

    [RelayCommand]
    private Task SuccessivoAsync()
    {
        Giorno = Giorno.AddDays(NumeroGiorni);
        return CaricaAsync();
    }

    [RelayCommand]
    private Task OggiAsync()
    {
        Giorno = _oggi;
        return CaricaAsync();
    }

    [RelayCommand]
    private Task ApriAsync(Guid interventoId) => shell.ApriInterventoAsync(interventoId);

    [RelayCommand]
    private async Task NuovoInterventoAsync()
    {
        if (await shell.TecniciPerDialogoAsync() is { } tecnici)
        {
            shell.MostraDialogo(new EditorInterventoViewModel(shell, null, null, tecnici, id => _ = shell.ApriInterventoAsync(id)));
        }
    }

    /// <summary>
    /// Un intervento lasciato sul giorno di un tecnico. Se era già in calendario
    /// mantiene orario e durata e cambia solo giorno e tecnico; se era da
    /// pianificare si apre il dialogo con giorno e tecnico già scelti, perché
    /// manca l'orario. Una sovrapposizione la rifiuta il server, con il suo messaggio.
    /// </summary>
    public async Task SpostaAsync(Guid interventoId, Guid tecnicoUtenteId, DateOnly giorno)
    {
        if (!PuoPianificare)
        {
            return;
        }

        Errore = null;
        var letto = await shell.Api.InterventoAsync(interventoId);
        if (letto.Valore is not { } intervento)
        {
            Errore = letto.Messaggio;
            return;
        }

        if (intervento.Stato == "da_pianificare")
        {
            var tecnici = await shell.TecniciPerDialogoAsync();
            var editor = new EditorInterventoViewModel(shell, null, intervento, tecnici, salvato => { _ = CaricaAsync(); })
            {
                Data = giorno.ToDateTime(TimeOnly.MinValue),
            };
            editor.Tecnico = editor.Tecnici.FirstOrDefault(t => t.Id == tecnicoUtenteId) ?? editor.Tecnico;
            shell.MostraDialogo(editor);
            return;
        }

        if (intervento is not { Stato: "pianificato", Inizio: { } inizio, Fine: { } fine })
        {
            Errore = string.Format(CultureInfo.CurrentCulture, Testi.SpostamentoNonConsentito, FormatoInterventi.Stato(intervento.Stato).ToLower(CultureInfo.CurrentCulture));
            return;
        }

        var nuovoInizio = FormatoInterventi.DaLocale(giorno, FormatoInterventi.Locale(inizio).TimeOfDay);
        if (intervento.TecnicoUtenteId == tecnicoUtenteId && nuovoInizio.UtcDateTime == DateTime.SpecifyKind(inizio, DateTimeKind.Utc))
        {
            return;
        }

        var dati = new DatiIntervento(
            intervento.ImpiantoId, intervento.GeneratoreId, intervento.Tipo, intervento.Descrizione,
            nuovoInizio, nuovoInizio + (fine - inizio), tecnicoUtenteId, intervento.Versione);
        var risposta = await shell.Api.AggiornaInterventoAsync(interventoId, dati);
        if (!risposta.Riuscita)
        {
            Errore = risposta.Messaggio;
            return;
        }

        await CaricaAsync();
    }

    private void CambiaVista(bool settimanale)
    {
        if (Settimanale == settimanale)
        {
            return;
        }

        Settimanale = settimanale;
        _ = CaricaAsync();
    }

    private async Task<List<TecnicoAssegnabile>> TecniciAsync()
    {
        // Il Tecnico vede solo sé stesso: non serve l'elenco dei colleghi.
        if (!PuoPianificare)
        {
            return [new TecnicoAssegnabile(shell.Profilo.UtenteId, $"{shell.Profilo.Nome} {shell.Profilo.Cognome}")];
        }

        var risposta = await shell.Api.TecniciAsync();
        return risposta.Valore is { } elenco ? [.. elenco] : [];
    }

    private async Task CaricaDaPianificareAsync()
    {
        var risposta = await shell.Api.CercaInterventiAsync(null, null, null, "da_pianificare", 1, 50);
        DaPianificare.Clear();
        foreach (var i in risposta.Valore?.Elementi ?? [])
        {
            DaPianificare.Add(new VoceDaPianificare(
                i.InterventoId, FormatoInterventi.Numero(i.Numero, i.Anno), i.Cliente, $"{FormatoInterventi.Numero(i.Numero, i.Anno)} · {FormatoInterventi.Tipo(i.Tipo)} · {i.Comune}"));
        }

        SenzaDaPianificare = DaPianificare.Count == 0;
    }

    // Un intervento che passa la mezzanotte compare in ogni giorno che tocca.
    private static bool Tocca(RiepilogoIntervento intervento, DateOnly giorno)
    {
        var inizio = DateOnly.FromDateTime(FormatoInterventi.Locale(intervento.Inizio!.Value));
        var fine = FormatoInterventi.Locale(intervento.Fine!.Value);
        var ultimoGiorno = DateOnly.FromDateTime(fine.TimeOfDay == TimeSpan.Zero ? fine.AddTicks(-1) : fine);
        return giorno >= inizio && giorno <= ultimoGiorno;
    }

    private static VoceCalendario Voce(RiepilogoIntervento i, bool puoPianificare) => new(
        i.InterventoId,
        FormatoInterventi.Orario(i.Inizio, i.Fine),
        i.Cliente,
        $"{FormatoInterventi.Tipo(i.Tipo)} · {i.Comune}",
        FormatoInterventi.Stato(i.Stato),
        i.Stato == "in_corso",
        i.Stato == "completato",
        puoPianificare && i.Stato == "pianificato");
}
