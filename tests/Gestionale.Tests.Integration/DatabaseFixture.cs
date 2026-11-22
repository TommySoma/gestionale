using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using Gestionale.Migrator;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gestionale.Tests.Integration;

/// <summary>
/// PostgreSQL 18 con pgvector e pgTAP, inizializzato come in produzione:
/// stesso script dei ruoli (deploy/initdb) e migrazioni applicate da app_owner.
/// Le password sono valori di test validi solo per questo container usa e getta.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string NomeDatabase = "gestionale";
    private const string PasswordSuperutente = "test-superutente";
    private const string PasswordOwner = "test-app-owner";
    private const string PasswordApi = "test-app-api";
    private const string PasswordChatbot = "test-app-chatbot";

    /// <summary>Cartella del container in cui vengono copiati i test pgTAP.</summary>
    public const string CartellaPgTap = "/pgtap";

    // Un'immagine per processo, condivisa dalle classi di test che girano in parallelo.
    // Non viene cancellata a fine test: se una classe la cancellasse, le altre fallirebbero.
    private static readonly Lazy<Task<IFutureDockerImage>> s_immagine = new(CostruisciImmagineAsync);

    private PostgreSqlContainer? _container;

    public PostgreSqlContainer Container =>
        _container ?? throw new InvalidOperationException("Container non avviato.");

    public string StringaConnessioneOwner => ConUtente("app_owner", PasswordOwner);

    public string StringaConnessioneApi => ConUtente("app_api", PasswordApi);

    public async ValueTask InitializeAsync()
    {
        var immagine = await s_immagine.Value;

        _container = new PostgreSqlBuilder(immagine)
            .WithDatabase(NomeDatabase)
            .WithUsername("postgres")
            .WithPassword(PasswordSuperutente)
            .WithEnvironment("APP_OWNER_PASSWORD", PasswordOwner)
            .WithEnvironment("APP_API_PASSWORD", PasswordApi)
            .WithEnvironment("APP_CHATBOT_PASSWORD", PasswordChatbot)
            .WithResourceMapping(
                FilePath.Of(PercorsiRepository.Combina("deploy", "initdb", "01-ruoli.sh")),
                DirectoryPath.Of("/docker-entrypoint-initdb.d"))
            .WithResourceMapping(
                DirectoryPath.Of(PercorsiRepository.Combina("db", "tests")),
                DirectoryPath.Of(CartellaPgTap))
            .Build();

        await _container.StartAsync();

        var esito = Migratore.Applica(StringaConnessioneOwner);
        if (!esito.Successful)
        {
            throw new InvalidOperationException("Migrazioni non applicate.", esito.Error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task<IFutureDockerImage> CostruisciImmagineAsync()
    {
        var immagine = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(PercorsiRepository.Combina("db", "tests"))
            .WithDockerfile("Dockerfile")
            .WithName("gestionale-postgres-pgtap:test")
            .WithCleanUp(false)
            .Build();

        await immagine.CreateAsync();
        return immagine;
    }

    private string ConUtente(string utente, string password) =>
        new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Username = utente,
            Password = password,
        }.ConnectionString;
}
