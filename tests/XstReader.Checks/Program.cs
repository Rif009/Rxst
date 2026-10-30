using System.Security.Cryptography;
using XstReader;
using XstReader.App;

if (args is ["--preview-attachments"]) { AttachmentPreview.Run(); return; }

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
var passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
Check(AppSettings.LanguageForCulture(System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) == 0 &&
    AppSettings.LanguageForCulture(System.Globalization.CultureInfo.GetCultureInfo("ru-KZ")) == 0 &&
    AppSettings.LanguageForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")) == 1 &&
    AppSettings.LanguageForCulture(System.Globalization.CultureInfo.GetCultureInfo("de-DE")) == 1 &&
    AppSettings.LanguageForCulture(System.Globalization.CultureInfo.InvariantCulture) == 1,
    "System language selects Russian for all Russian locales and English for other or invariant locales");
Check(new AppSettings().LanguageIndex == AppSettings.LanguageForCulture(System.Globalization.CultureInfo.CurrentUICulture),
    "Fresh preferences use the system UI language before application localization changes it");
var mail = new[]
{
    new MailItem { Sender = "Анна Смирнова", To = "Иван", Subject = "План проекта", Date = new(2026, 10, 29, 23, 59, 0), Unread = true, HasAttachments = true },
    new MailItem { Sender = "Михаил", To = "Анна", Subject = "Договор", Date = new(2026, 10, 28), HasAttachments = true },
    new MailItem { Sender = "АННА", To = "Команда", Subject = "План проекта: итоги", Date = null }
};
Check(new MailFilter("from:анна subject:\"план проекта\"").Apply(mail).Count() == 2, "Cyrillic case-insensitive operators and quoted phrase");
Check(new MailFilter("to:Иван", UnreadOnly: true, AttachmentsOnly: true, From: new(2026, 10, 29), To: new(2026, 10, 29)).Apply(mail).Single() == mail[0], "Combined filters with inclusive end date");
Check(!new MailFilter(From: new(2026, 10, 30), To: new(2026, 10, 29)).Apply(mail).Any(), "Invalid range excludes all results");
Check(new MailFilter(Sort: 1).Apply(mail).Last() == mail[0], "Oldest-first ordering with unknown dates");
Check(!new MailFilter("несуществующее").Apply(mail).Any(), "Empty search result");
await ContentSearchChecks.Run(Check);
SearchCompletionChecks.Run(Check);
var html = MailBody.HtmlToText("<html><head><style>hidden</style></head><body><p>Привет &amp; мир</p><script>bad()</script><p>Вторая строка</p></body></html>");
Check(html.Contains("Привет & мир") && html.Contains("\nВторая строка") && !html.Contains("bad") && !html.Contains("hidden"), "HTML entities, line breaks and removal of active content");
Check(MailBody.Read(new Message { RtfCompressed = null, Body = "plain" }) == "plain", "Plain text body");
var formatted = MailHtml.Prepare("<html><head><style>td{color:red} @import url(https://external/style.css); p{background:url(file:///secret)}</style></head><body><table><tr><td><b>HTML письмо</b></td></tr></table><img src='https://tracking/pixel' onerror='bad()'><script>bad()</script><a href='javascript:bad()'>bad link</a></body></html>");
Check(formatted.Contains("<table>") && formatted.Contains("<b>HTML письмо</b>") && formatted.Contains("color:red"), "HTML preserves tables, formatting and inline stylesheet");
var headerHtml = MailHtml.Prepare("<table><tr><th>Заголовок</th></tr><tr><td>Данные</td></tr></table>");
Check(!headerHtml.Contains("<th") && headerHtml.Contains("font-weight:bold") && headerHtml.Contains("Заголовок"), "Table headers retain emphasis without renderer clipping");
Check(!formatted.Contains("https://tracking") && !formatted.Contains("onerror") && !formatted.Contains("<script") && !formatted.Contains("javascript:") && !formatted.Contains("file:///secret") && !formatted.Contains("@import"), "HTML excludes active content and remote/local resources");
Check(MailHtml.IsSafeLink("https://example.com") && !MailHtml.IsSafeLink("file:///secret") && !MailHtml.IsSafeLink("javascript:alert(1)"), "Only safe link protocols");
var tinyPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7x8AAAAASUVORK5CYII=");
var cidHtml = MailHtml.Prepare("<p>Inline</p><img src='cid:logo@example'>", cid => cid == "logo@example" ? MailHtml.ImageDataUri(tinyPng) : null);
Check(cidHtml.Contains("data:image/png;base64,") && !cidHtml.Contains("cid:"), "CID image resolves from the selected message");
var fakeRoot = new Folder { Name = "" };
var wrapper = new Folder { Name = "Корневой уровень файла данных Outlook" };
wrapper.Folders.Add(new Folder { Name = "Входящие", ContentCount = 3516 });
fakeRoot.Folders.Add(wrapper); fakeRoot.Folders.Add(new Folder { Name = "IPM_COMMON_VIEWS" });
var normalized = MailFolder.FromArchive(fakeRoot, Path.GetFullPath("archive.pst"));
Check(normalized.Children.Count == 1 && normalized.Children[0].Name == "Входящие" && normalized.TotalCount == 3516, "Mail folders appear directly beneath the named archive");
normalized.Expanded = false; normalized.Children[0].Reveal();
Check(normalized.Expanded && normalized.HasCount && !new MailFolder { Name = "Empty" }.HasCount, "Selected path expands and zero counters stay hidden");
Check(MailBody.HtmlToText(RtfPipe.Rtf.ToHtml(@"{\rtf1\ansi Hello\par World}" )).Contains("World"), "Cross-platform RTF conversion");
// MS-OXRTFCP LZFu stream: literal tokens followed by a dictionary end marker.
var rawRtf = System.Text.Encoding.ASCII.GetBytes(@"{\rtf1\ansi Hello\par World}");
using (var payload = new MemoryStream())
{
    var offset = 0;
    while (rawRtf.Length - offset >= 8)
    {
        payload.WriteByte(0); payload.Write(rawRtf, offset, 8); offset += 8;
    }
    var remaining = rawRtf.Length - offset;
    payload.WriteByte((byte)(1 << remaining)); payload.Write(rawRtf, offset, remaining);
    var end = ((207 + rawRtf.Length) % 4096) << 4;
    payload.WriteByte((byte)(end >> 8)); payload.WriteByte((byte)end);
    var bytes = payload.ToArray();
    uint crc = 0;
    foreach (var b in bytes)
    {
        crc ^= b;
        for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
    }
    using var compressed = new MemoryStream();
    using (var writer = new BinaryWriter(compressed, System.Text.Encoding.ASCII, true))
    {
        writer.Write(bytes.Length + 12); writer.Write(rawRtf.Length); writer.Write(0x75465A4Cu); writer.Write(crc); writer.Write(bytes);
    }
    var message = new Message { RtfCompressed = compressed.ToArray() };
    Check(message.GetRtfBody() == System.Text.Encoding.ASCII.GetString(rawRtf), "Compressed RTF stream reads from beginning");
    Check(MailBody.Read(message).Contains("Hello") && MailBody.Read(message).Contains("World"), "Compressed RTF renders readable text");
}
var service = new MailboxService();
var hierarchy = new[] { new MailFolder { Name = "Root", Children = [new() { Name = "Child" }] } };
Check(hierarchy.SelectMany(f => f.DescendantsAndSelf()).Count() == 2, "All-folder traversal");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    try { await service.ReadAsync(hierarchy, cancellation.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "Cancelled folder scan"); }
}
var invalid = Path.GetTempFileName();
try
{
    await File.WriteAllTextAsync(invalid, "invalid PST");
    try { await service.OpenAsync(invalid); throw new Exception("Invalid archive accepted"); }
    catch (EndOfStreamException) { Check(true, "Truncated archive rejected"); }
    catch (XstException) { Check(true, "Invalid archive rejected"); }
}
finally { File.Delete(invalid); }
var settingsPath = Path.Combine(Path.GetTempPath(), "xst-settings-" + Guid.NewGuid() + ".json");
try
{
    var store = new SettingsStore(settingsPath);
    await File.WriteAllTextAsync(settingsPath, "{}");
    Check(store.Load().LanguageIndex == AppSettings.SystemLanguageIndex, "Older settings without a language preference use the system default");
    var manualLanguage = 1 - AppSettings.SystemLanguageIndex;
    store.Save(new AppSettings { LanguageIndex = manualLanguage });
    Check(store.Load().LanguageIndex == manualLanguage, "An explicitly saved language overrides the system default");
    File.Delete(settingsPath);
    var empty = new MainViewModel(store);
    Check(empty.ShowWelcome && !empty.ShowMailbox && empty.Folders.Count == 0 && empty.Messages.Count == 0, "Startup shows welcome with no demo data");
    empty.Query = "q"; empty.Query = "query";
    Check(empty.SearchHistory.Count == 0, "Live typing does not store partial searches");
    for (var i = 1; i <= 6; i++) { empty.Query = "query " + i; empty.CommitSearch(true); }
    Check(empty.SearchHistory.SequenceEqual(new[] { "query 6", "query 5", "query 4", "query 3", "query 2" }), "Search history retains the five most recent completed queries");
    empty.UseSearchHistory("query 4");
    Check(empty.Query == "query 4" && empty.SearchHistory[0] == "query 4" && empty.SearchHistory.Count == 5, "Reusing a search moves it to the front without duplicates");
    var reopenedHistory = new MainViewModel(store);
    Check(reopenedHistory.SearchHistory.SequenceEqual(empty.SearchHistory), "Search history persists across application restarts");
    empty.Query = "  FROM:Anna  "; empty.CommitSearch(true); empty.Query = "from:anna"; empty.CommitSearch(true);
    Check(empty.SearchHistory.Count(q => q.Equals("from:anna", StringComparison.OrdinalIgnoreCase)) == 1, "History trims whitespace and deduplicates case-insensitively");
    empty.ClearSearch(); Check(empty.Query == "" && !empty.HasQuery && empty.SearchHistory[0] == "from:anna", "Clear-search empties only the input and preserves completed query history");
    empty.Query = "current query"; empty.CommitSearch(true); empty.ClearSearchHistory(); empty.CommitSearch();
    Check(empty.Query == "current query" && empty.NoHistory && new SearchHistoryStore(Path.ChangeExtension(settingsPath, ".search-history.json")).Load().Count == 0, "Clearing history preserves the current query and does not silently re-add it");
    empty.Settings.HistoryLimit = 10;
    for (var i = 0; i < 12; i++) { empty.Query = "expanded " + i; empty.CommitSearch(true); }
    var largerHistory = new MainViewModel(store);
    Check(largerHistory.Settings.HistoryLimit == 10 && largerHistory.SearchHistory.Count == 10 && largerHistory.SearchHistory[0] == "expanded 11", "Custom history capacity and all retained searches survive restart");
    empty.Settings.HistoryLimit = 2;
    Check(empty.SearchHistory.SequenceEqual(new[] { "expanded 11", "expanded 10" }) && new MainViewModel(store).SearchHistory.Count == 2, "Reducing capacity immediately trims and persists only the newest searches");
    empty.Settings.HistoryLimit = 0; empty.Query = "not saved"; empty.CommitSearch(true);
    Check(empty.NoHistory && new MainViewModel(store).Settings.HistoryLimit == 0 && new MainViewModel(store).NoHistory, "Zero disables recording and clears persisted history");
    empty.ResetSettings();
    Check(empty.Settings.HistoryLimit == 5 && store.Load().HistoryLimit == 5, "Reset restores the default history capacity");
    empty.OpenSettings(); Check(empty.ShowSettings && !empty.ShowWelcome, "Settings page opens from welcome");
    empty.ShowMailPage(); Check(empty.ShowWelcome && !empty.ShowSettings, "Mail navigation returns to welcome without archives");
    empty.Settings.LanguageIndex = 1;
    Check(Strings.Current["WelcomeTitle"] == "Welcome to Rxst" && empty.ArchiveName == "No archives open", "Language switches labels and view-model summaries immediately");
    var entry = Strings.Current.Entry("Settings"); var updates = 0;
    entry.PropertyChanged += (_, e) => { if (e.PropertyName == "Value") updates++; };
    Strings.Current.SetLanguage(0); Strings.Current.SetLanguage(1);
    Check(updates == 2 && entry.Value == "Settings", "Existing localization bindings receive notifications in both directions");
    Check(new AppSettings().LanguageIndex == AppSettings.SystemLanguageIndex, "Changing the app language does not replace the captured system default");
    var english = Strings.Keys.ToDictionary(k => k, k => Strings.Current[k]);
    Strings.Current.SetLanguage(0);
    Check(Strings.Keys.All(k => !string.IsNullOrWhiteSpace(Strings.Current[k]) && !string.IsNullOrWhiteSpace(english[k]) &&
        System.Text.RegularExpressions.Regex.Matches(Strings.Current[k], @"\{\d+").Select(m => m.Value).SequenceEqual(
            System.Text.RegularExpressions.Regex.Matches(english[k], @"\{\d+").Select(m => m.Value))), "Both languages contain all labels with matching format arguments");
    empty.Settings.LanguageIndex = 0;
    empty.Settings.ThemeIndex = 2; empty.Settings.EnableHtml = false; empty.Settings.AllowExternalResources = true;
    empty.Settings.ShowInlineImages = false; empty.Settings.MailFontSize = 19;
    empty.Settings.AutoSelectFirstMessage = false; empty.Settings.SearchAllArchives = false; empty.Settings.DefaultSort = 2;
    var restored = store.Load();
    Check(restored.ThemeIndex == 2 && !restored.EnableHtml && restored.AllowExternalResources && !restored.ShowInlineImages &&
        restored.MailFontSize == 19 && !restored.AutoSelectFirstMessage && !restored.SearchAllArchives && restored.DefaultSort == 2, "Every preference persists through a settings-store round trip");
    empty.ResetSettings();
    restored = store.Load();
    Check(restored.EnableHtml && !restored.AllowExternalResources && restored.ShowInlineImages && restored.AutoSelectFirstMessage &&
        restored.SearchAllArchives && restored.MailFontSize == 14 && restored.LanguageIndex == AppSettings.SystemLanguageIndex && restored.ThemeIndex == 0 && restored.DefaultSort == 0, "Reset restores and persists safe defaults including the original system language");
    await File.WriteAllTextAsync(settingsPath, "{invalid json");
    Check(store.Load().EnableHtml && store.LoadFailed, "Malformed settings recover to defaults");
    await File.WriteAllTextAsync(settingsPath, "{\"LanguageIndex\":99,\"ThemeIndex\":-1,\"MailFontSize\":999,\"DefaultSort\":100}");
    var clamped = store.Load();
    Check(clamped.LanguageIndex == AppSettings.SystemLanguageIndex && clamped.ThemeIndex == 0 && clamped.MailFontSize == 22 && clamped.DefaultSort == 3, "Out-of-range persisted preferences are validated");
}
finally { File.Delete(settingsPath); File.Delete(Path.ChangeExtension(settingsPath, ".search-history.json")); Strings.Current.SetLanguage(0); }
var resources = new FakeResources(tinyPng);
var remoteHtml = "<html><head><link rel='stylesheet' href='https://example.test/mail.css'></head><body><img src='https://example.test/image.png'><img src='cid:logo'><script>bad()</script></body></html>";
var blocked = await ExternalHtml.PrepareAsync(remoteHtml, new(), cid => MailHtml.ImageDataUri(tinyPng), loader: resources);
Check(resources.Requests == 0 && !blocked.Contains("https://example.test/") && blocked.Contains("data:image/png"), "External resources are never requested by default; CID still works");
var allowed = await ExternalHtml.PrepareAsync(remoteHtml, new(AllowExternalResources: true), cid => MailHtml.ImageDataUri(tinyPng), loader: resources);
Check(resources.Requests == 2 && allowed.Contains("data:image/png") && allowed.Contains("#112233") && !allowed.Contains("<script"), "Opt-in loads remote images and CSS while removing scripts");
var noInline = await ExternalHtml.PrepareAsync("<img src='cid:logo'><img src='data:image/png;base64," + Convert.ToBase64String(tinyPng) + "'>", new(ShowInlineImages: false), cid => MailHtml.ImageDataUri(tinyPng));
Check(!noInline.Contains("data:image/") && !noInline.Contains("cid:"), "Embedded images can be disabled independently");
var previousRequests = resources.Requests;
Check(await ExternalHtml.PrepareAsync(remoteHtml, new(EnableHtml: false, AllowExternalResources: true), loader: resources) == "" && resources.Requests == previousRequests, "Disabling HTML also prevents all external requests");
await ExternalHtml.PrepareAsync("<img src='file:///secret.png'><link rel='stylesheet' href='file:///secret.css'>", new(AllowExternalResources: true), loader: resources);
Check(resources.Requests == previousRequests, "Enabling external resources never permits local file access");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await ExternalHtml.PrepareAsync(remoteHtml, new(AllowExternalResources: true), cancellationToken: cancelled.Token, loader: resources); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(resources.Requests == previousRequests, "Cancelled message rendering starts no new requests"); }
}
using (var cancelPending = new CancellationTokenSource())
{
    var pending = new PendingResources();
    var rendering = ExternalHtml.PrepareAsync(remoteHtml, new(AllowExternalResources: true), cancellationToken: cancelPending.Token, loader: pending);
    await pending.StartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancelPending.Cancel();
    try { await rendering; throw new Exception("Pending download was not cancelled"); }
    catch (OperationCanceledException) { Check(pending.Started && pending.Cancelled, "Changing rendering policy cancels an in-flight resource request"); }
}
var archiveResults = new List<(MailFolder Root, int Count)>();
foreach (var path in args)
{
    var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
    var root = await service.OpenAsync(path);
    var items = await service.ReadAsync(root.DescendantsAndSelf(), default);
    archiveResults.Add((root, items.Count));
    var textOnly = await service.DetailsAsync(items[0], new(EnableHtml: false));
    Check(textOnly.Html == "", "Reader respects the global HTML-off preference");
    Check(items.Count > 0, $"Real archive {Path.GetFileName(path)}: {items.Count} messages");
    foreach (var item in items.Take(25))
    {
        var details = await service.DetailsAsync(item);
        var original = await service.ExportAsync(item, false);
        var properties = await service.ExportAsync(item, true);
        Check(original.Extension is "txt" or "html" or "rtf", "Native body export format");
        Check(properties.Extension == "csv" && properties.Content.StartsWith("Property,Value"), "MAPI CSV export");
        foreach (var attachment in details.Attachments)
        {
            using var output = new MemoryStream();
            await service.SaveAttachmentAsync(attachment, output);
            Check(output.Length > 0, $"Attachment bytes: {attachment.FileName}");
        }
    }
    var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
    Check(before.SequenceEqual(after), "Source archive unchanged after reading");
}
if (archiveResults.Count > 1)
{
    var all = await service.ReadAsync(archiveResults.SelectMany(a => a.Root.DescendantsAndSelf()), default);
    Check(all.Count == archiveResults.Sum(a => a.Count), "Combined search source includes every open archive");
    Check(all.Select(m => m.ArchivePath).Distinct().Count() == archiveResults.Count, "Message provenance is retained across archives");
    var first = archiveResults[0].Root;
    Check(ReferenceEquals(first, await service.OpenAsync(first.ArchivePath)), "Opening an existing archive does not duplicate it");
    var fresh = new MailboxService();
    var freshRoots = new List<MailFolder>();
    foreach (var result in archiveResults) freshRoots.Add(await fresh.OpenAsync(result.Root.ArchivePath));
    foreach (var root in freshRoots)
    {
        var items = await fresh.ReadAsync(root.DescendantsAndSelf(), default);
        foreach (var item in items.Take(2))
        {
            await fresh.DetailsAsync(item);
            var csv = await fresh.ExportAsync(item, true);
            Check(csv.Content.Contains("Property,Value") && item.Source!.Folder.XstFile == root.Source!.XstFile, "Details and export use the owning archive after multiple opens");
        }
    }
    await service.CloseAsync(first);
    var remaining = await service.ReadAsync(archiveResults.Skip(1).SelectMany(a => a.Root.DescendantsAndSelf()), default);
    Check(remaining.Count == archiveResults.Skip(1).Sum(a => a.Count), "Closing one archive preserves the remaining messages");
    var vm = new MainViewModel(new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    vm.Settings.LanguageIndex = 0; // This integration section verifies Russian status labels on any host OS.
    async Task Ready()
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!vm.CanInteract && DateTime.UtcNow < until) await Task.Delay(10);
        if (!vm.CanInteract) throw new TimeoutException("View model did not finish loading");
    }
    await Ready();
    await vm.OpenManyAsync(args);
    Check(vm.Folders.Count == args.Length && vm.AllFolders && vm.Messages.Count == archiveResults.Sum(a => a.Count), "View model opens multiple archives and searches across all of them");
    await vm.IndexingTask; await vm.SearchTask;
    var expectedIndexCount = archiveResults.Sum(a => a.Count);
    Check(!vm.IndexSummary.StartsWith("Ошибка") && vm.IndexSummary.Contains($"{expectedIndexCount} из {expectedIndexCount}"), "Background indexing processes real PST and OST messages without errors");
    var realMail = vm.Messages.First();
    var realBody = (await vm.Service.DetailsAsync(realMail, new(EnableHtml: false))).Body;
    var realTerm = System.Text.RegularExpressions.Regex.Match(realBody, @"[\p{L}\p{N}]{4,}").Value;
    Check(realTerm.Length > 0, "Public archive fixture contains searchable body text");
    vm.Query = "body:" + realTerm; await vm.SearchTask;
    Check(vm.Messages.Contains(realMail), "View model finds actual archive messages by body text through the persistent index");
    vm.Settings.SearchBody = false; await vm.IndexingTask; await vm.SearchTask;
    Check(vm.Messages.Count == 0, "Changing a search field immediately excludes cached real body results");
    vm.Settings.SearchBody = true; await vm.IndexingTask; await vm.SearchTask;
    Check(vm.Messages.Contains(realMail), "Re-enabling a search field rebuilds and restores matching real messages");
    vm.Query = "body:missing-term-before-clear";
    await vm.ClearContentIndexAsync(); await vm.SearchTask;
    Check(!vm.IsIndexing && vm.Messages.Count == 0, "Deleting an index during a pending query cancels work and leaves no stale results");
    await vm.ClearContentIndexAsync(true); await vm.IndexingTask; await vm.SearchTask;
    vm.Query = "body:" + realTerm; await vm.SearchTask;
    Check(vm.Messages.Contains(realMail), "Manual rebuild restores body search after index deletion");
    vm.Reset();
    vm.SelectedScope = vm.ArchiveScopes[1]; await Ready();
    Check(vm.Messages.Count == archiveResults[0].Count && vm.FolderTitle == vm.Folders[0].Name, "Archive scope filters results and updates the heading");
    var selectedRoots = new[] { vm.Folders[0], vm.Folders[^1] };
    vm.SetArchiveSelection(selectedRoots); await Ready();
    Check(vm.SelectedArchiveCount == 2 && vm.AllFolders && vm.Messages.Count == archiveResults[0].Count + archiveResults[^1].Count &&
        vm.Messages.All(m => selectedRoots.Any(r => r.ArchivePath == m.ArchivePath)), "An arbitrary set of checked archives supplies combined results without unselected files");
    vm.Folders[0].IsIncluded = false; await Ready();
    Check(vm.SelectedArchiveCount == 1 && vm.Messages.Count == archiveResults[^1].Count, "Unchecking an archive immediately removes its messages");
    vm.ClearArchiveSelection(); await Ready();
    Check(vm.SelectedArchiveCount == 0 && vm.Messages.Count == 0 && vm.SelectedMail == null && vm.EmptyResultTitle == Strings.Current["NoArchivesSelected"], "No selected archives means an explicit empty state instead of silently searching all");
    vm.SelectAllArchives(); await Ready(); vm.Query = "subject:test"; vm.AttachmentsOnly = true;
    Check(vm.Messages.All(m => m.HasAttachments && m.Subject.Contains("test", StringComparison.OrdinalIgnoreCase)), "Search and attachment filters apply together to selected archives");
    vm.SetArchiveSelection(selectedRoots); await Ready();
    Check(vm.Query == "subject:test" && vm.AttachmentsOnly && vm.Messages.All(m => selectedRoots.Any(r => r.ArchivePath == m.ArchivePath)), "Changing archive selection preserves the active search and filters");
    vm.Reset();
    var child = vm.Folders[0].DescendantsAndSelf().First(f => !f.IsArchive && f.MessageCount > 0);
    vm.SelectedFolder = child; await Ready();
    Check(!vm.AllFolders && vm.Messages.Count == child.MessageCount, "Choosing a folder leaves the global search scope");
    await vm.OpenManyAsync([args[1], Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pst")]);
    Check(vm.Folders.Count == args.Length && vm.Status.StartsWith("Не удалось открыть:"), "Duplicate and failed additions preserve all existing archives");
    vm.Settings.EnableHtml = false;
    foreach (var root in vm.Folders.ToArray()) await vm.CloseArchiveAsync(root);
    Check(vm.ShowWelcome && vm.Messages.Count == 0 && vm.SelectedMail == null, "Closing the last archive restores welcome");
    vm.Settings.AutoSelectFirstMessage = false; vm.Settings.SearchAllArchives = false;
    await vm.OpenManyAsync(args);
    Check(vm.AllFolders && vm.SelectedMail == null, "Explicitly opening multiple archives combines them even when the default global scope is disabled");
    await vm.StopSearchAsync();
}
Console.WriteLine($"{passed} checks passed.");

sealed class FakeResources(byte[] png) : IExternalResourceLoader
{
    public int Requests { get; private set; }
    public Task<byte[]?> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Requests++;
        return Task.FromResult<byte[]?>(uri.AbsolutePath.EndsWith(".css") ? System.Text.Encoding.UTF8.GetBytes("p{color:#112233}") : png);
    }
}
sealed class PendingResources : IExternalResourceLoader
{
    public TaskCompletionSource StartedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Started { get; private set; }
    public bool Cancelled { get; private set; }
    public async Task<byte[]?> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        Started = true; StartedSignal.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, cancellationToken); return null; }
        catch (OperationCanceledException) { Cancelled = true; throw; }
    }
}




