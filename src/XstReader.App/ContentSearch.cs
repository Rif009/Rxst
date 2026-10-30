using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace XstReader.App;

public sealed record SearchOptions(bool Body = true, bool AttachmentNames = true, bool FileContents = false,
    bool CaseSensitive = false, bool WholeWords = false, int MaxFileMb = 20, int MaxTextChars = 500000,
    int TimeoutSeconds = 10, string ExcludedExtensions = "")
{
    public bool NeedsIndex => Body || AttachmentNames || FileContents;
    // Matching preferences do not require extracting documents again.
    public string ExtractionSignature => $"v1|{Body}|{FileContents}|{MaxFileMb}|{MaxTextChars}|{TimeoutSeconds}|{ExcludedExtensions.ToLowerInvariant()}";
    public bool Excludes(string name) => ExcludedExtensions.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Any(e => e.TrimStart('.', '*').Equals(Path.GetExtension(name).TrimStart('.'), StringComparison.OrdinalIgnoreCase));
    public bool Contains(string text, string value)
    {
        if (value.Length == 0) return true;
        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var start = 0; start <= text.Length - value.Length;)
        {
            var at = text.IndexOf(value, start, comparison);
            if (at < 0) return false;
            if (!WholeWords || (at == 0 || !IsWord(text[at - 1])) &&
                (at + value.Length == text.Length || !IsWord(text[at + value.Length]))) return true;
            start = at + 1;
        }
        return false;
    }
    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;
}

public sealed record SearchDocument(string Body, string Names, string Files, int SkippedFiles = 0, int FailedFiles = 0);
public sealed record SearchTerm(string Field, string Value);

public static class SearchQuery
{
    public static IReadOnlyList<SearchTerm> Parse(string query) => Regex.Matches(query, "(?:[^\\s\"]|\"[^\"]*\")+")
        .Select(m =>
        {
            var raw = m.Value;
            var colon = raw.IndexOf(':');
            var field = colon > 0 ? raw[..colon].ToLowerInvariant() : "";
            return field is "from" or "to" or "subject" or "body" or "attachment" or "filename" or "content"
                ? new SearchTerm(field, raw[(colon + 1)..].Trim('"')) : new SearchTerm("", raw.Trim('"'));
        }).ToArray();
}

