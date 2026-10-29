using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileNest.Core.Models;
using FileNest.Core.Services;
using FileNest.Services;
using FileNest.Ui;
using Microsoft.VisualBasic.FileIO;

namespace FileNest.ViewModels;

public sealed partial class CleanupItemViewModel : ObservableObject
{
    public CleanupItemViewModel(InstallerMatch match, Action selectionChanged)
    {
        Match = match;
        _isSelected = match.Confidence == MatchConfidence.High;
        _selectionChanged = selectionChanged;
        (PillText, PillBackground, PillForeground) = match.Confidence == MatchConfidence.High
            ? ("Installed", Res.Brush("SuccessBgBrush"), Res.Brush("SuccessTextBrush"))
            : ("Probably installed", Res.Brush("WarningBgBrush"), Res.Brush("WarningTextBrush"));
    }

    private readonly Action _selectionChanged;

    public InstallerMatch Match { get; }

    public string FileName => Path.GetFileName(Match.Installer.Path);

    public string FilePath => Match.Installer.Path;

    public string SizeText => Format.Size(Match.Installer.SizeBytes);

    public string Details
    {
        get
        {
            var app = Match.App;
            var parts = new List<string> { $"{app.Name} is installed" };
            if (!string.IsNullOrWhiteSpace(app.Version) && !app.Name.Contains(app.Version, StringComparison.Ordinal))
                parts.Add($"version {app.Version}");
            if (app.InstallDate is { } date)
                parts.Add($"since {date:MMM d, yyyy}");
            parts.Add($"downloaded {Format.Ago(Match.Installer.Modified)}");
            return string.Join(" · ", parts);
        }
    }

    public string PillText { get; }

    public Brush PillBackground { get; }

