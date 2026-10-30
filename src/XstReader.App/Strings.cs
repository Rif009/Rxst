using System.Globalization;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace XstReader.App;

public sealed class Strings : Observable
{
    public static Strings Current { get; } = new();
    public bool English { get; private set; }
    private readonly Dictionary<string, LocalizedText> entries = [];
    public LocalizedText Entry(string key)
    {
        if (!entries.TryGetValue(key, out var entry)) entries[key] = entry = new(key);
        return entry;
    }
    public string this[string key] => Text.TryGetValue(key, out var pair) ? English ? pair.En : pair.Ru : key;
    public string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);
    public void SetLanguage(int index)
    {
        English = index == 1;
        var culture = CultureInfo.GetCultureInfo(English ? "en-US" : "ru-RU");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        foreach (var entry in entries.Values.ToArray()) entry.Refresh();
        Changed(null); Changed("Item[]");
    }
    public static IReadOnlyCollection<string> Keys => Text.Keys;
    private static readonly Dictionary<string, (string Ru, string En)> Text = new()
    {
        ["Title"] = ("Rxst · Почтовый архив", "Rxst · Mail archive"),
        ["AddArchives"] = ("＋ Добавить архивы", "＋ Add archives"),
        ["SearchHint"] = ("Поиск писем и вложений · Ctrl+K", "Search messages and attachments · Ctrl+K"),
        ["SuggestionTitle"] = ("Где искать", "Search fields"),
        ["SuggestionKeys"] = ("↑ ↓ выбрать · Enter / Tab вставить · Esc закрыть · Ctrl+Space подсказки", "↑ ↓ select · Enter / Tab insert · Esc close · Ctrl+Space suggestions"),
        ["SuggestionDisabled"] = ("Область выключена — включите её в настройках поиска", "This field is disabled — enable it in search settings"),
        ["SuggestFrom"] = ("Отправитель · from:Анна", "Sender · from:Anna"),
        ["SuggestTo"] = ("Получатель · to:Иван", "Recipient · to:Alex"),
        ["SuggestSubject"] = ("Тема письма · subject:\"план проекта\"", "Message subject · subject:\"project plan\""),
        ["SuggestBody"] = ("Текст письма · body:договор", "Message body · body:contract"),
        ["SuggestAttachment"] = ("Название вложения · attachment:отчёт", "Attachment name · attachment:report"),
        ["SuggestContent"] = ("Текст внутри файлов · content:\"общая сумма\"", "Text inside attached files · content:\"total amount\""),
        ["SuggestFilename"] = ("Название вложения · синоним attachment:", "Attachment name · alias for attachment:"),
        ["ContentSearchSettings"] = ("Поиск по содержимому", "Content search"),
        ["SearchBody"] = ("Искать в тексте писем", "Search message bodies"),
        ["SearchAttachmentNames"] = ("Искать по названиям вложений", "Search attachment names"),
        ["SearchFileContents"] = ("Искать в содержимом вложенных файлов", "Search inside attached files"),
        ["ContentFormats"] = ("PDF с текстом, DOCX, XLS/XLSX/XLSB, PPTX, ODT/ODS/ODP, EPUB, RTF, HTML, TXT, CSV и исходный код. Сканированные PDF, изображения, старые DOC/PPT и зашифрованные файлы не распознаются; их имена доступны для поиска.", "Text PDFs, DOCX, XLS/XLSX/XLSB, PPTX, ODT/ODS/ODP, EPUB, RTF, HTML, TXT, CSV and source code. Scanned PDFs, images, legacy DOC/PPT and encrypted files are not extracted; their names remain searchable."),
        ["SearchCaseSensitive"] = ("Учитывать регистр", "Case-sensitive search"),
        ["SearchWholeWords"] = ("Только целые слова", "Whole words only"),
        ["SearchMaxFileMb"] = ("Максимальный размер файла, МБ", "Maximum attachment size, MB"),
        ["SearchMaxTextChars"] = ("Лимит символов текста письма и всех вложений (для каждой группы)", "Character limit for body and combined attachments (per group)"),
        ["SearchTimeoutSeconds"] = ("Тайм-аут обработки одного файла, секунд", "Processing timeout per file, seconds"),
        ["SearchExcludedExtensions"] = ("Не читать содержимое форматов", "Skip content for these extensions"),
        ["SearchExcludedHint"] = ("Например: pdf, xlsx, log. Поиск по имени сохраняется.", "For example: pdf, xlsx, log. Names remain searchable."),
        ["SearchSyntax"] = ("Все слова запроса должны совпасть. Кавычки задают фразу. Поля: from:, to:, subject:, body:, attachment: (или filename:), content:. Отключённые области не участвуют даже с оператором.", "All query terms must match. Use quotes for phrases. Fields: from:, to:, subject:, body:, attachment: (or filename:), content:. Disabled fields stay excluded even with an operator."),
        ["IndexPrivacy"] = ("Индекс хранится только на этом устройстве и содержит извлечённый текст. Ничего не отправляется в интернет. Архивы не изменяются. При первом открытии результаты дополняются по мере индексирования; повторное открытие использует сохранённый индекс. Изменение архива или параметров обработки обновляет индекс.", "The index stays on this device and contains extracted text. Nothing is sent online. Archives remain unchanged. Results expand during first-time indexing; reopening reuses the saved index. Archive or extraction-setting changes refresh the index."),
        ["IndexProgress"] = ("Индексирование: {0} из {1} писем · Пропущено файлов: {2} · Ошибок: {3}", "Indexing: {0} of {1} messages · Skipped files: {2} · Errors: {3}"),
        ["IndexReady"] = ("В текущей области проиндексировано: {0} из {1} · Пропущено файлов: {2} · Ошибок: {3}", "Indexed in current scope: {0} of {1} · Skipped files: {2} · Errors: {3}"),
        ["IndexError"] = ("Ошибка поискового индекса: {0}", "Search index error: {0}"),
        ["SearchingContent"] = ("Поиск в локальном индексе…", "Searching the local index…"),
        ["ClearIndex"] = ("Удалить поисковый индекс", "Delete search index"),
        ["RebuildIndex"] = ("Перестроить индекс", "Rebuild index"),
        ["IndexCleared"] = ("Поисковый индекс удалён. Для повторного создания нажмите «Перестроить индекс» или откройте папку архива.", "Search index deleted. Choose Rebuild index or open an archive folder to recreate it."),
        ["OpenHint"] = ("Один или несколько PST / OST · Ctrl+O", "One or more PST / OST files · Ctrl+O"),
        ["Theme"] = ("Тема оформления", "Appearance"),
        ["Mail"] = ("Почта", "Mail"),
        ["Settings"] = ("Настройки", "Settings"),
        ["Workspace"] = ("РАБОЧЕЕ ПРОСТРАНСТВО", "WORKSPACE"),
        ["Folders"] = ("АРХИВЫ И ПАПКИ", "ARCHIVES & FOLDERS"),
        ["CloseArchive"] = ("Закрыть архив", "Close archive"),
        ["LocalArchives"] = ("●  Локальные архивы", "●  Local archives"),
        ["ReadOnlyHint"] = ("Только чтение · без Outlook", "Read only · no Outlook required"),
        ["ResetFilters"] = ("Сбросить", "Reset"),
        ["Unread"] = ("Непрочитанные", "Unread"),
        ["WithAttachments"] = ("С вложениями", "Has attachments"),
        ["GlobalSearch"] = ("Все письма выбранных архивов", "All messages in selected archives"),
        ["ClearSearch"] = ("Очистить поиск", "Clear search"),
        ["SearchHistory"] = ("Последние запросы", "Recent searches"),
        ["NoSearchHistory"] = ("История поиска пока пуста", "No recent searches yet"),
        ["HistorySettings"] = ("Локальная история поиска", "Local search history"),
        ["HistoryHint"] = ("Завершённые запросы хранятся только на этом компьютере. Enter сохраняет запрос; очистка истории не меняет текущий поиск.", "Completed searches are stored only on this computer. Enter saves a search; clearing history leaves the current search unchanged."),
        ["HistorySummary"] = ("Сохранено запросов: {0} из {1}", "Saved searches: {0} of {1}"),
        ["HistoryLimit"] = ("Количество сохраняемых запросов", "Number of searches to keep"),
        ["HistoryLimitHint"] = ("От 0 до 100. Значение 0 отключает историю. При уменьшении лимита старые запросы удаляются.", "From 0 to 100. Set to 0 to disable history. Reducing the limit removes older searches."),
        ["ClearHistory"] = ("Очистить историю поиска", "Clear search history"),
        ["HistoryCleared"] = ("История поиска очищена", "Search history cleared"),
        ["HistoryError"] = ("Не удалось сохранить историю поиска: {0}", "Could not save search history: {0}"),
        ["ArchiveSelectionHelp"] = ("Отметьте архивы для общей выдачи", "Check archives to combine their messages"),
        ["IncludeArchive"] = ("Включить архив в общую выдачу", "Include archive in combined results"),
        ["SelectedArchiveCount"] = ("Выбрано {0} из {1}", "Selected {0} of {1}"),
        ["SelectedArchivesTitle"] = ("Архивы · {0}", "Archives · {0}"),
        ["SelectAllArchives"] = ("Все", "All"),
        ["DeselectArchives"] = ("Ни одного", "None"),
        ["NoArchivesSelected"] = ("Архивы не выбраны", "No archives selected"),
        ["SelectArchivesHint"] = ("Отметьте один или несколько архивов слева, чтобы показать их письма.", "Check one or more archives on the left to display their messages."),
        ["AdvancedFilters"] = ("Дополнительные фильтры", "More filters"),
        ["SenderHint"] = ("Отправитель содержит…", "Sender contains…"),
        ["DateRange"] = ("Период получения", "Received date range"),
        ["DateFrom"] = ("С даты", "From date"),
        ["DateTo"] = ("По дату включительно", "Through date (inclusive)"),
        ["Messages"] = ("ПИСЬМА", "MESSAGES"),
        ["Newest"] = ("Сначала новые", "Newest first"),
        ["Oldest"] = ("Сначала старые", "Oldest first"),
        ["BySender"] = ("По отправителю", "By sender"),
        ["BySubject"] = ("По теме", "By subject"),
        ["NoResults"] = ("Ничего не найдено", "No messages found"),
        ["NoResultsHint"] = ("Измените запрос или сбросьте фильтры.", "Try another search or reset the filters."),
        ["ReadingPane"] = ("ОБЛАСТЬ ЧТЕНИЯ", "READING PANE"),
        ["Export"] = ("↓  Экспорт письма", "↓  Export message"),
        ["ExportText"] = ("Текст UTF-8 (.txt)", "UTF-8 text (.txt)"),
        ["ExportNative"] = ("Исходный формат (.html / .rtf / .txt)", "Original format (.html / .rtf / .txt)"),
        ["ExportProperties"] = ("Свойства MAPI (.csv)", "MAPI properties (.csv)"),
        ["SelectMail"] = ("Выберите письмо", "Select a message"),
        ["SelectMailHint"] = ("Всё важное — в одном месте", "Everything important, in one place"),
        ["PlainText"] = ("Показать текст без оформления", "Show plain text"),
        ["Properties"] = ("Свойства письма · MAPI", "Message properties · MAPI"),
        ["Footer"] = ("by rif009", "by rif009"),
        ["WelcomeTag"] = ("ВАША ПОЧТА. ВАШЕ ПРОСТРАНСТВО.", "YOUR MAIL. YOUR SPACE."),
        ["WelcomeTitle"] = ("Добро пожаловать в Rxst", "Welcome to Rxst"),
        ["WelcomeBody"] = ("Откройте почтовый архив и вернитесь к важным разговорам. Все письма остаются на вашем устройстве.", "Open a mail archive and rediscover important conversations. Your messages stay on your device."),
        ["WelcomeOpen"] = ("Открыть PST или OST", "Open PST or OST"),
        ["WelcomeMulti"] = ("Несколько архивов — один поиск", "Multiple archives, one search"),
        ["WelcomeMultiBody"] = ("Добавляйте файлы вместе или по очереди. Ищите и фильтруйте письма сразу во всех архивах.", "Add files together or one at a time. Search and filter messages across every archive."),
        ["WelcomeRead"] = ("Читайте в привычном виде", "Read mail the familiar way"),
        ["WelcomeReadBody"] = ("Папки, вложения и HTML-письма. Оформление и загрузку ресурсов можно изменить в настройках.", "Folders, attachments and HTML messages. Adjust rendering and resource loading in Settings."),
        ["NoArchives"] = ("Архивы не открыты", "No archives open"),
        ["AllArchives"] = ("Все архивы", "All archives"),
        ["ArchiveCount"] = ("Открыто архивов: {0}", "Archives open: {0}"),
        ["ResultCount"] = ("{0:N0} из {1:N0} писем", "{0:N0} of {1:N0} messages"),
        ["ToLabel"] = ("Кому: {0}", "To: {0}"),
        ["CcLabel"] = ("Копия: {0}", "Cc: {0}"),
        ["Ready"] = ("Готово", "Ready"),
        ["Opening"] = ("Открытие архивов…", "Opening archives…"),
        ["OpenFailed"] = ("Не удалось открыть: {0}", "Could not open: {0}"),
        ["StartHint"] = ("Добавьте PST или OST для начала работы", "Add a PST or OST file to get started"),
        ["ReadingAll"] = ("Чтение заголовков в открытых архивах…", "Reading headers across open archives…"),
        ["ReadingFolder"] = ("Чтение папки…", "Reading folder…"),
        ["Loaded"] = ("{0} · {1:N0} писем · только чтение", "{0} · {1:N0} messages · read only"),
        ["FolderError"] = ("Ошибка чтения папки: {0}", "Could not read folder: {0}"),
        ["DateError"] = ("Начальная дата должна быть раньше конечной.", "The start date must be before the end date."),
        ["ReadingMail"] = ("Чтение письма…", "Reading message…"),
        ["MailError"] = ("Не удалось прочитать письмо: {0}", "Could not read message: {0}"),
        ["FiltersReset"] = ("Фильтры сброшены", "Filters reset"),
        ["NoBody"] = ("Текст письма отсутствует.", "This message has no text body."),
        ["Smime"] = ("Письмо защищено S/MIME. Расшифровка не поддерживается. Вложение можно сохранить.", "This message is protected with S/MIME. Decryption is not supported. You can save the attachment."),
        ["NoDate"] = ("Без даты", "No date"),
        ["AttachmentMark"] = ("⌁ Вложения", "⌁ Attachments"),
        ["Bytes"] = ("{0:N0} байт", "{0:N0} bytes"),
        ["SizeKB"] = ("КБ", "KB"),
        ["SizeMB"] = ("МБ", "MB"),
        ["SizeGB"] = ("ГБ", "GB"),
        ["UnnamedFile"] = ("Файл без названия", "Unnamed file"),
        ["AttachmentSummary"] = ("Вложения · {0}", "Attachments · {0}"),
        ["FileSpreadsheet"] = ("Электронная таблица", "Spreadsheet"),
        ["FileDocument"] = ("Документ", "Document"),
        ["FilePresentation"] = ("Презентация", "Presentation"),
        ["FilePdf"] = ("Документ PDF", "PDF document"),
        ["FileArchive"] = ("Архив", "Archive"),
        ["FileImage"] = ("Изображение", "Image"),
        ["FileAudio"] = ("Аудиозапись", "Audio"),
        ["FileVideo"] = ("Видео", "Video"),
        ["FileCode"] = ("Исходный код", "Source code"),
        ["FileMail"] = ("Почтовый файл", "Mail file"),
        ["FileText"] = ("Текстовый файл", "Text file"),
        ["FileOther"] = ("Файл", "File"),
        ["UnknownSender"] = ("Неизвестный отправитель", "Unknown sender"),
        ["NoSubject"] = ("(Без темы)", "(No subject)"),
        ["Unnamed"] = ("Без названия", "Untitled"),
        ["FolderDescription"] = ("{0} · {1:N0} писем", "{0} · {1:N0} messages"),
        ["ExternalImage"] = ("Внешнее изображение", "External image"),
        ["HiddenImage"] = ("Изображение скрыто", "Image hidden"),
        ["Language"] = ("Язык интерфейса", "Interface language"),
        ["SettingsHint"] = ("Настройте Rxst под себя. Изменения применяются сразу и сохраняются автоматически.", "Make Rxst your own. Changes apply immediately and are saved automatically."),
        ["General"] = ("Общие", "General"),
        ["Dark"] = ("Тёмная", "Dark"),
        ["Light"] = ("Светлая", "Light"),
        ["System"] = ("Системная", "System"),
        ["Reading"] = ("Чтение писем", "Reading messages"),
        ["EnableHtml"] = ("Отображать HTML-письма с оформлением", "Display formatted HTML messages"),
        ["EnableHtmlHint"] = ("При выключении все письма показываются как текст.", "When disabled, every message is displayed as plain text."),
        ["ExternalResources"] = ("Загружать внешние изображения и стили", "Load external images and styles"),
        ["ExternalHint"] = ("Разрешает HTTP/HTTPS-запросы из писем. Отправитель может узнать, что письмо было открыто. Скрипты не выполняются.", "Allows HTTP/HTTPS requests from messages. Senders may learn that a message was opened. Scripts never run."),
        ["InlineImages"] = ("Показывать встроенные изображения", "Show embedded images"),
        ["InlineHint"] = ("Изображения из вложений письма (CID) и встроенных данных.", "Images stored in message attachments (CID) and inline data."),
        ["MailFontSize"] = ("Размер текста письма", "Message text size"),
        ["Behavior"] = ("Поиск и навигация", "Search and navigation"),
        ["AutoSelect"] = ("Автоматически выбирать первое письмо", "Automatically select the first message"),
        ["DefaultGlobal"] = ("При добавлении файла искать по всем архивам", "Search all archives after adding a file"),
        ["DefaultSort"] = ("Сортировка по умолчанию", "Default sort order"),
        ["Defaults"] = ("Восстановить настройки по умолчанию", "Restore default settings"),
        ["SettingsSaved"] = ("Настройки сохранены", "Settings saved"),
        ["SettingsError"] = ("Не удалось сохранить настройки: {0}", "Could not save settings: {0}"),
        ["SettingsLoadError"] = ("Не удалось прочитать настройки. Используются значения по умолчанию.", "Could not read settings. Default values are in use."),
        ["About"] = ("О приложении", "About"),
        ["Licenses"] = ("Лицензии и сторонние компоненты", "Licenses and third-party components"),
        ["RepositoryLink"] = ("Открыть репозиторий Rxst на GitHub в браузере", "Open the Rxst GitHub repository in your browser"),
        ["BrowserUnavailable"] = ("Не удалось запустить браузер по умолчанию", "Could not launch the default browser"),
        ["AboutBody"] = ("Просмотр PST / OST без Outlook. Ядро: Dijji / XstReader (MS-PL).\nCtrl+O — добавить архивы · Ctrl+K — поиск · Esc — вернуться к почте / сбросить фильтры.\nАрхивы открываются только для чтения.", "View PST / OST files without Outlook. Reader: Dijji / XstReader (MS-PL).\nCtrl+O — add archives · Ctrl+K — search · Esc — return to mail / reset filters.\nArchives are opened read only."),
        ["OpenDialog"] = ("Добавить почтовые архивы", "Add mail archives"),
        ["OutlookArchive"] = ("Архив Outlook", "Outlook archive"),
        ["LinkError"] = ("Не удалось открыть ссылку: {0}", "Could not open link: {0}"),
        ["RenderError"] = ("Не удалось отобразить оформление. Показана текстовая версия.", "Could not display formatting. Plain text is shown instead."),
        ["ExportDialog"] = ("Экспорт письма", "Export message"),
        ["TextUtf8"] = ("Текст UTF-8", "UTF-8 text"),
        ["ExportHeader"] = ("От: {0}\nКому: {1}\nКопия: {2}\nДата: {3:O}\nТема: {4}\n\n{5}", "From: {0}\nTo: {1}\nCc: {2}\nDate: {3:O}\nSubject: {4}\n\n{5}"),
        ["TextSaved"] = ("Письмо сохранено в UTF-8", "Message saved as UTF-8"),
        ["ExportError"] = ("Ошибка экспорта: {0}", "Export failed: {0}"),
        ["SaveAttachment"] = ("Сохранить вложение", "Save attachment"),
        ["AttachmentSaved"] = ("Вложение сохранено", "Attachment saved"),
        ["SaveError"] = ("Ошибка сохранения: {0}", "Save failed: {0}"),
        ["SaveProperties"] = ("Сохранить свойства MAPI", "Save MAPI properties"),
        ["SaveOriginal"] = ("Сохранить исходное письмо", "Save original message"),
        ["PropertiesSaved"] = ("Свойства MAPI сохранены", "MAPI properties saved"),
        ["OriginalSaved"] = ("Письмо сохранено в исходном формате", "Message saved in its original format")
    };
}

public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding(nameof(LocalizedText.Value)) { Source = Strings.Current.Entry(key), Mode = BindingMode.OneWay };
}

public sealed class LocalizedText(string key) : Observable
{
    public string Value => Strings.Current[key];
    public void Refresh() => Changed(nameof(Value));
}

