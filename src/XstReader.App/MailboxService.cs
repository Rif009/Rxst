namespace XstReader.App;

// A single gate protects the original reader's mutable indexes and message tables.
public sealed class MailboxService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, MailFolder> archives = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<MailFolder, List<MailItem>> cache = [];
    private readonly HashSet<Message> loaded = [];
    public async Task<SearchDocument> SearchDocumentAsync(MailItem mail, SearchOptions options, CancellationToken token)
    {
        if (mail.Source == null) return new("", "", "");
        Message message;
        await gate.WaitAsync(token);
        try
        {
            message = await Task.Run(() =>
            {
                var copy = mail.Source.CreateContentReader();
                copy.Folder.XstFile.ReadMessageDetails(copy);
                return copy;
            }, token);
        }
        finally { gate.Release(); }
        var body = options.Body ? await Task.Run(() => MailBody.Read(message), token) : "";
        if (body.Length > options.MaxTextChars) body = body[..options.MaxTextChars];
        var files = message.Attachments.Where(a => a.IsFile).ToArray();
        var names = string.Join('\n', files.Select(a => a.FileName));
        var content = new System.Text.StringBuilder(); var skipped = 0; var failed = 0;
        if (options.FileContents)
        {
            foreach (var attachment in files)
            {
                token.ThrowIfCancellationRequested();
                if (!AttachmentText.Supports(attachment.FileName) || options.Excludes(attachment.FileName) ||
                    attachment.Size > options.MaxFileMb * 1024L * 1024 || content.Length >= options.MaxTextChars)
                { skipped++; continue; }
                try
                {
                    byte[] bytes;
                    await gate.WaitAsync(token);
                    try
                    {
                        bytes = await Task.Run(() =>
                        {
                            using var output = new LimitedAttachmentStream(options.MaxFileMb * 1024L * 1024);
                            message.Folder.XstFile.SaveAttachment(output, attachment);
                            return output.ToArray();
                        }, token);
                    }
                    finally { gate.Release(); }
                    var extracted = await AttachmentText.ExtractIsolatedAsync(attachment.FileName, bytes, options, token);
                    if (extracted.Status != "ok") { failed++; continue; }
                    // A message-wide budget also bounds mails with thousands of small attachments.
                    var remaining = options.MaxTextChars - content.Length;
                    content.Append(extracted.Text.AsSpan(0, Math.Min(extracted.Text.Length, remaining)));
                    if (content.Length < options.MaxTextChars) content.Append('\n');
                }
                catch (OperationCanceledException) { throw; }
                catch { failed++; }
            }
        }
        return new(body, names, content.ToString(), skipped, failed);
    }
    private sealed class LimitedAttachmentStream(long limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        { if (Position + count > limit) throw new InvalidDataException("Attachment size limit"); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { if (Position + buffer.Length > limit) throw new InvalidDataException("Attachment size limit"); base.Write(buffer); }
        public override void WriteByte(byte value)
        { if (Position >= limit) throw new InvalidDataException("Attachment size limit"); base.WriteByte(value); }
    }
    public async Task CloseAsync(MailFolder root)
    {
        await gate.WaitAsync();
        try
        {
            archives.Remove(root.ArchivePath);
            foreach (var key in cache.Keys.Where(f => f.ArchivePath == root.ArchivePath).ToArray()) cache.Remove(key);
            loaded.RemoveWhere(m => m.Folder.XstFile == root.Source?.XstFile);
        }
        finally { gate.Release(); }
    }
    public async Task<MailFolder> OpenAsync(string path)
    {
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                path = Path.GetFullPath(path);
                if (archives.TryGetValue(path, out var existing)) return existing;
                var candidate = new XstFile(path);
                var root = MailFolder.FromArchive(candidate.ReadFolderTree(), path);
                archives.Add(path, root);
                return root;
            });
        }
        finally { gate.Release(); }
    }
    public async Task<List<MailItem>> ReadAsync(IEnumerable<MailFolder> folders, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                var result = new List<MailItem>();
                foreach (var folder in folders)
                {
                    token.ThrowIfCancellationRequested();
                    if (folder.Source != null)
                    {
                        if (!cache.TryGetValue(folder, out var items))
                        {
                            items = folder.Source.XstFile.ReadMessages(folder.Source)
                                .Select(m => MailItem.From(m, folder.Name, folder.ArchiveName, folder.ArchivePath)).ToList();
                            cache[folder] = items;
                        }
                        result.AddRange(items);
                    }
                }
                return result;
            }, token);
        }
        finally { gate.Release(); }
    }
    public async Task<(string Body, List<Attachment> Attachments, string Properties, string Html)> DetailsAsync(MailItem mail,
        MailRenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        (string Body, List<Attachment> Attachments, string Properties, string RawHtml, Dictionary<string, string> Images) snapshot;
        await gate.WaitAsync(cancellationToken);
        try
        {
            snapshot = await Task.Run(() =>
            {
                if (mail.Source is not { } m) return (Strings.Current["NoBody"], new List<Attachment>(), "", "", new Dictionary<string, string>());
                var file = m.Folder.XstFile;
                if (!loaded.Contains(m)) { file.ReadMessageDetails(m); loaded.Add(m); }
                return (MailBody.Read(m), m.Attachments.Where(a => a.IsFile && !a.Hide).ToList(),
                    string.Join(Environment.NewLine, m.Properties.Select(p => $"{p.Tag}: {p.Value}")),
                    options.EnableHtml ? RawHtml(m) : "", options.EnableHtml && options.ShowInlineImages ? InlineImages(m, file) : new Dictionary<string, string>());
            }, cancellationToken);
        }
        finally { gate.Release(); }
        var html = await Task.Run(() => ExternalHtml.PrepareAsync(snapshot.RawHtml, options,
            cid => snapshot.Images.GetValueOrDefault(cid.Trim('<', '>')), cancellationToken), cancellationToken);
        return (snapshot.Body, snapshot.Attachments, snapshot.Properties, html);
    }
    public async Task SaveAttachmentAsync(Attachment attachment, Stream output)
    {
        await gate.WaitAsync();
        try { await Task.Run(() => attachment.Message.Folder.XstFile.SaveAttachment(output, attachment)); }
        finally { gate.Release(); }
    }
    public async Task<(string Extension, string Content)> ExportAsync(MailItem mail, bool properties)
    {
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                if (mail.Source is not { } m) return ("txt", "");
                if (!loaded.Contains(m)) { m.Folder.XstFile.ReadMessageDetails(m); loaded.Add(m); }
                if (properties)
                {
                    static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
                    return ("csv", "Property,Value\r\n" + string.Join("\r\n", m.Properties.Select(p => Csv(p.Tag.ToString()) + "," + Csv(Convert.ToString((object?)p.Value) ?? ""))));
                }
                if (m.IsBodyHtml) return ("html", m.EmbedHtmlPrintHeader(m.GetBodyAsHtmlString() ?? ""));
                if (m.IsBodyRtf) return ("rtf", m.GetRtfBody() ?? "");
                return ("txt", m.EmbedTextPrintHeader(m.Body ?? ""));
            });
        }
        finally { gate.Release(); }
    }
    private static string RawHtml(Message message) => message.IsBodyHtml ? message.GetBodyAsHtmlString() ?? "" : message.IsBodyRtf ? RtfPipe.Rtf.ToHtml(message.GetRtfBody()) : "";
    private static Dictionary<string, string> InlineImages(Message message, XstFile file)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var budget = 16 * 1024 * 1024;
        foreach (var attachment in message.Attachments.Where(a => a.IsFile && !string.IsNullOrWhiteSpace(a.ContentId)))
        {
            if (attachment.Size > budget) continue;
            using var output = new MemoryStream(); file.SaveAttachment(output, attachment);
            if (output.Length > budget) continue;
            budget -= (int)output.Length;
            if (MailHtml.ImageDataUri(output.ToArray()) is { } data) result[attachment.ContentId.Trim('<', '>')] = data;
        }
        return result;
    }
}
