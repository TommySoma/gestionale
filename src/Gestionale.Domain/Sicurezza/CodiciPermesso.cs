namespace Gestionale.Domain.Sicurezza;

/// <summary>Codici dei permessi, come in <c>sec.permessi</c>. Aggiungili qui quando servono a un endpoint.</summary>
public static class CodiciPermesso
{
    public const string UtentiLettura = "utenti.lettura";
    public const string UtentiScrittura = "utenti.scrittura";
    public const string AuditLettura = "audit.lettura";
    public const string ClientiLettura = "clienti.lettura";
    public const string ClientiScrittura = "clienti.scrittura";
}

/// <summary>Estensione di un permesso: su tutto o solo sulle attività assegnate all'utente.</summary>
public enum AmbitoPermesso
{
    Tutti,
    Propri,
}
