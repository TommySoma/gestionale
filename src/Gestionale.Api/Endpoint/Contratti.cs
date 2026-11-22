using Gestionale.Application.Autenticazione;

namespace Gestionale.Api.Endpoint;

// Corpi JSON di richieste e risposte. I campi possono arrivare null anche se
// non sono nullable: gli endpoint li controllano prima di usarli.

public sealed record PersonaInstallazione(string Email, string Nome, string Cognome, string Password);

public sealed record RichiestaInstallazione(
    string RagioneSociale,
    string? PartitaIva,
    string Regione,
    PersonaInstallazione Titolare,
    PersonaInstallazione? Amministratore);

public sealed record RispostaStatoInstallazione(bool DaInizializzare);

public sealed record RispostaInstallazione(Guid AziendaId);

public sealed record RichiestaAccesso(string Email, string Password, string? Dispositivo);

public sealed record RichiestaCodice(string Codice, string? Dispositivo);

public sealed record RichiestaRinnovo(string TokenRinnovo, string? Dispositivo);

public sealed record RispostaToken(string TokenAccesso, DateTimeOffset AccessoScadeIl, string TokenRinnovo, DateTimeOffset RinnovoScadeIl)
{
    public static RispostaToken Da(CoppiaToken token) =>
        new(token.TokenAccesso, token.AccessoScadeIl, token.TokenRinnovo, token.RinnovoScadeIl);
}

/// <summary>Esito: <c>completato</c>, <c>mfa_richiesto</c> o <c>mfa_da_configurare</c>.</summary>
public sealed record RispostaAccesso(string Esito, RispostaToken? Token, string? TokenPassaggio);

public sealed record RispostaConfigurazioneMfa(string Segreto, string UriConfigurazione);

/// <summary>I codici di recupero sono mostrati una sola volta: il client deve farli salvare all'utente.</summary>
public sealed record RispostaAttivazioneMfa(IReadOnlyList<string> CodiciRecupero, RispostaToken? Token);

public sealed record RispostaPermesso(string Codice, string Ambito);

public sealed record RispostaProfilo(
    Guid UtenteId,
    Guid AziendaId,
    string Email,
    string Nome,
    string Cognome,
    IReadOnlyList<string> Ruoli,
    IReadOnlyList<RispostaPermesso> Permessi);

public sealed record RichiestaNuovoUtente(string Email, string Nome, string Cognome, string Password, string[] Ruoli);

public sealed record RispostaUtente(Guid UtenteId, string Email, string Nome, string Cognome, string Stato, bool MfaAttivo, IReadOnlyList<string> Ruoli);