// SQLite's trigram FTS index finds candidates; the same verifier used for headers
// preserves exact substring, Unicode case and whole-word behavior in every field.
public sealed class ContentIndex(string path) : IDisposable
{
    private readonly object sync = new();
    private SqliteConnection? connection;
    public string FilePath { get; } = path;
    public static string Key(MailItem mail) => ArchiveKey(mail.ArchivePath) + ":" + (mail.Source?.NodeId.ToString() ?? mail.Subject);
    private static string ArchiveKey(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path)));
    private SqliteConnection Db
    {
        get
        {
            if (connection != null) return connection;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = false }.ToString());
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA secure_delete=ON;
                CREATE TABLE IF NOT EXISTS archives (id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS documents (id TEXT PRIMARY KEY, archive TEXT NOT NULL, signature TEXT NOT NULL, skipped INTEGER, failed INTEGER);
                CREATE INDEX IF NOT EXISTS document_archive ON documents(archive);
                CREATE VIRTUAL TABLE IF NOT EXISTS content USING fts5(id UNINDEXED, body, names, files, tokenize='trigram');
                """;
            cmd.ExecuteNonQuery();
            db.CreateFunction<string, string, long, long, bool>("rx_contains", (text, value, sensitive, words) =>
                new SearchOptions(CaseSensitive: sensitive != 0, WholeWords: words != 0).Contains(text, value), isDeterministic: true);
            connection = db;
            return db;
        }
    }
    public void PrepareArchive(string path)
    {
        var file = new FileInfo(path);
        var fingerprint = $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
        var key = ArchiveKey(path);
        lock (sync)
        {
            using var check = Db.CreateCommand();
            check.CommandText = "SELECT fingerprint FROM archives WHERE id=$id";
            check.Parameters.AddWithValue("$id", key);
            if (Equals(check.ExecuteScalar(), fingerprint)) return;
            using var tx = Db.BeginTransaction();
            using var cmd = Db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM content WHERE rowid IN (SELECT rowid FROM documents WHERE archive=$id); DELETE FROM documents WHERE archive=$id; INSERT OR REPLACE INTO archives VALUES($id,$fingerprint)";
            cmd.Parameters.AddWithValue("$id", key); cmd.Parameters.AddWithValue("$fingerprint", fingerprint);
            cmd.ExecuteNonQuery(); tx.Commit();
        }
    }
    public (int Skipped, int Failed)? Cached(MailItem mail, SearchOptions options)
    {
        lock (sync)
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = "SELECT skipped,failed FROM documents WHERE id=$id AND signature=$signature";
            cmd.Parameters.AddWithValue("$id", Key(mail)); cmd.Parameters.AddWithValue("$signature", options.ExtractionSignature);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? (reader.GetInt32(0), reader.GetInt32(1)) : null;
        }
    }
    public void Put(MailItem mail, SearchOptions options, SearchDocument document)
    {
        lock (sync)
        {
            using var tx = Db.BeginTransaction();
            using var cmd = Db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO documents(id,archive,signature,skipped,failed) VALUES($id,$archive,$signature,$skipped,$failed)
                    ON CONFLICT(id) DO UPDATE SET signature=$signature, skipped=$skipped, failed=$failed;
                DELETE FROM content WHERE rowid=(SELECT rowid FROM documents WHERE id=$id);
                INSERT INTO content(rowid,id,body,names,files)
                    SELECT rowid,$id,$body,$names,$files FROM documents WHERE id=$id;
                """;
            cmd.Parameters.AddWithValue("$id", Key(mail)); cmd.Parameters.AddWithValue("$archive", ArchiveKey(mail.ArchivePath));
            cmd.Parameters.AddWithValue("$signature", options.ExtractionSignature);
            cmd.Parameters.AddWithValue("$body", document.Body); cmd.Parameters.AddWithValue("$names", document.Names); cmd.Parameters.AddWithValue("$files", document.Files);
            cmd.Parameters.AddWithValue("$skipped", document.SkippedFiles); cmd.Parameters.AddWithValue("$failed", document.FailedFiles);
            cmd.ExecuteNonQuery(); tx.Commit();
        }
    }
    public Func<MailItem, string, string, bool> Matcher(string query, SearchOptions options, CancellationToken token, IEnumerable<string>? archivePaths = null)
    {
        var matches = new Dictionary<(string, string), HashSet<string>>();
        var scope = archivePaths?.Select(ArchiveKey).Distinct().ToArray();
        foreach (var term in SearchQuery.Parse(query))
        {
            token.ThrowIfCancellationRequested();
            var columns = term.Field switch
            {
                "body" => options.Body ? new[] { "body" } : [],
                "attachment" or "filename" => options.AttachmentNames ? new[] { "names" } : [],
                "content" => options.FileContents ? new[] { "files" } : [],
                "" => new[] { options.Body ? "body" : "", options.AttachmentNames ? "names" : "", options.FileContents ? "files" : "" }.Where(c => c.Length > 0).ToArray(),
                _ => []
            };
            var ids = new HashSet<string>();
            if (columns.Length > 0 && term.Value.Length > 0)
            {
                lock (sync)
                {
                    using var cmd = Db.CreateCommand();
                    // Quotes and punctuation remain literal, never FTS query syntax.
                    var fts = term.Value.EnumerateRunes().Count() >= 3;
                    cmd.CommandText = "SELECT content.id FROM content JOIN documents ON content.id=documents.id WHERE documents.signature=$signature AND " +
                        (fts ? "content MATCH $fts AND " : "") + "(" + string.Join(" OR ", columns.Select(c => $"rx_contains({c},$value,$case,$words)")) + ")";
                    cmd.Parameters.AddWithValue("$signature", options.ExtractionSignature);
                    if (scope is { Length: > 0 })
                    {
                        cmd.CommandText += " AND documents.archive IN (" + string.Join(",", scope.Select((_, i) => "$a" + i)) + ")";
                        for (var i = 0; i < scope.Length; i++) cmd.Parameters.AddWithValue("$a" + i, scope[i]);
                    }
                    if (fts) cmd.Parameters.AddWithValue("$fts", "{" + string.Join(" ", columns) + "} : \"" + term.Value.Replace("\"", "\"\"") + "\"");
                    cmd.Parameters.AddWithValue("$value", term.Value); cmd.Parameters.AddWithValue("$case", options.CaseSensitive ? 1 : 0); cmd.Parameters.AddWithValue("$words", options.WholeWords ? 1 : 0);
                    using var registration = token.Register(cmd.Cancel);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) { token.ThrowIfCancellationRequested(); ids.Add(reader.GetString(0)); }
                }
            }
            matches[(term.Field, term.Value)] = ids;
        }
        return (mail, field, value) => matches.TryGetValue((field, value), out var ids) && ids.Contains(Key(mail));
    }
    public void Clear()
    {
        lock (sync)
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = "DELETE FROM content; DELETE FROM documents; DELETE FROM archives; INSERT INTO content(content) VALUES('rebuild'); PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
            cmd.ExecuteNonQuery();
        }
    }
    public void Dispose() { lock (sync) { connection?.Dispose(); connection = null; } }
}
