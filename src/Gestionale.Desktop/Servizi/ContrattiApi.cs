using System.Text.Json.Nodes;

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

public sealed record Sede(Guid SedeId, string? Descrizione, string Indirizzo, string? Civico, string Cap, string Comune, string Provincia, string? NoteAccesso, int Versione, string? CodiceIstatComune = null);

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

public sealed record DatiSede(string? Descrizione, string Indirizzo, string? Civico, string Cap, string Comune, string Provincia, string? NoteAccesso, int? Versione, string? CodiceIstatComune = null);

public sealed record DatiContatto(string Tipo, string Valore, string? Etichetta, bool Principale, int? Versione);

/// <summary>Dati dell'impresa; la regione si legge soltanto.</summary>
public sealed record Azienda(
    Guid AziendaId,
    string RagioneSociale,
    string? PartitaIva,
    string? CodiceFiscale,
    string? Indirizzo,
    string? Cap,
    string? Comune,
    string? Provincia,
    string Regione,
    int? CodiceDittaSiert,
    int Versione);

public sealed record DatiAzienda(
    string RagioneSociale,
    string? PartitaIva,
    string? CodiceFiscale,
    string? Indirizzo,
    string? Cap,
    string? Comune,
    string? Provincia,
    int? CodiceDittaSiert,
    int Versione);

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
    bool MatricolaDuplicata,
    string? SiglaCatasto = null);

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
    int? ManutenzioneMesi,
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
    int? ManutenzioneMesi,
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
    int? Versione,
    string? SiglaCatasto = null);

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
    bool ControlloEfficienza,
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

public sealed record DatiChiusura(
    int Versione,
    string Esito,
    string? Note,
    IReadOnlyList<DatiRiga> Righe,
    bool ControlloEfficienza = false,
    string? ModelloRapporto = null,
    Guid? GeneratoreRapporto = null);

public sealed record EsitoScritturaIntervento(Guid Id, int Versione, int Anno, int Numero, Guid? RapportoId = null);

// --- Scadenze ----------------------------------------------------------
// Le calcola il server: il client le legge, corregge la data e registra i richiami.

/// <summary>Riga della lista da richiamare; GiorniMancanti è negativo per le scadute. Per il Tecnico telefono e richiami sono vuoti.</summary>
public sealed record ScadenzaInArrivo(
    Guid ScadenzaId,
    Guid ImpiantoId,
    string Tipo,
    DateOnly DataScadenza,
    int GiorniMancanti,
    string StatoRichiamo,
    int Versione,
    Guid ClienteId,
    string Cliente,
    string Indirizzo,
    string Comune,
    string Provincia,
    string? Telefono,
    DateTime? UltimoRichiamoIl,
    string? UltimoEsito,
    DateOnly? RichiamareIl);

public sealed record Richiamo(
    Guid RichiamoId,
    string Canale,
    DateTime EffettuatoIl,
    string? EffettuatoDa,
    string Esito,
    DateOnly? RichiamareIl,
    Guid? InterventoId,
    string? Note);

public sealed record Scadenza(
    Guid ScadenzaId,
    Guid ImpiantoId,
    string Tipo,
    DateOnly DataScadenza,
    string Stato,
    int PeriodicitaMesi,
    string? Regola,
    bool RegolaDaVerificare,
    bool DataManuale,
    Guid? InterventoOrigineId,
    Guid? InterventoChiusuraId,
    DateTime? ChiusaIl,
    DateTime? AnnullataIl,
    string? MotivoAnnullamento,
    string StatoRichiamo,
    int Versione,
    IReadOnlyList<Richiamo> Richiami);

public sealed record DatiRichiamo(string Canale, string Esito, DateOnly? RichiamareIl, Guid? InterventoId, string? Note, int Versione);

// --- Listino e preventivi -----------------------------------------------

public sealed record PrezzoListino(Guid PrezzoId, decimal Prezzo, DateOnly ValidoDal, DateTime CreatoIl);

/// <summary>Voce con il prezzo in vigore oggi (null se non ne ha ancora uno); Prezzi solo nella scheda.</summary>
public sealed record VoceListino(
    Guid VoceId,
    string Codice,
    string Descrizione,
    string Tipo,
    string Unita,
    decimal? AliquotaIva,
    bool Attiva,
    string? Note,
    decimal? Prezzo,
    DateOnly? PrezzoDal,
    int Versione,
    IReadOnlyList<PrezzoListino>? Prezzi);

/// <summary>PrezzoIniziale e ValidoDal valgono solo alla creazione.</summary>
public sealed record DatiVoce(
    string Codice, string Descrizione, string Tipo, string Unita, decimal? AliquotaIva, bool Attiva, string? Note,
    decimal? PrezzoIniziale, DateOnly? ValidoDal, int? Versione);

public sealed record DatiPrezzo(decimal Prezzo, DateOnly? ValidoDal);

public sealed record RiepilogoPreventivo(
    Guid PreventivoId,
    int? Anno,
    int? Numero,
    string Stato,
    bool Scaduto,
    Guid ClienteId,
    string Cliente,
    string Oggetto,
    DateOnly? EmessoIl,
    DateOnly? ScadeIl,
    decimal Totale,
    Guid? InterventoId,
    DateTime CreatoIl,
    int Versione);

public sealed record RigaPreventivo(
    int Posizione,
    Guid? VoceId,
    string Tipo,
    string? Codice,
    string Descrizione,
    string Unita,
    decimal Quantita,
    decimal PrezzoUnitario,
    decimal ScontoPercentuale,
    decimal? AliquotaIva,
    decimal Importo);

