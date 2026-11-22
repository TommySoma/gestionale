using Gestionale.Migrator;

// La stringa di connessione arriva dalla riga di comando o dalla variabile d'ambiente,
// mai dal codice: l'utente è app_owner, proprietario del database.
const string VariabileAmbiente = "GESTIONALE_DB_MIGRAZIONI";

var stringaConnessione = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable(VariabileAmbiente);

if (string.IsNullOrWhiteSpace(stringaConnessione))
{
    Console.Error.WriteLine($"Stringa di connessione mancante: passala come argomento o nella variabile {VariabileAmbiente}.");
    return 2;
}

var esito = Migratore.Applica(stringaConnessione);

if (!esito.Successful)
{
    Console.Error.WriteLine($"Migrazione fallita: {esito.Error?.Message}");
    return 1;
}

Console.WriteLine("Database aggiornato.");
return 0;
