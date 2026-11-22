using System.Net;
using System.Security.Cryptography;
using System.Text;
using Gestionale.Domain.Sicurezza;

namespace Gestionale.Application.Autenticazione;

/// <summary>Esito di un passo di accesso.</summary>
public abstract record EsitoAccesso
{
    /// <summary>Accesso completato: l'utente riceve i token.</summary>
    public sealed record Completato(CoppiaToken Token) : EsitoAccesso;

    /// <summary>Password corretta: serve il codice del secondo fattore.</summary>
    public sealed record MfaRichiesto(string TokenPassaggio) : EsitoAccesso;

    /// <summary>Password corretta: il ruolo obbliga a configurare il secondo fattore.</summary>
    public sealed record MfaDaConfigurare(string TokenPassaggio) : EsitoAccesso;

    /// <summary>
    /// Credenziali o codice non validi, account bloccato o non attivo. Il motivo
    /// non viene detto al client, per non rivelare quali account esistono.
    /// </summary>
    public sealed record Rifiutato : EsitoAccesso;
}

public sealed record ConfigurazioneMfa(string SegretoBase32, string UriConfigurazione);

public sealed record AttivazioneMfa(IReadOnlyList<string> CodiciRecupero, CoppiaToken? Token);

public abstract record EsitoCreazioneUtente
{
    public sealed record Creato(Guid UtenteId) : EsitoCreazioneUtente;

    public sealed record PasswordNonValida(IReadOnlyList<ErrorePassword> Errori) : EsitoCreazioneUtente;

    public sealed record RuoloSconosciuto(string Ruolo) : EsitoCreazioneUtente;

    public sealed record RuoloNonAssegnabile(string Ruolo) : EsitoCreazioneUtente;

    public sealed record SenzaRuoli : EsitoCreazioneUtente;

    public sealed record EmailInUso : EsitoCreazioneUtente;
}

public abstract record EsitoInstallazione
{
    public sealed record Completata(Guid AziendaId) : EsitoInstallazione;

    public sealed record GiaInizializzata : EsitoInstallazione;

    public sealed record PasswordNonValida(string Email, IReadOnlyList<ErrorePassword> Errori) : EsitoInstallazione;
}

