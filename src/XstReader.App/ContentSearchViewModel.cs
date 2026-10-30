namespace XstReader.App;

public sealed partial class MainViewModel
{
    private readonly ContentIndex contentIndex;
    private readonly SemaphoreSlim indexingGate = new(1, 1);
    private CancellationTokenSource? indexCancellation, searchCancellation;
    private bool indexRunning, searchRunning, stopped, maintainingIndex;
    private int indexedCount, indexTotal, skippedFiles, failedFiles;
    private string indexError = "";
    public Task IndexingTask { get; private set; } = Task.CompletedTask;
    public Task SearchTask { get; private set; } = Task.CompletedTask;
    public bool IsIndexing => indexRunning;
    public bool IsSearching => searchRunning;
    public bool CanManageIndex => !maintainingIndex;
    public string IndexPath => contentIndex.FilePath;
    public string IndexSummary => indexError.Length > 0 ? L.Format("IndexError", indexError) :
        L.Format(indexRunning ? "IndexProgress" : "IndexReady", indexedCount, indexTotal, skippedFiles, failedFiles);
    public string SearchProgress => L["SearchingContent"];
    private void RefreshIndex()
    {
        Changed(nameof(IsIndexing)); Changed(nameof(IndexSummary)); Changed(nameof(IsSearching)); Changed(nameof(SearchProgress));
    }
    private void StartContentIndex()
    {
        if (maintainingIndex || stopped) return;
        indexCancellation?.Cancel();
        indexCancellation?.Dispose();
        var cancellation = indexCancellation = new();
        var items = source.ToArray(); var options = Settings.SearchSnapshot();
        indexRunning = options.NeedsIndex && items.Length > 0 && !stopped;
        indexedCount = skippedFiles = failedFiles = 0; indexTotal = items.Length; indexError = "";
        RefreshIndex();
        IndexingTask = IndexScopeAsync(items, options, cancellation);
    }
    private async Task IndexScopeAsync(MailItem[] items, SearchOptions options, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            if (!options.NeedsIndex || items.Length == 0 || stopped) return;
            await indexingGate.WaitAsync(token);
            try
            {
                await Task.Run(() =>
                {
                    foreach (var path in items.Select(m => m.ArchivePath).Distinct()) { token.ThrowIfCancellationRequested(); contentIndex.PrepareArchive(path); }
                }, token);
                foreach (var mail in items)
                {
                    token.ThrowIfCancellationRequested();
                    var cached = await Task.Run(() => contentIndex.Cached(mail, options), token);
                    token.ThrowIfCancellationRequested();
                    if (cached is { } previous) { skippedFiles += previous.Skipped; failedFiles += previous.Failed; }
                    else
                    {
                        try
                        {
                            var document = await Service.SearchDocumentAsync(mail, options, token);
                            token.ThrowIfCancellationRequested();
                            await Task.Run(() => contentIndex.Put(mail, options, document), token);
                            skippedFiles += document.SkippedFiles; failedFiles += document.FailedFiles;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) when (ex is not Microsoft.Data.Sqlite.SqliteException)
                        {
                            failedFiles++; // A bad message does not prevent indexing the rest; retry on the next pass.
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    indexedCount++;
                    if (indexedCount % 32 == 0) { RefreshIndex(); ScheduleContentSearch(); }
                }
            }
            finally { indexingGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) indexError = ex.Message; }
        finally
        {
            if (ReferenceEquals(indexCancellation, cancellation))
            { indexRunning = false; RefreshIndex(); ScheduleContentSearch(); }
        }
    }
    private void ScheduleContentSearch()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        var cancellation = searchCancellation = new();
        var items = source; var options = Settings.SearchSnapshot();
        var filter = new MailFilter(Query, SenderFilter, UnreadOnly, AttachmentsOnly, DateFrom, DateTo, SortIndex, options);
        searchRunning = !stopped && !maintainingIndex && options.NeedsIndex && Query.Length > 0 && items.Count > 0;
        Changed(nameof(IsSearching)); Changed(nameof(NoResults));
        SearchTask = SearchContentAsync(items, options, filter, cancellation, searchRunning);
    }
    private async Task SearchContentAsync(List<MailItem> items, SearchOptions options, MailFilter filter, CancellationTokenSource cancellation, bool run)
    {
        var token = cancellation.Token;
        try
        {
            if (!run) return;
            await Task.Delay(180, token);
            var matches = await Task.Run(() =>
            {
                var matcher = contentIndex.Matcher(filter.Query, options, token, items.Select(m => m.ArchivePath).Distinct());
                return (filter with { ContentMatch = matcher }).Apply(items).ToList();
            }, token);
            if (token.IsCancellationRequested || !ReferenceEquals(source, items)) return;
            var selected = SelectedMail;
            Messages = matches; Changed(nameof(Messages));
            SelectedMail = selected != null && matches.Contains(selected) ? selected : Settings.AutoSelectFirstMessage ? matches.FirstOrDefault() : null;
            Changed(nameof(ResultLabel)); Changed(nameof(NoResults));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) { indexError = ex.Message; RefreshIndex(); } }
        finally
        {
            if (ReferenceEquals(searchCancellation, cancellation)) { searchRunning = false; Changed(nameof(IsSearching)); Changed(nameof(NoResults)); }
        }
    }
    public async Task ClearContentIndexAsync(bool rebuild = false)
    {
        if (maintainingIndex || stopped) return;
        maintainingIndex = true; Changed(nameof(CanManageIndex));
        indexCancellation?.Cancel(); searchCancellation?.Cancel();
        await IndexingTask; await SearchTask;
        try
        {
            await Task.Run(contentIndex.Clear);
            indexedCount = skippedFiles = failedFiles = 0; indexError = ""; RefreshIndex();
            Apply();
            if (!rebuild) Status = L["IndexCleared"];
        }
        catch (Exception ex) { indexError = ex.Message; RefreshIndex(); }
        finally { maintainingIndex = false; Changed(nameof(CanManageIndex)); }
        if (rebuild) StartContentIndex();
    }
    public async Task StopSearchAsync()
    {
        stopped = true; indexCancellation?.Cancel(); searchCancellation?.Cancel();
        await IndexingTask; await SearchTask;
        contentIndex.Dispose();
        indexCancellation?.Dispose(); indexCancellation = null;
        searchCancellation?.Dispose(); searchCancellation = null;
    }
}
