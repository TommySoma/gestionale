namespace Gestionale.Api.Autenticazione;

/// <summary>Nomi dei claim propri del gestionale.</summary>
internal static class ClaimGestionale
{
    public const string Azienda = "azienda";
    public const string Ruolo = "ruolo";

    /// <summary>Permesso effettivo: <c>codice</c> oppure <c>codice:propri</c>.</summary>
    public const string Permesso = "perm";

    /// <summary>Solo nei token di passaggio del secondo fattore.</summary>
    public const string Scopo = "scopo";

    public const string SuffissoPropri = ":propri";
}

/// <summary>Destinatari dei token: un token di passaggio non vale come token di accesso.</summary>
internal static class Destinatari
{
    public const string Api = "gestionale-api";
    public const string PassaggioMfa = "gestionale-mfa";
}

internal static class SchemiAutenticazione
{
    public const string PassaggioMfa = "PassaggioMfa";
}

internal static class PolicyAutenticazione
{
    public const string VerificaMfa = "verifica-mfa";
    public const string ConfigurazioneMfa = "configurazione-mfa";
    public const string PrefissoPermesso = "permesso:";
}

internal static class ScopiPassaggio
{
    public const string Verifica = "verifica";
    public const string Configurazione = "configurazione";
}

internal static class LimitiRichieste
{
    public const string Accesso = "accesso";
}
