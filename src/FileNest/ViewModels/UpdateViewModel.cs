using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileNest.Core.Models;
using FileNest.Services;
using FileNest.Ui;
using Velopack;

namespace FileNest.ViewModels;

/// <summary>
/// In-app updates: checks GitHub Releases shortly after start and every 6 hours, downloads in the background
/// (when automatic updates are on) and offers a restart. Quitting from the tray installs a downloaded update.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    public const string NotInstalled = "NotInstalled";
    public const string Idle = "Idle";
    public const string Checking = "Checking";
    public const string UpToDate = "UpToDate";
    public const string Available = "Available";
    public const string Downloading = "Downloading";
    public const string Ready = "Ready";
    public const string Failed = "Failed";

    private readonly AppSettings _settings;
    private readonly AppUpdater _updater;
    private readonly Action _save;
    private readonly Action<ToastRequest> _toast;
    private readonly Action _prepareForExit;
    private readonly DispatcherTimer _timer = new();
    private UpdateInfo? _update;
    private VelopackAsset? _downloaded;

    public UpdateViewModel(AppSettings settings, AppUpdater updater, Action save, Action<ToastRequest> toast, Action prepareForExit)
    {
        _settings = settings;
        _updater = updater;
        _save = save;
        _toast = toast;
        _prepareForExit = prepareForExit;
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(6);
            _ = CheckAsync(automatic: true);
        };
    }

    public string CurrentVersion => _updater.CurrentVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DetailText), nameof(ShowBadge), nameof(BadgeText), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand), nameof(RestartCommand))]
    private string _state = Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _progress;

    [ObservableProperty]
    private string _newVersion = "";

    [ObservableProperty]
    private string _releaseNotes = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    private string _error = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    private DateTime? _lastChecked;

    public bool IsBusy => State is Checking or Downloading;

    public string StatusText => State switch
    {
        NotInstalled => "Updates work in the installed copy of FileNest",
        Checking => "Checking for updates…",
        UpToDate => "You're up to date",
        Available => $"Version {NewVersion} is available",
        Downloading => $"Downloading version {NewVersion}… {Progress}%",
        Ready => $"Version {NewVersion} is ready",
        Failed => "Couldn't check for updates",
        _ => "Check for the latest version",
    };

    public string DetailText => State switch
    {
        NotInstalled => "Install FileNest with Setup.exe to get updates.",
        Ready => "Restart FileNest to finish. It also installs when you quit from the tray.",
        Failed => Error,
        _ => LastChecked is { } t ? $"Last checked {Format.Ago(t)}" : "",
    };

    public bool ShowBadge => State is Available or Ready;

    public string BadgeText => State == Ready ? "Restart to update" : $"Update available · v{NewVersion}";

    public bool AutoUpdate
    {
        get => _settings.AutoUpdate;
        set
        {
            _settings.AutoUpdate = value;
            OnPropertyChanged();
            _save();
        }
    }

    /// <summary>Starts the background schedule: first check 30 seconds after launch, then every 6 hours.</summary>
    public void Start()
    {
        if (!_updater.IsInstalled)
        {
            State = NotInstalled;
            return;
        }

        if (_updater.PendingRestart is { } pending)
        {
            _downloaded = pending;
            NewVersion = pending.Version.ToString();
            State = Ready;
        }

        _timer.Interval = TimeSpan.FromSeconds(30);
        _timer.Start();
    }

    private bool CanCheck() => State is not (NotInstalled or Checking or Downloading or Ready);

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task Check() => CheckAsync(automatic: false);

    private async Task CheckAsync(bool automatic)
    {
        if (!CanCheck())
            return;
        State = Checking;
        try
        {
            _update = await _updater.CheckAsync();
            LastChecked = DateTime.Now;
            if (_update is null)
            {
                State = UpToDate;
                return;
            }

            NewVersion = _update.TargetFullRelease.Version.ToString();
            ReleaseNotes = _update.TargetFullRelease.NotesMarkdown?.Trim() ?? "";
            State = Available;
            if (_settings.AutoUpdate)
                await DownloadAsync(announce: true);
            else if (automatic)
                _toast(new ToastRequest($"FileNest {NewVersion} is available", "Update", () => _ = DownloadAsync(announce: true)));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            LastChecked = DateTime.Now;
            Error = Explain(ex);
            State = Failed;
        }
    }

    private bool CanDownload() => State == Available;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task Download() => DownloadAsync(announce: false);

    private async Task DownloadAsync(bool announce)
    {
        if (_update is null || State == Downloading)
            return;
        State = Downloading;
        Progress = 0;
        try
        {
            await _updater.DownloadAsync(_update, p => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Progress = p));
            _downloaded = _update.TargetFullRelease;
            State = Ready;
            UsageStats.Track("update_downloaded", new() { ["to"] = NewVersion });
            if (announce)
                _toast(new ToastRequest($"FileNest {NewVersion} is ready", "Restart", RestartNow));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            Error = Explain(ex);
            State = Failed;
        }
    }

    private bool CanRestart() => State == Ready;

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private void Restart() => RestartNow();

    private void RestartNow()
    {
        if (_downloaded is null)
            return;
        _prepareForExit();
        _updater.RestartNow(_downloaded);
    }

    /// <summary>Called when FileNest quits: a downloaded update gets installed quietly after exit.</summary>
    public void OnQuit()
    {
        if (_downloaded is not null && State == Ready)
            _updater.ApplyOnExit(_downloaded);
    }

    /// <summary>Clicking the sidebar badge: restart if ready, otherwise go see the details.</summary>
    public void BadgeClicked(Action openSettings)
    {
        if (State == Ready)
            RestartNow();
        else
            openSettings();
    }

    private static string Explain(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "No releases have been published yet.",
        HttpRequestException => "The update server couldn't be reached. Check your internet connection.",
        TaskCanceledException => "The update server took too long to answer.",
        _ => ex.Message.Split('\n')[0],
    };
}
