using System.Security.Cryptography;
using Gestionale.Application.Autenticazione;

namespace Gestionale.Infrastructure.Sicurezza;

/// <summary>
/// AES-256-GCM. Formato: versione (1 byte) | nonce (12) | tag (16) | testo cifrato.
/// La versione permette in futuro di ruotare la chiave senza perdere i dati.
/// </summary>
public sealed class CifratoreAesGcm : ICifratoreSegreti
{
    private const byte Versione = 1;
    private const int LunghezzaNonce = 12;
    private const int LunghezzaTag = 16;
    private const int Intestazione = 1 + LunghezzaNonce + LunghezzaTag;

    private readonly byte[] _chiave;

    public CifratoreAesGcm(byte[] chiave)
    {
        ArgumentNullException.ThrowIfNull(chiave);
        if (chiave.Length != 32)
        {
            throw new ArgumentException("La chiave AES deve essere di 32 byte.", nameof(chiave));
        }

        _chiave = [.. chiave];
    }

    public byte[] Cifra(byte[] testo)
    {
        ArgumentNullException.ThrowIfNull(testo);

        var risultato = new byte[Intestazione + testo.Length];
        risultato[0] = Versione;
        var nonce = risultato.AsSpan(1, LunghezzaNonce);
        var tag = risultato.AsSpan(1 + LunghezzaNonce, LunghezzaTag);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_chiave, LunghezzaTag);
        aes.Encrypt(nonce, testo, risultato.AsSpan(Intestazione), tag);
        return risultato;
    }

    public byte[] Decifra(byte[] cifrato)
    {
        ArgumentNullException.ThrowIfNull(cifrato);

        if (cifrato.Length < Intestazione || cifrato[0] != Versione)
        {
            throw new CryptographicException("Segreto cifrato in un formato sconosciuto.");
        }

        var testo = new byte[cifrato.Length - Intestazione];
        using var aes = new AesGcm(_chiave, LunghezzaTag);
        aes.Decrypt(
            cifrato.AsSpan(1, LunghezzaNonce),
            cifrato.AsSpan(Intestazione),
            cifrato.AsSpan(1 + LunghezzaNonce, LunghezzaTag),
            testo);
        return testo;
    }
}
