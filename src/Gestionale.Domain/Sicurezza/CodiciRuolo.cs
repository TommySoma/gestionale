namespace Gestionale.Domain.Sicurezza;

/// <summary>Codici dei ruoli di sistema, come in <c>sec.ruoli</c>.</summary>
public static class CodiciRuolo
{
    public const string Titolare = "titolare";
    public const string Amministrativo = "amministrativo";
    public const string ResponsabileTecnico = "responsabile_tecnico";
    public const string Tecnico = "tecnico";
    public const string SolaLettura = "sola_lettura";
    public const string AmministratoreSistema = "amministratore_sistema";

    public static IReadOnlySet<string> Tutti { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Titolare, Amministrativo, ResponsabileTecnico, Tecnico, SolaLettura, AmministratoreSistema,
    };
}
