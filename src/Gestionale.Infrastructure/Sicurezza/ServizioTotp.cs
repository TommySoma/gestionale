using Gestionale.Application.Autenticazione;
using OtpNet;

namespace Gestionale.Infrastructure.Sicurezza;

/// <summary>
/// TOTP standard (SHA-1, 6 cifre, 30 secondi), compatibile con le app di
/// autenticazione più diffuse. Accetta un passo di scarto per l'orologio.
/// </summary>
public sealed class ServizioTotp(TimeProvider orologio) : IServizioTotp
{
    private const int LunghezzaSegreto = 20;

    public byte[] NuovoSegreto() => KeyGeneration.GenerateRandomKey(LunghezzaSegreto);

    public string InBase32(byte[] segreto) => Base32Encoding.ToString(segreto);

    public string UriConfigurazione(byte[] segreto, string emittente, string account)
    {
        ArgumentNullException.ThrowIfNull(emittente);
        ArgumentNullException.ThrowIfNull(account);

        var etichetta = Uri.EscapeDataString($"{emittente}:{account}");
        return $"otpauth://totp/{etichetta}?secret={InBase32(segreto)}&issuer={Uri.EscapeDataString(emittente)}&algorithm=SHA1&digits=6&period=30";
    }

    public bool Verifica(byte[] segreto, string codice, out long passo)
    {
        var totp = new Totp(segreto);
        return totp.VerifyTotp(orologio.GetUtcNow().UtcDateTime, codice, out passo, VerificationWindow.RfcSpecifiedNetworkDelay);
    }
}
