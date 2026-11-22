using System.Net;

namespace Gestionale.Application.Autenticazione;

/// <summary>Hash delle password (Argon2id in formato PHC).</summary>
public interface IHasherPassword
{
    string Calcola(string password);

    bool Verifica(string password, string hash);
}

/// <summary>Elenco di password note o troppo comuni.</summary>
public interface IElencoPasswordNote
{
    bool Contiene(string password);
}

/// <summary>Codici a tempo (TOTP, RFC 6238).</summary>
public interface IServizioTotp
{
    byte[] NuovoSegreto();

    string InBase32(byte[] segreto);

    string UriConfigurazione(byte[] segreto, string emittente, string account);

    /// <summary>Verifica il codice; se valido restituisce il passo temporale usato.</summary>
    bool Verifica(byte[] segreto, string codice, out long passo);
}

/// <summary>Cifratura simmetrica autenticata dei segreti salvati nel database.</summary>
public interface ICifratoreSegreti
{
    byte[] Cifra(byte[] testo);

    byte[] Decifra(byte[] cifrato);
}

/// <summary>Token firmati: accesso, passaggio intermedio del secondo fattore.</summary>
public interface IEmettitoreToken
{
    TokenAccesso EmettiAccesso(UtenteAutenticato utente);

    string EmettiPassaggioMfa(Guid utenteId, Guid aziendaId, ScopoMfa scopo);
}

/// <summary>Persistenza di utenti, sessioni e audit; ogni metodo è una transazione.</summary>
public interface IRepositoryAccessi
{
    Task<bool> InstallazioneDaInizializzareAsync(CancellationToken ct);

    Task<Guid> InizializzaAsync(DatiInstallazione dati, CancellationToken ct);

    Task<DatiAccesso?> DatiAccessoAsync(string email, CancellationToken ct);

    Task<StatoUtente?> StatoUtenteAsync(Guid aziendaId, Guid utenteId, CancellationToken ct);

    Task<DateTime?> RegistraEsitoAccessoAsync(Guid aziendaId, Guid utenteId, bool riuscito, IPAddress? indirizzo, CancellationToken ct);

    Task<UtenteAutenticato> ProfiloAsync(Guid aziendaId, Guid utenteId, CancellationToken ct);

    Task<bool> RichiedeMfaAsync(Guid aziendaId, Guid utenteId, CancellationToken ct);

    Task SalvaSegretoMfaAsync(Guid aziendaId, Guid utenteId, byte[] segretoCifrato, CancellationToken ct);

    Task<bool> RegistraPassoTotpAsync(Guid aziendaId, Guid utenteId, long passo, CancellationToken ct);

    Task<bool> UsaCodiceRecuperoAsync(Guid aziendaId, Guid utenteId, byte[] codiceHash, CancellationToken ct);

    Task AttivaMfaAsync(Guid aziendaId, Guid utenteId, IReadOnlyList<byte[]> hashCodiciRecupero, CancellationToken ct);

    Task CreaSessioneAsync(Guid aziendaId, Guid utenteId, byte[] tokenHash, DateTime scadeIl, string? dispositivo, IPAddress? indirizzo, CancellationToken ct);

    Task<EsitoRotazione> RuotaSessioneAsync(byte[] tokenHash, byte[] nuovoHash, string? dispositivo, IPAddress? indirizzo, CancellationToken ct);

    Task<int> RevocaSessioneAsync(Guid aziendaId, Guid utenteId, byte[] tokenHash, CancellationToken ct);

    Task RegistraAuditAsync(Guid aziendaId, Guid? utenteId, string azione, IPAddress? indirizzo, CancellationToken ct);

    Task<IReadOnlyList<UtenteElenco>> ElencoUtentiAsync(Guid aziendaId, Guid utenteCorrente, CancellationToken ct);

    Task<Guid?> CreaUtenteAsync(Guid aziendaId, Guid utenteCorrente, NuovoUtente utente, string passwordHash, CancellationToken ct);
}
