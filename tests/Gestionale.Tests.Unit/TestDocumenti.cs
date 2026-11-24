using System.Security.Cryptography;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Documenti;
using Gestionale.Application.Impianti;
using Gestionale.Domain.Documenti;
using Gestionale.Domain.Impianti;
using Gestionale.Domain.Sicurezza;
using Gestionale.Infrastructure.Documenti;

namespace Gestionale.Tests.Unit;

public sealed class TestRegoleDocumenti
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 }, "application/pdf")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0 }, null)]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0, 0, 0, 0, 0 }, null)]
    [InlineData(new byte[] { 0xFF, 0xD8 }, null)]
    public void Il_tipo_si_riconosce_dai_primi_byte(byte[] inizio, string? atteso) =>
        Assert.Equal(atteso, RegoleDocumenti.TipoDaFirma(inizio));

    [Theory]
    [InlineData("caldaia.jpg", "image/jpeg", "caldaia.jpg")]
    [InlineData(@"C:\Users\prova\Desktop\foto caldaia.JPG", "image/jpeg", "foto caldaia.jpg")]
    [InlineData("../../etc/passwd", "application/pdf", "passwd.pdf")]
    [InlineData("libretto.exe", "application/pdf", "libretto.pdf")]
    [InlineData("  \"<?>.png", "image/png", "foto.png")]
    [InlineData(null, "application/pdf", "documento.pdf")]
    public void Il_nome_perde_cartelle_e_caratteri_pericolosi_e_prende_l_estensione_del_tipo(string? nome, string tipo, string atteso) =>
        Assert.Equal(atteso, RegoleDocumenti.NomeSicuro(nome, tipo));

    [Fact]
    public void Un_nome_lunghissimo_si_accorcia_tenendo_l_estensione()
    {
        var nome = RegoleDocumenti.NomeSicuro(new string('a', 400) + ".pdf", RegoleDocumenti.Pdf);

        Assert.Equal(RegoleDocumenti.LunghezzaMassimaNome, nome.Length);
        Assert.EndsWith(".pdf", nome, StringComparison.Ordinal);
    }
}

