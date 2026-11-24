using System.Globalization;
using Gestionale.Application.Interventi;

namespace Gestionale.Infrastructure.Documenti;

/// <summary>
/// Rapportino di un intervento chiuso: chi, dove, quando, cosa è stato fatto,
/// materiali e ore con i totali, spazi per le firme a mano. Non è un documento
/// normativo (quelli arrivano con M4), ma porta comunque il disclaimer.
/// Le ore sono nel fuso italiano, come nel client.
/// </summary>
public sealed class GeneratoreRapportino : IGeneratoreRapportino
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");
    private static readonly TimeZoneInfo s_fuso = TrovaFuso();

    public byte[] Genera(IntestazioneAzienda azienda, Intervento intervento)
    {
        ArgumentNullException.ThrowIfNull(azienda);
        ArgumentNullException.ThrowIfNull(intervento);

        var numero = Formato(TestiDocumenti.NumeroIntervento, intervento.Numero, intervento.Anno);
        var pagina = new Impaginatore(TestiDocumenti.Disclaimer);

        Intestazione(pagina, azienda, numero);

        pagina.Sezione(TestiDocumenti.SezioneIntervento);
        var campi = new List<(string, string)>
        {
            (TestiDocumenti.Tipo, Voce("TipoIntervento_", intervento.Tipo)),
            (TestiDocumenti.DataOrario, Finestra(intervento.Inizio, intervento.Fine)),
            (TestiDocumenti.Tecnico, intervento.Tecnico ?? TestiDocumenti.DaAssegnare),
            (TestiDocumenti.Esito, intervento.Esito is { } esito ? Voce("EsitoIntervento_", esito) : TestiDocumenti.NonIndicato),
        };
        if (intervento.ChiusoIl is { } chiuso)
        {
            campi.Add((TestiDocumenti.ChiusoIl, Locale(chiuso).ToString("d MMMM yyyy, HH:mm", s_italiano)));
        }

        pagina.Campi(campi);

        pagina.Sezione(TestiDocumenti.SezioneClienteImpianto);
        pagina.Campi(
        [
            (TestiDocumenti.Cliente, intervento.Cliente),
            (TestiDocumenti.Indirizzo, $"{intervento.Indirizzo}{(intervento.Civico is null ? "" : " " + intervento.Civico)}, {intervento.Cap} {intervento.Comune} ({intervento.Provincia})"),
            (TestiDocumenti.Impianto, intervento.TipoImpianto),
            (TestiDocumenti.Generatore, intervento.Generatore ?? TestiDocumenti.TuttoLImpianto),
        ]);

        if (intervento.Descrizione is { } richiesta)
        {
            pagina.Sezione(TestiDocumenti.Richiesta);
            pagina.Testo(richiesta);
        }

        if (intervento.NoteChiusura is { } lavoro)
        {
            pagina.Sezione(TestiDocumenti.LavoroSvolto);
            pagina.Testo(lavoro);
        }

        pagina.Sezione(TestiDocumenti.MaterialiOre);
        if (intervento.Righe.Count == 0)
        {
            pagina.Testo(TestiDocumenti.NessunaRiga, secondario: true);
        }
        else
        {
            Righe(pagina, intervento.Righe);
        }

        pagina.Firme(TestiDocumenti.FirmaTecnico, TestiDocumenti.FirmaCliente);

        return pagina.Pdf($"{TestiDocumenti.TitoloRapportino} {numero}", azienda.RagioneSociale);
    }

    private static void Intestazione(Impaginatore pagina, IntestazioneAzienda azienda, string numero)
    {
        pagina.Testo(azienda.RagioneSociale, 14, grassetto: true);
        var recapito = string.Join(" · ", new[]
        {
            azienda.Indirizzo is null ? null : $"{azienda.Indirizzo}, {azienda.Cap} {azienda.Comune}{(azienda.Provincia is null ? "" : $" ({azienda.Provincia})")}".Trim(),
            azienda.PartitaIva is null ? null : Formato(TestiDocumenti.PartitaIva, azienda.PartitaIva),
        }.Where(v => !string.IsNullOrWhiteSpace(v)));
        if (recapito.Length > 0)
        {
            pagina.Testo(recapito, 9, secondario: true);
        }

        pagina.Spazio(14);
        pagina.Testo($"{TestiDocumenti.TitoloRapportino} {numero}", 18, grassetto: true);
        pagina.Spazio(4);
    }

    private static void Righe(Impaginatore pagina, IReadOnlyList<RigaIntervento> righe)
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

        pagina.Tabella(colonne, righe.Select(r => (IReadOnlyList<string>)
        [
            r.Codice is null ? r.Descrizione : $"{r.Codice} · {r.Descrizione}",
            $"{r.Quantita.ToString("0.###", s_italiano)} {r.Unita}",
            Euro(r.PrezzoUnitario),
            r.ScontoPercentuale == 0 ? "" : $"{r.ScontoPercentuale.ToString("0.##", s_italiano)}%",
            r.AliquotaIva is { } iva ? $"{iva.ToString("0.##", s_italiano)}%" : "—",
            Euro(r.Importo),
        ]));

        // IVA per aliquota, arrotondata al centesimo per ogni aliquota; le righe senza aliquota restano fuori.
        var imponibile = righe.Sum(r => r.Importo);
        var iva = righe
            .Where(r => r.AliquotaIva is not null)
            .GroupBy(r => r.AliquotaIva!.Value)
            .Sum(g => decimal.Round(g.Sum(r => r.Importo) * g.Key / 100, 2, MidpointRounding.AwayFromZero));

        pagina.Totali(
        [
            (TestiDocumenti.Imponibile, Euro(imponibile)),
            (TestiDocumenti.Iva, Euro(iva)),
            (TestiDocumenti.Totale, Euro(imponibile + iva)),
        ]);

        if (righe.Any(r => r.AliquotaIva is null))
        {
            pagina.Testo(TestiDocumenti.IvaNonIndicata, 8, secondario: true);
        }
    }

    private static string Finestra(DateTime? inizio, DateTime? fine)
    {
        if (inizio is not { } i || fine is not { } f)
        {
            return TestiDocumenti.NonIndicato;
        }

        var (li, lf) = (Locale(i), Locale(f));
        return li.Date == lf.Date
            ? Formato(TestiDocumenti.FinestraStessoGiorno, li.ToString("dddd d MMMM yyyy", s_italiano), li.ToString("HH:mm", s_italiano), lf.ToString("HH:mm", s_italiano))
            : $"{li.ToString("d MMM yyyy HH:mm", s_italiano)} – {lf.ToString("d MMM yyyy HH:mm", s_italiano)}";
    }

    private static string Euro(decimal importo) => importo.ToString("C", s_italiano);

    private static DateTime Locale(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), s_fuso);

    private static string Voce(string prefisso, string codice) =>
        TestiDocumenti.ResourceManager.GetString(prefisso + codice, TestiDocumenti.Culture) ?? codice;

    private static string Formato(string modello, params object[] valori) => string.Format(s_italiano, modello, valori);

    private static TimeZoneInfo TrovaFuso()
    {
        foreach (var id in new[] { "Europe/Rome", "W. Europe Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var fuso))
            {
                return fuso;
            }
        }

        return TimeZoneInfo.Utc;
    }
}
