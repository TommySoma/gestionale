using Gestionale.Application.Autenticazione;
using Gestionale.Domain.Sicurezza;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gestionale.Api.Autenticazione;

/// <summary>Chiavi decodificate dalla configurazione e verificate all'avvio.</summary>
internal sealed record ChiaviAutenticazione(SymmetricSecurityKey Firma, byte[] CifraturaMfa);

/// <summary>Token JWT firmati HMAC-SHA256: accesso (15 minuti) e passaggio del secondo fattore.</summary>
internal sealed class EmettitoreJwt(OpzioniAutenticazione opzioni, ChiaviAutenticazione chiavi, TimeProvider orologio) : IEmettitoreToken
{
    private readonly JsonWebTokenHandler _gestore = new();
    private readonly SigningCredentials _credenziali = new(chiavi.Firma, SecurityAlgorithms.HmacSha256);

    public TokenAccesso EmettiAccesso(UtenteAutenticato utente)
    {
        ArgumentNullException.ThrowIfNull(utente);

        var scadeIl = orologio.GetUtcNow().AddMinutes(opzioni.DurataAccessoMinuti);
        var claim = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = utente.UtenteId.ToString(),
            [ClaimGestionale.Azienda] = utente.AziendaId.ToString(),
            [JwtRegisteredClaimNames.Email] = utente.Email,
            [JwtRegisteredClaimNames.Name] = $"{utente.Nome} {utente.Cognome}",
            [ClaimGestionale.Ruolo] = utente.Ruoli.ToArray(),
            [ClaimGestionale.Permesso] = utente.Permessi
                .Select(p => p.Ambito == AmbitoPermesso.Propri ? p.Codice + ClaimGestionale.SuffissoPropri : p.Codice)
                .ToArray(),
        };

        return new TokenAccesso(Crea(Destinatari.Api, claim, scadeIl), scadeIl);
    }

    public string EmettiPassaggioMfa(Guid utenteId, Guid aziendaId, ScopoMfa scopo)
    {
        var claim = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = utenteId.ToString(),
            [ClaimGestionale.Azienda] = aziendaId.ToString(),
            [ClaimGestionale.Scopo] = scopo == ScopoMfa.Verifica ? ScopiPassaggio.Verifica : ScopiPassaggio.Configurazione,
        };

        return Crea(Destinatari.PassaggioMfa, claim, orologio.GetUtcNow().AddMinutes(opzioni.DurataPassaggioMfaMinuti));
    }

    public static TokenValidationParameters Parametri(OpzioniAutenticazione opzioni, ChiaviAutenticazione chiavi, string destinatario) => new()
    {
        ValidIssuer = opzioni.Emittente,
        ValidAudience = destinatario,
        IssuerSigningKey = chiavi.Firma,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Name,
        RoleClaimType = ClaimGestionale.Ruolo,
    };

    private string Crea(string destinatario, Dictionary<string, object> claim, DateTimeOffset scadeIl)
    {
        var adesso = orologio.GetUtcNow().UtcDateTime;
        claim[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString();

        return _gestore.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opzioni.Emittente,
            Audience = destinatario,
            IssuedAt = adesso,
            NotBefore = adesso,
            Expires = scadeIl.UtcDateTime,
            Claims = claim,
            SigningCredentials = _credenziali,
        });
    }
}
