using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace XstReader.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
}

public sealed class MailFolder : Observable
{
    private bool expanded;
    public bool Expanded { get => expanded; set => Set(ref expanded, value); }
    public required string Name { get; init; }
    public string ArchiveName { get; init; } = "";
    public string ArchivePath { get; init; } = "";
    public bool IsArchive { get; init; }
    private bool isIncluded = true;
    public bool IsIncluded { get => isIncluded; set => Set(ref isIncluded, value); }
    public MailFolder? Parent { get; private set; }
    public Folder? Source { get; init; }
    public List<MailFolder> Children { get; init; } = [];
    public long MessageCount => Source?.ContentCount ?? 0;
    public long TotalCount => MessageCount + Children.Sum(f => f.TotalCount);
    public string Count => (IsArchive ? TotalCount : MessageCount).ToString("N0");
    public bool HasCount => (IsArchive ? TotalCount : MessageCount) > 0;
    public string Icon => IsArchive ? "▣" : "▱";
    public string Description => IsArchive ? ArchivePath : Strings.Current.Format("FolderDescription", Name, MessageCount);
    public void RefreshLanguage() => Changed(null);
    public static MailFolder From(Folder folder, bool expanded = true) => new()
    {
        Name = string.IsNullOrWhiteSpace(folder.Name) ? "Почтовый архив" : folder.Name,
        Source = folder, Expanded = expanded, Children = folder.Folders.Select(f => From(f, false)).ToList()
    };
    public static MailFolder FromArchive(Folder folder, string path)
    {
        var name = System.IO.Path.GetFileName(path);
        MailFolder Build(Folder f, bool root = false)
        {
            var children = f.Folders.SelectMany(child =>
            {
                var n = (child.Name ?? "").Trim();
                // Flatten empty technical containers, preserving every message-bearing folder.
                var wrapper = n.Equals("IPM_SUBTREE", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Top of Information Store", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Top of Personal Folders", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Root - Mailbox", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Корневой уровень", StringComparison.OrdinalIgnoreCase);
                if (wrapper && child.ContentCount == 0) return child.Folders.Select(c => Build(c));
                var system = n.StartsWith("IPM_COMMON", StringComparison.OrdinalIgnoreCase) || n.Equals("IPM_VIEWS", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Search Root", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("корневая папка поиска", StringComparison.OrdinalIgnoreCase);
                var built = Build(child);
                return system && built.TotalCount == 0 ? [] : new[] { built };
            }).ToList();
            var node = new MailFolder
            {
                Name = root ? name : (string.IsNullOrWhiteSpace(f.Name) ? "Без названия" : f.Name),
                ArchiveName = name, ArchivePath = path, Source = f, IsArchive = root, Expanded = root, Children = children
            };
            foreach (var child in children) child.Parent = node;
            return node;
        }
        return Build(folder, true);
    }
    public void Reveal()
    {
        for (var parent = Parent; parent != null; parent = parent.Parent) parent.Expanded = true;
    }
    public IEnumerable<MailFolder> DescendantsAndSelf()
    {
        yield return this;
        foreach (var item in Children.SelectMany(x => x.DescendantsAndSelf())) yield return item;
    }
}

public sealed class MailItem : Observable
{
    public Message? Source { get; init; }
    private string sender = "", subject = "";
    public required string Sender { get => Source != null && string.IsNullOrEmpty(Source.From) ? Strings.Current["UnknownSender"] : sender; init => sender = value; }
    public required string Subject { get => Source != null && string.IsNullOrEmpty(Source.Subject) ? Strings.Current["NoSubject"] : subject; init => subject = value; }
    public string To { get; init; } = "";
    public string Cc { get; init; } = "";
    public string FolderName { get; init; } = "";
    public string ArchiveName { get; init; } = "";
    public string ArchivePath { get; init; } = "";
    public DateTime? Date { get; init; }
    public bool Unread { get; init; }
    public bool HasAttachments { get; init; }
    public string LocationLabel => $"{ArchiveName} / {FolderName}";
    public string DateLabel => Date?.ToString("dd MMM · HH:mm") ?? Strings.Current["NoDate"];
    public string Initials => string.Join("", Sender.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => s[0])).ToUpperInvariant();
    public string AttachmentMark => HasAttachments ? Strings.Current["AttachmentMark"] : "";
    public void RefreshLanguage() => Changed(null);
    public string ReadMark => Unread ? "●" : "";
    public static MailItem From(Message m, string folder, string archiveName = "", string archivePath = "") => new()
    {
        Source = m, Sender = m.From ?? Strings.Current["UnknownSender"], Subject = m.Subject ?? Strings.Current["NoSubject"],
        To = m.To ?? "", Cc = m.Cc ?? "", Date = m.Date, Unread = m.IsUnread,
        HasAttachments = m.HasAttachment, FolderName = folder, ArchiveName = archiveName, ArchivePath = archivePath
    };
}

