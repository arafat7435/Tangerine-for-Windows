namespace Tangerine;

public enum FileKind { Image, Video, Audio, Pdf, Document, Presentation, Spreadsheet, Epub, Subtitle, Archive, Other }
public enum WheelMode { Convert, Tools, Actions }
public record WheelItem(string Id, string Label, string Description, string Icon = "");
public record Category(FileKind Kind, string Name, string Heading, string Icon, string Sample);

public static class Catalog
{
    public static readonly Category[] Categories = {
        new(FileKind.Image,"Images","Image tools","\uE91B","jpg"), new(FileKind.Video,"Video","Video tools","\uE714","mov"),
        new(FileKind.Audio,"Audio","Audio tools","\uE8D6","mp3"), new(FileKind.Pdf,"PDF","PDF tools","\uEA90","pdf"),
        new(FileKind.Document,"Documents","Document conversions","\uE8A5","docx"), new(FileKind.Presentation,"Slides","Presentation conversions","\uE8F9","pptx"),
        new(FileKind.Spreadsheet,"Spreadsheets","Spreadsheet conversions","\uE80A","xlsx"), new(FileKind.Epub,"EPUB","EPUB conversions","\uE82D","epub"),
        new(FileKind.Subtitle,"Subtitles","Subtitle conversions","\uE8F2","srt"), new(FileKind.Archive,"Archives","Archive tools","\uE7B8","zip") };
    public static string Extension(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch { "jpeg" => "jpg", "tif" => "tiff", "heif" => "heic", "aif" => "aiff", "tgz" => "gz", var ext => ext };
    public static string Stem(string path) => path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(path)[..^7] : Path.GetFileNameWithoutExtension(path);
    public static FileKind Kind(string path) => Extension(path) switch
    {
        "jpg" or "jpeg" or "png" or "webp" or "heic" or "heif" or "tif" or "tiff" or "avif" or "bmp" or "svg" => FileKind.Image,
        "mp4" or "mov" or "mkv" or "webm" or "avi" or "wmv" or "gif" => FileKind.Video,
        "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aiff" or "aif" or "wma" => FileKind.Audio,
        "pdf" => FileKind.Pdf,
        "ppt" or "pptx" => FileKind.Presentation,
        "xlsx" or "xls" or "ods" or "csv" or "tsv" => FileKind.Spreadsheet,
        "epub" => FileKind.Epub,
        "srt" or "vtt" => FileKind.Subtitle,
        "zip" or "tar" or "gz" or "tgz" or "rar" => FileKind.Archive,
        "doc" or "docx" or "rtf" or "txt" or "md" or "html" or "htm" or "pages" => FileKind.Document,
        _ => FileKind.Other
    };
    public static List<WheelItem> Conversions(string path)
    {
        if (Extension(path) == "pages") return new();
        string[] formats = Kind(path) switch
        {
            FileKind.Image => new[] { "jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "pdf" },
            FileKind.Video when Extension(path) == "gif" => new[] { "mp4", "mov", "mkv", "webm", "avi", "wmv" },
            FileKind.Video => new[] { "mp4", "mov", "mkv", "webm", "avi", "wmv", "gif", "mp3" },
            FileKind.Audio => new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma" },
            FileKind.Pdf => new[] { "docx", "pptx", "xlsx", "jpg", "png", "epub", "txt", "md" },
            FileKind.Document => new[] { "docx", "pdf", "txt", "md", "html", "epub", "png", "jpg" },
            FileKind.Presentation => new[] { "pdf", "jpg", "png" },
            FileKind.Spreadsheet => new[] { "xlsx", "ods", "csv", "tsv", "pdf" },
            FileKind.Epub => new[] { "pdf", "docx", "txt", "md", "html" },
            FileKind.Subtitle => new[] { "srt", "vtt", "txt" },
            FileKind.Archive => new[] { "zip", "tar", "gz" },
            _ => Array.Empty<string>()
        };
        return formats.Where(x => x != Extension(path)).Select(x => new WheelItem(x, x == "gz" ? "GZIP" : x.ToUpperInvariant(), $"Convert to {(x == "gz" ? "GZIP" : x.ToUpperInvariant())}")).ToList();
    }
    static WheelItem T(string id, string name, string icon, string? label = null) => new(id, label ?? name.ToUpperInvariant(), name, icon);
    public static bool IsGroup(string id) => id is "createpdf" or "collage" or "join" or "merge";
    public static string[] Selection(IEnumerable<string> paths) => paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static List<WheelItem> ConversionsForSelection(string[] files)
    {
        if (files.Length == 0 || files.Any(Directory.Exists)) return new();
        return files.SelectMany(Conversions).DistinctBy(x => x.Id).Where(item => files.All(file => Extension(file) == item.Id || Conversions(file).Any(x => x.Id == item.Id))).ToList();
    }
    public static List<WheelItem> Tools(string path, int count = 1)
    {
        var tools = SingleTools(path);
        if (count > 1) switch (Kind(path))
            {
                case FileKind.Image: tools.Add(T("createpdf", "Create PDF", "\uE8A5")); tools.Add(T("collage", "Collage", "\uE8F1")); break;
                case FileKind.Video when Extension(path) != "gif": tools.Add(T("join", "Join", "\uE714")); break;
                case FileKind.Pdf: tools.Add(T("merge", "Merge", "\uE80A")); break;
            }
        return tools;
    }
    static List<WheelItem> SingleTools(string path) => Kind(path) switch
    {
        FileKind.Image when Extension(path) == "svg" => new(),
        FileKind.Image => new() { T("compress", "Compress", "\uE8E0"), T("metadata", "Metadata", "\uE8EC"), T("edit", "Edit photo", "\uE9E9", "EDIT"), T("annotate", "Annotate", "\uE70F"), T("background", "Add background", "\uE91B", "ADD BG"), T("crop", "Crop", "\uE7A8"), T("redact", "Redact", "\uED1A") },
        FileKind.Video when Extension(path) == "gif" => new() { T("metadata", "Metadata", "\uE8EC") },
        FileKind.Video => new() { T("compress", "Compress", "\uE8E0"), T("metadata", "Metadata", "\uE8EC"), T("mute", "Mute", "\uE74F"), T("trim", "Trim", "\uE8C6"), T("crop", "Crop", "\uE7A8"), T("speed", "Speed", "\uE7C4"), T("snapshot", "Snapshot", "\uE722"), T("split", "Split", "\uE714"), T("redact", "Redact", "\uED1A") },
        FileKind.Audio => new() { T("compress", "Compress", "\uE8E0"), T("metadata", "Metadata", "\uE8EC"), T("normalize", "Normalize", "\uE9D9"), T("visualize", "Visualize", "\uE9F9"), T("trim", "Trim", "\uE8C6"), T("channels", "Channels", "\uE7F5"), T("bleep", "Bleep", "\uE9D9") },
        FileKind.Pdf => new() { T("compress", "Compress", "\uE8E0"), T("metadata", "Metadata", "\uE8EC"), T("split", "Split", "\uE8F1"), T("organize", "Organize", "\uE80A") },
        FileKind.Archive => new() { T("extract", "Extract", "\uE7B8") },
        _ => new()
    };
    public static List<WheelItem> ToolsForSelection(string[] files) => files.Length == 0 || files.Any(Directory.Exists) ? new() : Tools(files[0], files.Length).Where(item => (files.Select(Kind).Distinct().Count() == 1 || item.Id is "compress" or "metadata" or "trim" or "crop" or "redact") && files.Skip(1).All(file => Tools(file, files.Length).Any(other => other.Id == item.Id))).ToList();
    public static readonly List<WheelItem> Actions = new() { T("share", "Share", "\uE72D"), T("float", "Float", "\uE718"), T("copy", "Copy", "\uE8C8"), T("move", "Move", "\uE8B7"), T("copypath", "Copy path", "\uE8A5"), T("terminal", "Terminal", "\uE756") };
}

public class AppSettings
{
    public string OutputDirectory { get; set; } = "";
    public string EngineDirectory { get; set; } = "";
    public bool GlobalDragEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool WheelSoundsEnabled { get; set; } = true;
    public int Quality { get; set; } = 82;
    public string ConvertKey { get; set; } = "Shift";
    public string ToolsKey { get; set; } = "Ctrl";
    public string ActionsKey { get; set; } = "Alt";
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TangerineWindows");
    public static AppSettings Load(string? path = null) { try { var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? Path.Combine(DataDirectory, "settings.json"))) ?? new(); settings.Validate(false); return settings; } catch { return new(); } }
    public void Validate(bool checkEngines = true)
    {
        var keys = new[] { ConvertKey, ToolsKey, ActionsKey };
        if (keys.Distinct().Count() != 3 || keys.Any(x => x is not ("Shift" or "Ctrl" or "Alt"))) throw new ArgumentException("Use a different Shift, Ctrl or Alt key for each wheel.");
        if (Quality is < 1 or > 100) throw new ArgumentException("Quality must be between 1 and 100.");
        foreach (var path in new[] { OutputDirectory, EngineDirectory })
        {
            if (path == null) throw new ArgumentException("Folder paths cannot be null.");
            if (path.Length > 0 && (!Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)) throw new ArgumentException("Choose an absolute folder path.");
            if (File.Exists(path)) throw new ArgumentException("Choose a folder, not a file.");
        }
        if (checkEngines && EngineDirectory.Length > 0 && new[] { "ffmpeg.exe", "ffprobe.exe" }.Any(name => !File.Exists(Path.Combine(EngineDirectory, name)))) throw new ArgumentException("The media engine folder must contain ffmpeg.exe and ffprobe.exe.");
    }
    public void CopyFrom(AppSettings other) { OutputDirectory = other.OutputDirectory; EngineDirectory = other.EngineDirectory; GlobalDragEnabled = other.GlobalDragEnabled; MinimizeToTray = other.MinimizeToTray; WheelSoundsEnabled = other.WheelSoundsEnabled; Quality = other.Quality; ConvertKey = other.ConvertKey; ToolsKey = other.ToolsKey; ActionsKey = other.ActionsKey; }
    public void Save(string? path = null)
    {
        Validate(); path ??= Path.Combine(DataDirectory, "settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public class OperationOptions
{
    public int MaxDimension { get; set; } = 0;
    public double Exposure { get; set; } = 0; public double Contrast { get; set; } = 0; public double Hue { get; set; } = 0;
    public string AnnotationShape { get; set; } = "Text"; public bool BlurRedaction { get; set; }
    public string RedactionColor { get; set; } = "#000000";
    public string BackgroundEnd { get; set; } = "#FFD36A"; public int Shadow { get; set; } = 24; public double BackgroundRatio { get; set; }
    public double TargetLoudness { get; set; } = -16; public double LoudnessRange { get; set; } = 11; public double TruePeak { get; set; } = -1.5;
    public double LeftVolume { get; set; } = 100; public double RightVolume { get; set; } = 100;
    public int CollageColumns { get; set; } = 0; public int CollageWidth { get; set; } = 0; public int CollageGap { get; set; } = 24; public double CollageRatio { get; set; } = 1; public bool CollageFill { get; set; }
    public List<double> SplitPoints { get; set; } = new(); public List<double> SnapshotTimes { get; set; } = new();
    public List<TimeRange> BleepRanges { get; set; } = new(); public List<PdfPageChoice> PageEdits { get; set; } = new();
    public int Quality { get; set; } = 82;
    public int X { get; set; } = 0; public int Y { get; set; } = 0; public int Width { get; set; } = 0; public int Height { get; set; } = 0;
    public double Start { get; set; } = 0; public double Duration { get; set; } = 10; public double Speed { get; set; } = 2;
    public int Channels { get; set; } = 1; public int Padding { get; set; } = 80; public int Radius { get; set; } = 24;
    public int Rotation { get; set; } = 0; public double Brightness { get; set; } = 100; public double Saturation { get; set; } = 100;
    public string Color { get; set; } = "#FF5900"; public string Text { get; set; } = "Your annotation"; public string Pages { get; set; } = "";
    public int FontSize { get; set; } = 36; public bool StripMetadata { get; set; } = true;
    public void Validate(string? operation = null)
    {
        if (new[] { Exposure, Contrast, Hue, BackgroundRatio, TargetLoudness, LoudnessRange, TruePeak, LeftVolume, RightVolume, CollageRatio }.Any(x => !double.IsFinite(x)) || Exposure is < -3 or > 3 || Contrast is < -100 or > 100 || Hue is < -180 or > 180 || MaxDimension is < 0 or > 16384 || Shadow is < 0 or > 200 || BackgroundRatio < 0 || TargetLoudness is < -36 or > -5 || LoudnessRange is < 1 or > 20 || TruePeak is < -9 or > 0 || LeftVolume is < 0 or > 200 || RightVolume is < 0 or > 200 || CollageColumns is < 0 or > 8 || CollageWidth is < 0 or > 8192 || CollageGap is < 0 or > 200 || CollageRatio is < .25 or > 4) throw new ArgumentException("Enter values within the indicated ranges.");
        if (SplitPoints.Concat(SnapshotTimes).Any(x => !double.IsFinite(x) || x < 0) || BleepRanges.Any(r => !double.IsFinite(r.Start) || !double.IsFinite(r.End) || r.Start < 0 || r.End <= r.Start)) throw new ArgumentException("Choose a valid time range.");
        if (new[] { Start, Duration, Speed, Brightness, Saturation }.Any(x => !double.IsFinite(x))) throw new ArgumentException("Enter finite numeric values.");
        if (Quality is < 1 or > 100 || Start < 0 || Duration <= 0 || Speed is < .25 or > 8 || Padding is < 0 or > 2000 || Radius is < 0 or > 2000 || FontSize is < 1 or > 1000 || Channels is < 1 or > 2 || Brightness is < 0 or > 300 || Saturation is < 0 or > 300) throw new ArgumentException("Enter values within the indicated ranges.");
        if (operation is "crop" or "redact" && (X < 0 || Y < 0 || Width <= 0 || Height <= 0)) throw new ArgumentException("Choose a rectangle with positive dimensions.");
        if (operation == "organize" && Rotation % 90 != 0) throw new ArgumentException("PDF rotation must be a multiple of 90°.");
        _ = new ImageMagick.MagickColor(Color);
        _ = new ImageMagick.MagickColor(BackgroundEnd);
        _ = new ImageMagick.MagickColor(RedactionColor);
    }
    internal OperationOptions PreviewCopy(double scale) { var copy = (OperationOptions)MemberwiseClone(); copy.X = (int)(X * scale); copy.Y = (int)(Y * scale); copy.Width = Math.Max(1, (int)(Width * scale)); copy.Height = Math.Max(1, (int)(Height * scale)); copy.Padding = (int)(Padding * scale); copy.Radius = (int)(Radius * scale); copy.Shadow = (int)(Shadow * scale); copy.FontSize = Math.Max(1, (int)(FontSize * scale)); return copy; }
    public OperationOptions Copy() { var copy = (OperationOptions)MemberwiseClone(); copy.SplitPoints = new(SplitPoints); copy.SnapshotTimes = new(SnapshotTimes); copy.BleepRanges = new(BleepRanges); copy.PageEdits = new(PageEdits); return copy; }
}
public record TimeRange(double Start, double End);
public record PdfPageChoice(int Number, int Rotation = 0);

public class Job : INotifyPropertyChanged
{
    public string Name { get; set; } = ""; public string Operation { get; set; } = "";
    string status = "Waiting"; public string Status { get => status; set { status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public string? Output { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
