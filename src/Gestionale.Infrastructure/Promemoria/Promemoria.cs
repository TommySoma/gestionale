using System.Globalization;
using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Promemoria;
using Gestionale.Domain.Promemoria;
using Gestionale.Infrastructure.Documenti;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Npgsql;

namespace Gestionale.Infrastructure.Promemoria;

/// <summary>Parametri di posta, dati delle scadenze scelte e registrazione dei promemoria inviati.</summary>
public sealed class RepositoryPromemoria(Database.Database db) : IRepositoryPromemoria
{
    public Task<ParametriPosta?> ParametriAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.QuerySingleOrDefaultAsync<ParametriPosta>(Comando(
            """
            SELECT host, porta, sicurezza, utente, password_cifrata IS NOT NULL AS con_password, mittente_email, mittente_nome, versione
            FROM sec.parametri_posta WHERE azienda_id = @AziendaId
            """,
            new { contesto.AziendaId }, t, ct)), ct);

    public Task<ParametriInvio?> ParametriInvioAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, async (c, t) => (await c.QuerySingleOrDefaultAsync<RigaParametri>(Comando(
            "SELECT host, porta, sicurezza, utente, password_cifrata, mittente_email, mittente_nome FROM sec.parametri_posta WHERE azienda_id = @AziendaId",
            new { contesto.AziendaId }, t, ct))) is { } r
            ? new ParametriInvio(r.Host, r.Porta, r.Sicurezza, r.Utente, r.PasswordCifrata, r.MittenteEmail, r.MittenteNome)
            : null, ct);

    public Task<EsitoPromemoria> SalvaParametriAsync(Contesto contesto, int? versione, DatiPosta dati, byte[]? passwordCifrata, CancellationToken ct) =>
        Esegui<EsitoPromemoria>(contesto, async (c, t) =>
        {
            var parametri = new
            {
                contesto.AziendaId,
                contesto.UtenteId,
                versione,
                dati.Host,
                dati.Porta,
                dati.Sicurezza,
                dati.Utente,
                password = passwordCifrata,
                dati.MittenteEmail,
                dati.MittenteNome,
            };

            // La prima volta si crea; poi si aggiorna con la versione letta. Senza utente non resta nessuna password.
            var nuova = await c.ExecuteScalarAsync<int?>(Comando(
                """
                INSERT INTO sec.parametri_posta (azienda_id, host, porta, sicurezza, utente, password_cifrata, mittente_email, mittente_nome, creato_da)
                SELECT @AziendaId, @Host, @Porta, @Sicurezza, @Utente, CASE WHEN @Utente::text IS NULL THEN NULL ELSE @password::bytea END,
                       @MittenteEmail, @MittenteNome, @UtenteId
                WHERE @versione::integer IS NULL
                ON CONFLICT (azienda_id) DO NOTHING
                RETURNING versione
                """,
                parametri, t, ct));
            if (nuova is { } creata)
            {
                return new EsitoPromemoria.Salvato(creata);
            }

            var aggiornata = await c.ExecuteScalarAsync<int?>(Comando(
                """
                UPDATE sec.parametri_posta
                SET host = @Host, porta = @Porta, sicurezza = @Sicurezza, utente = @Utente,
                    password_cifrata = CASE WHEN @Utente::text IS NULL THEN NULL ELSE coalesce(@password::bytea, password_cifrata) END,
                    mittente_email = @MittenteEmail, mittente_nome = @MittenteNome, modificato_da = @UtenteId
                WHERE azienda_id = @AziendaId AND versione = @versione
                RETURNING versione
                """,
                parametri, t, ct));
            return aggiornata is { } v ? new EsitoPromemoria.Salvato(v) : new EsitoPromemoria.VersioneSuperata();
        }, ct);

    public Task<ImpresaPromemoria> ImpresaAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.QuerySingleAsync<ImpresaPromemoria>(Comando(
            """SELECT ragione_sociale AS "RagioneSociale", indirizzo AS "Indirizzo", cap AS "Cap", comune AS "Comune", provincia AS "Provincia" FROM sec.aziende WHERE azienda_id = @AziendaId""",
            new { contesto.AziendaId }, t, ct)), ct);

    public Task<string> EmailUtenteAsync(Contesto contesto, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.QuerySingleAsync<string>(Comando(
            "SELECT email FROM sec.utenti WHERE utente_id = @UtenteId", new { contesto.UtenteId }, t, ct)), ct);

    public Task<IReadOnlyList<DatiPromemoria>> DatiAsync(Contesto contesto, IReadOnlyList<Guid> scadenze, CancellationToken ct) =>
        Esegui<IReadOnlyList<DatiPromemoria>>(contesto, async (c, t) => [.. await c.QueryAsync<DatiPromemoria>(Comando(
            "SELECT * FROM lavoro.dati_promemoria(@scadenze)", new { scadenze = scadenze.ToArray() }, t, ct))], ct);

    public Task RegistraInvioAsync(Contesto contesto, Guid scadenzaId, string oggetto, CancellationToken ct) =>
        Esegui(contesto, (c, t) => c.ExecuteAsync(Comando(
            """
            INSERT INTO lavoro.richiami (azienda_id, scadenza_id, canale, esito, note, creato_da)
            VALUES (@AziendaId, @scadenzaId, 'email', 'promemoria_inviato', @oggetto, @UtenteId)
            """,
            new { contesto.AziendaId, scadenzaId, oggetto, contesto.UtenteId }, t, ct)), ct);

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class RigaParametri
    {
        public string Host { get; init; } = "";

        public int Porta { get; init; }

        public string Sicurezza { get; init; } = "";

        public string? Utente { get; init; }

        public byte[]? PasswordCifrata { get; init; }

        public string MittenteEmail { get; init; } = "";

        public string? MittenteNome { get; init; }
    }
}

