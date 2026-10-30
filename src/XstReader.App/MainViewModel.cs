using System.Collections.ObjectModel;

namespace XstReader.App;
public sealed partial class MainViewModel : Observable
{
    public Strings L => Strings.Current;
    public string LicenseText { get; } = ReadNotice("Rxst.THIRD-PARTY-NOTICES") + "\n\n" + ReadNotice("Rxst.LICENSE");
    private static string ReadNotice(string name)
    {
        using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream(name);
        if (stream == null) return "";
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public AppSettings Settings { get; }
    public IReadOnlyList<LocalizedText> ThemeChoices { get; } = [Strings.Current.Entry("Dark"), Strings.Current.Entry("Light"), Strings.Current.Entry("System")];
    public IReadOnlyList<LocalizedText> SortChoices { get; } = [Strings.Current.Entry("Newest"), Strings.Current.Entry("Oldest"), Strings.Current.Entry("BySender"), Strings.Current.Entry("BySubject")];
    private readonly SettingsStore settingsStore;
    private readonly SearchHistoryStore historyStore;
    public ObservableCollection<string> SearchHistory { get; } = [];
    private bool queryEdited;
    public bool HasQuery => !string.IsNullOrEmpty(Query);
    public bool HasHistory => SearchHistory.Count > 0;
    public bool NoHistory => !HasHistory;
    public string HistorySummary => L.Format("HistorySummary", SearchHistory.Count, Settings.HistoryLimit);
    private bool changingArchiveSelection;
    public int SelectedArchiveCount => Folders.Count(f => f.IsIncluded);
    public string ArchiveSelectionLabel => L.Format("SelectedArchiveCount", SelectedArchiveCount, Folders.Count);
    public string EmptyResultTitle => AllFolders && SelectedArchiveCount == 0 ? L["NoArchivesSelected"] : L["NoResults"];
    public string EmptyResultHint => AllFolders && SelectedArchiveCount == 0 ? L["SelectArchivesHint"] : L["NoResultsHint"];
    private bool changingDefaults, settingsOpen;
    private CancellationTokenSource? detailCancellation;
    public bool ShowSettings => settingsOpen;
    public bool ShowWelcome => !settingsOpen && Folders.Count == 0;
    public bool ShowMailbox => !settingsOpen && Folders.Count > 0;
    public void OpenSettings() { settingsOpen = true; RefreshPages(); }
    public void ShowMailPage() { settingsOpen = false; RefreshPages(); }
    private void RefreshPages() { Changed(nameof(ShowSettings)); Changed(nameof(ShowWelcome)); Changed(nameof(ShowMailbox)); }
    public MailboxService Service { get; } = new();
    public ObservableCollection<MailFolder> Folders { get; } = [];
    public IReadOnlyList<MailItem> Messages { get; private set; } = [];
    public ObservableCollection<AttachmentItem> Attachments { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;
    public string AttachmentSummary => L.Format("AttachmentSummary", Attachments.Count);
    public IReadOnlyList<ArchiveScope> ArchiveScopes { get; private set; } = [];
    private ArchiveScope? selectedScope;
    public ArchiveScope? SelectedScope
    {
        get => selectedScope;
        set { if (Set(ref selectedScope, value)) SetArchiveSelection(value?.Root is { } root ? [root] : Folders.ToArray()); }
    }
    private List<MailItem> source = [];
    private CancellationTokenSource? loadCancellation;
    private int selectionVersion;
    private bool busy;
    public bool CanInteract => !busy;
    public bool CanExport => !busy && HasSelection && detailsReady;
    private bool detailsReady;
    private string archiveName = "", archivePath = "", status = "", body = "", properties = "";
    public string ArchiveName { get => archiveName; private set => Set(ref archiveName, value); }
    public string ArchivePath { get => archivePath; private set => Set(ref archivePath, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string Body { get => body; private set => Set(ref body, value); }
    public string Properties { get => properties; private set => Set(ref properties, value); }
    private string html = "";
    private bool textMode;
    public string Html { get => html; private set { Set(ref html, value); Changed(nameof(ShowHtml)); Changed(nameof(ShowText)); Changed(nameof(HasHtml)); } }
    public bool HasHtml => Settings.EnableHtml && !string.IsNullOrEmpty(Html);
    public bool TextMode { get => textMode; set { if (Set(ref textMode, value)) { Changed(nameof(ShowHtml)); Changed(nameof(ShowText)); } } }
    public bool ShowHtml => HasHtml && !TextMode;
    public bool ShowText => !ShowHtml;
    public string ResultLabel => L.Format("ResultCount", Messages.Count, source.Count);
    public string FolderTitle => !AllFolders ? SelectedFolder?.Name ?? L["Mail"] : SelectedArchiveCount == 1
        ? Folders.First(f => f.IsIncluded).Name : SelectedArchiveCount == Folders.Count ? L["AllArchives"] : L.Format("SelectedArchivesTitle", SelectedArchiveCount);
    public bool NoResults => Messages.Count == 0 && !busy && !IsSearching;
    public bool HasSelection => SelectedMail != null;
    public bool NoSelection => !HasSelection;
    public string RecipientLabel => L.Format("ToLabel", SelectedMail?.To) + (string.IsNullOrWhiteSpace(SelectedMail?.Cc) ? "" : "\n" + L.Format("CcLabel", SelectedMail.Cc));
    private MailFolder? selectedFolder;
    public MailFolder? SelectedFolder
    {
        get => selectedFolder;
        set
        {
            if (!Set(ref selectedFolder, value)) return;
            value?.Reveal();
            if (value != null && !value.IsArchive && allFolders) { allFolders = false; Changed(nameof(AllFolders)); }
            Changed(nameof(FolderTitle)); _ = LoadFolderAsync();
        }
    }
    private MailItem? selectedMail;
    public MailItem? SelectedMail
    {
        get => selectedMail;
        set
        {
            if (!Set(ref selectedMail, value)) return;
            Changed(nameof(HasSelection)); Changed(nameof(NoSelection)); Changed(nameof(RecipientLabel));
            _ = LoadDetailsAsync(value);
        }
    }
    private string query = "", senderFilter = "";
    private bool unreadOnly, attachmentsOnly, allFolders;
    private DateTime? dateFrom, dateTo;
    private int sortIndex;
    public string Query { get => query; set { if (Set(ref query, value ?? "")) { queryEdited = true; Changed(nameof(HasQuery)); Apply(); } } }
    public string SenderFilter { get => senderFilter; set { if (Set(ref senderFilter, value)) Apply(); } }
    public bool UnreadOnly { get => unreadOnly; set { if (Set(ref unreadOnly, value)) Apply(); } }
    public bool AttachmentsOnly { get => attachmentsOnly; set { if (Set(ref attachmentsOnly, value)) Apply(); } }
    public bool AllFolders { get => allFolders; set { if (Set(ref allFolders, value)) { RefreshArchiveSelection(); _ = LoadFolderAsync(); } } }
    public DateTime? DateFrom { get => dateFrom; set { if (Set(ref dateFrom, value)) Apply(); } }
    public DateTime? DateTo { get => dateTo; set { if (Set(ref dateTo, value)) Apply(); } }
    public int SortIndex { get => sortIndex; set { if (Set(ref sortIndex, value)) Apply(); } }

    public MainViewModel(SettingsStore? store = null)
    {
        settingsStore = store ?? new(); Settings = settingsStore.Load();
        contentIndex = new(Path.ChangeExtension(settingsStore.FilePath, ".search-index.db"));
        historyStore = new(Path.ChangeExtension(settingsStore.FilePath, ".search-history.json"));
        foreach (var term in historyStore.Load(Settings.HistoryLimit)) SearchHistory.Add(term);
        L.SetLanguage(Settings.LanguageIndex); sortIndex = Settings.DefaultSort;
        RefreshArchives();
        Status = L[settingsStore.LoadFailed ? "SettingsLoadError" : "StartHint"];
        Settings.PropertyChanged += (_, e) =>
        {
            if (changingDefaults) return;
            ApplySettings(e.PropertyName);
        };
    }
    private void ApplySettings(string? property)
    {
        if (property is null || property.StartsWith("Search", StringComparison.Ordinal) && property != nameof(AppSettings.SearchAllArchives))
        {
            if (property is nameof(AppSettings.SearchCaseSensitive) or nameof(AppSettings.SearchWholeWords)) Apply();
            else { StartContentIndex(); Apply(); }
        }
        var historySaved = true;
        if (property is null or nameof(AppSettings.HistoryLimit))
        {
            var recent = SearchHistoryStore.Normalize(SearchHistory, Settings.HistoryLimit);
            SearchHistory.Clear(); foreach (var term in recent) SearchHistory.Add(term);
            if (Settings.HistoryLimit == 0) queryEdited = false;
            RefreshHistory(); historySaved = SaveHistory();
        }
        if (property is null or nameof(AppSettings.LanguageIndex))
        {
            L.SetLanguage(Settings.LanguageIndex);
            var scope = selectedScope?.Root;
            RefreshArchives();
            selectedScope = ArchiveScopes.FirstOrDefault(s => s.Root == scope) ?? ArchiveScopes[0]; Changed(nameof(SelectedScope));
            foreach (var mail in source) mail.RefreshLanguage();
            foreach (var folder in Folders.SelectMany(f => f.DescendantsAndSelf())) folder.RefreshLanguage();
            Changed(nameof(ResultLabel)); Changed(nameof(FolderTitle)); Changed(nameof(RecipientLabel));
            Changed(nameof(AttachmentSummary));
            Changed(nameof(HistorySummary)); RefreshArchiveSelection();
            RefreshIndex();
        }
        if (property is null or nameof(AppSettings.DefaultSort)) { SortIndex = Settings.DefaultSort; }
        Changed(nameof(HasHtml)); Changed(nameof(ShowHtml)); Changed(nameof(ShowText));
        if (property is null or nameof(AppSettings.LanguageIndex) or nameof(AppSettings.EnableHtml) or nameof(AppSettings.AllowExternalResources)
            or nameof(AppSettings.ShowInlineImages) or nameof(AppSettings.MailFontSize)) _ = LoadDetailsAsync(SelectedMail);
        try { settingsStore.Save(Settings); if (historySaved) Status = L["SettingsSaved"]; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = L.Format("SettingsError", ex.Message); }
    }
    public void ResetSettings()
    {
        changingDefaults = true;
        Settings.LanguageIndex = AppSettings.SystemLanguageIndex; Settings.ThemeIndex = 0; Settings.EnableHtml = true; Settings.AllowExternalResources = false;
        Settings.ShowInlineImages = true; Settings.AutoSelectFirstMessage = true; Settings.SearchAllArchives = true; Settings.DefaultSort = 0; Settings.MailFontSize = 14;
        Settings.HistoryLimit = 5;
        Settings.SearchBody = true; Settings.SearchAttachmentNames = true; Settings.SearchFileContents = false;
        Settings.SearchCaseSensitive = false; Settings.SearchWholeWords = false; Settings.SearchMaxFileMb = 20;
        Settings.SearchMaxTextChars = 500000; Settings.SearchTimeoutSeconds = 10; Settings.SearchExcludedExtensions = "";
        changingDefaults = false; ApplySettings(null);
    }
    public void CommitSearch(bool force = false)
    {
        if (Settings.HistoryLimit == 0) { queryEdited = false; return; }
        var term = Query.Trim();
        if (term.Length == 0 || !force && !queryEdited) return;
        var recent = SearchHistoryStore.Normalize(new[] { term }.Concat(SearchHistory), Settings.HistoryLimit);
        SearchHistory.Clear(); foreach (var item in recent) SearchHistory.Add(item);
        queryEdited = false; RefreshHistory(); SaveHistory();
    }
    public void ClearSearch() { CommitSearch(); Query = ""; }
    public void UseSearchHistory(string term) { Query = term; CommitSearch(true); }
    public void ClearSearchHistory()
    {
        SearchHistory.Clear(); queryEdited = false; RefreshHistory();
        if (SaveHistory()) Status = L["HistoryCleared"];
    }
    private void RefreshHistory() { Changed(nameof(HasHistory)); Changed(nameof(NoHistory)); Changed(nameof(HistorySummary)); }
    private bool SaveHistory()
    {
        try { historyStore.Save(SearchHistory, Settings.HistoryLimit); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = L.Format("HistoryError", ex.Message); return false; }
    }
    public void SelectAllArchives() => SetArchiveSelection(Folders.ToArray());
    public void ClearArchiveSelection() => SetArchiveSelection([]);
    public void SetArchiveSelection(IEnumerable<MailFolder> roots)
    {
        var selected = roots.ToHashSet();
        changingArchiveSelection = true;
        foreach (var root in Folders) root.IsIncluded = selected.Contains(root);
        changingArchiveSelection = false;
        ArchiveSelectionChanged();
    }
    private void ArchivePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MailFolder.IsIncluded) && !changingArchiveSelection) ArchiveSelectionChanged();
    }
    private void ArchiveSelectionChanged()
    {
        allFolders = true; Changed(nameof(AllFolders));
        RefreshArchiveSelection(); _ = LoadFolderAsync();
    }
    private void RefreshArchiveSelection()
    {
        Changed(nameof(SelectedArchiveCount)); Changed(nameof(ArchiveSelectionLabel)); Changed(nameof(FolderTitle));
        Changed(nameof(EmptyResultTitle)); Changed(nameof(EmptyResultHint));
    }
    private void Busy(bool value)
    {
        busy = value; Changed(nameof(CanInteract)); Changed(nameof(CanExport)); Changed(nameof(NoResults));
    }
    public Task OpenAsync(string path) => OpenManyAsync([path]);
    public async Task OpenManyAsync(IEnumerable<string> paths)
    {
        if (busy) return;
        var requested = paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        loadCancellation?.Cancel(); loadCancellation = null;
        Busy(true); Status = L["Opening"];
        var errors = new List<string>();
        MailFolder? last = null;
        changingArchiveSelection = true;
        foreach (var path in requested)
        {
            try
            {
                var root = await Service.OpenAsync(path);
                if (!Folders.Contains(root)) { root.PropertyChanged += ArchivePropertyChanged; Folders.Add(root); }
                root.IsIncluded = true;
                last = root;
            }
            catch (Exception ex) { errors.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}"); }
        }
        changingArchiveSelection = false;
        if (last != null)
        {
            RefreshArchives();
            selectedFolder = Folders.Count > 1 ? last : last.DescendantsAndSelf().FirstOrDefault(f =>
                f.Name.Equals("Входящие", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
                ?? last.DescendantsAndSelf().FirstOrDefault(f => !f.IsArchive && f.MessageCount > 0) ?? last;
            selectedFolder.Reveal(); Changed(nameof(SelectedFolder));
            allFolders = requested.Length > 1 || Settings.SearchAllArchives && Folders.Count > 1; Changed(nameof(AllFolders)); RefreshArchiveSelection(); ShowMailPage();
            if (allFolders) foreach (var root in Folders) root.Expanded = false;
        }
        Busy(false);
        if (last != null) await LoadFolderAsync();
        if (errors.Count > 0) Status = L.Format("OpenFailed", string.Join("; ", errors));
    }
    private void RefreshArchives()
    {
        ArchiveName = Folders.Count == 0 ? L["NoArchives"] : Folders.Count == 1 ? Folders[0].Name : L.Format("ArchiveCount", Folders.Count);
        ArchivePath = string.Join(Environment.NewLine, Folders.Select(f => f.ArchivePath));
        ArchiveScopes = [new(L["AllArchives"], null), .. Folders.Select(f => new ArchiveScope(f.Name, f))];
        selectedScope = ArchiveScopes[0]; Changed(nameof(ArchiveScopes)); Changed(nameof(SelectedScope));
        RefreshArchiveSelection();
        RefreshPages();
    }
    public async Task CloseArchiveAsync(MailFolder root)
    {
        if (busy || !root.IsArchive) return;
        loadCancellation?.Cancel(); Busy(true); SelectedMail = null;
        try
        {
            await Service.CloseAsync(root); root.PropertyChanged -= ArchivePropertyChanged; Folders.Remove(root); RefreshArchives();
            selectedFolder = Folders.FirstOrDefault(); Changed(nameof(SelectedFolder)); Changed(nameof(FolderTitle));
            source = []; StartContentIndex(); Apply();
        }
        finally { Busy(false); }
        if (SelectedFolder != null) await LoadFolderAsync();
        else { ArchiveName = L["NoArchives"]; Status = L["StartHint"]; }
    }
    private async Task LoadFolderAsync()
    {
        if (SelectedFolder == null) return;
        loadCancellation?.Cancel();
        var cancellation = loadCancellation = new();
        Busy(true); Status = L[AllFolders ? "ReadingAll" : "ReadingFolder"];
        try
        {
            var roots = Folders.Where(f => f.IsIncluded).ToArray();
            var folders = AllFolders ? roots.SelectMany(f => f.DescendantsAndSelf()).ToArray()
                : SelectedFolder.IsArchive ? SelectedFolder.DescendantsAndSelf().ToArray() : [SelectedFolder];
            var result = await Service.ReadAsync(folders, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            source = result; StartContentIndex(); Apply(); Status = L.Format("Loaded", ArchiveName, source.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested) { source = []; Apply(); Status = L.Format("FolderError", ex.Message); } }
        finally { if (ReferenceEquals(loadCancellation, cancellation)) Busy(false); }
    }
    private void Apply()
    {
        var selected = SelectedMail;
        var filtered = new MailFilter(Query ?? "", SenderFilter ?? "", UnreadOnly, AttachmentsOnly, DateFrom, DateTo, SortIndex, Settings.SearchSnapshot()).Apply(source).ToList();
        Messages = filtered; Changed(nameof(Messages));
        SelectedMail = selected != null && filtered.Contains(selected) ? selected : Settings.AutoSelectFirstMessage ? filtered.FirstOrDefault() : null;
        Changed(nameof(ResultLabel)); Changed(nameof(NoResults));
        if (DateFrom > DateTo) Status = L["DateError"];
        ScheduleContentSearch();
    }
    private async Task LoadDetailsAsync(MailItem? mail)
    {
        var version = ++selectionVersion;
        detailCancellation?.Cancel(); detailCancellation?.Dispose();
        var cancellation = detailCancellation = new();
        detailsReady = false; Changed(nameof(CanExport)); Attachments.Clear(); Properties = ""; Html = ""; Body = mail == null ? "" : L["ReadingMail"];
        Changed(nameof(HasAttachments)); Changed(nameof(AttachmentSummary));
        if (mail == null) return;
        try
        {
            var detail = await Service.DetailsAsync(mail, Settings.Snapshot(), cancellation.Token);
            if (version != selectionVersion) return;
            Body = detail.Body; Properties = detail.Properties; Html = detail.Html;
            foreach (var attachment in detail.Attachments) Attachments.Add(new(attachment));
            Changed(nameof(HasAttachments)); Changed(nameof(AttachmentSummary));
            detailsReady = true; Changed(nameof(CanExport));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == selectionVersion) Body = L.Format("MailError", ex.Message); }
    }
    public void Reset()
    {
        Query = ""; SenderFilter = ""; UnreadOnly = false; AttachmentsOnly = false; DateFrom = null; DateTo = null; SortIndex = 0;
        Status = L["FiltersReset"];
    }
}
public sealed record ArchiveScope(string Name, MailFolder? Root);
