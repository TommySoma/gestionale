using System.Text.Json;

namespace Gestionale.Desktop.Servizi;

/// <summary>Preferenze non segrete del computer: server, ultima email usata e tema.</summary>
public sealed class Impostazioni
{
    public string IndirizzoServer { get; set; } = "http://localhost:5225";

    public string? UltimaEmail { get; set; }

    /// <summary>Uno dei <see cref="CodiciTema"/>; assente vuol dire come il sistema.</summary>
    public string? Tema { get; set; }
}

public interface IArchivioImpostazioni
{
    Impostazioni Carica();

    void Salva(Impostazioni impostazioni);
}

/// <summary>File JSON nella cartella dei dati dell'utente. Non contiene segreti.</summary>
public sealed class ArchivioImpostazioniSuFile(string percorso) : IArchivioImpostazioni
{
    private static readonly JsonSerializerOptions s_opzioni = new() { WriteIndented = true };

    public static ArchivioImpostazioniSuFile Predefinito() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gestionale impianti", "impostazioni.json"));

    public Impostazioni Carica()
    {
        try
        {
            return File.Exists(percorso)
                ? JsonSerializer.Deserialize<Impostazioni>(File.ReadAllText(percorso)) ?? new Impostazioni()
                : new Impostazioni();
        }
        catch (JsonException)
        {
            // Un file rovinato non deve impedire l'avvio: si ripartono le preferenze.
            return new Impostazioni();
        }
    }

    public void Salva(Impostazioni impostazioni)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        File.WriteAllText(percorso, JsonSerializer.Serialize(impostazioni, s_opzioni));
    }
}

public sealed class ArchivioImpostazioniInMemoria : IArchivioImpostazioni
{
    private Impostazioni _impostazioni = new();

    public Impostazioni Carica() => _impostazioni;

    public void Salva(Impostazioni impostazioni) => _impostazioni = impostazioni;
}
