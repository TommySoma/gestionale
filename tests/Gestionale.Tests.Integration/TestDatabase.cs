using Gestionale.Migrator;
using Npgsql;

namespace Gestionale.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class TestDatabase(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    public static TheoryData<string> FilePgTap() =>
        [.. Directory.GetFiles(PercorsiRepository.Combina("db", "tests"), "*.sql")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(FilePgTap))]
    public async Task I_test_pgTAP_passano(string file)
    {
        var esito = await db.Container.ExecAsync(
            ["pg_prove", "--username", "postgres", "--dbname", "gestionale", "--verbose", $"{DatabaseFixture.CartellaPgTap}/{file}"],
            TestContext.Current.CancellationToken);

        TestContext.Current.TestOutputHelper?.WriteLine(esito.Stdout);
        Assert.True(esito.ExitCode == 0, $"pg_prove è fallito per {file}:\n{esito.Stdout}\n{esito.Stderr}");
    }

    [Fact]
    public void Una_seconda_esecuzione_delle_migrazioni_non_applica_nulla()
    {
        var esito = Migratore.Applica(db.StringaConnessioneOwner);

        Assert.True(esito.Successful, esito.Error?.Message);
        Assert.Empty(esito.Scripts);
    }

    [Fact]
    public async Task Lo_schema_sec_appartiene_ad_app_owner()
    {
        await using var connessione = new NpgsqlConnection(db.StringaConnessioneOwner);
        await connessione.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new NpgsqlCommand(
            "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'sec'", connessione);

        var proprietario = await comando.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal("app_owner", proprietario);
    }

    // pgTAP gira in una sola sessione: la concorrenza vera si prova da qui, con
    // transazioni parallele, alcune annullate. I numeri confermati devono essere
    // 1..n senza buchi, perché chi annulla restituisce il numero a chi aspetta.
    [Fact]
    public async Task La_numerazione_sotto_concorrenza_non_lascia_buchi()
    {
        var annulla = TestContext.Current.CancellationToken;
        var aziendaId = Guid.CreateVersion7();
        await using (var connessione = new NpgsqlConnection(db.StringaConnessioneOwner))
        {
            await connessione.OpenAsync(annulla);
            await using var comando = new NpgsqlCommand(
                "INSERT INTO sec.aziende (azienda_id, ragione_sociale, regione) VALUES (@id, 'Prova concorrenza (fittizia)', 'toscana')",
                connessione);
            comando.Parameters.AddWithValue("id", aziendaId);
            await comando.ExecuteNonQueryAsync(annulla);
        }

        var esiti = await Task.WhenAll(Enumerable.Range(1, 12).Select(async i =>
        {
            await using var connessione = new NpgsqlConnection(db.StringaConnessioneOwner);
            await connessione.OpenAsync(annulla);
            await using var transazione = await connessione.BeginTransactionAsync(annulla);
            await using var comando = new NpgsqlCommand(
                "SELECT commerciale.prossimo_numero(@azienda, 'preventivo', 2026)", connessione, transazione);
            comando.Parameters.AddWithValue("azienda", aziendaId);
            var numero = (int)(await comando.ExecuteScalarAsync(annulla))!;
            await Task.Delay(50, annulla);

            var conferma = i % 4 != 0;
            if (conferma)
            {
                await transazione.CommitAsync(annulla);
            }
            else
            {
                await transazione.RollbackAsync(annulla);
            }

            return (numero, conferma);
        }));

        var confermati = esiti.Where(e => e.conferma).Select(e => e.numero).Order().ToArray();
        Assert.Equal(Enumerable.Range(1, 9), confermati);
    }

    [Fact]
    public async Task App_api_senza_azienda_impostata_non_vede_utenti()
    {
        await using var connessione = new NpgsqlConnection(db.StringaConnessioneApi);
        await connessione.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new NpgsqlCommand("SELECT count(*) FROM sec.utenti", connessione);

        var righe = await comando.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0L, righe);
    }
}
