using System.Text.Json;
using System.Globalization;

namespace XstReader.App;

public sealed class AppSettings : Observable
{
    // Capture the OS UI culture before Strings changes the application's culture.
    public static int SystemLanguageIndex { get; } = LanguageForCulture(CultureInfo.CurrentUICulture);
    public static int LanguageForCulture(CultureInfo culture) => culture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    private int languageIndex = SystemLanguageIndex, themeIndex, defaultSort, mailFontSize = 14;
    private int historyLimit = 5;
    private bool searchBody = true, searchAttachmentNames = true, searchFileContents, searchCaseSensitive, searchWholeWords;
    private int searchMaxFileMb = 20, searchMaxTextChars = 500000, searchTimeoutSeconds = 10;
    private string searchExcludedExtensions = "";
    public bool SearchBody { get => searchBody; set => Set(ref searchBody, value); }
    public bool SearchAttachmentNames { get => searchAttachmentNames; set => Set(ref searchAttachmentNames, value); }
    public bool SearchFileContents { get => searchFileContents; set => Set(ref searchFileContents, value); }
    public bool SearchCaseSensitive { get => searchCaseSensitive; set => Set(ref searchCaseSensitive, value); }
    public bool SearchWholeWords { get => searchWholeWords; set => Set(ref searchWholeWords, value); }
    public int SearchMaxFileMb { get => searchMaxFileMb; set => Set(ref searchMaxFileMb, Math.Clamp(value, 1, 100)); }
    public int SearchMaxTextChars { get => searchMaxTextChars; set => Set(ref searchMaxTextChars, Math.Clamp(value, 10000, 2000000)); }
    public int SearchTimeoutSeconds { get => searchTimeoutSeconds; set => Set(ref searchTimeoutSeconds, Math.Clamp(value, 1, 60)); }
    public string SearchExcludedExtensions { get => searchExcludedExtensions; set => Set(ref searchExcludedExtensions, value ?? ""); }
    public SearchOptions SearchSnapshot() => new(SearchBody, SearchAttachmentNames, SearchFileContents, SearchCaseSensitive,
        SearchWholeWords, SearchMaxFileMb, SearchMaxTextChars, SearchTimeoutSeconds, SearchExcludedExtensions);
    public int HistoryLimit { get => historyLimit; set => Set(ref historyLimit, Math.Clamp(value, 0, 100)); }
    private bool enableHtml = true, allowExternalResources, showInlineImages = true, autoSelectFirstMessage = true, searchAllArchives = true;
    public int LanguageIndex { get => languageIndex; set => Set(ref languageIndex, value is 0 or 1 ? value : SystemLanguageIndex); }
    public int ThemeIndex { get => themeIndex; set => Set(ref themeIndex, Math.Clamp(value, 0, 2)); }
    public bool EnableHtml { get => enableHtml; set => Set(ref enableHtml, value); }
    public bool AllowExternalResources { get => allowExternalResources; set => Set(ref allowExternalResources, value); }
    public bool ShowInlineImages { get => showInlineImages; set => Set(ref showInlineImages, value); }
    public bool AutoSelectFirstMessage { get => autoSelectFirstMessage; set => Set(ref autoSelectFirstMessage, value); }
    public bool SearchAllArchives { get => searchAllArchives; set => Set(ref searchAllArchives, value); }
    public int DefaultSort { get => defaultSort; set => Set(ref defaultSort, Math.Clamp(value, 0, 3)); }
    public int MailFontSize { get => mailFontSize; set => Set(ref mailFontSize, Math.Clamp(value, 12, 22)); }
    public MailRenderOptions Snapshot() => new(EnableHtml, AllowExternalResources, ShowInlineImages, MailFontSize);
}

public sealed record MailRenderOptions(bool EnableHtml = true, bool AllowExternalResources = false, bool ShowInlineImages = true, int FontSize = 14);

public sealed class SettingsStore(string? path = null)
{
    public string FilePath { get; } = path ?? Environment.GetEnvironmentVariable("XST_SETTINGS_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XstReader", "settings.json");
    public bool LoadFailed { get; private set; }
    public AppSettings Load()
    {
        try
        {
            LoadFailed = false;
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadFailed = true; return new();
        }
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
