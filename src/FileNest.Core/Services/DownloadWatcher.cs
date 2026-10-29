using System.Collections.Concurrent;
using FileNest.Core.Models;

namespace FileNest.Core.Services;

/// <summary>A download that's still being written, shown as "Waiting" in the UI.</summary>
public sealed record InProgressDownload(string TempPath, string Name, long SizeBytes);

/// <summary>
/// Watches the top level of a folder and sorts each new file once it has finished downloading:
/// it waits until the file has been quiet for a moment, has no browser temp file next to it, and
/// can be opened exclusively.
/// </summary>
public sealed class DownloadWatcher : IDisposable
{
    private sealed class Pending(DateTime seen)
    {
        public DateTime FirstSeen { get; } = seen;
        public DateTime LastSeen { get; set; } = seen;
    }

    private readonly Func<AppSettings> _settings;
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _suppressed = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sortLock = new(1, 1);
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private DateTime _startedAt;
    private IReadOnlyList<InProgressDownload> _downloads = [];
    private int _ticking;

    /// <summary>How long a file must stay unchanged before it's sorted.</summary>
    public TimeSpan SettleTime { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Give up on a file that's still locked after this long.</summary>
    public TimeSpan GiveUpAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Temp files untouched for this long are treated as abandoned downloads.</summary>
    public TimeSpan AbandonedAfter { get; set; } = TimeSpan.FromHours(1);

    public string? Folder { get; private set; }

    public bool IsPaused { get; private set; }

    public IReadOnlyList<InProgressDownload> Downloads => _downloads;

    /// <summary>Raised for every file moved. <c>bulk</c> is true for files moved by <see cref="OrganizeNowAsync"/>.</summary>
    public event Action<ActivityEntry, bool>? FileSorted;

    public event Action<ActivityEntry>? FileFailed;

    public event Action<IReadOnlyList<InProgressDownload>>? DownloadsChanged;

    public DownloadWatcher(Func<AppSettings> settings, bool autoTick = true)
    {
        _settings = settings;
        _timer = new Timer(_ => ProcessPending());
        if (autoTick)
            _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Start(string folder)
    {
        StopWatcher();
        Folder = folder;
        _startedAt = DateTime.Now;
        _pending.Clear();
        if (!Directory.Exists(folder))
            return;

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += (s, e) => Track(e.FullPath);
        _watcher.Renamed += (s, e) => { _pending.TryRemove(e.OldFullPath, out _); Track(e.FullPath); };
        _watcher.Changed += (s, e) => Touch(e.FullPath);
        _watcher.Deleted += (s, e) => _pending.TryRemove(e.FullPath, out _);
        _watcher.Error += (s, e) => RescanRecent();
        _watcher.EnableRaisingEvents = !IsPaused;
    }

    public void Pause()
    {
        IsPaused = true;
        _pending.Clear();
        if (_watcher is not null)
            _watcher.EnableRaisingEvents = false;
    }

    public void Resume()
    {
        IsPaused = false;
        if (_watcher is not null)
            _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Never sort this path again this session (used after Undo so the file stays put).</summary>
    public void Suppress(string path) => _suppressed[path] = 0;

    /// <summary>Queue a path as if the watcher had seen it appear.</summary>
    public void Track(string path)
    {
        if (IsPaused || _suppressed.ContainsKey(path))
            return;
        var now = DateTime.Now;
        _pending.AddOrUpdate(path, _ => new Pending(now), (_, p) => { p.LastSeen = now; return p; });
    }

    private void Touch(string path)
    {
        // Only files we're already waiting on: a "Changed" on an old file the user is editing shouldn't sort it.
        if (_pending.TryGetValue(path, out var p))
            p.LastSeen = DateTime.Now;
    }

    private void RescanRecent()
    {
        // The event buffer overflowed, so some new files may have been missed.
        if (Folder is null || !Directory.Exists(Folder))
            return;
        foreach (var file in SafeEnumerate(Folder))
        {
            if (File.GetCreationTime(file) >= _startedAt)
                Track(file);
        }
    }

    /// <summary>One pass over the queue. Called every second by the timer; public for tests.</summary>
    public void ProcessPending()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
            return;
        try
        {
            UpdateDownloads();
            if (IsPaused || _pending.IsEmpty)
                return;

            var now = DateTime.Now;
            foreach (var (path, pending) in _pending)
            {
                if (!File.Exists(path) || _suppressed.ContainsKey(path) || FileSorter.ShouldIgnore(path))
                {
                    _pending.TryRemove(path, out _);
                    continue;
                }

                if (now - pending.LastSeen < SettleTime)
                    continue;

                if (FileSorter.HasCompanionTemporary(path))
                {
                    pending.LastSeen = now;
                    continue;
                }

                if (!IsUnlocked(path))
                {
                    if (now - pending.FirstSeen > GiveUpAfter)
                    {
                        _pending.TryRemove(path, out _);
                        ReportFailure(path, "The file stayed in use by another app.");
                    }
                    continue;
                }

                TrySort(path, pending, bulk: false);
            }
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private void TrySort(string path, Pending? pending, bool bulk)
    {
        _sortLock.Wait();
        try
        {
            var entry = FileSorter.Sort(path, _settings());
            _pending.TryRemove(path, out _);
            if (entry is not null)
                FileSorted?.Invoke(entry, bulk);
        }
        catch (Exception ex) when (ex is IOException)
        {
            // Probably locked again between the check and the move; try on the next pass.
            if (pending is null || DateTime.Now - pending.FirstSeen > GiveUpAfter)
            {
                _pending.TryRemove(path, out _);
                if (pending is not null)
                    ReportFailure(path, ex.Message);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
            _pending.TryRemove(path, out _);
            ReportFailure(path, ex.Message);
        }
        finally
        {
            _sortLock.Release();
        }
    }

    /// <summary>
    /// Sorts every finished file already in the folder. Locked files and in-progress downloads are skipped.
    /// Returns how many files were moved.
    /// </summary>
    public Task<int> OrganizeNowAsync() => Task.Run(() =>
    {
        var folder = Folder;
        if (folder is null || !Directory.Exists(folder))
            return 0;

        var moved = 0;
        void Count(ActivityEntry _, bool bulk) { if (bulk) Interlocked.Increment(ref moved); }
        FileSorted += Count;
        try
        {
            foreach (var file in SafeEnumerate(folder))
            {
                if (_suppressed.ContainsKey(file) || FileSorter.ShouldIgnore(file) || FileSorter.HasCompanionTemporary(file))
                    continue;
                if (!IsUnlocked(file))
                    continue;
                TrySort(file, null, bulk: true);
            }
        }
        finally
        {
            FileSorted -= Count;
        }

        return moved;
    });

    private void UpdateDownloads()
    {
        var folder = Folder;
        IReadOnlyList<InProgressDownload> current = [];
        if (folder is not null && Directory.Exists(folder))
        {
            var cutoff = DateTime.Now - AbandonedAfter;
            current = SafeEnumerate(folder)
                .Where(FileSorter.IsTemporaryDownload)
                .Select(f => new FileInfo(f))
                .Where(f => f.Exists && f.LastWriteTime >= cutoff)
                .Select(f => new InProgressDownload(f.FullName, FileSorter.FinalNameOfTemporary(f.Name), f.Length))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!current.SequenceEqual(_downloads))
        {
            _downloads = current;
            DownloadsChanged?.Invoke(current);
        }
    }

    private void ReportFailure(string path, string reason)
    {
        long size = 0;
        try { size = new FileInfo(path).Length; } catch (IOException) { }

        FileFailed?.Invoke(new ActivityEntry
        {
            FileName = Path.GetFileName(path),
            SourcePath = path,
            NestName = "",
            Status = ActivityStatus.Failed,
            SizeBytes = size,
            Error = reason,
        });
    }

    private static bool IsUnlocked(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerate(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void StopWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        _timer.Dispose();
        StopWatcher();
        _sortLock.Dispose();
    }
}
