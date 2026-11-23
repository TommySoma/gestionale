using System.Data;
using System.Net;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Gestionale.Infrastructure.Database;

/// <summary>
/// Accesso al database come app_api. Ogni unità di lavoro è una transazione che
/// imposta azienda e utente con set_config(..., true), cioè SET LOCAL: i valori
/// valgono solo per quella transazione e non restano sulla connessione quando
/// torna nel pool di Npgsql.
/// </summary>
public sealed class Database(NpgsqlDataSource sorgente)
{
    static Database()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new GestoreIndirizzoIp());
        SqlMapper.AddTypeHandler(new GestoreData());
    }

    public async Task<T> EseguiAsync<T>(
        Guid? aziendaId,
        Guid? utenteId,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lavoro);

        await using var connessione = await sorgente.OpenConnectionAsync(ct);
        await using var transazione = await connessione.BeginTransactionAsync(ct);

        await connessione.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('app.azienda_id', @azienda, true), set_config('app.utente_id', @utente, true)",
            new { azienda = aziendaId?.ToString() ?? "", utente = utenteId?.ToString() ?? "" },
            transazione,
            cancellationToken: ct));

        var risultato = await lavoro(connessione, transazione);
        await transazione.CommitAsync(ct);
        return risultato;
    }

    /// <summary>Indirizzi IP come tipo inet di PostgreSQL.</summary>
    private sealed class GestoreIndirizzoIp : SqlMapper.TypeHandler<IPAddress>
    {
        public override void SetValue(IDbDataParameter parameter, IPAddress? value)
        {
            parameter.Value = (object?)value ?? DBNull.Value;
            if (parameter is NpgsqlParameter npgsql)
            {
                npgsql.NpgsqlDbType = NpgsqlDbType.Inet;
            }
        }

        public override IPAddress? Parse(object value) => value as IPAddress;
    }

    /// <summary>Date senza ora (tipo date di PostgreSQL) come DateOnly.</summary>
    private sealed class GestoreData : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.Value = value;
            if (parameter is NpgsqlParameter npgsql)
            {
                npgsql.NpgsqlDbType = NpgsqlDbType.Date;
            }
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly data => data,
            DateTime momento => DateOnly.FromDateTime(momento),
            _ => throw new InvalidCastException("Valore non convertibile in DateOnly: " + value.GetType()),
        };
    }
}
