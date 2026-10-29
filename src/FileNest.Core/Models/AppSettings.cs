using FileNest.Core.Services;

namespace FileNest.Core.Models;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Window material: Mica is what File Explorer uses, Acrylic is see-through glass.</summary>
public enum AppBackdrop
{
    Mica,
    Acrylic,
    Solid,
}

public sealed class AppSettings
{
    /// <summary>The folder FileNest watches (the user's Downloads folder by default).</summary>
    public string WatchFolder { get; set; } = KnownFolders.Downloads;

    /// <summary>Where nest folders are created. Empty means inside <see cref="WatchFolder"/>.</summary>
    public string DestinationRoot { get; set; } = "";

    public List<NestRule> Rules { get; set; } = DefaultRules.Create();

    /// <summary>Move files that match no nest into <see cref="UnmatchedFolderName"/>.</summary>
    public bool SortUnmatched { get; set; } = true;

    public string UnmatchedFolderName { get; set; } = "Others";

    public bool StartWithWindows { get; set; }

    public bool ShowNotifications { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public string Accent { get; set; } = "Teal";

    public AppTheme Theme { get; set; } = AppTheme.System;

    public AppBackdrop Backdrop { get; set; } = AppBackdrop.Mica;

    /// <summary>Installer files the user chose to keep; Cleanup won't suggest them again.</summary>
    public List<string> KeptInstallers { get; set; } = [];

    /// <summary>Weekly reminder when installers of already-installed apps are taking up space.</summary>
    public bool CleanupReminders { get; set; } = true;

    public DateTime? LastCleanupReminder { get; set; }

    /// <summary>Download new versions in the background and install them on the next restart.</summary>
    public bool AutoUpdate { get; set; } = true;

    public string EffectiveDestinationRoot =>
        string.IsNullOrWhiteSpace(DestinationRoot) ? WatchFolder : DestinationRoot;
}
