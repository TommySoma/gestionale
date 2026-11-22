using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Gestionale.Application.Autenticazione;
using Konscious.Security.Cryptography;

namespace Gestionale.Infrastructure.Sicurezza;

/// <summary>Parametri di Argon2id. I valori predefiniti sono il minimo raccomandato da OWASP.</summary>
public sealed record ParametriArgon2(int MemoriaKiB = 19_456, int Iterazioni = 2, int Parallelismo = 1);

/// <summary>
/// Argon2id con hash in formato PHC: <c>$argon2id$v=19$m=19456,t=2,p=1$sale$hash</c>.
/// La verifica legge i parametri dall'hash, quindi cambiarli non invalida le
/// password esistenti.
/// </summary>
public sealed class HasherArgon2id(ParametriArgon2 parametri) : IHasherPassword
{
    private const int LunghezzaSale = 16;
    private const int LunghezzaHash = 32;
    private const string Prefisso = "$argon2id$v=19$";

    public string Calcola(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var sale = RandomNumberGenerator.GetBytes(LunghezzaSale);
        var hash = Deriva(password, sale, parametri, LunghezzaHash);

        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefisso}m={parametri.MemoriaKiB},t={parametri.Iterazioni},p={parametri.Parallelismo}${Base64SenzaPadding(sale)}${Base64SenzaPadding(hash)}");
    }

    public bool Verifica(string password, string hash)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryLeggi(hash, out var letti, out var sale, out var atteso))
        {
            return false;
        }

        var calcolato = Deriva(password, sale, letti, atteso.Length);
        return CryptographicOperations.FixedTimeEquals(calcolato, atteso);
    }

    internal static bool TryLeggi(string hash, out ParametriArgon2 parametri, out byte[] sale, out byte[] valore)
    {
        parametri = new ParametriArgon2();
        sale = [];
        valore = [];

        if (string.IsNullOrEmpty(hash) || !hash.StartsWith(Prefisso, StringComparison.Ordinal))
        {
            return false;
        }

        var parti = hash[Prefisso.Length..].Split('$');
        if (parti.Length != 3)
        {
            return false;
        }

        int? memoria = null, iterazioni = null, parallelismo = null;
        foreach (var coppia in parti[0].Split(','))
        {
            var chiaveValore = coppia.Split('=');
            if (chiaveValore.Length != 2 || !int.TryParse(chiaveValore[1], NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
            {
                return false;
            }

            switch (chiaveValore[0])
            {
                case "m": memoria = numero; break;
                case "t": iterazioni = numero; break;
                case "p": parallelismo = numero; break;
                default: return false;
            }
        }

        if (memoria is not > 0 || iterazioni is not > 0 || parallelismo is not > 0)
        {
            return false;
        }

        try
        {
            sale = DaBase64SenzaPadding(parti[1]);
            valore = DaBase64SenzaPadding(parti[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (sale.Length < 8 || valore.Length < 16)
        {
            return false;
        }

        parametri = new ParametriArgon2(memoria.Value, iterazioni.Value, parallelismo.Value);
        return true;
    }

    private static byte[] Deriva(string password, byte[] sale, ParametriArgon2 parametri, int lunghezza)
    {
        var byteDellaPassword = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(byteDellaPassword)
            {
                Salt = sale,
                MemorySize = parametri.MemoriaKiB,
                Iterations = parametri.Iterazioni,
                DegreeOfParallelism = parametri.Parallelismo,
            };

            return argon2.GetBytes(lunghezza);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(byteDellaPassword);
        }
    }

    private static string Base64SenzaPadding(byte[] dati) => Convert.ToBase64String(dati).TrimEnd('=');

    private static byte[] DaBase64SenzaPadding(string testo) =>
        Convert.FromBase64String(testo.PadRight(testo.Length + ((4 - (testo.Length % 4)) % 4), '='));
}