public sealed record MailFilter(string Query = "", string Sender = "", bool UnreadOnly = false,
    bool AttachmentsOnly = false, DateTime? From = null, DateTime? To = null, int Sort = 0,
    SearchOptions? Search = null, Func<MailItem, string, string, bool>? ContentMatch = null)
{
    public IEnumerable<MailItem> Apply(IEnumerable<MailItem> source)
    {
        var terms = SearchQuery.Parse(Query);
        var result = source.Where(m =>
            (!UnreadOnly || m.Unread) && (!AttachmentsOnly || m.HasAttachments) &&
            (From == null || m.Date?.Date >= From.Value.Date) && (To == null || m.Date?.Date <= To.Value.Date) &&
            Contains(m.Sender, Sender) && terms.All(term => Matches(m, term)));
        return Sort switch
        {
            1 => result.OrderBy(m => m.Date),
            2 => result.OrderBy(m => m.Sender, StringComparer.CurrentCultureIgnoreCase),
            3 => result.OrderBy(m => m.Subject, StringComparer.CurrentCultureIgnoreCase),
            _ => result.OrderByDescending(m => m.Date)
        };
    }
    private bool Contains(string value, string term) => (Search ?? new()).Contains(value, term);
    private bool Matches(MailItem mail, SearchTerm term)
    {
        var value = term.Value;
        return term.Field switch
        {
            "from" => Contains(mail.Sender, value), "to" => Contains(mail.To, value),
            "subject" => Contains(mail.Subject, value),
            "body" or "attachment" or "filename" or "content" => ContentMatch?.Invoke(mail, term.Field, value) ?? false,
            _ => Contains(mail.Sender, value) || Contains(mail.To, value) || Contains(mail.Cc, value) || Contains(mail.Subject, value) ||
                (ContentMatch?.Invoke(mail, "", value) ?? false)
        };
    }
}

public static class MailBody
{
    public static string Read(Message m)
    {
        if (!string.IsNullOrWhiteSpace(m.Body)) return m.Body;
        if (m.IsBodyHtml) return HtmlToText(m.GetBodyAsHtmlString() ?? "");
        if (m.IsBodyRtf) return HtmlToText(RtfPipe.Rtf.ToHtml(m.GetRtfBody()));
        return m.IsEncryptedOrSigned
            ? Strings.Current["Smime"] : Strings.Current["NoBody"];
    }
    public static string HtmlToText(string html)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//head") ?? Enumerable.Empty<HtmlNode>()) node.Remove();
        foreach (var node in doc.DocumentNode.SelectNodes("//br|//p|//div|//tr|//li|//h1|//h2") ?? Enumerable.Empty<HtmlNode>())
            node.ParentNode.InsertBefore(doc.CreateTextNode("\n"), node);
        return Regex.Replace(HtmlEntity.DeEntitize(doc.DocumentNode.InnerText), @"\n[\t \r]*\n(?:[\t \r]*\n)+", "\n\n").Trim();
    }
}
