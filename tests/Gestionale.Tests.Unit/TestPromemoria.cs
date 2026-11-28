using Gestionale.Application.Promemoria;
using Gestionale.Domain.Promemoria;
using Gestionale.Infrastructure.Promemoria;

namespace Gestionale.Tests.Unit;

public sealed class TestPromemoria
{
    private static readonly ImpresaPromemoria s_impresa = new("Termoidraulica Prova Srl (fittizia)", "Via delle Prove 1", "50100", "Firenze", "FI");

    [Fact]
    public void I_parametri_di_posta_si_normalizzano_e_si_validano()
    {
        var (dati, errori) = RegolePromemoria.Valida(new DatiPosta(" SMTP.Example.com ", 587, "STARTTLS", " utente ", "", " Info@Example.com ", null));
        Assert.Empty(errori);
        Assert.Equal(("smtp.example.com", "starttls", "utente", null, "info@example.com"), (dati.Host, dati.Sicurezza, dati.Utente, dati.Password, dati.MittenteEmail));

        var sbagliati = RegolePromemoria.Valida(new DatiPosta("smtp example", 0, "tls", null, "segreta", "nessuna-email", null)).Errori;
        Assert.Equal(
            [ErrorePromemoria.HostPostaNonValido, ErrorePromemoria.PortaPostaNonValida, ErrorePromemoria.SicurezzaPostaNonValida,
             ErrorePromemoria.MittentePostaNonValido, ErrorePromemoria.PasswordSenzaUtente],
            sbagliati);
    }

    [Fact]
    public void Un_invio_ha_almeno_una_scadenza_e_non_troppe()
    {
        var una = Guid.NewGuid();
        var (scadenze, nota, errori) = RegolePromemoria.ValidaInvio([una, una, Guid.Empty], "  ");
        Assert.Equal([una], scadenze);
        Assert.Null(nota);
        Assert.Empty(errori);

        Assert.Equal([ErrorePromemoria.NessunaScadenzaScelta], RegolePromemoria.ValidaInvio([], null).Errori);
        Assert.Equal([ErrorePromemoria.TroppeScadenzeScelte],
            RegolePromemoria.ValidaInvio(Enumerable.Range(0, RegolePromemoria.ScadenzeMassimePerInvio + 1).Select(_ => Guid.NewGuid()), null).Errori);
        Assert.Equal([ErrorePromemoria.NotaTroppoLunga], RegolePromemoria.ValidaInvio([una], new string('x', 1001)).Errori);
    }

    [Fact]
    public void Il_testo_dice_cosa_scade_quando_e_perche_arriva()
    {
        var dati = new DatiPromemoria
        {
            Tipo = "controllo_efficienza",
            DataScadenza = new DateOnly(2027, 2, 15),
            Cliente = "Giulia Esempio",
            TipoImpianto = "Riscaldamento (voce di prova)",
            Indirizzo = "Via dei Platani 12",
            Comune = "Firenze",
        };

        var (oggetto, testo) = new ComponitorePromemoria().Componi(dati, s_impresa, "Siamo in zona il martedì.", new DateOnly(2027, 1, 13));

        Assert.Equal("Il controllo di efficienza energetica dell'impianto entro il 15 febbraio 2027", oggetto);
        Assert.StartsWith("Gentile Giulia Esempio,", testo, StringComparison.Ordinal);
        Assert.Contains("impianto di riscaldamento (voce di prova) in Via dei Platani 12, Firenze, il controllo di efficienza energetica è in scadenza entro il 15 febbraio 2027", testo, StringComparison.Ordinal);
        Assert.Contains("Siamo in zona il martedì.", testo, StringComparison.Ordinal);
        Assert.Contains("Termoidraulica Prova Srl (fittizia)\r\nVia delle Prove 1 50100 Firenze (FI)", testo, StringComparison.Ordinal);
        Assert.Contains("acconsentito", testo, StringComparison.Ordinal);

        // Una scadenza già passata lo dice.
        var (_, scaduta) = new ComponitorePromemoria().Componi(
            new DatiPromemoria { Tipo = "manutenzione", DataScadenza = dati.DataScadenza, Cliente = dati.Cliente, TipoImpianto = dati.TipoImpianto, Indirizzo = dati.Indirizzo, Comune = dati.Comune },
            s_impresa, null, new DateOnly(2027, 3, 1));
        Assert.Contains("la manutenzione era in scadenza il 15 febbraio 2027 e risulta ancora da fare", scaduta, StringComparison.Ordinal);
    }
}
