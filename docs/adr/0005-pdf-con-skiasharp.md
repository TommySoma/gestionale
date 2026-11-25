# ADR 0005 – Documenti PDF con SkiaSharp, generati nell'API

- **Stato:** accettato
- **Data:** 24 novembre 2026
- **Riguarda:** rapportino di intervento (M2) e, in seguito, rapporti di controllo e dichiarazioni di conformità (M4)

## Contesto

Il piano prevedeva QuestPDF, da verificare per la licenza. Le verifiche del 24/11/2026:

| Libreria | Licenza | Problema |
| --- | --- | --- |
| QuestPDF | Community License v3.0, "source-available" | Gratis sotto 1 milione di dollari di fatturato; pagano le imprese più grandi e gli enti pubblici, cioè chi installa il gestionale. È una libreria passata a licenza commerciale, come quelle che il progetto evita. |
| PDFsharp / MigraDoc 6.5 | AGPL-3.0 o commerciale | Incompatibile con la distribuzione MIT del progetto. |
| SkiaSharp 3.119 | MIT | Scrive PDF (`SKDocument.CreatePdf`), ma non impagina: testo a capo, tabelle e salti pagina vanno scritti. |

## Decisione

- I PDF si generano con **SkiaSharp**, la stessa versione già usata da Avalonia nel client.
- Un piccolo impaginatore nostro (`Infrastructure/Documenti`) gestisce A4, margini, testo a capo, tabelle con intestazione ripetuta, salti pagina, numeri di pagina e il disclaimer in ogni pagina.
- I documenti si generano **nell'API**: il client li scarica e li salva. Così i documenti normativi di M4 potranno essere archiviati sul server con l'impronta SHA-256.
- Il carattere **Inter** (SIL Open Font License 1.1) è incorporato nella DLL, con la licenza accanto: il PDF è identico su Windows, macOS e Linux, e sul server non servono caratteri di sistema né fontconfig (`SkiaSharp.NativeAssets.Linux.NoDependencies`).
- Nei test il testo dei PDF si rilegge con **PdfPig** (Apache 2.0), solo come dipendenza di test.

## Conseguenze

- Nessun vincolo di licenza per chi installa il gestionale.
- Più codice da mantenere: l'impaginatore copre solo ciò che serve ai nostri documenti, non è un motore generale.
- Se un giorno i documenti richiederanno PDF/A o firme digitali, la scelta va rivista: SkiaSharp produce PDF semplici.
