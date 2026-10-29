using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileNest.Core.Models;
using FileNest.Core.Services;
using FileNest.Services;
using FileNest.Ui;
using FileNest.ViewModels;
using FileNest.Views;

namespace FileNest;

public partial class App : Application
{
    private const string InstanceName = "FileNest.SingleInstance";
    private const string ShowEventName = "FileNest.ShowWindow";

    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private AppSettings _settings = null!;
    private ActivityLog _log = null!;
    private DownloadWatcher _watcher = null!;
    private MainViewModel _vm = null!;
    private MainWindow _window = null!;
    private TrayIcon? _tray;
    private ToastWindow? _toast;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        // One FileNest per user: a second launch just brings the first one's window up.
        _instance = new Mutex(true, $"{InstanceName}.{Environment.UserName}", out var first);
        if (!first)
        {
            try
            {
                EventWaitHandle.OpenExisting($"{ShowEventName}.{Environment.UserName}").Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // For trying FileNest out without touching your real settings: FILENEST_DATA=<folder>.
        if (Environment.GetEnvironmentVariable("FILENEST_DATA") is { Length: > 0 } dataFolder)
            JsonStore.DataFolder = dataFolder;

        _settings = JsonStore.Load(MainViewModel.SettingsFile, () => new AppSettings());
        if (string.IsNullOrWhiteSpace(_settings.WatchFolder))
            _settings.WatchFolder = KnownFolders.Downloads;
        ThemeManager.Initialize(_settings);

        _log = new ActivityLog();
        _watcher = new DownloadWatcher(() => _settings);
        _vm = new MainViewModel(_settings, _log, _watcher, new DialogService(() => _window), new AppUpdater(), PrepareForExit);

        _watcher.FileSorted += (entry, bulk) => Dispatcher.BeginInvoke(() => _vm.OnFileSorted(entry, bulk));
        _watcher.FileFailed += entry => Dispatcher.BeginInvoke(() => _vm.OnFileFailed(entry));
        _watcher.DownloadsChanged += list => Dispatcher.BeginInvoke(() => _vm.OnDownloadsChanged(list));
        _watcher.Start(_settings.WatchFolder);

        _vm.ToastRequested += ShowToast;
        _vm.PausedChanged += paused => _tray?.SetPaused(paused);
        _vm.ShowWindowRequested += () => _window.ShowAndActivate();

        _window = new MainWindow(_vm);
        _window.HiddenToTray += () =>
        {
            if (_trayHintShown)
                return;
            _trayHintShown = true;
            ShowToast(new ToastRequest("Still sorting in the background. Find FileNest in the tray."));
        };

        _tray = new TrayIcon();
        _tray.OpenRequested += () => _window.ShowAndActivate();
        _tray.OrganizeRequested += () =>
        {
            _window.ShowAndActivate();
            _vm.OrganizeNowCommand.Execute(null);
        };
        _tray.PauseToggleRequested += () => _vm.TogglePauseCommand.Execute(null);
        _tray.OpenFolderRequested += () => WindowsIntegration.OpenFolder(_settings.WatchFolder);
        _tray.QuitRequested += Quit;

        // Keep the startup entry pointing at this exe in case it was moved.
        if (_settings.StartWithWindows)
            TryRun(() => WindowsIntegration.SetStartWithWindows(true));

        ListenForSecondLaunch();
        _vm.Updates.Start();
        StartCleanupChecks();

        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            _window.Show();
    }

    private void ListenForSecondLaunch()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"{ShowEventName}.{Environment.UserName}");
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _window.ShowAndActivate());
        })
        {
            IsBackground = true,
            Name = "FileNest second-launch listener",
        };
        thread.Start();
    }

    private void ShowToast(ToastRequest request)
    {
        if (_toast is null)
        {
            _toast = new ToastWindow();
            _toast.BodyClicked += () => _window.ShowAndActivate();
        }

        _toast.ShowMessage(request.Message, request.ActionText, request.Action);
    }

    private void StartCleanupChecks()
    {
        // First installer check a little after startup, so launching at sign-in stays light.
        var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        once.Tick += (_, _) =>
        {
            once.Stop();
            _ = _vm.Cleanup.ScanAsync(remind: true);
        };
        once.Start();
    }

    /// <summary>Saves state and removes the tray icon before the process ends (quit or update restart).</summary>
    private void PrepareForExit()
    {
        _log.Flush();
        _watcher.Dispose();
        _tray?.Dispose();
        _tray = null;
        _toast?.Close();
    }

    public void Quit()
    {
        PrepareForExit();
        _vm.Updates.OnQuit();
        _window.AllowClose = true;
        _window.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _log?.Flush();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log and keep running: a background sorter shouldn't vanish from the tray over one bad file.
        TryRun(() =>
        {
            Directory.CreateDirectory(JsonStore.DataFolder);
            File.AppendAllText(Path.Combine(JsonStore.DataFolder, "errors.log"), $"[{DateTime.Now:u}] {e.Exception}\n\n");
        });
        e.Handled = true;
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
