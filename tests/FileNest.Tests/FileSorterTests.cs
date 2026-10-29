using FileNest.Core.Models;
using FileNest.Core.Services;

namespace FileNest.Tests;

public class FileSorterTests
{
    private static AppSettings SettingsFor(string folder) => new() { WatchFolder = folder };

    [Theory]
    [InlineData("invoice.pdf", "PDFs")]
    [InlineData("INVOICE.PDF", "PDFs")]
    [InlineData("shot.png", "Images")]
    [InlineData("setup.exe", "Installers")]
    [InlineData("backup.tar.gz", "Archives")]
    [InlineData("notes.docx", "Documents")]
    [InlineData("song.mp3", "Media")]
    public void FindRule_matches_default_nests(string fileName, string expectedNest)
    {
        Assert.Equal(expectedNest, FileSorter.FindRule(fileName, DefaultRules.Create())?.Name);
    }

    [Fact]
    public void FindRule_prefers_the_longest_extension()
    {
        var rules = new List<NestRule>
        {
            new() { Name = "Gz", Extensions = [".gz"] },
            new() { Name = "Tarballs", Extensions = [".tar.gz"] },
        };

        Assert.Equal("Tarballs", FileSorter.FindRule("x.tar.gz", rules)?.Name);
        Assert.Equal("Gz", FileSorter.FindRule("x.gz", rules)?.Name);
    }

    [Fact]
    public void FindRule_skips_disabled_rules_and_bare_extensions()
    {
        var rules = new List<NestRule> { new() { Name = "PDFs", Extensions = [".pdf"], Enabled = false } };

        Assert.Null(FileSorter.FindRule("a.pdf", rules));
        Assert.Null(FileSorter.FindRule(".pdf", DefaultRules.Create()));
    }

    [Theory]
    [InlineData("movie.mkv.crdownload", true)]
    [InlineData("movie.mkv.part", true)]
    [InlineData("~$report.docx", true)]
    [InlineData("desktop.ini", true)]
    [InlineData(".hidden", true)]
    [InlineData("report.pdf", false)]
    public void ShouldIgnore_skips_temporary_and_system_files(string name, bool ignored)
    {
        using var temp = new TempFolder();
        Assert.Equal(ignored, FileSorter.ShouldIgnore(temp.File(name)));
    }

    [Fact]
    public void FinalNameOfTemporary_strips_the_browser_extension()
    {
        Assert.Equal("movie.mkv", FileSorter.FinalNameOfTemporary(@"C:\x\movie.mkv.crdownload"));
        Assert.Equal("plain.txt", FileSorter.FinalNameOfTemporary("plain.txt"));
    }

    [Fact]
    public void UniquePath_adds_a_counter_when_the_name_is_taken()
    {
        using var temp = new TempFolder();
        temp.File("report.pdf");
        temp.File("report (1).pdf");

        Assert.Equal(Path.Combine(temp.Path, "report (2).pdf"), FileSorter.UniquePath(temp.Path, "report.pdf"));
        Assert.Equal(Path.Combine(temp.Path, "other.pdf"), FileSorter.UniquePath(temp.Path, "other.pdf"));
    }

    [Fact]
    public void Sort_moves_the_file_into_its_nest()
    {
        using var temp = new TempFolder();
        var file = temp.File("invoice.pdf", "12345");

        var entry = FileSorter.Sort(file, SettingsFor(temp.Path));

        Assert.NotNull(entry);
        Assert.Equal(ActivityStatus.Sorted, entry.Status);
        Assert.Equal("PDFs", entry.NestName);
        Assert.Equal(5, entry.SizeBytes);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(temp.Path, "PDFs", "invoice.pdf")));
    }

    [Fact]
    public void Sort_renames_when_the_nest_already_has_that_name()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Path, "PDFs"));
        File.WriteAllText(Path.Combine(temp.Path, "PDFs", "report.pdf"), "old");
        var file = temp.File("report.pdf", "new");

        var entry = FileSorter.Sort(file, SettingsFor(temp.Path));

        Assert.Equal(ActivityStatus.Renamed, entry!.Status);
        Assert.Equal("report (1).pdf", entry.FileName);
        Assert.Equal("old", File.ReadAllText(Path.Combine(temp.Path, "PDFs", "report.pdf")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(temp.Path, "PDFs", "report (1).pdf")));
    }

    [Fact]
    public void Sort_sends_unmatched_files_to_others_or_leaves_them()
    {
        using var temp = new TempFolder();
        var settings = SettingsFor(temp.Path);

        var first = temp.File("data.xyz");
        Assert.Equal("Others", FileSorter.Sort(first, settings)!.NestName);
        Assert.True(File.Exists(Path.Combine(temp.Path, "Others", "data.xyz")));

        settings.SortUnmatched = false;
        var second = temp.File("more.xyz");
        Assert.Null(FileSorter.Sort(second, settings));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void Sort_uses_a_separate_destination_root_when_set()
    {
        using var watch = new TempFolder();
        using var dest = new TempFolder();
        var file = watch.File("pic.jpg");

        FileSorter.Sort(file, new AppSettings { WatchFolder = watch.Path, DestinationRoot = dest.Path });

        Assert.True(File.Exists(Path.Combine(dest.Path, "Images", "pic.jpg")));
    }

    [Fact]
    public void Undo_moves_the_file_back()
    {
        using var temp = new TempFolder();
        var file = temp.File("invoice.pdf");
        var entry = FileSorter.Sort(file, SettingsFor(temp.Path))!;

        var restored = FileSorter.Undo(entry);

        Assert.Equal(file, restored);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(entry.DestinationPath));
    }

    [Fact]
    public void ParseExtensions_normalises_free_text()
    {
        Assert.Equal([".png", ".jpg", ".tar.gz"], NestRule.ParseExtensions("png, .JPG  tar.gz; png"));
    }
}
