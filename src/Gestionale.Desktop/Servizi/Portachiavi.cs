using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Gestionale.Desktop.Servizi;

/// <summary>
/// Segreti dell'utente sul computer: solo nel portachiavi del sistema operativo,
/// mai in un file. Qui finisce soltanto il token di rinnovo.
/// </summary>
public interface IPortachiavi
{
    string? Leggi(string account);

    void Salva(string account, string segreto);

    void Elimina(string account);
}

public static class Portachiavi
{
    public const string Servizio = "Gestionale impianti";

    /// <summary>
    /// Il portachiavi del sistema in uso. Su un sistema senza supporto i segreti
    /// restano in memoria e si perdono alla chiusura: si accede di nuovo ogni volta.
    /// </summary>
    public static IPortachiavi DelSistema()
    {
        if (OperatingSystem.IsWindows())
        {
            return new PortachiaviWindows();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new PortachiaviMacOs();
        }

        return new PortachiaviInMemoria();
    }
}

/// <summary>Nessuna persistenza: usato dove il sistema non offre un portachiavi, e nei test.</summary>
public sealed class PortachiaviInMemoria : IPortachiavi
{
    private readonly Dictionary<string, string> _segreti = new(StringComparer.Ordinal);

    public string? Leggi(string account) => _segreti.GetValueOrDefault(account);

    public void Salva(string account, string segreto) => _segreti[account] = segreto;

    public void Elimina(string account) => _segreti.Remove(account);
}

/// <summary>Windows Credential Manager (credenziali generiche dell'utente corrente).</summary>
[SupportedOSPlatform("windows")]
public sealed partial class PortachiaviWindows : IPortachiavi
{
    private const uint TipoGenerico = 1;
    private const uint PersistenzaLocale = 2;
    private const int ElementoNonTrovato = 1168;

    public string? Leggi(string account)
    {
        if (!CredRead(Destinazione(account), TipoGenerico, 0, out var puntatore))
        {
            var errore = Marshal.GetLastPInvokeError();
            return errore == ElementoNonTrovato ? null : throw new InvalidOperationException($"Lettura dal portachiavi non riuscita ({errore}).");
        }

        try
        {
            var credenziale = Marshal.PtrToStructure<Credenziale>(puntatore);
            var dati = new byte[credenziale.DimensioneSegreto];
            Marshal.Copy(credenziale.Segreto, dati, 0, dati.Length);
            return Encoding.UTF8.GetString(dati);
        }
        finally
        {
            CredFree(puntatore);
        }
    }

