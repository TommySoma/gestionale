using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record RigaImpiantoSede(Guid Id, string Tipo, string Dettaglio, bool Dismesso);

public sealed record RigaSede(Guid Id, int Versione, string Indirizzo, string? Note, IReadOnlyList<RigaImpiantoSede> Impianti, Sede Dati)
{
    public bool SenzaImpianti => Impianti.Count == 0;
}

public sealed record RigaContatto(Guid Id, int Versione, string Tipo, string Valore, string? Etichetta, bool Principale);

public sealed record RigaConsenso(string Finalita, bool Concesso, string Stato, string Dettaglio);

/// <summary>Scheda di un cliente: sedi, contatti, dati fiscali e consensi.</summary>
public partial class SchedaClienteViewModel(ShellViewModel shell, Guid clienteId) : ViewModelBase
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");
    private Cliente? _cliente;
    private IReadOnlyList<FinalitaPrivacy> _finalita = [];

    public ObservableCollection<RigaSede> Sedi { get; } = [];

    public ObservableCollection<RigaContatto> Contatti { get; } = [];

    public ObservableCollection<RigaConsenso> Consensi { get; } = [];

    public bool PuoModificare => shell.ModificaClienti;

    public bool PuoAnonimizzare => shell.AnonimizzaClienti;

    public bool VedeImpianti => shell.VedeImpianti;

    public bool PuoAggiungereImpianti => shell.ModificaImpianti;

    public ObservableCollection<RigaElencoPreventivo> Preventivi { get; } = [];

    public bool VedePreventivi => shell.VedePreventivi;

    public bool PuoCrearePreventivi => shell.GestiscePreventivi;

    public bool SenzaPreventivi => Caricato && Preventivi.Count == 0;

    [ObservableProperty]
    public partial string Nome { get; set; } = "";

    [ObservableProperty]
    public partial string Tipo { get; set; } = "";

    [ObservableProperty]
    public partial string CodiceFiscale { get; set; } = "";

    [ObservableProperty]
    public partial string PartitaIva { get; set; } = "";

    [ObservableProperty]
    public partial string Pec { get; set; } = "";

    [ObservableProperty]
    public partial bool Caricato { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool SenzaSedi => Caricato && Sedi.Count == 0;

    public bool SenzaContatti => Caricato && Contatti.Count == 0;

    public bool SenzaConsensi => Caricato && Consensi.Count == 0;

    public async Task CaricaAsync()
    {
        var risposta = await shell.Api.ClienteAsync(clienteId);
        if (risposta.Valore is not { } cliente)
        {
            Errore = risposta.Messaggio;
            return;
        }

        if (_finalita.Count == 0 && (await shell.Api.FinalitaAsync()).Valore is { } finalita)
        {
            _finalita = finalita;
        }

        // Gli impianti si leggono a parte: chi non ha il permesso non li vede, il resto della scheda sì.
        IReadOnlyList<RiepilogoImpianto> impianti = [];
        if (VedeImpianti)
        {
            var elenco = await shell.Api.CercaImpiantiAsync(null, clienteId, null, 1, 100);
            impianti = elenco.Valore?.Elementi ?? [];
            if (elenco.Valore is null)
            {
                Errore = elenco.Messaggio;
            }
        }

        _cliente = cliente;
        Nome = cliente.Tipo == "privato" ? $"{cliente.Cognome} {cliente.Nome}" : cliente.Denominazione ?? "";
        Tipo = TipiCliente.Nome(cliente.Tipo);
        CodiceFiscale = cliente.CodiceFiscale ?? Testi.NonIndicato;
        PartitaIva = cliente.PartitaIva ?? Testi.NonIndicato;
        Pec = cliente.Pec ?? Testi.NonIndicato;

        Sedi.Clear();
        foreach (var s in cliente.Sedi)
        {
            var indirizzo = $"{s.Indirizzo}{(s.Civico is null ? "" : " " + s.Civico)}, {s.Cap} {s.Comune} ({s.Provincia})";
            var note = s.NoteAccesso is null ? null : string.Format(CultureInfo.CurrentCulture, Testi.NoteAccesso, s.NoteAccesso);
            var impiantiSede = impianti
                .Where(i => i.SedeId == s.SedeId)
                .Select(i => new RigaImpiantoSede(i.ImpiantoId, i.TipoImpianto, DettaglioImpianto(i), i.Stato == "dismesso"))
                .ToList();
            Sedi.Add(new RigaSede(s.SedeId, s.Versione, indirizzo, note, impiantiSede, s));
        }

        Contatti.Clear();
        foreach (var c in cliente.Contatti)
        {
            Contatti.Add(new RigaContatto(c.ContattoId, c.Versione, NomeTipoContatto(c.Tipo), c.Valore, c.Etichetta, c.Principale));
        }

        Consensi.Clear();
        foreach (var c in cliente.Consensi)
        {
            var nome = _finalita.FirstOrDefault(f => f.Codice == c.Finalita)?.Nome ?? c.Finalita;
            var dettaglio = $"{c.RegistratoIl.ToLocalTime().ToString("d MMMM yyyy", s_italiano)} · {NomeFonte(c.Fonte)}";
            Consensi.Add(new RigaConsenso(nome, c.Concesso, c.Concesso ? Testi.Concesso : Testi.NonConcesso, dettaglio));
        }

        await CaricaPreventiviAsync();

        Caricato = true;
        OnPropertyChanged(nameof(SenzaSedi));
        OnPropertyChanged(nameof(SenzaPreventivi));
        OnPropertyChanged(nameof(SenzaContatti));
        OnPropertyChanged(nameof(SenzaConsensi));
    }

    [RelayCommand]
    private Task IndietroAsync() => shell.ApriClientiAsync();

    [RelayCommand]
    private Task ApriPreventivoAsync(RigaElencoPreventivo riga) => shell.ApriPreventivoAsync(riga.Id);

    [RelayCommand]
    private async Task NuovoPreventivoAsync() =>
        shell.MostraDialogo(await EditorPreventivoViewModel.CreaAsync(
            shell, null, new OpzioneCliente(clienteId, Nome), id => _ = shell.ApriPreventivoAsync(id)));

    // I preventivi del cliente, dal più recente; chi non ha il permesso non li vede.
    private async Task CaricaPreventiviAsync()
    {
        Preventivi.Clear();
        if (!VedePreventivi)
        {
            return;
        }

        var risposta = await shell.Api.CercaPreventiviAsync(null, null, clienteId, 1, 20);
        foreach (var p in risposta.Valore?.Elementi ?? [])
        {
            Preventivi.Add(new RigaElencoPreventivo(
                p.PreventivoId,
                FormatoCommerciale.Numero(p.Numero, p.Anno),
                p.Cliente,
                p.Oggetto,
                p.EmessoIl is { } emesso ? FormatoImpianti.Data(emesso) : FormatoInterventi.DataOra(p.CreatoIl),
                FormatoInterventi.Importo(p.Totale),
                FormatoCommerciale.StatoPreventivo(p.Stato, p.Scaduto),
                p.Scaduto));
        }
    }

    [RelayCommand]
    private void Modifica()
    {
        if (_cliente is not null)
        {
            shell.MostraDialogo(new EditorClienteViewModel(shell, _cliente, id => { _ = CaricaAsync(); }));
        }
    }

    [RelayCommand]
    private void Elimina()
    {
        if (_cliente is not { } cliente)
        {
            return;
        }

        shell.MostraDialogo(new ConfermaViewModel(
            shell,
            Testi.ConfermaEliminaTitolo,
            string.Format(CultureInfo.CurrentCulture, Testi.ConfermaElimina, Nome),
            Testi.EliminaCliente,
            async () =>
            {
                var risposta = await shell.Api.EliminaClienteAsync(cliente.ClienteId, cliente.Versione);
                if (!risposta.Riuscita)
                {
                    return risposta.Messaggio;
                }

                await shell.ApriClientiAsync();
                return null;
            }));
    }

    [RelayCommand]
    private void Anonimizza()
    {
        if (_cliente is not { } cliente)
        {
            return;
        }

        shell.MostraDialogo(new ConfermaViewModel(
            shell,
            Testi.ConfermaAnonimizzaTitolo,
            string.Format(CultureInfo.CurrentCulture, Testi.ConfermaAnonimizza, Nome),
            Testi.AnonimizzaCliente,
            async () =>
            {
                var risposta = await shell.Api.AnonimizzaClienteAsync(cliente.ClienteId, cliente.Versione);
                if (!risposta.Riuscita)
                {
                    return risposta.Messaggio;
                }

                await shell.ApriClientiAsync();
                return null;
            },
            Testi.ParolaAnonimizza));
    }

    [RelayCommand]
    private void AggiungiSede() => shell.MostraDialogo(new EditorSedeViewModel(shell, clienteId, () => _ = CaricaAsync()));

    [RelayCommand]
    private void AggiungiContatto() => shell.MostraDialogo(new EditorContattoViewModel(shell, clienteId, () => _ = CaricaAsync()));

    [RelayCommand]
    private void RegistraConsenso() =>
        shell.MostraDialogo(new EditorConsensoViewModel(shell, clienteId, _finalita, () => _ = CaricaAsync()));

    [RelayCommand]
    private void ModificaSede(RigaSede sede) =>
        shell.MostraDialogo(new EditorSedeViewModel(shell, clienteId, () => _ = CaricaAsync(), sede.Dati));

    [RelayCommand]
    private async Task EliminaSedeAsync(RigaSede sede)
    {
        var risposta = await shell.Api.EliminaSedeAsync(clienteId, sede.Id, sede.Versione);
        Errore = risposta.Riuscita ? null : risposta.Messaggio;
        await CaricaAsync();
    }

    [RelayCommand]
    private async Task EliminaContattoAsync(RigaContatto contatto)
    {
        var risposta = await shell.Api.EliminaContattoAsync(clienteId, contatto.Id, contatto.Versione);
        Errore = risposta.Riuscita ? null : risposta.Messaggio;
        await CaricaAsync();
    }

    [RelayCommand]
    private Task ApriImpiantoAsync(RigaImpiantoSede impianto) => shell.ApriImpiantoAsync(impianto.Id);

    [RelayCommand]
    private async Task AggiungiImpiantoAsync(RigaSede sede)
    {
        var voci = await shell.VociImpiantiAsync();
        if (voci.Valore is null)
        {
            Errore = voci.Messaggio;
            return;
        }

        shell.MostraDialogo(new EditorImpiantoViewModel(
            shell, voci.Valore, sede.Id, new OpzioneCliente(clienteId, Nome), null, id => _ = shell.ApriImpiantoAsync(id)));
    }

    private static string DettaglioImpianto(RiepilogoImpianto impianto)
    {
        var parti = new List<string>();
        if (impianto.Stato == "dismesso")
        {
            parti.Add(Testi.StatoDismesso);
        }

        if (impianto.CodiceCatasto is { } codice)
        {
            parti.Add(codice);
        }

        parti.Add(impianto.NumeroGeneratori == 1
            ? Testi.UnGeneratore
            : string.Format(CultureInfo.CurrentCulture, Testi.NumeroGeneratori, impianto.NumeroGeneratori));
        return string.Join(" · ", parti);
    }

    internal static string NomeTipoContatto(string tipo) => tipo switch
    {
        "telefono" => Testi.Telefono,
        "cellulare" => Testi.Cellulare,
        "email" => Testi.EmailContatto,
        _ => tipo,
    };

    internal static string NomeFonte(string fonte) => fonte switch
    {
        "modulo_firmato" => Testi.FonteModulo,
        "email" => Testi.FonteEmail,
        "telefono" => Testi.FonteTelefono,
        "di_persona" => Testi.FonteDiPersona,
        _ => Testi.FonteAltro,
    };
}
