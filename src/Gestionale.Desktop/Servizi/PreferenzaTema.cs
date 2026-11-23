using Avalonia.Styling;

namespace Gestionale.Desktop.Servizi;

/// <summary>Codici del tema salvati nelle impostazioni.</summary>
public static class CodiciTema
{
    public const string Sistema = "sistema";
    public const string Chiaro = "chiaro";
    public const string Scuro = "scuro";

    public static ThemeVariant Variante(string? codice) => codice switch
    {
        Chiaro => ThemeVariant.Light,
        Scuro => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public static string Normalizza(string? codice) => codice is Chiaro or Scuro ? codice : Sistema;
}

/// <summary>
/// Tema dell'interfaccia: come il sistema operativo (predefinito), chiaro o scuro.
/// La scelta resta nelle impostazioni del computer e vale subito.
/// </summary>
public sealed class PreferenzaTema(IArchivioImpostazioni archivio, Action<ThemeVariant> applica)
{
    public string Corrente => CodiciTema.Normalizza(archivio.Carica().Tema);

    /// <summary>All'avvio: applica la scelta salvata.</summary>
    public void ApplicaSalvato() => applica(CodiciTema.Variante(Corrente));

    public void Imposta(string codice)
    {
        var normalizzato = CodiciTema.Normalizza(codice);
        var impostazioni = archivio.Carica();
        impostazioni.Tema = normalizzato;
        archivio.Salva(impostazioni);
        applica(CodiciTema.Variante(normalizzato));
    }
}
