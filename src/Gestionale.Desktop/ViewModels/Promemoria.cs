using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>Una scadenza della lista, scelta o no per il promemoria email.</summary>
public partial class SelezioneRiga : ObservableObject
{
    [ObservableProperty]
    public partial bool Scelta { get; set; }
}

public sealed record RigaPromemoria(string Cliente, string Destinatario, string Stato, bool Escluso);

/// <summary>
/// Promemoria email delle scadenze scelte (ADR 0007): l'anteprima dice chi lo riceve e
/// chi no (senza email o senza consenso), mostra il testo e lascia aggiungere una nota.
/// Si invia solo con una conferma esplicita; ogni email partita diventa un richiamo.
/// </summary>
public partial class PromemoriaViewModel(ShellViewModel shell, IReadOnlyList<Guid> scadenze, Action<IReadOnlyList<Promemoria>> inviati)
    : DialogoViewModel(shell)
{
    private IReadOnlyList<Promemoria> _risultati = [];

    public ObservableCollection<RigaPromemoria> Righe { get; } = [];

    [ObservableProperty]
    public partial string Nota { get; set; } = "";

    [ObservableProperty]
    public partial string? Esempio { get; set; }

    [ObservableProperty]
    public partial string Riepilogo { get; set; } = "";

    [ObservableProperty]
    public partial bool NessunoDaInviare { get; set; }

    public async Task CaricaAsync()
    {
        var risposta = await Shell.Api.AnteprimaPromemoriaAsync(scadenze, null);
        if (risposta.Valore is not { } anteprima)
        {
            Errore = risposta.Messaggio;
            NessunoDaInviare = true;
            return;
        }

        Righe.Clear();
        foreach (var p in anteprima)
        {
            Righe.Add(new RigaPromemoria(p.Cliente, p.Email ?? Testi.NessunaEmail, p.Motivo ?? Testi.RiceveIlPromemoria, p.Esclusione is not null));
        }

        var daInviare = anteprima.Count(p => p.Esclusione is null);
        NessunoDaInviare = daInviare == 0;
        Riepilogo = string.Format(CultureInfo.CurrentCulture, Testi.RiepilogoPromemoria, daInviare, anteprima.Count - daInviare);
        Esempio = anteprima.FirstOrDefault(p => p.Esclusione is null) is { } primo ? $"{primo.Oggetto}\n\n{primo.Testo}" : null;
    }

    protected override async Task<string?> EseguiAsync()
    {
        if (NessunoDaInviare)
        {
            return Testi.NessunPromemoriaDaInviare;
        }

        var risposta = await Shell.Api.InviaPromemoriaAsync(scadenze, Vuoto(Nota));
        _risultati = risposta.Valore ?? [];
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => inviati(_risultati);
}