    public void Salva(string account, string segreto)
    {
        ArgumentNullException.ThrowIfNull(segreto);

        var dati = Encoding.UTF8.GetBytes(segreto);
        var destinazione = Marshal.StringToHGlobalUni(Destinazione(account));
        var utente = Marshal.StringToHGlobalUni(account);
        var blocco = Marshal.AllocHGlobal(dati.Length);
        try
        {
            Marshal.Copy(dati, 0, blocco, dati.Length);
            var credenziale = new Credenziale
            {
                Tipo = TipoGenerico,
                NomeDestinazione = destinazione,
                DimensioneSegreto = (uint)dati.Length,
                Segreto = blocco,
                Persistenza = PersistenzaLocale,
                NomeUtente = utente,
            };

            if (!CredWrite(in credenziale, 0))
            {
                throw new InvalidOperationException($"Scrittura nel portachiavi non riuscita ({Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            Marshal.Copy(new byte[dati.Length], 0, blocco, dati.Length);
            Marshal.FreeHGlobal(blocco);
            Marshal.FreeHGlobal(destinazione);
            Marshal.FreeHGlobal(utente);
        }
    }

    public void Elimina(string account)
    {
        if (!CredDelete(Destinazione(account), TipoGenerico, 0) && Marshal.GetLastPInvokeError() != ElementoNonTrovato)
        {
            throw new InvalidOperationException($"Eliminazione dal portachiavi non riuscita ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static string Destinazione(string account) => $"{Portachiavi.Servizio}/{account}";

    // Struttura CREDENTIALW di wincred.h.
    [StructLayout(LayoutKind.Sequential)]
    private struct Credenziale
    {
        public uint Flag;
        public uint Tipo;
        public IntPtr NomeDestinazione;
        public IntPtr Commento;
        public uint UltimaScritturaBassa;
        public uint UltimaScritturaAlta;
        public uint DimensioneSegreto;
        public IntPtr Segreto;
        public uint Persistenza;
        public uint NumeroAttributi;
        public IntPtr Attributi;
        public IntPtr Alias;
        public IntPtr NomeUtente;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in Credenziale credenziale, uint flag);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string destinazione, uint tipo, uint flag, out IntPtr credenziale);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string destinazione, uint tipo, uint flag);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr credenziale);
}

/// <summary>
/// Portachiavi di macOS (password generiche del portachiavi di login).
/// Usa le API SecKeychain*, deprecate ma ancora supportate: si passerà a
/// SecItem* quando serviranno attributi più ricchi.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class PortachiaviMacOs : IPortachiavi
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ElementoNonTrovato = -25300;

    private static readonly byte[] s_servizio = Encoding.UTF8.GetBytes(Portachiavi.Servizio);

    public string? Leggi(string account)
    {
        var nome = Encoding.UTF8.GetBytes(account);
        var esito = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s_servizio.Length, s_servizio, (uint)nome.Length, nome,
            out var lunghezza, out var dati, out var elemento);

        if (esito == ElementoNonTrovato)
        {
            return null;
        }

        Verifica(esito, "Lettura dal portachiavi");
        try
        {
            var copia = new byte[lunghezza];
            Marshal.Copy(dati, copia, 0, copia.Length);
            return Encoding.UTF8.GetString(copia);
        }
        finally
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, dati);
            CFRelease(elemento);
        }
    }

    public void Salva(string account, string segreto)
    {
        ArgumentNullException.ThrowIfNull(segreto);

        Elimina(account);
        var nome = Encoding.UTF8.GetBytes(account);
        var dati = Encoding.UTF8.GetBytes(segreto);
        Verifica(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s_servizio.Length, s_servizio, (uint)nome.Length, nome,
            (uint)dati.Length, dati, IntPtr.Zero), "Scrittura nel portachiavi");
    }

    public void Elimina(string account)
    {
        var nome = Encoding.UTF8.GetBytes(account);
        var esito = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s_servizio.Length, s_servizio, (uint)nome.Length, nome,
            out _, out var dati, out var elemento);

        if (esito == ElementoNonTrovato)
        {
            return;
        }

        Verifica(esito, "Ricerca nel portachiavi");
        try
        {
            Verifica(SecKeychainItemDelete(elemento), "Eliminazione dal portachiavi");
        }
        finally
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, dati);
            CFRelease(elemento);
        }
    }

    private static void Verifica(int esito, string operazione)
    {
        if (esito != 0)
        {
            throw new InvalidOperationException($"{operazione} non riuscita ({esito}).");
        }
    }

    [LibraryImport(Security)]
    private static partial int SecKeychainAddGenericPassword(
        IntPtr portachiavi, uint lunghezzaServizio, byte[] servizio, uint lunghezzaAccount, byte[] account,
        uint lunghezzaPassword, byte[] password, IntPtr elemento);

    [LibraryImport(Security)]
    private static partial int SecKeychainFindGenericPassword(
        IntPtr portachiavi, uint lunghezzaServizio, byte[] servizio, uint lunghezzaAccount, byte[] account,
        out uint lunghezzaPassword, out IntPtr password, out IntPtr elemento);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemDelete(IntPtr elemento);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemFreeContent(IntPtr attributi, IntPtr dati);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr oggetto);
}
