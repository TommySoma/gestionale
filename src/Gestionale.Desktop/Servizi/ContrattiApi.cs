namespace Gestionale.Desktop.Servizi;

// Copie dei contratti JSON dell'API. Il client non referenzia i progetti del
// server: parla solo con l'API, e questi tipi descrivono cosa si scambiano.

public sealed record TokenSessione(string TokenAccesso, DateTimeOffset AccessoScadeIl, string TokenRinnovo, DateTimeOffset RinnovoScadeIl);

public sealed record RispostaAccesso(string Esito, TokenSessione? Token, string? TokenPassaggio);

public sealed record ConfigurazioneMfa(string Segreto, string UriConfigurazione);

public sealed record AttivazioneMfa(IReadOnlyList<string> CodiciRecupero, TokenSessione? Token);

public sealed record Permesso(string Codice, string Ambito);

public sealed record Profilo(Guid UtenteId, Guid AziendaId, string Email, string Nome, string Cognome, IReadOnlyList<string> Ruoli, IReadOnlyList<Permesso> Permessi)
{
    public bool Ha(string codice) => Permessi.Any(p => p.Codice == codice);
}

public sealed record Pagina<T>(IReadOnlyList<T> Elementi, int Totale, int NumeroPagina, int Dimensione);

public sealed record RiepilogoCliente(Guid ClienteId, string Tipo, string NomeVisualizzato, string? CodiceFiscale, string? PartitaIva, string? ComunePrincipale, int Versione);

public sealed record Sede(Guid SedeId, string? Descrizione, string Indirizzo, string? Civico, string Cap, string Comune, string Provincia, string? NoteAccesso, int Versione);

public sealed record Contatto(Guid ContattoId, string Tipo, string Valore, string? Etichetta, bool Principale, int Versione);

public sealed record ConsensoCorrente(string Finalita, bool Concesso, string Fonte, DateTimeOffset RegistratoIl);

public sealed record Cliente(
    Guid ClienteId,
    string Tipo,
    string? Denominazione,
    string? Nome,
    string? Cognome,
    string? CodiceFiscale,
    string? PartitaIva,
    string? Pec,
    string? Note,
    int Versione,
    IReadOnlyList<Sede> Sedi,
    IReadOnlyList<Contatto> Contatti,
    IReadOnlyList<ConsensoCorrente> Consensi);

public sealed record FinalitaPrivacy(string Codice, string Nome, string Descrizione);

public sealed record DatiCliente(string Tipo, string? Denominazione, string? Nome, string? Cognome, string? CodiceFiscale, string? PartitaIva, string? Pec, string? Note, int? Versione);

public sealed record DatiSede(string? Descrizione, string Indirizzo, string? Civico, string Cap, string Comune, string Provincia, string? NoteAccesso, int? Versione);

public sealed record DatiContatto(string Tipo, string Valore, string? Etichetta, bool Principale, int? Versione);

public sealed record DatiConsenso(string Finalita, bool Concesso, string Fonte, string? Note);

public sealed record EsitoScrittura(Guid Id, int Versione);

public sealed record RiepilogoImpianto(
    Guid ImpiantoId,
    string TipoImpianto,
    string Stato,
    Guid SedeId,
    Guid ClienteId,
    string Cliente,
    string Indirizzo,
    string? Civico,
    string Comune,
    string Provincia,
    string? Pdr,
    string? CodiceCatasto,
    int NumeroGeneratori,
    int Versione);

public sealed record Generatore(
    Guid GeneratoreId,
    Guid TipoGeneratoreId,
    string TipoGeneratore,
    string? Marca,
    string? Modello,
    string? Matricola,
    decimal? PotenzaKw,
    Guid? CombustibileId,
    string? Combustibile,
    DateOnly? InstallatoIl,
    DateOnly? DismessoIl,
    string? Note,
    int Versione,
    bool MatricolaDuplicata);

public sealed record RiferimentoCatasto(Guid RiferimentoCatastoId, string Regione, string Sistema, string Codice, DateOnly? RegistratoIl, int Versione);

public sealed record Impianto(
    Guid ImpiantoId,
    Guid SedeId,
    Guid ClienteId,
    string Cliente,
    string Indirizzo,
    string? Civico,
    string Cap,
    string Comune,
    string Provincia,
    Guid TipoImpiantoId,
    string TipoImpianto,
    bool TipoDaVerificare,
    string Stato,
    int? AnnoInstallazione,
    decimal? PotenzaUtileKw,
    Guid? CombustibileId,
    string? Combustibile,
    string? Pdr,
    Guid? ProprietarioClienteId,
    string? Proprietario,
    Guid? ResponsabileClienteId,
    string? Responsabile,
    string? Note,
    DateOnly? DismessoIl,
    int Versione,
    bool IndirizzoDuplicato,
    IReadOnlyList<Generatore> Generatori,
    IReadOnlyList<RiferimentoCatasto> RiferimentiCatasto);

