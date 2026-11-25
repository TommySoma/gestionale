using System.Globalization;
using Gestionale.Application.Commerciale;
using Gestionale.Application.Interventi;

namespace Gestionale.Infrastructure.Documenti;

/// <summary>
/// PDF del preventivo (ADR 0005): testata dell'impresa, cliente e luogo, oggetto,
/// righe con i totali salvati dal database, validità e spazio per l'accettazione
/// del cliente. Una bozza non ha numero e lo dice nel titolo; un preventivo
/// annullato o rifiutato lo dice in evidenza. Porta il disclaimer a piè di pagina.
/// </summary>
public sealed class GeneratorePreventivo : IGeneratorePreventivo
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public byte[] Genera(IntestazioneAzienda azienda, Preventivo preventivo)
    {
        ArgumentNullException.ThrowIfNull(azienda);
        ArgumentNullException.ThrowIfNull(preventivo);

        var titolo = preventivo.Numero is { } numero
            ? $"{TestiDocumenti.TitoloPreventivo} {Formato(TestiDocumenti.NumeroPreventivo, numero, preventivo.Anno ?? 0)}"
            : TestiDocumenti.TitoloBozzaPreventivo;
        var pagina = new Impaginatore(TestiDocumenti.Disclaimer);

        GeneratoreRapportino.Intestazione(pagina, azienda, titolo);

        if (Avviso(preventivo) is { } avviso)
        {
            pagina.Testo(avviso, 11, grassetto: true);
            pagina.Spazio(4);
        }

        pagina.Sezione(TestiDocumenti.SezionePreventivo);
        pagina.Campi(
        [
            (TestiDocumenti.Data, preventivo.EmessoIl is { } emesso ? Data(emesso) : TestiDocumenti.NonEmesso),
            (TestiDocumenti.ValidoFinoAl, preventivo.ScadeIl is { } scade
                ? Data(scade)
                : Formato(TestiDocumenti.ValiditaGiorni, preventivo.ValiditaGiorni)),
        ]);

        pagina.Sezione(TestiDocumenti.SezioneCliente);
        var cliente = new List<(string, string)> { (TestiDocumenti.Cliente, preventivo.Cliente) };
        if (preventivo.PartitaIva is { } partitaIva)
        {
            cliente.Add((TestiDocumenti.PartitaIvaCliente, partitaIva));
        }
        else if (preventivo.CodiceFiscale is { } codiceFiscale)
        {
            cliente.Add((TestiDocumenti.CodiceFiscale, codiceFiscale));
        }

        if (preventivo.Indirizzo is { } indirizzo)
        {
            cliente.Add((TestiDocumenti.LuogoLavori,
                $"{indirizzo}{(preventivo.Civico is null ? "" : " " + preventivo.Civico)}, {preventivo.Cap} {preventivo.Comune} ({preventivo.Provincia})"));
        }

        if (preventivo.TipoImpianto is { } impianto)
        {
            cliente.Add((TestiDocumenti.Impianto, impianto));
        }

        pagina.Campi(cliente);

        pagina.Sezione(TestiDocumenti.Oggetto);
        pagina.Testo(preventivo.Oggetto);

        pagina.Sezione(TestiDocumenti.VociPreventivo);
        if (preventivo.Righe.Count == 0)
        {
            pagina.Testo(TestiDocumenti.NessunaRiga, secondario: true);
        }
        else
        {
            Righe(pagina, preventivo);
        }

        if (preventivo.Note is { } note)
        {
            pagina.Sezione(TestiDocumenti.Note);
            pagina.Testo(note);
        }

        pagina.Firme(TestiDocumenti.FirmaImpresa, TestiDocumenti.FirmaAccettazione);

        return pagina.Pdf(titolo, azienda.RagioneSociale);
    }

    private static string? Avviso(Preventivo preventivo) => preventivo.Stato switch
    {
        "bozza" => TestiDocumenti.AvvisoBozza,
        "annullato" => Formato(TestiDocumenti.AvvisoAnnullato, preventivo.MotivoAnnullamento ?? ""),
        "rifiutato" => TestiDocumenti.AvvisoRifiutato,
        _ when preventivo.Scaduto => TestiDocumenti.AvvisoScaduto,
        _ => null,
    };

    // I totali sono quelli salvati: il database li calcola per aliquota, come il rapportino.
    private static void Righe(Impaginatore pagina, Preventivo preventivo)
    {
        var larghezza = Impaginatore.LarghezzaUtile;
        var colonne = new[]
        {
            new Colonna(TestiDocumenti.ColDescrizione, larghezza - 300),
            new Colonna(TestiDocumenti.ColQuantita, 60, Allineamento.Destra),
            new Colonna(TestiDocumenti.ColPrezzo, 70, Allineamento.Destra),
            new Colonna(TestiDocumenti.ColSconto, 50, Allineamento.Destra),
            new Colonna(TestiDocumenti.ColIva, 45, Allineamento.Destra),
            new Colonna(TestiDocumenti.ColImporto, 75, Allineamento.Destra),
        };

        pagina.Tabella(colonne, preventivo.Righe.Select(r => (IReadOnlyList<string>)
        [
            r.Codice is null ? r.Descrizione : $"{r.Codice} · {r.Descrizione}",
            $"{r.Quantita.ToString("0.###", s_italiano)} {r.Unita}",
            Euro(r.PrezzoUnitario),
            r.ScontoPercentuale == 0 ? "" : $"{r.ScontoPercentuale.ToString("0.##", s_italiano)}%",
            r.AliquotaIva is { } iva ? $"{iva.ToString("0.##", s_italiano)}%" : "—",
            Euro(r.Importo),
        ]));

        pagina.Totali(
        [
            (TestiDocumenti.Imponibile, Euro(preventivo.Imponibile)),
            (TestiDocumenti.Iva, Euro(preventivo.Iva)),
            (TestiDocumenti.Totale, Euro(preventivo.Totale)),
        ]);

        if (preventivo.Righe.Any(r => r.AliquotaIva is null))
        {
            pagina.Testo(TestiDocumenti.IvaNonIndicata, 8, secondario: true);
        }
    }

    private static string Data(DateOnly data) => data.ToString("d MMMM yyyy", s_italiano);

    private static string Euro(decimal importo) => importo.ToString("C", s_italiano);

    private static string Formato(string modello, params object[] valori) => string.Format(s_italiano, modello, valori);
}
