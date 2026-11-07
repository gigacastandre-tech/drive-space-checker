using System.Windows.Media;

namespace DriveSpaceChecker.Models;

public sealed class FileCategory
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required string Hex { get; init; }
    public required string[] Extensions { get; init; }
    public bool DarkText { get; init; }
}

public static class FileCategories
{
    public const int Video = 0;
    public const int Image = 1;
    public const int Audio = 2;
    public const int Archive = 3;
    public const int Program = 4;
    public const int Document = 5;
    public const int Code = 6;
    public const int System = 7;
    public const int Other = 8;
    public const int Free = 9;

    public static readonly FileCategory[] All =
    {
        new()
        {
            Index = Video, Name = "Video", Hex = "#7C3AED",
            Extensions = new[]
            {
                "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts", "m2ts",
                "vob", "3gp", "ogv", "rmvb", "asf", "mts"
            }
        },
        new()
        {
            Index = Image, Name = "Images", Hex = "#0EA5E9",
            Extensions = new[]
            {
                "jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "webp", "heic", "heif", "raw",
                "cr2", "nef", "arw", "dng", "psd", "svg", "ico", "jfif", "avif"
            }
        },
        new()
        {
            Index = Audio, Name = "Audio", Hex = "#EC4899",
            Extensions = new[]
            {
                "mp3", "flac", "wav", "aac", "ogg", "m4a", "wma", "opus", "aiff", "alac", "mid", "midi"
            }
        },
        new()
        {
            Index = Archive, Name = "Archives / disk images", Hex = "#F59E0B",
            Extensions = new[]
            {
                "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso", "img", "cab", "tgz", "zst",
                "vhd", "vhdx", "vmdk", "wim", "esd", "pkg", "dmg"
            }
        },
        new()
        {
            Index = Program, Name = "Programs / binaries", Hex = "#2563EB",
            Extensions = new[]
            {
                "exe", "dll", "msi", "sys", "ocx", "so", "bin", "appx", "msix", "msixbundle",
                "jar", "com", "scr", "drv", "cpl", "mui", "winmd", "pdb", "lib", "a", "obj"
            }
        },
        new()
        {
            Index = Document, Name = "Documents", Hex = "#10B981",
            Extensions = new[]
            {
                "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "md", "rtf",
                "odt", "ods", "odp", "epub", "mobi", "one", "vsd", "vsdx", "pages", "numbers"
            }
        },
        new()
        {
            Index = Code, Name = "Code / data", Hex = "#14B8A6",
            Extensions = new[]
            {
                "cs", "js", "ts", "jsx", "tsx", "py", "java", "cpp", "c", "h", "hpp", "json", "xml",
                "html", "htm", "css", "scss", "sql", "ps1", "bat", "cmd", "sh", "yml", "yaml", "toml",
                "ini", "cfg", "conf", "vue", "php", "rb", "go", "rs", "swift", "kt", "db", "sqlite"
            }
        },
        new()
        {
            Index = System, Name = "System / temporary", Hex = "#64748B",
            Extensions = new[]
            {
                "log", "tmp", "temp", "etl", "dmp", "bak", "old", "cache", "chk", "swp", "part",
                "crdownload", "download", "~", "thumb", "db-journal", "pf"
            }
        },
        new()
        {
            Index = Other, Name = "Other", Hex = "#94A3B8",
            Extensions = Array.Empty<string>()
        },
        new()
        {
            Index = Free, Name = "Free space", Hex = "#E2E8F0",
            Extensions = Array.Empty<string>(), DarkText = true
        }
    };

    private static readonly Dictionary<string, int> Lookup = BuildLookup();
    private static readonly Brush[] Brushes = All.Select(c => CreateBrush(c.Hex)).ToArray();

    private static Dictionary<string, int> BuildLookup()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in All)
        {
            foreach (var ext in category.Extensions)
            {
                map[ext] = category.Index;
            }
        }
        return map;
    }

    private static Brush CreateBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static int GetCategory(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return Other;
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return Lookup.TryGetValue(ext, out var index) ? index : Other;
    }

    public static FileCategory Get(int index) => All[Math.Clamp(index, 0, All.Length - 1)];

    public static Brush BrushFor(int index) => Brushes[Math.Clamp(index, 0, Brushes.Length - 1)];

    public static bool UseDarkText(int index) => Get(index).DarkText;
}
