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

/// <summary>Risposta di errore dell'API (RFC 9457): il campo detail è già un messaggio per l'utente.</summary>
internal sealed record ProblemaApi(string? Title, string? Detail, int? Status);
