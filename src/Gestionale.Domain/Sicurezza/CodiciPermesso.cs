namespace Gestionale.Domain.Sicurezza;

/// <summary>Codici dei permessi, come in <c>sec.permessi</c>. Aggiungili qui quando servono a un endpoint.</summary>
public static class CodiciPermesso
{
    public const string UtentiLettura = "utenti.lettura";
    public const string UtentiScrittura = "utenti.scrittura";
    public const string AuditLettura = "audit.lettura";
    public const string ClientiLettura = "clienti.lettura";
    public const string ClientiScrittura = "clienti.scrittura";
    public const string ClientiAnonimizzazione = "clienti.anonimizzazione";
    public const string ImpiantiLettura = "impianti.lettura";
    public const string ImpiantiScrittura = "impianti.scrittura";
    public const string InterventiLettura = "interventi.lettura";
    public const string InterventiScrittura = "interventi.scrittura";
    public const string DocumentiLettura = "documenti.lettura";
    public const string DocumentiScrittura = "documenti.scrittura";
    public const string ScadenzeLettura = "scadenze.lettura";
    public const string ScadenzeScrittura = "scadenze.scrittura";
}

/// <summary>Estensione di un permesso: su tutto o solo sulle attività assegnate all'utente.</summary>
public enum AmbitoPermesso
{
    Tutti,
    Propri,
}