public sealed record Preventivo(
    Guid PreventivoId,
    int? Anno,
    int? Numero,
    string Stato,
    bool Scaduto,
    Guid ClienteId,
    string Cliente,
    string? CodiceFiscale,
    string? PartitaIva,
    Guid? SedeId,
    string? Indirizzo,
    string? Civico,
    string? Cap,
    string? Comune,
    string? Provincia,
    Guid? ImpiantoId,
    string? TipoImpianto,
    string Oggetto,
    string? Note,
    int ValiditaGiorni,
    DateOnly? EmessoIl,
    DateOnly? ScadeIl,
    decimal Imponibile,
    decimal Iva,
    decimal Totale,
    DateTime? AccettatoIl,
    DateTime? RifiutatoIl,
    DateTime? AnnullatoIl,
    string? MotivoAnnullamento,
    Guid? InterventoId,
    DateTime CreatoIl,
    int Versione,
    IReadOnlyList<RigaPreventivo> Righe);

public sealed record DatiRigaPreventivo(
    Guid? VoceId, string Tipo, string? Codice, string Descrizione, string Unita, decimal Quantita, decimal? PrezzoUnitario, decimal? ScontoPercentuale, decimal? AliquotaIva);

public sealed record DatiPreventivo(
    Guid ClienteId, Guid? SedeId, Guid? ImpiantoId, string Oggetto, string? Note, int ValiditaGiorni, IReadOnlyList<DatiRigaPreventivo> Righe, int? Versione);

/// <summary>InterventoId c'è dopo la generazione dell'intervento.</summary>
public sealed record EsitoScritturaCommerciale(Guid Id, int Versione, int? Anno, int? Numero, Guid? InterventoId);

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

// --- Rapporti di controllo, bollini e invii al SIERT (M4) ---------------
// I dati del rapporto sono nella codifica del tracciato: chiavi di norma.tracciati_campi, valori testo o numero.

public sealed record RiepilogoRapporto(
    Guid RapportoControlloId,
    Guid ImpiantoId,
    Guid GeneratoreId,
    Guid? InterventoId,
    string Modello,
    DateOnly DataControllo,
    DateOnly? TermineTrasmissione,
    string Stato,
    string? Cliente,
    string? Indirizzo,
    string? Comune,
    string? CodiceBollino,
    int Versione);

public sealed record ErroreValidazione(string? Campo, string Messaggio, bool Bloccante);

public sealed record RapportoControllo(
    Guid RapportoControlloId,
    Guid ImpiantoId,
    Guid GeneratoreId,
    Guid? InterventoId,
    string Modello,
    Guid? TecnicoId,
    string? Tecnico,
    DateOnly DataControllo,
    DateOnly? TermineTrasmissione,
    string Stato,
    JsonObject Dati,
    Guid? BollinoId,
    string? CodiceBollino,
    string? TipoBollino,
    DateTime? ConvalidatoIl,
    DateTime? FirmatoIl,
    DateTime? AnnullatoIl,
    string? MotivoAnnullamento,
    DateTime CreatoIl,
    int Versione,
    IReadOnlyList<ErroreValidazione> Errori);

public sealed record CampoTracciato(
    int Posizione,
    int NumeroCampo,
    string Nome,
    string Tipo,
    int? Lunghezza,
    bool Obbligatorio,
    IReadOnlyList<string>? ValoriAmmessi,
    string? Formato,
    string Origine,
    string Chiave);

public sealed record DatiNuovoRapporto(Guid ImpiantoId, Guid GeneratoreId, Guid? InterventoId, string Modello, DateOnly DataControllo);

public sealed record DatiBozzaRapporto(DateOnly DataControllo, JsonObject Dati, int Versione);

public sealed record EsitoScritturaRapporto(Guid Id, int Versione);

public sealed record TipoBollino(Guid TipoBollinoId, string Codice, string Categoria, string Nome, decimal Importo, int Disponibili);

public sealed record Bollino(
    Guid BollinoId,
    string Codice,
    Guid TipoBollinoId,
    string TipoBollino,
    string Categoria,
    decimal Importo,
    DateOnly AcquistatoIl,
    string Stato,
    Guid? RapportoControlloId,
    string? Note,
    int Versione);

public sealed record DatiAcquistoBollini(Guid TipoBollinoId, DateOnly AcquistatoIl, IReadOnlyList<string> Codici, string? Note);

public sealed record EsitoBolliniRegistrati(int Numero);

public sealed record RiepilogoInvio(
    Guid InvioCatastoId,
    string Modello,
    string NomeFile,
    string VersioneTracciato,
    string Stato,
    int Righe,
    DateTime? CaricatoIl,
    DateTime? ElaboratoIl,
    DateTime CreatoIl,
    int Versione);

public sealed record RigaInvio(int Riga, Guid RapportoControlloId, Guid ImpiantoId, DateOnly DataControllo, string? Cliente, string Esito, string? Causa);

public sealed record InvioCatasto(RiepilogoInvio Invio, IReadOnlyList<RigaInvio> Rapporti);

public sealed record EsitoInvioPreparato(Guid Id, int Versione, int Righe, IReadOnlyList<Guid> Esclusi);

public sealed record EsitoRigaInvio(Guid RapportoControlloId, string Esito, string? Causa);

// --- Importazione da CSV (M4b) ------------------------------------------

public sealed record SegnalazioneImportazione(int Riga, string Codice, string Messaggio);

public sealed record EsitoImportazione(
    bool Importata,
    int Righe,
    int ClientiNuovi,
    int ClientiEsistenti,
    int SediNuove,
    int ImpiantiNuovi,
    int ImpiantiSaltati,
    int Generatori,
    IReadOnlyList<SegnalazioneImportazione> Errori,
    IReadOnlyList<SegnalazioneImportazione> Avvisi);
