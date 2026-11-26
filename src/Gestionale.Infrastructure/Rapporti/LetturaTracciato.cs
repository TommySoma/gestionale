using Dapper;
using Gestionale.Domain.Rapporti;
using Npgsql;

namespace Gestionale.Infrastructure.Rapporti;

/// <summary>Il tracciato SIERT in vigore oggi per un modello, con la sua versione.</summary>
internal static class LetturaTracciato
{
    public static async Task<(string? Versione, IReadOnlyList<CampoTracciato> Campi)> LeggiAsync(
        NpgsqlConnection c, NpgsqlTransaction t, string modello, CancellationToken ct)
    {
        var righe = (await c.QueryAsync<RigaTracciato>(new CommandDefinition(
            """
            SELECT t.versione, t.posizione, t.numero_campo, t.nome, t.tipo, t.lunghezza, t.obbligatorio,
                   t.valori_ammessi, t.formato, t.origine, t.chiave
            FROM norma.tracciati_campi t
            JOIN norma.modelli_rapporto m ON m.modello_rapporto_id = t.modello_rapporto_id
            WHERE m.codice = @modello
              AND t.sistema = 'siert'
              AND t.valido_dal <= (now() AT TIME ZONE 'Europe/Rome')::date
              AND (t.valido_al IS NULL OR t.valido_al >= (now() AT TIME ZONE 'Europe/Rome')::date)
            ORDER BY t.posizione
            """,
            new { modello }, t, cancellationToken: ct))).ToList();

        return (
            righe.FirstOrDefault()?.Versione,
            [.. righe.Select(r => new CampoTracciato(
                r.Posizione, r.NumeroCampo, r.Nome, r.Tipo, r.Lunghezza, r.Obbligatorio, r.ValoriAmmessi, r.Formato, r.Origine, r.Chiave))]);
    }

    private sealed class RigaTracciato
    {
        public string Versione { get; init; } = "";

        public int Posizione { get; init; }

        public int NumeroCampo { get; init; }

        public string Nome { get; init; } = "";

        public string Tipo { get; init; } = "";

        public int? Lunghezza { get; init; }

        public bool Obbligatorio { get; init; }

        public string[]? ValoriAmmessi { get; init; }

        public string? Formato { get; init; }

        public string Origine { get; init; } = "";

        public string Chiave { get; init; } = "";
    }
}
