using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gestionale.Domain.Rapporti;

/// <summary>Stati del rapporto di controllo, come nel vincolo ck_rapporti_controllo_stato (ADR 0006).</summary>
public static class StatiRapporto
{
    public const string Bozza = "bozza";
    public const string Convalidato = "convalidato";
    public const string Firmato = "firmato";
    public const string Trasmesso = "trasmesso";
    public const string Scartato = "scartato";
    public const string Annullato = "annullato";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Bozza, Convalidato, Firmato, Trasmesso, Scartato, Annullato,
    };

    /// <summary>Stati da cui un rapporto entra in un invio al catasto.</summary>
    public static IReadOnlySet<string> DaInviare { get; } = new HashSet<string>(StringComparer.Ordinal) { Firmato, Scartato };
}

/// <summary>Modelli di RCEE gestiti in M4: quelli dell'import massivo SIERT.</summary>
public static class ModelliRapporto
{
    public const string Tipo1 = "tipo_1";
    public const string Tipo2 = "tipo_2";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal) { Tipo1, Tipo2 };
}

/// <summary>Da dove viene il valore di una colonna del tracciato (norma.tracciati_campi.origine).</summary>
public static class OriginiCampo
{
    public const string Dati = "dati";
    public const string Rapporto = "rapporto";
    public const string Azienda = "azienda";
    public const string Bollino = "bollino";
}

public static class StatiBollino
{
    public const string Disponibile = "disponibile";
    public const string Assegnato = "assegnato";
    public const string Usato = "usato";
    public const string Annullato = "annullato";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal) { Disponibile, Assegnato, Usato, Annullato };
}

/// <summary>Esiti di una riga del report SIERT, come nel vincolo ck_invii_rapporti_esito.</summary>
public static class EsitiRigaInvio
{
    public const string InAttesa = "in_attesa";
    public const string Importato = "importato";
    public const string ImportatoBozza = "importato_bozza";
    public const string Scartato = "scartato";

    /// <summary>Esiti che si leggono sul report e si registrano.</summary>
    public static IReadOnlySet<string> Registrabili { get; } = new HashSet<string>(StringComparer.Ordinal) { Importato, ImportatoBozza, Scartato };
}

public enum ErroreRapporto
{
    ModelloRapportoNonValido,
    DataControlloMancante,
    DataControlloFutura,
    ImpiantoRapportoNonValido,
    GeneratoreRapportoNonValido,
    InterventoRapportoNonValido,
    InterventoRapportoObbligatorio,
    RapportoGiaPresente,
    DatiRapportoNonOggetto,
    ChiaveDatiSconosciuta,
    ValoreDatiNonValido,
    MotivoAnnullamentoRapportoMancante,
    TipoBollinoNonValido,
    TipoBollinoNonDelModello,
    BollinoNonDisponibile,
    CodiciBolliniMancanti,
    CodiceBollinoNonValido,
    CodiceBollinoGiaRegistrato,
    DataAcquistoBollinoNonValida,
    NoteBollinoTroppoLunghe,
    EsitoInvioNonValido,
    CausaScartoMancante,
    RapportoNonNellInvio,
    NessunRapportoDaInviare,
}

/// <summary>Una colonna del file di invio, come in norma.tracciati_campi.</summary>
public sealed record CampoTracciato(
    int Posizione,
    int NumeroCampo,
    string Nome,
    string Tipo,
    int? Lunghezza,
    bool Obbligatorio,
    IReadOnlyList<string>? ValoriAmmessi,
    string? Formato,
    string Origine,
    string Chiave);

/// <summary>
/// Regole del rapporto di controllo lato API. La validazione campo per campo
/// contro il tracciato la fa il database (norma.valida_rapporto); qui si
/// controlla la forma dei dati in arrivo e si compone il file di invio.
/// </summary>
public static class RegoleRapporti
{
    /// <summary>Il valore più lungo ammesso dal tracciato SIERT v3.0 è di 500 caratteri.</summary>
    public const int LunghezzaMassimaValore = 500;

    public const int LunghezzaMassimaMotivo = 500;

    public const int LunghezzaMassimaNote = 500;

    public const int MassimoBolliniPerAcquisto = 500;

