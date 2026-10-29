using FileNest.Core.Models;
using FileNest.Core.Services;

namespace FileNest.Tests;

public class DownloadWatcherTests
{
    private static DownloadWatcher Create(string folder, List<ActivityEntry> sorted)
    {
        var settings = new AppSettings { WatchFolder = folder };
        var watcher = new DownloadWatcher(() => settings, autoTick: false) { SettleTime = TimeSpan.Zero };
        watcher.FileSorted += (e, _) => sorted.Add(e);
        watcher.Start(folder);
        return watcher;
    }

    [Fact]
    public void Sorts_a_tracked_file_once_it_is_settled_and_unlocked()
    {
        using var temp = new TempFolder();
        var sorted = new List<ActivityEntry>();
        using var watcher = Create(temp.Path, sorted);
        var file = temp.File("a.pdf");

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            watcher.Track(file);
            watcher.ProcessPending();
            Assert.Empty(sorted); // still open by "the browser"
        }

        watcher.ProcessPending();
        Assert.Single(sorted);
        Assert.True(File.Exists(Path.Combine(temp.Path, "PDFs", "a.pdf")));
    }

    [Fact]
    public void Waits_while_a_companion_temp_file_exists()
    {
        using var temp = new TempFolder();
        var sorted = new List<ActivityEntry>();
        using var watcher = Create(temp.Path, sorted);
        var placeholder = temp.File("big.zip", "");
        var part = temp.File("big.zip.part");

        watcher.Track(placeholder);
        watcher.ProcessPending();
        Assert.Empty(sorted);
        Assert.Contains(watcher.Downloads, d => d.Name == "big.zip");

        File.Delete(part);
        watcher.ProcessPending();
        Assert.Single(sorted);
    }

    [Fact]
    public void Paused_watcher_ignores_new_files()
    {
        using var temp = new TempFolder();
        var sorted = new List<ActivityEntry>();
        using var watcher = Create(temp.Path, sorted);
        watcher.Pause();

        watcher.Track(temp.File("a.png"));
        watcher.ProcessPending();

        Assert.Empty(sorted);
    }

    [Fact]
    public void Suppressed_paths_are_left_alone()
    {
        using var temp = new TempFolder();
        var sorted = new List<ActivityEntry>();
        using var watcher = Create(temp.Path, sorted);
        var file = temp.File("keep.pdf");

        watcher.Suppress(file);
        watcher.Track(file);
        watcher.ProcessPending();

        Assert.Empty(sorted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task OrganizeNow_sorts_existing_files_but_skips_downloads_in_progress()
    {
        using var temp = new TempFolder();
        var sorted = new List<ActivityEntry>();
        using var watcher = Create(temp.Path, sorted);
        temp.File("a.pdf");
        temp.File("b.jpg");
        temp.File("c.mkv.crdownload");
        Directory.CreateDirectory(Path.Combine(temp.Path, "SomeFolder"));

        var moved = await watcher.OrganizeNowAsync();

        Assert.Equal(2, moved);
        Assert.True(File.Exists(Path.Combine(temp.Path, "c.mkv.crdownload")));
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "SomeFolder")));
    }

    [Fact]
    public async Task Sorts_files_created_while_watching()
    {
        using var temp = new TempFolder();
        var settings = new AppSettings { WatchFolder = temp.Path };
        var done = new TaskCompletionSource<ActivityEntry>();
        using var watcher = new DownloadWatcher(() => settings) { SettleTime = TimeSpan.FromMilliseconds(200) };
        watcher.FileSorted += (e, _) => done.TrySetResult(e);
        watcher.Start(temp.Path);

        temp.File("live.png");

        var entry = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Images", entry.NestName);
    }
}
