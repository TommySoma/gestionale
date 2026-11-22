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
