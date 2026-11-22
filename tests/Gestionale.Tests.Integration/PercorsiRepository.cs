namespace Gestionale.Tests.Integration;

/// <summary>Trova i file del repository partendo dalla cartella dei binari di test.</summary>
internal static class PercorsiRepository
{
    private static readonly Lazy<string> s_radice = new(TrovaRadice);

    public static string Radice => s_radice.Value;

    public static string Combina(params string[] parti) => Path.Combine([Radice, .. parti]);

    private static string TrovaRadice()
    {
        for (var cartella = new DirectoryInfo(AppContext.BaseDirectory); cartella is not null; cartella = cartella.Parent)
        {
            if (File.Exists(Path.Combine(cartella.FullName, "global.json")))
            {
                return cartella.FullName;
            }
        }

        throw new InvalidOperationException("Radice del repository non trovata (manca global.json).");
    }
}
