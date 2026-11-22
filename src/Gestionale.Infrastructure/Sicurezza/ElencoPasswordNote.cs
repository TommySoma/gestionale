using Gestionale.Application.Autenticazione;

namespace Gestionale.Infrastructure.Sicurezza;

/// <summary>
/// Password note, da un file di testo incorporato (una per riga, confronto senza
/// maiuscole). L'elenco attuale è un segnaposto con pochi esempi: va sostituito
/// con un elenco pubblico di password violate, dopo averne controllato la licenza.
/// </summary>
public sealed class ElencoPasswordNote : IElencoPasswordNote
{
    private const string NomeRisorsa = "Gestionale.Infrastructure.Sicurezza.password-note.txt";

    private readonly HashSet<string> _password;

    public ElencoPasswordNote()
    {
        using var flusso = typeof(ElencoPasswordNote).Assembly.GetManifestResourceStream(NomeRisorsa)
            ?? throw new InvalidOperationException($"Risorsa {NomeRisorsa} mancante.");
        using var lettore = new StreamReader(flusso);

        _password = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (lettore.ReadLine() is { } riga)
        {
            var valore = riga.Trim();
            if (valore.Length > 0 && !valore.StartsWith('#'))
            {
                _password.Add(valore);
            }
        }
    }

    public int Numero => _password.Count;

    public bool Contiene(string password) => _password.Contains(password);
}