public sealed class TestArchivioDocumenti : IDisposable
{
    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "gestionale-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cartella))
        {
            Directory.Delete(_cartella, recursive: true);
        }
    }

    [Fact]
    public async Task Il_file_si_salva_rinominato_con_dimensione_impronta_e_primi_byte()
    {
        var archivio = new ArchivioDocumentiSuDisco(_cartella);
        var (azienda, documento) = (Guid.NewGuid(), Guid.NewGuid());
        var contenuto = Jpeg(200_000);

        var file = (await archivio.SalvaAsync(azienda, documento, new MemoryStream(contenuto), 1_000_000, TestContext.Current.CancellationToken))!;

        Assert.Equal($"{azienda:D}/{documento:D}", file.Percorso);
        Assert.Equal(contenuto.Length, file.Dimensione);
        Assert.Equal(SHA256.HashData(contenuto), file.Sha256);
        Assert.Equal(contenuto[..8], file.Inizio);
        await using var letto = archivio.Apri(file.Percorso);
        using var copia = new MemoryStream();
        await letto.CopyToAsync(copia, TestContext.Current.CancellationToken);
        Assert.Equal(contenuto, copia.ToArray());
        Assert.Empty(Directory.GetFiles(_cartella, "*.parziale", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Oltre_il_limite_non_resta_nulla_sul_disco()
    {
        var archivio = new ArchivioDocumentiSuDisco(_cartella);

        var file = await archivio.SalvaAsync(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream(Jpeg(5000)), 4096, TestContext.Current.CancellationToken);

        Assert.Null(file);
        Assert.Empty(Directory.GetFiles(_cartella, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Un_percorso_che_esce_dalla_cartella_è_rifiutato()
    {
        var archivio = new ArchivioDocumentiSuDisco(_cartella);

        Assert.Throws<InvalidOperationException>(() => archivio.Apri("../fuori"));
    }

    internal static byte[] Jpeg(int dimensione)
    {
        var dati = new byte[dimensione];
        Random.Shared.NextBytes(dati);
        dati[0] = 0xFF;
        dati[1] = 0xD8;
        dati[2] = 0xFF;
        return dati;
    }
}

public sealed class TestServizioDocumenti
{
    private static readonly Contesto s_contesto = new(Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public async Task Un_file_di_tipo_non_ammesso_viene_scartato_dal_disco()
    {
        var archivio = new ArchivioFinto();
        var repository = new RepositoryFinto();
        var servizio = new ServizioDocumenti(repository, archivio, new ImpiantiFinti(assegnato: true));

        var esito = await servizio.CaricaAsync(s_contesto, AmbitoPermesso.Tutti, Guid.NewGuid(), "virus.exe", null,
            new MemoryStream([0x4D, 0x5A, 0x90, 0, 1, 2, 3, 4]), TestContext.Current.CancellationToken);

        Assert.Equal(ErroreDocumento.TipoNonAmmesso, Assert.IsType<EsitoDocumento.NonValido>(esito).Errore);
        Assert.Single(archivio.Scartati);
        Assert.Null(repository.Creato);
    }

    [Fact]
    public async Task Il_tecnico_carica_solo_sugli_impianti_assegnati_e_non_elimina()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new RepositoryFinto();
        var assegnato = new ServizioDocumenti(repository, new ArchivioFinto(), new ImpiantiFinti(assegnato: true));
        var altrui = new ServizioDocumenti(repository, new ArchivioFinto(), new ImpiantiFinti(assegnato: false));

        var caricato = await assegnato.CaricaAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), "caldaia.jpg", " Vista frontale ",
            new MemoryStream(TestArchivioDocumenti.Jpeg(100)), ct);
        Assert.IsType<EsitoDocumento.Caricato>(caricato);
        Assert.Equal(("foto", "image/jpeg", "caldaia.jpg", "Vista frontale"),
            (repository.Creato!.Categoria, repository.Creato.TipoMime, repository.Creato.NomeOriginale, repository.Creato.Descrizione));

        Assert.IsType<EsitoDocumento.NonTrovato>(await altrui.CaricaAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), "x.jpg", null,
            new MemoryStream(TestArchivioDocumenti.Jpeg(100)), ct));
        Assert.Null(await altrui.ElencoAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), ct));
        Assert.IsType<EsitoDocumento.Vietato>(await assegnato.EliminaAsync(s_contesto, AmbitoPermesso.Propri, Guid.NewGuid(), Guid.NewGuid(), 1, ct));
    }

    private sealed class ArchivioFinto : IArchivioDocumenti
    {
        public List<string> Scartati { get; } = [];

        public async Task<FileSalvato?> SalvaAsync(Guid aziendaId, Guid documentoId, Stream contenuto, long massimo, CancellationToken ct)
        {
            using var copia = new MemoryStream();
            await contenuto.CopyToAsync(copia, ct);
            var dati = copia.ToArray();
            return new FileSalvato($"{aziendaId:D}/{documentoId:D}", dati.Length, SHA256.HashData(dati), dati[..Math.Min(8, dati.Length)]);
        }

        public Stream Apri(string percorso) => throw new InvalidOperationException();

        public void Scarta(string percorso) => Scartati.Add(percorso);
    }

    private sealed class RepositoryFinto : IRepositoryDocumenti
    {
        public NuovoDocumento? Creato { get; private set; }

        public Task<IReadOnlyList<Documento>> ElencoAsync(Contesto contesto, string entita, Guid entitaId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Documento>>([]);

        public Task<(Documento Documento, string Percorso)?> DocumentoAsync(Contesto contesto, string entita, Guid entitaId, Guid documentoId, CancellationToken ct) =>
            Task.FromResult<(Documento, string)?>(null);

        public Task<Documento> CreaAsync(Contesto contesto, NuovoDocumento documento, CancellationToken ct)
        {
            Creato = documento;
            return Task.FromResult(new Documento { DocumentoId = documento.DocumentoId, NomeOriginale = documento.NomeOriginale, Versione = 1 });
        }

        public Task<bool?> EliminaAsync(Contesto contesto, string entita, Guid entitaId, Guid documentoId, int versione, CancellationToken ct) =>
            throw new InvalidOperationException("Il servizio non doveva eliminare.");
    }

    /// <summary>Impianto sempre visibile; "assegnato" dice se il Tecnico ha un intervento su di esso.</summary>
    private sealed class ImpiantiFinti(bool assegnato) : IRepositoryImpianti
    {
        public Task<bool> AssegnatoAsync(Contesto contesto, Guid impiantoId, CancellationToken ct) => Task.FromResult(assegnato);

        public Task<Impianto?> ImpiantoAsync(Contesto contesto, Guid impiantoId, CancellationToken ct) => Task.FromResult<Impianto?>(new Impianto());

        public Task<Pagina<RiepilogoImpianto>> CercaAsync(Contesto contesto, FiltroImpianti filtro, int numeroPagina, int dimensione, CancellationToken ct) => throw new NotSupportedException();

        public Task<VociImpianti> VociAsync(Contesto contesto, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> CreaAsync(Contesto contesto, DatiImpianto dati, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> AggiornaAsync(Contesto contesto, Guid impiantoId, int versione, DatiImpianto dati, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> EliminaAsync(Contesto contesto, Guid impiantoId, int versione, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> CreaGeneratoreAsync(Contesto contesto, Guid impiantoId, DatiGeneratore dati, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> AggiornaGeneratoreAsync(Contesto contesto, Guid impiantoId, Guid generatoreId, int versione, DatiGeneratore dati, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> CreaRiferimentoAsync(Contesto contesto, Guid impiantoId, DatiRiferimentoCatasto dati, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> AggiornaRiferimentoAsync(Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, DatiRiferimentoCatasto dati, CancellationToken ct) => throw new NotSupportedException();

        public Task<EsitoImpianto> EliminaRiferimentoAsync(Contesto contesto, Guid impiantoId, Guid riferimentoId, int versione, CancellationToken ct) => throw new NotSupportedException();
    }
}
