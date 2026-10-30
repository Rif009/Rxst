using System.Globalization;

namespace XstReader.App;

public sealed class AttachmentItem(Attachment source) : Observable
{
    public Attachment Source { get; } = source;
    public string FileName => string.IsNullOrWhiteSpace(Source.FileName) ? Strings.Current["UnnamedFile"] : Source.FileName.Replace('\\', '/').Split('/')[^1];
    public string Extension => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
    public string Details => $"{Strings.Current[Kind.Label]} · {(string.IsNullOrEmpty(Extension) ? "FILE" : Extension)} · {SizeLabel}";
    public string SizeLabel
    {
        get
        {
            var bytes = Math.Max(0, Source.Size);
            if (bytes < 1024) return Strings.Current.Format("Bytes", bytes);
            var size = bytes / 1024d;
            var units = new[] { "SizeKB", "SizeMB", "SizeGB" };
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return size.ToString("0.#", CultureInfo.CurrentCulture) + " " + Strings.Current[units[unit]];
        }
    }
    public string IconColor => Kind.Color;
    public string IconPath => Kind.Path;
    public void RefreshLanguage() => Changed(null);
    private FileKind Kind => Extension.ToLowerInvariant() switch
    {
        "xls" or "xlsx" or "xlsm" or "xlsb" or "csv" or "ods" or "tsv" => Spreadsheet,
        "doc" or "docx" or "docm" or "odt" or "rtf" => Document,
        "ppt" or "pptx" or "pptm" or "odp" => Presentation,
        "pdf" => Pdf,
        "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" => Archive,
        "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "svg" or "tif" or "tiff" or "heic" => Image,
        "mp3" or "wav" or "flac" or "aac" or "m4a" or "ogg" or "wma" => Audio,
        "mp4" or "mkv" or "avi" or "mov" or "wmv" or "webm" or "mpeg" => Video,
        "html" or "htm" or "xml" or "json" or "cs" or "js" or "ts" or "py" or "sql" or "css" or "yml" or "yaml" => Code,
        "eml" or "msg" or "pst" or "ost" => Mail,
        "txt" or "md" or "log" or "ini" => Text,
        _ => Other
    };
    private sealed record FileKind(string Label, string Color, string Path);
    private static readonly FileKind Spreadsheet = new("FileSpreadsheet", "#23764E", "M3 3H21V21H3Z M3 9H21 M3 15H21 M9 3V21");
    private static readonly FileKind Document = new("FileDocument", "#3165AE", "M5 2H14L20 8V22H5Z M14 2V8H20 M8 12H16 M8 16H16 M8 19H13");
    private static readonly FileKind Presentation = new("FilePresentation", "#B35B2B", "M3 3H21V17H3Z M12 17V22 M7 22H17 M7 12L10 8L13 11L17 6");
    private static readonly FileKind Pdf = new("FilePdf", "#B8444B", "M5 2H14L20 8V22H5Z M14 2V8H20 M8 18L11 11L14 18 M9 16H13 M15 13H17");
    private static readonly FileKind Archive = new("FileArchive", "#9B7027", "M3 4H21V9H3Z M5 9V21H19V9 M10 13H14 M12 4V9");
    private static readonly FileKind Image = new("FileImage", "#8156AA", "M3 4H21V20H3Z M3 16L8 11L13 16L17 12L21 16 M15 8A1 1 0 1 0 13 8A1 1 0 1 0 15 8");
    private static readonly FileKind Audio = new("FileAudio", "#A04880", "M9 17V5L20 3V15 M9 8L20 6 M9 17C9 21 3 22 3 19C3 16 9 15 9 17 M20 15C20 19 14 20 14 17C14 14 20 13 20 15");
    private static readonly FileKind Video = new("FileVideo", "#4F61AA", "M3 5H16V19H3Z M16 9L22 5V19L16 15");
    private static readonly FileKind Code = new("FileCode", "#337D8A", "M8 6L2 12L8 18 M16 6L22 12L16 18 M14 3L10 21");
    private static readonly FileKind Mail = new("FileMail", "#7555B6", "M2 5H22V19H2Z M2 5L12 13L22 5 M2 19L9 11 M22 19L15 11");
    private static readonly FileKind Text = new("FileText", "#536C83", "M5 2H14L20 8V22H5Z M14 2V8H20 M8 12H16 M8 16H16 M8 19H13");
    private static readonly FileKind Other = new("FileOther", "#66627C", "M5 2H14L20 8V22H5Z M14 2V8H20");
}
