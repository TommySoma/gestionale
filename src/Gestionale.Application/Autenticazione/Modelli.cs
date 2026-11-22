using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Autenticazione;

/// <summary>Parametri dell'autenticazione, dalla configurazione del server.</summary>
public sealed class OpzioniAutenticazione
{
    public const string Sezione = "Autenticazione";

    public string Emittente { get; set; } = "gestionale";

    /// <summary>Chiave HMAC dei token, in Base64, almeno 32 byte.</summary>
    public string ChiaveFirma { get; set; } = "";

    /// <summary>Chiave AES-256 per i segreti TOTP, in Base64, esattamente 32 byte.</summary>
    public string ChiaveCifraturaMfa { get; set; } = "";

    /// <summary>Token richiesto per il primo avvio; vuoto disattiva il primo avvio.</summary>
    public string TokenInstallazione { get; set; } = "";

    public int DurataAccessoMinuti { get; set; } = 15;

    public int DurataPassaggioMfaMinuti { get; set; } = 5;

    /// <summary>Durata assoluta di una sessione: i rinnovi non la allungano.</summary>
    public int DurataSessioneOre { get; set; } = 12;

    public int LimiteRichiesteAccessoAlMinuto { get; set; } = 10;
}

public enum ScopoMfa
{
    /// <summary>Password corretta, manca il codice del secondo fattore.</summary>
    Verifica,

    /// <summary>Password corretta, il ruolo richiede un secondo fattore non ancora configurato.</summary>
    Configurazione,
}

public sealed record PermessoEffettivo(string Codice, AmbitoPermesso Ambito);

public sealed record UtenteAutenticato(
    Guid UtenteId,
    Guid AziendaId,
    string Email,
    string Nome,
    string Cognome,
    IReadOnlyList<string> Ruoli,
    IReadOnlyList<PermessoEffettivo> Permessi);

public sealed record TokenAccesso(string Token, DateTimeOffset ScadeIl);

public sealed record CoppiaToken(string TokenAccesso, DateTimeOffset AccessoScadeIl, string TokenRinnovo, DateTimeOffset RinnovoScadeIl);

public sealed record DatiInstallazione(
    string RagioneSociale,
    string? PartitaIva,
    string Regione,
    string TitolareEmail,
    string TitolareNome,
    string TitolareCognome,
    string TitolarePasswordHash,
    string? AmministratoreEmail,
    string? AmministratoreNome,
    string? AmministratoreCognome,
    string? AmministratorePasswordHash);

public sealed class DatiAccesso
{
    public Guid UtenteId { get; init; }

    public Guid AziendaId { get; init; }

    public string PasswordHash { get; init; } = "";

    public string Stato { get; init; } = "";

    public bool MfaAttivo { get; init; }

    public DateTime? BloccatoFinoAl { get; init; }
}

public sealed class StatoUtente
{
    public string Email { get; init; } = "";

    public string Stato { get; init; } = "";

    public bool MfaAttivo { get; init; }

    public byte[]? MfaSegretoCifrato { get; init; }

    public DateTime? BloccatoFinoAl { get; init; }
}

public sealed class EsitoRotazione
{
    public string Esito { get; init; } = "";

    public Guid? UtenteId { get; init; }

    public Guid? AziendaId { get; init; }

    /// <summary>Scadenza assoluta della sessione: i rinnovi non la allungano.</summary>
    public DateTime? ScadeIl { get; init; }
}

public sealed class UtenteElenco
{
    public Guid UtenteId { get; init; }

    public string Email { get; init; } = "";

    public string Nome { get; init; } = "";

    public string Cognome { get; init; } = "";

    public string Stato { get; init; } = "";

    public bool MfaAttivo { get; init; }

    public string[] Ruoli { get; init; } = [];
}

public sealed record NuovoUtente(string Email, string Nome, string Cognome, IReadOnlyList<string> Ruoli);
