using System.IO.Compression;
using System.Text;
using XstReader.App;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

static class ContentSearchChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rxst-content-checks-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var archive = Path.Combine(dir, "test.pst"); await File.WriteAllTextAsync(archive, "fingerprint-a");
            var mail = new MailItem { Sender = "Анна", Subject = "Обычное письмо", ArchivePath = archive, HasAttachments = true };
            var second = new MailItem { Sender = "Иван", Subject = "Другое письмо", ArchivePath = archive };
            var options = new SearchOptions(FileContents: true);
            var dbPath = Path.Combine(dir, "index.db");
            using (var index = new ContentIndex(dbPath))
            {
                index.PrepareArchive(archive);
                index.Put(mail, options, new("Секретный ПРОЕКТ альфа. Договор подписан. A+B SQL OR NEAR", "бюджет_2026.xlsx\nannual report.pdf", "Вложения содержат сумму 12345 и уникальный маркер ёжик", 2, 1));
                index.Put(second, options, new("Планирование командировки", "invoice.txt", "separate content"));
                bool Find(string query, SearchOptions? search = null)
                {
                    var o = search ?? options;
                    return new MailFilter(query, Search: o, ContentMatch: index.Matcher(query, o, default)).Apply([mail, second]).Contains(mail);
                }
                check(Find("секретный") && Find("body:\"ПРОЕКТ альфа\"") && Find("body:ёжик") == false, "Body indexing finds Cyrillic words and phrases in their own field");
                check(Find("attachment:бюджет") && Find("filename:\"annual report.pdf\"") && Find("xlsx"), "Attachment filenames, extensions and quoted names are searchable");
                check(Find("content:ёжик") && Find("12345") && !Find("content:бюджет"), "Attachment contents participate in general and field-specific searches");
                check(Find("from:анна body:договор attachment:xlsx content:12345") && !Find("from:иван content:12345"), "Header, body, filename and content terms combine with AND");
                check(Find("body:A+B") && Find("body:OR") && Find("body:\"SQL OR NEAR\"") && !Find("body:\"' OR 1=1 --\""), "FTS operators, punctuation, quotes and SQL fragments are literal query data");
                check(Find("content:и") && Find("body:ДО") && !Find("body:я"), "One- and two-character searches work without trigram false negatives");
                check(Find("body:ПРОЕКТ", options with { CaseSensitive = true }) && !Find("body:проект", options with { CaseSensitive = true }), "Case-sensitive matching verifies original Cyrillic text");
                check(Find("body:дог", options) && !Find("body:дог", options with { WholeWords = true }) && Find("body:Договор", options with { WholeWords = true }), "Whole-word option rejects substrings and accepts complete Unicode words");
                check(!Find("attachment:бюджет", options with { AttachmentNames = false }), "Disabling filename search also disables explicit filename operators");
                check(!Find("content:ёжик", options with { FileContents = false }) && !Find("body:договор", options with { Body = false }), "Disabled extraction fields cannot expose stale cached content");
                check(index.Cached(mail, options) == (2, 1) && index.Cached(mail, options with { MaxFileMb = 1 }) == null, "Incremental cache retains diagnostics and invalidates changed extraction policies");
            }
            using (var index = new ContentIndex(dbPath))
            {
                index.PrepareArchive(archive);
                check(index.Cached(mail, options) != null && index.Matcher("content:ёжик", options, default)(mail, "content", "ёжик"), "Persistent FTS index survives process-style reopen without re-extraction");
                await File.AppendAllTextAsync(archive, "changed"); index.PrepareArchive(archive);
                check(index.Cached(mail, options) == null && !index.Matcher("ёжик", options, default)(mail, "", "ёжик"), "Archive fingerprint changes remove obsolete text and cached records");
                index.Put(mail, options, new("private", "secret.txt", "sensitive")); index.Clear();
                check(index.Cached(mail, options) == null && !index.Matcher("sensitive", options, default)(mail, "", "sensitive"), "Clearing the index removes extracted bodies, names and attachment text");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { index.Matcher("cancelled", options, cancelled.Token); check(false, "Cancellation ignored"); }
                catch (OperationCanceledException) { check(true, "Index queries honor cancellation"); }
            }
            var unicode = "Согласование договора & Приложение";
            check(AttachmentText.Extract("file.txt", Encoding.UTF8.GetBytes(unicode), 10000) == unicode, "UTF-8 text extraction preserves Cyrillic");
            check(AttachmentText.Extract("file.csv", Encoding.GetEncoding(1251).GetBytes(unicode), 10000) == unicode, "Legacy Windows-1251 attachment text is decoded");
            check(AttachmentText.Extract("file.txt", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(unicode)).ToArray(), 10000) == unicode, "UTF-16 BOM attachments are decoded");
            var html = AttachmentText.Extract("file.html", Encoding.UTF8.GetBytes("<p>договор &amp; бюджет</p><script>notindexed</script>"), 10000);
            check(html.Contains("договор & бюджет") && !html.Contains("notindexed"), "HTML extraction indexes visible text without scripts or external requests");
            check(AttachmentText.Extract("file.rtf", Encoding.ASCII.GetBytes(@"{\rtf1\ansi Hello contract}"), 10000).Contains("Hello contract"), "RTF extraction reads formatted document text");
            check(AttachmentText.Extract("long.txt", Encoding.UTF8.GetBytes(new string('a', 20000)), 10000).Length == 10000, "Extracted text obeys the configured character limit");
            byte[] Package(params (string Name, string Xml)[] entries)
            {
                using var stream = new MemoryStream();
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()); writer.Write(entry.Xml); }
                return stream.ToArray();
            }
            var docx = Package(("word/document.xml", "<w:document xmlns:w='word'><w:p><w:r><w:t>Договор</w:t></w:r><w:r><w:t> № 42 &amp; бюджет</w:t></w:r></w:p></w:document>"), ("word/header1.xml", "<w:hdr xmlns:w='word'><w:p><w:r><w:t>Заголовок</w:t></w:r></w:p></w:hdr>"));
            var document = AttachmentText.Extract("file.docx", docx, 10000);
            check(document.Contains("Договор № 42 & бюджет") && document.Contains("Заголовок"), "DOCX extraction preserves runs, entities and headers");
            var presentation = Package(("ppt/slides/slide1.xml", "<a:p xmlns:a='draw'><a:r><a:t>Презентация проекта</a:t></a:r></a:p>"));
            check(AttachmentText.Extract("file.pptx", presentation, 10000).Contains("Презентация проекта"), "PPTX slide text is extracted");
            var odt = Package(("content.xml", "<office:document xmlns:office='o' xmlns:text='t'><text:p>OpenDocument пример</text:p></office:document>"));
            check(AttachmentText.Extract("file.odt", odt, 10000).Contains("OpenDocument пример"), "OpenDocument text is extracted");
            var xlsx = Package(
                ("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/><Override PartName='/xl/worksheets/sheet1.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'/></Types>"),
                ("_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>"),
                ("xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Бюджет' sheetId='1' r:id='rId1'/></sheets></workbook>"),
                ("xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/></Relationships>"),
                ("xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>Сумма договора</t></is></c><c r='B1'><v>12345</v></c></row></sheetData></worksheet>"));
            var sheet = AttachmentText.Extract("file.xlsx", xlsx, 10000);
            check(sheet.Contains("Бюджет") && sheet.Contains("Сумма договора") && sheet.Contains("12345"), "Excel extraction indexes sheet names, strings and numeric cell values");
            var pdf = new PdfDocumentBuilder(); var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
            pdf.AddPage(300, 300).AddText("Searchable PDF invoice", 12, new PdfPoint(20, 200), font);
            check(AttachmentText.Extract("file.pdf", pdf.Build(), 10000).Contains("Searchable PDF invoice"), "PDF extraction reads a real text page");
            check(!AttachmentText.Supports("image.png") && !AttachmentText.Supports("legacy.doc") && AttachmentText.Supports("table.xls"), "Supported and unsupported binary formats are explicitly distinguished");
            check(options.Excludes("a.PDF") == false && (options with { ExcludedExtensions = ".pdf, XLSX; *.log" }).Excludes("a.PDF"), "Extension exclusions accept dots, case and multiple separators");
            var worker = await AttachmentText.ExtractIsolatedAsync("file.docx", docx, options, default);
            check(worker.Status == "ok" && worker.Text.Contains("Договор № 42"), "Isolated extraction worker handles real process I/O and Unicode output");
            var broken = await AttachmentText.ExtractIsolatedAsync("broken.pdf", Encoding.UTF8.GetBytes("not a PDF"), options, default);
            check(broken.Status == "failed", "Malformed attachments fail independently without crashing the application");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                try { await AttachmentText.ExtractIsolatedAsync("file.txt", [], options, cancel.Token); check(false, "Worker cancellation ignored"); }
                catch (OperationCanceledException) { check(true, "Cancelled extraction does not launch a worker"); }
            }
            var settings = new AppSettings { SearchBody = false, SearchFileContents = true, SearchAttachmentNames = false, SearchWholeWords = true, SearchCaseSensitive = true, SearchMaxFileMb = 7, SearchMaxTextChars = 20000, SearchTimeoutSeconds = 3, SearchExcludedExtensions = "pdf" };
            var store = new SettingsStore(Path.Combine(dir, "settings.json")); store.Save(settings); var loaded = store.Load();
            check(loaded.SearchSnapshot() == settings.SearchSnapshot(), "All content-search preferences persist through restart");
        }
        finally { Directory.Delete(dir, true); }
    }
}