    /// <summary>
    /// Tiene solo le chiavi del tracciato con origine "dati", con valori testo o
    /// numero; toglie gli spazi ai bordi e scarta i valori vuoti. Restituisce gli
    /// errori e le chiavi che li causano.
    /// </summary>
    public static (JsonObject Dati, IReadOnlyList<ErroreRapporto> Errori, IReadOnlyList<string> Chiavi) NormalizzaDati(
        JsonElement? dati, IEnumerable<CampoTracciato> campi)
    {
        ArgumentNullException.ThrowIfNull(campi);

        var normalizzati = new JsonObject();
        if (dati is not { } elemento || elemento.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return (normalizzati, [], []);
        }

        if (elemento.ValueKind != JsonValueKind.Object)
        {
            return (normalizzati, [ErroreRapporto.DatiRapportoNonOggetto], []);
        }

        var ammesse = campi.Where(c => c.Origine == OriginiCampo.Dati).Select(c => c.Chiave).ToHashSet(StringComparer.Ordinal);
        var sconosciute = new List<string>();
        var nonValide = new List<string>();

        foreach (var proprieta in elemento.EnumerateObject())
        {
            if (!ammesse.Contains(proprieta.Name))
            {
                sconosciute.Add(proprieta.Name);
                continue;
            }

            switch (proprieta.Value.ValueKind)
            {
                case JsonValueKind.Null:
                    break;
                case JsonValueKind.String:
                    var testo = proprieta.Value.GetString()!.Trim();
                    if (testo.Length > LunghezzaMassimaValore)
                    {
                        nonValide.Add(proprieta.Name);
                    }
                    else if (testo.Length > 0)
                    {
                        normalizzati[proprieta.Name] = testo;
                    }

                    break;
                case JsonValueKind.Number:
                    normalizzati[proprieta.Name] = JsonNode.Parse(proprieta.Value.GetRawText());
                    break;
                default:
                    nonValide.Add(proprieta.Name);
                    break;
            }
        }

        var errori = new List<ErroreRapporto>();
        if (sconosciute.Count > 0)
        {
            errori.Add(ErroreRapporto.ChiaveDatiSconosciuta);
        }

        if (nonValide.Count > 0)
        {
            errori.Add(ErroreRapporto.ValoreDatiNonValido);
        }

        return (normalizzati, errori, [.. sconosciute, .. nonValide]);
    }

    public static IReadOnlyList<ErroreRapporto> ValidaDataControllo(DateOnly? data, DateOnly oggi) =>
        data switch
        {
            null => [ErroreRapporto.DataControlloMancante],
            { } d when d > oggi => [ErroreRapporto.DataControlloFutura],
            _ => [],
        };

    public static string? MotivoAnnullamento(string? motivo) =>
        string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > LunghezzaMassimaMotivo ? null : motivo.Trim();

    /// <summary>Codici dei bollini acquistati: 13 cifre, le prime quattro l'anno (stessa regola di ck_bollini_codice).</summary>
    public static (IReadOnlyList<string> Codici, IReadOnlyList<ErroreRapporto> Errori, IReadOnlyList<string> NonValidi) NormalizzaCodiciBollini(
        IReadOnlyList<string>? codici)
    {
        var puliti = (codici ?? []).Select(c => (c ?? "").Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (puliti.Count == 0 || puliti.Count > MassimoBolliniPerAcquisto)
        {
            return ([], [ErroreRapporto.CodiciBolliniMancanti], []);
        }

        var nonValidi = puliti.Where(c => !CodiceBollinoValido(c)).ToList();
        return nonValidi.Count > 0 ? ([], [ErroreRapporto.CodiceBollinoNonValido], nonValidi) : (puliti, [], []);
    }

    public static bool CodiceBollinoValido(string codice) =>
        codice.Length == 13 && codice.All(char.IsAsciiDigit) && codice[..2] is "19" or "20";

    /// <summary>Nome del file di import: modello e momento della preparazione, solo caratteri sicuri.</summary>
    public static string NomeFileInvio(string modello, DateTimeOffset momento) =>
        string.Create(CultureInfo.InvariantCulture, $"rcee_{modello}_{momento:yyyyMMdd_HHmmss}.txt");

    /// <summary>
    /// Una riga del file di import massivo: i valori nell'ordine delle colonne,
    /// separati da tabulazioni, vuoti dove manca il valore (guida SIERT v3.0).
    /// </summary>
    public static string RigaInvio(IEnumerable<CampoTracciato> campi, Func<CampoTracciato, string?> valore)
    {
        ArgumentNullException.ThrowIfNull(campi);
        ArgumentNullException.ThrowIfNull(valore);

        return string.Join('\t', campi.OrderBy(c => c.Posizione).Select(c => valore(c) ?? ""));
    }

    /// <summary>
    /// Il file intero: una riga per rapporto, senza intestazione (la guida non ne
    /// prevede: domanda aperta, ADR 0006), righe chiuse da CRLF, UTF-8 senza BOM.
    /// </summary>
    public static byte[] ContenutoInvio(IEnumerable<string> righe)
    {
        ArgumentNullException.ThrowIfNull(righe);

        var testo = new StringBuilder();
        foreach (var riga in righe)
        {
            testo.Append(riga).Append("\r\n");
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(testo.ToString());
    }

    /// <summary>Valore di una chiave dei dati come testo del file: i numeri restano con il punto decimale.</summary>
    public static string? ValoreDati(JsonObject dati, string chiave)
    {
        ArgumentNullException.ThrowIfNull(dati);

        return dati[chiave] switch
        {
            null => null,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            JsonNode n => n.ToJsonString(),
        };
    }
}
