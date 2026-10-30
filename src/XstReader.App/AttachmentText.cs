using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml;
using ExcelDataReader;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace XstReader.App;

public sealed record ExtractionResult(string Text = "", string Status = "ok");

public static class AttachmentText
{
    private static readonly HashSet<string> PlainExtensions = new("txt md csv tsv log json xml cs js ts py sql css yml yaml ini cfg conf eml vcf ics c cpp h java rs sh ps1 bat tex".Split(' '), StringComparer.OrdinalIgnoreCase);
    public static bool Supports(string name) => PlainExtensions.Contains(Extension(name)) || Extension(name) is
        "html" or "htm" or "rtf" or "pdf" or "docx" or "docm" or "xlsx" or "xlsm" or "xls" or "xlsb" or "pptx" or "pptm" or "odt" or "ods" or "odp" or "epub";
    private static string Extension(string name) => Path.GetExtension(name).TrimStart('.').ToLowerInvariant();

    public static string Extract(string name, byte[] bytes, int limit)
    {
        if (!Supports(name)) return "";
        limit = Math.Clamp(limit, 10000, 2000000);
        var ext = Extension(name);
        using var stream = new MemoryStream(bytes, false);
        var text = new StringBuilder();
        void Add(string? value) { if (value != null && text.Length < limit) text.Append(value.AsSpan(0, Math.Min(value.Length, limit - text.Length))); }
        if (ext == "pdf")
        {
            using var pdf = PdfDocument.Open(stream);
            foreach (var page in pdf.GetPages()) { Add(ContentOrderTextExtractor.GetText(page)); Add("\n"); if (text.Length >= limit) break; }
        }
        else if (ext is "xls" or "xlsb" or "xlsx" or "xlsm")
        {
            using var workbook = ExcelReaderFactory.CreateReader(stream);
            do
            {
                Add(workbook.Name); Add("\n");
                while (text.Length < limit && workbook.Read())
                {
                    for (var c = 0; c < workbook.FieldCount && text.Length < limit; c++) { Add(Convert.ToString(workbook.GetValue(c), System.Globalization.CultureInfo.InvariantCulture)); Add("\t"); }
                    Add("\n");
                }
            } while (text.Length < limit && workbook.NextResult());
        }
        else if (ext is "docx" or "docm" or "pptx" or "pptm" or "odt" or "ods" or "odp" or "epub")
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            long expanded = 0;
            foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                var n = entry.FullName;
                var include = ext is "docx" or "docm" ? n.StartsWith("word/", StringComparison.Ordinal) &&
                    (n == "word/document.xml" || n.StartsWith("word/header") || n.StartsWith("word/footer") || n == "word/footnotes.xml" || n == "word/endnotes.xml") :
                    ext is "pptx" or "pptm" ? n.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal) && !n.Contains("/_rels/") :
                    ext == "epub" ? n.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".html", StringComparison.OrdinalIgnoreCase) : n == "content.xml";
                if (!include) continue;
                expanded += entry.Length;
                if (expanded > 64 * 1024 * 1024) throw new InvalidDataException("Expanded document exceeds limit");
                using var part = entry.Open();
                using var xml = XmlReader.Create(part, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024, IgnoreComments = true });
                while (text.Length < limit && xml.Read())
                {
                    if (xml.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) Add(xml.Value);
                    else if (xml.NodeType == XmlNodeType.Element && xml.LocalName is "tab" or "br" or "cr") Add(" ");
                    else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName is "p" or "tr" or "table-row" or "h" or "br") Add("\n");
                    else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName is "tc" or "table-cell") Add("\t");
                }
                Add("\n"); if (text.Length >= limit) break;
            }
        }
        else
        {
            var value = Decode(bytes);
            Add(ext is "html" or "htm" ? MailBody.HtmlToText(value) : ext == "rtf" ? MailBody.HtmlToText(RtfPipe.Rtf.ToHtml(value)) : value);
        }
        return text.ToString().Replace('\0', ' ');
    }
    private static string Decode(byte[] bytes)
    {
        // BOM and BOM-less UTF-16, strict UTF-8, then legacy Cyrillic mail attachments.
        if (bytes.Length >= 2 && (bytes[0] == 0xff && bytes[1] == 0xfe || bytes[0] == 0xfe && bytes[1] == 0xff))
        { using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true); return reader.ReadToEnd(); }
        if (bytes.Take(Math.Min(256, bytes.Length)).Where((b, i) => i % 2 == 1 && b == 0).Count() > 30) return Encoding.Unicode.GetString(bytes);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff'); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1251).GetString(bytes); }
    }

    // Each file is processed in a child of this same executable: a malformed parser
    // cannot stall the UI or leave an unbounded Task running after its timeout.
    public static async Task<ExtractionResult> ExtractIsolatedAsync(string name, byte[] bytes, SearchOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var assembly = typeof(AttachmentText).Assembly;
        var managedEntry = Path.Combine(AppContext.BaseDirectory, assembly.GetName().Name + ".dll");
        var executable = Environment.ProcessPath!;
        if (Assembly.GetEntryAssembly() == assembly)
        {
            start.FileName = executable;
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(managedEntry);
        }
        else
        {
            // Used by the independent check runner and other managed hosts.
            start.FileName = "dotnet"; start.ArgumentList.Add(managedEntry);
        }
        start.ArgumentList.Add("--extract-search-text"); start.ArgumentList.Add(Path.GetExtension(name));
        start.ArgumentList.Add(options.MaxTextChars.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        Task<string>? read = null, errors = null;
        try
        {
            process.Start();
            read = ReadBoundedAsync(process.StandardOutput, options.MaxTextChars * 6 + 4096, timeout.Token);
            errors = ReadBoundedAsync(process.StandardError, 4096, timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(bytes, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var output = await read; await errors;
            return process.ExitCode == 0 ? JsonSerializer.Deserialize<ExtractionResult>(output) ?? new(Status: "failed") : new(Status: "failed");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(Status: "timeout"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception) { return new(Status: "failed"); }
        finally
        {
            timeout.Cancel();
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
            catch (InvalidOperationException) { }
            // Observe cancelled/broken pipes as well as successful reads.
            foreach (var pending in new[] { read, errors }) if (pending != null) { try { await pending; } catch { } }
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        { if (result.Length + count > limit) throw new IOException("Extractor output limit"); result.Append(buffer, 0, count); }
        return result.ToString();
    }
    public static int RunWorker(string[] args)
    {
        try
        {
            if (args.Length != 3 || !int.TryParse(args[2], out var limit)) return 2;
            using var input = Console.OpenStandardInput(); using var bytes = new MemoryStream();
            var buffer = new byte[81920]; int count;
            while ((count = input.Read(buffer)) > 0)
            { if (bytes.Length + count > 100 * 1024 * 1024) return 2; bytes.Write(buffer, 0, count); }
            var result = new ExtractionResult(Extract("attachment" + args[1], bytes.ToArray(), limit));
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.Write(JsonSerializer.Serialize(result)); return 0;
        }
        catch { return 1; }
    }
}
