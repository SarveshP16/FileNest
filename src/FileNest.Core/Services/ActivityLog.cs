using FileNest.Core.Models;

namespace FileNest.Core.Services;

public sealed record NestTally(string NestName, string Color, int Count);

public sealed record TodayStats(int Count, long Bytes, IReadOnlyList<NestTally> ByNest);

/// <summary>
/// Sorting history plus the all-time counter, saved to activity.json. Thread-safe.
/// </summary>
public sealed class ActivityLog
{
    public const int MaxEntries = 1000;
    private const string FileName = "activity.json";

    private readonly object _gate = new();
    private readonly Timer _saveTimer;
    private readonly bool _persist;
    private Data _data;

    private sealed class Data
    {
        public long TotalSorted { get; set; }
        public List<ActivityEntry> Entries { get; set; } = [];
    }

    public event Action? Changed;

    public ActivityLog(bool persist = true)
    {
        _persist = persist;
        _data = persist ? JsonStore.Load(FileName, () => new Data()) : new Data();
        _saveTimer = new Timer(_ => Flush());
    }

    public long TotalSorted
    {
        get { lock (_gate) return _data.TotalSorted; }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (_gate) return [.. _data.Entries];
    }

    public void Add(ActivityEntry entry)
    {
        lock (_gate)
        {
            _data.Entries.Insert(0, entry);
            if (entry.CountsAsSorted)
                _data.TotalSorted++;
            if (_data.Entries.Count > MaxEntries)
                _data.Entries.RemoveRange(MaxEntries, _data.Entries.Count - MaxEntries);
        }

        ScheduleSave();
    }

    /// <summary>Call after changing an entry in place (for example after Undo).</summary>
    public void Updated() => ScheduleSave();

    public void Clear()
    {
        lock (_gate) _data.Entries.Clear();
        ScheduleSave();
    }

    public TodayStats Today()
    {
        var today = DateTime.Today;
        lock (_gate)
        {
            var sorted = _data.Entries.Where(e => e.Time >= today && e.CountsAsSorted).ToList();
            var byNest = sorted
                .GroupBy(e => e.NestName)
                .Select(g => new NestTally(g.Key, g.First().Color, g.Count()))
                .OrderByDescending(t => t.Count)
                .ToList();
            return new TodayStats(sorted.Count, sorted.Sum(e => e.SizeBytes), byNest);
        }
    }

    public void Flush()
    {
        if (!_persist)
            return;
        lock (_gate)
        {
            try
            {
                JsonStore.Save(FileName, _data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try again on the next change.
            }
        }
    }

    private void ScheduleSave()
    {
        // Batch writes: a bulk "Organize now" can add hundreds of entries in a second.
        _saveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        Changed?.Invoke();
    }
}
