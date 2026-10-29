namespace FileNest.Ui;

/// <summary>
/// Glyphs from the bundled Tabler icon font (Assets/Fonts/tabler-icons.ttf, v3.40.0, MIT).
/// Use with the <c>IconFont</c> resource. To add one, look up its code point in the Tabler webfont CSS.
/// </summary>
public static class Icons
{
    public const string Folder = "";
    public const string Folders = "";
    public const string FolderOpen = "﫷";
    public const string FolderCog = "蘿";
    public const string FolderSearch = "落";
    public const string Minus = "";
    public const string Square = "";
    public const string Squares = "";
    public const string X = "";
    public const string LayoutDashboard = "";
    public const string Adjustments = "";
    public const string History = "";
    public const string Settings = "";
    public const string Pause = "";
    public const string Play = "";
    public const string Sparkles = "";
    public const string Plus = "";
    public const string Pencil = "";
    public const string Trash = "";
    public const string Undo = "";
    public const string Check = "";
    public const string ChevronRight = "";
    public const string AlertTriangle = "";
    public const string Download = "";
    public const string Refresh = "";
    public const string Info = "";
    public const string Eraser = "";
    public const string Dots = "";
    public const string ArrowRight = "";
    public const string Sun = "";
    public const string Moon = "";
    public const string Desktop = "";
    public const string Recycle = "";
    public const string PackageOff = "";
    public const string CloudDownload = "";
    public const string RotateClockwise = "";
    public const string CircleCheck = "";
    public const string ArrowUpCircle = "";

    // Nest icons, chosen in the nest editor.
    public const string Pdf = "﬐";
    public const string Photo = "";
    public const string Package = "";
    public const string Zip = "";
    public const string Text = "";
    public const string Movie = "";
    public const string Music = "";
    public const string Code = "";
    public const string Book = "";
    public const string Archive = "";
    public const string File = "";
    public const string FileUnknown = "";

    /// <summary>Icon keys stored in <c>NestRule.Icon</c>, in the order the editor shows them.</summary>
    public static readonly IReadOnlyList<string> NestKeys =
        ["pdf", "photo", "package", "zip", "text", "movie", "music", "code", "book", "archive", "download", "folder", "file", "dots"];

    public static string ForKey(string? key) => key switch
    {
        "pdf" => Pdf,
        "photo" => Photo,
        "package" => Package,
        "zip" => Zip,
        "text" => Text,
        "movie" => Movie,
        "music" => Music,
        "code" => Code,
        "book" => Book,
        "archive" => Archive,
        "download" => Download,
        "file" => File,
        "dots" => Dots,
        "failed" => AlertTriangle,
        _ => Folder,
    };
}
