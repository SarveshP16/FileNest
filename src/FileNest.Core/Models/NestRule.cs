namespace FileNest.Core.Models;

/// <summary>
/// A "nest": a destination folder plus the file extensions that belong in it.
/// </summary>
public sealed class NestRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name, also used as the folder name when <see cref="FolderName"/> is empty.</summary>
    public string Name { get; set; } = "";

    /// <summary>Folder created under the destination root. Falls back to <see cref="Name"/>.</summary>
    public string FolderName { get; set; } = "";

    /// <summary>Lower-case extensions including the dot, e.g. ".pdf" or ".tar.gz".</summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>Icon key understood by the UI (e.g. "pdf", "photo", "package").</summary>
    public string Icon { get; set; } = "folder";

    /// <summary>Color key understood by the UI (e.g. "Red", "Blue").</summary>
    public string Color { get; set; } = "Gray";

    public bool Enabled { get; set; } = true;

    public string EffectiveFolderName => string.IsNullOrWhiteSpace(FolderName) ? Name : FolderName;

    public NestRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        FolderName = FolderName,
        Extensions = [.. Extensions],
        Icon = Icon,
        Color = Color,
        Enabled = Enabled,
    };

    /// <summary>
    /// Turns free text like "png, .JPG  webp" into [".png", ".jpg", ".webp"].
    /// </summary>
    public static List<string> ParseExtensions(string text) =>
        text.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .Where(e => e.Length > 1)
            .Distinct()
            .ToList();
}
