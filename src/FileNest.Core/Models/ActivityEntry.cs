namespace FileNest.Core.Models;

public enum ActivityStatus
{
    /// <summary>Moved into its nest with its original name.</summary>
    Sorted,

    /// <summary>Moved, but renamed because a file with that name already existed.</summary>
    Renamed,

    /// <summary>The user moved it back with Undo.</summary>
    Undone,

    /// <summary>Couldn't be moved (locked for too long, access denied, ...).</summary>
    Failed,
}

public sealed class ActivityEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Time { get; set; } = DateTime.Now;

    /// <summary>The name the file has now (after any rename).</summary>
    public string FileName { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public string DestinationPath { get; set; } = "";

    /// <summary>Nest display name, or the unmatched folder name.</summary>
    public string NestName { get; set; } = "";

    /// <summary>The nest's icon and color keys at the time of sorting, so history keeps its look.</summary>
    public string Icon { get; set; } = "folder";

    public string Color { get; set; } = "Gray";

    public long SizeBytes { get; set; }

    public ActivityStatus Status { get; set; }

    public string? Error { get; set; }

    public bool CountsAsSorted => Status is ActivityStatus.Sorted or ActivityStatus.Renamed;
}
