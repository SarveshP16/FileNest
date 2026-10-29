using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FileNest.Core.Models;
using FileNest.Core.Services;
using FileNest.Ui;

namespace FileNest.ViewModels;

/// <summary>Theme brushes captured when an item is created. Lists are rebuilt when the theme changes.</summary>
internal static class Res
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

public sealed partial class NestCardViewModel : ObservableObject
{
    public NestCardViewModel(NestRule rule, string folderPath)
    {
        Rule = rule;
        FolderPath = folderPath;
        Glyph = Icons.ForKey(rule.Icon);
        IconBackground = ThemeManager.NestBackground(rule.Color);
        IconForeground = ThemeManager.NestForeground(rule.Color);
        var shown = rule.Extensions.Take(3);
        var more = rule.Extensions.Count - 3;
        ExtensionSummary = string.Join(" ", shown) + (more > 0 ? $" +{more}" : "");
    }

    public NestRule Rule { get; }

    public string Name => Rule.Name;

    public string Glyph { get; }

    public Brush IconBackground { get; }

    public Brush IconForeground { get; }

    public string ExtensionSummary { get; }

    public IReadOnlyList<string> Extensions => Rule.Extensions;

    public string FolderPath { get; }

    /// <summary>Short form for lists, e.g. "Downloads\PDFs".</summary>
    public string FolderDisplay =>
        System.IO.Path.Combine(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(FolderPath)) ?? "", Rule.EffectiveFolderName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int? _count;

    public string CountText => Count is { } c ? Format.Count(c) : "–";

    /// <summary>Set by the owner; called when the Enabled switch flips.</summary>
    public Action<NestCardViewModel>? EnabledChanged { get; init; }

    public bool Enabled
    {
        get => Rule.Enabled;
        set
        {
            if (Rule.Enabled == value)
                return;
            Rule.Enabled = value;
            OnPropertyChanged();
            EnabledChanged?.Invoke(this);
        }
    }
}

public sealed partial class ActivityItemViewModel : ObservableObject
{
    public ActivityItemViewModel(ActivityEntry entry)
    {
        Entry = entry;
        var failed = entry.Status == ActivityStatus.Failed;
        Glyph = Icons.ForKey(failed ? "failed" : entry.Icon);
        IconBackground = failed ? Res.Brush("DangerBgBrush") : ThemeManager.NestBackground(entry.Color);
        IconForeground = failed ? Res.Brush("DangerTextBrush") : ThemeManager.NestForeground(entry.Color);
        (StatusText, PillBackground, PillForeground) = entry.Status switch
        {
            ActivityStatus.Sorted => ("Sorted", Res.Brush("SuccessBgBrush"), Res.Brush("SuccessTextBrush")),
            ActivityStatus.Renamed => ("Renamed", Res.Brush("WarningBgBrush"), Res.Brush("WarningTextBrush")),
            ActivityStatus.Undone => ("Undone", Res.Brush("Surface1Brush"), Res.Brush("TextSecondaryBrush")),
            _ => ("Failed", Res.Brush("DangerBgBrush"), Res.Brush("DangerTextBrush")),
        };
    }

    public ActivityEntry Entry { get; }

    public string FileName => Entry.FileName;

    public string Glyph { get; }

    public Brush IconBackground { get; }

    public Brush IconForeground { get; }

    public string StatusText { get; }

    public Brush PillBackground { get; }

    public Brush PillForeground { get; }

    public bool CanUndo => Entry.CountsAsSorted && System.IO.File.Exists(Entry.DestinationPath);

    public bool CanShow => Entry.Status != ActivityStatus.Failed;

    /// <summary>Short line for the Overview list, matching the mockup.</summary>
    public string Summary => Entry.Status switch
    {
        ActivityStatus.Sorted => $"Moved to {Entry.NestName} · {Format.Ago(Entry.Time)}",
        ActivityStatus.Renamed => "Renamed, a file with that name existed",
        ActivityStatus.Undone => $"Moved back · {Format.Ago(Entry.Time)}",
        _ => Entry.Error ?? "Couldn't be moved",
    };

    /// <summary>Longer line for the Activity page.</summary>
    public string Details => Entry.Status switch
    {
        ActivityStatus.Failed => $"{Entry.Error ?? "Couldn't be moved"} · {Format.Ago(Entry.Time)}",
        ActivityStatus.Undone => $"Moved back to {System.IO.Path.GetDirectoryName(Entry.DestinationPath)} · {Format.Ago(Entry.Time)}",
        ActivityStatus.Renamed => $"Moved to {Entry.NestName} as {Entry.FileName} · {Format.Size(Entry.SizeBytes)} · {Format.Ago(Entry.Time)}",
        _ => $"Moved to {Entry.NestName} · {Format.Size(Entry.SizeBytes)} · {Format.Ago(Entry.Time)}",
    };

    public string ToolTipText => Entry.Status == ActivityStatus.Failed ? Entry.SourcePath : Entry.DestinationPath;

    /// <summary>Re-reads the relative times ("2 min ago").</summary>
    public void RefreshTime()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Details));
    }
}

public sealed class DownloadItemViewModel
{
    public DownloadItemViewModel(InProgressDownload download, IReadOnlyList<NestRule> rules)
    {
        Name = download.Name;
        SizeText = $"Downloading · {Format.Size(download.SizeBytes)}";
        var rule = FileSorter.FindRule(download.Name, rules);
        Glyph = rule is null ? Icons.Download : Icons.ForKey(rule.Icon);
        IconBackground = Res.Brush("Surface1Brush");
        IconForeground = Res.Brush("TextSecondaryBrush");
        PillBackground = Res.Brush("Surface1Brush");
        PillForeground = Res.Brush("TextSecondaryBrush");
    }

    public string Name { get; }

    public string SizeText { get; }

    public string Glyph { get; }

    public Brush IconBackground { get; }

    public Brush IconForeground { get; }

    public Brush PillBackground { get; }

    public Brush PillForeground { get; }
}

public sealed record LegendItem(string Text, Brush Brush);

public sealed record AccentOption(string Name, Brush Brush, bool IsSelected);
