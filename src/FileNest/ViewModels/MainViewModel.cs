using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileNest.Core.Models;
using FileNest.Core.Services;
using FileNest.Services;
using FileNest.Ui;

namespace FileNest.ViewModels;

/// <summary>
/// State and commands for the whole main window. Watcher events must be marshalled to the UI thread
/// before calling <see cref="OnFileSorted"/>, <see cref="OnFileFailed"/> and <see cref="OnDownloadsChanged"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const string SettingsFile = "settings.json";
    private const int RecentCount = 5;

    private readonly AppSettings _settings;
    private readonly ActivityLog _log;
    private readonly DownloadWatcher _watcher;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _listsDebounce;

    /// <summary>Asks the app to show a toast.</summary>
    public event Action<ToastRequest>? ToastRequested;

    /// <summary>Raised when paused/resumed, so the tray menu can follow.</summary>
    public event Action<bool>? PausedChanged;

    public MainViewModel(AppSettings settings, ActivityLog log, DownloadWatcher watcher, IDialogService dialogs,
        AppUpdater updater, Action prepareForExit)
    {
        _settings = settings;
        _log = log;
        _watcher = watcher;
        _dialogs = dialogs;
        Cleanup = new CleanupViewModel(settings, dialogs, Save, r => ToastRequested?.Invoke(r), () => ShowPage("Cleanup"));
        Updates = new UpdateViewModel(settings, updater, Save, r => ToastRequested?.Invoke(r), prepareForExit);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => RefreshTimes();
        _clock.Start();

        _listsDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _listsDebounce.Tick += (_, _) =>
        {
            _listsDebounce.Stop();
            RefreshStats();
            RebuildRecent();
            RebuildActivity();
            _ = RefreshCountsAsync();
        };

        ThemeManager.Changed += RebuildAll;
        RebuildAll();
    }

    public AppSettings Settings => _settings;

    public CleanupViewModel Cleanup { get; }

    public UpdateViewModel Updates { get; }

    /// <summary>Asks the app to bring the window up (used when a toast's action opens a page).</summary>
    public event Action? ShowWindowRequested;

    // ---------- Navigation ----------

    [ObservableProperty]
    private string _currentPage = "Overview";

    partial void OnCurrentPageChanged(string value)
    {
        // Opening Cleanup re-checks installers so the list is fresh.
        if (value == "Cleanup")
            _ = Cleanup.ScanAsync(remind: false);
    }

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    private void ShowPage(string page)
    {
        CurrentPage = page;
        ShowWindowRequested?.Invoke();
    }

    [RelayCommand]
    private void UpdateBadge() => Updates.BadgeClicked(() => CurrentPage = "Settings");

    // ---------- Status ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusKind), nameof(StatusTitle), nameof(StatusSubtitle), nameof(PauseText), nameof(PauseGlyph))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrganizeText))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeNowCommand))]
    private bool _isOrganizing;

    public bool FolderMissing => !Directory.Exists(_settings.WatchFolder);

    public string StatusKind => FolderMissing ? "Danger" : IsPaused ? "Warning" : "Success";

    public string StatusTitle => FolderMissing ? "Folder not found" : IsPaused ? "Paused" : "Watching";

    public string StatusSubtitle => FolderMissing
        ? "Choose one in Folders"
        : IsPaused ? "Not sorting new files" : $"{FolderLabel} folder";

    public string PauseText => IsPaused ? "Resume" : "Pause";

    public string PauseGlyph => IsPaused ? Icons.Play : Icons.Pause;

    public string OrganizeText => IsOrganizing ? "Organizing…" : "Organize now";

    public string WatchFolder => _settings.WatchFolder;

    public string FolderLabel => Path.GetFileName(_settings.WatchFolder.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : _settings.WatchFolder;

    public string DestinationRoot => _settings.EffectiveDestinationRoot;

    public bool UsesCustomDestination => !string.IsNullOrWhiteSpace(_settings.DestinationRoot);

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused)
            _watcher.Resume();
        else
            _watcher.Pause();
        IsPaused = _watcher.IsPaused;
        PausedChanged?.Invoke(IsPaused);
    }

    private bool CanOrganize() => !IsOrganizing;

    [RelayCommand(CanExecute = nameof(CanOrganize))]
    private async Task OrganizeNow()
    {
        if (FolderMissing)
        {
            _dialogs.Info("Folder not found", $"{_settings.WatchFolder} doesn't exist. Choose another folder in Folders.");
            return;
        }

        var candidates = CountOrganizable();
        if (candidates == 0)
        {
            ToastRequested?.Invoke(new ToastRequest($"{FolderLabel} is already tidy"));
            return;
        }

        var noun = candidates == 1 ? "file" : "files";
        if (!_dialogs.Confirm($"Organize {FolderLabel}?",
                $"FileNest will move {Format.Count(candidates)} {noun} from {FolderLabel} into {(candidates == 1 ? "its nest" : "their nests")}. You can undo each move from Activity.",
                "Organize"))
            return;

        IsOrganizing = true;
        try
        {
            var moved = await _watcher.OrganizeNowAsync();
            ToastRequested?.Invoke(new ToastRequest(moved == 1 ? "Organized 1 file" : $"Organized {Format.Count(moved)} files"));
        }
        finally
        {
            IsOrganizing = false;
        }
    }

    private int CountOrganizable()
    {
        try
        {
            return Directory.EnumerateFiles(_settings.WatchFolder)
                .Count(f => !FileSorter.ShouldIgnore(f) && !FileSorter.HasCompanionTemporary(f) && FileSorter.Plan(f, _settings) is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // ---------- Today summary ----------

    [ObservableProperty]
    private string _todayCount = "0";

    [ObservableProperty]
    private string _todayDetail = "files sorted today";

    [ObservableProperty]
    private string _allTimeText = "";

    [ObservableProperty]
    private IReadOnlyList<BarSegment> _barSegments = [];

    [ObservableProperty]
    private IReadOnlyList<LegendItem> _legend = [];

    private void RefreshStats()
    {
        var today = _log.Today();
        TodayCount = Format.Count(today.Count);
        TodayDetail = (today.Count == 1 ? "file sorted today" : "files sorted today") + (today.Count > 0 ? $" · {Format.Size(today.Bytes)}" : "");
        AllTimeText = $"{Format.Count(_log.TotalSorted)} all time";
        BarSegments = today.ByNest.Select(t => new BarSegment(t.Count, ThemeManager.NestBar(t.Color))).ToList();
        Legend = today.Count == 0
            ? []
            : today.ByNest.Select(t => new LegendItem($"{t.NestName} {t.Count}", ThemeManager.NestBar(t.Color))).ToList();
    }

    // ---------- Nests ----------

    public ObservableCollection<NestCardViewModel> Nests { get; } = [];

    public bool SortUnmatched
    {
        get => _settings.SortUnmatched;
        set
        {
            _settings.SortUnmatched = value;
            OnPropertyChanged();
            Save();
        }
    }

    public string UnmatchedFolderName => _settings.UnmatchedFolderName;

    private void RebuildNests()
    {
        Nests.Clear();
        foreach (var rule in _settings.Rules)
        {
            Nests.Add(new NestCardViewModel(rule, Path.Combine(_settings.EffectiveDestinationRoot, rule.EffectiveFolderName))
            {
                EnabledChanged = _ => Save(),
            });
        }

        _ = RefreshCountsAsync();
    }

    private async Task RefreshCountsAsync()
    {
        var cards = Nests.ToList();
        var counts = await Task.Run(() => cards.Select(c => CountFiles(c.FolderPath)).ToList());
        for (var i = 0; i < cards.Count; i++)
            cards[i].Count = counts[i];
    }

    private static int CountFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Count() : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    [RelayCommand]
    private void AddNest()
    {
        if (_dialogs.EditNest(null, _settings.Rules) is not { } rule)
            return;
        _settings.Rules.Add(rule);
        Save();
        RebuildNests();
    }

    [RelayCommand]
    private void EditNest(NestCardViewModel card)
    {
        if (_dialogs.EditNest(card.Rule, _settings.Rules) is not { } edited)
            return;
        var index = _settings.Rules.FindIndex(r => r.Id == edited.Id);
        if (index < 0)
            return;
        _settings.Rules[index] = edited;
        Save();
        RebuildNests();
    }

    [RelayCommand]
    private void DeleteNest(NestCardViewModel card)
    {
        if (!_dialogs.Confirm($"Delete {card.Name}?",
                $"New {card.Name.ToLowerInvariant()} won't be sorted any more. Files already in {card.FolderPath} stay where they are.",
                "Delete", danger: true))
            return;
        _settings.Rules.RemoveAll(r => r.Id == card.Rule.Id);
        Save();
        RebuildNests();
    }

    [RelayCommand]
    private void ResetNests()
    {
        if (!_dialogs.Confirm("Reset nests?", "Your nests will be replaced with the six default ones. Files already sorted stay where they are.", "Reset"))
            return;
        _settings.Rules = DefaultRules.Create();
        Save();
        RebuildNests();
    }

    [RelayCommand]
    private void OpenNest(NestCardViewModel card)
    {
        Directory.CreateDirectory(card.FolderPath);
        WindowsIntegration.OpenFolder(card.FolderPath);
    }

    // ---------- Activity ----------

    public ObservableCollection<ActivityItemViewModel> Recent { get; } = [];

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];

    public ObservableCollection<ActivityItemViewModel> Activity { get; } = [];

    public bool HasRecent => Recent.Count > 0 || Downloads.Count > 0;

    public bool HasActivity => Activity.Count > 0;

    [ObservableProperty]
    private string _activityFilter = "All";

    partial void OnActivityFilterChanged(string value) => RebuildActivity();

    private void RebuildRecent()
    {
        Recent.Clear();
        foreach (var entry in _log.Snapshot().Take(RecentCount))
            Recent.Add(new ActivityItemViewModel(entry));
        OnPropertyChanged(nameof(HasRecent));
    }

    private void RebuildActivity()
    {
        Activity.Clear();
        IEnumerable<ActivityEntry> entries = _log.Snapshot();
        entries = ActivityFilter switch
        {
            "Sorted" => entries.Where(e => e.Status == ActivityStatus.Sorted),
            "Renamed" => entries.Where(e => e.Status == ActivityStatus.Renamed),
            "Failed" => entries.Where(e => e.Status is ActivityStatus.Failed or ActivityStatus.Undone),
            _ => entries,
        };
        foreach (var entry in entries)
            Activity.Add(new ActivityItemViewModel(entry));
        OnPropertyChanged(nameof(HasActivity));
    }

    private void RebuildDownloads(IReadOnlyList<InProgressDownload> downloads)
    {
        Downloads.Clear();
        foreach (var d in downloads)
            Downloads.Add(new DownloadItemViewModel(d, _settings.Rules));
        OnPropertyChanged(nameof(HasRecent));
    }

    public void OnFileSorted(ActivityEntry entry, bool bulk)
    {
        _log.Add(entry);
        ScheduleListRefresh();
        if (InstallerMatcher.InstallerExtensions.Any(e => entry.FileName.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            Cleanup.InstallerArrived();

        if (!bulk && _settings.ShowNotifications)
            ToastRequested?.Invoke(new ToastRequest($"{entry.FileName} moved to {entry.NestName}", "Undo", () => UndoEntry(entry)));
    }

    public void OnFileFailed(ActivityEntry entry)
    {
        _log.Add(entry);
        ScheduleListRefresh();
        if (_settings.ShowNotifications)
            ToastRequested?.Invoke(new ToastRequest($"Couldn't move {entry.FileName}"));
    }

    private void ScheduleListRefresh()
    {
        // "Organize now" can sort hundreds of files a second; rebuild the lists once per burst.
        _listsDebounce.Stop();
        _listsDebounce.Start();
    }

    public void OnDownloadsChanged(IReadOnlyList<InProgressDownload> downloads) => RebuildDownloads(downloads);

    [RelayCommand]
    private void Undo(ActivityItemViewModel item) => UndoEntry(item.Entry);

    /// <summary>Moves a sorted file back. Also used by the toast's Undo link.</summary>
    public void UndoEntry(ActivityEntry entry)
    {
        if (!entry.CountsAsSorted)
            return;
        try
        {
            var restored = FileSorter.Undo(entry, _watcher.Suppress);
            entry.Status = ActivityStatus.Undone;
            entry.DestinationPath = restored;
            entry.FileName = Path.GetFileName(restored);
            entry.Time = DateTime.Now;
            _log.Updated();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Info("Couldn't undo", $"{entry.FileName} couldn't be moved back. {ex.Message}");
        }

        RefreshStats();
        RebuildRecent();
        RebuildActivity();
        _ = RefreshCountsAsync();
    }

    [RelayCommand]
    private void ShowInFolder(ActivityItemViewModel item) =>
        WindowsIntegration.ShowInExplorer(item.Entry.Status == ActivityStatus.Failed ? item.Entry.SourcePath : item.Entry.DestinationPath);

    [RelayCommand]
    private void ClearHistory()
    {
        if (!_dialogs.Confirm("Clear activity?", "This clears the list only. Your files and the all-time count stay as they are.", "Clear", danger: true))
            return;
        _log.Clear();
        RefreshStats();
        RebuildRecent();
        RebuildActivity();
    }

    private void RefreshTimes()
    {
        foreach (var item in Recent)
            item.RefreshTime();
        foreach (var item in Activity)
            item.RefreshTime();
        // Midnight rolls today's summary over.
        RefreshStats();
    }

    // ---------- Folders ----------

    [RelayCommand]
    private void OpenWatchFolder() => WindowsIntegration.OpenFolder(_settings.WatchFolder);

    [RelayCommand]
    private void OpenDestination()
    {
        Directory.CreateDirectory(_settings.EffectiveDestinationRoot);
        WindowsIntegration.OpenFolder(_settings.EffectiveDestinationRoot);
    }

    [RelayCommand]
    private void ChangeWatchFolder()
    {
        if (_dialogs.PickFolder("Choose the folder to watch", _settings.WatchFolder) is not { } folder)
            return;
        _settings.WatchFolder = folder;
        Save();
        _watcher.Start(folder);
        OnFoldersChanged();
    }

    [RelayCommand]
    private void ChangeDestination()
    {
        if (_dialogs.PickFolder("Choose where nests are created", _settings.EffectiveDestinationRoot) is not { } folder)
            return;
        _settings.DestinationRoot = string.Equals(folder, _settings.WatchFolder, StringComparison.OrdinalIgnoreCase) ? "" : folder;
        Save();
        OnFoldersChanged();
    }

    [RelayCommand]
    private void ResetDestination()
    {
        _settings.DestinationRoot = "";
        Save();
        OnFoldersChanged();
    }

    private void OnFoldersChanged()
    {
        OnPropertyChanged(nameof(WatchFolder));
        OnPropertyChanged(nameof(FolderLabel));
        OnPropertyChanged(nameof(DestinationRoot));
        OnPropertyChanged(nameof(UsesCustomDestination));
        OnPropertyChanged(nameof(FolderMissing));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusSubtitle));
        RebuildNests();
    }

    // ---------- Settings ----------

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            try
            {
                WindowsIntegration.SetStartWithWindows(value);
                _settings.StartWithWindows = value;
                Save();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                _dialogs.Info("Couldn't change startup", ex.Message);
            }

            OnPropertyChanged();
        }
    }

    public bool ShowNotifications
    {
        get => _settings.ShowNotifications;
        set
        {
            _settings.ShowNotifications = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            _settings.CloseToTray = value;
            OnPropertyChanged();
            Save();
        }
    }

    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value)
                return;
            _settings.Theme = value;
            OnPropertyChanged();
            Save();
            ThemeManager.Apply(value, _settings.Accent, _settings.Backdrop);
        }
    }

    /// <summary>Mica (like File Explorer), Acrylic (glassy) or Solid.</summary>
    public AppBackdrop Backdrop
    {
        get => _settings.Backdrop;
        set
        {
            if (_settings.Backdrop == value)
                return;
            _settings.Backdrop = value;
            OnPropertyChanged();
            Save();
            ThemeManager.Apply(_settings.Theme, _settings.Accent, value);
        }
    }

    public bool BackdropSupported => ThemeManager.BackdropSupported;

    [ObservableProperty]
    private IReadOnlyList<AccentOption> _accents = [];

    [RelayCommand]
    private void SetAccent(string name)
    {
        _settings.Accent = name;
        Save();
        ThemeManager.Apply(_settings.Theme, name, _settings.Backdrop);
    }

    public string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    public string DataFolder => JsonStore.DataFolder;

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(JsonStore.DataFolder);
        WindowsIntegration.OpenFolder(JsonStore.DataFolder);
    }

    // ---------- Plumbing ----------

    private void RebuildAll()
    {
        Accents = ThemeManager.Accents
            .Select(a => new AccentOption(a.Key, ThemeManager.Frozen(a.Value.Fill), a.Key == ThemeManager.AccentName))
            .ToList();
        IsPaused = _watcher.IsPaused;
        RefreshStats();
        RebuildNests();
        RebuildRecent();
        RebuildActivity();
        RebuildDownloads(_watcher.Downloads);
        Cleanup.RefreshTheme();
    }

    public void Save()
    {
        try
        {
            JsonStore.Save(SettingsFile, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Info("Couldn't save settings", ex.Message);
        }
    }
}
