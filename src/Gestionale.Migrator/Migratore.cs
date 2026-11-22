using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;

namespace Gestionale.Migrator;

/// <summary>
/// Applica in ordine gli script SQL numerati di /db/migrations, incorporati nell'assembly.
/// Ogni script gira nella propria transazione; quelli già applicati sono registrati
/// nella tabella di journal e non vengono rieseguiti.
/// </summary>
public static class Migratore
{
    private static readonly Assembly s_assembly = typeof(Migratore).Assembly;

    /// <summary>Prefisso delle risorse incorporate che contengono le migrazioni.</summary>
    public const string PrefissoRisorse = "Gestionale.Migrator.migrations.";

    /// <summary>Nomi degli script incorporati, nell'ordine in cui vengono applicati.</summary>
    public static IReadOnlyList<string> Script() =>
        [.. s_assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(PrefissoRisorse, StringComparison.Ordinal)
                        && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n => n[PrefissoRisorse.Length..])
            .Order(StringComparer.Ordinal)];

    /// <summary>Applica le migrazioni mancanti alla stringa di connessione indicata.</summary>
    public static DatabaseUpgradeResult Applica(string stringaConnessione, IUpgradeLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stringaConnessione);

        var motore = DeployChanges.To
            .PostgresqlDatabase(stringaConnessione)
            .WithScriptsEmbeddedInAssembly(s_assembly, n => n.StartsWith(PrefissoRisorse, StringComparison.Ordinal))
            .WithTransactionPerScript()
            // Niente sostituzione di variabili $nome$: gli script sono SQL puro (dollar quoting, hash Argon2id).
            .WithVariablesDisabled()
            .JournalToPostgresqlTable("public", "migrazioni_applicate")
            .LogTo(log ?? new ConsoleUpgradeLog())
            .Build();

        return motore.PerformUpgrade();
    }
}