/// <summary>Invio SMTP con MailKit (MIT): una connessione per tutti i messaggi di un invio.</summary>
public sealed class PostinoSmtp : IPostino
{
    private static readonly TimeSpan s_attesa = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<string?>> InviaAsync(
        ParametriInvio parametri, string? password, IReadOnlyList<MessaggioEmail> messaggi, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parametri);
        ArgumentNullException.ThrowIfNull(messaggi);

        using var client = new SmtpClient { Timeout = (int)s_attesa.TotalMilliseconds };
        try
        {
            var sicurezza = parametri.Sicurezza switch
            {
                SicurezzePosta.Ssl => SecureSocketOptions.SslOnConnect,
                SicurezzePosta.StartTls => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None,
            };
            await client.ConnectAsync(parametri.Host, parametri.Porta, sicurezza, ct);
            if (parametri.Utente is { } utente && password is not null)
            {
                await client.AuthenticateAsync(utente, password, ct);
            }
        }
        catch (Exception e) when (e is SmtpCommandException or SmtpProtocolException or AuthenticationException or IOException
                                      or System.Net.Sockets.SocketException or SslHandshakeException or TimeoutException)
        {
            // Senza connessione nessun messaggio parte: lo stesso motivo per tutti.
            return [.. messaggi.Select(_ => (string?)e.Message)];
        }

        var esiti = new List<string?>();
        foreach (var m in messaggi)
        {
            var messaggio = new MimeMessage();
            messaggio.From.Add(new MailboxAddress(parametri.MittenteNome ?? parametri.MittenteEmail, parametri.MittenteEmail));
            messaggio.To.Add(new MailboxAddress(m.NomeDestinatario ?? m.Destinatario, m.Destinatario));
            messaggio.Subject = m.Oggetto;
            messaggio.Body = new TextPart("plain") { Text = m.Testo };
            try
            {
                await client.SendAsync(messaggio, ct);
                esiti.Add(null);
            }
            catch (Exception e) when (e is SmtpCommandException or SmtpProtocolException or IOException)
            {
                esiti.Add(e.Message);
            }
        }

        await client.DisconnectAsync(true, ct);
        return esiti;
    }
}

/// <summary>Testo dei promemoria dai file di traduzione dei documenti, con la data all'italiana.</summary>
public sealed class ComponitorePromemoria : IComponitorePromemoria
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public (string Oggetto, string Testo) Componi(DatiPromemoria dati, ImpresaPromemoria impresa, string? nota, DateOnly oggi)
    {
        ArgumentNullException.ThrowIfNull(dati);
        ArgumentNullException.ThrowIfNull(impresa);

        var data = dati.DataScadenza.ToString("d MMMM yyyy", s_italiano);
        var cosa = dati.Tipo == "controllo_efficienza" ? TestiDocumenti.PromemoriaControlloEfficienza : TestiDocumenti.PromemoriaManutenzione;
        var oggetto = string.Format(s_italiano, TestiDocumenti.PromemoriaOggetto, Maiuscola(cosa), data);
        var quando = string.Format(s_italiano, dati.DataScadenza < oggi ? TestiDocumenti.PromemoriaScaduta : TestiDocumenti.PromemoriaInScadenza, cosa, data);

        var righe = new List<string>
        {
            string.Format(s_italiano, TestiDocumenti.PromemoriaSaluto, dati.Cliente),
            "",
            string.Format(s_italiano, TestiDocumenti.PromemoriaCorpo, dati.TipoImpianto.ToLower(s_italiano), dati.Indirizzo, dati.Comune, quando),
            "",
            TestiDocumenti.PromemoriaAppuntamento,
        };
        if (nota is not null)
        {
            righe.AddRange(["", nota]);
        }

        righe.AddRange(["", TestiDocumenti.PromemoriaCongedo, .. Firma(impresa), "", TestiDocumenti.PromemoriaConsenso]);
        return (oggetto, string.Join("\r\n", righe));
    }

    public MessaggioEmail Prova(string destinatario, ImpresaPromemoria impresa)
    {
        ArgumentNullException.ThrowIfNull(impresa);

        var testo = string.Join("\r\n", [TestiDocumenti.ProvaPostaCorpo, "", .. Firma(impresa)]);
        return new MessaggioEmail(destinatario, null, TestiDocumenti.ProvaPostaOggetto, testo);
    }

    private static IEnumerable<string> Firma(ImpresaPromemoria impresa)
    {
        yield return impresa.RagioneSociale;
        var indirizzo = string.Join(" ", new[] { impresa.Indirizzo, impresa.Cap, impresa.Comune, impresa.Provincia is null ? null : $"({impresa.Provincia})" }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        if (indirizzo.Length > 0)
        {
            yield return indirizzo;
        }
    }

    private static string Maiuscola(string testo) => testo.Length == 0 ? testo : char.ToUpper(testo[0], s_italiano) + testo[1..];
}
