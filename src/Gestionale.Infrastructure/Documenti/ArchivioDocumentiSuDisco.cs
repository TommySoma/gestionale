using System.Security.Cryptography;
using Gestionale.Application.Documenti;

namespace Gestionale.Infrastructure.Documenti;

/// <summary>
/// File dei documenti in una cartella del server (un volume, in produzione):
/// <c>&lt;azienda&gt;/&lt;documento&gt;</c>, senza estensione e senza il nome
/// originale, che resta solo nel database. Si scrive prima in un file temporaneo
/// e si rinomina alla fine: un caricamento interrotto non lascia file a metà.
/// </summary>
public sealed class ArchivioDocumentiSuDisco : IArchivioDocumenti
{
    private const int Blocco = 81920;
    private readonly string _radice;

    public ArchivioDocumentiSuDisco(string cartella)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartella);
        _radice = Path.GetFullPath(cartella);
        Directory.CreateDirectory(_radice);
    }

    public async Task<FileSalvato?> SalvaAsync(Guid aziendaId, Guid documentoId, Stream contenuto, long massimo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenuto);

        var percorso = $"{aziendaId:D}/{documentoId:D}";
        var finale = Completo(percorso);
        Directory.CreateDirectory(Path.GetDirectoryName(finale)!);
        var temporaneo = finale + ".parziale";

        using var impronta = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var inizio = new List<byte>(Domain.Documenti.RegoleDocumenti.ByteFirma);
        long totale = 0;
        var buffer = new byte[Blocco];

        try
        {
            await using (var destinazione = new FileStream(temporaneo, FileMode.CreateNew, FileAccess.Write, FileShare.None, Blocco, useAsync: true))
            {
                int letti;
                while ((letti = await contenuto.ReadAsync(buffer, ct)) > 0)
                {
                    totale += letti;
                    if (totale > massimo)
                    {
                        File.Delete(temporaneo);
                        return null;
                    }

                    if (inizio.Count < Domain.Documenti.RegoleDocumenti.ByteFirma)
                    {
                        inizio.AddRange(buffer.Take(Math.Min(letti, Domain.Documenti.RegoleDocumenti.ByteFirma - inizio.Count)));
                    }

                    impronta.AppendData(buffer, 0, letti);
                    await destinazione.WriteAsync(buffer.AsMemory(0, letti), ct);
                }
            }

            File.Move(temporaneo, finale);
        }
        catch
        {
            File.Delete(temporaneo);
            throw;
        }

        return new FileSalvato(percorso, totale, impronta.GetHashAndReset(), [.. inizio]);
    }

    public Stream Apri(string percorso) =>
        new FileStream(Completo(percorso), FileMode.Open, FileAccess.Read, FileShare.Read, Blocco, useAsync: true);

    public void Scarta(string percorso) => File.Delete(Completo(percorso));

    // Il percorso viene dal database, ma si controlla comunque che resti dentro la cartella.
    private string Completo(string percorso)
    {
        var completo = Path.GetFullPath(Path.Combine(_radice, percorso));
        if (!completo.StartsWith(_radice + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Percorso di un documento fuori dalla cartella dei documenti.");
        }

        return completo;
    }
}
