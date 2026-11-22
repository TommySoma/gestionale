using System.Security.Claims;
using System.Security.Cryptography;
using Gestionale.Api.Autenticazione;
using Gestionale.Application.Autenticazione;
using Gestionale.Domain.Sicurezza;
using Gestionale.Infrastructure.Sicurezza;
using OtpNet;

namespace Gestionale.Tests.Unit;

public sealed class TestHasherArgon2id
{
    // Parametri leggeri: qui si prova il formato, non la resistenza.
    private readonly HasherArgon2id _hasher = new(new ParametriArgon2(MemoriaKiB: 1024, Iterazioni: 1, Parallelismo: 1));

    [Fact]
    public void L_hash_è_in_formato_PHC_argon2id()
    {
        var hash = _hasher.Calcola("una password lunga abbastanza");

        Assert.StartsWith("$argon2id$v=19$m=1024,t=1,p=1$", hash, StringComparison.Ordinal);
        Assert.Equal(5, hash.Split('$').Length - 1);
    }

    [Fact]
    public void La_password_giusta_è_verificata_e_quella_sbagliata_no()
    {
        var hash = _hasher.Calcola("una password lunga abbastanza");

        Assert.True(_hasher.Verifica("una password lunga abbastanza", hash));
        Assert.False(_hasher.Verifica("una password lunga abbastanzA", hash));
    }

    [Fact]
    public void Due_hash_della_stessa_password_sono_diversi()
    {
        Assert.NotEqual(_hasher.Calcola("stessa password di prova"), _hasher.Calcola("stessa password di prova"));
    }

    [Fact]
    public void La_verifica_usa_i_parametri_scritti_nell_hash()
    {
        var hashVecchio = new HasherArgon2id(new ParametriArgon2(2048, 2, 1)).Calcola("password con parametri vecchi");

        Assert.True(_hasher.Verifica("password con parametri vecchi", hashVecchio));
    }

    [Theory]
    [InlineData("")]
    [InlineData("$argon2i$v=19$m=1024,t=1,p=1$c2FsZXNhbGU$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1$c2FsZXNhbGU$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=0,t=1,p=1$c2FsZXNhbGU$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$non base64!$aGFzaGhhc2hoYXNoaGFzaA")]
    public void Un_hash_malformato_non_verifica_nessuna_password(string hash)
    {
        Assert.False(_hasher.Verifica("qualsiasi password", hash));
    }
}

public sealed class TestCifratoreAesGcm
{
    private readonly CifratoreAesGcm _cifratore = new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Cifra_e_decifra_restituiscono_il_testo_originale()
    {
        var segreto = RandomNumberGenerator.GetBytes(20);

        var cifrato = _cifratore.Cifra(segreto);

        Assert.NotEqual(segreto, cifrato[^20..]);
        Assert.Equal(segreto, _cifratore.Decifra(cifrato));
    }

    [Fact]
    public void Un_segreto_manomesso_non_si_decifra()
    {
        var cifrato = _cifratore.Cifra(RandomNumberGenerator.GetBytes(20));
        cifrato[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => _cifratore.Decifra(cifrato));
    }

    [Fact]
    public void Con_un_altra_chiave_non_si_decifra()
    {
        var cifrato = _cifratore.Cifra(RandomNumberGenerator.GetBytes(20));

        Assert.ThrowsAny<CryptographicException>(() => new CifratoreAesGcm(RandomNumberGenerator.GetBytes(32)).Decifra(cifrato));
    }

    [Fact]
    public void La_chiave_deve_essere_di_32_byte()
    {
        Assert.Throws<ArgumentException>(() => new CifratoreAesGcm(new byte[16]));
    }
}

public sealed class TestServizioTotp
{
    private static readonly DateTimeOffset s_adesso = new(2026, 11, 22, 10, 0, 15, TimeSpan.Zero);

    private readonly ServizioTotp _totp = new(new OrologioFisso(s_adesso));

    [Fact]
    public void Il_codice_corrente_è_valido_e_restituisce_il_passo()
    {
        var segreto = _totp.NuovoSegreto();
        var codice = new Totp(segreto).ComputeTotp(s_adesso.UtcDateTime);

        Assert.True(_totp.Verifica(segreto, codice, out var passo));
        Assert.Equal(s_adesso.ToUnixTimeSeconds() / 30, passo);
    }

    [Fact]
    public void Un_codice_di_cinque_minuti_fa_non_è_valido()
    {
        var segreto = _totp.NuovoSegreto();
        var codice = new Totp(segreto).ComputeTotp(s_adesso.UtcDateTime.AddMinutes(-5));

        Assert.False(_totp.Verifica(segreto, codice, out _));
    }

