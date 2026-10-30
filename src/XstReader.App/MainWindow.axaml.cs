using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace XstReader.App;
public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext!;
    private Strings L => Strings.Current;
    public MainWindow()
    {
        InitializeComponent(); DataContext = new MainViewModel();
        InitializeSearchSuggestions();
        Closing += (_, _) => Vm.CommitSearch();
        Closed += async (_, _) => await Vm.StopSearchAsync();
        ApplyTheme();
        Vm.Settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.ThemeIndex)) ApplyTheme(); };
        HtmlContent.ImageLoad += (_, e) =>
        {
            if (!e.Event.Src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) e.Event.Handled = true;
        };
        HtmlContent.LinkClicked += async (_, e) =>
        {
            e.Event.Handled = true;
            if (Uri.TryCreate(e.Event.Link, UriKind.Absolute, out var uri) && MailHtml.IsSafeLink(uri.AbsoluteUri))
            {
                try { await Launcher.LaunchUriAsync(uri); }
                catch (Exception ex) { Vm.Status = L.Format("LinkError", ex.Message); }
            }
        };
        HtmlContent.RenderError += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Vm.TextMode = true; Vm.Status = L["RenderError"];
        });
        KeyDown += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.K) { Vm.ShowMailPage(); SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O) { OpenArchive(this, new()); e.Handled = true; }
            if (e.Key == Key.Escape && !e.Handled) { if (Vm.ShowSettings) Vm.ShowMailPage(); else Vm.Reset(); }
        };
        Opened += async (_, _) =>
        {
            var paths = Environment.GetCommandLineArgs().Skip(1).Where(a => a.EndsWith(".pst", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".ost", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFullPath).ToArray();
            if (paths.Length > 0) await Vm.OpenManyAsync(paths);
        };
    }
    private async void OpenArchive(object? sender, RoutedEventArgs e)
    {
        if (!Vm.CanInteract) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = L["OpenDialog"], AllowMultiple = true,
                FileTypeFilter = [new(L["OutlookArchive"]) { Patterns = ["*.pst", "*.ost"] }]
            });
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths.Length > 0) await Vm.OpenManyAsync(paths);
        }
        catch (Exception ex) { Vm.Status = L.Format("OpenFailed", ex.Message); }
    }
    private void ResetFilters(object? sender, RoutedEventArgs e) => Vm.Reset();
    private void SearchLostFocus(object? sender, RoutedEventArgs e) { SearchSuggestionsPopup.IsOpen = false; Vm.CommitSearch(); }
    private void ClearSearch(object? sender, RoutedEventArgs e) { Vm.ClearSearch(); SearchBox.Focus(); }
    private void UseSearchHistory(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string term }) { Vm.UseSearchHistory(term); HistoryButton.Flyout?.Hide(); SearchBox.Focus(); SearchBox.CaretIndex = Vm.Query.Length; }
    }
    private void ClearSearchHistory(object? sender, RoutedEventArgs e) => Vm.ClearSearchHistory();
    private async void ClearContentIndex(object? sender, RoutedEventArgs e) => await Vm.ClearContentIndexAsync();
    private async void RebuildContentIndex(object? sender, RoutedEventArgs e) => await Vm.ClearContentIndexAsync(true);
    private void SelectAllArchives(object? sender, RoutedEventArgs e) => Vm.SelectAllArchives();
    private void DeselectArchives(object? sender, RoutedEventArgs e) => Vm.ClearArchiveSelection();
    private void ArchiveCheckboxPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
    private void ShowMail(object? sender, RoutedEventArgs e) { Vm.ShowMailPage(); SearchBox.Focus(); }
    private void OpenSettings(object? sender, RoutedEventArgs e) => Vm.OpenSettings();
    private async void OpenRepository(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri("https://github.com/Rif009/Rxst")))
                Vm.Status = L.Format("LinkError", L["BrowserUnavailable"]);
        }
        catch (Exception ex) { Vm.Status = L.Format("LinkError", ex.Message); }
    }
    private void ResetAppSettings(object? sender, RoutedEventArgs e) { Vm.ResetSettings(); ApplyTheme(); }
    private async void CloseArchive(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: MailFolder root }) await Vm.CloseArchiveAsync(root);
    }
    private void ToggleTheme(object? sender, RoutedEventArgs e)
    {
        Vm.Settings.ThemeIndex = Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
    }
    private void ApplyTheme()
    {
        Application.Current!.RequestedThemeVariant = Vm.Settings.ThemeIndex switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Default, _ => ThemeVariant.Dark };
    }
    private async void ExportMail(object? sender, RoutedEventArgs e)
    {
        var mail = Vm.SelectedMail;
        if (mail == null || !Vm.CanExport) return;
        // Capture the exact content before the save dialog allows selection to change.
        var text = L.Format("ExportHeader", mail.Sender, mail.To, mail.Cc, mail.Date, mail.Subject, Vm.Body);
        try
        {
            var safe = string.Concat(mail.Subject.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = L["ExportDialog"], SuggestedFileName = safe[..Math.Min(safe.Length, 100)] + ".txt", DefaultExtension = "txt", FileTypeChoices = [new(L["TextUtf8"]) { Patterns = ["*.txt"] }] });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await using var writer = new StreamWriter(stream); await writer.WriteAsync(text);
            Vm.Status = L["TextSaved"];
        }
        catch (Exception ex) { Vm.Status = L.Format("ExportError", ex.Message); }
    }
    private async void SaveAttachment(object? sender, RoutedEventArgs e)
    {
        if (sender is not AttachmentCard { DataContext: AttachmentItem item }) return;
        var attachment = item.Source;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = L["SaveAttachment"], SuggestedFileName = Path.GetFileName(attachment.FileName ?? "attachment.bin") });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await Vm.Service.SaveAttachmentAsync(attachment, stream);
            Vm.Status = L["AttachmentSaved"];
        }
        catch (Exception ex) { Vm.Status = L.Format("SaveError", ex.Message); }
    }
    private void ExportNative(object? sender, RoutedEventArgs e) => ExportOriginal(false);
    private void ExportProperties(object? sender, RoutedEventArgs e) => ExportOriginal(true);
    private async void ExportOriginal(bool properties)
    {
        var mail = Vm.SelectedMail;
        if (mail == null || !Vm.CanExport) return;
        try
        {
            var export = await Vm.Service.ExportAsync(mail, properties);
            var safe = string.Concat(mail.Subject.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = L[properties ? "SaveProperties" : "SaveOriginal"],
                SuggestedFileName = safe[..Math.Min(safe.Length, 100)] + "." + export.Extension,
                DefaultExtension = export.Extension,
                FileTypeChoices = [new(export.Extension.ToUpperInvariant()) { Patterns = ["*." + export.Extension] }]
            });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await using var writer = new StreamWriter(stream); await writer.WriteAsync(export.Content);
            Vm.Status = L[properties ? "PropertiesSaved" : "OriginalSaved"];
        }
        catch (Exception ex) { Vm.Status = L.Format("ExportError", ex.Message); }
    }
}
