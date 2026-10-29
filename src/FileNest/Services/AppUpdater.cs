using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace FileNest.Services;

/// <summary>
/// Thin wrapper over Velopack's UpdateManager. Releases are read from GitHub Releases on <see cref="RepoUrl"/>;
/// build-installer.ps1 -Publish uploads them there. Only works in an installed copy (Setup.exe), not dev builds.
/// </summary>
public sealed class AppUpdater
{
    /// <summary>Public GitHub repo whose Releases hold the installer packages.</summary>
    public const string RepoUrl = "https://github.com/SarveshP16/FileNest";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false));

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString() ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>An update that's downloaded and waiting for a restart, if any.</summary>
    public VelopackAsset? PendingRestart => _manager.UpdatePendingRestart;

    public Task<UpdateInfo?> CheckAsync() => _manager.CheckForUpdatesAsync();

    public Task DownloadAsync(UpdateInfo update, Action<int> progress) => _manager.DownloadUpdatesAsync(update, progress);

    /// <summary>Closes FileNest, installs the update and starts the new version.</summary>
    public void RestartNow(VelopackAsset update) => _manager.ApplyUpdatesAndRestart(update);

    /// <summary>Installs the update quietly once FileNest exits (used when quitting from the tray).</summary>
    public void ApplyOnExit(VelopackAsset update) => _manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
}
