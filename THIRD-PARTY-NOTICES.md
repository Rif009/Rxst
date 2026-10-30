# Third-party notices

This application includes source code derived from **Dijji / XstReader**:
https://github.com/dijji/XstReader

Copyright (c) 2016, 2019, 2020, Dijji and other original contributors.
Licensed under the Microsoft Public License (MS-PL); the complete text is in `LICENSE`.
Original per-file copyright notices have been retained. The .NET 10 port and new UI are distributed under the same MS-PL license.

Dependencies restored through NuGet:

| Component | License | Source |
| --- | --- | --- |
| Avalonia UI and Fluent theme | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.HtmlRenderer / HTML Renderer | MIT | https://github.com/AvaloniaUI/Avalonia.HtmlRenderer |
| Inter font | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| Html Agility Pack | MIT | https://github.com/zzzprojects/html-agility-pack |
| RtfPipe | MIT | https://github.com/erdomke/RtfPipe |
| Microsoft.Data.Sqlite | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | Public domain | https://sqlite.org/copyright.html |
| PdfPig | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| ExcelDataReader | MIT | https://github.com/ExcelDataReader/ExcelDataReader |

The content-search architecture was informed by **Kensaku** (https://github.com/yjid/kensaku, MIT): local persistent indexing, separate text extractors, incremental invalidation, bounded processing and trigram candidate search. Rxst's implementation is written independently in C#; no Kensaku source is included. OCR and semantic/AI search are not implemented in Rxst.

Their packages include additional transitive components with their respective notices. The public sample archives used during development came from java-libpst (Apache-2.0 / LGPL); they are not included in the application distribution.
