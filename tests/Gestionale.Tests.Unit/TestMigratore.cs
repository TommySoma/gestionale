using System.Globalization;
using System.Text.RegularExpressions;
using Gestionale.Migrator;

namespace Gestionale.Tests.Unit;

public sealed partial class TestMigratore
{
    [GeneratedRegex(@"^(?<numero>\d{4})_[a-z0-9_]+\.sql$")]
    private static partial Regex NomeScript();

    [Fact]
    public void Le_migrazioni_sono_incorporate()
    {
        Assert.Contains("0001_schema_sec.sql", Migratore.Script());
    }

    [Fact]
    public void Le_migrazioni_sono_numerate_senza_buchi_ne_doppioni()
    {
        var numeri = Migratore.Script()
            .Select(nome =>
            {
                var corrispondenza = NomeScript().Match(nome);
                Assert.True(corrispondenza.Success, $"Nome non valido: {nome} (atteso NNNN_descrizione.sql)");
                return int.Parse(corrispondenza.Groups["numero"].Value, CultureInfo.InvariantCulture);
            })
            .ToList();

        Assert.Equal(Enumerable.Range(1, numeri.Count), numeri);
    }
}
