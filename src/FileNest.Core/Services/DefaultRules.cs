using FileNest.Core.Models;

namespace FileNest.Core.Services;

public static class DefaultRules
{
    public static List<NestRule> Create() =>
    [
        Rule("PDFs", "pdf", "Red", ".pdf"),
        Rule("Images", "photo", "Blue",
            ".png", ".jpg", ".webp", ".jpeg", ".gif", ".bmp", ".svg", ".heic", ".avif", ".tif", ".tiff", ".ico"),
        Rule("Installers", "package", "Amber",
            ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle"),
        Rule("Archives", "zip", "Purple",
            ".zip", ".rar", ".7z", ".tar", ".gz", ".tar.gz", ".tgz", ".bz2", ".xz", ".iso"),
        Rule("Documents", "text", "Green",
            ".docx", ".xlsx", ".doc", ".xls", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp", ".csv", ".md", ".epub"),
        Rule("Media", "movie", "Gray",
            ".mp4", ".mkv", ".mp3", ".mov", ".avi", ".webm", ".wmv", ".wav", ".flac", ".m4a", ".aac", ".ogg"),
    ];

    private static NestRule Rule(string name, string icon, string color, params string[] extensions) => new()
    {
        Name = name,
        Icon = icon,
        Color = color,
        Extensions = [.. extensions],
    };
}