/// <summary>
/// Orchestrazione del login: password, secondo fattore, sessioni di rinnovo,
/// primo avvio e creazione degli utenti. La logica transazionale vive nelle
/// funzioni SQL; qui stanno le validazioni e l'ordine dei passi.
/// </summary>
public sealed class ServizioAutenticazione(
    IRepositoryAccessi repository,
    IHasherPassword hasher,
    IElencoPasswordNote passwordNote,
    IServizioTotp totp,
    ICifratoreSegreti cifratore,
    IEmettitoreToken emettitore,
    OpzioniAutenticazione opzioni,
    TimeProvider orologio)
{
    private const int NumeroCodiciRecupero = 10;
    private const string AlfabetoCodiciRecupero = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const string EmittenteTotp = "Gestionale impianti";

    // Hash calcolato una volta: verificarlo per email inesistenti rende i tempi
    // di risposta simili a quelli di un'email esistente.
    private readonly Lazy<string> _hashFittizio = new(() => hasher.Calcola(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))));

    public Task<bool> InstallazioneDaInizializzareAsync(CancellationToken ct) =>
        repository.InstallazioneDaInizializzareAsync(ct);

    public async Task<EsitoInstallazione> InstallaAsync(
        DatiInstallazione dati, string passwordTitolare, string? passwordAmministratore, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dati);

        if (!await repository.InstallazioneDaInizializzareAsync(ct))
        {
            return new EsitoInstallazione.GiaInizializzata();
        }

        var erroriTitolare = ValidaPassword(passwordTitolare, dati.TitolareEmail);
        if (erroriTitolare.Count > 0)
        {
            return new EsitoInstallazione.PasswordNonValida(dati.TitolareEmail, erroriTitolare);
        }

        var completi = dati with { TitolarePasswordHash = hasher.Calcola(passwordTitolare) };

        if (dati.AmministratoreEmail is not null
            && !string.Equals(dati.AmministratoreEmail.Trim(), dati.TitolareEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var erroriAmministratore = ValidaPassword(passwordAmministratore ?? "", dati.AmministratoreEmail);
            if (erroriAmministratore.Count > 0)
            {
                return new EsitoInstallazione.PasswordNonValida(dati.AmministratoreEmail, erroriAmministratore);
            }

            completi = completi with { AmministratorePasswordHash = hasher.Calcola(passwordAmministratore!) };
        }
        else
        {
            completi = completi with
            {
                AmministratoreEmail = null,
                AmministratoreNome = null,
                AmministratoreCognome = null,
                AmministratorePasswordHash = null,
            };
        }

        try
        {
            return new EsitoInstallazione.Completata(await repository.InizializzaAsync(completi, ct));
        }
        catch (InstallazioneGiaInizializzataException)
        {
            return new EsitoInstallazione.GiaInizializzata();
        }
    }

    public async Task<EsitoAccesso> AccediAsync(string email, string password, string? dispositivo, IPAddress? indirizzo, CancellationToken ct)
    {
        var dati = await repository.DatiAccessoAsync(email, ct);

        if (dati is null)
        {
            hasher.Verifica(password, _hashFittizio.Value);
            return new EsitoAccesso.Rifiutato();
        }

        if (dati.Stato != "attivo" || Bloccato(dati.BloccatoFinoAl))
        {
            await repository.RegistraAuditAsync(dati.AziendaId, dati.UtenteId, "accesso.rifiutato_account_non_disponibile", indirizzo, ct);
            return new EsitoAccesso.Rifiutato();
        }

        if (!hasher.Verifica(password, dati.PasswordHash))
        {
            await repository.RegistraEsitoAccessoAsync(dati.AziendaId, dati.UtenteId, riuscito: false, indirizzo, ct);
            return new EsitoAccesso.Rifiutato();
        }

        if (dati.MfaAttivo)
        {
            return new EsitoAccesso.MfaRichiesto(emettitore.EmettiPassaggioMfa(dati.UtenteId, dati.AziendaId, ScopoMfa.Verifica));
        }

        if (await repository.RichiedeMfaAsync(dati.AziendaId, dati.UtenteId, ct))
        {
            return new EsitoAccesso.MfaDaConfigurare(emettitore.EmettiPassaggioMfa(dati.UtenteId, dati.AziendaId, ScopoMfa.Configurazione));
        }

        return new EsitoAccesso.Completato(await CompletaAccessoAsync(dati.AziendaId, dati.UtenteId, dispositivo, indirizzo, ct));
    }

    public async Task<EsitoAccesso> VerificaMfaAsync(
        Guid aziendaId, Guid utenteId, string codice, string? dispositivo, IPAddress? indirizzo, CancellationToken ct)
    {
        var stato = await repository.StatoUtenteAsync(aziendaId, utenteId, ct);

        if (stato is null || stato.Stato != "attivo" || Bloccato(stato.BloccatoFinoAl) || !stato.MfaAttivo || stato.MfaSegretoCifrato is null)
        {
            return new EsitoAccesso.Rifiutato();
        }

        if (!await CodiceValidoAsync(aziendaId, utenteId, stato.MfaSegretoCifrato, codice, ammettiRecupero: true, ct))
        {
            await repository.RegistraEsitoAccessoAsync(aziendaId, utenteId, riuscito: false, indirizzo, ct);
            return new EsitoAccesso.Rifiutato();
        }

        return new EsitoAccesso.Completato(await CompletaAccessoAsync(aziendaId, utenteId, dispositivo, indirizzo, ct));
    }

    /// <summary>Genera un nuovo segreto TOTP da confermare; null se il secondo fattore è già attivo.</summary>
    public async Task<ConfigurazioneMfa?> ConfiguraMfaAsync(Guid aziendaId, Guid utenteId, CancellationToken ct)
    {
        var stato = await repository.StatoUtenteAsync(aziendaId, utenteId, ct);
        if (stato is null || stato.MfaAttivo)
        {
            return null;
        }

        var segreto = totp.NuovoSegreto();
        await repository.SalvaSegretoMfaAsync(aziendaId, utenteId, cifratore.Cifra(segreto), ct);

        return new ConfigurazioneMfa(totp.InBase32(segreto), totp.UriConfigurazione(segreto, EmittenteTotp, stato.Email));
    }

    /// <summary>
    /// Conferma il secondo fattore con un codice valido e genera i codici di recupero.
    /// Se <paramref name="completaAccesso"/> è vero, chiude anche il login.
    /// Restituisce null se il codice non è valido.
    /// </summary>
    public async Task<AttivazioneMfa?> AttivaMfaAsync(
        Guid aziendaId, Guid utenteId, string codice, bool completaAccesso, string? dispositivo, IPAddress? indirizzo, CancellationToken ct)
    {
        var stato = await repository.StatoUtenteAsync(aziendaId, utenteId, ct);
        if (stato is null || stato.MfaAttivo || stato.MfaSegretoCifrato is null)
        {
            return null;
        }

        if (!await CodiceValidoAsync(aziendaId, utenteId, stato.MfaSegretoCifrato, codice, ammettiRecupero: false, ct))
        {
            await repository.RegistraEsitoAccessoAsync(aziendaId, utenteId, riuscito: false, indirizzo, ct);
            return null;
        }

        var codici = Enumerable.Range(0, NumeroCodiciRecupero).Select(_ => NuovoCodiceRecupero()).ToList();
        await repository.AttivaMfaAsync(aziendaId, utenteId, [.. codici.Select(HashCodiceRecupero)], ct);

        var token = completaAccesso ? await CompletaAccessoAsync(aziendaId, utenteId, dispositivo, indirizzo, ct) : null;
        return new AttivazioneMfa(codici, token);
    }

    /// <summary>Ruota il token di rinnovo; null se non è più valido.</summary>
    public async Task<CoppiaToken?> RinnovaAsync(string tokenRinnovo, string? dispositivo, IPAddress? indirizzo, CancellationToken ct)
    {
        var nuovoToken = NuovoTokenRinnovo();
        var esito = await repository.RuotaSessioneAsync(HashToken(tokenRinnovo), HashToken(nuovoToken), dispositivo, indirizzo, ct);

        if (esito.Esito == "riuso" && esito.AziendaId is { } aziendaRiuso)
        {
            await repository.RegistraAuditAsync(aziendaRiuso, esito.UtenteId, "sessione.riuso_rilevato", indirizzo, ct);
        }

        if (esito.Esito != "ok" || esito.AziendaId is not { } aziendaId || esito.UtenteId is not { } utenteId || esito.ScadeIl is not { } scadeIl)
        {
            return null;
        }

        var profilo = await repository.ProfiloAsync(aziendaId, utenteId, ct);
        var accesso = emettitore.EmettiAccesso(profilo);

        return new CoppiaToken(accesso.Token, accesso.ScadeIl, nuovoToken, new DateTimeOffset(DateTime.SpecifyKind(scadeIl, DateTimeKind.Utc)));
    }

    public async Task EsciAsync(Guid aziendaId, Guid utenteId, string tokenRinnovo, IPAddress? indirizzo, CancellationToken ct)
    {
        await repository.RevocaSessioneAsync(aziendaId, utenteId, HashToken(tokenRinnovo), ct);
        await repository.RegistraAuditAsync(aziendaId, utenteId, "accesso.uscita", indirizzo, ct);
    }

    public Task<UtenteAutenticato> ProfiloAsync(Guid aziendaId, Guid utenteId, CancellationToken ct) =>
        repository.ProfiloAsync(aziendaId, utenteId, ct);

    public Task<IReadOnlyList<UtenteElenco>> ElencoUtentiAsync(UtenteAutenticato chiamante, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chiamante);
        return repository.ElencoUtentiAsync(chiamante.AziendaId, chiamante.UtenteId, ct);
    }

    /// <summary>
    /// Crea un utente. Solo il Titolare assegna qualsiasi ruolo; gli altri possono
    /// assegnare solo ruoli che hanno già, così l'Amministratore di sistema non può
    /// darsi accesso ai dati di business.
    /// </summary>
    public async Task<EsitoCreazioneUtente> CreaUtenteAsync(UtenteAutenticato chiamante, NuovoUtente utente, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chiamante);
        ArgumentNullException.ThrowIfNull(utente);

        if (utente.Ruoli.Count == 0)
        {
            return new EsitoCreazioneUtente.SenzaRuoli();
        }

        foreach (var ruolo in utente.Ruoli)
        {
            if (!CodiciRuolo.Tutti.Contains(ruolo))
            {
                return new EsitoCreazioneUtente.RuoloSconosciuto(ruolo);
            }

            if (!chiamante.Ruoli.Contains(CodiciRuolo.Titolare) && !chiamante.Ruoli.Contains(ruolo))
            {
                return new EsitoCreazioneUtente.RuoloNonAssegnabile(ruolo);
            }
        }

        var errori = ValidaPassword(password, utente.Email);
        if (errori.Count > 0)
        {
            return new EsitoCreazioneUtente.PasswordNonValida(errori);
        }

        var utenteId = await repository.CreaUtenteAsync(chiamante.AziendaId, chiamante.UtenteId, utente, hasher.Calcola(password), ct);
        return utenteId is { } id ? new EsitoCreazioneUtente.Creato(id) : new EsitoCreazioneUtente.EmailInUso();
    }

    public IReadOnlyList<ErrorePassword> ValidaPassword(string password, string email) =>
        PoliticaPassword.Valida(password, email, passwordNote.Contiene);

    internal static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    internal static byte[] HashCodiceRecupero(string codice) => HashToken(NormalizzaCodiceRecupero(codice));

    internal static string NormalizzaCodiceRecupero(string codice) =>
        new([.. codice.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static string NuovoTokenRinnovo() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string NuovoCodiceRecupero()
    {
        var caratteri = RandomNumberGenerator.GetString(AlfabetoCodiciRecupero, 10);
        return $"{caratteri[..5]}-{caratteri[5..]}";
    }

    private static string Base64Url(byte[] dati) =>
        Convert.ToBase64String(dati).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private bool Bloccato(DateTime? bloccatoFinoAl) =>
        bloccatoFinoAl is { } fino && fino > orologio.GetUtcNow().UtcDateTime;

    private async Task<bool> CodiceValidoAsync(
        Guid aziendaId, Guid utenteId, byte[] segretoCifrato, string codice, bool ammettiRecupero, CancellationToken ct)
    {
        var pulito = codice.Trim();

        if (pulito.Length == 6 && pulito.All(char.IsAsciiDigit))
        {
            return totp.Verifica(cifratore.Decifra(segretoCifrato), pulito, out var passo)
                && await repository.RegistraPassoTotpAsync(aziendaId, utenteId, passo, ct);
        }

        return ammettiRecupero
            && await repository.UsaCodiceRecuperoAsync(aziendaId, utenteId, HashCodiceRecupero(pulito), ct);
    }

    private async Task<CoppiaToken> CompletaAccessoAsync(Guid aziendaId, Guid utenteId, string? dispositivo, IPAddress? indirizzo, CancellationToken ct)
    {
        await repository.RegistraEsitoAccessoAsync(aziendaId, utenteId, riuscito: true, indirizzo, ct);

        var profilo = await repository.ProfiloAsync(aziendaId, utenteId, ct);
        var accesso = emettitore.EmettiAccesso(profilo);

        var tokenRinnovo = NuovoTokenRinnovo();
        var scadenzaSessione = orologio.GetUtcNow().AddHours(opzioni.DurataSessioneOre);
        await repository.CreaSessioneAsync(aziendaId, utenteId, HashToken(tokenRinnovo), scadenzaSessione.UtcDateTime, dispositivo, indirizzo, ct);

        return new CoppiaToken(accesso.Token, accesso.ScadeIl, tokenRinnovo, scadenzaSessione);
    }
}

/// <summary>Sollevata dal repository quando un altro primo avvio è arrivato prima.</summary>
public sealed class InstallazioneGiaInizializzataException : Exception
{
    public InstallazioneGiaInizializzataException()
    {
    }

    public InstallazioneGiaInizializzataException(string message) : base(message)
    {
    }

    public InstallazioneGiaInizializzataException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