    [Fact]
    public void L_uri_contiene_emittente_account_e_segreto()
    {
        var segreto = _totp.NuovoSegreto();

        var uri = _totp.UriConfigurazione(segreto, "Gestionale impianti", "mario.rossi@alfa.example");

        Assert.StartsWith("otpauth://totp/Gestionale%20impianti%3Amario.rossi%40alfa.example?", uri, StringComparison.Ordinal);
        Assert.Contains($"secret={_totp.InBase32(segreto)}", uri, StringComparison.Ordinal);
    }

    private sealed class OrologioFisso(DateTimeOffset adesso) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => adesso;
    }
}

public sealed class TestPoliticaPassword
{
    private static bool Nessuna(string _) => false;

    [Fact]
    public void Una_password_lunga_e_non_nota_è_accettata()
    {
        Assert.Empty(PoliticaPassword.Valida("caldaia-verde-sul-tetto", "mario@alfa.example", Nessuna));
    }

    [Fact]
    public void Undici_caratteri_non_bastano()
    {
        Assert.Contains(ErrorePassword.TroppoCorta, PoliticaPassword.Valida("abcdefghijk", "mario@alfa.example", Nessuna));
    }

    [Fact]
    public void I_caratteri_si_contano_come_li_vede_l_utente()
    {
        // 12 emoji: 24 unità UTF-16 ma 12 caratteri.
        Assert.DoesNotContain(ErrorePassword.TroppoCorta, PoliticaPassword.Valida(string.Concat(Enumerable.Repeat("🔥", 12)), "a@b.example", Nessuna));
        Assert.Contains(ErrorePassword.TroppoCorta, PoliticaPassword.Valida(string.Concat(Enumerable.Repeat("🔥", 11)), "a@b.example", Nessuna));
    }

    [Fact]
    public void Oltre_128_caratteri_è_troppo_lunga()
    {
        Assert.Contains(ErrorePassword.TroppoLunga, PoliticaPassword.Valida(new string('x', 129), "mario@alfa.example", Nessuna));
    }

    [Fact]
    public void La_password_non_può_essere_l_email()
    {
        Assert.Contains(ErrorePassword.UgualeAllEmail, PoliticaPassword.Valida("Mario.Rossi.Termo", "mario.rossi.termo@alfa.example", Nessuna));
        Assert.Contains(ErrorePassword.UgualeAllEmail, PoliticaPassword.Valida("mario.rossi@alfa.example", "mario.rossi@alfa.example", Nessuna));
    }

    [Fact]
    public void Le_password_note_sono_rifiutate()
    {
        var elenco = new ElencoPasswordNote();

        Assert.True(elenco.Numero > 0);
        Assert.Contains(ErrorePassword.Nota, PoliticaPassword.Valida("Password1234", "mario@alfa.example", elenco.Contiene));
    }
}

public sealed class TestCodiciRecupero
{
    [Theory]
    [InlineData("ABCDE-FGHJK")]
    [InlineData("abcde fghjk")]
    [InlineData(" abcdefghjk ")]
    public void Il_codice_si_può_scrivere_senza_trattino_e_in_minuscolo(string scritto)
    {
        Assert.Equal(ServizioAutenticazione.HashCodiceRecupero("ABCDE-FGHJK"), ServizioAutenticazione.HashCodiceRecupero(scritto));
    }
}

public sealed class TestPermessiDaToken
{
    private static ClaimsPrincipal Utente(params string[] permessi) =>
        new(new ClaimsIdentity(permessi.Select(p => new Claim("perm", p)), "Bearer"));

    [Fact]
    public void Un_permesso_senza_suffisso_vale_su_tutto()
    {
        Assert.Equal(AmbitoPermesso.Tutti, Utente("clienti.lettura").AmbitoPermesso(CodiciPermesso.ClientiLettura));
    }

    [Fact]
    public void Un_permesso_con_suffisso_vale_solo_sui_propri()
    {
        Assert.Equal(AmbitoPermesso.Propri, Utente("clienti.lettura:propri").AmbitoPermesso(CodiciPermesso.ClientiLettura));
    }

    [Fact]
    public void Senza_il_permesso_l_ambito_è_nullo()
    {
        Assert.Null(Utente("clienti.lettura").AmbitoPermesso(CodiciPermesso.ClientiScrittura));
        Assert.Null(Utente("clienti.letturaextra").AmbitoPermesso(CodiciPermesso.ClientiLettura));
    }
}
