using FileNest.Core.Models;

namespace FileNest.Core.Services;

/// <summary>
/// Decides where a file belongs and moves it there. Has no knowledge of watching or UI.
/// </summary>
public static class FileSorter
{
    /// <summary>Extensions browsers and download managers use while a download is still in progress.</summary>
    public static readonly string[] TemporaryExtensions =
    [
        ".crdownload", // Chrome, Edge, Brave
        ".part",       // Firefox
        ".partial",    // old Edge
        ".download",   // Safari
        ".opdownload", // Opera
        ".tmp",
        ".aria2",
        ".!ut",        // uTorrent
        ".!qb",        // qBittorrent
        ".bc!",        // BitComet
    ];

    public static bool IsTemporaryDownload(string path) =>
        TemporaryExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>The name a temporary download will have once it finishes, e.g. "movie.mkv" for "movie.mkv.crdownload".</summary>
    public static string FinalNameOfTemporary(string path)
    {
        var name = Path.GetFileName(path);
        var ext = TemporaryExtensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        return ext is null ? name : name[..^ext.Length];
    }

    /// <summary>
    /// Files FileNest should never touch: in-progress downloads, Office lock files, hidden and system files.
    /// </summary>
    public static bool ShouldIgnore(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name) || IsTemporaryDownload(name))
            return true;
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.'))
            return true;
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory)) != 0)
                return true;
        }
        catch (IOException)
        {
            // Gone already, or not readable yet. The caller retries.
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True while a browser still has a companion temp file next to this one. Firefox, for example,
    /// creates an empty "file.pdf" placeholder next to "file.pdf.part" until the download finishes.
    /// </summary>
    public static bool HasCompanionTemporary(string path) =>
        TemporaryExtensions.Any(ext => File.Exists(path + ext));

    /// <summary>
    /// The enabled rule whose extension matches the end of the file name. Longest extension wins,
    /// so ".tar.gz" beats ".gz".
    /// </summary>
    public static NestRule? FindRule(string fileName, IEnumerable<NestRule> rules)
    {
        var name = fileName.ToLowerInvariant();
        NestRule? best = null;
        var bestLength = 0;

        foreach (var rule in rules)
        {
            if (!rule.Enabled)
                continue;
            foreach (var ext in rule.Extensions)
            {
                if (ext.Length > bestLength && name.Length > ext.Length && name.EndsWith(ext, StringComparison.Ordinal))
                {
                    best = rule;
                    bestLength = ext.Length;
                }
            }
        }

        return best;
    }

    /// <summary>A path in <paramref name="directory"/> that doesn't exist yet: "report.pdf", then "report (1).pdf", ...</summary>
    public static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; ; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>Where the file would go, or null if it should stay where it is.</summary>
    public static (string Folder, NestRule? Rule)? Plan(string path, AppSettings settings)
    {
        var rule = FindRule(Path.GetFileName(path), settings.Rules);
        if (rule is not null)
            return (Path.Combine(settings.EffectiveDestinationRoot, rule.EffectiveFolderName), rule);
        if (settings.SortUnmatched && !string.IsNullOrWhiteSpace(settings.UnmatchedFolderName))
            return (Path.Combine(settings.EffectiveDestinationRoot, settings.UnmatchedFolderName), null);
        return null;
    }

    /// <summary>
    /// Moves the file into its nest. Returns null when no nest applies. Throws on IO errors so the
    /// caller can retry locked files.
    /// </summary>
    public static ActivityEntry? Sort(string path, AppSettings settings)
    {
        var plan = Plan(path, settings);
        if (plan is null)
            return null;

        var (folder, rule) = plan.Value;
        var fileName = Path.GetFileName(path);

        // Already inside its nest (possible when the destination root is the watch folder's parent).
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            return null;

        Directory.CreateDirectory(folder);
        var destination = UniquePath(folder, fileName);
        var size = new FileInfo(path).Length;
        File.Move(path, destination, overwrite: false);

        return new ActivityEntry
        {
            FileName = Path.GetFileName(destination),
            SourcePath = path,
            DestinationPath = destination,
            NestName = rule?.Name ?? settings.UnmatchedFolderName,
            Icon = rule?.Icon ?? "dots",
            Color = rule?.Color ?? "Gray",
            SizeBytes = size,
            Status = string.Equals(Path.GetFileName(destination), fileName, StringComparison.OrdinalIgnoreCase)
                ? ActivityStatus.Sorted
                : ActivityStatus.Renamed,
        };
    }

    /// <summary>
    /// Moves a sorted file back to the folder it came from. Returns the path it ended up at.
    /// <paramref name="beforeMove"/> gets the target path first, so a watcher can be told to leave it alone.
    /// </summary>
    public static string Undo(ActivityEntry entry, Action<string>? beforeMove = null)
    {
        var originalFolder = Path.GetDirectoryName(entry.SourcePath)!;
        Directory.CreateDirectory(originalFolder);
        var target = UniquePath(originalFolder, Path.GetFileName(entry.SourcePath));
        beforeMove?.Invoke(target);
        File.Move(entry.DestinationPath, target, overwrite: false);
        return target;
    }
}