public sealed record VoceLookup(Guid Id, string Codice, string Nome, bool DaVerificare);

public sealed record VociImpianti(IReadOnlyList<VoceLookup> TipiImpianto, IReadOnlyList<VoceLookup> TipiGeneratore, IReadOnlyList<VoceLookup> Combustibili);

public sealed record DatiImpianto(
    Guid SedeId,
    Guid TipoImpiantoId,
    string Stato,
    int? AnnoInstallazione,
    decimal? PotenzaUtileKw,
    Guid? CombustibileId,
    string? Pdr,
    Guid? ProprietarioClienteId,
    Guid? ResponsabileClienteId,
    string? Note,
    DateOnly? DismessoIl,
    int? Versione);

public sealed record DatiGeneratore(
    Guid TipoGeneratoreId,
    string? Marca,
    string? Modello,
    string? Matricola,
    decimal? PotenzaKw,
    Guid? CombustibileId,
    DateOnly? InstallatoIl,
    DateOnly? DismessoIl,
    string? Note,
    int? Versione);

public sealed record DatiRiferimentoCatasto(string Regione, string Codice, DateOnly? RegistratoIl, int? Versione);

/// <summary>Le scritture sugli impianti possono riuscire con avvisi: doppioni sospetti da controllare.</summary>
public sealed record EsitoScritturaImpianto(Guid Id, int Versione, IReadOnlyList<string> Avvisi);

// --- Interventi ---------------------------------------------------------
// Inizio, fine e date di chiusura arrivano in UTC: il client le mostra nel fuso italiano.

public sealed record RiepilogoIntervento(
    Guid InterventoId,
    int Anno,
    int Numero,
    string Tipo,
    string Stato,
    string? Esito,
    DateTime? Inizio,
    DateTime? Fine,
    Guid? TecnicoUtenteId,
    string? Tecnico,
    Guid ImpiantoId,
    string TipoImpianto,
    string Cliente,
    string Indirizzo,
    string? Civico,
    string Comune,
    string Provincia,
    string? Descrizione,
    int Versione);

public sealed record RigaIntervento(
    int Posizione,
    string Tipo,
    string? Codice,
    string Descrizione,
    string Unita,
    decimal Quantita,
    decimal PrezzoUnitario,
    decimal ScontoPercentuale,
    decimal? AliquotaIva,
    decimal Importo);

public sealed record Intervento(
    Guid InterventoId,
    int Anno,
    int Numero,
    string Tipo,
    string Stato,
    string? Esito,
    DateTime? Inizio,
    DateTime? Fine,
    Guid? TecnicoUtenteId,
    string? Tecnico,
    Guid ImpiantoId,
    string TipoImpianto,
    Guid? GeneratoreId,
    string? Generatore,
    Guid ClienteId,
    string Cliente,
    string Indirizzo,
    string? Civico,
    string Cap,
    string Comune,
    string Provincia,
    string? Descrizione,
    string? NoteChiusura,
    DateTime? ChiusoIl,
    DateTime? AnnullatoIl,
    string? MotivoAnnullamento,
    int Versione,
    IReadOnlyList<RigaIntervento> Righe,
    decimal Totale);

public sealed record TecnicoAssegnabile(Guid UtenteId, string Nome);

public sealed record DatiIntervento(
    Guid ImpiantoId,
    Guid? GeneratoreId,
    string Tipo,
    string? Descrizione,
    DateTimeOffset? Inizio,
    DateTimeOffset? Fine,
    Guid? TecnicoUtenteId,
    int? Versione);

public sealed record DatiRiga(
    string Tipo,
    string? Codice,
    string Descrizione,
    string Unita,
    decimal Quantita,
    decimal? PrezzoUnitario,
    decimal? ScontoPercentuale,
    decimal? AliquotaIva);

public sealed record DatiChiusura(int Versione, string Esito, string? Note, IReadOnlyList<DatiRiga> Righe);

public sealed record EsitoScritturaIntervento(Guid Id, int Versione, int Anno, int Numero);

// --- Documenti ----------------------------------------------------------

public sealed record Documento(
    Guid DocumentoId,
    string Categoria,
    string NomeOriginale,
    string TipoMime,
    long Dimensione,
    string? Descrizione,
    DateTime CreatoIl,
    string? CreatoDa,
    int Versione);

/// <summary>File da caricare: il client lo invia come modulo multipart, ricostruito a ogni tentativo.</summary>
public sealed record ModuloFile(byte[] Dati, string Nome, string? Descrizione);

/// <summary>Risposta di errore dell'API (RFC 9457): il campo detail è già un messaggio per l'utente.</summary>
internal sealed record ProblemaApi(string? Title, string? Detail, int? Status);
