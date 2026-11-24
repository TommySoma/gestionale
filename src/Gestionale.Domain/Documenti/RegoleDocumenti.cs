namespace Gestionale.Domain.Documenti;

/// <summary>Motivi per cui un file non è accettato; l'API li traduce in messaggi.</summary>
public enum ErroreDocumento
{
    FileVuoto,
    FileTroppoGrande,
    TipoNonAmmesso,
    DescrizioneTroppoLunga,
}

/// <summary>
/// Regole dei file caricati (decisioni del 24/11/2026): solo JPEG, PNG e PDF, al
/// massimo 20 MB. Il tipo si riconosce dai primi byte del file, non dal nome né
/// da quello che dichiara chi lo invia. Il nome originale si conserva ripulito,
/// ma sul disco il file è rinominato con l'id del documento.
/// </summary>
public static class RegoleDocumenti
{
    public const long DimensioneMassima = 20L * 1024 * 1024;

    public const int LunghezzaMassimaNome = 255;

    public const int LunghezzaMassimaDescrizione = 500;

    /// <summary>Quanti byte iniziali servono per riconoscere il tipo.</summary>
    public const int ByteFirma = 8;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Pdf = "application/pdf";

    private static readonly byte[] s_firmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] s_firmaPdf = "%PDF-"u8.ToArray();

    /// <summary>Tipo MIME riconosciuto dai primi byte, oppure null se non è ammesso.</summary>
    public static string? TipoDaFirma(ReadOnlySpan<byte> inizio) => inizio switch
    {
        [0xFF, 0xD8, 0xFF, ..] => Jpeg,
        _ when inizio.StartsWith(s_firmaPng) => Png,
        _ when inizio.StartsWith(s_firmaPdf) => Pdf,
        _ => null,
    };

    public static string Categoria(string tipoMime) => tipoMime == Pdf ? "documento" : "foto";

    public static string Estensione(string tipoMime) => tipoMime switch
    {
        Jpeg => ".jpg",
        Png => ".png",
        _ => ".pdf",
    };

    /// <summary>
    /// Nome da mostrare e da proporre allo scaricamento: senza cartelle, senza
    /// caratteri di controllo né separatori, al massimo 255 caratteri, con
    /// l'estensione giusta per il tipo riconosciuto.
    /// </summary>
    public static string NomeSicuro(string? nome, string tipoMime)
    {
        var base_ = (nome ?? "").Replace('\\', '/');
        base_ = base_[(base_.LastIndexOf('/') + 1)..];
        base_ = new string([.. base_.Where(c => !char.IsControl(c) && c is not ('"' or ':' or '*' or '?' or '<' or '>' or '|'))]).Trim();

        // Prima si toglie l'estensione dichiarata, poi spazi e punti rimasti: ".png" non diventa "png".
        var estensione = Estensione(tipoMime);
        var senzaEstensione = Path.GetFileNameWithoutExtension(base_).Trim().Trim('.').Trim();
        if (string.IsNullOrWhiteSpace(senzaEstensione))
        {
            senzaEstensione = Categoria(tipoMime);
        }

        var massimo = LunghezzaMassimaNome - estensione.Length;
        return (senzaEstensione.Length > massimo ? senzaEstensione[..massimo] : senzaEstensione) + estensione;
    }
}