    public Brush PillForeground { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

/// <summary>
/// Installer cleanup: finds installers (.exe/.msi) in Downloads and the installer nests whose app is already
/// installed, and moves the ones you pick to the Recycle Bin.
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private static readonly TimeSpan ReminderEvery = TimeSpan.FromDays(7);

    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly Action _save;
    private readonly Action<ToastRequest> _toast;
    private readonly Action _openPage;
    private readonly DispatcherTimer _daily = new() { Interval = TimeSpan.FromHours(24) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(5) };

    public CleanupViewModel(AppSettings settings, IDialogService dialogs, Action save, Action<ToastRequest> toast, Action openPage)
    {
        _settings = settings;
        _dialogs = dialogs;
        _save = save;
        _toast = toast;
        _openPage = openPage;
        _daily.Tick += (_, _) => _ = ScanAsync(remind: true);
        _daily.Start();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = ScanAsync(remind: false); };
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanText))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanText))]
    private DateTime? _lastScan;

    public string ScanText => IsScanning ? "Checking your installers…" : LastScan is { } t ? $"Checked {Format.Ago(t)}" : "Not checked yet";

    public bool HasItems => Items.Count > 0;

    public long TotalBytes => Items.Sum(i => i.Match.Installer.SizeBytes);

    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.Match.Installer.SizeBytes);

    public int SelectedCount => Items.Count(i => i.IsSelected);

    /// <summary>Headline for the Cleanup page and the Overview banner.</summary>
    public string Summary => Items.Count switch
    {
        0 => "No leftover installers",
        1 => $"1 installer is for an app you already have · {Format.Size(TotalBytes)}",
        var n => $"{n} installers are for apps you already have · {Format.Size(TotalBytes)}",
    };

    public string RecycleText => SelectedCount == 0
        ? "Move to Recycle Bin"
        : $"Move {SelectedCount} to Recycle Bin · {Format.Size(SelectedBytes)}";

    public bool Reminders
    {
        get => _settings.CleanupReminders;
        set
        {
            _settings.CleanupReminders = value;
            OnPropertyChanged();
            _save();
        }
    }

    /// <summary>The folders holding installers: the watched folder and every nest that collects .exe/.msi.</summary>
    private IEnumerable<string> Folders()
    {
        yield return _settings.WatchFolder;
        foreach (var rule in _settings.Rules.Where(r => r.Extensions.Intersect(InstallerMatcher.InstallerExtensions).Any()))
            yield return Path.Combine(_settings.EffectiveDestinationRoot, rule.EffectiveFolderName);
    }

    /// <summary>Re-scan a few seconds after an installer is sorted (it may have just been run).</summary>
    public void InstallerArrived()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private bool CanScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task Scan() => ScanAsync(remind: false);

    public async Task ScanAsync(bool remind)
    {
        if (IsScanning)
            return;
        IsScanning = true;
        try
        {
            var folders = Folders().ToList();
            var kept = _settings.KeptInstallers.ToList();
            var matches = await Task.Run(() => InstallerScanner.Scan(folders, kept));
            Items.Clear();
            foreach (var match in matches.OrderByDescending(m => m.Installer.SizeBytes))
                Items.Add(new CleanupItemViewModel(match, SelectionChanged));
            LastScan = DateTime.Now;
            SelectionChanged();
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(Summary));

            if (remind)
                MaybeRemind();
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void MaybeRemind()
    {
        if (!_settings.CleanupReminders || Items.Count == 0)
            return;
        if (_settings.LastCleanupReminder is { } last && DateTime.Now - last < ReminderEvery)
            return;
        _settings.LastCleanupReminder = DateTime.Now;
        _save();
        var what = Items.Count == 1 ? "1 installer is" : $"{Items.Count} installers are";
        _toast(new ToastRequest($"{what} for apps you already have. Free up {Format.Size(TotalBytes)}.", "Review", _openPage));
    }

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(RecycleText));
        RecycleSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        var select = Items.Any(i => !i.IsSelected);
        foreach (var item in Items)
            item.IsSelected = select;
    }

    private bool CanRecycle() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanRecycle))]
    private void RecycleSelected()
    {
        var chosen = Items.Where(i => i.IsSelected).ToList();
        var noun = chosen.Count == 1 ? "installer" : "installers";
        if (!_dialogs.Confirm($"Move {chosen.Count} {noun} to the Recycle Bin?",
                $"This frees up {Format.Size(chosen.Sum(i => i.Match.Installer.SizeBytes))}. The apps stay installed, and you can restore the files from the Recycle Bin.",
                "Move to Recycle Bin"))
            return;

        var moved = 0;
        long freed = 0;
        var failed = new List<string>();
        foreach (var item in chosen)
        {
            try
            {
                if (File.Exists(item.FilePath))
                    FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                Items.Remove(item);
                moved++;
                freed += item.Match.Installer.SizeBytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                failed.Add(item.FileName);
            }
        }

        SelectionChanged();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
        if (moved > 0)
            UsageStats.Track("installers_recycled", new() { ["count"] = moved, ["mb"] = (int)(freed / 1_048_576) });
        if (moved > 0)
            _toast(new ToastRequest($"Moved {moved} {(moved == 1 ? "installer" : "installers")} to the Recycle Bin · {Format.Size(freed)} freed"));
        if (failed.Count > 0)
            _dialogs.Info("Some installers weren't moved", $"These are in use or protected: {string.Join(", ", failed)}");
    }

    /// <summary>Keep this installer and stop suggesting it.</summary>
    [RelayCommand]
    private void Keep(CleanupItemViewModel item)
    {
        _settings.KeptInstallers.Add(item.FilePath);
        _save();
        Items.Remove(item);
        SelectionChanged();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void ShowInFolder(CleanupItemViewModel item) => WindowsIntegration.ShowInExplorer(item.FilePath);

    /// <summary>Called when the theme changes, so item brushes are rebuilt.</summary>
    public void RefreshTheme()
    {
        var items = Items.Select(i => (i.Match, i.IsSelected)).ToList();
        Items.Clear();
        foreach (var (match, selected) in items)
            Items.Add(new CleanupItemViewModel(match, SelectionChanged) { IsSelected = selected });
    }
}
